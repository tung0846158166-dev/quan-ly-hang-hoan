using System.Text.RegularExpressions;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Core.Import;

public enum OtherWhere { Archive, Deleted }

/// <summary>Đơn đang ở "bảng khác" (kho lưu trữ / đã xóa) khi nhập: dùng để chống tạo lại và để làm giàu dữ liệu.</summary>
public sealed record OtherEntry(Order Order, OtherWhere Where);

/// <param name="OrderConflict">true = khớp đúng MỘT kiện theo mã vận đơn nhưng kiện đó thuộc MỘT ĐƠN KHÁC (mã đơn thật khác nhau): mã dùng chung giữa nhiều đơn, không gộp, cần kiểm tra thủ công.</param>
public sealed record AmbiguousParcel(string Platform, string OrderId, string Track, string ReturnOrderId, int Candidates, bool OrderConflict = false);

public sealed class MergeOutcome
{
    public List<Order> Added { get; } = new();
    /// <summary>Đơn đang ở bảng chính được cập nhật (đối tượng đã sửa tại chỗ).</summary>
    public List<Order> Updated { get; } = new();
    /// <summary>Đơn ở kho lưu trữ được làm giàu thông tin (không tạo dòng mới, không đụng trạng thái nhận/ảnh).</summary>
    public List<Order> EnrichedArchived { get; } = new();
    public int SkippedArchivedUnchanged { get; set; }
    public int SkippedDeleted { get; set; }
    /// <summary>v2 KHÁC app cũ: khớp NHIỀU kiện thì báo và không đổi gì (app cũ tạo thêm một dòng mới rồi dọn trùng sau).</summary>
    public List<AmbiguousParcel> Ambiguous { get; } = new();
    public int Enriched { get; set; }
}

/// <summary>
/// Đẩy các kiện đã ánh xạ vào bảng đơn: đúng thứ tự quyết định của app cũ (ingestTikTokOnlyRecords, nhánh "đẩy ra bảng chính"):
/// 1) khớp ĐÚNG MỘT kiện ở bảng chính -> cập nhật; 2) dòng cũ cùng mã đơn chưa có mã vận đơn nào -> điền mã vào đó;
/// 3) đã ở kho lưu trữ -> chỉ làm giàu; đã xóa -> bỏ qua (chống nạp lại); 4) còn lại -> dòng mới.
/// </summary>
public static class TikTokMerger
{
    private static string Up(string? s) => (s ?? "").Trim().ToUpperInvariant();

    /// <summary>Mã đơn "thật": không rỗng và KHÔNG chỉ là bản sao của mã vận đơn (đơn tạo từ máy quét điện thoại/Salework dùng mã vận đơn làm mã đơn tạm).</summary>
    private static string RealOrderId(Order o)
    {
        var id = Up(o.OrderId);
        if (id.Length == 0) return "";
        if (id == Up(o.Track) || id == Up(o.ReturnTrack) || id == Up(o.ForwardTrack) || id == Up(o.AltTrack)) return "";
        return id;
    }

    /// <summary>
    /// v2 KHÁC app cũ có chủ ý (bất biến 1-3): app cũ coi hai kiện là MỘT khi cùng mã vận đơn và ghi đè mã đơn của dòng cũ,
    /// làm một đơn thật biến mất. Nay nếu khớp theo mã nhưng hai bên có mã đơn thật KHÁC nhau thì báo xung đột và không đổi gì.
    /// </summary>
    private static bool OrderIdConflict(Order existing, Order incoming)
    {
        var a = RealOrderId(existing);
        var b = RealOrderId(incoming);
        return a.Length > 0 && b.Length > 0 && a != b;
    }

    /// <summary>orderTrackingCodes: track, altTrack, returnTrack, trackingCodes (nguyên văn viết hoa và bản làm sạch).</summary>
    private static HashSet<string> OtherCodes(Order o)
    {
        var set = new HashSet<string>();
        foreach (var v in new[] { o.Track, o.AltTrack, o.ReturnTrack }.Concat(o.TrackingCodes))
        {
            var t = Up(v);
            if (t.Length == 0) continue;
            set.Add(t);
            var c = LegacyText.CleanTrackingCode(t);
            if (c.Length > 0) set.Add(c);
        }
        return set;
    }

