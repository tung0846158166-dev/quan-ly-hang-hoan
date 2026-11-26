using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.FolderWatch;
using QuanLyHangHoan.Data.Import;

namespace QuanLyHangHoan.App.Services;

public interface IFolderWatchController : IDisposable
{
    string Dir { get; }
    bool Enabled { get; }
    bool LastOk { get; }
    string LastNote { get; }
    string PriceMode { get; }
    string ProductMode { get; }
    bool OnlyReturns { get; }
    bool SkipUnshippedCancel { get; }
    bool IsDemoMode { get; }

    event Action<FolderWatchRunResult>? Completed;
    event Action? ConfigChanged;

    void Start(TimeSpan? firstDelay = null);
    void Configure(string dir, bool enabled);
    void UpdateOptions(string priceMode, string productMode, bool onlyReturns, bool skipUnshippedCancel);
    FolderWatchRunResult RunOnce(bool includeExisting = false);
}

/// <summary>
/// Điều phối theo dõi thư mục thật: bọc FolderWatchService của Data.
/// </summary>
public sealed class RealFolderWatchController : IFolderWatchController
{
    private readonly FolderWatchService _service;

    public RealFolderWatchController(
        string dbPath,
        string statePath,
        Func<DateTime>? clock = null,
        TimeSpan? interval = null,
        QuanLyHangHoan.Data.Cloud.CloudImportSink? cloud = null)
    {
        _service = new FolderWatchService(dbPath, statePath, clock ?? (() => DateTime.Now), interval, cloud);
        _service.Completed += OnServiceCompleted;
    }

    public string Dir => _service.Config.Dir;
    public bool Enabled => _service.Config.Enabled;
    public bool LastOk => _service.Config.LastOk;
    public string LastNote => _service.Config.LastNote;
    public string PriceMode => _service.Config.PriceMode;
    public string ProductMode => _service.Config.ProductMode;
    public bool OnlyReturns => _service.Config.OnlyReturns;
    public bool SkipUnshippedCancel => _service.Config.SkipUnshippedCancel;
    public bool IsDemoMode => false;

    public event Action<FolderWatchRunResult>? Completed;
    public event Action? ConfigChanged;

    public void Start(TimeSpan? firstDelay = null) => _service.Start(firstDelay);

    public void Configure(string dir, bool enabled)
    {
        _service.Configure(dir, enabled);
        ConfigChanged?.Invoke();
    }

    public void UpdateOptions(string priceMode, string productMode, bool onlyReturns, bool skipUnshippedCancel)
    {
        _service.Config.PriceMode = priceMode;
        _service.Config.ProductMode = productMode;
        _service.Config.OnlyReturns = onlyReturns;
        _service.Config.SkipUnshippedCancel = skipUnshippedCancel;
        _service.SaveConfig();
        ConfigChanged?.Invoke();
    }

    public FolderWatchRunResult RunOnce(bool includeExisting = false) => _service.RunOnce(includeExisting);

    private void OnServiceCompleted(FolderWatchRunResult result) => Completed?.Invoke(result);

    public void Dispose()
    {
        _service.Completed -= OnServiceCompleted;
        _service.Dispose();
    }
}

/// <summary>
/// Bản giả cho chế độ --demo và test giao diện: KHÔNG đọc/ghi ổ đĩa hay folder_watch.json thật.
/// </summary>
public sealed class FakeFolderWatchController : IFolderWatchController
{
    private readonly Func<bool, FolderWatchRunResult>? _runOnceHandler;

    public FakeFolderWatchController(Func<bool, FolderWatchRunResult>? runOnceHandler = null)
    {
        _runOnceHandler = runOnceHandler;
    }

    public string Dir { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public bool LastOk { get; private set; } = true;
    public string LastNote { get; private set; } = string.Empty;
    public string PriceMode { get; private set; } = "tong_gia_tri";
    public string ProductMode { get; private set; } = "supplement";
    public bool OnlyReturns { get; private set; } = true;
    public bool SkipUnshippedCancel { get; private set; }
    public bool IsDemoMode => true;

    public event Action<FolderWatchRunResult>? Completed;
    public event Action? ConfigChanged;

    public void Start(TimeSpan? firstDelay = null)
    {
        // Chế độ giả lập: không mở timer nền hay đọc ổ đĩa
    }

    public void Configure(string dir, bool enabled)
    {
        var d = (dir ?? string.Empty).Trim().Trim('"');
        Dir = d;
        Enabled = enabled && d.Length > 0;
        ConfigChanged?.Invoke();
    }

    public void UpdateOptions(string priceMode, string productMode, bool onlyReturns, bool skipUnshippedCancel)
    {
        PriceMode = priceMode;
        ProductMode = productMode;
        OnlyReturns = onlyReturns;
        SkipUnshippedCancel = skipUnshippedCancel;
        ConfigChanged?.Invoke();
    }

    public FolderWatchRunResult RunOnce(bool includeExisting = false)
    {
        if (!Enabled || Dir.Length == 0)
        {
            return new FolderWatchRunResult { Ran = false };
        }

        FolderWatchRunResult result;
        if (_runOnceHandler is not null)
        {
            result = _runOnceHandler(includeExisting);
        }
        else
        {
            result = new FolderWatchRunResult
            {
                Ran = true,
                Ok = true,
                Note = "14:00 23/11: Đã nạp 1 file TikTok (tiktok-demo.csv): thêm 2, cập nhật 1",
                FilesImported = 1,
                Import = new TikTokImportResult { Ok = true, Added = 2, Updated = 1 }
            };
            result.Names.Add("tiktok-demo.csv");
        }

        if (result.Ran)
        {
            LastOk = result.Ok;
            LastNote = result.Note;
            Completed?.Invoke(result);
        }

        return result;
    }

    public void EmitCompleted(FolderWatchRunResult result)
    {
        if (result.Ran)
        {
            LastOk = result.Ok;
            LastNote = result.Note;
        }
        Completed?.Invoke(result);
    }

    public void Dispose() { }
}
