using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Data.Import;

namespace QuanLyHangHoan.Data.FolderWatch;

/// <summary>Cấu hình + trạng thái theo dõi thư mục (lưu ở tệp riêng, ghi nguyên tử).</summary>
public sealed class FolderWatchConfig
{
    public string Dir { get; set; } = "";
    public bool Enabled { get; set; }
    /// <summary>Thời điểm bật (ticks): file có giờ sửa trước mốc này không tự nạp.</summary>
    public long SinceTicks { get; set; }
    /// <summary>Khóa "tên|dung lượng|mtime ms" đã xử lý -> thời điểm (ticks); giữ tối đa 3.000 khóa mới nhất.</summary>
    public Dictionary<string, long> Seen { get; set; } = new();
    public bool LastOk { get; set; } = true;
    public string LastNote { get; set; } = "";
    public string PriceMode { get; set; } = "tong_gia_tri";
    /// <summary>"supplement" (mặc định app cũ) | "replace"</summary>
    public string ProductMode { get; set; } = "supplement";
    public bool OnlyReturns { get; set; } = true;
    public bool SkipUnshippedCancel { get; set; }
}

public sealed class FolderWatchRunResult
{
    /// <summary>false = không chạy (chưa bật, hoặc đang có lượt chạy khác).</summary>
    public bool Ran { get; init; }
    public bool Ok { get; init; } = true;
    public string Note { get; init; } = "";
    public int FilesImported { get; init; }
    public List<string> Names { get; } = new();
    public List<string> Skipped { get; } = new();
    public List<string> Failed { get; } = new();
    public TikTokImportResult? Import { get; init; }
}

