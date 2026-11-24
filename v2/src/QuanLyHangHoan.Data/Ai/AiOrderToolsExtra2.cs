using System.Globalization;
using System.Text.Json.Nodes;
using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.Data.Ai;

/// <summary>Đợt công cụ ngầm thứ hai: tìm theo chữ, thời gian xử lý, kiểm tra chất lượng dữ liệu, mã trùng, thống kê hoạt động, thông báo.</summary>
public sealed partial class AiOrderTools
{
    /// <summary>Thông báo trong trung tâm thông báo (thời gian, mức, nguồn, tiêu đề, chi tiết, đã đọc).</summary>
    public sealed record AiNotice(DateTime Time, string Level, string Source, string Title, string Detail, bool Read);

    private static IEnumerable<JsonObject> Extra2Tools()
    {
        var search = FilterProps();
        search["query"] = Prop("string", "Từ khóa tìm (nhiều từ = phải khớp tất cả): mã đơn, mã vận đơn, mã hoàn, tên/màu/size/SKU sản phẩm, ghi chú, đơn vị vận chuyển, trạng thái.");
        search["limit"] = Prop("integer", "Số dòng tối đa, mặc định 20, tối đa 50.");

        var turn = FilterProps();
        turn["metric"] = Prop("string",
            "Đo thời gian nào: arrival_to_receipt = từ lúc hàng về shop tới lúc nhận tại kho (thời gian kho xử lý, mặc định); return_to_arrival = từ ngày khách hoàn tới lúc hàng về shop (thời gian vận chuyển).",
            "arrival_to_receipt", "return_to_arrival");
        turn["group_by"] = Prop("string", "Tách theo: none, platform, carrier, order_type, status, day, week, month, weekday.",
            "none", "platform", "carrier", "order_type", "status", "day", "week", "month", "weekday");

        yield return Tool("search_orders",
            "Tìm kiếm tự do như Ctrl+K: nhiều từ khóa cùng lúc trên mã đơn, mã vận đơn, sản phẩm, ghi chú, đơn vị vận chuyển... kết hợp với các bộ lọc khác. Danh sách đơn bấm được.",
            search, "query");
        yield return Tool("turnaround_stats",
            "Thống kê THỜI GIAN XỬ LÝ: trung bình, trung vị, p90, nhanh nhất, chậm nhất (giờ), tỷ lệ nhận trong 12 giờ / 24 giờ, và danh sách đơn chậm nhất. Dùng cho 'kho xử lý nhanh hay chậm', 'đơn vị vận chuyển nào chậm', 'trung bình bao lâu thì nhận'.",
            turn);
        yield return Tool("data_health",
            "Kiểm tra chất lượng dữ liệu: đơn thiếu giá trị, thiếu đơn vị vận chuyển, đang chờ mà chưa có giờ hàng về, đã nhận mà thiếu ngày nhận, ngày nhận trước giờ hàng về, không có sản phẩm, thiếu mã, mã vận đơn trùng. Mỗi mục kèm số lượng và vài đơn ví dụ.",
            new JsonObject { ["scope"] = Prop("string", "active (mặc định), archived, all.", "active", "archived", "all") });
        yield return Tool("duplicates",
            "Tìm mã vận đơn (hoặc mã vận đơn trả hàng) bị TRÙNG giữa nhiều dòng khác nhau — thường là lỗi nhập liệu cần kiểm tra. Lưu ý các kiện A/B/C của cùng mã đơn có mã vận đơn riêng nên không tính là trùng.",
            new JsonObject
            {
                ["field"] = Prop("string", "track (mã vận đơn, mặc định) hoặc return_track (mã vận đơn trả hàng).", "track", "return_track"),
                ["scope"] = Prop("string", "active (mặc định), archived, all.", "active", "archived", "all"),
                ["limit"] = Prop("integer", "Số nhóm trùng tối đa, mặc định 15, tối đa 30."),
            });
        yield return Tool("activity_stats",
            "Thống kê hoạt động trong khoảng ngày theo loại (nạp đơn, quét nhận, sửa, xóa, khôi phục...) và theo ngày, từ nhật ký hoạt động. Dùng cho 'hôm nay nạp mấy lần', 'tuần này quét bao nhiêu'.",
            new JsonObject
            {
                ["from"] = Prop("string", "Từ ngày yyyy-MM-dd/today/yesterday (mặc định 30 ngày gần nhất)."),
                ["to"] = Prop("string", "Đến ngày."),
                ["type"] = Prop("string", "Loại: import, edit, scan, salework, phone_new_order, archive, delete, restore, return_watch (để trống = mọi loại)."),
            });
        yield return Tool("notifications",
            "Danh sách thông báo trong trung tâm thông báo (chuông): cảnh báo vàng/đỏ, yêu cầu từ điện thoại, nạp đơn... kèm đã đọc chưa.",
            new JsonObject
            {
                ["unread_only"] = Prop("boolean", "true = chỉ thông báo chưa đọc."),
                ["limit"] = Prop("integer", "Số dòng tối đa, mặc định 15, tối đa 50."),
            });
    }

