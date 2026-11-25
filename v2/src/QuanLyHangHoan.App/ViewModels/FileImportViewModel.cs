using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Import;

namespace QuanLyHangHoan.App.ViewModels;

public sealed record ImportPriceOption(string Value, string Label);

public sealed record ImportProductModeOption(ImportProductMode Mode, string Label);

public sealed record SelectedImportFileItem(string FullPath, string FileName, string SizeText);

public sealed record ImportResultFileRow(
    string Name,
    string KindLabel,
    bool IsUnknown,
    int Rows,
    string ErrorText,
    bool HasError);

public sealed partial class FileImportViewModel : ObservableObject
{
    private readonly IFileImportController _controller;
    private readonly IImportFilePickerService _filePickerService;
    private readonly IConfirmationService _confirmationService;
    private readonly Action<Action> _dispatchToUi;
    private readonly WriteGate _writeGate;

    public FileImportViewModel(
        IFileImportController controller,
        IImportFilePickerService? filePickerService = null,
        IConfirmationService? confirmationService = null,
        Action<Action>? dispatchToUi = null,
        WriteGate? writeGate = null)
    {
        _controller = controller;
        _filePickerService = filePickerService ?? new WindowsImportFilePickerService();
        _confirmationService = confirmationService ?? new WindowsConfirmationService();
        _dispatchToUi = dispatchToUi ?? (a => a());
        _writeGate = writeGate ?? WriteGate.AlwaysOpen;
        if (!ReferenceEquals(_writeGate, WriteGate.AlwaysOpen))
        {
            _writeGate.Changed += OnWriteGateChanged;
        }

        PriceModeOptions =
        [
            new ImportPriceOption("tong_gia_tri", "Tổng giá trị"),
            new ImportPriceOption("sau_chiet_khau", "Sau chiết khấu"),
            new ImportPriceOption("gia_salework", "Giá Salework"),
            new ImportPriceOption("doanh_thu_uoc_tinh", "Doanh thu ước tính")
        ];

        ProductModeOptions =
        [
            new ImportProductModeOption(ImportProductMode.Supplement, "Nhập bổ sung - giữ sản phẩm không có trong file mới"),
            new ImportProductModeOption(ImportProductMode.Replace, "Thay toàn bộ sản phẩm của từng kiện có trong file")
        ];

        _selectedPriceMode = PriceModeOptions[0];
        _selectedProductMode = ProductModeOptions[0];
    }

    public IReadOnlyList<ImportPriceOption> PriceModeOptions { get; }
    public IReadOnlyList<ImportProductModeOption> ProductModeOptions { get; }
    public ObservableCollection<SelectedImportFileItem> SelectedFiles { get; } = new();
    public ObservableCollection<ImportResultFileRow> ResultFiles { get; } = new();

    public bool IsDemoMode => _controller.IsDemoMode;

    public string FixedNoteText =>
        "App chỉ ĐỌC file, không xóa/sửa/di chuyển file. Cột thông tin khách hàng (tên, SĐT, địa chỉ) bị bỏ ngay khi đọc. Chỉ đọc .xlsx và .csv.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    [NotifyPropertyChangedFor(nameof(CanSelectFiles))]
    [NotifyPropertyChangedFor(nameof(ImportButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ImportFilesCommand))]
    private bool _isImporting;

    [ObservableProperty]
    private ImportPriceOption _selectedPriceMode;

    [ObservableProperty]
    private ImportProductModeOption _selectedProductMode;

    [ObservableProperty]
    private bool _onlyReturns = true;

