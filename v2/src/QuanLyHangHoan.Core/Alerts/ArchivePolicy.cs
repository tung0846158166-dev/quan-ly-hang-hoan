using System.Globalization;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Core.Alerts;

/// <summary>
/// Đơn đã nhận về kho (Status = "Hàng đã trả") và đã qua >= 24h kể từ ngày nhận thì tự dời sang kho lưu trữ
/// (chỉ đặt cờ, không xóa; v2 dùng một bảng đơn). Ngày nhận không đọc được => KHÔNG dời (không đoán).
/// Ngày nhận chỉ có ngày (không có giờ) => coi là cuối ngày, để không bao giờ dời sớm.
/// </summary>
public sealed record ArchivePolicy(TimeSpan After)
{
    public static ArchivePolicy Default { get; } = new(TimeSpan.FromHours(24));

    private static readonly string[] DateTimeFormats =
        { "dd/MM/yyyy HH:mm:ss", "d/M/yyyy H:mm:ss", "dd/MM/yyyy HH:mm", "d/M/yyyy H:mm" };
    private static readonly string[] DateOnlyFormats = { "dd/MM/yyyy", "d/M/yyyy" };

    public static bool ShouldArchive(Order o, DateTime now) => Default.Check(o, now);

    public bool Check(Order o, DateTime now)
    {
        if (o.Status != ReceiveStatus.Received) return false;
        var recv = ParseRecvDate(o.RecvDate);
        return recv is not null && now - recv.Value >= After;
    }

    public static DateTime? ParseRecvDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        var ci = CultureInfo.InvariantCulture;
        if (DateTime.TryParseExact(s, DateTimeFormats, ci, DateTimeStyles.None, out var dt)) return dt;
        if (DateTime.TryParseExact(s, DateOnlyFormats, ci, DateTimeStyles.None, out var d)) return d.Date.AddDays(1).AddSeconds(-1);
        return null;
    }
}
