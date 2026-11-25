using System.Globalization;
using System.Text.RegularExpressions;

namespace QuanLyHangHoan.Core.Import;

public sealed class SheetItem
{
    public string Name { get; set; } = "";
    public string Variation { get; set; } = "";
    public int Qty { get; set; } = 1;
}

/// <summary>Một đơn gộp từ các tab Google Sheet (hanghoan / trahang / huy).</summary>
public sealed class SheetParcel
{
    public string OrderId { get; set; } = "";
    public string Src { get; set; } = "";
    public List<SheetItem> Items { get; } = new();
    public string ClaimId { get; set; } = "";
    public string Track { get; set; } = "";
    public string Type { get; set; } = "";
    public string ParcelStatus { get; set; } = "";
    public string RequestStatus { get; set; } = "";
    public string Reason { get; set; } = "";
    public string BuyerNote { get; set; } = "";
    public string WhoCancelled { get; set; } = "";
    public string Journey { get; set; } = "";
    public string ReturnDate { get; set; } = "";
    public string DeliveredAt { get; set; } = "";
    public string Deadline { get; set; } = "";
    public string Refund { get; set; } = "";
    public string TypeLabel { get; set; } = "";
    public double UpdatedAt { get; set; }
}

public sealed class SheetBuildResult
{
    public List<Dictionary<string, string>> Rows { get; } = new();
    public List<SheetParcel> Parcels { get; } = new();
    public int OldRequestsDropped { get; set; }
    public int SkippedCancelTypes { get; set; }
    /// <summary>Số tab tra mã vận đơn đã đọc (không tính vào số tab đơn hàng) và số kiện được điền mã từ đó.</summary>
    public int TrackLookupTabs { get; set; }
    public int TracksFilled { get; set; }
    public int TooOld { get; set; }
    public List<string> SkippedTabs { get; } = new();
    /// <summary>Dòng của tab không nhận diện được kiểu (nạp như file Excel thường).</summary>
    public List<Dictionary<string, string>> OtherRows { get; } = new();
}

/// <summary>
/// Chuyển từ quy trình Google Sheet của app cũ (runSheetSync, phần xử lý dữ liệu): ghép 3 tab Hàng hoàn / Trả hàng / Huỷ thành MỘT kiện cho mỗi
/// mã đơn rồi đổi sang tên cột như file Excel Shopee để ShopeeMapper đọc. Đối chiếu với mã JS cũ (Golden/sheet.*.json). Dữ liệu GIẢ.
/// Mạng, token, lịch 10 phút, chữ ký "đơn đổi" nằm ở lớp Data.
/// </summary>
public static class SheetPipeline
{
    public static readonly string[] DefaultSkipTypes = { "huy truoc chuan bi" };

    private static string K(string? s) => LegacyText.Key(s);

    public static DateTime? ParseFullDate(string? val)
    {
        var s = (val ?? "").Trim();
        if (s.Length == 0 || Regex.IsMatch(s, @"^dd[\/\-\.]mm", RegexOptions.IgnoreCase)) return null;
        var m = Regex.Match(s, @"^(\d{1,2})[\/\-\.](\d{1,2})[\/\-\.](\d{2,4})(?:\s+(\d{1,2}):(\d{1,2})(?::(\d{1,2}))?)?");
        if (m.Success) { var d = Make(int.Parse(m.Groups[3].Value) is var y && y < 100 ? y + 2000 : int.Parse(m.Groups[3].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value), m); if (d is not null) return d; }
        m = Regex.Match(s, @"^(\d{4})[\/\-\.](\d{1,2})[\/\-\.](\d{1,2})(?:\s+(\d{1,2}):(\d{1,2})(?::(\d{1,2}))?)?");
        if (m.Success) { var d = Make(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), m); if (d is not null) return d; }
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;