    // ===================================================== tìm theo chữ

    private static string Blob(Order o)
    {
        var parts = new List<string>
        {
            o.OrderId, o.ReturnOrderId, o.Track, o.ReturnTrack, o.ForwardTrack, o.AltTrack, o.Platform, o.Carrier, o.OrderType,
            o.PlatformStatus, o.Status, o.Note, o.UserNote, o.WatchlistReason,
        };
        parts.AddRange(o.TrackingCodes);
        parts.AddRange(o.ScanAliases);
        foreach (var i in o.Items) { parts.Add(i.Product); parts.Add(i.Color); parts.Add(i.Size); parts.Add(i.Sku ?? ""); }
        return string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p))).ToLowerInvariant();
    }

    private AiToolResult Search(JsonObject a)
    {
        var q = Str(a, "query");
        if (q is null || q.Length < 2) return Error("Từ khóa quá ngắn (tối thiểu 2 ký tự).");
        var tokens = q.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var limit = Math.Clamp(Int(a, "limit") ?? 12, 1, 30);
        var rows = Select(ReadFilter(a)).Where(o => { var b = Blob(o); return tokens.All(b.Contains); }).ToList();
        return Rows(rows, limit);
    }

    // ===================================================== thời gian xử lý

    private static double Hours(DateTime from, DateTime to) => (to - from).TotalHours;

    private static JsonObject Stats(List<double> hours)
    {
        var s = hours.OrderBy(x => x).ToList();
        double Pick(double p) => s.Count == 0 ? 0 : s[Math.Clamp((int)Math.Ceiling(p * s.Count) - 1, 0, s.Count - 1)];
        double R(double d) => Math.Round(d, 1);
        var o = new JsonObject { ["samples"] = s.Count };
        if (s.Count == 0) return o;
        o["avgHours"] = R(s.Average());
        o["medianHours"] = R(Pick(0.5));
        o["p90Hours"] = R(Pick(0.9));
        o["minHours"] = R(s[0]);
        o["maxHours"] = R(s[^1]);
        o["within12hPercent"] = R(s.Count(h => h <= 12) * 100.0 / s.Count);
        o["within24hPercent"] = R(s.Count(h => h <= 24) * 100.0 / s.Count);
        return o;
    }

    private string TurnaroundKey(string groupBy, Order o, Filter f) => groupBy switch
    {
        "platform" => Label(o.Platform),
        "carrier" => Label(o.Carrier),
        "order_type" => Label(o.OrderType),
        "status" => Label(o.Status),
        "day" => DayKey(o, f.DateField, "yyyy-MM-dd"),
        "week" => WeekKey(o, f.DateField),
        "month" => DayKey(o, f.DateField, "yyyy-MM"),
        "weekday" => ExtraGroupKey("weekday", o),
        _ => "tất cả",
    };

    private AiToolResult Turnaround(JsonObject a)
    {
        var f = ReadFilter(a);
        var rows = Select(f);
        var metric = (Str(a, "metric") ?? "arrival_to_receipt").ToLowerInvariant();
        var groupBy = (Str(a, "group_by") ?? "none").ToLowerInvariant();

        var samples = new List<(Order O, double H)>();
        foreach (var o in rows)
        {
            DateTime? from, to;
            if (metric == "return_to_arrival")
            {
                from = ArchivePolicy.ParseRecvDate(o.ReturnDate);
                to = PhoneSnapshot.ArrivedAt(o);
            }
            else
            {
                from = PhoneSnapshot.ArrivedAt(o);
                to = PhoneSnapshot.IsReceived(o) ? ArchivePolicy.ParseRecvDate(o.RecvDate) : null;
            }
            if (from is null || to is null) continue;
            var h = Hours(from.Value, to.Value);
            if (h >= 0) samples.Add((o, h));
        }

        var res = new JsonObject
        {
            ["metric"] = metric,
            ["ordersConsidered"] = rows.Count,
            ["overall"] = Stats(samples.Select(s => s.H).ToList()),
        };
        if (groupBy != "none")
        {
            var groups = samples.GroupBy(s => TurnaroundKey(groupBy, s.O, f)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Take(20).ToList();
            var arr = new JsonArray();
            foreach (var g in groups)
            {
                var st = Stats(g.Select(x => x.H).ToList());
                st["group"] = MaskPhones(g.Key);
                arr.Add(st);
            }
            res["groups"] = arr;
        }
        var slowest = samples.OrderByDescending(s => s.H).Take(5).ToList();
        var refs = new List<AiOrderRef>();
        var slow = new JsonArray();
        foreach (var (o, h) in slowest)
        {
            _seen[o.Id] = o;
            refs.Add(new AiOrderRef(o.Id, o.OrderId, string.IsNullOrEmpty(o.Track) ? o.ReturnTrack : o.Track, o.Status));
            slow.Add(new JsonObject { ["id"] = o.Id, ["orderId"] = o.OrderId, ["track"] = o.Track, ["carrier"] = o.Carrier, ["hours"] = Math.Round(h, 1) });
        }
        res["slowest"] = slow;
        return new(res.ToJsonString(Json), refs);
    }

    // ===================================================== chất lượng dữ liệu

    private AiToolResult DataHealth(JsonObject a)
    {
        var f = ReadFilter(a) with { Status = null, From = null, To = null };
        var rows = Select(f);
        var refs = new List<AiOrderRef>();
        JsonObject Check(string label, Func<Order, bool> bad)
        {
            var hit = rows.Where(bad).ToList();
            var ex = new JsonArray();
            foreach (var o in hit.Take(3))
            {
                _seen[o.Id] = o;
                if (refs.Count < 30) refs.Add(new AiOrderRef(o.Id, o.OrderId, string.IsNullOrEmpty(o.Track) ? o.ReturnTrack : o.Track, o.Status));
                ex.Add(new JsonObject { ["id"] = o.Id, ["orderId"] = o.OrderId, ["track"] = o.Track, ["status"] = o.Status });
            }
            return new JsonObject { ["issue"] = label, ["count"] = hit.Count, ["examples"] = ex };
        }

        var dupTracks = rows.Where(o => !string.IsNullOrWhiteSpace(o.Track)).GroupBy(o => o.Track.Trim().ToUpperInvariant()).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var checks = new JsonArray
        {
            Check("Thiếu giá trị đơn", o => o.Price <= 0),
            Check("Thiếu đơn vị vận chuyển", o => string.IsNullOrWhiteSpace(o.Carrier)),
            Check("Đang chờ nhưng chưa có giờ hàng về", o => PhoneSnapshot.IsPending(o) && PhoneSnapshot.ArrivedAt(o) is null),
            Check("Đã nhận nhưng thiếu ngày nhận", o => PhoneSnapshot.IsReceived(o) && string.IsNullOrWhiteSpace(o.RecvDate)),
            Check("Ngày nhận trước giờ hàng về", o => PhoneSnapshot.IsReceived(o) && PhoneSnapshot.ArrivedAt(o) is { } ar && ArchivePolicy.ParseRecvDate(o.RecvDate) is { } rc && rc < ar),
            Check("Không có sản phẩm", o => o.Items.Count == 0),
            Check("Thiếu mã đơn", o => string.IsNullOrWhiteSpace(o.OrderId)),
            Check("Thiếu mã vận đơn", o => string.IsNullOrWhiteSpace(o.Track) && string.IsNullOrWhiteSpace(o.ReturnTrack)),
            Check("Mã vận đơn trùng giữa nhiều dòng", o => !string.IsNullOrWhiteSpace(o.Track) && dupTracks.Contains(o.Track.Trim().ToUpperInvariant())),
        };
        var res = new JsonObject { ["ordersChecked"] = rows.Count, ["checks"] = checks };
        return new(res.ToJsonString(Json), refs);
    }

    // ===================================================== mã trùng

    private AiToolResult Duplicates(JsonObject a)
    {
        var field = (Str(a, "field") ?? "track").ToLowerInvariant();
        var limit = Math.Clamp(Int(a, "limit") ?? 15, 1, 30);
        var f = ReadFilter(a) with { Status = null, From = null, To = null };
        var rows = Select(f);
        string Code(Order o) => (field == "return_track" ? o.ReturnTrack : o.Track) ?? "";
        var groups = rows.Where(o => !string.IsNullOrWhiteSpace(Code(o))).GroupBy(o => Code(o).Trim().ToUpperInvariant())
            .Where(g => g.Count() > 1).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
        var arr = new JsonArray();
        var refs = new List<AiOrderRef>();
        foreach (var g in groups.Take(limit))
        {
            var items = new JsonArray();
            foreach (var o in g)
            {
                _seen[o.Id] = o;
                if (refs.Count < 50) refs.Add(new AiOrderRef(o.Id, o.OrderId, string.IsNullOrEmpty(o.Track) ? o.ReturnTrack : o.Track, o.Status));
                items.Add(new JsonObject { ["id"] = o.Id, ["orderId"] = o.OrderId, ["status"] = o.Status, ["platform"] = o.Platform, ["value"] = (long)Math.Round(o.Price) });
            }
            arr.Add(new JsonObject { ["code"] = g.Key, ["rows"] = g.Count(), ["items"] = items });
        }
        var res = new JsonObject { ["field"] = field, ["duplicateGroups"] = groups.Count, ["shown"] = arr.Count, ["truncated"] = groups.Count > arr.Count, ["groups"] = arr };
        return new(res.ToJsonString(Json), refs);
    }

    // ===================================================== thống kê hoạt động

    private AiToolResult ActivityStats(JsonObject a)
    {
        if (_activity is null) return Error("Chưa đọc được nhật ký hoạt động.");
        var today = _clock().Date;
        var from = ParseDay(Str(a, "from")) ?? today.AddDays(-29);
        var to = ParseDay(Str(a, "to")) ?? today;
        var entries = _activity(5000, Str(a, "type") ?? "", "");
        var byType = new Dictionary<string, int>();
        var byDay = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var counted = 0;
        foreach (var e in entries)
        {
            var t = ArchivePolicy.ParseRecvDate(e.Time);
            if (t is null && DateTime.TryParse(e.Time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var p)) t = p;
            if (t is null) continue;
            var d = t.Value.Date;
            if (d < from || d > to) continue;
            counted++;
            byType[e.Label] = byType.GetValueOrDefault(e.Label) + 1;
            var key = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            byDay[key] = byDay.GetValueOrDefault(key) + 1;
        }
        var types = new JsonObject();
        foreach (var kv in byType.OrderByDescending(k => k.Value)) types[kv.Key] = kv.Value;
        var days = new JsonObject();
        foreach (var kv in byDay.TakeLast(31)) days[kv.Key] = kv.Value;
        var res = new JsonObject
        {
            ["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["to"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["total"] = counted, ["byType"] = types, ["byDay"] = days, ["note"] = entries.Count >= 5000 ? "Chỉ đọc 5000 dòng nhật ký mới nhất." : "",
        };
        return new(res.ToJsonString(Json), Array.Empty<AiOrderRef>());
    }

    // ===================================================== thông báo

    private AiToolResult Notifications(JsonObject a)
    {
        if (_notifications is null) return Error("Chưa đọc được trung tâm thông báo.");
        var all = _notifications().OrderByDescending(n => n.Time).ToList();
        var unreadOnly = Bool(a, "unread_only") == true;
        var list = all.Where(n => !unreadOnly || !n.Read).ToList();
        var limit = Math.Clamp(Int(a, "limit") ?? 15, 1, 50);
        var arr = new JsonArray();
        foreach (var n in list.Take(limit))
            arr.Add(new JsonObject
            {
                ["time"] = n.Time.ToString("dd/MM/yyyy HH:mm"), ["level"] = n.Level, ["source"] = n.Source,
                ["title"] = MaskPhones(n.Title), ["detail"] = MaskPhones(n.Detail.Length > 200 ? n.Detail[..200] : n.Detail), ["read"] = n.Read,
            });
        var res = new JsonObject { ["total"] = all.Count, ["unread"] = all.Count(n => !n.Read), ["shown"] = arr.Count, ["notifications"] = arr };
        return new(res.ToJsonString(Json), Array.Empty<AiOrderRef>());
    }
}
