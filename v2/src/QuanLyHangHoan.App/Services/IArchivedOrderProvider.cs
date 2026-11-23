using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Hợp đồng cấp dữ liệu cho màn hình Kho lưu trữ (24h).
/// T003 dùng dữ liệu giả; Claude sẽ thay bằng bản SQLite sau.
/// </summary>
public interface IArchivedOrderProvider
{
    DateTime GetCurrentTime();
    IReadOnlyList<Order> GetArchivedOrders();
}
