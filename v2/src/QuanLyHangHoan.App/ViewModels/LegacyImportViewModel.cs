using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;

namespace QuanLyHangHoan.App.ViewModels;

public sealed partial class LegacyImportViewModel : ObservableObject
{
    private readonly ILegacyImporter _importer;
    private readonly IFolderPickerService? _folderPicker;
    private readonly WriteGate _writeGate;

    public LegacyImportViewModel(ILegacyImporter importer, IFolderPickerService? folderPicker = null, bool isDemoMode = false, WriteGate? writeGate = null)
    {
        _importer = importer;
        _folderPicker = folderPicker;
        _isDemoMode = isDemoMode;
        _writeGate = writeGate ?? WriteGate.AlwaysOpen;
        if (!ReferenceEquals(_writeGate, WriteGate.AlwaysOpen))
        {
            _writeGate.Changed += OnWriteGateChanged;
        }
    }

    public bool CanWrite => _writeGate.CanWrite;
    public string? WriteDisabledTooltip => _writeGate.TooltipReason;

    private void OnWriteGateChanged()
    {
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(WriteDisabledTooltip));
        OnPropertyChanged(nameof(CanExecuteImport));
        ExecuteImportCommand.NotifyCanExecuteChanged();
    }

    public event Func<Task>? ImportSucceeded;

    [ObservableProperty]
    private bool _isDemoMode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunDryRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteImportCommand))]
    private string _sourceFolderPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunDryRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteImportCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteImportCommand))]
    private bool _hasCompletedDryRun;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteImportCommand))]
    private bool _isConfirmed;

    [ObservableProperty]
    private LegacyImportDryRunReport? _dryRunReport;

    [ObservableProperty]
    private LegacyImportResult? _importResult;

    [ObservableProperty]
    private string _statusMessage = "Bước 1: Chọn thư mục chứa dữ liệu app cũ, sau đó bấm Chạy thử (Dry-run) để kiểm tra trước khi nhập.";

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public bool HasWarnings => DryRunReport is { Warnings.Count: > 0 };
    public bool HasSkippedByDecision => DryRunReport is { SkippedByDecision.Count: > 0 };
    public bool HasImportResult => ImportResult is not null;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnSourceFolderPathChanged(string value)
    {
        // Khi đổi đường dẫn nguồn, bắt buộc chạy thử (dry-run) lại và xác nhận lại.
        HasCompletedDryRun = false;
        IsConfirmed = false;
        DryRunReport = null;
        ImportResult = null;
        ErrorMessage = string.Empty;
    }

    partial void OnDryRunReportChanged(LegacyImportDryRunReport? value)
    {
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(HasSkippedByDecision));
    }

    partial void OnImportResultChanged(LegacyImportResult? value)
    {
        OnPropertyChanged(nameof(HasImportResult));
    }

    partial void OnErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    public bool CanRunDryRun => !IsBusy && !string.IsNullOrWhiteSpace(SourceFolderPath);

    public bool CanExecuteImport =>
        CanWrite
        && !IsBusy
        && HasCompletedDryRun
        && IsConfirmed
        && !string.IsNullOrWhiteSpace(SourceFolderPath);

    [RelayCommand]
    private void BrowseFolder()
    {
        if (_folderPicker is null) return;
        var picked = _folderPicker.PickFolder(
            "Chọn thư mục dữ liệu app cũ (QuanLyHangHoan)",
            string.IsNullOrWhiteSpace(SourceFolderPath) ? null : SourceFolderPath);
        if (!string.IsNullOrWhiteSpace(picked))
        {
            SourceFolderPath = picked;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunDryRun))]
    public async Task RunDryRunAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRunDryRun) return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        ImportResult = null;
        IsConfirmed = false;
        HasCompletedDryRun = false;
        StatusMessage = IsDemoMode
            ? "Đang chạy thử (dry-run) đối chiếu dữ liệu giả lập..."
            : "Đang sao chép an toàn, kiểm hash và chạy thử (dry-run)...";

        try
        {
            var report = await _importer.RunDryRunAsync(SourceFolderPath, cancellationToken);
            DryRunReport = report;
            HasCompletedDryRun = true;
            StatusMessage = $"Chạy thử hoàn tất lúc {report.GeneratedAt:HH:mm:ss}. Vui lòng kiểm tra báo cáo và tick xác nhận để bật nút Nhập thật.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DryRunReport = null;
            HasCompletedDryRun = false;
            ErrorMessage = ex.Message;
            StatusMessage = "Chạy thử thất bại. Vui lòng kiểm tra lại đường dẫn.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteImport))]
    public async Task ExecuteImportAsync(CancellationToken cancellationToken = default)
    {
        if (!CanExecuteImport) return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = IsDemoMode
            ? "Đang thực hiện nhập dữ liệu (giả lập)..."
            : "Đang nhập dữ liệu vào SQLite và đối chiếu số liệu...";

        try
        {
            var result = await _importer.ExecuteImportAsync(SourceFolderPath, cancellationToken);
            ImportResult = result;
            if (result.Success && ImportSucceeded is not null)
            {
                await ImportSucceeded.Invoke();
            }
            StatusMessage = result.Success
                ? $"Hoàn tất lúc {result.CompletedAt:HH:mm:ss}: {result.Message}"
                : $"Nhập dữ liệu không thành công: {result.Message}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Có lỗi khi nhập dữ liệu.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Reset()
    {
        SourceFolderPath = string.Empty;
        DryRunReport = null;
        ImportResult = null;
        HasCompletedDryRun = false;
        IsConfirmed = false;
        ErrorMessage = string.Empty;
        StatusMessage = "Bước 1: Chọn thư mục chứa dữ liệu app cũ, sau đó bấm Chạy thử (Dry-run) để kiểm tra trước khi nhập.";
    }
}
