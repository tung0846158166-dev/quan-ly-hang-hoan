using System.Globalization;
using System.Text;
using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Nguồn duy nhất cho ngưỡng cảnh báo Vàng / Đỏ của giao diện.
/// Chủ dự án chỉ cần sửa 2 hằng số ở đây (hoặc truyền AlertPolicy mới) khi đổi quy tắc.
/// </summary>
public static class AlertThresholds
{
    public const double YellowHours = 12;
    public const double RedHours = 24;

    public static AlertPolicy MainPolicy { get; } =
        new(TimeSpan.FromHours(YellowHours), TimeSpan.FromHours(RedHours));

    // Chờ chủ dự án quyết: quy tắc quá hạn riêng cho Khiếu nại (tạm giữ 48h / 72h theo app cũ, không tự suy diễn).
    public const double ClaimYellowHours = 48;
    public const double ClaimRedHours = 72;

    public static AlertPolicy ClaimPolicy { get; } =
        new(TimeSpan.FromHours(ClaimYellowHours), TimeSpan.FromHours(ClaimRedHours));

    private static readonly string[] DateFormats =
    [
        "dd/MM/yyyy HH:mm:ss",
        "dd/MM/yyyy HH:mm",
        "dd/MM/yyyy",
        "d/M/yyyy HH:mm:ss",
        "d/M/yyyy HH:mm",
        "d/M/yyyy",
        "HH:mm:ss dd/MM/yyyy",
        "HH:mm dd/MM/yyyy",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd"
    ];

    public static bool IsOrderReceivedOrDone(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        return status.Equals(ReceiveStatus.Received, StringComparison.OrdinalIgnoreCase)
            || status.Equals("Đã nhận", StringComparison.OrdinalIgnoreCase)
            || status.Equals(ReceiveStatus.NotNeeded, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsOrderPending(string? status)
    {
        if (IsOrderReceivedOrDone(status)) return false;
        if (string.Equals(status, ReceiveStatus.Lost, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(status, "Hư hỏng", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    public static DateTime? TryParseVietnamDateTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();

        if (DateTime.TryParseExact(trimmed, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var exact))
        {
            return exact;
        }

        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    public static AlertLevel EvaluateOrder(Order order, DateTime now, AlertPolicy? policy = null)
    {
        var activePolicy = policy ?? MainPolicy;
        if (IsOrderReceivedOrDone(order.Status))
        {
            return AlertLevel.Done;
        }

        if (string.Equals(order.Status, ReceiveStatus.Lost, StringComparison.OrdinalIgnoreCase)
            || string.Equals(order.Status, "Hư hỏng", StringComparison.OrdinalIgnoreCase))
        {
            return AlertLevel.None;
        }

        var arrivedAt = TryParseVietnamDateTime(order.ReturnedAt);
        return activePolicy.Evaluate(arrivedAt, received: false, now);
    }

    public static string NormalizeForSearch(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark) continue;

            sb.Append(ch switch
            {
                'đ' or 'Đ' => 'd',
                _ => char.ToLowerInvariant(ch)
            });
        }

        return sb.ToString();
    }
}
