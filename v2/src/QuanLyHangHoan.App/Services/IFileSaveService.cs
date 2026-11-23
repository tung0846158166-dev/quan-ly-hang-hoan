namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Dịch vụ chọn đường dẫn và ghi file (dùng cho xuất báo cáo CSV mà vẫn giữ ViewModel thuần MVVM).
/// </summary>
public interface IFileSaveService
{
    string? PickSavePath(string title, string defaultFileName, string filter);
    Task WriteTextAsync(string filePath, string content, CancellationToken cancellationToken = default);
}
