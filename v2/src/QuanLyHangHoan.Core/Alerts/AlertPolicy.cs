namespace QuanLyHangHoan.Core.Alerts;

public enum AlertLevel { None, Safe, Yellow, Red, Done }

/// <summary>
/// Chính sách cảnh báo cấu hình được. Mặc định theo bảng chính app cũ v1.9.68:
/// Vàng sau 12h, Đỏ sau 24h kể từ lúc hàng về chưa nhận. (Bảng hủy 3 ngày, khiếu nại 72h
/// là chính sách riêng - chủ dự án chưa quyết định thống nhất, nên tách thành tham số.)
/// </summary>
public sealed record AlertPolicy(TimeSpan Yellow, TimeSpan Red)
{
    public static AlertPolicy MainTable { get; } = new(TimeSpan.FromHours(12), TimeSpan.FromHours(24));
    public static AlertPolicy CancelledTable { get; } = new(TimeSpan.FromDays(2), TimeSpan.FromDays(3));
    public static AlertPolicy ClaimOverdue { get; } = new(TimeSpan.FromHours(48), TimeSpan.FromHours(72));

    /// <param name="arrivedAt">Lúc hàng về shop (null nếu chưa về).</param>
    /// <param name="received">Đã nhận tại kho hoặc không cần nhận.</param>
    public AlertLevel Evaluate(DateTime? arrivedAt, bool received, DateTime now)
    {
        if (received) return AlertLevel.Done;
        if (arrivedAt is null) return AlertLevel.None;
        var elapsed = now - arrivedAt.Value;
        if (elapsed >= Red) return AlertLevel.Red;
        if (elapsed >= Yellow) return AlertLevel.Yellow;
        return AlertLevel.Safe;
    }
}
