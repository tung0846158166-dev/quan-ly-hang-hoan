using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Data.Sheet;

namespace QuanLyHangHoan.App.ViewModels;

public sealed record GoogleSheetLogEntry(
    string TimeText,
    string StatusText,
    string SummaryText,
    bool IsError,
    bool IsWarning);

public sealed partial class GoogleSheetViewModel : ObservableObject, IDisposable
{
    private const int MaxLogEntries = 100;

    private readonly ISheetSyncController _controller;
    private readonly IClipboardService _clipboardService;
    private readonly Action<Action> _dispatchToUi;
    private readonly Func<DateTime> _clock;
    private readonly WriteGate _writeGate;
    private bool _suppressSave;
    private bool _disposed;

    public GoogleSheetViewModel(
        ISheetSyncController controller,
        IClipboardService? clipboardService = null,
        Action<Action>? dispatchToUi = null,
        Func<DateTime>? clock = null,
        WriteGate? writeGate = null)
    {
        _controller = controller;
        _clipboardService = clipboardService ?? new WindowsClipboardService();
        _dispatchToUi = dispatchToUi ?? (a => a());
        _clock = clock ?? (() => DateTime.Now);
        _writeGate = writeGate ?? WriteGate.AlwaysOpen;
        if (!ReferenceEquals(_writeGate, WriteGate.AlwaysOpen))
        {
            _writeGate.Changed += OnWriteGateChanged;
        }

        _controller.Completed += OnControllerCompleted;
        _controller.ConfigChanged += OnControllerConfigChanged;

        SyncFromController();
    }

    public ObservableCollection<GoogleSheetLogEntry> Logs { get; } = new();

    public bool IsDemoMode => _controller.IsDemoMode;
    public bool HasLogs => Logs.Count > 0;

    public string AppsScriptGuideText =>
        "1. Mở Google Sheet → Tiện ích mở rộng → Apps Script, dán Token bên trên vào biến TOKEN trong script.\n" +
        "2. Chọn Triển khai → Triển khai mới → Ứng dụng web (Ai có quyền truy cập: Bất kỳ ai).\n" +
        "3. Sao chép địa chỉ Web App (https://script.google.com/macros/s/.../exec) rồi dán vào ô Địa chỉ Apps Script.";

