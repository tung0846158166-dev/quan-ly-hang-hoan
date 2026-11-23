namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Dịch vụ chọn đường dẫn và ghi file (dùng cho xuất báo cáo CSV mà vẫn giữ ViewModel thuần MVVM).
/// </summary>
public interface IFileSaveService
{
    string? PickSavePath(string title, string defaultFileName, string filter);
    Task WriteTextAsync(string filePath, string content, CancellationToken cancellationToken = default);

    /// <summary>Ghi tệp nhị phân (xlsx). Bản mặc định chưa hỗ trợ: bản thật ghi tạm rồi đổi tên nên không để lại tệp dở.</summary>
    Task WriteBytesAsync(string filePath, byte[] content, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Dịch vụ lưu tệp này chưa hỗ trợ ghi tệp nhị phân.");
}
