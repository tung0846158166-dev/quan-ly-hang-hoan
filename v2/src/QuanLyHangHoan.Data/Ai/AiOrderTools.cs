using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Data.Ai;

/// <summary>Đơn liên quan trong câu trả lời, để giao diện cho bấm mở chi tiết.</summary>
public sealed record AiOrderRef(string Id, string OrderId, string Track, string Status);

public sealed record AiToolResult(string Json, IReadOnlyList<AiOrderRef> Refs);

/// <summary>
/// Công cụ CHỈ ĐỌC cho trợ lý AI. Số liệu do mã tính, AI chỉ diễn đạt lại.
/// Quyền riêng tư: chỉ trả về các trường trong danh sách trắng bên dưới. KHÔNG bao giờ trả tên/số điện thoại/địa chỉ người nhận
/// (không có trong mô hình đơn; nằm trong RawDetails/ExtraJson/BuyerNote nên cố ý không lấy), và ghi chú còn bị che mọi dãy giống số điện thoại.
/// </summary>
public sealed class AiOrderTools
{
    internal static readonly System.Text.Json.JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly Func<IReadOnlyList<StoredOrder>> _loader;
    private readonly Func<DateTime> _clock;
    private readonly Dictionary<string, Order> _seen = new(StringComparer.Ordinal);

