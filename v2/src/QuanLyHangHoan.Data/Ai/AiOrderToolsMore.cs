using System.Globalization;
using System.Text.Json.Nodes;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.Data.Ai;

/// <summary>Công cụ bổ sung cho trợ lý AI (chạy ngầm, người dùng không thấy): so sánh hai kỳ, đơn nhiều kiện, bảng Hàng hoàn về, trạng thái hệ thống, hướng dẫn dùng phần mềm, thêm cách gom nhóm.</summary>
public sealed partial class AiOrderTools
{
    // Lưu ý: các thành viên dùng khi dựng định nghĩa công cụ là hằng/thuộc tính (không phải trường static) để không phụ thuộc thứ tự khởi tạo giữa các tệp.
    private static string[] AggregateGroups => new[]
    {
        "none", "status", "platform", "carrier", "order_type", "platform_status", "product", "day", "week", "month", "alert",
        "reason", "sku", "forward_carrier", "return_carrier", "weekday", "arrival_hour", "waiting_age", "watching", "partial", "price_range",
        "color", "size", "variant", "watch_reason",
    };

    private const string GroupByHelp =
        "Gom nhóm theo: status, platform (sàn), carrier (đơn vị vận chuyển), order_type, platform_status, product, sku, " +
        "day/week/month (theo date_field), alert (mức cảnh báo), reason (lý do hoàn/ghi chú), forward_carrier (hãng giao đi), return_carrier (hãng trả về), " +
        "weekday (thứ trong tuần), arrival_hour (giờ hàng về trong ngày), waiting_age (đã chờ bao lâu: <12h, 12-24h, 24-48h, 48-72h, >72h), " +
        "watching (có/không theo dõi), watch_reason (lý do theo dõi), partial (hoàn một phần hay toàn bộ), price_range (khoảng giá trị), " +
        "color (màu), size, variant (sản phẩm + màu + size), none (chỉ tổng).";

    private static readonly HashSet<string> ExtraGroupKeys = new(StringComparer.Ordinal)
    {
        "reason", "sku", "forward_carrier", "return_carrier", "weekday", "arrival_hour", "waiting_age", "watching", "partial", "price_range",
        "watch_reason",
    };

    /// <summary>Gom nhóm theo TỪNG SẢN PHẨM trong đơn (một đơn nhiều sản phẩm xuất hiện ở nhiều nhóm).</summary>
    private static Func<OrderItem, string>? ItemKey(string groupBy) => groupBy switch
    {
        "product" => i => Label(i.Product.Trim()),
        "sku" => i => Label((i.Sku ?? "").Trim()),
        "color" => i => Label((i.Color ?? "").Trim()),
        "size" => i => Label((i.Size ?? "").Trim()),
        "variant" => i => Label($"{i.Product.Trim()} / {(i.Color ?? "").Trim()} / {(i.Size ?? "").Trim()}".Trim(' ', '/')),
        _ => null,
    };

    private static IEnumerable<OrderItem> ItemsOrPlaceholder(Order o) =>
        o.Items.Count == 0 ? new[] { new OrderItem { Product = "(không có sản phẩm)", Qty = 1 } } : o.Items;

    private static readonly string[] Weekdays = { "Chủ nhật", "Thứ hai", "Thứ ba", "Thứ tư", "Thứ năm", "Thứ sáu", "Thứ bảy" };

