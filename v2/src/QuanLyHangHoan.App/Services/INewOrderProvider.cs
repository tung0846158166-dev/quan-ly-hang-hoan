using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.App.Services;

public sealed class NewOrderRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ScannedAt { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Track { get; set; } = string.Empty;
    public string Carrier { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string PhotoId { get; set; } = string.Empty;
    public bool IsSelectedForImport { get; set; }
    public Order? FullOrder { get; set; }
}

/// <summary>
/// Hợp đồng cấp dữ liệu cho màn hình Bảng đơn mới (từ điện thoại).
/// T003 dùng dữ liệu giả; Claude sẽ thay bằng bản SQLite/SyncTransport sau.
/// </summary>
public interface INewOrderProvider
{
    DateTime GetCurrentTime();
    IReadOnlyList<NewOrderRecord> GetNewOrders();
}
