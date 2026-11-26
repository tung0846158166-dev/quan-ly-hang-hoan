using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.Data.Ai;

/// <summary>Đơn liên quan trong câu trả lời, để giao diện cho bấm mở chi tiết.</summary>
public sealed record AiOrderRef(string Id, string OrderId, string Track, string Status);

public sealed record AiToolResult(string Json, IReadOnlyList<AiOrderRef> Refs);

/// <summary>
/// Công cụ CHỈ ĐỌC cho trợ lý AI. Số liệu do mã tính, AI chỉ diễn đạt lại.
/// Quyền riêng tư: chỉ trả về các trường trong danh sách trắng bên dưới. KHÔNG bao giờ trả tên/số điện thoại/địa chỉ người nhận
/// (không có trong mô hình đơn; nằm trong RawDetails/ExtraJson/BuyerNote nên cố ý không lấy), và ghi chú còn bị che mọi dãy giống số điện thoại.
/// </summary>
public sealed partial class AiOrderTools
{
    internal static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly Func<IReadOnlyList<StoredOrder>> _loader;
    private readonly Func<DateTime> _clock;
    private readonly Func<int, string, string, IReadOnlyList<AuditEntry>>? _activity;
    private readonly Func<IReadOnlyDictionary<string, object?>>? _status;
    private readonly Func<IEnumerable<StoredOrder>, IReadOnlyDictionary<string, (long Since, int Stage)>>? _returnWatch;
    private readonly Func<IReadOnlyList<AiNotice>>? _notifications;
    private readonly Dictionary<string, Order> _seen = new(StringComparer.Ordinal);

    /// <param name="activity">Đọc nhật ký hoạt động (giới hạn, loại, chữ lọc). null = công cụ nhật ký báo chưa có.</param>
    public AiOrderTools(Func<IReadOnlyList<StoredOrder>> loader, Func<DateTime>? clock = null,
        Func<int, string, string, IReadOnlyList<AuditEntry>>? activity = null,
        Func<IReadOnlyDictionary<string, object?>>? status = null,
        Func<IEnumerable<StoredOrder>, IReadOnlyDictionary<string, (long Since, int Stage)>>? returnWatch = null,
        Func<IReadOnlyList<AiNotice>>? notifications = null)
    {
        _loader = loader;
        _clock = clock ?? (() => DateTime.Now);
        _activity = activity;
        _status = status;
        _returnWatch = returnWatch;
        _notifications = notifications;
    }

    private IReadOnlyList<StoredOrder>? _cache;
    private long _cacheTick;

    // Một câu hỏi có thể gọi nhiều công cụ liên tiếp: đọc CSDL một lần cho cả câu hỏi (thả ngay khi trả lời xong để không giữ RAM).
    private IReadOnlyList<StoredOrder> Load()
    {
        var now = Environment.TickCount64;
        if (_cache is null || now - _cacheTick > 30_000) { _cache = _loader(); _cacheTick = now; }
        return _cache;
    }

    public void ReleaseCache() => _cache = null;

    /// <summary>Tìm đơn đã từng được trả về trong phiên này (để mở chi tiết khi người dùng bấm).</summary>
    public Order? TryGetOrder(string id) => _seen.TryGetValue(id, out var o) ? o : null;

    private static readonly Regex PhoneLike = new(@"(\+?84|0)[\s.\-]?\d(?:[\s.\-]?\d){8,9}", RegexOptions.Compiled);
    private static readonly Regex LongDigits = new(@"(?<![A-Za-z0-9])\d{9,12}(?![A-Za-z0-9])", RegexOptions.Compiled);

    /// <summary>Che dãy giống số điện thoại trong văn bản tự do (ghi chú). Mã vận đơn (có chữ) không bị che.</summary>
    public static string MaskPhones(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var t = PhoneLike.Replace(text, "[ẩn SĐT]");
        return LongDigits.Replace(t, m => m.Value.StartsWith('0') ? "[ẩn SĐT]" : m.Value);
    }

    // ===================================================== định nghĩa công cụ (định dạng OpenAI/Groq/Gemini)

    public static readonly string DefinitionsJson = BuildDefinitions();

    private static JsonObject Prop(string type, string description, params string[] enumValues)
    {
        var p = new JsonObject { ["type"] = type, ["description"] = description };
        if (enumValues.Length > 0) p["enum"] = new JsonArray(enumValues.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());
        return p;
    }

    /// <summary>Bộ lọc dùng chung cho mọi công cụ truy vấn đơn. Mô tả CỐ Ý ngắn: gói miễn phí của Groq giới hạn 7.000-8.000 token mỗi yêu cầu.</summary>
    private static JsonObject FilterProps(bool withProduct = true)
    {
        var o = new JsonObject
        {
            ["status"] = Prop("string", "Đang chờ=chưa nhận/chưa về; Đã nhận=đã về kho", "Đang chờ", "Đã nhận", "Không cần nhận", "Bị mất"),
            ["date_field"] = Prop("string", "recv=ngày nhận (mặc định), return=ngày hoàn, ship=ngày giao, imported=ngày nạp, arrived=giờ hàng về", "recv", "return", "ship", "imported", "arrived"),
            ["from"] = Prop("string", "yyyy-MM-dd|today|yesterday"),
            ["to"] = Prop("string", "yyyy-MM-dd|today|yesterday"),
            ["platform"] = Prop("string", "Shopee|TikTok Shop|Lazada"),
            ["carrier"] = Prop("string", "SPX, GHN..."),
            ["order_type"] = Prop("string", "loại đơn"),
            ["platform_status"] = Prop("string", "trạng thái trên sàn"),
            ["alert"] = Prop("string", "đơn chờ: yellow>12h, red>24h, warning=vàng+đỏ, safe", "yellow", "red", "warning", "safe"),
            ["watching"] = Prop("boolean", "chỉ đơn theo dõi"),
            ["partial_return"] = Prop("boolean", "chỉ hoàn một phần"),
            ["min_value"] = Prop("number", "đồng"),
            ["max_value"] = Prop("number", "đồng"),
            ["text"] = Prop("string", "trong ghi chú"),
            ["scope"] = Prop("string", "mặc định active", "active", "archived", "deleted", "all"),
        };
        if (withProduct) o["product"] = Prop("string", "tên hoặc SKU");
        return o;
    }