    public AiOrderTools(Func<IReadOnlyList<StoredOrder>> loader, Func<DateTime>? clock = null)
    {
        _loader = loader;
        _clock = clock ?? (() => DateTime.Now);
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

    public const string DefinitionsJson = """
    [
      {"type":"function","function":{"name":"count_orders","description":"Đếm số kiện hoàn theo bộ lọc. Không có bộ lọc = tổng quan toàn bộ. Trả về tổng và phân loại theo trạng thái, sàn. 'Đơn về hôm nay' = status 'Hàng đã trả' với date_field 'recv' và from=to=hôm nay.","parameters":{"type":"object","properties":{
        "status":{"type":"string","enum":["Hàng đã trả","Chưa trả","Chưa về","Không cần nhận","Bị mất"],"description":"Trạng thái nhận hàng. 'Hàng đã trả' nghĩa là hàng đã về và đã nhận tại kho."},
        "date_field":{"type":"string","enum":["recv","return","ship","imported"],"description":"Ngày dùng để lọc: recv = ngày nhận tại kho (mặc định), return = ngày khách hoàn, ship = ngày giao đi, imported = ngày nạp vào phần mềm."},
        "from":{"type":"string","description":"Từ ngày (gồm), dạng yyyy-MM-dd hoặc today/yesterday."},
        "to":{"type":"string","description":"Đến ngày (gồm), dạng yyyy-MM-dd hoặc today/yesterday."},
        "platform":{"type":"string","description":"Sàn: Shopee, TikTok Shop, Lazada (khớp một phần)."},
        "carrier":{"type":"string","description":"Đơn vị vận chuyển (khớp một phần)."},
        "product":{"type":"string","description":"Tên sản phẩm hoặc SKU (khớp một phần)."},
        "scope":{"type":"string","enum":["active","archived","deleted","all"],"description":"active = đang xử lý (mặc định), archived = kho lưu trữ, deleted = đã xóa, all = tất cả."}
      }}}},
      {"type":"function","function":{"name":"list_orders","description":"Liệt kê các kiện hoàn theo bộ lọc (tối đa 50 dòng). Dùng khi người dùng muốn xem danh sách đơn.","parameters":{"type":"object","properties":{
        "status":{"type":"string","enum":["Hàng đã trả","Chưa trả","Chưa về","Không cần nhận","Bị mất"]},
        "date_field":{"type":"string","enum":["recv","return","ship","imported"]},
        "from":{"type":"string"},"to":{"type":"string"},"platform":{"type":"string"},"carrier":{"type":"string"},"product":{"type":"string"},
        "scope":{"type":"string","enum":["active","archived","deleted","all"]},
        "limit":{"type":"integer","description":"Số dòng tối đa, mặc định 20, tối đa 50."}
      }}}},
      {"type":"function","function":{"name":"find_order","description":"Tìm kiện theo mã đơn, mã vận đơn, mã vận đơn trả hàng hoặc mã yêu cầu hoàn (khớp một phần), ở mọi nơi kể cả lưu trữ và đã xóa.","parameters":{"type":"object","properties":{"code":{"type":"string","description":"Mã cần tìm."}},"required":["code"]}}},
      {"type":"function","function":{"name":"product_stats","description":"Thống kê sản phẩm: số lượng và số đơn theo từng sản phẩm, sắp giảm dần (tối đa 20). Cùng bộ lọc như count_orders.","parameters":{"type":"object","properties":{
        "status":{"type":"string","enum":["Hàng đã trả","Chưa trả","Chưa về","Không cần nhận","Bị mất"]},
        "date_field":{"type":"string","enum":["recv","return","ship","imported"]},
        "from":{"type":"string"},"to":{"type":"string"},"platform":{"type":"string"},"carrier":{"type":"string"},
        "scope":{"type":"string","enum":["active","archived","deleted","all"]}
      }}}}
    ]
    """;

    public AiToolResult Execute(string name, string? argsJson)
    {
        JsonObject args;
        try { args = string.IsNullOrWhiteSpace(argsJson) ? new JsonObject() : (JsonNode.Parse(argsJson) as JsonObject ?? new JsonObject()); }
        catch (JsonException) { return Error("Tham số không phải JSON hợp lệ."); }

        try
        {
            return name switch
            {
                "count_orders" => Count(args),
                "list_orders" => List(args),
                "find_order" => Find(args),
                "product_stats" => ProductStats(args),
                _ => Error($"Không có công cụ '{name}'."),
            };
        }
        catch (Exception ex) { return Error("Lỗi khi truy vấn: " + ex.Message); }
    }

    private static AiToolResult Error(string message) =>
        new(new JsonObject { ["error"] = message }.ToJsonString(Json), Array.Empty<AiOrderRef>());

    private sealed record Filter(string? Status, string DateField, DateTime? From, DateTime? To, string? Platform, string? Carrier, string? Product, string Scope);

    private Filter ReadFilter(JsonObject a)
    {
        string? S(string k) => a.TryGetPropertyValue(k, out var v) && v is not null && v.GetValueKind() == JsonValueKind.String
            ? (string.IsNullOrWhiteSpace(v.GetValue<string>()) ? null : v.GetValue<string>().Trim()) : null;
        var scope = (S("scope") ?? "active").ToLowerInvariant();
        if (scope is not ("active" or "archived" or "deleted" or "all")) scope = "active";
        var field = (S("date_field") ?? "recv").ToLowerInvariant();
        if (field is not ("recv" or "return" or "ship" or "imported")) field = "recv";
        return new Filter(NormalizeStatus(S("status")), field, ParseDay(S("from")), ParseDay(S("to")), S("platform"), S("carrier"), S("product"), scope);
    }

    private static string? NormalizeStatus(string? s)
    {
        if (s is null) return null;
        foreach (var known in new[] { ReceiveStatus.Received, ReceiveStatus.NotReturned, ReceiveStatus.NotYetArrived, ReceiveStatus.NotNeeded, ReceiveStatus.Lost })
            if (known.Equals(s, StringComparison.OrdinalIgnoreCase)) return known;
        var l = s.ToLowerInvariant();
        if (l.Contains("đã trả") || l.Contains("đã nhận") || l.Contains("đã về") || l == "received") return ReceiveStatus.Received;
        if (l.Contains("chưa trả")) return ReceiveStatus.NotReturned;
        if (l.Contains("chưa về") || l.Contains("chưa nhận")) return ReceiveStatus.NotYetArrived;
        if (l.Contains("mất")) return ReceiveStatus.Lost;
        if (l.Contains("không cần")) return ReceiveStatus.NotNeeded;
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
        var text = field switch { "return" => o.ReturnDate, "ship" => o.ShipDate, "imported" => o.ImportedAt, _ => o.RecvDate };
        if (string.IsNullOrWhiteSpace(text)) return null;
        var p = ArchivePolicy.ParseRecvDate(text);
        if (p is not null) return p.Value.Date;
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt.Date : null;
    }

    private List<Order> Select(Filter f)
    {
        var all = Load();
        var q = new List<Order>();
        foreach (var s in all)
        {
            var inScope = f.Scope switch
            {
                "archived" => s.IsArchived && !s.IsDeleted,
                "deleted" => s.IsDeleted,
                "all" => true,
                _ => !s.IsArchived && !s.IsDeleted,
            };
            if (!inScope) continue;
            var o = s.Order;
            if (f.Status is not null && !string.Equals(o.Status, f.Status, StringComparison.Ordinal)) continue;
            if (f.Platform is not null && !o.Platform.Contains(f.Platform, StringComparison.OrdinalIgnoreCase)) continue;
            if (f.Carrier is not null && !o.Carrier.Contains(f.Carrier, StringComparison.OrdinalIgnoreCase)) continue;
            if (f.Product is not null && !o.Items.Any(i => i.Product.Contains(f.Product, StringComparison.OrdinalIgnoreCase)
                    || (i.Sku ?? "").Contains(f.Product, StringComparison.OrdinalIgnoreCase))) continue;
            if (f.From is not null || f.To is not null)
            {
                var d = OrderDate(o, f.DateField);
                if (d is null) continue;
                if (f.From is not null && d < f.From) continue;
                if (f.To is not null && d > f.To) continue;
            }
            q.Add(o);
        }
        return q;
    }

    private AiToolResult Count(JsonObject a)
    {
        var rows = Select(ReadFilter(a));
        var res = new JsonObject
        {
            ["total"] = rows.Count,
            ["byStatus"] = Group(rows, o => string.IsNullOrWhiteSpace(o.Status) ? "(trống)" : o.Status),
            ["byPlatform"] = Group(rows, o => string.IsNullOrWhiteSpace(o.Platform) ? "(trống)" : o.Platform),
        };
        return new(res.ToJsonString(Json), Array.Empty<AiOrderRef>());
    }

    private static JsonObject Group(IEnumerable<Order> rows, Func<Order, string> key)
    {
        var obj = new JsonObject();
        foreach (var g in rows.GroupBy(key).OrderByDescending(g => g.Count())) obj[g.Key] = g.Count();
        return obj;
    }

    private AiToolResult List(JsonObject a)
    {
        var rows = Select(ReadFilter(a));
        var limit = 20;
        if (a.TryGetPropertyValue("limit", out var lv) && lv is not null && lv.GetValueKind() == JsonValueKind.Number) limit = lv.GetValue<int>();
        limit = Math.Clamp(limit, 1, 50);
        return Rows(rows, limit);
    }

    private AiToolResult Find(JsonObject a)
    {
        var code = a.TryGetPropertyValue("code", out var c) && c is not null && c.GetValueKind() == JsonValueKind.String ? c.GetValue<string>().Trim() : "";
        if (code.Length < 3) return Error("Mã cần tìm quá ngắn (tối thiểu 3 ký tự).");
        var rows = Load().Where(s =>
            Has(s.Order.OrderId, code) || Has(s.Order.Track, code) || Has(s.Order.ReturnTrack, code) || Has(s.Order.ReturnOrderId, code)
            || Has(s.Order.ForwardTrack, code) || Has(s.Order.AltTrack, code)).Select(s => s.Order).ToList();
        return Rows(rows, 10);
    }

    private static bool Has(string? hay, string needle) => !string.IsNullOrEmpty(hay) && hay.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private AiToolResult Rows(List<Order> rows, int limit)
    {
        var arr = new JsonArray();
        var refs = new List<AiOrderRef>();
        foreach (var o in rows.Take(limit))
        {
            _seen[o.Id] = o;
            refs.Add(new AiOrderRef(o.Id, o.OrderId, string.IsNullOrEmpty(o.Track) ? o.ReturnTrack : o.Track, o.Status));
            var items = new JsonArray();
            foreach (var i in o.Items.Take(10))
                items.Add(new JsonObject { ["product"] = MaskPhones(i.Product), ["color"] = i.Color, ["size"] = i.Size, ["qty"] = i.Qty });
            arr.Add(new JsonObject
            {
                ["id"] = o.Id, ["orderId"] = o.OrderId, ["track"] = o.Track, ["returnTrack"] = o.ReturnTrack,
                ["platform"] = o.Platform, ["status"] = o.Status, ["carrier"] = o.Carrier,
                ["recvDate"] = o.RecvDate, ["returnDate"] = o.ReturnDate, ["shipDate"] = o.ShipDate, ["deadline"] = o.Deadline,
                ["note"] = MaskPhones(o.Note), ["userNote"] = MaskPhones(o.UserNote), ["watching"] = o.IsWatchlist, ["items"] = items,
            });
        }
        var res = new JsonObject { ["total"] = rows.Count, ["shown"] = arr.Count, ["truncated"] = rows.Count > arr.Count, ["orders"] = arr };
        return new(res.ToJsonString(Json), refs);
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
}
