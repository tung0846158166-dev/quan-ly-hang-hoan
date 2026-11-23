using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Core.Tracking;

namespace QuanLyHangHoan.Core.Import;

public enum UpsertKind { Inserted, Updated, Ambiguous }

public sealed record UpsertResult(UpsertKind Kind, Order? Target, IReadOnlyList<Order> Candidates);

/// <summary>
/// Quy tắc 5 + 1-4: nhập lại cùng kiện thì CẬP NHẬT (không cộng lặp sản phẩm). Chỉ coi là cùng kiện khi cùng sàn,
/// cùng mã đơn gốc VÀ có ít nhất một mã vận đơn riêng trùng nhau (track/returnTrack/altTrack/trackingCodes/scanAliases).
/// Cùng mã đơn hoặc mã yêu cầu hoàn mà không trùng mã riêng => kiện khác (A/B/C). Nhiều ứng viên => Ambiguous, không đổi gì.
/// </summary>
public static class ParcelUpsert
{
    /// <summary>Các kiện đã có được coi là CÙNG kiện với <paramref name="incoming"/> (cùng sàn, cùng mã đơn, trùng ít nhất một mã riêng).</summary>
    public static IReadOnlyList<Order> FindMatches(IEnumerable<Order> existing, Order incoming)
    {
        var inCodes = TrackingResolver.OwnCodes(incoming).Where(c => c.Length > 0).ToHashSet();
        return existing.Where(e =>
                string.Equals(e.Platform.Trim(), incoming.Platform.Trim(), StringComparison.OrdinalIgnoreCase) &&
                TrackingResolver.Normalize(e.OrderId) == TrackingResolver.Normalize(incoming.OrderId) &&
                TrackingResolver.OwnCodes(e).Any(c => c.Length > 0 && inCodes.Contains(c)))
            .ToList();
    }

    public static UpsertResult Apply(IList<Order> existing, Order incoming)
    {
        var candidates = FindMatches(existing, incoming);

        if (candidates.Count == 0)
        {
            existing.Add(incoming);
            return new(UpsertKind.Inserted, incoming, Array.Empty<Order>());
        }
        if (candidates.Count > 1) return new(UpsertKind.Ambiguous, null, candidates);

        var target = candidates[0];
        Merge(target, incoming);
        return new(UpsertKind.Updated, target, candidates);
    }

    private static void Merge(Order t, Order n)
    {
        // Dữ liệu từ file nhập: ghi đè khi có giá trị.
        if (n.ReturnOrderId.Length > 0) t.ReturnOrderId = n.ReturnOrderId;
        if (n.ForwardTrack.Length > 0) t.ForwardTrack = n.ForwardTrack;
        if (n.ReturnTrack.Length > 0) t.ReturnTrack = n.ReturnTrack;
        if (n.AltTrack.Length > 0) t.AltTrack = n.AltTrack;
        if (n.OrderType.Length > 0) t.OrderType = n.OrderType;
        if (n.PlatformStatus.Length > 0) t.PlatformStatus = n.PlatformStatus;
        if (n.PreOrder.Length > 0) t.PreOrder = n.PreOrder;
        if (n.Carrier.Length > 0) t.Carrier = n.Carrier;
        if (n.ShipDate.Length > 0) t.ShipDate = n.ShipDate;
        if (n.ReturnDate.Length > 0) t.ReturnDate = n.ReturnDate;
        if (n.Deadline.Length > 0) t.Deadline = n.Deadline;
        if (n.Price != 0) t.Price = n.Price;
        if (n.Note.Length > 0) t.Note = n.Note;
        if (n.BuyerNote.Length > 0) t.BuyerNote = n.BuyerNote;
        if (n.ImportWeek.Length > 0) t.ImportWeek = n.ImportWeek;
        if (n.ImportedAt.Length > 0) t.ImportedAt = n.ImportedAt;
        if (n.IsPartialReturn) t.IsPartialReturn = true;

        // Sản phẩm: THAY bằng danh sách nhập (không cộng dồn) nếu file có sản phẩm.
        if (n.Items.Count > 0) t.Items = n.Items.Select(i => new OrderItem
        { Product = i.Product, Color = i.Color, Size = i.Size, Qty = i.Qty, Sku = i.Sku, SkuId = i.SkuId }).ToList();

        // Giữ mã lịch sử: hợp mã riêng cũ + mới, không mất mã nào.
        if (TrackingResolver.Normalize(n.Track) != TrackingResolver.Normalize(t.Track) && n.Track.Length > 0
            && !t.TrackingCodes.Any(c => TrackingResolver.Normalize(c) == TrackingResolver.Normalize(n.Track)))
            t.TrackingCodes.Add(n.Track);
        foreach (var c in n.TrackingCodes.Concat(n.ScanAliases))
            if (c.Length > 0 && !TrackingResolver.OwnCodes(t).Contains(TrackingResolver.Normalize(c)))
                t.TrackingCodes.Add(c);

        // KHÔNG đụng trường do kho nhập tay: Status, RecvDate, ReturnedAt, PhotoId, UserNote, IsWatchlist, WatchlistReason.
    }
}