    private static Dictionary<string, string> ShortDescriptions => new()   // thuộc tính (không phải trường static): tránh phụ thuộc thứ tự khởi tạo
    {
        ["overview"] = "Tổng quan: đơn đang chờ + tiền, cảnh báo vàng/đỏ, nhận hôm nay/hôm qua, đang theo dõi.",
        ["count_orders"] = "Đếm kiện + tổng tiền + số lượng theo bộ lọc. 'Đơn về hôm nay' = status Đã nhận, date_field recv, from=to=today.",
        ["aggregate"] = "Gom nhóm: số kiện/tiền/số lượng theo sàn, vận chuyển, sản phẩm, ngày, tuần, tháng, cảnh báo... Cho 'tổng tiền', 'nhiều nhất', 'theo ...'.",
        ["list_orders"] = "Liệt kê kiện (tối đa 30) kèm tiền, giờ hàng về, cảnh báo. Đơn bấm được.",
        ["find_order"] = "Tìm kiện theo mã đơn/mã vận đơn (khớp một phần).",
        ["order_detail"] = "Chi tiết đầy đủ MỘT đơn: sản phẩm, mã, mốc thời gian, ghi chú.",
        ["product_stats"] = "Sản phẩm: số lượng + số đơn, giảm dần.",
        ["recent_activity"] = "Nhật ký hoạt động gần đây (nạp, quét, sửa, xóa).",
        ["compare_periods"] = "So sánh hai kỳ (hôm nay/hôm qua, tuần, tháng...) theo số kiện, tiền, % thay đổi.",
        ["order_groups"] = "Mã đơn có nhiều kiện A/B/C, liệt kê từng kiện.",
        ["return_watch"] = "Bảng Hàng hoàn về theo 4 chặng: số đơn + danh sách.",
        ["system_status"] = "Tình trạng: kết nối điện thoại, sao lưu, thông báo, số đơn.",
        ["app_guide"] = "Hướng dẫn dùng phần mềm theo chủ đề (không bịa tính năng).",
        ["search_orders"] = "Tìm tự do nhiều từ khóa (mã, sản phẩm, ghi chú...).",
        ["turnaround_stats"] = "Thời gian xử lý (giờ): TB, trung vị, p90, % trong 12h/24h, đơn chậm nhất; tách theo sàn/vận chuyển.",
        ["data_health"] = "Kiểm tra dữ liệu thiếu/sai (thiếu giá, ngày nhận, mã trùng...).",
        ["duplicates"] = "Mã vận đơn bị trùng giữa các dòng.",
        ["activity_stats"] = "Đếm hoạt động theo loại/ngày từ nhật ký.",
        ["notifications"] = "Thông báo trong chuông (chưa đọc, mức, nội dung).",
    };

    /// <summary>Rút gọn mô tả công cụ và thuộc tính để mỗi yêu cầu gửi AI nhỏ (token ít).</summary>
    private static void Compact(JsonArray tools)
    {
        foreach (var t in tools)
        {
            var fn = t!["function"]!.AsObject();
            var name = fn["name"]!.GetValue<string>();
            if (ShortDescriptions.TryGetValue(name, out var d)) fn["description"] = d;
            if (fn["parameters"]?["properties"] is JsonObject props)
                foreach (var kv in props)
                    if (kv.Value is JsonObject p && p["description"] is JsonValue dv && dv.GetValue<string>() is { Length: > 80 } longText)
                        p["description"] = longText[..80];
        }
    }

    /// <summary>Chọn các công cụ liên quan tới câu hỏi (theo từ khóa, không phân biệt dấu) để mỗi yêu cầu chỉ gửi vài công cụ thay vì tất cả.</summary>
    public static string DefinitionsFor(string question)
    {
        var all = JsonNode.Parse(DefinitionsJson)!.AsArray();
        var q = Fold(question);
        var want = new HashSet<string>(StringComparer.Ordinal) { "count_orders", "aggregate", "list_orders" };
        void Add(params string[] names) { foreach (var n in names) want.Add(n); }
        bool Has(params string[] keys) => keys.Any(q.Contains);
        var codeLike = System.Text.RegularExpressions.Regex.IsMatch(question ?? "", @"\b(?=[A-Za-z0-9-]*\d)(?=[A-Za-z0-9-]*[A-Za-z])[A-Za-z0-9-]{6,}\b|\b\d{6,}\b");
        if (codeLike || Has("tim ", "chi tiet", "van don", "ma don", "ma van", "don nao", "tracking")) Add("find_order", "order_detail", "search_orders");
        if (Has("tinh hinh", "tong quan", "can xu ly", "co gi", "hom nay co")) Add("overview");
        if (Has("san pham", "mau ", "size", "sku", "ao ", "quan ")) Add("product_stats");
        if (Has("so voi", "so sanh", "tang ", "giam ", "hom qua", "tuan truoc", "thang truoc", "tuan nay", "thang nay", "hon kem")) Add("compare_periods");
        if (Has("bao lau", "thoi gian xu ly", "nhanh", "cham", "trung binh", "xu ly")) Add("turnaround_stats");
        if (Has("nhieu kien", "kien a", "kien b", "thieu kien", "tach kien", "nhom don")) Add("order_groups");
        if (Has("hoan ve", "chang", "dang ve", "da dong y", "ve shop")) Add("return_watch");
        if (Has("loi du lieu", "thieu", "sai ", "kiem tra du lieu", "chat luong")) Add("data_health");
        if (Has("trung")) Add("duplicates");
        if (Has("nhat ky", "hoat dong", "lich su", "da lam", "nap don", "bao nhieu lan")) Add("recent_activity", "activity_stats");
        if (Has("ket noi", "sao luu", "backup", "thong bao", "dien thoai", "trang thai he thong")) Add("system_status", "notifications");
        if (Has("lam sao", "o dau", "huong dan", "cach ", "tinh nang", "cai dat", "dung phan mem", "man hinh", "muc nao", "nut ", "la gi", "y nghia", "phan mem co", "sheet", "bam vao")) Add("app_guide");
        if (want.Count == 3) Add("find_order");   // tổng quan đã có sẵn trong số liệu hiện tại của lời nhắc hệ thống
        var picked = new JsonArray();
        foreach (var t in all)
            if (want.Contains(t!["function"]!["name"]!.GetValue<string>())) picked.Add(t.DeepClone());
        return picked.ToJsonString(Json);
    }

