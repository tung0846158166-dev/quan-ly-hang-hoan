namespace QuanLyHangHoan.Core.Models;

/// <summary>
/// Đơn "mới tạo" (tạo tay trên máy tính hoặc quét tạo từ điện thoại) nằm THẲNG ở bảng chính nhưng được đánh dấu để hiện ở màn "Bảng đơn mới":
/// dấu là <see cref="Order.ImportWeek"/> = <see cref="Week"/>. Đơn do điện thoại tạo trước khi có dấu này nhận ra qua ghi chú mặc định.
/// </summary>
public static class NewOrderMarker
{
    public const string Week = "Đơn mới tạo";
    public const string LegacyPhoneNote = "Đơn mới tạo từ máy quét điện thoại";

    public static bool IsNewlyCreated(Order o) =>
        string.Equals(o.ImportWeek, Week, StringComparison.Ordinal)
        || (o.Note ?? "").StartsWith(LegacyPhoneNote, StringComparison.Ordinal);
}
