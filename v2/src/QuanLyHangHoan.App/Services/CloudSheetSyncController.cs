using QuanLyHangHoan.Data.Sheet;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Chế độ cloud: Google Sheet do cloud tự nạp (Apps Script gửi thẳng lên Worker, xem cloud/sheet-script).
/// Máy tính KHÔNG nạp Sheet nữa để tránh hai nơi cùng ghi một đơn: không chạy lịch, bấm "chạy ngay" chỉ báo lý do.
/// Cấu hình cũ vẫn đọc/ghi qua controller gốc (không mất khi tắt cloud).
/// </summary>
public sealed class CloudSheetSyncController : ISheetSyncController
{
    public const string CloudNote = "Đang dùng cloud: Google Sheet do cloud tự nạp (Apps Script), máy tính không nạp Sheet.";
    private readonly ISheetSyncController _inner;

    public CloudSheetSyncController(ISheetSyncController inner) => _inner = inner;

    public bool HasUrl => _inner.HasUrl;
    public string? Token => _inner.Token;
    public bool Enabled => false;
    public string Tabs => _inner.Tabs;
    public string MaxDays => _inner.MaxDays;
    public string? SkipTypes => _inner.SkipTypes;
    public bool OnlyReturns => _inner.OnlyReturns;
    public long LastAtTicks => _inner.LastAtTicks;
    public bool LastOk => true;
    public string LastNote => CloudNote;
    public bool IsDemoMode => _inner.IsDemoMode;

    public event Action<SheetRunResult>? Completed { add { } remove { } }
    public event Action? ConfigChanged
    {
        add => _inner.ConfigChanged += value;
        remove => _inner.ConfigChanged -= value;
    }

    public bool SetUrl(string url, out string error) => _inner.SetUrl(url, out error);
    public bool SetToken(string token, out string error) => _inner.SetToken(token, out error);
    public void UpdateConfig(bool enabled, string tabs, string maxDays, string? skipTypes, bool onlyReturns) =>
        _inner.UpdateConfig(_inner.Enabled, tabs, maxDays, skipTypes, onlyReturns);   // giữ nguyên công tắc gốc để tắt cloud thì như cũ
    public void Start(TimeSpan? firstDelay = null, TimeSpan? interval = null) { }
    public Task<SheetRunResult> RunOnceAsync(bool manual = false, CancellationToken ct = default) =>
        Task.FromResult(new SheetRunResult { Ran = false, Ok = true, Note = CloudNote });
    public void Dispose() => _inner.Dispose();
}