    private static Dictionary<string, OtherWhere> BuildIndex(IEnumerable<OtherEntry> others)
    {
        var map = new Dictionary<string, OtherWhere>();
        void Put(string k, OtherWhere w) { if (k.Length > 0 && !map.ContainsKey(k)) map[k] = w; }
        foreach (var e in others)
        {
            var tr = OtherCodes(e.Order);
            foreach (var c in tr) Put("T:" + c, e.Where);
            var oid = Up(e.Order.OrderId);
            if (oid.Length > 0) { Put("O:" + oid, e.Where); if (tr.Count == 0) Put("N:" + oid, e.Where); }
        }
        return map;
    }

    private static OtherWhere? FindInOther(Dictionary<string, OtherWhere> index, Order incoming)
    {
        var tr = OtherCodes(incoming);
        foreach (var c in tr) if (index.TryGetValue("T:" + c, out var w)) return w;
        var oid = Up(incoming.OrderId);
        if (oid.Length > 0)
        {
            if (tr.Count == 0 && index.TryGetValue("O:" + oid, out var w1)) return w1;
            if (tr.Count > 0 && index.TryGetValue("N:" + oid, out var w2)) return w2;
        }
        return null;
    }

    /// <summary>enrichOtherTableEntry (nhánh kho lưu trữ): chỉ BỔ SUNG, không đụng trạng thái nhận, ngày nhận, ảnh.</summary>
    private static (Order? Entry, bool Changed) EnrichArchived(IEnumerable<OtherEntry> others, Order incoming)
    {
        var codes = OtherCodes(incoming);
        var oid = Up(incoming.OrderId);
        var entry = others.Where(e => e.Where == OtherWhere.Archive).Select(e => e.Order).FirstOrDefault(o =>
        {
            var tr = OtherCodes(o);
            if (codes.Any(tr.Contains)) return true;
            return oid.Length > 0 && Up(o.OrderId) == oid && (codes.Count == 0 || tr.Count == 0);
        });
        if (entry is null) return (null, false);

        var changed = false;
        void Set(Func<string> get, Action<string> put, string v) { if (v.Length > 0 && get() != v) { put(v); changed = true; } }
        void Fill(Func<string> get, Action<string> put, string v) { if (v.Length > 0 && get().Length == 0) { put(v); changed = true; } }

        var eTrack = Up(entry.Track);
        if (incoming.OrderId.Length > 0 && (entry.OrderId.Length == 0 || Up(entry.OrderId) == eTrack)) Set(() => entry.OrderId, v => entry.OrderId = v, incoming.OrderId);
        Fill(() => entry.ReturnOrderId, v => entry.ReturnOrderId = v, incoming.ReturnOrderId);
        Fill(() => entry.Track, v => entry.Track = v, incoming.Track);
        Fill(() => entry.ReturnTrack, v => entry.ReturnTrack = v, incoming.ReturnTrack);
        Fill(() => entry.Carrier, v => entry.Carrier = v, incoming.Carrier);
        Fill(() => entry.ShipDate, v => entry.ShipDate = v, incoming.ShipDate);
        if (incoming.Platform.Length > 0) Set(() => entry.Platform, v => entry.Platform = v, incoming.Platform);
        Set(() => entry.PlatformStatus, v => entry.PlatformStatus = v, incoming.PlatformStatus);
        Set(() => entry.ReturnedAt, v => entry.ReturnedAt = v, incoming.ReturnedAt);
        Fill(() => entry.PreOrder, v => entry.PreOrder = v, incoming.PreOrder);
        if (entry.Price == 0 && incoming.Price != 0) { entry.Price = incoming.Price; changed = true; }

        var hasReal = entry.Items.Any(it => it.Product.Length > 0 && !Regex.IsMatch(it.Product, "Sản phẩm mới|Chưa rõ sản phẩm"));
        if (!hasReal && incoming.Items.Any(it => it.Product.Length > 0)) { entry.Items = incoming.Items.Select(ItemMerge.Copy).ToList(); changed = true; }

        var merged = entry.TrackingCodes.Concat(incoming.TrackingCodes).Distinct().ToList();
        if (merged.Count > 0 && !entry.TrackingCodes.SequenceEqual(merged)) { entry.TrackingCodes = merged; changed = true; }
        return (entry, changed);
    }