    private string ExtraGroupKey(string g, Order o)
    {
        switch (g)
        {
            case "reason":
                {
                    var r = MaskPhones((o.Note ?? "").Trim());
                    return r.Length == 0 ? "(không ghi lý do)" : (r.Length > 60 ? r[..60] : r);
                }
            case "sku": return Label(o.Items.Select(i => i.Sku).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim());
            case "forward_carrier": return Label(o.Import.ForwardCarrier);
            case "return_carrier": return Label(o.Import.ReturnCarrier);
            case "weekday":
                return OrderDate(o, "recv") is { } d ? Weekdays[(int)d.DayOfWeek] : (OrderDate(o, "arrived") is { } a ? Weekdays[(int)a.DayOfWeek] : "(không có ngày)");
            case "arrival_hour":
                return PhoneSnapshot.ArrivedAt(o) is { } t ? $"{t.Hour:00}h" : "(chưa có giờ hàng về)";
            case "waiting_age":
                {
                    if (!PhoneSnapshot.IsPending(o)) return "đã xử lý";
                    if (PhoneSnapshot.ArrivedAt(o) is not { } at) return "chưa có giờ hàng về";
                    var h = (_clock() - at).TotalHours;
                    return h < 12 ? "<12h" : h < 24 ? "12-24h" : h < 48 ? "24-48h" : h < 72 ? "48-72h" : ">72h";
                }
            case "watching": return o.IsWatchlist ? "đang theo dõi" : "không theo dõi";
            case "watch_reason":
                {
                    if (!o.IsWatchlist) return "(không theo dõi)";
                    var r = MaskPhones((o.WatchlistReason ?? "").Trim());
                    return r.Length == 0 ? "(không ghi lý do)" : (r.Length > 60 ? r[..60] : r);
                }
            case "partial": return o.IsPartialReturn ? "hoàn một phần" : "hoàn toàn bộ";
            case "price_range":
                return o.Price <= 0 ? "(chưa có giá)" : o.Price < 100_000 ? "dưới 100k" : o.Price < 300_000 ? "100k-300k" : o.Price < 500_000 ? "300k-500k" : "từ 500k";
            default: return "tất cả";
        }
    }

    // ===================================================== định nghĩa các công cụ bổ sung

    private static IEnumerable<JsonObject> ExtraTools()
    {
        var compare = FilterProps();
        compare["preset"] = Prop("string",
            "Kỳ so sánh có sẵn: today_vs_yesterday, this_week_vs_last_week (tuần này tới hôm nay so với cùng số ngày tuần trước), " +
            "this_month_vs_last_month (tháng này tới hôm nay so với cùng số ngày tháng trước), last_7_days_vs_previous_7_days.",
            "today_vs_yesterday", "this_week_vs_last_week", "this_month_vs_last_month", "last_7_days_vs_previous_7_days");
        compare["a_from"] = Prop("string", "Kỳ A (kỳ hiện tại) từ ngày yyyy-MM-dd/today/yesterday. Dùng thay cho preset khi so sánh khoảng tùy ý.");
        compare["a_to"] = Prop("string", "Kỳ A đến ngày.");
        compare["b_from"] = Prop("string", "Kỳ B (kỳ để so) từ ngày. Bỏ trống = khoảng cùng độ dài ngay trước kỳ A.");
        compare["b_to"] = Prop("string", "Kỳ B đến ngày.");
        compare.Remove("from");
        compare.Remove("to");

        var groups = FilterProps();
        groups["min_parcels"] = Prop("integer", "Số kiện tối thiểu của một mã đơn, mặc định 2.");
        groups["limit"] = Prop("integer", "Số mã đơn tối đa, mặc định 15, tối đa 30.");

        yield return Tool("compare_periods",
            "So sánh hai kỳ (hôm nay với hôm qua, tuần này với tuần trước, tháng này với tháng trước, hoặc hai khoảng tùy ý) theo số kiện, tổng giá trị, số lượng và phần trăm thay đổi. Dùng cho 'so với', 'tăng hay giảm', 'hơn kém bao nhiêu'.",
            compare);
        yield return Tool("order_groups",
            "Các mã đơn có NHIỀU KIỆN hoàn (kiện A/B/C): mỗi mã đơn liệt kê từng kiện với mã vận đơn, trạng thái, giá trị riêng. Dùng khi hỏi đơn nào hoàn nhiều kiện, đã nhận kiện nào còn thiếu kiện nào.",
            groups);
        yield return Tool("return_watch",
            "Bảng 'Hàng hoàn về': các đơn khách đã yêu cầu hoàn đang được theo dõi theo 4 chặng (1 Đã đồng ý, 2 Đang về, 3 Đã về shop, 4 Đã nhận kho). Trả về số đơn theo chặng và danh sách.",
            new JsonObject
            {
                ["stage"] = Prop("string", "Chỉ một chặng: 1/2/3/4 hoặc tên chặng (để trống = tất cả).", "1", "2", "3", "4"),
                ["limit"] = Prop("integer", "Số dòng tối đa, mặc định 15, tối đa 50."),
            });
        yield return Tool("system_status",
            "Tình trạng hệ thống: kết nối đồng bộ điện thoại, số thông báo chưa đọc, bản sao lưu gần nhất, số đơn theo từng nơi (đang xử lý/lưu trữ/đã xóa), thời điểm hiện tại. Dùng khi hỏi 'điện thoại có kết nối không', 'đã sao lưu chưa'.",
            new JsonObject());
        yield return Tool("app_guide",
            "Hướng dẫn sử dụng phần mềm (nạp đơn, quét nhận hàng, đồng bộ điện thoại, cảnh báo, lưu trữ, sao lưu, tìm kiếm, theo dõi, trợ lý AI...). Dùng khi người dùng hỏi 'làm sao', 'ở đâu', 'phần mềm có tính năng gì'. Chỉ nói những gì có trong hướng dẫn, không bịa tính năng.",
            new JsonObject { ["topic"] = Prop("string", "Chủ đề hoặc từ khóa, ví dụ 'nạp đơn', 'quét', 'sao lưu', 'điện thoại', 'cảnh báo'. Để trống để xem danh sách chủ đề.") });
    }