    [ObservableProperty]
    private bool _skipUnshippedCancel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummary))]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAmbiguousWarning))]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    private string _ambiguousWarningText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    private string _errorMessage = string.Empty;

    public bool CanWrite => _writeGate.CanWrite;
    public string? WriteDisabledTooltip => _writeGate.TooltipReason;

    private void OnWriteGateChanged()
    {
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(WriteDisabledTooltip));
        OnPropertyChanged(nameof(CanImport));
        ImportFilesCommand.NotifyCanExecuteChanged();
    }

    public bool HasSelectedFiles => SelectedFiles.Count > 0;
    public bool CanImport => CanWrite && SelectedFiles.Count > 0 && !IsImporting;
    public bool CanSelectFiles => !IsImporting;
    public string ImportButtonText => IsImporting ? "Đang nạp..." : "Nạp";
    public bool HasSummary => !string.IsNullOrWhiteSpace(SummaryText);
    public bool HasAmbiguousWarning => !string.IsNullOrWhiteSpace(AmbiguousWarningText);
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasResults => ResultFiles.Count > 0 || HasSummary || HasAmbiguousWarning || HasError;

    [RelayCommand]
    public void BrowseFiles()
    {
        if (!CanSelectFiles) return;
        var picked = _filePickerService.PickFiles();
        AddFiles(picked);
    }

    public void AddFiles(IEnumerable<string>? filePaths)
    {
        if (filePaths is null) return;

        var added = false;
        foreach (var rawPath in filePaths)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) continue;
            var fullPath = rawPath.Trim();
            if (SelectedFiles.Any(f => string.Equals(f.FullPath, fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var fileName = Path.GetFileName(fullPath);
            var sizeText = FormatFileSize(fullPath);
            SelectedFiles.Add(new SelectedImportFileItem(fullPath, fileName, sizeText));
            added = true;
        }

        if (added)
        {
            NotifySelectedFilesChanged();
        }
    }

    [RelayCommand]
    public void RemoveFile(SelectedImportFileItem? item)
    {
        if (item is null || IsImporting) return;
        if (SelectedFiles.Remove(item))
        {
            NotifySelectedFilesChanged();
        }
    }

    [RelayCommand]
    public void ClearFiles()
    {
        if (IsImporting || SelectedFiles.Count == 0) return;
        SelectedFiles.Clear();
        NotifySelectedFilesChanged();
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    public async Task ImportFilesAsync()
    {
        if (!CanImport) return;

        var count = SelectedFiles.Count;
        var confirmed = _confirmationService.Confirm(
            "Xác nhận nạp file Excel",
            $"Sẽ nạp {count} file vào bảng đơn. Dữ liệu đã có chỉ được cập nhật, không bị xóa.");
        if (!confirmed) return;

        IsImporting = true;
        ErrorMessage = string.Empty;
        SummaryText = string.Empty;
        AmbiguousWarningText = string.Empty;
        ResultFiles.Clear();
        OnPropertyChanged(nameof(HasResults));

        try
        {
            var paths = SelectedFiles.Select(f => f.FullPath).ToList();
            var options = new TikTokMapOptions
            {
                OnlyReturns = OnlyReturns,
                SkipUnshippedCancel = SkipUnshippedCancel,
                PriceMode = SelectedPriceMode?.Value ?? "tong_gia_tri",
                Today = DateTime.Now
            };
            var mode = SelectedProductMode?.Mode ?? ImportProductMode.Supplement;

            var result = await _controller.ImportAsync(paths, options, mode);
            _dispatchToUi(() => ApplyResult(result));
        }
        catch (Exception ex)
        {
            _dispatchToUi(() =>
            {
                ErrorMessage = $"Lỗi nạp file: {ex.Message}";
                OnPropertyChanged(nameof(HasResults));
            });
        }
        finally
        {
            _dispatchToUi(() => IsImporting = false);
        }
    }

    private void ApplyResult(FileImportResult result)
    {
        ResultFiles.Clear();
        foreach (var item in result.Files)
        {
            var kindLabel = item.Kind switch
            {
                FileKind.TikTok => "TikTok / Salework",
                FileKind.Shopee => "Shopee",
                _ => "Không nhận ra"
            };

            ResultFiles.Add(new ImportResultFileRow(
                item.Name,
                kindLabel,
                item.Kind == FileKind.Unknown,
                item.Rows,
                item.Error ?? string.Empty,
                !string.IsNullOrWhiteSpace(item.Error)));
        }

        if (!result.Ok)
        {
            ErrorMessage = string.IsNullOrWhiteSpace(result.Message)
                ? "Nạp file thất bại. Dữ liệu trong bảng không thay đổi."
                : result.Message;
            SummaryText = string.Empty;
            AmbiguousWarningText = string.Empty;
        }
        else
        {
            ErrorMessage = string.Empty;
            var enriched = (result.TikTok?.EnrichedArchived ?? 0) + (result.Shopee?.EnrichedArchived ?? 0);
            SummaryText = enriched > 0
                ? $"Tổng kết: thêm {result.Added}, cập nhật {result.Updated}, bổ sung lưu trữ {enriched}"
                : $"Tổng kết: thêm {result.Added}, cập nhật {result.Updated}";

            AmbiguousWarningText = result.Ambiguous > 0
                ? $"{result.Ambiguous} dòng khớp nhiều kiện nên chưa nạp, cần kiểm tra thủ công"
                : string.Empty;
        }

        OnPropertyChanged(nameof(HasResults));
    }

    private void NotifySelectedFilesChanged()
    {
        OnPropertyChanged(nameof(HasSelectedFiles));
        OnPropertyChanged(nameof(CanImport));
        ImportFilesCommand.NotifyCanExecuteChanged();
    }

    private static string FormatFileSize(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath)) return "—";
            var bytes = new FileInfo(fullPath).Length;
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F2} MB";
        }
        catch
        {
            return "—";
        }
    }
}
