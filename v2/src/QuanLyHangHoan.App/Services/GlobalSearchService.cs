using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Core.Tracking;

namespace QuanLyHangHoan.App.Services;

public sealed record GlobalSearchResultItem(
    Order Source,
    string OrderId,
    string DisplayTracking,
    string Platform,
    string ItemsSummary,
    string Status,
    string LocationLabel,
    string TargetNavKey,
    bool IsExactCodeMatch,
    int MatchRank,
    int SourceBucketOrder,
    int OriginalIndex);

public sealed record GlobalSearchOutput(
    IReadOnlyList<GlobalSearchResultItem> Items,
    int TotalMatches,
    int RemainingCount,
    bool HasOverflow,
    string OverflowMessage);

/// <summary>
/// Tìm kiếm toàn cục theo từng KIỆN (không bao giờ gộp kiện A/B/C cùng mã đơn).
/// Hỗ trợ tìm mã và chữ tiếng Việt có/không dấu; xếp khớp chính xác mã riêng của kiện lên đầu.
/// Mã giao đi dùng chung không bao giờ được gắn nhãn "Khớp mã" (quy tắc 3).
/// </summary>
public static class GlobalSearchService
{
    public const int MaxResults = 50;

    public static GlobalSearchOutput Search(
        string? query,
        IReadOnlyList<Order> activeOrders,
        IReadOnlyList<Order> archivedOrders,
        IReadOnlyList<DeletedOrderRecord> deletedOrders)
    {
        var normText = AlertThresholds.NormalizeForSearch(query).Trim();
        if (normText.Length == 0)
        {
            return new GlobalSearchOutput(Array.Empty<GlobalSearchResultItem>(), 0, 0, false, string.Empty);
        }

        var normCode = TrackingResolver.Normalize(query);
        var matches = new List<GlobalSearchResultItem>();

        ScanBucket(activeOrders, normText, normCode, bucketOrder: 0, isArchived: false, isDeleted: false, matches);
        ScanBucket(archivedOrders, normText, normCode, bucketOrder: 1, isArchived: true, isDeleted: false, matches);

        for (var i = 0; i < deletedOrders.Count; i++)
        {
            var del = deletedOrders[i];
            var order = del.FullOrder ?? SynthesizeDeletedOrder(del);
            if (TryMatchOrder(order, normText, normCode, bucketOrder: 2, originalIndex: i, isArchived: false, isDeleted: true, out var item))
            {
                matches.Add(item);
            }
        }

        matches.Sort((a, b) =>
        {
            var exactCmp = b.IsExactCodeMatch.CompareTo(a.IsExactCodeMatch);
            if (exactCmp != 0) return exactCmp;
            var rankCmp = a.MatchRank.CompareTo(b.MatchRank);
            if (rankCmp != 0) return rankCmp;
            var bucketCmp = a.SourceBucketOrder.CompareTo(b.SourceBucketOrder);
            if (bucketCmp != 0) return bucketCmp;
            return a.OriginalIndex.CompareTo(b.OriginalIndex);
        });

        var total = matches.Count;
        var top = total <= MaxResults ? matches : matches.Take(MaxResults).ToList();
        var remaining = Math.Max(0, total - MaxResults);
        var hasOverflow = remaining > 0;
        var overflowMsg = hasOverflow ? $"Còn {remaining:N0} kết quả, hãy gõ thêm" : string.Empty;

        return new GlobalSearchOutput(top, total, remaining, hasOverflow, overflowMsg);
    }

    private static void ScanBucket(
        IReadOnlyList<Order> orders,
        string normText,
        string normCode,
        int bucketOrder,
        bool isArchived,
        bool isDeleted,
        List<GlobalSearchResultItem> matches)
    {
        for (var i = 0; i < orders.Count; i++)
        {
            if (TryMatchOrder(orders[i], normText, normCode, bucketOrder, i, isArchived, isDeleted, out var item))
            {
                matches.Add(item);
            }
        }
    }