    private static string Fold(string? s)
    {
        var d = (s ?? "").Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var ch in d)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch == 'đ' ? 'd' : ch == 'Đ' ? 'd' : char.ToLowerInvariant(ch));
        }
        return sb.ToString() + " ";
    }

    private static JsonObject Tool(string name, string description, JsonObject props, params string[] required)
    {
        var parameters = new JsonObject { ["type"] = "object", ["properties"] = props };
        if (required.Length > 0) parameters["required"] = new JsonArray(required.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray());
        return new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = name, ["description"] = description, ["parameters"] = parameters },
        };
    }

    private static string BuildDefinitions()
    {
        var listProps = FilterProps();
        listProps["sort_by"] = Prop("string", "mặc định: đang chờ=cũ nhất trước, còn lại=mới nhất trước",
            "arrived", "recv_date", "return_date", "value");
        listProps["descending"] = Prop("boolean", "true=giảm dần");
        listProps["limit"] = Prop("integer", "mặc định 12, tối đa 30");

        var aggProps = FilterProps();
        aggProps["group_by"] = Prop("string", GroupByHelp,
            AggregateGroups);
        aggProps["metric"] = Prop("string", "xếp nhóm theo: count (mặc định), value, quantity",
            "count", "value", "quantity");
        aggProps["top"] = Prop("integer", "mặc định 20, tối đa 60");

        var tools = new JsonArray
        {
            Tool("overview",
                "Tổng quan nhanh tình hình hiện tại: hôm nay nhận bao nhiêu, đang chờ bao nhiêu và tổng giá trị, số đơn cảnh báo vàng/đỏ, đơn đang theo dõi, đơn nạp hôm nay. Dùng khi hỏi chung chung 'tình hình hôm nay', 'có gì cần xử lý'.",
                new JsonObject()),
            Tool("count_orders",
                "Đếm kiện hoàn theo bộ lọc. Trả về tổng số kiện, tổng giá trị (đồng), tổng số lượng sản phẩm và phân loại theo trạng thái, sàn. 'Đơn về hôm nay' = status 'Đã nhận', date_field 'recv', from=to=today.",
                FilterProps()),
            Tool("aggregate",
                "Thống kê gom nhóm: đếm kiện / tổng giá trị / tổng số lượng theo từng trạng thái, sàn, đơn vị vận chuyển, loại đơn, sản phẩm, ngày, tuần, tháng hoặc mức cảnh báo. Dùng cho mọi câu hỏi 'bao nhiêu ... theo ...', 'tổng tiền ...', 'nhiều nhất', 'so sánh', 'xu hướng'.",
                aggProps),
            Tool("list_orders",
                "Liệt kê kiện hoàn theo bộ lọc (tối đa 50 dòng), kèm giá trị, đơn vị vận chuyển, giờ hàng về, mức cảnh báo, số giờ chờ. Danh sách là đơn bấm được.",
                listProps),
            Tool("find_order",
                "Tìm kiện theo mã đơn, mã vận đơn, mã vận đơn trả hàng hoặc mã yêu cầu hoàn (khớp một phần), ở mọi nơi kể cả lưu trữ và đã xóa.",
                new JsonObject { ["code"] = Prop("string", "Mã cần tìm (tối thiểu 3 ký tự).") }, "code"),
            Tool("order_detail",
                "Chi tiết đầy đủ của một đơn: tất cả sản phẩm (màu, size, số lượng, số lượng hoàn), giá trị, các mã, các mốc thời gian, ghi chú, theo dõi, lưu trữ/xóa, mức cảnh báo. Dùng khi hỏi sâu về MỘT đơn cụ thể.",
                new JsonObject
                {
                    ["code"] = Prop("string", "Mã đơn hoặc mã vận đơn (khớp một phần)."),
                    ["id"] = Prop("string", "Mã nội bộ của đơn (lấy từ kết quả list_orders/find_order)."),
                }),
            Tool("product_stats",
                "Thống kê sản phẩm: số lượng và số đơn theo từng sản phẩm, sắp giảm dần (tối đa 20). Cùng bộ lọc như count_orders. Dùng cho 'sản phẩm nào ... nhiều nhất'.",
                FilterProps(withProduct: false)),
            Tool("recent_activity",
                "Nhật ký hoạt động gần đây của phần mềm: nạp đơn, quét nhận, sửa, xóa, khôi phục... Mới nhất trước.",
                new JsonObject
                {
                    ["limit"] = Prop("integer", "Số dòng, mặc định 15, tối đa 50."),
                    ["type"] = Prop("string", "Loại: import, edit, scan, salework, phone_new_order, archive, delete, restore, return_watch (để trống = mọi loại)."),
                    ["text"] = Prop("string", "Lọc theo chữ trong chi tiết (ví dụ mã đơn)."),
                }),
        };
        foreach (var t in ExtraTools()) tools.Add(t);
        foreach (var t in Extra2Tools()) tools.Add(t);
        Compact(tools);
        return tools.ToJsonString(Json);
    }

    // ===================================================== thực thi

    public AiToolResult Execute(string name, string? argsJson)
    {
        JsonObject args;
        try { args = string.IsNullOrWhiteSpace(argsJson) ? new JsonObject() : (JsonNode.Parse(argsJson) as JsonObject ?? new JsonObject()); }
        catch (JsonException) { return Error("Tham số không phải JSON hợp lệ."); }

        try
        {
            return name switch
            {
                "overview" => Overview(),
                "compare_periods" => ComparePeriods(args),
                "search_orders" => Search(args),
                "turnaround_stats" => Turnaround(args),
                "data_health" => DataHealth(args),
                "duplicates" => Duplicates(args),
                "activity_stats" => ActivityStats(args),
                "notifications" => Notifications(args),
                "order_groups" => OrderGroups(args),
                "return_watch" => ReturnWatch(args),
                "system_status" => SystemStatus(),
                "app_guide" => AppGuide(args),
                "count_orders" => Count(args),
                "aggregate" => Aggregate(args),
                "list_orders" => List(args),
                "find_order" => Find(args),
                "order_detail" => Detail(args),
                "product_stats" => ProductStats(args),
                "recent_activity" => Activity(args),
                _ => Error($"Không có công cụ '{name}'."),
            };
        }
        catch (Exception ex) { return Error("Lỗi khi truy vấn: " + ex.Message); }
    }

    private static AiToolResult Error(string message) =>
        new(new JsonObject { ["error"] = message }.ToJsonString(Json), Array.Empty<AiOrderRef>());

    // ---------------------------------------------------- đọc tham số (chịu được số/chữ lẫn lộn)

    private static string? Str(JsonObject a, string k)
    {
        if (!a.TryGetPropertyValue(k, out var v) || v is null) return null;
        var s = v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.ToJsonString(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static int? Int(JsonObject a, string k) =>
        Str(a, k) is { } s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)d : null;

    private static decimal? Dec(JsonObject a, string k) =>
        Str(a, k) is { } s && decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static bool? Bool(JsonObject a, string k) => Str(a, k)?.ToLowerInvariant() switch
    {
        "true" or "1" or "có" or "yes" => true,
        "false" or "0" or "không" or "no" => false,
        _ => null,
    };

    // ---------------------------------------------------- bộ lọc

    private sealed record Filter(string? Status, string DateField, DateTime? From, DateTime? To, string? Platform, string? Carrier, string? Product,
        string Scope, string? OrderType, string? PlatformStatus, string? Alert, bool? Watching, bool? Partial, decimal? MinValue, decimal? MaxValue, string? Text);

    private Filter ReadFilter(JsonObject a)
    {
        var scope = (Str(a, "scope") ?? "active").ToLowerInvariant();
        if (scope is not ("active" or "archived" or "deleted" or "all")) scope = "active";
        var field = (Str(a, "date_field") ?? "recv").ToLowerInvariant();
        if (field is not ("recv" or "return" or "ship" or "imported" or "arrived")) field = "recv";
        var alert = Str(a, "alert")?.ToLowerInvariant();
        if (alert is not (null or "yellow" or "red" or "warning" or "safe"))
            alert = (alert is "overdue" or "đỏ") ? "red" : ((alert is "vàng") ? "yellow" : null);
        return new Filter(NormalizeStatus(Str(a, "status")), field, ParseDay(Str(a, "from")), ParseDay(Str(a, "to")),
            Str(a, "platform"), Str(a, "carrier"), Str(a, "product"), scope, Str(a, "order_type"), Str(a, "platform_status"),
            alert, Bool(a, "watching"), Bool(a, "partial_return"), Dec(a, "min_value"), Dec(a, "max_value"), Str(a, "text"));
    }

    private const string Waiting = "@waiting";
    private const string Received = "@received";

    private static string? NormalizeStatus(string? s)
    {
        if (s is null) return null;
        var l = s.ToLowerInvariant();
        if (l.Contains("đã trả") || l.Contains("đã nhận") || l.Contains("đã về") || l == "received") return Received;
        if (l.Contains("chưa") || l.Contains("chờ") || l.Contains("pending") || l.Contains("waiting")) return Waiting;
        if (l.Contains("không cần")) return ReceiveStatus.NotNeeded;
        if (l.Contains("mất")) return ReceiveStatus.Lost;
        return s;
    }

    private DateTime? ParseDay(string? s)
    {
        if (s is null) return null;
        var today = _clock().Date;
        switch (s.ToLowerInvariant())
        {
            case "today": case "hôm nay": return today;
            case "yesterday": case "hôm qua": return today.AddDays(-1);
        }
        if (DateTime.TryParseExact(s, new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d.Date;
        return null;
    }

    private static DateTime? OrderDate(Order o, string field)
    {
        if (field == "arrived") return PhoneSnapshot.ArrivedAt(o)?.Date;
        var text = field switch { "return" => o.ReturnDate, "ship" => o.ShipDate, "imported" => o.ImportedAt, _ => o.RecvDate };
        if (string.IsNullOrWhiteSpace(text)) return null;
        var p = ArchivePolicy.ParseRecvDate(text);
        if (p is not null) return p.Value.Date;
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt.Date : null;
    }

    private enum Level { Done, None, Safe, Yellow, Red }

    private Level LevelOf(Order o)
    {
        if (!PhoneSnapshot.IsPending(o)) return Level.Done;
        return AlertPolicy.MainTable.Evaluate(PhoneSnapshot.ArrivedAt(o), received: false, _clock()) switch
        {
            AlertLevel.Red => Level.Red,
            AlertLevel.Yellow => Level.Yellow,
            AlertLevel.Safe => Level.Safe,
            _ => Level.None,
        };
    }

    private static string LevelName(Level l) => l switch
    {
        Level.Red => "đỏ (quá 24 giờ)",
        Level.Yellow => "vàng (quá 12 giờ)",
        Level.Safe => "an toàn",
        Level.None => "chưa có giờ hàng về",
        _ => "đã xử lý",
    };

    private bool Matches(StoredOrder s, Filter f)
    {
        var inScope = f.Scope switch
        {
            "archived" => s.IsArchived && !s.IsDeleted,
            "deleted" => s.IsDeleted,
            "all" => true,
            _ => !s.IsArchived && !s.IsDeleted,
        };
        if (!inScope) return false;
        var o = s.Order;
        if (f.Status is not null)
        {
            var ok = f.Status switch
            {
                Waiting => PhoneSnapshot.IsPending(o),
                Received => PhoneSnapshot.IsReceived(o),
                _ => string.Equals(o.Status, f.Status, StringComparison.OrdinalIgnoreCase),
            };
            if (!ok) return false;
        }
        if (f.Platform is not null && !o.Platform.Contains(f.Platform, StringComparison.OrdinalIgnoreCase)) return false;
        if (f.Carrier is not null && !o.Carrier.Contains(f.Carrier, StringComparison.OrdinalIgnoreCase)) return false;
        if (f.OrderType is not null && !o.OrderType.Contains(f.OrderType, StringComparison.OrdinalIgnoreCase)) return false;
        if (f.PlatformStatus is not null && !o.PlatformStatus.Contains(f.PlatformStatus, StringComparison.OrdinalIgnoreCase)) return false;
        if (f.Product is not null && !o.Items.Any(i => i.Product.Contains(f.Product, StringComparison.OrdinalIgnoreCase)
                || (i.Sku ?? "").Contains(f.Product, StringComparison.OrdinalIgnoreCase))) return false;
        if (f.Watching is { } w && o.IsWatchlist != w) return false;
        if (f.Partial is { } p && o.IsPartialReturn != p) return false;
        if (f.MinValue is { } mn && o.Price < mn) return false;
        if (f.MaxValue is { } mx && o.Price > mx) return false;
        if (f.Text is not null && !(o.Note.Contains(f.Text, StringComparison.OrdinalIgnoreCase) || o.UserNote.Contains(f.Text, StringComparison.OrdinalIgnoreCase))) return false;
        if (f.Alert is not null)
        {
            var lv = LevelOf(o);
            var ok = f.Alert switch
            {
                "yellow" => lv == Level.Yellow,
                "red" => lv == Level.Red,
                "warning" => lv is Level.Yellow or Level.Red,
                "safe" => lv == Level.Safe,
                _ => true,
            };
            if (!ok) return false;
        }
        if (f.From is not null || f.To is not null)
        {
            var d = OrderDate(o, f.DateField);
            if (d is null) return false;
            if (f.From is not null && d < f.From) return false;
            if (f.To is not null && d > f.To) return false;
        }
        return true;
    }

    private List<Order> Select(Filter f) => Load().Where(s => Matches(s, f)).Select(s => s.Order).ToList();

    private static long Value(IEnumerable<Order> rows) => (long)Math.Round(rows.Sum(o => o.Price));
    private static int Quantity(Order o) => o.Items.Count == 0 ? 1 : o.Items.Sum(i => i.Qty > 0 ? i.Qty : 1);

    // ---------------------------------------------------- công cụ

    private AiToolResult Overview()
    {
        var now = _clock();
        var today = now.Date;
        var active = Load().Where(s => !s.IsArchived && !s.IsDeleted).Select(s => s.Order).ToList();
        var waiting = active.Where(PhoneSnapshot.IsPending).ToList();
        var receivedToday = active.Where(o => PhoneSnapshot.IsReceived(o) && OrderDate(o, "recv") == today).ToList();
        var receivedYesterday = active.Where(o => PhoneSnapshot.IsReceived(o) && OrderDate(o, "recv") == today.AddDays(-1)).ToList();
        var importedToday = active.Where(o => OrderDate(o, "imported") == today).ToList();
        var red = waiting.Where(o => LevelOf(o) == Level.Red).ToList();
        var yellow = waiting.Where(o => LevelOf(o) == Level.Yellow).ToList();
        var res = new JsonObject
        {
            ["now"] = now.ToString("yyyy-MM-dd HH:mm"),
            ["activeOrders"] = active.Count,
            ["waiting"] = new JsonObject { ["count"] = waiting.Count, ["value"] = Value(waiting), ["byPlatform"] = Group(waiting, o => Label(o.Platform)) },
            ["alerts"] = new JsonObject
            {
                ["redOver24h"] = new JsonObject { ["count"] = red.Count, ["value"] = Value(red) },
                ["yellowOver12h"] = new JsonObject { ["count"] = yellow.Count, ["value"] = Value(yellow) },
            },
            ["receivedToday"] = new JsonObject { ["count"] = receivedToday.Count, ["value"] = Value(receivedToday) },
            ["receivedYesterday"] = new JsonObject { ["count"] = receivedYesterday.Count, ["value"] = Value(receivedYesterday) },
            ["importedToday"] = importedToday.Count,
            ["watching"] = active.Count(o => o.IsWatchlist),
            ["archivedOrders"] = Load().Count(s => s.IsArchived && !s.IsDeleted),
        };
        return new(res.ToJsonString(Json), Array.Empty<AiOrderRef>());
    }

    /// <summary>
    /// Ảnh chụp nhanh SỐ LIỆU HIỆN TẠI (nhỏ, ~200 token) gắn sẵn vào lời nhắc hệ thống: các câu hỏi phổ biến (đang chờ bao nhiêu, báo đỏ,
    /// hôm nay về mấy đơn, sàn/vận chuyển/sản phẩm nào nhiều) trả lời được NGAY, không cần vòng gọi công cụ (tiết kiệm token và giới hạn phút).
    /// Chỉ chứa số tổng hợp và tên sản phẩm; không có tên/SĐT/địa chỉ. Lỗi đọc dữ liệu thì trả chuỗi rỗng.
    /// </summary>
    public string Snapshot()
    {
        try
        {
            var now = _clock();
            var today = now.Date;
            var active = Load().Where(s => !s.IsArchived && !s.IsDeleted).Select(s => s.Order).ToList();
            var waiting = active.Where(PhoneSnapshot.IsPending).ToList();
            var received = active.Where(PhoneSnapshot.IsReceived).ToList();
            var recvToday = received.Where(o => OrderDate(o, "recv") == today).ToList();
            var recvYesterday = received.Where(o => OrderDate(o, "recv") == today.AddDays(-1)).ToList();
            var recv7 = received.Where(o => OrderDate(o, "recv") is { } d && d > today.AddDays(-7) && d <= today).ToList();
            var red = waiting.Where(o => LevelOf(o) == Level.Red).ToList();
            var yellow = waiting.Where(o => LevelOf(o) == Level.Yellow).ToList();
            var topProducts = new JsonArray();
            foreach (var g in waiting.SelectMany(o => o.Items.Select(i => (Name: i.Product, Qty: i.Qty > 0 ? i.Qty : 1)))
                         .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                         .GroupBy(x => x.Name.Trim()).Select(g => (Name: g.Key, Qty: g.Sum(x => x.Qty)))
                         .OrderByDescending(x => x.Qty).Take(5))
                topProducts.Add(new JsonObject { ["name"] = MaskPhones(g.Name.Length > 40 ? g.Name[..40] : g.Name), ["qty"] = g.Qty });
            var res = new JsonObject
            {
                ["activeOrders"] = active.Count,
                ["waiting"] = new JsonObject { ["count"] = waiting.Count, ["value"] = Value(waiting) },
                ["waitingByPlatform"] = Group(waiting, o => Label(o.Platform)),
                ["waitingByCarrier"] = Group(waiting, o => Label(o.Carrier)),
                ["red24h"] = new JsonObject { ["count"] = red.Count, ["value"] = Value(red) },
                ["yellow12h"] = new JsonObject { ["count"] = yellow.Count, ["value"] = Value(yellow) },
                ["receivedToday"] = new JsonObject { ["count"] = recvToday.Count, ["value"] = Value(recvToday) },
                ["receivedYesterday"] = new JsonObject { ["count"] = recvYesterday.Count, ["value"] = Value(recvYesterday) },
                ["received7days"] = new JsonObject { ["count"] = recv7.Count, ["value"] = Value(recv7) },
                ["watching"] = active.Count(o => o.IsWatchlist),
            };
            if (topProducts.Count > 0) res["topWaitingProducts"] = topProducts;
            return res.ToJsonString(Json);
        }
        catch { return ""; }
    }

    private static string Label(string? s) => string.IsNullOrWhiteSpace(s) ? "(trống)" : s;

    private static JsonObject Group(IEnumerable<Order> rows, Func<Order, string> key)
    {
        var obj = new JsonObject();
        foreach (var g in rows.GroupBy(key).OrderByDescending(g => g.Count())) obj[g.Key] = g.Count();
        return obj;
    }

    private AiToolResult Count(JsonObject a)
    {
        var rows = Select(ReadFilter(a));
        var res = new JsonObject
        {
            ["total"] = rows.Count,
            ["totalValue"] = Value(rows),
            ["totalQuantity"] = rows.Sum(Quantity),
            ["byStatus"] = Group(rows, o => Label(o.Status)),
            ["byPlatform"] = Group(rows, o => Label(o.Platform)),
        };
        return new(res.ToJsonString(Json), Array.Empty<AiOrderRef>());
    }

    private AiToolResult Aggregate(JsonObject a)
    {
        var f = ReadFilter(a);
        var rows = Select(f);
        var groupBy = (Str(a, "group_by") ?? "none").ToLowerInvariant();
        var metric = (Str(a, "metric") ?? "count").ToLowerInvariant();
        var top = Math.Clamp(Int(a, "top") ?? 20, 1, 60);

        IEnumerable<IGrouping<string, Order>> groups = groupBy switch
        {
            "status" => rows.GroupBy(o => Label(o.Status)),
            "platform" => rows.GroupBy(o => Label(o.Platform)),
            "carrier" => rows.GroupBy(o => Label(o.Carrier)),
            "order_type" => rows.GroupBy(o => Label(o.OrderType)),
            "platform_status" => rows.GroupBy(o => Label(o.PlatformStatus)),
            "alert" => rows.GroupBy(o => LevelName(LevelOf(o))),
            var g when ItemKey(g) is not null => rows.SelectMany(o => ItemsOrPlaceholder(o).Select(i => (Key: ItemKey(g)!(i), Order: o))).GroupBy(x => x.Key, x => x.Order),
            var g when ExtraGroupKeys.Contains(g) => rows.GroupBy(o => ExtraGroupKey(g, o)),
            "day" => rows.GroupBy(o => DayKey(o, f.DateField, "yyyy-MM-dd")),
            "week" => rows.GroupBy(o => WeekKey(o, f.DateField)),
            "month" => rows.GroupBy(o => DayKey(o, f.DateField, "yyyy-MM")),
            _ => rows.GroupBy(_ => "tất cả"),
        };

        var list = groups.Select(g =>
        {
            var os = g.Distinct().ToList();
            var itemKey = ItemKey(groupBy);
            var qty = itemKey is not null
                ? os.Sum(o => ItemsOrPlaceholder(o).Where(i => itemKey(i) == g.Key).Sum(i => i.Qty > 0 ? i.Qty : 1))
                : os.Sum(Quantity);
            return (Key: g.Key, Count: os.Count, Value: Value(os), Qty: qty);
        }).ToList();

        var chronological = groupBy is "day" or "week" or "month";
        var ordered = chronological
            ? list.OrderBy(x => x.Key, StringComparer.Ordinal).ToList()
            : (metric switch
            {
                "value" => list.OrderByDescending(x => x.Value),
                "quantity" => list.OrderByDescending(x => x.Qty),
                _ => list.OrderByDescending(x => x.Count),
            }).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
        var shown = chronological ? ordered.TakeLast(top).ToList() : ordered.Take(top).ToList();

        var arr = new JsonArray();
        foreach (var x in shown) arr.Add(new JsonObject { ["group"] = MaskPhones(x.Key), ["count"] = x.Count, ["value"] = x.Value, ["quantity"] = x.Qty });
        var res = new JsonObject
        {
            ["groupBy"] = groupBy,
            ["ordersCounted"] = rows.Count,
            ["totalValue"] = Value(rows),
            ["totalQuantity"] = rows.Sum(Quantity),
            ["groupsTotal"] = list.Count,
            ["truncated"] = list.Count > shown.Count,
            ["groups"] = arr,
        };
        return new(res.ToJsonString(Json), Array.Empty<AiOrderRef>());
    }

    private static string DayKey(Order o, string field, string format) =>
        OrderDate(o, field)?.ToString(format, CultureInfo.InvariantCulture) ?? "(không có ngày)";

    private static string WeekKey(Order o, string field)
    {
        if (OrderDate(o, field) is not { } d) return "(không có ngày)";
        var monday = d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
        return monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " (tuần)";
    }

    private AiToolResult List(JsonObject a)
    {
        var f = ReadFilter(a);
        var rows = Select(f);
        var limit = Math.Clamp(Int(a, "limit") ?? 12, 1, 30);

        var sortBy = Str(a, "sort_by")?.ToLowerInvariant();
        var desc = Bool(a, "descending");
        if (sortBy is null) { sortBy = f.Status == Waiting ? "arrived" : (f.DateField == "return" ? "return_date" : "recv_date"); desc ??= f.Status != Waiting; }
        desc ??= sortBy != "arrived";
        DateTime Key(Order o) => (sortBy switch
        {
            "arrived" => PhoneSnapshot.ArrivedAt(o),
            "return_date" => OrderDate(o, "return"),
            _ => OrderDate(o, "recv"),
        }) ?? (desc == true ? DateTime.MinValue : DateTime.MaxValue);
        rows = sortBy == "value"
            ? (desc == true ? rows.OrderByDescending(o => o.Price) : rows.OrderBy(o => o.Price)).ToList()
            : (desc == true ? rows.OrderByDescending(Key) : rows.OrderBy(Key)).ToList();
        return Rows(rows, limit);
    }

    private AiToolResult Find(JsonObject a)
    {
        var code = Str(a, "code") ?? "";
        if (code.Length < 3) return Error("Mã cần tìm quá ngắn (tối thiểu 3 ký tự).");
        return Rows(FindByCode(code).Select(s => s.Order).ToList(), 10);
    }

    private IEnumerable<StoredOrder> FindByCode(string code) => Load().Where(s =>
        Has(s.Order.OrderId, code) || Has(s.Order.Track, code) || Has(s.Order.ReturnTrack, code) || Has(s.Order.ReturnOrderId, code)
        || Has(s.Order.ForwardTrack, code) || Has(s.Order.AltTrack, code));

    private static bool Has(string? hay, string needle) => !string.IsNullOrEmpty(hay) && hay.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>Dòng đơn GỌN cho danh sách: bỏ trường rỗng/false, tối đa 3 sản phẩm, ghi chú cắt 80 ký tự (tiết kiệm token).</summary>
    private JsonObject RowJsonCompact(StoredOrder s)
    {
        var o = s.Order;
        var lv = LevelOf(o);
        var j = new JsonObject
        {
            ["id"] = o.Id, ["orderId"] = o.OrderId, ["track"] = o.Track, ["status"] = o.Status, ["value"] = (long)Math.Round(o.Price),
        };
        void Put(string k, string? v) { if (!string.IsNullOrWhiteSpace(v)) j[k] = v; }
        Put("platform", o.Platform); Put("carrier", o.Carrier); Put("returnTrack", o.ReturnTrack); Put("orderType", o.OrderType);
        Put("returnDate", o.ReturnDate); Put("arrivedAt", o.ReturnedAt); Put("recvDate", o.RecvDate);
        if (lv != Level.Done) j["alert"] = LevelName(lv);
        if (PhoneSnapshot.ArrivedAt(o) is { } ar && lv is Level.Safe or Level.Yellow or Level.Red) j["hoursWaiting"] = Math.Round((_clock() - ar).TotalHours, 1);
        var note = MaskPhones(string.Join(" / ", new[] { o.Note, o.UserNote }.Where(x => !string.IsNullOrWhiteSpace(x))));
        Put("note", note.Length > 80 ? note[..80] : note);
        if (o.IsWatchlist) j["watching"] = true;
        if (o.IsPartialReturn) j["partialReturn"] = true;
        var items = new JsonArray();
        foreach (var i in o.Items.Take(3))
        {
            var it = new JsonObject { ["product"] = MaskPhones(i.Product.Length > 50 ? i.Product[..50] : i.Product), ["qty"] = i.Qty };
            if (!string.IsNullOrWhiteSpace(i.Color)) it["color"] = i.Color;
            if (!string.IsNullOrWhiteSpace(i.Size)) it["size"] = i.Size;
            items.Add(it);
        }
        j["items"] = items;
        if (o.Items.Count > 3) j["moreItems"] = o.Items.Count - 3;
        return j;
    }

    private JsonObject RowJson(StoredOrder s, bool detailed)
    {
        if (!detailed) return RowJsonCompact(s);
        var o = s.Order;
        var lv = LevelOf(o);
        var arrived = PhoneSnapshot.ArrivedAt(o);
        var items = new JsonArray();
        foreach (var i in o.Items.Take(detailed ? 30 : 10))
        {
            var it = new JsonObject { ["product"] = MaskPhones(i.Product), ["color"] = i.Color, ["size"] = i.Size, ["qty"] = i.Qty };
            if (detailed)
            {
                if (i.ReturnedQty > 0) it["returnedQty"] = i.ReturnedQty;
                if (!string.IsNullOrEmpty(i.Sku)) it["sku"] = i.Sku;
            }
            items.Add(it);
        }
        var j = new JsonObject
        {
            ["id"] = o.Id, ["orderId"] = o.OrderId, ["track"] = o.Track, ["returnTrack"] = o.ReturnTrack,
            ["platform"] = o.Platform, ["carrier"] = o.Carrier, ["status"] = o.Status, ["orderType"] = o.OrderType, ["platformStatus"] = o.PlatformStatus,
            ["value"] = (long)Math.Round(o.Price),
            ["shipDate"] = o.ShipDate, ["returnDate"] = o.ReturnDate, ["arrivedAt"] = o.ReturnedAt, ["recvDate"] = o.RecvDate, ["deadline"] = o.Deadline,
            ["alert"] = LevelName(lv),
            ["note"] = MaskPhones(o.Note), ["userNote"] = MaskPhones(o.UserNote), ["watching"] = o.IsWatchlist, ["partialReturn"] = o.IsPartialReturn,
            ["items"] = items,
        };
        if (arrived is { } ar && lv is Level.Safe or Level.Yellow or Level.Red) j["hoursWaiting"] = Math.Round((_clock() - ar).TotalHours, 1);
        if (detailed)
        {
            j["returnOrderId"] = o.ReturnOrderId;
            j["forwardTrack"] = o.ForwardTrack;
            j["importedAt"] = o.ImportedAt;
            j["watchReason"] = MaskPhones(o.WatchlistReason);
            j["archived"] = s.IsArchived;
            if (s.IsArchived) j["archivedAt"] = s.ArchivedAt;
            j["deleted"] = s.IsDeleted;
            if (s.IsDeleted) { j["deletedAt"] = s.DeletedAt; j["deletedFrom"] = s.DeletedFrom; }
            j["otherCodes"] = new JsonArray(o.TrackingCodes.Concat(o.ScanAliases).Distinct().Take(20).Select(c => (JsonNode)JsonValue.Create(c)!).ToArray());
        }
        return j;
    }

    private AiToolResult Rows(List<Order> rows, int limit)
    {
        // dữ liệu cũ có thể có 2 dòng cùng id (ảnh 28): lấy dòng đầu, không được lỗi "trùng khóa"
        var byId = new Dictionary<string, StoredOrder>(StringComparer.Ordinal);
        foreach (var s in Load()) byId.TryAdd(s.Order.Id, s);
        var arr = new JsonArray();
        var refs = new List<AiOrderRef>();
        foreach (var o in rows.Take(limit))
        {
            _seen[o.Id] = o;
            refs.Add(new AiOrderRef(o.Id, o.OrderId, string.IsNullOrEmpty(o.Track) ? o.ReturnTrack : o.Track, o.Status));
            arr.Add(RowJson(byId.TryGetValue(o.Id, out var s) ? s : new StoredOrder(o, false, "", false, "", ""), detailed: false));
        }
        var res = new JsonObject { ["total"] = rows.Count, ["totalValue"] = Value(rows), ["shown"] = arr.Count, ["truncated"] = rows.Count > arr.Count, ["orders"] = arr };
        return new(res.ToJsonString(Json), refs);
    }

    private AiToolResult Detail(JsonObject a)
    {
        var id = Str(a, "id");
        var code = Str(a, "code");
        if (id is null && (code is null || code.Length < 3)) return Error("Cần mã nội bộ (id) hoặc mã đơn/mã vận đơn (tối thiểu 3 ký tự).");
        var found = id is not null
            ? Load().Where(s => s.Order.Id == id).ToList()
            : FindByCode(code!).Take(3).ToList();
        if (found.Count == 0) return Error("Không tìm thấy đơn nào khớp.");
        var arr = new JsonArray();
        var refs = new List<AiOrderRef>();
        foreach (var s in found)
        {
            var o = s.Order;
            _seen[o.Id] = o;
            refs.Add(new AiOrderRef(o.Id, o.OrderId, string.IsNullOrEmpty(o.Track) ? o.ReturnTrack : o.Track, o.Status));
            arr.Add(RowJson(s, detailed: true));
        }
        return new(new JsonObject { ["matches"] = arr.Count, ["orders"] = arr }.ToJsonString(Json), refs);
    }

    private AiToolResult ProductStats(JsonObject a)
    {
        var rows = Select(ReadFilter(a));
        var stats = rows.SelectMany(o => o.Items.Select(i => (o, i)))
            .GroupBy(x => x.i.Product.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.Key, Qty: g.Sum(x => x.i.Qty), Orders: g.Select(x => x.o.Id).Distinct().Count()))
            .OrderByDescending(x => x.Qty).Take(20);
        var arr = new JsonArray();
        foreach (var s in stats) arr.Add(new JsonObject { ["product"] = MaskPhones(s.Name), ["qty"] = s.Qty, ["orders"] = s.Orders });
        return new(new JsonObject { ["ordersCounted"] = rows.Count, ["products"] = arr }.ToJsonString(Json), Array.Empty<AiOrderRef>());
    }

    private AiToolResult Activity(JsonObject a)
    {
        if (_activity is null) return Error("Chưa đọc được nhật ký hoạt động.");
        var limit = Math.Clamp(Int(a, "limit") ?? 15, 1, 50);
        var entries = _activity(limit, Str(a, "type") ?? "", Str(a, "text") ?? "");
        var arr = new JsonArray();
        foreach (var e in entries.Take(limit))
            arr.Add(new JsonObject { ["time"] = e.Time, ["type"] = e.Label, ["detail"] = MaskPhones(e.Detail.Length > 300 ? e.Detail[..300] : e.Detail) });
        return new(new JsonObject { ["count"] = arr.Count, ["entries"] = arr }.ToJsonString(Json), Array.Empty<AiOrderRef>());
    }
}