    public static MergeOutcome Apply(List<Order> main, IReadOnlyList<OtherEntry> others, IEnumerable<ImportedParcel> parcels,
        ImportProductMode mode, DateTime today, Func<ImportedParcel, DateTime, ImportedParcel>? forNewRow = null)
    {
        forNewRow ??= TikTokMapper.ForNewRow;
        var outcome = new MergeOutcome();
        var index = BuildIndex(others);
        var year = today.Year;

        foreach (var raw in parcels)
        {
            var incoming = ParcelMerge.ToOrder(raw);

            var all = ParcelMatching.FindAllMatchIndexes(main, incoming);
            if (all.Count > 1)
            {
                outcome.Ambiguous.Add(new AmbiguousParcel(incoming.Platform, incoming.OrderId, incoming.Track, incoming.ReturnOrderId, all.Count));
                continue;
            }
            var existingIdx = all.Count == 1 ? all[0] : -1;

            if (existingIdx != -1 && OrderIdConflict(main[existingIdx], incoming))
            {
                outcome.Ambiguous.Add(new AmbiguousParcel(incoming.Platform, incoming.OrderId, incoming.Track, incoming.ReturnOrderId, 1, OrderConflict: true));
                continue;
            }

            if (existingIdx == -1)
            {
                var fillIdx = ParcelMatching.FindTracklessSameOrderIndex(main, incoming);
                var tracklessCount = fillIdx == -1 ? 0 : main.Count(o => ParcelMatching.FindTracklessSameOrderIndex(new[] { o }, incoming) == 0);
                if (tracklessCount > 1)
                {
                    // quy tắc 2-3: nhiều kiện chưa có mã cùng mã đơn -> không đoán kiện nào
                    outcome.Ambiguous.Add(new AmbiguousParcel(incoming.Platform, incoming.OrderId, incoming.Track, incoming.ReturnOrderId, tracklessCount));
                    continue;
                }
                if (fillIdx != -1)
                {
                    ParcelMerge.FillTrackless(main[fillIdx], raw);
                    outcome.Updated.Add(main[fillIdx]);
                    outcome.Enriched++;
                    continue;
                }
                var shopee = ParcelMatching.FindShopeeTracklessSameOrderIndexes(main, incoming);
                if (shopee.Count > 1)
                {
                    outcome.Ambiguous.Add(new AmbiguousParcel(incoming.Platform, incoming.OrderId, incoming.Track, incoming.ReturnOrderId, shopee.Count));
                    continue;
                }
                if (shopee.Count == 1)
                {
                    ParcelMerge.FillTrackless(main[shopee[0]], raw);
                    outcome.Updated.Add(main[shopee[0]]);
                    outcome.Enriched++;
                    continue;
                }
            }

            if (existingIdx != -1)
            {
                var order = main[existingIdx];
                var oldStatus = order.PlatformStatus;
                var changed = ParcelMerge.ApplyMatch(order, raw, force: true, mode, year);
                if (raw.PlatformStatus.Length > 0 && raw.PlatformStatus != oldStatus) order.PlatformStatus = raw.PlatformStatus;
                if (raw.ReturnedAt.Length > 0) order.ReturnedAt = raw.ReturnedAt;
                if (raw.BuyerNote.Length > 0) order.BuyerNote = raw.BuyerNote;
                if (changed || (raw.PlatformStatus.Length > 0 && raw.PlatformStatus != oldStatus))
                {
                    outcome.Enriched++;
                    outcome.Updated.Add(order);
                }
                continue;
            }

            var where = FindInOther(index, incoming);
            if (where == OtherWhere.Deleted) { outcome.SkippedDeleted++; continue; }
            if (where == OtherWhere.Archive)
            {
                var (entry, changed) = EnrichArchived(others, incoming);
                if (changed && entry is not null) outcome.EnrichedArchived.Add(entry); else outcome.SkippedArchivedUnchanged++;
                continue;
            }

            var row = ParcelMerge.NewRow(raw, today, forNewRow);
            main.Add(row);
            outcome.Added.Add(row);
        }
        return outcome;
    }
}
