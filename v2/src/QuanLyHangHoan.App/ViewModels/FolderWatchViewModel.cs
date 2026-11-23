using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Data.FolderWatch;

namespace QuanLyHangHoan.App.ViewModels;

public sealed record FolderWatchOption(string Value, string Label);

public sealed record FolderWatchLogEntry(
    string TimeText,
    string StatusText,
    string FileNamesText,
    string SummaryText,
    bool IsError,
    bool IsWarning);

public sealed partial class FolderWatchViewModel : ObservableObject, IDisposable
{
    private const int MaxLogEntries = 100;

    private readonly IFolderWatchController _controller;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IConfirmationService _confirmationService;
    private readonly Action<Action> _dispatchToUi;
    private readonly Func<DateTime> _clock;
    private bool _suppressSave;
    private bool _disposed;

    public FolderWatchViewModel(
        IFolderWatchController controller,
        IFolderPickerService? folderPickerService = null,
        IConfirmationService? confirmationService = null,
        Action<Action>? dispatchToUi = null,
        Func<DateTime>? clock = null)
    {
        _controller = controller;
        _folderPickerService = folderPickerService ?? new WindowsFolderPickerService();
        _confirmationService = confirmationService ?? new WindowsConfirmationService();
        _dispatchToUi = dispatchToUi ?? (a => a());
        _clock = clock ?? (() => DateTime.Now);

        PriceModeOptions =
        [
            new FolderWatchOption("tong_gia_tri", "Tổng giá trị"),
            new FolderWatchOption("sau_chiet_khau", "Sau chiết khấu"),
            new FolderWatchOption("gia_salework", "Giá Salework"),
            new FolderWatchOption("doanh_thu_uoc_tinh", "Doanh thu ước tính")
        ];

        ProductModeOptions =
        [
            new FolderWatchOption("supplement", "Nhập bổ sung - giữ sản phẩm không có trong file mới"),
            new FolderWatchOption("replace", "Thay toàn bộ sản phẩm của từng kiện có trong file")
        ];

        _controller.Completed += OnControllerCompleted;
        _controller.ConfigChanged += OnControllerConfigChanged;

        SyncFromController();
    }

    public IReadOnlyList<FolderWatchOption> PriceModeOptions { get; }
    public IReadOnlyList<FolderWatchOption> ProductModeOptions { get; }
    public ObservableCollection<FolderWatchLogEntry> Logs { get; } = new();

    public bool IsDemoMode => _controller.IsDemoMode;

    public string FixedNoteText =>
        "App chỉ ĐỌC file, không xóa/sửa/di chuyển file nào. Cột thông tin khách hàng (tên, SĐT, địa chỉ) bị bỏ ngay khi đọc. Chỉ đọc .xlsx và .csv (file .xls cũ hãy lưu lại thành .xlsx).";