/// <summary>
/// Tự nạp file TikTok từ một thư mục (app cũ v1.9.34): kiểm tra định kỳ; chỉ nạp file MỚI đã tải xong (dung lượng không đổi giữa hai lần
/// kiểm tra và file đã cũ hơn 3 giây); file không phải TikTok bị bỏ qua; KHÔNG xóa / sửa / di chuyển file nào; chỉ đọc đúng các tệp
/// .xlsx/.csv ở cấp cao nhất (bỏ tệp tạm "~$", tệp rỗng, tệp trên 60 MB). Cột thông tin cá nhân bị bỏ ngay khi đọc.
/// Lỗi ghi DB thì file KHÔNG được đánh dấu "đã thấy" nên lần sau tự thử lại.
/// </summary>
public sealed class FolderWatchService : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(15);
    private const int MaxSeen = 3000;
    private const long MaxFileBytes = 60L * 1024 * 1024;
    private static readonly Regex WatchExt = new(@"\.(xlsx|xls|csv)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly string _dbPath, _statePath;
    private readonly Func<DateTime> _clock;
    private readonly TimeSpan _interval;
    private readonly QuanLyHangHoan.Data.Cloud.CloudImportSink? _cloud;
    private readonly object _lock = new();
    private readonly Dictionary<string, long> _pendingSize = new();
    private CancellationTokenSource? _cts;

    public FolderWatchConfig Config { get; private set; }
    public event Action<FolderWatchRunResult>? Completed;

    /// <param name="cloud">Chế độ cloud: gửi kiện lên cloud gộp (không gộp vào máy).</param>
    public FolderWatchService(string dbPath, string statePath, Func<DateTime> clock, TimeSpan? interval = null, QuanLyHangHoan.Data.Cloud.CloudImportSink? cloud = null)
    {
        _dbPath = dbPath; _statePath = statePath; _clock = clock; _interval = interval ?? DefaultInterval; _cloud = cloud;
        Config = LoadConfig(statePath);
    }

    // ------------------------------------------------------------ cấu hình
    private static FolderWatchConfig LoadConfig(string path)
    {
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<FolderWatchConfig>(File.ReadAllText(path)) ?? new FolderWatchConfig();
        }
        catch (JsonException)
        {
            try { File.Move(path, $"{path}.corrupt-{DateTime.Now:yyyyMMddHHmmssfff}"); } catch (IOException) { /* giữ nguyên */ }
        }
        return new FolderWatchConfig();
    }

    public void SaveConfig()
    {
        lock (_lock)
        {
            if (Config.Seen.Count > MaxSeen)
                Config.Seen = Config.Seen.OrderByDescending(kv => kv.Value).Take(MaxSeen).ToDictionary(kv => kv.Key, kv => kv.Value);
            var dir = Path.GetDirectoryName(Path.GetFullPath(_statePath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _statePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _statePath, overwrite: true);
        }
    }

    /// <summary>Chọn thư mục và bật/tắt. Bật mới (hoặc đổi thư mục) thì đặt mốc "từ bây giờ": file có sẵn không tự nạp.</summary>
    public void Configure(string dir, bool enabled)
    {
        lock (_lock)
        {
            var d = (dir ?? "").Trim().Trim('"');
            var changed = !string.Equals(d, Config.Dir, StringComparison.OrdinalIgnoreCase);
            if (enabled && (!Config.Enabled || changed)) Config.SinceTicks = _clock().Ticks;
            Config.Dir = d;
            Config.Enabled = enabled && d.Length > 0;
            _pendingSize.Clear();
            SaveConfig();
        }
    }

    // ------------------------------------------------------------ chạy một lượt
    private sealed record Candidate(string Name, string FullPath, long Size, long MtimeMs, DateTime Mtime);

    public FolderWatchRunResult RunOnce(bool includeExisting = false)
    {
        if (!Monitor.TryEnter(_lock)) return new FolderWatchRunResult { Ran = false };
        FolderWatchRunResult result;
        try { result = RunLocked(includeExisting); }
        finally { Monitor.Exit(_lock); }
        if (result.Ran) { try { Completed?.Invoke(result); } catch (Exception) { /* người nghe lỗi không được làm hỏng dịch vụ */ } }
        return result;
    }

    private FolderWatchRunResult RunLocked(bool includeExisting)
    {
        if (!Config.Enabled || Config.Dir.Length == 0) return new FolderWatchRunResult { Ran = false };

        if (!Directory.Exists(Config.Dir)) return Finish(false, "Không tìm thấy thư mục: " + Config.Dir);

        var now = _clock();
        var files = new List<Candidate>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(Config.Dir))
            {
                var name = Path.GetFileName(path);
                if (!WatchExt.IsMatch(name) || name.StartsWith("~$", StringComparison.Ordinal)) continue;
                FileInfo fi;
                try { fi = new FileInfo(path); } catch (IOException) { continue; }
                if (!fi.Exists || fi.Length == 0 || fi.Length > MaxFileBytes) continue;
                files.Add(new Candidate(name, path, fi.Length, new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeMilliseconds(), fi.LastWriteTime));
            }
        }
        catch (Exception ex) { return Finish(false, "Không đọc được thư mục: " + ex.Message); }

        var fresh = new List<Candidate>();
        foreach (var f in files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var key = $"{f.Name}|{f.Size}|{f.MtimeMs}";
            if (Config.Seen.ContainsKey(key)) continue;
            // có sẵn trước lúc bật: không tự nạp (không đánh dấu "đã thấy", để nút "Nạp các file đang có" vẫn dùng được)
            if (!includeExisting && Config.SinceTicks > 0 && f.Mtime < new DateTime(Config.SinceTicks)) continue;
            // chờ tải xong: dung lượng giữ nguyên qua hai lần kiểm tra và file đã cũ hơn 3 giây
            _pendingSize.TryGetValue(f.Name, out var prev);
            var known = _pendingSize.ContainsKey(f.Name);
            _pendingSize[f.Name] = f.Size;
            if (!includeExisting && (!known || prev != f.Size || (now - f.Mtime).TotalSeconds < 3)) continue;
            fresh.Add(f);
        }
        if (fresh.Count == 0) { SaveConfig(); return new FolderWatchRunResult { Ran = true, Ok = Config.LastOk, Note = Config.LastNote }; }

        var parsed = new List<(Candidate File, List<Dictionary<string, string>> Rows)>();
        var skipped = new List<string>();
        var failed = new List<string>();
        foreach (var f in fresh)
        {
            var key = $"{f.Name}|{f.Size}|{f.MtimeMs}";
            try
            {
                byte[] bytes;
                using (var fs = new FileStream(f.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var ms = new MemoryStream()) { fs.CopyTo(ms); bytes = ms.ToArray(); }
                var rows = SpreadsheetReader.DropPersonalColumns(SpreadsheetReader.Read(f.Name, bytes));
                if (rows.Count == 0 || !TikTokMapper.IsTikTokExport(rows)) { skipped.Add(f.Name); Config.Seen[key] = now.Ticks; _pendingSize.Remove(f.Name); continue; }
                parsed.Add((f, rows));
            }
            catch (SpreadsheetFormatException ex) when (ex.NotAnOrderTable)
            {
                // Bảng tính đọc được nhưng không có bảng đơn hàng (file không liên quan trong thư mục chung): bỏ qua, không báo lỗi.
                skipped.Add(f.Name);
                Config.Seen[key] = now.Ticks;
                _pendingSize.Remove(f.Name);
            }
            catch (Exception ex)
            {
                failed.Add($"{f.Name} ({ex.Message})");
                Config.Seen[key] = now.Ticks;     // file lỗi không bị thử lại mãi
                _pendingSize.Remove(f.Name);
            }
        }
        SaveConfig();

        if (parsed.Count == 0)
        {
            var quiet = new FolderWatchRunResult { Ran = true, Ok = Config.LastOk, Note = Config.LastNote };
            quiet.Skipped.AddRange(skipped);
            return failed.Count > 0 ? Finish(false, "Không đọc được: " + string.Join(", ", failed), skipped, failed) : quiet;
        }

        var prep = PlatformImportPrep.Prepare(parsed.Select(p => new SourceFile(p.File.Name, p.Rows)).ToList());
        var options = new TikTokMapOptions
        {
            PriceMode = Config.PriceMode, OnlyReturns = Config.OnlyReturns, SkipUnshippedCancel = Config.SkipUnshippedCancel, Today = now,
        };
        var mode = Config.ProductMode == "replace" ? ImportProductMode.Replace : ImportProductMode.Supplement;
        var import = _cloud is not null
            ? _cloud.Apply(prep.Groups.Select(g => (IReadOnlyList<Dictionary<string, string>>)g).ToList(), options, mode, ImportSource.TikTok, "folder").Import
            : TikTokImportService.ApplyGroups(_dbPath, prep.Groups, options, mode, now);

        var names = parsed.Select(p => p.File.Name).ToList();
        if (!import.Ok)
            return Finish(false, $"Không nạp được {string.Join(", ", names)}: {import.Message}", skipped, failed, import);   // KHÔNG đánh dấu đã thấy: lần sau thử lại

        foreach (var p in parsed)
        {
            Config.Seen[$"{p.File.Name}|{p.File.Size}|{p.File.MtimeMs}"] = now.Ticks;
            _pendingSize.Remove(p.File.Name);
        }
        var summary = $"thêm {import.Added}, cập nhật {import.Updated}" +
                      (import.EnrichedArchived > 0 ? $", bổ sung kho lưu trữ {import.EnrichedArchived}" : "") +
                      (import.SkippedDeleted > 0 ? $", bỏ qua {import.SkippedDeleted} đơn đã xóa" : "") +
                      (import.Ambiguous.Count > 0 ? $", {import.Ambiguous.Count} dòng nhập nhằng cần kiểm tra" : "") +
                      (prep.DuplicateCount > 0 ? $", bỏ {prep.DuplicateCount} dòng trùng giữa các file" : "");
        try
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
            conn.Open();
            using var tx = conn.BeginTransaction();
            OrderStore.AppendAudit(conn, tx, "folder-watch", now, $"TỰ NẠP FILE TIKTOK: {string.Join(", ", names)} ({summary})");
            tx.Commit();
        }
        catch (Exception) { /* nhật ký là phụ: không làm hỏng lượt nạp đã thành công */ }

        return Finish(true, $"Đã nạp {names.Count} file TikTok ({string.Join(", ", names)}): {summary}", skipped, failed, import, names);
    }

    private FolderWatchRunResult Finish(bool ok, string note, List<string>? skipped = null, List<string>? failed = null,
        TikTokImportResult? import = null, List<string>? names = null)
    {
        Config.LastOk = ok;
        Config.LastNote = $"{_clock():HH:mm dd/MM}: {note}";
        SaveConfig();
        var r = new FolderWatchRunResult { Ran = true, Ok = ok, Note = Config.LastNote, FilesImported = ok ? names?.Count ?? 0 : 0, Import = import };
        if (names is not null) r.Names.AddRange(names);
        if (skipped is not null) r.Skipped.AddRange(skipped);
        if (failed is not null) r.Failed.AddRange(failed);
        return r;
    }

    // ------------------------------------------------------------ chạy nền
    /// <summary>Kiểm tra định kỳ (mặc định 15 giây). Lần đầu sau <paramref name="firstDelay"/> (mặc định 20 giây như app cũ).</summary>
    public void Start(TimeSpan? firstDelay = null)
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var delay = firstDelay ?? TimeSpan.FromSeconds(20);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, token);
                using var timer = new PeriodicTimer(_interval);
                do { try { RunOnce(); } catch (Exception) { /* một lượt lỗi không được dừng dịch vụ */ } }
                while (await timer.WaitForNextTickAsync(token));
            }
            catch (OperationCanceledException) { /* dừng bình thường */ }
        });
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
