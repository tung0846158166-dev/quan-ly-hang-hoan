using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Import;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Điều phối lấy đơn Salework tự động bằng WebView2 (hồ sơ riêng, không đụng trình duyệt của người dùng).
/// </summary>
public sealed class RealSaleworkController : ISaleworkController
{
    private sealed class PersistedState
    {
        public string? Url { get; set; }
        public bool Watch { get; set; } = true;
        public bool LastOk { get; set; } = true;
        public string LastNote { get; set; } = string.Empty;
        public long LastAtTicks { get; set; }
        public string LastErrorStep { get; set; } = string.Empty;
        public bool NeedsRelogin { get; set; }
    }

    private sealed class ReloginRequiredException(string message) : Exception(message);

    private const int PageTimeoutMs = 45_000;
    private const int InitialWaitMs = 25_000;
    private const int StepPauseMs = 2_500;
    private const int WatchPauseMs = 2_500;
    private const int ActWaitMs = 8_000;
    private const int ClickRetryMs = 6_000;
    private const int DownloadTimeoutMs = 120_000;
    private const int KeepOnErrorMs = 120_000;

    private readonly string _dbPath;
    private readonly string _statePath;
    private readonly string _webViewProfileDir;
    private readonly Func<DateTime> _clock;
    private readonly object _stateLock = new();

    private PersistedState _state;
    private SaleworkBrowserWindow? _loginWin;
    private SaleworkBrowserWindow? _runWin;
    private CancellationTokenSource? _runCts;
    private volatile bool _aborted;
    private bool _disposed;

    public RealSaleworkController(
        string dbPath,
        string statePath,
        string webViewProfileDir,
        Func<DateTime>? clock = null,
        QuanLyHangHoan.Data.Cloud.CloudImportSink? cloud = null)
    {
        _cloud = cloud;
        _dbPath = dbPath;
        _statePath = statePath;
        _webViewProfileDir = webViewProfileDir;
        _clock = clock ?? (() => DateTime.Now);
        _state = LoadState();
    }

    public bool Configured => !string.IsNullOrWhiteSpace(_state.Url);
    public bool WatchMode => _state.Watch;
    public bool IsRunning { get; private set; }
    public bool IsLoginOpen => _loginWin is not null;
    public long LastAtTicks => _state.LastAtTicks;
    public bool LastOk => _state.LastOk;
    public string LastNote => _state.LastNote;
    public string LastErrorStep => _state.LastErrorStep;
    public bool NeedsRelogin => _state.NeedsRelogin;
    public bool IsDemoMode => false;

    public event Action<SaleworkRunResult>? Completed;
    public event Action? StateChanged;

    /// <summary>Trang đăng nhập Salework mặc định của cửa hàng (chủ dự án 26/11/2026).</summary>
    public const string DefaultLoginUrl = "https://salework.net/login/lavistudio";
    /// <summary>Chế độ cloud: nạp Salework qua cloud (cùng đường với điện thoại).</summary>
    private readonly QuanLyHangHoan.Data.Cloud.CloudImportSink? _cloud;

