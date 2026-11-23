using System.Diagnostics;
using System.IO;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

public sealed record BackupInfo(string Name, DateTime Time, long Bytes);

public sealed record BackupOutcome(bool Ok, string Message);

/// <summary>
/// Sao lưu cơ sở dữ liệu cho giao diện: sao lưu ngay, danh sách bản sao lưu, mở thư mục, và LỊCH TỰ SAO LƯU chạy nền
/// (bản thật gọi <see cref="BackupService.RunIfDue"/> sau ~1 phút khi mở app rồi mỗi 10 phút; bản giả/demo không chạm đĩa).
/// </summary>
public interface IBackupController : IDisposable
{
    string FolderPath { get; }
    IReadOnlyList<BackupInfo> List();
    Task<BackupOutcome> BackupNowAsync(CancellationToken cancellationToken = default);
    /// <summary>Đặt lịch tự sao lưu. enabled = false thì dừng. Gọi lại khi người dùng đổi cài đặt.</summary>
    void ApplySchedule(bool enabled, int everyHours, int keepDays);
    void OpenFolder();
    /// <summary>Có bản sao lưu mới (tự động hoặc bằng tay): giao diện làm mới danh sách.</summary>
    event Action? Changed;
}

public sealed class SqliteBackupController : IBackupController
{
    private readonly string _dbPath;
    private readonly string _dir;
    private readonly Func<DateTime> _clock;
    private readonly Action<string> _openFolder;
    private readonly TimeSpan _firstDelay;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private Timer? _timer;
    private bool _enabled;
    private int _every = 2, _keep = 7;
    private int _running;

    public SqliteBackupController(string dbPath, Func<DateTime>? clock = null, Action<string>? openFolder = null,
        TimeSpan? firstDelay = null, TimeSpan? interval = null, string? folder = null)
    {
        _dbPath = dbPath;
        _dir = folder ?? BackupService.DefaultDir(dbPath);
        _clock = clock ?? (() => DateTime.Now);
        _openFolder = openFolder ?? DefaultOpen;
        _firstDelay = firstDelay ?? TimeSpan.FromMinutes(1);
        _interval = interval ?? TimeSpan.FromMinutes(10);
    }

    public string FolderPath => _dir;
    public event Action? Changed;

    public IReadOnlyList<BackupInfo> List() =>
        BackupService.List(_dir).Select(e => new BackupInfo(e.Name, e.Time, e.Bytes)).ToList();

    public Task<BackupOutcome> BackupNowAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var r = BackupService.Backup(_dbPath, _dir, _clock());
        if (r.Ok)
        {
            int keep; lock (_gate) keep = _keep;
            BackupService.Prune(_dir, keep, _clock());
            Changed?.Invoke();
        }
        return new BackupOutcome(r.Ok, r.Message);
    }, cancellationToken);

    public void ApplySchedule(bool enabled, int everyHours, int keepDays)
    {
        lock (_gate)
        {
            _enabled = enabled;
            _every = Math.Clamp(everyHours, 1, 24);
            _keep = Math.Clamp(keepDays, 1, 90);
            _timer?.Dispose();
            _timer = null;
            if (enabled) _timer = new Timer(_ => Tick(), null, _firstDelay, _interval);
        }
    }

    /// <summary>Một lần kiểm tra theo lịch (public để test gọi trực tiếp, không cần chờ bộ đếm giờ).</summary>
    public BackupOutcome? Tick()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return null;
        try
        {
            bool enabled; int every, keep;
            lock (_gate) { enabled = _enabled; every = _every; keep = _keep; }
            if (!enabled) return null;
            var r = BackupService.RunIfDue(_dbPath, _dir, every, keep, _clock());
            if (r is null) return null;
            if (r.Ok) Changed?.Invoke();
            return new BackupOutcome(r.Ok, r.Message);
        }
        catch (Exception ex) { return new BackupOutcome(false, "Sao lưu tự động lỗi: " + ex.Message); }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    public void OpenFolder()
    {
        try { Directory.CreateDirectory(_dir); _openFolder(_dir); }
        catch (Exception) { /* không mở được thư mục thì thôi, không làm sập app */ }
    }

    private static void DefaultOpen(string dir) =>
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });

    public void Dispose()
    {
        lock (_gate) { _timer?.Dispose(); _timer = null; _enabled = false; }
    }
}

/// <summary>Bản giả cho --demo và test: không ghi đĩa, không mở Explorer.</summary>
public sealed class FakeBackupController : IBackupController
{
    private readonly List<BackupInfo> _items = new();
    private readonly Func<DateTime> _clock;
    public FakeBackupController(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.Now);
        var now = _clock();
        _items.Add(new BackupInfo("data-demo-1.db", now.AddHours(-2), 142_000_000));
        _items.Add(new BackupInfo("data-demo-2.db", now.AddHours(-4), 141_800_000));
    }

    public string FolderPath => "(chế độ demo: không có thư mục thật)";
    public bool FailNext { get; set; }
    public int OpenFolderCalls { get; private set; }
    public bool ScheduleEnabled { get; private set; }
    public int ScheduleEvery { get; private set; }
    public int ScheduleKeep { get; private set; }
    public int ApplyScheduleCalls { get; private set; }
    public event Action? Changed;

    public IReadOnlyList<BackupInfo> List() => _items.OrderByDescending(i => i.Time).ToList();

    public Task<BackupOutcome> BackupNowAsync(CancellationToken cancellationToken = default)
    {
        if (FailNext) { FailNext = false; return Task.FromResult(new BackupOutcome(false, "Sao lưu lỗi: (giả lập)")); }
        _items.Add(new BackupInfo($"data-demo-{_items.Count + 1}.db", _clock(), 142_100_000));
        Changed?.Invoke();
        return Task.FromResult(new BackupOutcome(true, "Đã sao lưu (demo, không ghi đĩa)."));
    }

    public void ApplySchedule(bool enabled, int everyHours, int keepDays)
    {
        ScheduleEnabled = enabled; ScheduleEvery = everyHours; ScheduleKeep = keepDays; ApplyScheduleCalls++;
    }

    public void OpenFolder() => OpenFolderCalls++;
    public void Dispose() { }
}
