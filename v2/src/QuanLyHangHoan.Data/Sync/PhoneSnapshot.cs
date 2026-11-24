using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Core.Tracking;

namespace QuanLyHangHoan.Data.Sync;

/// <summary>
/// Danh sách đơn gửi sang app iPhone (định dạng của `ios-native/ScannerCore/Snapshot.swift`, tương thích app cũ v1.9.x):
///  - snapshot_chunk: toàn bộ danh sách (JSON -> deflate thô -> base64 -> mảnh 2.400 ký tự);
///  - snapshot_delta: chỉ phần thay đổi so với bản đầy đủ gần nhất (khóa dòng k|c|o|ci, trùng đánh số #2, #3);
///  - watch_sync: danh sách đơn đang theo dõi + lý do.
/// Chỉ ĐỌC dữ liệu; không có tên/SĐT/địa chỉ khách (v2 không lưu).
/// k: m = bảng chính, c = đơn hủy (Order.OrderType == "Đơn hủy", chưa lưu trữ), a = kho lưu trữ; đơn đã xóa không gửi.
/// </summary>
public static class PhoneSnapshot
{
    public const int ChunkSize = 2400;
    public const int MaxRows = 30000;
    public const int DeltaMaxRows = 400;
    public const string CancelType = "Đơn hủy";

    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string ToJson(object value) => JsonSerializer.Serialize(value, Json);

    // ---------------- quy tắc trạng thái (port isOrderReceived / isOrderPending) ----------------
    public static bool IsReceived(Order o) => o.Status == ReceiveStatus.Received || o.Status == "Đã nhận";
    public static bool IsPending(Order o) =>
        !IsReceived(o) && o.Status != ReceiveStatus.NotNeeded && o.Status != ReceiveStatus.Lost && o.Status != "Hư hỏng";

    /// <summary>Giờ hàng về shop (cột returnedAt). Salework ghi giờ trước ngày ("10:15:22 21/11/2026") nên đảo lại trước khi đọc.</summary>
    public static DateTime? ArrivedAt(Order o)
    {
        var s = (o.ReturnedAt ?? "").Trim();
        if (s.Length == 0 && o.Import.RawDetails.TryGetValue("returnSuccessTime", out var rs)) s = (rs ?? "").Trim();
        if (s.Length == 0) return null;
        var m = System.Text.RegularExpressions.Regex.Match(s, @"^(\d{1,2}:\d{1,2}(?::\d{1,2})?)\s+(\d{1,2}[/\-.]\d{1,2}[/\-.]\d{2,4})");
        return SheetPipeline.ParseFullDate(m.Success ? m.Groups[2].Value + " " + m.Groups[1].Value : s);
    }

