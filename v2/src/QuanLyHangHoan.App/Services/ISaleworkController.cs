using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Import;

namespace QuanLyHangHoan.App.Services;

public sealed class SaleworkRunResult
{
    public bool Ran { get; init; } = true;
    public bool Ok { get; init; }
    public bool Stopped { get; init; }
    public bool NeedsRelogin { get; init; }
    public string Step { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public int RowCount { get; init; }
    public string FileName { get; init; } = string.Empty;
    public SaleworkImportResult? Import { get; init; }
}

public interface ISaleworkController : IDisposable
{
    bool Configured { get; }
    bool WatchMode { get; }
    bool IsRunning { get; }
    bool IsLoginOpen { get; }
    long LastAtTicks { get; }
    bool LastOk { get; }
    string LastNote { get; }
    string LastErrorStep { get; }
    bool NeedsRelogin { get; }
    bool IsDemoMode { get; }

    event Action<SaleworkRunResult>? Completed;
    event Action? StateChanged;

    bool OpenLogin(string? startUrl, out string error);
    bool SaveCurrentPage(out string error);
    void SetWatchMode(bool enabled);
    Task<SaleworkRunResult> RunNowAsync(CancellationToken ct = default);
    void Stop();
    Task ForgetAsync();
}

/// <summary>
/// Bản giả cho chế độ --demo và test giao diện: KHÔNG tạo WebView2, KHÔNG mở mạng.
/// </summary>
public sealed class FakeSaleworkController : ISaleworkController
{
    private readonly Func<CancellationToken, Task<SaleworkRunResult>>? _runHandler;
    private readonly Func<DateTime> _clock;
    private CancellationTokenSource? _runCts;
    private string? _savedUrl;
    private string? _loginCurrentUrl;

    public FakeSaleworkController(
        Func<CancellationToken, Task<SaleworkRunResult>>? runHandler = null,
        Func<DateTime>? clock = null)
    {
        _runHandler = runHandler;
        _clock = clock ?? (() => DateTime.Now);
    }

    public bool Configured => !string.IsNullOrWhiteSpace(_savedUrl);
    public bool WatchMode { get; private set; } = true;
    public bool IsRunning { get; private set; }
    public bool IsLoginOpen { get; private set; }
    public long LastAtTicks { get; private set; }
    public bool LastOk { get; private set; } = true;
    public string LastNote { get; private set; } = string.Empty;
    public string LastErrorStep { get; private set; } = string.Empty;
    public bool NeedsRelogin { get; private set; }
    public bool IsDemoMode => true;
    public bool WebViewCreated { get; private set; }
    public int ForgetCount { get; private set; }

    public event Action<SaleworkRunResult>? Completed;
    public event Action? StateChanged;

    public void SetFakeLoginUrl(string url)
    {
        _loginCurrentUrl = url;
        IsLoginOpen = true;
        StateChanged?.Invoke();
    }

    public bool OpenLogin(string? startUrl, out string error)
    {
        var candidate = string.IsNullOrWhiteSpace(startUrl)
            ? (_savedUrl ?? "https://salework.net/don-hoan")
            : startUrl.Trim();
        if (!SaleworkPageScript.ValidateOpenUrl(candidate, out error))
        {
            return false;
        }

        _loginCurrentUrl = candidate;
        IsLoginOpen = true;
        StateChanged?.Invoke();
        return true;
    }

    public bool SaveCurrentPage(out string error)
    {
        if (!IsLoginOpen)
        {
            error = "Cửa sổ đăng nhập chưa mở";
            return false;
        }

        if (!SaleworkPageScript.ValidateSaveUrl(_loginCurrentUrl, out error))
        {
            return false;
        }

        _savedUrl = _loginCurrentUrl!.Trim();
        StateChanged?.Invoke();
        return true;
    }

    public void SetWatchMode(bool enabled)
    {
        WatchMode = enabled;
        StateChanged?.Invoke();
    }

    public async Task<SaleworkRunResult> RunNowAsync(CancellationToken ct = default)
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

        if (!Configured)
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
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        StateChanged?.Invoke();

        SaleworkRunResult result;
        try
        {
            if (_runHandler is not null)
            {
                result = await _runHandler(_runCts.Token);
            }
            else
            {
                await Task.Delay(50, _runCts.Token);
                result = new SaleworkRunResult
                {
                    Ran = true,
                    Ok = true,
                    RowCount = 8,
                    FileName = "salework_demo.xlsx",
                    Import = new SaleworkImportResult
                    {
                        Ok = true,
                        Import = new TikTokImportResult { Ok = true, Added = 3, Updated = 2 },
                        SupersededMerged = 1
                    }
                };
            }
        }
        catch (OperationCanceledException)
        {
            result = new SaleworkRunResult
            {
                Ran = true,
                Ok = false,
                Stopped = true,
                Step = "đã dừng",
                Error = "Đã dừng theo yêu cầu."
            };
        }
        finally
        {
            IsRunning = false;
            _runCts?.Dispose();
            _runCts = null;
        }

        LastAtTicks = _clock().Ticks;
        LastOk = result.Ok;
        NeedsRelogin = result.NeedsRelogin;
        LastErrorStep = result.Ok ? string.Empty : result.Step;
        LastNote = result.Ok
            ? $"Lấy {result.RowCount} dòng"
            : (string.IsNullOrWhiteSpace(result.Step) ? result.Error : $"Lỗi ở bước \"{result.Step}\": {result.Error}");

        StateChanged?.Invoke();
        Completed?.Invoke(result);
        return result;
    }

    public void Stop()
    {
        _runCts?.Cancel();
    }

    public Task ForgetAsync()
    {
        _savedUrl = null;
        _loginCurrentUrl = null;
        IsLoginOpen = false;
        NeedsRelogin = false;
        LastErrorStep = string.Empty;
        LastNote = string.Empty;
        LastAtTicks = 0;
        LastOk = true;
        ForgetCount++;
        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _runCts?.Dispose();
    }
}