    // ===================================================== so sánh hai kỳ

    private (DateTime From, DateTime To, DateTime BFrom, DateTime BTo)? PeriodsFor(JsonObject a)
    {
        var today = _clock().Date;
        var preset = Str(a, "preset")?.ToLowerInvariant();
        switch (preset)
        {
            case "today_vs_yesterday":
                return (today, today, today.AddDays(-1), today.AddDays(-1));
            case "this_week_vs_last_week":
                {
                    var mon = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                    var span = (today - mon).Days;
                    var bFrom = mon.AddDays(-7);
                    return (mon, today, bFrom, bFrom.AddDays(span));
                }
            case "this_month_vs_last_month":
                {
                    var first = new DateTime(today.Year, today.Month, 1);
                    var span = today.Day - 1;
                    var prevFirst = first.AddMonths(-1);
                    var prevLast = first.AddDays(-1);
                    var bTo = prevFirst.AddDays(span) > prevLast ? prevLast : prevFirst.AddDays(span);
                    return (first, today, prevFirst, bTo);
                }
            case "last_7_days_vs_previous_7_days":
                return (today.AddDays(-6), today, today.AddDays(-13), today.AddDays(-7));
        }
        var af = ParseDay(Str(a, "a_from"));
        var at = ParseDay(Str(a, "a_to")) ?? af;
        if (af is null || at is null || at < af) return null;
        var bf = ParseDay(Str(a, "b_from"));
        var bt = ParseDay(Str(a, "b_to")) ?? bf;
        if (bf is null || bt is null)
        {
            var len = (at.Value - af.Value).Days + 1;
            bt = af.Value.AddDays(-1);
            bf = bt.Value.AddDays(-(len - 1));
        }
        return (af.Value, at.Value, bf.Value, bt.Value);
    }