    [ObservableProperty]
    private string _urlInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUrlError))]
    private string _urlError = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSavedUrlState))]
    [NotifyPropertyChangedFor(nameof(ShowUrlEditor))]
    [NotifyPropertyChangedFor(nameof(CanRunNow))]
    [NotifyCanExecuteChangedFor(nameof(RunNowCommand))]
    private bool _hasUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSavedUrlState))]
    [NotifyPropertyChangedFor(nameof(ShowUrlEditor))]
    private bool _isEditingUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasToken))]
    [NotifyPropertyChangedFor(nameof(DisplayTokenText))]
    private string _token = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCopiedNotice))]
    private string _copiedNotice = string.Empty;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string _tabs = string.Empty;

    [ObservableProperty]
    private string _maxDays = string.Empty;

    [ObservableProperty]
    private string _skipTypes = string.Empty;

    [ObservableProperty]
    private bool _onlyReturns = true;

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
    [NotifyPropertyChangedFor(nameof(CanRunNow))]
    [NotifyPropertyChangedFor(nameof(RunNowButtonText))]
    [NotifyCanExecuteChangedFor(nameof(RunNowCommand))]
    private bool _isRunning;

    public bool CanWrite => _writeGate.CanWrite;
    public string? WriteDisabledTooltip => _writeGate.TooltipReason;

    private void OnWriteGateChanged()
    {
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(WriteDisabledTooltip));
        OnPropertyChanged(nameof(CanRunNow));
        RunNowCommand.NotifyCanExecuteChanged();
    }

    public bool HasUrlError => !string.IsNullOrWhiteSpace(UrlError);
    public bool ShowSavedUrlState => HasUrl && !IsEditingUrl;
    public bool ShowUrlEditor => !HasUrl || IsEditingUrl;
    public bool HasToken => !string.IsNullOrWhiteSpace(Token);
    public string DisplayTokenText => HasToken ? Token : "(Lưu địa chỉ Apps Script để tạo token)";
    public bool HasCopiedNotice => !string.IsNullOrWhiteSpace(CopiedNotice);
    public bool CanRunNow => CanWrite && HasUrl && !IsRunning;
    public string RunNowButtonText => IsRunning ? "Đang cập nhật..." : "Cập nhật ngay";

    public string LastStatusText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(LastNote)) return "Chưa cập nhật lần nào.";
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

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressSave) return;
        SaveConfigIfNeeded();
        if (value && HasUrl && !IsDemoMode)
        {
            _controller.Start();
        }
    }

    partial void OnTabsChanged(string value) => SaveConfigIfNeeded();
    partial void OnMaxDaysChanged(string value) => SaveConfigIfNeeded();
    partial void OnSkipTypesChanged(string value) => SaveConfigIfNeeded();
    partial void OnOnlyReturnsChanged(bool value) => SaveConfigIfNeeded();

    [RelayCommand]
    public void SaveUrl()
    {
        CopiedNotice = string.Empty;
        if (!_controller.SetUrl(UrlInput, out var error))
        {
            UrlError = error;
            return;
        }

        UrlError = string.Empty;
        UrlInput = string.Empty;
        IsEditingUrl = false;
        SyncFromController();

        if (IsEnabled && HasUrl && !IsDemoMode)
        {
            _controller.Start();
        }
    }

    [RelayCommand]
    public void StartEditUrl()
    {
        UrlError = string.Empty;
        UrlInput = string.Empty;
        IsEditingUrl = true;
    }

    [RelayCommand]
    public void CancelEditUrl()
    {
        UrlError = string.Empty;
        UrlInput = string.Empty;
        IsEditingUrl = false;
    }

    [ObservableProperty]
    private string _tokenInput = string.Empty;

    [ObservableProperty]
    private string _tokenError = string.Empty;

    /// <summary>Dán token của app cũ để hai app dùng chung một Apps Script.</summary>
    [RelayCommand]
    public void SaveToken()
    {
        if (_controller.SetToken(TokenInput, out var err))
        {
            TokenError = string.Empty;
            TokenInput = string.Empty;
            Token = _controller.Token ?? string.Empty;
            CopiedNotice = "Đã dùng token mới";
        }
        else TokenError = err;
    }

    [RelayCommand]
    public void CopyToken()
    {
        if (!HasToken) return;
        _clipboardService.SetText(Token);
        CopiedNotice = "Đã sao chép token";
    }

    [RelayCommand(CanExecute = nameof(CanRunNow))]
    public async Task RunNowAsync()
    {
        if (!CanRunNow) return;
        IsRunning = true;
        try
        {
            await Task.Run(() => _controller.RunOnceAsync(manual: true));
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void SaveConfigIfNeeded()
    {
        if (_suppressSave) return;
        _controller.UpdateConfig(
            IsEnabled,
            Tabs,
            MaxDays,
            string.IsNullOrWhiteSpace(SkipTypes) ? null : SkipTypes,
            OnlyReturns);
    }

    private void OnControllerConfigChanged()
    {
        _dispatchToUi(() =>
        {
            if (!_disposed) SyncFromController();
        });
    }

    private void OnControllerCompleted(SheetRunResult result)
    {
        _dispatchToUi(() =>
        {
            if (_disposed) return;

            LastAtTicks = _controller.LastAtTicks;
            LastOk = _controller.LastOk;
            LastNote = _controller.LastNote;

            if (!result.Ran) return;

            var timeText = _clock().ToString("HH:mm:ss");
            if (!result.Ok)
            {
                InsertLog(new GoogleSheetLogEntry(
                    timeText,
                    "Lỗi",
                    result.Note,
                    IsError: true,
                    IsWarning: false));
                return;
            }

            var ambiguousCount = result.Import?.Ambiguous.Count ?? 0;
            if (ambiguousCount > 0)
            {
                InsertLog(new GoogleSheetLogEntry(
                    timeText,
                    "Cảnh báo",
                    $"{ambiguousCount} dòng khớp nhiều kiện nên chưa nạp, cần kiểm tra thủ công",
                    IsError: false,
                    IsWarning: true));
            }

            InsertLog(new GoogleSheetLogEntry(
                timeText,
                result.Unchanged ? "Không đổi" : "Thành công",
                result.Note,
                IsError: false,
                IsWarning: false));
        });
    }

    private void InsertLog(GoogleSheetLogEntry entry)
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
            HasUrl = _controller.HasUrl;
            Token = _controller.Token ?? string.Empty;
            IsEnabled = _controller.Enabled;
            Tabs = _controller.Tabs;
            MaxDays = _controller.MaxDays;
            SkipTypes = _controller.SkipTypes ?? string.Empty;
            OnlyReturns = _controller.OnlyReturns;
            LastAtTicks = _controller.LastAtTicks;
            LastOk = _controller.LastOk;
            LastNote = _controller.LastNote;
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
        _controller.ConfigChanged -= OnControllerConfigChanged;
    }
}
