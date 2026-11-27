namespace QuanLyHangHoan.Core.Models;

/// <summary>
/// Đơn NGOÀI SÀN (bán qua Facebook, Zalo, tại shop...) do chủ shop tự tạo (chủ dự án 27/11/2026). Cùng quy ước với cloud (neworder.ts):
/// sàn "Ngoài sàn", dấu <see cref="Order.ImportWeek"/> = <see cref="Week"/>, kênh bán ghi ở cột trạng thái sàn ("Kênh: Facebook").
/// KHÔNG lưu thông tin người mua (quy tắc cloud).
/// </summary>
public static class OffPlatform
{
    public const string Name = "Ngoài sàn";
    public const string Week = "Đơn ngoài sàn";
    public const string DefaultNote = "Đơn ngoài sàn tạo tay";
    public const string ChannelPrefix = "Kênh: ";

    /// <summary>Loại đơn chọn được; loại khác -> phần tử đầu.</summary>
    public static readonly IReadOnlyList<string> OrderTypes = ["Trả hàng/Hoàn tiền", "Đổi hàng", "Giao không thành công"];

    /// <summary>Kênh bán gợi ý (vẫn gõ được kênh khác).</summary>
    public static readonly IReadOnlyList<string> Channels = ["Salework", "Facebook", "Zalo", "Tại shop", "Website", "Instagram", "Khác"];

    public static bool Is(string? platform) => string.Equals((platform ?? "").Trim(), Name, StringComparison.OrdinalIgnoreCase);
    public static bool Is(Order o) => Is(o.Platform);

    public static string NormalizeOrderType(string? t) => OrderTypes.Contains((t ?? "").Trim()) ? t!.Trim() : OrderTypes[0];
    public static string ChannelStatus(string? channel) => ChannelPrefix + (string.IsNullOrWhiteSpace(channel) ? "Khác" : channel.Trim());

    /// <summary>Kênh bán đọc lại từ cột trạng thái sàn ("Kênh: Zalo" -> "Zalo").</summary>
    public static string ChannelOf(Order o) =>
        (o.PlatformStatus ?? "").StartsWith(ChannelPrefix, StringComparison.Ordinal) ? o.PlatformStatus[ChannelPrefix.Length..].Trim() : "";

    private const string CodeAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>Mã tự sinh khi không có mã vận đơn: "NS" + yyMMdd + 4 ký tự (bỏ 0/O/1/I cho dễ đọc). Giống cloud.</summary>
    public static string NewCode(DateTime now, Random? rng = null)
    {
        var r = rng ?? Random.Shared;
        var tail = new char[4];
        for (var i = 0; i < tail.Length; i++) tail[i] = CodeAlphabet[r.Next(CodeAlphabet.Length)];
        return "NS" + now.ToString("yyMMdd", System.Globalization.CultureInfo.InvariantCulture) + new string(tail);
    }
}