    public bool OpenLogin(string? startUrl, out string error)
    {
        // nút "Đăng nhập": luôn mở trang đăng nhập của cửa hàng (đã đăng nhập thì Salework tự chuyển vào trong)
        var candidate = string.IsNullOrWhiteSpace(startUrl) ? DefaultLoginUrl : startUrl.Trim();
        if (!SaleworkPageScript.ValidateOpenUrl(candidate, out error))
        {
            return false;
        }

        RunOnUiSync(() =>
        {
            if (_loginWin is not null)
            {
                if (!string.IsNullOrWhiteSpace(startUrl)
                    && !string.Equals(_loginWin.CurrentUrl, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    _loginWin.Navigate(candidate);
                }
                _loginWin.Activate();
                return;
            }

            var win = new SaleworkBrowserWindow(
                "Salework — đăng nhập rồi mở trang Đơn hoàn",
                1280,
                860,
                visibleToUser: true);
            _loginWin = win;
            win.Closed += (_, _) =>
            {
                if (ReferenceEquals(_loginWin, win))
                {
                    _loginWin = null;
                    StateChanged?.Invoke();
                }
            };
            win.Show();
            _ = InitAndNavigateAsync(win, candidate);
        });

        StateChanged?.Invoke();
        return true;
    }

    private async Task InitAndNavigateAsync(SaleworkBrowserWindow win, string url)
    {
        try
        {
            await win.InitializeAsync(_webViewProfileDir);
            win.Navigate(url);
        }
        catch
        {
            // Bỏ qua nếu người dùng đóng cửa sổ trước khi khởi tạo xong
        }
    }

    public bool SaveCurrentPage(out string error)
    {
        string currentUrl = string.Empty;
        var hasWin = false;
        RunOnUiSync(() =>
        {
            if (_loginWin is not null)
            {
                hasWin = true;
                currentUrl = _loginWin.CurrentUrl;
            }
        });

        if (!hasWin)
        {
            error = "Cửa sổ đăng nhập chưa mở";
            return false;
        }

        if (!SaleworkPageScript.ValidateSaveUrl(currentUrl, out error))
        {
            return false;
        }

        lock (_stateLock)
        {
            _state.Url = currentUrl.Trim();
            _state.NeedsRelogin = false;
            SaveState(_state);
        }

        StateChanged?.Invoke();
        return true;
    }

    public void SetWatchMode(bool enabled)
    {
        lock (_stateLock)
        {
            _state.Watch = enabled;
            SaveState(_state);
        }
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        _aborted = true;
        _runCts?.Cancel();
        RunOnUiAsync(() =>
        {
            try { _runWin?.Close(); } catch { /* bỏ qua */ }
        });
    }

    public async Task ForgetAsync()
    {
        Stop();
        RunOnUiSync(() =>
        {
            try { _loginWin?.Close(); } catch { /* bỏ qua */ }
            _loginWin = null;
        });

        lock (_stateLock)
        {
            _state.Url = null;
            _state.NeedsRelogin = false;
            _state.LastErrorStep = string.Empty;
            _state.LastNote = string.Empty;
            _state.LastAtTicks = 0;
            _state.LastOk = true;
            SaveState(_state);
        }

        await Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(_webViewProfileDir))
                {
                    Directory.Delete(_webViewProfileDir, recursive: true);
                }
            }
            catch
            {
                // Thư mục WebView2 có thể vừa mới giải phóng tiến trình con
            }
        });

        StateChanged?.Invoke();
    }

    public Task<SaleworkRunResult> RunNowAsync(CancellationToken ct = default)
    {
        var dispatcher = Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("Không có WPF Dispatcher để chạy WebView2.");

        if (dispatcher.CheckAccess())
        {
            return RunCoreAsync(ct);
        }

        return dispatcher.InvokeAsync(() => RunCoreAsync(ct)).Task.Unwrap();
    }

    private async Task<SaleworkRunResult> RunCoreAsync(CancellationToken ct)
    {
        if (IsRunning)
        {
            return new SaleworkRunResult
            {
                Ran = false,
                Ok = false,
                Step = "busy",
                Error = "Đang chạy lượt trước."
            };
        }

        var url = _state.Url;
        if (string.IsNullOrWhiteSpace(url))
        {
            return new SaleworkRunResult
            {
                Ran = false,
                Ok = false,
                Step = "config",
                Error = "Chưa cài đặt: bấm \"Đăng nhập Salework\" và lưu trang Đơn hoàn."
            };
        }

        IsRunning = true;
        _aborted = false;
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var linkedCt = _runCts.Token;
        StateChanged?.Invoke();

        var watch = _state.Watch;
        SaleworkBrowserWindow? win = null;
        string? tmpDir = null;
        var step = "mở trang";
        var keepOpenOnError = false;
        SaleworkRunResult result;

        try
        {
            win = new SaleworkBrowserWindow(
                "Salework — app đang thao tác (đóng cửa sổ này để dừng)",
                1366,
                900,
                visibleToUser: watch);
            _runWin = win;
            win.Closed += (_, _) =>
            {
                if (ReferenceEquals(_runWin, win))
                {
                    _aborted = true;
                    _runCts?.Cancel();
                }
            };
            win.Show();
            await win.InitializeAsync(_webViewProfileDir);

            tmpDir = Path.Combine(Path.GetTempPath(), "qlhh-sw-auto-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);

            step = "mở trang";
            win.Navigate(url);

            JsonElement st;
            try
            {
                st = await WaitForStateAsync(
                    win,
                    s => GetBool(s, "hasExport") || GetBool(s, "hasPassword"),
                    InitialWaitMs,
                    $"Trang Salework không hiện nút \"Xuất excel\" sau {InitialWaitMs / 1000}s",
                    linkedCt);
            }
            catch (Exception ex0) when (!_aborted && !linkedCt.IsCancellationRequested && ex0.Message.Contains("Xuất excel", StringComparison.OrdinalIgnoreCase))
            {
                step = "vào Đơn hoàn bằng menu";
                var direct = true;
                try
                {
                    await ActAsync(win, new { text = "Đơn hoàn" }, "vào \"Đơn hoàn\"", watch, 1200, linkedCt);
                }
                catch when (!_aborted && !linkedCt.IsCancellationRequested)
                {
                    direct = false;
                }

                if (!direct)
                {
                    await ActAsync(win, new { text = "Kiểm tra, đối soát" }, "mở menu \"Kiểm tra, đối soát\"", watch, ActWaitMs, linkedCt);
                    await Task.Delay(StepPauseMs, linkedCt);
                    await ActAsync(win, new { text = "Đơn hoàn" }, "vào \"Đơn hoàn\"", watch, ActWaitMs, linkedCt);
                }

                st = await WaitForStateAsync(
                    win,
                    s => GetBool(s, "hasExport") || GetBool(s, "hasPassword"),
                    PageTimeoutMs,
                    $"Đã vào Đơn hoàn bằng menu nhưng vẫn không thấy nút \"Xuất excel\" sau {PageTimeoutMs / 1000}s",
                    linkedCt);
            }

            if (GetBool(st, "hasPassword") && !GetBool(st, "hasExport"))
            {
                throw new ReloginRequiredException("Salework yêu cầu đăng nhập lại.");
            }

            step = "chọn tab Đang chờ hoàn";
            await ActAsync(win, new { text = "Đang chờ hoàn", mode = "prefix" }, "chọn tab \"Đang chờ hoàn\"", watch, ActWaitMs, linkedCt);
            await Task.Delay(StepPauseMs, linkedCt);

            step = "kiểm tra kênh Tiktok";
            var s2 = await win.ExecutePageScriptAsync("state");
            if (!GetBool(s2, "channelTiktok"))
            {
                throw new InvalidOperationException(
                    "Không thấy kênh Tiktok trên trang — mở cửa sổ đăng nhập, chọn kênh Tiktok rồi lưu lại trang." + FormatDiag(s2));
            }

            step = "đưa chuột vào Xuất excel";
            await ActAsync(win, new { text = "Xuất excel", hover = true }, "đưa chuột vào \"Xuất excel\"", watch, ActWaitMs, linkedCt);
            await Task.Delay(StepPauseMs, linkedCt);

            step = "chọn \"Theo bộ lọc\"";
            var seen = await win.ExecutePageScriptAsync("exists", new { text = "Theo bộ lọc", mode = "prefix", inDialog = true });
            for (var k = 0; k < 3 && !GetBool(seen, "ok"); k++)
            {
                ThrowIfAborted(linkedCt);
                step = $"rê lại chuột vào Xuất excel (lần {k + 2})";
                await Task.Delay(1200 + k * 800, linkedCt);
                seen = await win.ExecutePageScriptAsync("exists", new { text = "Theo bộ lọc", mode = "prefix", inDialog = true });
                if (GetBool(seen, "ok")) break;
                await ActAsync(win, new { text = "Xuất excel", hover = true }, "rê lại chuột vào \"Xuất excel\"", false, ActWaitMs, linkedCt);
                await Task.Delay(1500, linkedCt);
                seen = await win.ExecutePageScriptAsync("exists", new { text = "Theo bộ lọc", mode = "prefix", inDialog = true });
            }

            if (!GetBool(seen, "ok"))
            {
                step = "bấm Xuất excel";
                await ActAsync(win, new { text = "Xuất excel" }, "bấm \"Xuất excel\"", watch, ActWaitMs, linkedCt);
                await Task.Delay(StepPauseMs, linkedCt);
                step = "chọn \"Theo bộ lọc\"";
            }

            await ActAsync(win, new { text = "Theo bộ lọc", mode = "prefix", inDialog = true }, "chọn \"Theo bộ lọc\"", watch, ActWaitMs, linkedCt);
            await Task.Delay(600, linkedCt);

            step = "bấm Bắt Đầu Xuất";
            var downloadTask = ArmDownloadAsync(win.Core!, tmpDir, linkedCt);
            await ActAsync(win, new { text = "Bắt Đầu Xuất", mode = "prefix", inDialog = true }, "bấm \"Bắt Đầu Xuất\"", watch, ActWaitMs, linkedCt);

            step = "chờ file tải về";
            var (downloadedPath, fileName) = await downloadTask;

            step = "đọc file và nạp vào bảng";
            var now = _clock();
            var (rowCount, importResult) = await Task.Run(() =>
            {
                // an toàn dữ liệu: sao lưu trước khi nạp (dùng lại bản < 30 phút nếu có); sao lưu hỏng thì không nạp
                if (_cloud is null && File.Exists(_dbPath))
                {
                    var bk = BackupService.EnsureRecent(_dbPath, BackupService.DefaultDir(_dbPath), TimeSpan.FromMinutes(30), 7, now);
                    if (!bk.Ok) throw new IOException("Không nạp vì chưa sao lưu được dữ liệu hiện tại: " + bk.Message);
                }
                var bytes = File.ReadAllBytes(downloadedPath);
                var rows = SpreadsheetReader.Read(fileName, bytes);
                var applied = SaleworkImportService.Apply(
                    _dbPath,
                    rows,
                    new TikTokMapOptions { Today = now },
                    ImportProductMode.Supplement,
                    now,
                    _cloud);
                return (rows.Count, applied);
            }, linkedCt);

            if (!importResult.Ok)
            {
                throw new InvalidOperationException(importResult.Message);
            }

            result = new SaleworkRunResult
            {
                Ran = true,
                Ok = true,
                RowCount = rowCount,
                FileName = fileName,
                Import = importResult
            };
            SaveRunOutcome(ok: true, note: $"Lấy {rowCount} dòng", errorStep: string.Empty, needsRelogin: false);
        }
        catch (Exception ex)
        {
            var stopped = _aborted || ex is OperationCanceledException;
            var needsRelogin = ex is ReloginRequiredException;
            var msg = stopped && !ex.Message.Contains("dừng", StringComparison.OrdinalIgnoreCase)
                ? "Đã dừng (cửa sổ bị đóng) — " + ex.Message
                : ex.Message;

            if (watch && !stopped && win is not null && win.IsVisible)
            {
                keepOpenOnError = true;
                try
                {
                    await win.ExecutePageScriptAsync("banner", new
                    {
                        text = $"⚠️ App dừng ở bước \"{step}\" — bạn có thể xem trang này, rồi đóng cửa sổ"
                    });
                }
                catch { /* bỏ qua */ }

                var w = win;
                _ = Task.Delay(KeepOnErrorMs).ContinueWith(_ =>
                {
                    try { w.Dispatcher.BeginInvoke(() => { try { w.Close(); } catch { } }); } catch { }
                });
            }

            SaveRunOutcome(
                ok: false,
                note: $"Lỗi ở bước \"{step}\": {msg}",
                errorStep: step,
                needsRelogin: needsRelogin);

            result = new SaleworkRunResult
            {
                Ran = true,
                Ok = false,
                Stopped = stopped,
                NeedsRelogin = needsRelogin,
                Step = step,
                Error = msg
            };
        }
        finally
        {
            IsRunning = false;
            if (!keepOpenOnError)
            {
                _runWin = null;
                try { win?.Close(); } catch { /* bỏ qua */ }
            }
            _runCts?.Dispose();
            _runCts = null;

            if (tmpDir is not null)
            {
                try { Directory.Delete(tmpDir, recursive: true); } catch { /* bỏ qua */ }
            }
        }

        StateChanged?.Invoke();
        Completed?.Invoke(result);
        return result;
    }

    private async Task<(string Path, string FileName)> ArmDownloadAsync(CoreWebView2 core, string tmpDir, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<(string, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(DownloadTimeoutMs);
        using var reg = timeoutCts.Token.Register(() =>
        {
            if (ct.IsCancellationRequested)
            {
                tcs.TrySetCanceled(ct);
            }
            else
            {
                tcs.TrySetException(new TimeoutException($"Không thấy file tải về sau {DownloadTimeoutMs / 1000}s"));
            }
        });

        void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs args)
        {
            core.DownloadStarting -= OnDownloadStarting;
            var rawName = Path.GetFileName(args.ResultFilePath);
            if (string.IsNullOrWhiteSpace(rawName)) rawName = "don_hoan.xlsx";
            var safeName = Regex.Replace(rawName, @"[^\w.\-]+", "_");
            var destPath = Path.Combine(tmpDir, safeName);
            args.ResultFilePath = destPath;
            args.Handled = true;

            var op = args.DownloadOperation;
            op.StateChanged += (_, _) =>
            {
                if (op.State == CoreWebView2DownloadState.Completed)
                {
                    tcs.TrySetResult((destPath, safeName));
                }
                else if (op.State == CoreWebView2DownloadState.Interrupted)
                {
                    tcs.TrySetException(new InvalidOperationException($"Tải file không hoàn tất ({op.InterruptReason})"));
                }
            };
        }

        core.DownloadStarting += OnDownloadStarting;
        try
        {
            return await tcs.Task;
        }
        catch
        {
            core.DownloadStarting -= OnDownloadStarting;
            throw;
        }
    }

    private async Task<JsonElement> WaitForStateAsync(
        SaleworkBrowserWindow win,
        Func<JsonElement, bool> predicate,
        int timeoutMs,
        string timeoutLabel,
        CancellationToken ct)
    {
        var start = Environment.TickCount64;
        JsonElement? last = null;
        while (Environment.TickCount64 - start < timeoutMs)
        {
            ThrowIfAborted(ct);
            try
            {
                var cur = await win.ExecutePageScriptAsync("state");
                last = cur;
                if (predicate(cur)) return cur;
            }
            catch
            {
                // Trang đang chuyển hướng
            }
            await Task.Delay(700, ct);
        }

        throw new TimeoutException(timeoutLabel + (last.HasValue ? FormatDiag(last.Value) : string.Empty));
    }

    private async Task ActAsync(
        SaleworkBrowserWindow win,
        object arg,
        string label,
        bool watch,
        int waitMs,
        CancellationToken ct)
    {
        ThrowIfAborted(ct);
        var start = Environment.TickCount64;
        var ex = await win.ExecutePageScriptAsync("exists", arg);
        while (!GetBool(ex, "ok") && Environment.TickCount64 - start < waitMs)
        {
            ThrowIfAborted(ct);
            await Task.Delay(400, ct);
            ex = await win.ExecutePageScriptAsync("exists", arg);
        }

        if (!GetBool(ex, "ok"))
        {
            var more = await TryGetPageTextsAsync(win);
            throw new InvalidOperationException(GetString(ex, "error", "Không thấy phần tử") + more);
        }

        if (watch)
        {
            try
            {
                await win.ExecutePageScriptAsync("banner", new
                {
                    text = $"🤖 App đang: {label} — đóng cửa sổ này hoặc bấm \"Dừng\" trong app để DỪNG NGAY"
                });
            }
            catch { /* bỏ qua */ }

            var m = await win.ExecutePageScriptAsync("mark", arg);
            if (!GetBool(m, "ok"))
            {
                throw new InvalidOperationException(GetString(m, "error", "Không đánh dấu được phần tử"));
            }

            await Task.Delay(WatchPauseMs, ct);
            ThrowIfAborted(ct);
        }

        var isHover = arg.GetType().GetProperty("hover")?.GetValue(arg) is true;
        if (isHover)
        {
            if (!await TryRealMouseAsync(win, arg, isMoveOnly: true, ct))
            {
                var h = await win.ExecutePageScriptAsync("hover", arg);
                if (!GetBool(h, "ok"))
                {
                    throw new InvalidOperationException(GetString(h, "error", "Không rê chuột được"));
                }
            }
            return;
        }

        if (await TryRealMouseAsync(win, arg, isMoveOnly: false, ct))
        {
            return;
        }

        var r = await win.ExecutePageScriptAsync("click", arg);
        var t1 = Environment.TickCount64;
        while (!GetBool(r, "ok") && Environment.TickCount64 - t1 < ClickRetryMs)
        {
            ThrowIfAborted(ct);
            await Task.Delay(400, ct);
            r = await win.ExecutePageScriptAsync("click", arg);
        }

        if (!GetBool(r, "ok"))
        {
            var more = await TryGetPageTextsAsync(win);
            throw new InvalidOperationException(GetString(r, "error", "Không bấm được phần tử") + more);
        }
    }

    private async Task<bool> TryRealMouseAsync(SaleworkBrowserWindow win, object arg, bool isMoveOnly, CancellationToken ct)
    {
        var rect = await win.ExecutePageScriptAsync("rect", arg);
        if (!GetBool(rect, "ok")) return false;
        if (!rect.TryGetProperty("x", out var xProp) || !rect.TryGetProperty("y", out var yProp)) return false;

        var x = xProp.GetInt32();
        var y = yProp.GetInt32();
        var topOk = GetBool(rect, "topOk");
        if (!topOk)
        {
            var btnText = arg.GetType().GetProperty("text")?.GetValue(arg)?.ToString() ?? "?";
            var topText = GetString(rect, "topText", "?");
            throw new InvalidOperationException($"Nút \"{btnText}\" đang bị che bởi \"{topText}\" — không bấm để tránh bấm nhầm");
        }

        if (isMoveOnly)
        {
            await win.DispatchMouseEventAsync("mouseMoved", Math.Max(1, x - 80), y + 60);
            await Task.Delay(120, ct);
            await win.DispatchMouseEventAsync("mouseMoved", Math.Max(1, x - 30), y + 20);
            await Task.Delay(120, ct);
        }

        await win.DispatchMouseEventAsync("mouseMoved", x, y);
        await Task.Delay(250, ct);
        if (!isMoveOnly)
        {
            await win.DispatchMouseEventAsync("mousePressed", x, y, "left", 1);
            await Task.Delay(80, ct);
            await win.DispatchMouseEventAsync("mouseReleased", x, y, "left", 1);
        }
        return true;
    }

    private static async Task<string> TryGetPageTextsAsync(SaleworkBrowserWindow win)
    {
        try
        {
            var tx = await win.ExecutePageScriptAsync("texts");
            if (tx.TryGetProperty("texts", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var items = arr.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrEmpty(s));
                return $" — trên trang đang thấy: [{string.Join(" | ", items)}]";
            }
        }
        catch { /* bỏ qua */ }
        return string.Empty;
    }

    private static string FormatDiag(JsonElement st)
    {
        var path = GetString(st, "path", "?");
        var title = GetString(st, "title", "");
        if (title.Length > 60) title = title[..60];
        var buttons = st.TryGetProperty("buttons", out var b) && b.ValueKind == JsonValueKind.Array
            ? string.Join(" | ", b.EnumerateArray().Select(x => x.GetString()).Where(s => !string.IsNullOrEmpty(s)))
            : string.Empty;
        return $" — trang đang ở: {path}, tiêu đề \"{title}\", thấy các nút: [{buttons}]";
    }

    private void ThrowIfAborted(CancellationToken ct)
    {
        if (_aborted || ct.IsCancellationRequested)
        {
            throw new OperationCanceledException("Đã dừng theo yêu cầu.");
        }
    }

    private void SaveRunOutcome(bool ok, string note, string errorStep, bool needsRelogin)
    {
        lock (_stateLock)
        {
            _state.LastAtTicks = _clock().Ticks;
            _state.LastOk = ok;
            _state.LastNote = note;
            _state.LastErrorStep = errorStep;
            _state.NeedsRelogin = needsRelogin;
            SaveState(_state);
        }
    }

    private PersistedState LoadState()
    {
        try
        {
            if (File.Exists(_statePath))
            {
                var json = File.ReadAllText(_statePath);
                return JsonSerializer.Deserialize<PersistedState>(json) ?? new PersistedState();
            }
        }
        catch
        {
            // Tệp trạng thái hỏng -> dùng mặc định
        }
        return new PersistedState();
    }

    private void SaveState(PersistedState state)
    {
        try
        {
            var dir = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_statePath, JsonSerializer.Serialize(state));
        }
        catch
        {
            // Bỏ qua lỗi ghi trạng thái phụ
        }
    }

    private static bool GetBool(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty(prop, out var v)
        && v.ValueKind == JsonValueKind.True;

    private static string GetString(JsonElement el, string prop, string fallback) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty(prop, out var v)
        && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? fallback)
            : fallback;

    private static void RunOnUiSync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    private static void RunOnUiAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        RunOnUiAsync(() =>
        {
            try { _loginWin?.Close(); } catch { }
            try { _runWin?.Close(); } catch { }
        });
    }
}