        static DateTime? Make(int y, int mo, int d, Match mm)
        {
            int G(int i) => mm.Groups[i].Success ? int.Parse(mm.Groups[i].Value) : 0;
            try { return new DateTime(y, 1, 1).AddMonths(mo - 1).AddDays(d - 1).AddHours(G(4)).AddMinutes(G(5)).AddSeconds(G(6)); }
            catch (ArgumentOutOfRangeException) { return null; }
        }
    }

    private static double Ms(DateTime d) => (d - new DateTime(1970, 1, 1)).TotalMilliseconds;

    /// <summary>parseSheetItemsCell: "TÊN · Màu, Size × SL; ...".</summary>
    public static List<SheetItem> ParseItemsCell(string? text, string? totalQty)
    {
        var items = Regex.Split(text ?? "", @"\s*;\s*").Select(t => t.Trim()).Where(t => t.Length > 0).Select(p =>
        {
            var qty = 0; var body = p;
            var mq = Regex.Match(body, @"\s*[×xX\*]\s*(\d+)\s*$");
            if (mq.Success) { qty = int.Parse(mq.Groups[1].Value); body = body[..mq.Index].Trim(); }
            var dot = Regex.Match(body, @"\s+[·•]\s+");
            var name = dot.Success ? body[..dot.Index].Trim() : body;
            var variation = dot.Success ? Regex.Replace(body[dot.Index..], @"^\s+[·•]\s+", "").Trim() : "";
            return new SheetItem { Name = name, Variation = variation, Qty = qty };
        }).ToList();
        if (items.Count == 1 && items[0].Qty == 0)
        {
            var digits = Regex.Replace(totalQty ?? "", @"[^\d]", "");
            items[0].Qty = digits.Length > 0 && int.TryParse(digits, out var n) && n > 0 ? n : 1;
        }
        foreach (var it in items) if (it.Qty == 0) it.Qty = 1;
        return items;
    }

    /// <summary>keepLatestRequestPerOrder: mỗi đơn giữ 1 yêu cầu (còn hiệu lực rồi mới nhất).</summary>
    public static (List<Dictionary<string, string>> Rows, int Removed) KeepLatestRequestPerOrder(List<Dictionary<string, string>> rows)
    {
        if (rows.Count < 2) return (rows, 0);
        var keys = rows[0].Keys.ToList();
        string? Pick(params string[] names) => keys.FirstOrDefault(k => names.Contains(K(k)));
        var orderK = Pick("madon", "madonhang"); var claimK = Pick("ma", "matrahang", "masokhieunai", "mayeucau");
        var dateK = Pick("ngay", "ngayyeucau", "thoigianyeucau"); var statusK = Pick("trangthai", "trangthaive");
        if (orderK is null || claimK is null) return (rows, 0);
        string V(Dictionary<string, string> r, string? k) => k is not null && r.TryGetValue(k, out var v) ? v ?? "" : "";
        double Ts(string v) { var d = ParseFullDate(v); return d is null ? 0 : Ms(d.Value); }
        bool Inactive(Dictionary<string, string> r) => Regex.IsMatch(LegacyText.RemoveTones(V(r, statusK)).ToLowerInvariant(), "khach huy|da dong");
        var best = new Dictionary<string, (string C, int Active, double T)>();
        foreach (var r in rows)
        {
            var o = V(r, orderK).Trim(); var c = V(r, claimK).Trim();
            if (o.Length == 0 || c.Length == 0) continue;
            var cand = (C: c, Active: Inactive(r) ? 0 : 1, T: dateK is not null ? Ts(V(r, dateK)) : 0);
            if (!best.TryGetValue(o, out var cur) || cand.Active > cur.Active || (cand.Active == cur.Active && cand.T > cur.T)) best[o] = cand;
        }
        var outRows = rows.Where(r =>
        {
            var o = V(r, orderK).Trim(); var c = V(r, claimK).Trim();
            return o.Length == 0 || c.Length == 0 || best[o].C == c;
        }).ToList();
        return (outRows, rows.Count - outRows.Count);
    }

    /// <summary>Tên tab ẩn chứa mã vận đơn của đơn huỷ/bom (2 cột: Mã đơn, Mã vận đơn). Tùy chọn: thiếu thì bỏ qua.</summary>
    public const string TrackTabName = "Vận đơn huỷ";

    /// <summary>sheetTabKind: "hanghoan" | "trahang" | "huy" | "vandon" (tab tra mã vận đơn, không phải đơn hàng) | "".</summary>
    public static string TabKind(List<Dictionary<string, string>> rows)
    {
        var ks = new HashSet<string>((rows.Count > 0 ? rows[0].Keys : Enumerable.Empty<string>()).Select(K));
        if (ks.Contains("hang") && ks.Contains("madon") && ks.Contains("trangthaive")) return "hanghoan";
        if (ks.Contains("matrahang") && ks.Contains("madon")) return "trahang";
        if (ks.Contains("madon") && ks.Contains("loai") && (ks.Contains("aihuy") || ks.Contains("hanhtrinh"))) return "huy";
        if (ks.Contains("madon") && ks.Contains("mavandon")) return "vandon";   // đã loại các tab có cột Hàng / Loại / Trạng thái về / Mã trả hàng ở trên
        return "";
    }

    /// <summary>Bảng tra Mã đơn (HOA, cắt khoảng trắng) -> Mã vận đơn từ tab "vandon". Bỏ dòng thiếu mã đơn hoặc mã vận đơn; trùng mã đơn thì giữ dòng sau.</summary>
    public static Dictionary<string, string> TrackMap(IEnumerable<Dictionary<string, string>> rows)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            string o = "", t = "";
            foreach (var kv in r)
            {
                var k = K(kv.Key); var v = (kv.Value ?? "").Trim();
                if (k == "madon") o = v; else if (k == "mavandon") t = v;
            }
            if (o.Length > 0 && t.Length > 0) map[o.ToUpperInvariant()] = t;
        }
        return map;
    }

    /// <summary>Điền mã vận đơn cho kiện CHƯA có mã. Kiện đã có mã thì KHÔNG ghi đè. Trả về số kiện được điền.</summary>
    public static int ApplyTrackMap(IEnumerable<SheetParcel> parcels, IReadOnlyDictionary<string, string> map)
    {
        var n = 0;
        if (map.Count == 0) return 0;
        foreach (var p in parcels)
            if (p.Track.Length == 0 && p.OrderId.Length > 0 && map.TryGetValue(p.OrderId.Trim().ToUpperInvariant(), out var t)) { p.Track = t; n++; }
        return n;
    }

    public static List<string> SkipTypes(string? configured) =>
        configured is null ? DefaultSkipTypes.ToList()
            : Regex.Split(configured, @"[;\n]").Select(t => LegacyText.RemoveTones(t.Trim()).ToLowerInvariant()).Where(t => t.Length > 0).ToList();

    public static (List<SheetParcel> Parcels, int Skipped) RowsToParcels(List<Dictionary<string, string>> rows, string kind, IReadOnlyList<string>? skipTypes = null)
    {
        skipTypes ??= DefaultSkipTypes;
        string G(Dictionary<string, string> r, params string[] names)
        {
            foreach (var kv in r) if (names.Contains(K(kv.Key))) { var v = (kv.Value ?? "").Trim(); if (v.Length > 0) return v; }
            return "";
        }
        var map = new Dictionary<string, SheetParcel>(); var order = new List<string>(); var skipped = 0;
        foreach (var r in rows)
        {
            var orderId = G(r, "madon"); if (orderId.Length == 0) continue;
            var loai = G(r, "loai"); var loaiN = LegacyText.RemoveTones(loai).ToLowerInvariant();
            if (kind == "huy" && skipTypes.Any(t => loaiN.Contains(t))) { skipped++; continue; }
            if (!map.TryGetValue(orderId, out var p))
            {
                var ud = ParseFullDate(G(r, "capnhat"));
                p = new SheetParcel
                {
                    OrderId = orderId, Src = kind, ClaimId = G(r, "ma", "matrahang"), Track = kind is "hanghoan" or "huy" ? G(r, "mavandon") : "",
                    Reason = G(r, "lydo"), BuyerNote = G(r, "khachghi"), WhoCancelled = G(r, "aihuy"), Journey = G(r, "hanhtrinh"),
                    ReturnDate = G(r, "ngay", "ngayyeucau"), DeliveredAt = G(r, "veshopluc"), Deadline = G(r, "hankhieunai"), Refund = G(r, "tien", "hoantien"),
                    UpdatedAt = ud is null ? 0 : Ms(ud.Value),
                };
                if (kind == "hanghoan")
                {
                    p.TypeLabel = loai; p.ParcelStatus = G(r, "trangthaive");
                    p.Type = Regex.IsMatch(loaiN, "bom|that bai|khong thanh cong") ? "bom" : loaiN.Contains("huy") ? "cancel" : "return";
                }
                else
                {
                    p.RequestStatus = G(r, "trangthai");
                    p.Type = kind == "trahang" ? "return" : loaiN.Contains("bom") ? "bom" : Regex.IsMatch(loaiN, "tra hang|hoan tien") ? "return" : "cancel";
                    p.TypeLabel = loai;
                }
                map[orderId] = p; order.Add(orderId);
            }
            if (kind == "hanghoan") p.Items.AddRange(ParseItemsCell(G(r, "hang"), G(r, "sl")));
            else p.Items.Add(new SheetItem { Name = G(r, "tensanpham", "sanpham"), Variation = "", Qty = int.TryParse(Regex.Match(G(r, "sl"), @"^\s*-?\d+").Value, out var q) && q != 0 ? q : 1 });
        }
        return (order.Select(k => map[k]).ToList(), skipped);
    }

    /// <summary>mergeSheetParcels: gộp các tab thành MỘT kiện cho mỗi mã đơn.</summary>
    public static List<SheetParcel> Merge(IEnumerable<List<SheetParcel>> lists)
    {
        int Rank(string s) => s switch { "hanghoan" => 3, "trahang" => 2, "huy" => 1, _ => 0 };
        var by = new Dictionary<string, List<SheetParcel>>(); var order = new List<string>();
        foreach (var list in lists) foreach (var p in list)
        {
            if (!by.ContainsKey(p.OrderId)) { by[p.OrderId] = new(); order.Add(p.OrderId); }
            by[p.OrderId].Add(p);
        }
        var merged = new List<SheetParcel>();
        foreach (var id in order)
        {
            var ps = by[id];
            var newest = ps.OrderByDescending(p => p.UpdatedAt).ToList();
            string PickF(Func<SheetParcel, string> f) { foreach (var p in newest) if (f(p).Length > 0) return f(p); return ""; }
            var m = new SheetParcel
            {
                OrderId = ps[0].OrderId, ClaimId = PickF(p => p.ClaimId), Track = PickF(p => p.Track), ParcelStatus = PickF(p => p.ParcelStatus),
                RequestStatus = PickF(p => p.RequestStatus), Reason = PickF(p => p.Reason), BuyerNote = PickF(p => p.BuyerNote),
                WhoCancelled = PickF(p => p.WhoCancelled), Journey = PickF(p => p.Journey), ReturnDate = PickF(p => p.ReturnDate),
                DeliveredAt = PickF(p => p.DeliveredAt), Deadline = PickF(p => p.Deadline), Refund = PickF(p => p.Refund), TypeLabel = PickF(p => p.TypeLabel),
            };
            m.Type = ps.Any(p => p.Type == "bom") ? "bom" : newest.FirstOrDefault(p => p.Type.Length > 0)?.Type is { Length: > 0 } t ? t : "return";
            var itemSrc = ps.OrderByDescending(p => Rank(p.Src)).FirstOrDefault(p => p.Items.Any(it => it.Name.Length > 0));
            if (itemSrc is not null) m.Items.AddRange(itemSrc.Items);
            m.Src = string.Join(",", ps.Select(p => p.Src).Distinct());
            merged.Add(m);
        }
        return merged;
    }

    /// <summary>filterSheetParcelsByDays: "01/11/2026" (từ ngày) hoặc "30" (N ngày gần nhất); trống/0 = lấy hết.</summary>
    public static (List<SheetParcel> Parcels, int Dropped) FilterByDays(List<SheetParcel> parcels, string? days, DateTime now)
    {
        var v = (days ?? "").Trim(); DateTime? cut = null;
        var dm = Regex.Match(v, @"^(\d{1,2})[\/\-.](\d{1,2})[\/\-.](\d{4})$");
        if (dm.Success) cut = new DateTime(int.Parse(dm.Groups[3].Value), int.Parse(dm.Groups[2].Value), int.Parse(dm.Groups[1].Value));
        else { var nm = Regex.Match(v, @"^\s*(\d+)"); if (nm.Success && int.Parse(nm.Groups[1].Value) > 0) cut = now.AddDays(-int.Parse(nm.Groups[1].Value)); }
        if (cut is null) return (parcels, 0);
        var kept = parcels.Where(p => { var d = ParseFullDate(p.ReturnDate); return d is null || d.Value >= cut; }).ToList();
        return (kept, parcels.Count - kept.Count);
    }

    /// <summary>shopeeClaimDeadline: hạn 24 giờ kể từ lúc hàng về shop ("" nếu không có).</summary>
    public static string ClaimDeadline(string? deliveredAt)
    {
        var d = ParseFullDate(deliveredAt);
        return d is null ? "" : d.Value.AddHours(24).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>sheetParcelsToRows: đổi sang tên cột như file Excel Shopee.</summary>
    public static List<Dictionary<string, string>> ToRows(IEnumerable<SheetParcel> parcels)
    {
        var rows = new List<Dictionary<string, string>>();
        foreach (var m in parcels)
        {
            var baseRow = new Dictionary<string, string>
            {
                ["Mã đơn hàng"] = m.OrderId, ["Mã vận đơn"] = m.Track, ["Mã số khiếu nại"] = m.ClaimId, ["Lý do trả hàng/hoàn tiền"] = m.Reason,
                ["Ghi chú của Người mua khi trả hàng"] = m.BuyerNote, ["Thời gian yêu cầu"] = m.ReturnDate, ["Thời gian giao hàng thành công"] = m.DeliveredAt,
                ["Người bán cần phản hồi trước"] = ClaimDeadline(m.DeliveredAt) is { Length: > 0 } dl ? dl : m.Deadline, ["Tổng số tiền hoàn trả"] = m.Refund,
            };
            var status = m.ParcelStatus.Length > 0 ? m.ParcelStatus : m.RequestStatus;
            if (m.Type == "bom") { baseRow["Lý do hủy"] = "Giao hàng thất bại"; baseRow["Trạng thái Trả hàng/Hoàn tiền"] = status; }
            else if (m.Type == "cancel") baseRow["Trạng thái đơn hàng"] = (m.TypeLabel.Length > 0 ? m.TypeLabel + " · " : "") + (status.Length > 0 ? status : "Đã hủy");
            else baseRow["Trạng thái Trả hàng/Hoàn tiền"] = status;
            var items = m.Items.Count > 0 ? m.Items : new List<SheetItem> { new() };
            for (var i = 0; i < items.Count; i++)
            {
                var r = new Dictionary<string, string>(baseRow)
                {
                    ["Tên sản phẩm"] = items[i].Name, ["Tên phân loại hàng"] = items[i].Variation, ["Số lượng"] = (items[i].Qty == 0 ? 1 : items[i].Qty).ToString(),
                };
                if (i > 0) r["Tổng số tiền hoàn trả"] = "";
                rows.Add(r);
            }
        }
        return rows;
    }

    /// <summary>Toàn bộ phần xử lý dữ liệu của một lượt đồng bộ (không gồm mạng/chữ ký): các tab CSV đã đọc thành dòng -> dòng kiểu Excel Shopee.</summary>
    public static SheetBuildResult Build(IReadOnlyList<List<Dictionary<string, string>>> tabs, string? skipTypesConfig, string? maxDays, DateTime now)
    {
        var res = new SheetBuildResult();
        var lists = new List<List<SheetParcel>>();
        var other = new List<Dictionary<string, string>>();
        var trackMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tab in tabs)
        {
            if (tab.Count == 0) { res.SkippedTabs.Add("tab"); continue; }
            if (TabKind(tab) == "vandon") { foreach (var kv in TrackMap(tab)) trackMap[kv.Key] = kv.Value; res.TrackLookupTabs++; continue; }   // chỉ để tra mã, không nạp thành đơn
            var (kept, removed) = KeepLatestRequestPerOrder(tab);
            res.OldRequestsDropped += removed;
            var kind = TabKind(kept);
            if (kind.Length > 0) { var (ps, sk) = RowsToParcels(kept, kind, SkipTypes(skipTypesConfig)); res.SkippedCancelTypes += sk; lists.Add(ps); }
            else other.AddRange(kept);
        }
        if (lists.Count > 0)
        {
            var merged = Merge(lists);
            res.TracksFilled = ApplyTrackMap(merged, trackMap);
            var (parcels, dropped) = FilterByDays(merged, maxDays, now);
            res.TooOld = dropped;
            res.Parcels.AddRange(parcels);
            res.Rows.AddRange(ToRows(parcels));
        }
        res.OtherRows.AddRange(other);
        res.Rows.AddRange(other);
        return res;
    }
}
