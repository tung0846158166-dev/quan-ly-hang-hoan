using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.App.Services;

public sealed class DeletedOrderRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrderId { get; set; } = string.Empty;
    public string Track { get; set; } = string.Empty;
    public string ReturnTrack { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string DeletedFrom { get; set; } = "Bảng chính";
    public string DeletedAt { get; set; } = string.Empty;
    public string DeleteReason { get; set; } = string.Empty;
    public List<OrderItem> Items { get; set; } = [];
    public Order? FullOrder { get; set; }
}

/// <summary>
/// Hợp đồng cấp dữ liệu cho màn hình Đơn đã xóa (chống nạp lại).
/// T003 dùng dữ liệu giả; Claude sẽ thay bằng bản SQLite sau.
/// </summary>
public interface IDeletedOrderProvider
{
    DateTime GetCurrentTime();
    IReadOnlyList<DeletedOrderRecord> GetDeletedOrders();
}