    private static bool TryMatchOrder(
        Order order,
        string normText,
        string normCode,
        int bucketOrder,
        int originalIndex,
        bool isArchived,
        bool isDeleted,
        out GlobalSearchResultItem item)
    {
        var isExactOwn = false;
        var matchRank = 3;

        if (normCode.Length > 0)
        {
            if (TrackingResolver.OwnCodes(order).Any(c => c.Length > 0 && c == normCode))
            {
                isExactOwn = true;
                matchRank = 0;
            }
            else if (TrackingResolver.Normalize(order.ReturnOrderId) == normCode)
            {
                isExactOwn = true;
                matchRank = 1;
            }
            else if (TrackingResolver.Normalize(order.OrderId) == normCode
                     || TrackingResolver.Normalize(order.ForwardTrack) == normCode)
            {
                // Mã đơn gốc và mã giao đi dùng chung KHÔNG được coi là khớp chính xác (quy tắc 3)
                matchRank = 2;
            }
        }

        var itemsSummary = BuildItemsSummary(order.Items);
        var blob = BuildSearchBlob(order, itemsSummary);
        if (matchRank == 3 && !blob.Contains(normText, StringComparison.Ordinal))
        {
            item = null!;
            return false;
        }

        var (locationLabel, navKey) = ResolveLocation(order, isArchived, isDeleted);
        var displayTracking = BuildDisplayTracking(order);

        item = new GlobalSearchResultItem(
            order,
            order.OrderId ?? string.Empty,
            displayTracking,
            order.Platform ?? string.Empty,
            itemsSummary,
            string.IsNullOrWhiteSpace(order.Status) ? "—" : order.Status,
            locationLabel,
            navKey,
            isExactOwn,
            matchRank,
            bucketOrder,
            originalIndex);
        return true;
    }

    public static (string LocationLabel, string TargetNavKey) ResolveLocation(Order order, bool isArchived, bool isDeleted)
    {
        if (isDeleted) return ("Đã xóa", "deleted");
        if (isArchived) return ("Kho lưu trữ", "archive");

        if (order.Platform.Equals(PlatformNames.Shopee, StringComparison.OrdinalIgnoreCase))
        {
            return OrderTypeNames.IsCancelled(order.OrderType)
                ? ("Shopee - Đơn hủy", "shopee_cancelled")
                : ("Shopee - Hoàn hàng", "shopee_return");
        }

        if (order.Platform.Equals(PlatformNames.TikTok, StringComparison.OrdinalIgnoreCase))
        {
            return ("TikTok - Hoàn hàng", "tiktok_return");
        }

        return ("Bảng chính", "shopee_return");
    }

    private static string BuildDisplayTracking(Order o)
    {
        var ret = !string.IsNullOrWhiteSpace(o.ReturnTrack) ? o.ReturnTrack : o.Track;
        if (!string.IsNullOrWhiteSpace(ret)) return ret;
        if (!string.IsNullOrWhiteSpace(o.AltTrack)) return o.AltTrack;
        if (!string.IsNullOrWhiteSpace(o.ForwardTrack)) return $"Giao: {o.ForwardTrack}";
        return "Chưa có MVĐ";
    }

    private static string BuildItemsSummary(IReadOnlyList<OrderItem> items)
    {
        if (items is null || items.Count == 0) return "Chưa có sản phẩm";
        var first = items[0];
        var variant = string.Join("/", new[] { first.Color, first.Size }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var firstText = string.IsNullOrWhiteSpace(variant)
            ? $"{first.Product} (x{first.Qty})"
            : $"{first.Product} ({variant} x{first.Qty})";
        return items.Count == 1 ? firstText : $"{firstText} +{items.Count - 1} món";
    }

    private static string BuildSearchBlob(Order o, string itemsSummary)
    {
        var extraCodes = string.Join(' ', o.TrackingCodes.Concat(o.ScanAliases));
        var itemDetails = o.Items.Count > 0
            ? string.Join(' ', o.Items.Select(i => $"{i.Product} {i.Color} {i.Size} {i.Sku}"))
            : string.Empty;

        return AlertThresholds.NormalizeForSearch(
            $"{o.OrderId} {o.ReturnOrderId} {o.Track} {o.ReturnTrack} {o.AltTrack} {o.ForwardTrack} {extraCodes} " +
            $"{o.Platform} {o.OrderType} {o.Carrier} {o.Status} {o.PlatformStatus} " +
            $"{itemsSummary} {itemDetails} {o.Note} {o.UserNote} {o.BuyerNote} {o.WatchlistReason}");
    }

    private static Order SynthesizeDeletedOrder(DeletedOrderRecord del) => new()
    {
        Id = del.Id,
        OrderId = del.OrderId,
        Track = del.Track,
        ReturnTrack = del.ReturnTrack,
        Platform = del.Platform,
        Note = del.DeleteReason,
        Items = del.Items?.ToList() ?? new List<OrderItem>(),
        Status = ReceiveStatus.NotReturned
    };
}
