namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Chuẩn hóa tên sàn TMĐT trên toàn bộ App (T004/T006: dùng nhãn ngắn "TikTok").
/// </summary>
public static class PlatformNames
{
    public const string Shopee = "Shopee";
    public const string TikTok = "TikTok";
    public const string Lazada = "Lazada";
}

/// <summary>
/// Chuẩn hóa tên loại đơn (orderType) theo dữ liệu thực tế của Shopee &amp; TikTok.
/// </summary>
public static class OrderTypeNames
{
    public const string Cancelled = "Đơn hủy";
    public const string ReturnRefund = "Trả hàng/Hoàn tiền";
    public const string FailedDelivery = "Giao không thành công";
    public const string Lost = "THẤT THOÁT";
    public const string RefundOnly = "Chỉ hoàn tiền";

    public static IReadOnlyList<string> ShopeeReturnTypes { get; } =
    [
        ReturnRefund,
        FailedDelivery
    ];

    public static IReadOnlyList<string> TikTokTypes { get; } =
    [
        Lost,
        ReturnRefund,
        FailedDelivery,
        RefundOnly
    ];

    public static bool IsCancelled(string? orderType)
        => string.Equals(orderType?.Trim(), Cancelled, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Quy tắc Shopee "Hoàn hàng": mọi đơn Shopee KHÔNG phải "Đơn hủy"
    /// (bao gồm "Trả hàng/Hoàn tiền" và "Giao không thành công").
    /// </summary>
    public static bool IsShopeeReturn(string? orderType)
        => !IsCancelled(orderType);
}
