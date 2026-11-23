using System.Security.Cryptography;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Import;
using QuanLyHangHoan.Data.Sheet;

namespace QuanLyHangHoan.App.Services;

public interface ISheetSyncController : IDisposable
{
    bool HasUrl { get; }
    string? Token { get; }
    bool Enabled { get; }
    string Tabs { get; }
    string MaxDays { get; }
    string? SkipTypes { get; }
    bool OnlyReturns { get; }
    long LastAtTicks { get; }
    bool LastOk { get; }
    string LastNote { get; }
    bool IsDemoMode { get; }

    event Action<SheetRunResult>? Completed;
    event Action? ConfigChanged;

    bool SetUrl(string url, out string error);
    void UpdateConfig(bool enabled, string tabs, string maxDays, string? skipTypes, bool onlyReturns);
    void Start(TimeSpan? firstDelay = null, TimeSpan? interval = null);
    Task<SheetRunResult> RunOnceAsync(bool manual = false, CancellationToken ct = default);
}

/// <summary>
/// Điều phối đồng bộ Google Sheet thật: bọc SheetSyncService của Data.
/// </summary>
public sealed class RealSheetSyncController : ISheetSyncController
{
    private readonly SheetSyncService _service;

    public RealSheetSyncController(
        string dbPath,
        string statePath,
        ISecretProtector? protector = null,
        Func<DateTime>? clock = null,
        Func<string, CancellationToken, Task<string>>? fetch = null)
    {
        var resolvedProtector = protector ?? CreateDefaultProtector();
        _service = new SheetSyncService(dbPath, statePath, resolvedProtector, clock ?? (() => DateTime.Now), fetch);
        _service.Completed += OnServiceCompleted;
    }

    private static ISecretProtector CreateDefaultProtector()
    {
        if (OperatingSystem.IsWindows())
        {
            return new DpapiProtector();
        }
        throw new PlatformNotSupportedException("DpapiProtector chỉ hỗ trợ Windows.");
    }

    public bool HasUrl => _service.HasUrl;
    public string? Token => _service.Token;
    public bool Enabled => _service.Config.Enabled;
    public string Tabs => _service.Config.Tabs;
    public string MaxDays => _service.Config.MaxDays;
    public string? SkipTypes => _service.Config.SkipTypes;
    public bool OnlyReturns => _service.Config.OnlyReturns;
    public long LastAtTicks => _service.Config.LastAtTicks;
    public bool LastOk => _service.Config.LastOk;
    public string LastNote => _service.Config.LastNote;
    public bool IsDemoMode => false;

    public event Action<SheetRunResult>? Completed;
    public event Action? ConfigChanged;

    public bool SetUrl(string url, out string error)
    {
        var ok = _service.SetUrl(url, out error);
        if (ok)
        {
            ConfigChanged?.Invoke();
        }
        return ok;
    }

    public void UpdateConfig(bool enabled, string tabs, string maxDays, string? skipTypes, bool onlyReturns)
    {
        _service.Config.Enabled = enabled;
        _service.Config.Tabs = tabs ?? string.Empty;
        _service.Config.MaxDays = maxDays ?? string.Empty;
        _service.Config.SkipTypes = string.IsNullOrWhiteSpace(skipTypes) ? null : skipTypes.Trim();
        _service.Config.OnlyReturns = onlyReturns;
        _service.SaveConfig();
        ConfigChanged?.Invoke();
    }

    public void Start(TimeSpan? firstDelay = null, TimeSpan? interval = null) =>
        _service.Start(firstDelay, interval);

    public Task<SheetRunResult> RunOnceAsync(bool manual = false, CancellationToken ct = default) =>
        _service.RunOnceAsync(manual, ct);

    private void OnServiceCompleted(SheetRunResult result) => Completed?.Invoke(result);

    public void Dispose()
    {
        _service.Completed -= OnServiceCompleted;
        _service.Dispose();
    }
}

/// <summary>
/// Bản giả cho chế độ --demo và test giao diện: KHÔNG chạm mạng hay đĩa thật.
/// </summary>
public sealed class FakeSheetSyncController : ISheetSyncController
{
    private readonly Func<bool, SheetRunResult>? _runOnceHandler;
    private readonly Func<DateTime> _clock;

    public FakeSheetSyncController(
        Func<bool, SheetRunResult>? runOnceHandler = null,
        Func<DateTime>? clock = null)
    {
        _runOnceHandler = runOnceHandler;
        _clock = clock ?? (() => DateTime.Now);
    }

    public bool HasUrl { get; private set; }
    public string? Token { get; private set; }
    public bool Enabled { get; private set; }
    public string Tabs { get; private set; } = string.Empty;
    public string MaxDays { get; private set; } = string.Empty;
    public string? SkipTypes { get; private set; }
    public bool OnlyReturns { get; private set; } = true;
    public long LastAtTicks { get; private set; }
    public bool LastOk { get; private set; } = true;
    public string LastNote { get; private set; } = string.Empty;
    public bool IsDemoMode => true;

    public event Action<SheetRunResult>? Completed;
    public event Action? ConfigChanged;

    public bool SetUrl(string url, out string error)
    {
        if (!SheetSyncService.IsAllowedUrl(url, out error))
        {
            return false;
        }

        HasUrl = true;
        Token ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        ConfigChanged?.Invoke();
        return true;
    }

    public void UpdateConfig(bool enabled, string tabs, string maxDays, string? skipTypes, bool onlyReturns)
    {
        Enabled = enabled;
        Tabs = tabs ?? string.Empty;
        MaxDays = maxDays ?? string.Empty;
        SkipTypes = string.IsNullOrWhiteSpace(skipTypes) ? null : skipTypes.Trim();
        OnlyReturns = onlyReturns;
        ConfigChanged?.Invoke();
    }

    public void Start(TimeSpan? firstDelay = null, TimeSpan? interval = null)
    {
        // Chế độ giả lập: không mở timer nền hay gọi mạng
    }

    public Task<SheetRunResult> RunOnceAsync(bool manual = false, CancellationToken ct = default)
    {
        if (!HasUrl)
        {
            return Task.FromResult(new SheetRunResult
            {
                Ran = false,
                Note = "Chưa dán địa chỉ Apps Script của Google Sheet."
            });
        }

        SheetRunResult result;
        if (_runOnceHandler is not null)
        {
            result = _runOnceHandler(manual);
        }
        else
        {
            result = new SheetRunResult
            {
                Ran = true,
                Ok = true,
                Note = "12 đơn ghép từ 3 tab. Thêm 2, cập nhật 1",
                Import = new TikTokImportResult { Ok = true, Added = 2, Updated = 1 }
            };
        }

        if (result.Ran)
        {
            LastAtTicks = _clock().Ticks;
            LastOk = result.Ok;
            LastNote = result.Note;
            Completed?.Invoke(result);
        }

        return Task.FromResult(result);
    }

    public void EmitCompleted(SheetRunResult result)
    {
        if (result.Ran)
        {
            LastAtTicks = _clock().Ticks;
            LastOk = result.Ok;
            LastNote = result.Note;
        }
        Completed?.Invoke(result);
    }

    public void Dispose() { }
}
