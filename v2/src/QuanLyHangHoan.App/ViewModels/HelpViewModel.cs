using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;

namespace QuanLyHangHoan.App.ViewModels;

/// <param name="TargetKey">Khóa màn hình để nút "Đi tới" chuyển sang.</param>
public sealed record HelpStep(int Number, string Title, string Hint, bool Done, string TargetKey)
{
    public string Mark => Done ? "✓" : "○";
}

public sealed record ShortcutRow(string Keys, string Meaning);

/// <summary>
/// Màn "Trợ giúp": 5 bước bắt đầu nhanh (✓/○ tự tính từ trạng thái thật, chỉ gợi ý, không làm thay), khối CHẨN ĐOÁN có nút
/// "Sao chép tất cả" để dán cho người hỗ trợ (không chứa dữ liệu nhạy cảm), mở thư mục dữ liệu/sao lưu, phím tắt, phiên bản.
/// </summary>
public sealed partial class HelpViewModel : ObservableObject
{
    private readonly IDiagnosticsProvider _diag;
    private readonly IBackupController _backup;
    private readonly IClipboardService? _clipboard;
    private readonly Func<Task<IReadOnlyList<HelpStep>>> _steps;
    private readonly Func<string> _integrationStatus;
    private readonly Action<string>? _navigate;

    public HelpViewModel(
        IDiagnosticsProvider diag,
        IBackupController backup,
        IClipboardService? clipboard,
        Func<Task<IReadOnlyList<HelpStep>>> steps,
        Func<string> integrationStatus,
        Action<string>? navigate)
    {
        _diag = diag; _backup = backup; _clipboard = clipboard;
        _steps = steps; _integrationStatus = integrationStatus; _navigate = navigate;
    }

    public bool IsDemoMode { get; set; }
    public ObservableCollection<HelpStep> Steps { get; } = new();
    public string AppVersion => _diag.AppVersion;
    public string VersionText => $"Phiên bản app: {_diag.AppVersion}";

    /// <summary>Chỉ liệt kê các phím CÓ THẬT trong app.</summary>
    public IReadOnlyList<ShortcutRow> Shortcuts { get; } = new ShortcutRow[]
    {
        new("Ctrl+K", "Tìm toàn cục theo mã vận đơn, mã đơn, mã hoàn, tên sản phẩm"),
        new("Enter", "Xem chi tiết đơn đang chọn"),
        new("Delete", "Xóa đơn đang chọn (có xác nhận, hoàn tác được)"),
        new("Esc", "Đóng cửa sổ chi tiết / ô tìm"),
    };

    [ObservableProperty] private string _reportText = "";
    [ObservableProperty] private string _copyStatus = "";
    [ObservableProperty] private bool _isBusy;

    public bool ReportHasNoSecrets => !ReportText.Contains("script.google.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>Tính lại các bước và dựng lại báo cáo chẩn đoán.</summary>
    private Task? _refreshTask;

    [RelayCommand]
    public Task RefreshAsync()
    {
        // Đang làm mới (ví dụ do vừa mở trang): trả lại chính lượt đó để người gọi chờ được kết quả, không bỏ qua âm thầm.
        if (_refreshTask is { IsCompleted: false }) return _refreshTask;
        return _refreshTask = RefreshCoreAsync();
    }

    private async Task RefreshCoreAsync()
    {
        IsBusy = true;
        try
        {
            var steps = await _steps();
            Steps.Clear();
            foreach (var s in steps) Steps.Add(s);
            var sb = new StringBuilder(await Task.Run(_diag.BuildReport));
            sb.AppendLine().AppendLine("-- Trạng thái các đường nạp đơn / đồng bộ --").Append(_integrationStatus());
            ReportText = sb.ToString().TrimEnd();
            OnPropertyChanged(nameof(ReportHasNoSecrets));
            CopyStatus = "";
        }
        catch (Exception ex) { ReportText = "Không dựng được báo cáo chẩn đoán: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void CopyAll()
    {
        if (string.IsNullOrWhiteSpace(ReportText)) { CopyStatus = "Chưa có báo cáo."; return; }
        _clipboard?.SetText(ReportText);
        CopyStatus = "Đã sao chép, dán vào tin nhắn gửi người hỗ trợ.";
    }

    [RelayCommand] private void GoToStep(string? key) { if (!string.IsNullOrWhiteSpace(key)) _navigate?.Invoke(key); }
    [RelayCommand] private void OpenDataFolder() => _diag.OpenDataFolder();
    [RelayCommand] private void OpenBackupFolder() => _backup.OpenFolder();
}
