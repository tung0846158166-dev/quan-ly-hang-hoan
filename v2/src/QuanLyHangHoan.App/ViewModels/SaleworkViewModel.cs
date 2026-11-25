using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;

namespace QuanLyHangHoan.App.ViewModels;

public sealed record SaleworkLogEntry(
    string TimeText,
    string StatusText,
    string SummaryText,
    bool IsError,
    bool IsWarning);

public sealed partial class SaleworkViewModel : ObservableObject, IDisposable
{
    private const int MaxLogEntries = 100;

    private readonly ISaleworkController _controller;
    private readonly IConfirmationService _confirmationService;
    private readonly Action<Action> _dispatchToUi;
    private readonly Func<DateTime> _clock;
    private readonly WriteGate _writeGate;
    private bool _suppressSave;
    private bool _disposed;

    public SaleworkViewModel(
        ISaleworkController controller,
        IConfirmationService? confirmationService = null,
        Action<Action>? dispatchToUi = null,
        Func<DateTime>? clock = null,
        WriteGate? writeGate = null)
    {
        _controller = controller;
        _confirmationService = confirmationService ?? new WindowsConfirmationService();
        _dispatchToUi = dispatchToUi ?? (a => a());
        _clock = clock ?? (() => DateTime.Now);
        _writeGate = writeGate ?? WriteGate.AlwaysOpen;
        if (!ReferenceEquals(_writeGate, WriteGate.AlwaysOpen))
        {
            _writeGate.Changed += OnWriteGateChanged;
        }

        _controller.Completed += OnControllerCompleted;
        _controller.StateChanged += OnControllerStateChanged;

        SyncFromController();
    }

    public ObservableCollection<SaleworkLogEntry> Logs { get; } = new();

    public bool IsDemoMode => _controller.IsDemoMode;
    public bool HasLogs => Logs.Count > 0;