    private AiToolResult ComparePeriods(JsonObject a)
    {
        var p = PeriodsFor(a);
        if (p is null) return Error("Cần chọn preset hoặc a_from/a_to hợp lệ.");
        var f = ReadFilter(a);
        JsonObject Period(DateTime from, DateTime to)
        {
            var rows = Select(f with { From = from, To = to });
            return new JsonObject
            {
                ["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["to"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["count"] = (long)rows.Count, ["value"] = Value(rows), ["quantity"] = (long)rows.Sum(Quantity),
            };
        }
        var cur = Period(p.Value.From, p.Value.To);
        var prev = Period(p.Value.BFrom, p.Value.BTo);
        JsonNode Delta(string k)
        {
            var c = cur[k]!.GetValue<long>();
            var b = prev[k]!.GetValue<long>();
            var o = new JsonObject { ["diff"] = c - b };
            if (b != 0) o["percent"] = Math.Round((c - b) * 100.0 / b, 1);
            return o;
        }
        var res = new JsonObject
        {
            ["dateField"] = f.DateField,
            ["periodA"] = cur, ["periodB"] = prev,
            ["change"] = new JsonObject { ["count"] = Delta("count"), ["value"] = Delta("value"), ["quantity"] = Delta("quantity") },
        };
        return new(res.ToJsonString(Json), Array.Empty<AiOrderRef>());
    }

    // ===================================================== đơn nhiều kiện

    private AiToolResult OrderGroups(JsonObject a)
    {
        var rows = Select(ReadFilter(a));
        var min = Math.Clamp(Int(a, "min_parcels") ?? 2, 2, 20);
        var limit = Math.Clamp(Int(a, "limit") ?? 15, 1, 30);
        var groups = rows.Where(o => !string.IsNullOrWhiteSpace(o.OrderId)).GroupBy(o => o.OrderId)
            .Where(g => g.Count() >= min).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
        var arr = new JsonArray();
        var refs = new List<AiOrderRef>();
        foreach (var g in groups.Take(limit))
        {
            var parcels = new JsonArray();
            foreach (var o in g)
            {
                _seen[o.Id] = o;
                if (refs.Count < 50) refs.Add(new AiOrderRef(o.Id, o.OrderId, string.IsNullOrEmpty(o.Track) ? o.ReturnTrack : o.Track, o.Status));
                parcels.Add(new JsonObject
                {
                    ["id"] = o.Id, ["track"] = o.Track, ["returnTrack"] = o.ReturnTrack, ["status"] = o.Status, ["value"] = (long)Math.Round(o.Price),
                    ["product"] = MaskPhones(o.Items.FirstOrDefault()?.Product ?? ""),
                });
            }
            arr.Add(new JsonObject
            {
                ["orderId"] = g.Key, ["parcels"] = g.Count(), ["received"] = g.Count(PhoneSnapshot.IsReceived), ["waiting"] = g.Count(PhoneSnapshot.IsPending),
                ["value"] = Value(g), ["items"] = parcels,
            });
        }
        var res = new JsonObject { ["groupsTotal"] = groups.Count, ["shown"] = arr.Count, ["truncated"] = groups.Count > arr.Count, ["groups"] = arr };
        return new(res.ToJsonString(Json), refs);
    }

    // ===================================================== bảng Hàng hoàn về

    private AiToolResult ReturnWatch(JsonObject a)
    {
        if (_returnWatch is null) return Error("Chưa đọc được bảng Hàng hoàn về.");
        var all = Load();
        var marks = _returnWatch(all);
        var stage = Str(a, "stage") is { } s ? StageOf(s) : null;
        var entries = all.Where(x => marks.ContainsKey(x.Order.Id)).Select(x => (x.Order, marks[x.Order.Id].Since, marks[x.Order.Id].Stage)).ToList();
        var byStage = new JsonObject();
        for (var st = 1; st <= 4; st++)
        {
            var inStage = entries.Where(e => e.Stage == st).ToList();
            byStage[ReturnWatchService.StageNames[st]] = new JsonObject { ["count"] = inStage.Count, ["value"] = Value(inStage.Select(e => e.Order)) };
        }
        var shown = entries.Where(e => stage is null || e.Stage == stage).OrderBy(e => e.Stage).ThenBy(e => e.Since).ToList();
        var limit = Math.Clamp(Int(a, "limit") ?? 15, 1, 50);
        var arr = new JsonArray();
        var refs = new List<AiOrderRef>();
        foreach (var e in shown.Take(limit))
        {
            var o = e.Order;
            _seen[o.Id] = o;
            refs.Add(new AiOrderRef(o.Id, o.OrderId, string.IsNullOrEmpty(o.Track) ? o.ReturnTrack : o.Track, o.Status));
            arr.Add(new JsonObject
            {
                ["id"] = o.Id, ["orderId"] = o.OrderId, ["track"] = o.Track, ["returnTrack"] = o.ReturnTrack, ["platform"] = o.Platform,
                ["stage"] = ReturnWatchService.StageNames[e.Stage], ["enteredTable"] = DateTimeOffset.FromUnixTimeMilliseconds(e.Since).ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                ["value"] = (long)Math.Round(o.Price), ["platformStatus"] = o.PlatformStatus,
            });
        }
        var res = new JsonObject { ["total"] = entries.Count, ["byStage"] = byStage, ["shown"] = arr.Count, ["truncated"] = shown.Count > arr.Count, ["orders"] = arr };
        return new(res.ToJsonString(Json), refs);
    }

    private static int? StageOf(string s)
    {
        var l = s.ToLowerInvariant();
        if (int.TryParse(l, out var n) && n is >= 1 and <= 4) return n;
        if (l.Contains("đồng ý")) return 1;
        if (l.Contains("đang về")) return 2;
        if (l.Contains("về shop")) return 3;
        if (l.Contains("nhận kho")) return 4;
        return null;
    }

    // ===================================================== tình trạng hệ thống

    private AiToolResult SystemStatus()
    {
        var all = Load();
        var res = new JsonObject
        {
            ["now"] = _clock().ToString("yyyy-MM-dd HH:mm"),
            ["orders"] = new JsonObject
            {
                ["active"] = all.Count(s => !s.IsArchived && !s.IsDeleted),
                ["archived"] = all.Count(s => s.IsArchived && !s.IsDeleted),
                ["deleted"] = all.Count(s => s.IsDeleted),
            },
        };
        if (_status is not null)
        {
            try
            {
                foreach (var kv in _status())
                    res[kv.Key] = ToNode(kv.Value);
            }
            catch (Exception ex) { res["statusError"] = ex.Message; }
        }
        return new(res.ToJsonString(Json), Array.Empty<AiOrderRef>());
    }

    private static JsonNode? ToNode(object? v) => v switch
    {
        null => null,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        _ => JsonValue.Create(v.ToString()),
    };

    // ===================================================== hướng dẫn dùng phần mềm

    private static readonly (string Key, string Title, string Text)[] Guide =
    {
        ("tong_quan", "Phần mềm làm gì",
            "Quản lý và đối soát hàng hoàn Shopee, TikTok Shop, Lazada. Mỗi KIỆN hoàn là một dòng riêng (một mã đơn có thể có kiện A/B/C, mỗi kiện một mã vận đơn, trạng thái và ngày nhận riêng). " +
            "Nhận kiện A không làm nhận kiện B/C. Cảnh báo: vàng sau 12 giờ, đỏ sau 24 giờ kể từ lúc hàng về mà chưa nhận. Dữ liệu nằm trên máy tính; điện thoại là bản sao để quét và tra cứu."),
        ("nap_don", "Nạp đơn vào phần mềm",
            "Có 4 đường, đều nằm trong Cài đặt: (1) Google Sheet: tự lấy qua Apps Script mỗi 3 phút, dán địa chỉ Apps Script một lần. (2) Theo dõi thư mục: tự nạp file đặt vào thư mục được chọn. " +
            "(3) Salework: tự động theo cấu hình. (4) Nạp file Excel (TikTok): thủ công, chỉ dành cho file TikTok. Trước mỗi lần nạp file phần mềm tự sao lưu; nạp lại cùng kiện thì cập nhật, không cộng lặp sản phẩm."),
        ("quet_nhan_hang", "Quét nhận hàng",
            "Nhận kiện bằng cách quét mã vận đơn (hoặc mã đơn) ở màn Quét nhận hàng trên máy tính, hoặc bằng camera trên app điện thoại. Mã giao đi dùng chung hoặc mã đơn gốc có nhiều ứng viên thì phần mềm báo cần kiểm tra thay vì tự đoán. " +
            "Máy tính chỉ xác nhận sau khi đã lưu bền. Quét trùng kiện đã nhận sẽ báo đã nhận trước đó."),
        ("dien_thoai", "Đồng bộ với điện thoại",
            "Màn Đồng bộ điện thoại hiện mã phòng: nhập đúng mã đó vào app iPhone. Hai bên nói chuyện qua MQTT và ntfy; có thể nhập máy chủ MQTT riêng ở ô 'Máy chủ MQTT riêng' (cả máy tính lẫn điện thoại cùng địa chỉ). " +
            "Máy tính gửi danh sách đơn sang điện thoại; điện thoại quét nhận gửi về máy tính. Ảnh chụp lúc quét lưu ngay trên điện thoại trong chi tiết đơn, không gửi sang máy tính."),
        ("canh_bao", "Cảnh báo và thông báo",
            "Đơn đang chờ chuyển vàng sau 12 giờ, đỏ sau 24 giờ kể từ lúc hàng về. Trang chủ có khối 'Cần xử lý ngay': bấm vào để tới đúng đơn trong bảng (đơn được nhấp nháy). " +
            "Có thông báo Windows cho cảnh báo vàng/đỏ (bật tắt trong Cài đặt) và trung tâm thông báo ở biểu tượng chuông. Điện thoại có thông báo nhắc đơn sắp đến hạn."),
        ("luu_tru_xoa", "Kho lưu trữ và đã xóa",
            "Đơn đã nhận được dời sang Kho lưu trữ theo quy tắc lưu trữ của phần mềm. Đơn bị xóa nằm ở màn Đã xóa và có thể khôi phục. Cả hai bảng đều có phân trang, bộ lọc theo sản phẩm và ngày."),
        ("sao_luu", "Sao lưu dữ liệu",
            "Cài đặt có bật tự sao lưu theo chu kỳ (mặc định mỗi 2 giờ, giữ 7 ngày) và nút sao lưu ngay. Trước mỗi lần nạp file cũng tự sao lưu; nếu sao lưu hỏng thì không nạp. Có nút mở thư mục sao lưu."),
        ("tim_kiem_loc", "Tìm kiếm, lọc, thao tác nhanh",
            "Ctrl+K tìm toàn cục theo mã vận đơn, mã đơn, mã hoàn, tên sản phẩm, ghi chú. Các bảng đơn chia trang 100 dòng, có ô nhập số trang, bộ lọc nâng cao (sản phẩm, đơn vị vận chuyển, ngày) và sắp xếp theo cột. " +
            "Bấm vào mã đơn hoặc mã vận đơn để tự sao chép. Chọn nhiều dòng để thao tác hàng loạt. Nhấp đôi một dòng để mở chi tiết đơn."),
        ("theo_doi", "Theo dõi đơn",
            "Gắn sao (theo dõi) một hoặc nhiều đơn kèm lý do; xem ở màn Đang theo dõi. Theo dõi hàng loạt có trên thanh thao tác khi chọn nhiều dòng."),
        ("hang_hoan_ve", "Bảng Hàng hoàn về",
            "Theo dõi các đơn khách đã yêu cầu hoàn qua 4 chặng: Đã đồng ý, Đang về, Đã về shop, Đã nhận kho. Đơn quét nhận xong chuyển sang chặng Đã nhận kho."),
        ("thong_ke", "Thống kê",
            "Trang chủ có chỉ số tổng quan, xu hướng 7 ngày và sản phẩm bị hoàn nhiều nhất theo hôm nay/tuần/tháng. Màn Thống kê đơn và sản phẩm xem theo ngày, tuần, tháng."),
        ("tro_ly_ai", "Trợ lý AI",
            "Nút ✨ ở góc phải dưới mọi màn hình mở cửa sổ hỏi đáp. Khóa API (Groq, Google) và danh sách mô hình nhập ở Cài đặt. Trợ lý chỉ đọc dữ liệu, không sửa gì; dữ liệu gửi đi không có tên, số điện thoại, địa chỉ người nhận. Hết hạn mức một mô hình thì tự chuyển mô hình khác."),
    };

    private AiToolResult AppGuide(JsonObject a)
    {
        var topic = Str(a, "topic");
        if (topic is null || topic.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var idx = new JsonArray();
            foreach (var g in Guide) idx.Add(new JsonObject { ["topic"] = g.Key, ["title"] = g.Title });
            return new(new JsonObject { ["topics"] = idx, ["note"] = "Gọi lại app_guide với topic cụ thể để xem chi tiết." }.ToJsonString(Json), Array.Empty<AiOrderRef>());
        }
        var t = topic.ToLowerInvariant();
        var hits = Guide.Where(g => g.Key == t || g.Title.ToLowerInvariant().Contains(t) || g.Text.ToLowerInvariant().Contains(t)).ToList();
        if (hits.Count == 0)
            hits = Guide.Where(g => t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(w => w.Length >= 3 && (g.Title + " " + g.Text).ToLowerInvariant().Contains(w))).ToList();
        if (hits.Count == 0) return Error("Chưa có hướng dẫn cho chủ đề này. Gọi app_guide không có topic để xem danh sách.");
        var arr = new JsonArray();
        foreach (var g in hits.Take(3)) arr.Add(new JsonObject { ["topic"] = g.Key, ["title"] = g.Title, ["guide"] = g.Text });
        return new(new JsonObject { ["matches"] = arr }.ToJsonString(Json), Array.Empty<AiOrderRef>());
    }
}
