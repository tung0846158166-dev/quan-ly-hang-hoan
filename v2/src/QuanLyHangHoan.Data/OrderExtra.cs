using System.Text.Json;
using System.Text.Json.Nodes;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Data;

/// <summary>
/// Thông tin giá của lần nhập (priceModes, importModeSlots, importSelectedPriceMode, priceHasValue, priceNeedsReview) lưu trong
/// cột extra_json với ĐÚNG tên khóa của app cũ, nên đơn đã nhập từ dữ liệu cũ đọc được ngay. Mọi khóa khác trong extra_json được giữ nguyên.
/// </summary>
public static class OrderExtra
{
    private static readonly HashSet<string> NumericRaw = new() { "refundAmount", "compensation", "totalValue", "revenue", "forwardShippingFee", "returnShippingFee" };

    public static ImportMeta Read(string? extraJson)
    {
        var meta = new ImportMeta();
        if (string.IsNullOrWhiteSpace(extraJson)) return meta;
        try
        {
            using var doc = JsonDocument.Parse(extraJson);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return meta;

            if (r.TryGetProperty("priceModes", out var pm) && pm.ValueKind == JsonValueKind.Object)
                foreach (var p in pm.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out var n)) meta.PriceModes[p.Name] = (long)Math.Round(n);
                    else if (p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) meta.PriceModeFlags[p.Name] = p.Value.GetBoolean();
                }
            if (r.TryGetProperty("importModeSlots", out var sl) && sl.ValueKind == JsonValueKind.Object)
                foreach (var p in sl.EnumerateObject())
                    meta.ImportModeSlots[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
            if (r.TryGetProperty("importSelectedPriceMode", out var sm) && sm.ValueKind == JsonValueKind.String) meta.ImportSelectedPriceMode = sm.GetString();
            if (r.TryGetProperty("priceHasValue", out var hv) && hv.ValueKind is JsonValueKind.True or JsonValueKind.False) meta.PriceHasValue = hv.GetBoolean();
            if (r.TryGetProperty("priceNeedsReview", out var nr) && nr.ValueKind is JsonValueKind.True or JsonValueKind.False) meta.PriceNeedsReview = nr.GetBoolean();
            if (r.TryGetProperty("importPriceBasis", out var pb) && pb.ValueKind == JsonValueKind.String) meta.ImportPriceBasis = pb.GetString();
            if (r.TryGetProperty("forwardCarrier", out var fc) && fc.ValueKind == JsonValueKind.String) meta.ForwardCarrier = fc.GetString() ?? "";
            if (r.TryGetProperty("returnCarrier", out var rc) && rc.ValueKind == JsonValueKind.String) meta.ReturnCarrier = rc.GetString() ?? "";
            if (r.TryGetProperty("rawDetails", out var rd) && rd.ValueKind == JsonValueKind.Object)
                foreach (var p in rd.EnumerateObject())
                    meta.RawDetails[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ValueKind == JsonValueKind.Number ? p.Value.GetRawText() : "";
        }
        catch (JsonException) { /* extra_json hỏng: coi như chưa có thông tin giá, không làm hỏng việc đọc đơn */ }
        return meta;
    }

    /// <summary>Ghi meta vào extra_json, GIỮ NGUYÊN các khóa khác (rawDetails, deadline... của dữ liệu cũ).</summary>
    public static string Write(string? existingExtraJson, ImportMeta meta)
    {
        JsonObject obj;
        try { obj = string.IsNullOrWhiteSpace(existingExtraJson) ? new JsonObject() : JsonNode.Parse(existingExtraJson) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { obj = new JsonObject(); }

        if (meta.PriceModes.Count > 0 || meta.PriceModeFlags.Count > 0)
        {
            var pm = new JsonObject();
            foreach (var kv in meta.PriceModes) pm[kv.Key] = kv.Value;
            foreach (var kv in meta.PriceModeFlags) pm[kv.Key] = kv.Value;
            obj["priceModes"] = pm;
        }
        if (meta.ImportModeSlots.Count > 0)
        {
            var sl = new JsonObject();
            foreach (var kv in meta.ImportModeSlots) sl[kv.Key] = kv.Value is null ? null : JsonValue.Create(kv.Value);
            obj["importModeSlots"] = sl;
        }
        if (meta.ImportSelectedPriceMode is not null) obj["importSelectedPriceMode"] = meta.ImportSelectedPriceMode;
        if (meta.PriceHasValue is not null) obj["priceHasValue"] = meta.PriceHasValue;
        if (meta.PriceNeedsReview is not null) obj["priceNeedsReview"] = meta.PriceNeedsReview;
        if (meta.ImportPriceBasis is not null) obj["importPriceBasis"] = meta.ImportPriceBasis;
        if (meta.ForwardCarrier.Length > 0) obj["forwardCarrier"] = meta.ForwardCarrier;
        if (meta.ReturnCarrier.Length > 0) obj["returnCarrier"] = meta.ReturnCarrier;
        if (meta.RawDetails.Count > 0)
        {
            // Số tiền giữ dạng số như app cũ; mọi khóa cũ không có trong lần nhập này được giữ nguyên.
            var rd = obj["rawDetails"] as JsonObject ?? new JsonObject();
            foreach (var kv in meta.RawDetails)
                rd[kv.Key] = NumericRaw.Contains(kv.Key) && long.TryParse(kv.Value, out var n) ? JsonValue.Create(n) : JsonValue.Create(kv.Value);
            obj["rawDetails"] = rd;
        }
        return obj.Count == 0 ? "" : obj.ToJsonString();
    }
}