    private static long Ms(DateTime local) =>
        new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZoneInfo.Local.GetUtcOffset(local)).ToUnixTimeMilliseconds();

    private static string Clip(string? v, int n) { var s = v ?? ""; return s.Length <= n ? s : s[..n]; }
    private static string Join(IEnumerable<string> parts, int max) => Clip(string.Join(" | ", parts.Where(p => p.Length > 0)), max);

    private static string PhonePlatform(string platform) => platform == "TikTok" ? "TikTok Shop" : platform;

    // ---------------- dòng ----------------
    public static string KindOf(StoredOrder s) =>
        s.IsArchived ? "a" : (s.Order.OrderType == CancelType ? "c" : "m");

    public static Dictionary<string, object> BuildRow(StoredOrder s, DateTime now, IReadOnlyDictionary<string, (long Since, int Stage)>? returnWatch = null)
    {
        var o = s.Order;
        var kind = KindOf(s);
        var items = o.Items.Count > 0 ? o.Items : new List<OrderItem>();
        var pending = IsPending(o);
        var arrived = ArrivedAt(o);

        var c = FirstNonEmpty(o.Track, o.ReturnTrack, o.OrderId);
        var row = new Dictionary<string, object>
        {
            ["k"] = kind,
            ["c"] = c,
            ["o"] = (o.OrderId ?? "").Trim(),
            ["p"] = PhonePlatform(o.Platform ?? ""),
            ["n"] = Join(items.Select(i => Clip(i.Product, 70)), 160),
            ["co"] = Clip(string.Join(" | ", items.Select(i => i.Color ?? "")), 60),
            ["sz"] = Clip(string.Join(" | ", items.Select(i => i.Size ?? "")), 40),
            ["q"] = items.Count == 0 ? 1 : items.Sum(i => i.Qty > 0 ? i.Qty : 1),
            ["rd"] = o.ReturnDate ?? "",
            ["st"] = kind == "c" ? CancelType : (o.Status ?? ""),
        };

        // Hoàn một phần: tên các sản phẩm ĐÃ HOÀN (điện thoại tô màu các sản phẩm này trong danh sách `n`). Chỉ khi có cả hoàn lẫn không hoàn.
        var returnedNames = items.Where(i => i.ReturnedQty > 0).Select(i => Clip(i.Product, 70)).Where(n => n.Length > 0).ToList();
        if (returnedNames.Count > 0 && returnedNames.Count < items.Count) row["rn"] = Join(returnedNames, 160);

        long due = 0;
        var lv = 0;
        if (pending && arrived is { } a)
        {
            due = Ms(a.AddHours(24));
            var el = now - a;
            lv = el >= TimeSpan.FromHours(24) ? 2 : (el >= TimeSpan.FromHours(12) ? 1 : 0);
        }
        row["due"] = due;
        row["lv"] = lv;

        var ty = string.IsNullOrEmpty(o.OrderType) ? "" : o.OrderType;
        if (ty.Length > 0) row["ty"] = ty;
        if (!string.IsNullOrEmpty(o.RecvDate)) row["rcd"] = Clip(o.RecvDate, 24);
        if (!string.IsNullOrEmpty(o.PlatformStatus)) row["ps"] = Clip(o.PlatformStatus, 60);
        if (o.Price > 0) row["pr"] = (long)Math.Round(o.Price);                        // giá trị đơn (đồng), để AI trên điện thoại tính tổng tiền
        if (!string.IsNullOrEmpty(o.Carrier)) row["cr"] = Clip(o.Carrier, 30);          // đơn vị vận chuyển

        // Các mã KHÁC của chính kiện này (không gồm mã giao đi dùng chung, quy tắc 2/3) để điện thoại tra được khi quét kiện hoàn.
        var own = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { c.ToUpperInvariant(), (o.OrderId ?? "").Trim().ToUpperInvariant() };
        var extra = new List<string>();
        foreach (var raw in new[] { o.ReturnTrack, o.AltTrack, o.ReturnOrderId }.Concat(o.TrackingCodes).Concat(o.ScanAliases))
        {
            var v = TrackingResolver.Normalize(raw ?? "");
            if (v.Length > 0 && own.Add(v)) extra.Add(v);
        }
        if (extra.Count > 0) row["x"] = string.Join(' ', extra.Take(6));

        // Bảng Hàng Hoàn Về: rw = giờ vào bảng (ms), rws = chặng 1-4, ar = giờ hàng về shop (ms). Chỉ đơn bảng chính.
        if (kind == "m" && returnWatch is not null && returnWatch.TryGetValue(o.Id, out var rwm) && rwm.Since > 0)
        {
            row["rw"] = rwm.Since; row["rws"] = rwm.Stage;
            if (arrived is { } rwArr) row["ar"] = Ms(rwArr);
        }

        if (o.PreOrder == "Hàng đặt trước") row["pre"] = 1;
        if (!pending) row["pd"] = 0;
        if (IsReceived(o)) row["rv"] = 1;
        if (o.IsWatchlist)
        {
            row["wl"] = 1;
            if (!string.IsNullOrEmpty(o.WatchlistReason)) row["wr"] = Clip(o.WatchlistReason, 200);
        }
        var nt = Join(new[] { o.Note ?? "", o.BuyerNote ?? "", o.UserNote ?? "" }.Select(v => v.Trim()), 200);
        if (nt.Length > 0) row["nt"] = nt;
        if (kind == "c") row["ci"] = o.Id; // để điện thoại đánh dấu đã nhận đúng đơn hủy (cancel_receive)
        return row;
    }

    private static string FirstNonEmpty(params string?[] v) => v.Select(x => (x ?? "").Trim()).FirstOrDefault(x => x.Length > 0) ?? "";

    /// <summary>Mọi dòng gửi điện thoại: đơn đang chờ lên trước, hạn gần trước (giống PC cũ); đơn không có mã bị bỏ.</summary>
    public static List<Dictionary<string, object>> BuildRows(IEnumerable<StoredOrder> all, DateTime now, IReadOnlyDictionary<string, (long Since, int Stage)>? returnWatch = null)
    {
        var rows = all.Where(s => !s.IsDeleted).Select(s => BuildRow(s, now, returnWatch)).Where(r => ((string)r["c"]).Length > 0).ToList();
        return rows
            .Select((r, i) => (r, i))
            .OrderBy(t => t.r.ContainsKey("pd") ? 1 : 0)
            .ThenBy(t => (long)t.r["due"] == 0 ? long.MaxValue : (long)t.r["due"])
            .ThenBy(t => t.i)
            .Select(t => t.r).Take(MaxRows).ToList();
    }

    /// <summary>Bảng đối chiếu số đơn do PC tính trên dữ liệu gốc (điện thoại so với số nó nhận được).</summary>
    public static Dictionary<string, int> BuildStats(IEnumerable<StoredOrder> all, int sentCount, DateTime now)
    {
        var live = all.Where(s => !s.IsDeleted).ToList();
        var main = live.Where(s => KindOf(s) == "m").Select(s => s.Order).ToList();
        var arch = live.Where(s => s.IsArchived).Select(s => s.Order).ToList();
        var can = live.Where(s => KindOf(s) == "c").Select(s => s.Order).ToList();
        int Lv(Order o) => !IsPending(o) || ArrivedAt(o) is not { } a ? 0 : (now - a >= TimeSpan.FromHours(24) ? 2 : (now - a >= TimeSpan.FromHours(12) ? 1 : 0));
        var st = new Dictionary<string, int>
        {
            ["m_total"] = main.Count, ["m_recv"] = main.Count(IsReceived), ["m_pend"] = main.Count(IsPending),
            ["m_watch"] = main.Count(o => o.IsWatchlist), ["m_pre"] = main.Count(o => o.PreOrder == "Hàng đặt trước"),
            ["m_danger"] = main.Count(o => Lv(o) == 2), ["m_warn"] = main.Count(o => Lv(o) == 1), ["m_cancelType"] = 0,
            ["a_total"] = arch.Count, ["a_recv"] = arch.Count(IsReceived),
            ["c_total"] = can.Count, ["c_recv"] = can.Count(IsReceived),
            ["sent"] = sentCount,
        };
        st["dropped"] = st["m_total"] + st["a_total"] + st["c_total"] - sentCount;
        return st;
    }

    /// <summary>Khóa từng dòng, giống hệt iPhone (SnapshotDelta.keys): k|c|o|ci, trùng khóa đánh số #2, #3.</summary>
    public static List<string> RowKeys(IReadOnlyList<Dictionary<string, object>> rows)
    {
        var seen = new Dictionary<string, int>();
        var keys = new List<string>(rows.Count);
        foreach (var r in rows)
        {
            string G(string k) => r.TryGetValue(k, out var v) ? v?.ToString() ?? "" : "";
            var b = $"{G("k")}|{G("c")}|{G("o")}|{G("ci")}";
            seen[b] = seen.GetValueOrDefault(b) + 1;
            keys.Add(seen[b] > 1 ? b + "#" + seen[b] : b);
        }
        return keys;
    }

    // ---------------- gói tin ----------------
    public static string DeflateBase64(string json)
    {
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            var b = Encoding.UTF8.GetBytes(json);
            ds.Write(b, 0, b.Length);
        }
        return Convert.ToBase64String(ms.ToArray());
    }

    public static string InflateBase64(string b64)
    {
        using var input = new MemoryStream(Convert.FromBase64String(b64));
        using var ds = new DeflateStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(ds, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Cắt chuỗi base64 thành các bản tin {type, room, <idField>, idx, total, chunk, time[, count]}.</summary>
    public static List<string> Chunks(string type, string idField, string id, string room, string b64, long nowMs, int? count = null)
    {
        var total = Math.Max(1, (int)Math.Ceiling(b64.Length / (double)ChunkSize));
        var list = new List<string>(total);
        for (var i = 0; i < total; i++)
        {
            var chunk = b64.Substring(i * ChunkSize, Math.Min(ChunkSize, b64.Length - i * ChunkSize));
            var msg = new Dictionary<string, object> { ["type"] = type, ["room"] = room, [idField] = id, ["idx"] = i, ["total"] = total, ["chunk"] = chunk, ["time"] = nowMs + i };
            if (count is { } c) msg["count"] = c;
            list.Add(ToJson(msg));
        }
        return list;
    }

    public static string FullPayloadJson(IReadOnlyList<Dictionary<string, object>> rows, Dictionary<string, int> stats, string sid, long nowMs) =>
        ToJson(new Dictionary<string, object> { ["time"] = nowMs, ["sid"] = sid, ["count"] = rows.Count, ["stats"] = stats, ["rows"] = rows, ["inbox"] = Array.Empty<object>() });

    /// <summary>Bản gốc để tính phần thay đổi: khóa dòng -> JSON dòng.</summary>
    public sealed record Base(string Sid, Dictionary<string, string> Map);

    public static Base MakeBase(string sid, IReadOnlyList<Dictionary<string, object>> rows)
    {
        var keys = RowKeys(rows);
        var map = new Dictionary<string, string>(rows.Count);
        for (var i = 0; i < rows.Count; i++) map[keys[i]] = ToJson(rows[i]);
        return new Base(sid, map);
    }

    public sealed record Delta(List<Dictionary<string, object>> Up, List<string> Rm);

    public static Delta ComputeDelta(Base b, IReadOnlyList<Dictionary<string, object>> rows)
    {
        var keys = RowKeys(rows);
        var up = new List<Dictionary<string, object>>();
        var seen = new HashSet<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            seen.Add(keys[i]);
            if (!b.Map.TryGetValue(keys[i], out var old) || old != ToJson(rows[i]))
            {
                var copy = new Dictionary<string, object>(rows[i]) { ["_k"] = keys[i] };
                up.Add(copy);
            }
        }
        var rm = b.Map.Keys.Where(k => !seen.Contains(k)).ToList();
        return new Delta(up, rm);
    }

    public static string DeltaPayloadJson(string baseSid, int seq, int count, Delta d, Dictionary<string, int> stats, long nowMs) =>
        ToJson(new Dictionary<string, object>
        {
            ["time"] = nowMs, ["base"] = baseSid, ["seq"] = seq, ["count"] = count, ["up"] = d.Up, ["rm"] = d.Rm, ["stats"] = stats, ["inbox"] = Array.Empty<object>(),
        });

    /// <summary>Danh sách đơn đang theo dõi (bảng chính + đơn hủy, chưa lưu trữ/xóa) kèm lý do.</summary>
    public static List<Dictionary<string, string>> BuildWatchList(IEnumerable<StoredOrder> all) =>
        all.Where(s => !s.IsDeleted && !s.IsArchived && s.Order.IsWatchlist)
            .Select(s => new Dictionary<string, string>
            {
                ["c"] = FirstNonEmpty(s.Order.Track, s.Order.ReturnTrack, s.Order.OrderId),
                ["o"] = (s.Order.OrderId ?? "").Trim(),
                ["r"] = Clip(s.Order.WatchlistReason, 300),
            }).ToList();

    public static string WatchSyncJson(string room, long nowMs, List<Dictionary<string, string>> items) =>
        ToJson(new Dictionary<string, object> { ["type"] = "watch_sync", ["room"] = room, ["time"] = nowMs, ["items"] = items });
}