    public string EmptyGuideText =>
        "Chọn thư mục Tải xuống (Downloads) hoặc thư mục chứa file xuất từ TikTok Seller Center để app tự động nạp đơn mới.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolder))]
    [NotifyPropertyChangedFor(nameof(CanRunNow))]
    [NotifyPropertyChangedFor(nameof(DisplayDirText))]
    private string _dir = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunNow))]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusError))]
    [NotifyPropertyChangedFor(nameof(IsStatusSuccess))]
    private bool _lastOk = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastStatusText))]
    [NotifyPropertyChangedFor(nameof(IsStatusError))]
    [NotifyPropertyChangedFor(nameof(IsStatusSuccess))]
    private string _lastNote = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunNow))]
    [NotifyPropertyChangedFor(nameof(RunNowButtonText))]
    [NotifyPropertyChangedFor(nameof(RunExistingButtonText))]
    private bool _isRunning;

    [ObservableProperty]
    private FolderWatchOption _selectedPriceMode = new("tong_gia_tri", "Tổng giá trị");

    [ObservableProperty]
    private FolderWatchOption _selectedProductMode = new("supplement", "Nhập bổ sung - giữ sản phẩm không có trong file mới");

    [ObservableProperty]
    private bool _onlyReturns = true;

    [ObservableProperty]
    private bool _skipUnshippedCancel;

    public bool HasFolder => !string.IsNullOrWhiteSpace(Dir);
    public bool CanRunNow => HasFolder && IsEnabled && !IsRunning;
    public string DisplayDirText => HasFolder ? Dir : "(Chưa chọn thư mục)";
    public string LastStatusText => string.IsNullOrWhiteSpace(LastNote) ? "Chưa nạp file nào." : LastNote;
    public bool IsStatusError => !LastOk && !string.IsNullOrWhiteSpace(LastNote);
    public bool IsStatusSuccess => LastOk && !string.IsNullOrWhiteSpace(LastNote);
    public string RunNowButtonText => IsRunning ? "Đang nạp..." : "Nạp ngay";
    public string RunExistingButtonText => IsRunning ? "Đang nạp..." : "Nạp các file đang có trong thư mục";
    public bool HasLogs => Logs.Count > 0;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressSave) return;
        if (value && !HasFolder)
        {
            _suppressSave = true;
            IsEnabled = false;
            _suppressSave = false;
            return;
        }

        _controller.Configure(Dir, value);
        if (value && !IsDemoMode)
        {
            _controller.Start();
        }
    }

    partial void OnSelectedPriceModeChanged(FolderWatchOption value) => SaveOptionsIfNeeded();
    partial void OnSelectedProductModeChanged(FolderWatchOption value) => SaveOptionsIfNeeded();
    partial void OnOnlyReturnsChanged(bool value) => SaveOptionsIfNeeded();
    partial void OnSkipUnshippedCancelChanged(bool value) => SaveOptionsIfNeeded();

    [RelayCommand]
    public void BrowseFolder()
    {
        var picked = _folderPickerService.PickFolder("Chọn thư mục theo dõi file TikTok", HasFolder ? Dir : null);
        if (string.IsNullOrWhiteSpace(picked)) return;

        _controller.Configure(picked, IsEnabled);
        SyncFromController();
    }

    [RelayCommand]
    public async Task RunNowAsync()
    {
        if (!CanRunNow) return;
        IsRunning = true;
        try
        {
            await Task.Run(() => _controller.RunOnce(includeExisting: false));
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    public async Task RunExistingAsync()
    {
        if (!CanRunNow) return;
        var confirmed = _confirmationService.Confirm(
            "Xác nhận nạp file đang có",
            "Sẽ nạp cả các file TikTok đã có sẵn trước khi bật theo dõi");
        if (!confirmed) return;

        IsRunning = true;
        try
        {
            await Task.Run(() => _controller.RunOnce(includeExisting: true));
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void SaveOptionsIfNeeded()
    {
        if (_suppressSave) return;
        _controller.UpdateOptions(
            SelectedPriceMode?.Value ?? "tong_gia_tri",
            SelectedProductMode?.Value ?? "supplement",
            OnlyReturns,
            SkipUnshippedCancel);
    }

    private void OnControllerConfigChanged()
    {
        _dispatchToUi(() =>
        {
            if (!_disposed) SyncFromController();
        });
    }

    private void OnControllerCompleted(FolderWatchRunResult result)
    {
        _dispatchToUi(() =>
        {
            if (_disposed) return;

            LastOk = _controller.LastOk;
            LastNote = _controller.LastNote;

            if (!result.Ran) return;

            var timeText = _clock().ToString("HH:mm:ss");
            var ambiguousCount = result.Import?.Ambiguous.Count ?? 0;

            if (!result.Ok)
            {
                var failedFiles = result.Failed.Count > 0
                    ? string.Join(", ", result.Failed)
                    : (result.Names.Count > 0 ? string.Join(", ", result.Names) : "-");
                InsertLog(new FolderWatchLogEntry(
                    timeText,
                    "Lỗi",
                    failedFiles,
                    result.Note,
                    IsError: true,
                    IsWarning: false));
                return;
            }

            if (result.FilesImported > 0)
            {
                if (ambiguousCount > 0)
                {
                    InsertLog(new FolderWatchLogEntry(
                        timeText,
                        "Cảnh báo",
                        string.Join(", ", result.Names),
                        $"{ambiguousCount} dòng khớp nhiều kiện nên chưa nạp, cần kiểm tra thủ công",
                        IsError: false,
                        IsWarning: true));
                }

                var added = result.Import?.Added ?? 0;
                var updated = result.Import?.Updated ?? 0;
                var summary = $"thêm {added}, cập nhật {updated}";
                if ((result.Import?.EnrichedArchived ?? 0) > 0)
                {
                    summary += $", bổ sung lưu trữ {result.Import!.EnrichedArchived}";
                }

                InsertLog(new FolderWatchLogEntry(
                    timeText,
                    "Thành công",
                    string.Join(", ", result.Names),
                    summary,
                    IsError: false,
                    IsWarning: false));
            }
        });
    }

    private void InsertLog(FolderWatchLogEntry entry)
    {
        Logs.Insert(0, entry);
        while (Logs.Count > MaxLogEntries)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
        OnPropertyChanged(nameof(HasLogs));
    }

    private void SyncFromController()
    {
        _suppressSave = true;
        try
        {
            Dir = _controller.Dir;
            IsEnabled = _controller.Enabled;
            LastOk = _controller.LastOk;
            LastNote = _controller.LastNote;
            SelectedPriceMode = PriceModeOptions.FirstOrDefault(o =>
                string.Equals(o.Value, _controller.PriceMode, StringComparison.OrdinalIgnoreCase)) ?? PriceModeOptions[0];
            SelectedProductMode = ProductModeOptions.FirstOrDefault(o =>
                string.Equals(o.Value, _controller.ProductMode, StringComparison.OrdinalIgnoreCase)) ?? ProductModeOptions[0];
            OnlyReturns = _controller.OnlyReturns;
            SkipUnshippedCancel = _controller.SkipUnshippedCancel;
        }
        finally
        {
            _suppressSave = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.Completed -= OnControllerCompleted;
        _controller.ConfigChanged -= OnControllerConfigChanged;
    }
}