    public string GuideText =>
        "1. Bấm \"Đăng nhập Salework\", tự đăng nhập rồi mở trang Đơn hoàn (chọn kênh TikTok).\n" +
        "2. Bấm \"Lưu trang này\" để app nhớ địa chỉ trang Đơn hoàn (không lưu mật khẩu hay cookie ra ngoài).\n" +
        "3. Bấm \"Lấy ngay\" — app tự chọn tab \"Đang chờ hoàn\" và xuất Excel (bật \"Chế độ xem\" để theo dõi).";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunNow))]
    [NotifyPropertyChangedFor(nameof(ConfigStatusText))]
    [NotifyCanExecuteChangedFor(nameof(RunNowCommand))]
    private bool _configured;

    [ObservableProperty]
    private bool _watchMode = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunNow))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    [NotifyPropertyChangedFor(nameof(RunNowButtonText))]
    [NotifyCanExecuteChangedFor(nameof(RunNowCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveCurrentPage))]
    private bool _isLoginOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActionError))]
    private string _actionError = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActionNotice))]
    private string _actionNotice = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastStatusText))]
    private long _lastAtTicks;

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
    private string _lastErrorStep = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReloginHint))]
    private bool _needsRelogin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAmbiguousWarning))]
    [NotifyPropertyChangedFor(nameof(AmbiguousWarningText))]
    private int _ambiguousCount;

    public bool CanWrite => _writeGate.CanWrite;
    public string? WriteDisabledTooltip => _writeGate.TooltipReason;

    private void OnWriteGateChanged()
    {
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(WriteDisabledTooltip));
        OnPropertyChanged(nameof(CanRunNow));
        RunNowCommand.NotifyCanExecuteChanged();
    }

    public bool HasActionError => !string.IsNullOrWhiteSpace(ActionError);
    public bool HasActionNotice => !string.IsNullOrWhiteSpace(ActionNotice);
    public bool CanSaveCurrentPage => IsLoginOpen && !IsRunning;
    public bool CanRunNow => CanWrite && Configured && !IsRunning;
    public bool CanStop => IsRunning;
    public string RunNowButtonText => IsRunning ? "Đang lấy..." : "Lấy ngay";
    public bool ShowReloginHint => NeedsRelogin;
    public bool HasAmbiguousWarning => AmbiguousCount > 0;
    public string AmbiguousWarningText => $"{AmbiguousCount} đơn có nhiều kiện khác nhau, giữ nguyên để bạn xem";
    public string ConfigStatusText => Configured
        ? "🔒 Đã lưu trang Đơn hoàn (ẩn địa chỉ)"
        : "Chưa lưu trang Đơn hoàn — bấm \"Đăng nhập Salework\" rồi \"Lưu trang này\".";

    public string LastStatusText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(LastNote)) return "Chưa lấy dữ liệu lần nào.";
            if (LastAtTicks > 0)
            {
                var dt = new DateTime(LastAtTicks);
                return $"{dt:HH:mm dd/MM}: {LastNote}";
            }
            return LastNote;
        }
    }

    public bool IsStatusError => !LastOk && !string.IsNullOrWhiteSpace(LastNote);
    public bool IsStatusSuccess => LastOk && !string.IsNullOrWhiteSpace(LastNote);

    partial void OnWatchModeChanged(bool value)
    {
        if (_suppressSave) return;
        _controller.SetWatchMode(value);
    }

    [RelayCommand]
    public void OpenLogin()
    {
        ActionError = string.Empty;
        ActionNotice = string.Empty;
        if (!_controller.OpenLogin(null, out var error))
        {
            ActionError = error;
            return;
        }

        SyncFromController();
        ActionNotice = "Đã mở cửa sổ đăng nhập Salework. Hãy đăng nhập và mở trang Đơn hoàn kênh TikTok rồi bấm \"Lưu trang này\".";
    }

    [RelayCommand]
    public void SaveCurrentPage()
    {
        ActionError = string.Empty;
        ActionNotice = string.Empty;
        if (!_controller.SaveCurrentPage(out var error))
        {
            ActionError = error;
            return;
        }

        SyncFromController();
        ActionNotice = "Đã lưu trang Đơn hoàn.";
        InsertLog(new SaleworkLogEntry(
            _clock().ToString("HH:mm:ss"),
            "Cài đặt",
            "Đã lưu địa chỉ trang Đơn hoàn (đã ẩn địa chỉ).",
            IsError: false,
            IsWarning: false));
    }

    [RelayCommand(CanExecute = nameof(CanRunNow))]
    public async Task RunNowAsync()
    {
        if (!CanRunNow) return;
        ActionError = string.Empty;
        ActionNotice = string.Empty;
        AmbiguousCount = 0;
        IsRunning = true;
        try
        {
            await Task.Run(() => _controller.RunNowAsync());
        }
        finally
        {
            IsRunning = false;
            SyncFromController();
        }
    }

    [RelayCommand]
    public void Stop()
    {
        _controller.Stop();
        ActionNotice = "Đang dừng...";
    }

    [RelayCommand]
    public async Task ForgetAsync()
    {
        ActionError = string.Empty;
        ActionNotice = string.Empty;
        var ok = _confirmationService.Confirm(
            "Quên đăng nhập Salework",
            "Xóa địa chỉ trang Đơn hoàn đã lưu và toàn bộ phiên đăng nhập Salework riêng của app?");
        if (!ok) return;

        await _controller.ForgetAsync();
        AmbiguousCount = 0;
        SyncFromController();
        ActionNotice = "Đã xóa cài đặt và phiên đăng nhập Salework.";
        InsertLog(new SaleworkLogEntry(
            _clock().ToString("HH:mm:ss"),
            "Đã xóa",
            "Đã xóa địa chỉ đã lưu và dữ liệu hồ sơ WebView2 riêng.",
            IsError: false,
            IsWarning: false));
    }

    private void OnControllerStateChanged()
    {
        _dispatchToUi(() =>
        {
            if (!_disposed) SyncFromController();
        });
    }

    private void OnControllerCompleted(SaleworkRunResult result)
    {
        _dispatchToUi(() =>
        {
            if (_disposed) return;
            SyncFromController();
            if (!result.Ran) return;

            var timeText = _clock().ToString("HH:mm:ss");
            if (!result.Ok)
            {
                var summary = string.IsNullOrWhiteSpace(result.Step)
                    ? result.Error
                    : $"Bước \"{result.Step}\": {result.Error}";
                InsertLog(new SaleworkLogEntry(
                    timeText,
                    result.Stopped ? "Đã dừng" : "Lỗi",
                    summary,
                    IsError: true,
                    IsWarning: false));
                return;
            }

            AmbiguousCount = result.Import?.SupersedeAmbiguous ?? 0;
            if (AmbiguousCount > 0)
            {
                InsertLog(new SaleworkLogEntry(
                    timeText,
                    "Cảnh báo",
                    $"{AmbiguousCount} đơn có nhiều kiện khác nhau, giữ nguyên để bạn xem",
                    IsError: false,
                    IsWarning: true));
            }

            var merged = result.Import?.SupersededMerged ?? 0;
            var msg = $"Đã lấy {result.RowCount} dòng từ Salework và nạp vào bảng"
                + (merged > 0 ? $", gộp {merged} dòng chờ cũ" : string.Empty);
            InsertLog(new SaleworkLogEntry(
                timeText,
                "Thành công",
                msg,
                IsError: false,
                IsWarning: false));
        });
    }

    private void InsertLog(SaleworkLogEntry entry)
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
            Configured = _controller.Configured;
            WatchMode = _controller.WatchMode;
            IsRunning = _controller.IsRunning;
            IsLoginOpen = _controller.IsLoginOpen;
            LastAtTicks = _controller.LastAtTicks;
            LastOk = _controller.LastOk;
            LastNote = _controller.LastNote;
            LastErrorStep = _controller.LastErrorStep;
            NeedsRelogin = _controller.NeedsRelogin;
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
        if (!ReferenceEquals(_writeGate, WriteGate.AlwaysOpen))
        {
            _writeGate.Changed -= OnWriteGateChanged;
        }
        _controller.Completed -= OnControllerCompleted;
        _controller.StateChanged -= OnControllerStateChanged;
    }
}
