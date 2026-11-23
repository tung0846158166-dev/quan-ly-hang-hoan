using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Data.Legacy;

/// <summary>
/// Ánh xạ một dòng JSON cũ -> Order. Không suy đoán: trường nào không có cột riêng được giữ nguyên
/// trong ExtraJson; JSON gốc luôn được giữ ở LegacyRecord.RawJson.
/// </summary>
public static class LegacyMapper
{
    // Tên trường đã có cột riêng (cả tên đơn chính lẫn tên riêng của đơn hủy)
    private static readonly HashSet<string> Mapped = new(StringComparer.Ordinal)
    {
        "shipDate","returnDate","recvDate","platform","orderType","platformStatus","preOrder",
        "orderId","returnOrderId","track","returnTrack","altTrack","forwardTrack","trackingCodes","scanAliases","carrier",
        "price","status","note","buyerNote","userNote","importWeek","importedAt","isWatchlist","watchlistReason",
        "isPartialReturn","photoId","items","returnedAt","deadline",
    };

    public static Order Map(JsonElement row, LegacyCategory category)
    {
        if (row.ValueKind != JsonValueKind.Object)
            throw new FormatException("Dòng không phải đối tượng JSON.");

        var o = new Order
        {
            OrderId = Str(row, "orderId"),
            ReturnOrderId = Str(row, "returnOrderId"),
            Track = Str(row, "track"),
            ReturnTrack = Str(row, "returnTrack"),
            AltTrack = Str(row, "altTrack"),
            ForwardTrack = Str(row, "forwardTrack"),
            TrackingCodes = StrList(row, "trackingCodes"),
            ScanAliases = StrList(row, "scanAliases"),
            Platform = NormalizePlatform(Str(row, "platform")),
            OrderType = Str(row, "orderType"),
            PlatformStatus = Str(row, "platformStatus"),
            PreOrder = Str(row, "preOrder"),
            Status = Str(row, "status"),
            Carrier = Str(row, "carrier"),
            ShipDate = Str(row, "shipDate"),
            ReturnDate = Str(row, "returnDate"),
            RecvDate = Str(row, "recvDate"),
            ReturnedAt = Str(row, "returnedAt"),
            Deadline = Str(row, "deadline"),
            Price = Dec(row, "price"),
            Note = Str(row, "note"),
            BuyerNote = Str(row, "buyerNote"),
            UserNote = Str(row, "userNote"),
            ImportWeek = Str(row, "importWeek"),
            ImportedAt = Str(row, "importedAt"),
            IsWatchlist = Bool(row, "isWatchlist"),
            WatchlistReason = Str(row, "watchlistReason"),
            IsPartialReturn = Bool(row, "isPartialReturn"),
            PhotoId = Str(row, "photoId"),
            Items = Items(row),
            ExtraJson = Extra(row),
        };

        o.Id = StableId(category, o);
        return o;
    }

    /// <summary>Chủ dự án chốt nhãn ngắn "TikTok" (app cũ ghi lẫn "TikTok Shop"). Các sàn khác giữ nguyên.</summary>
    public static string NormalizePlatform(string platform)
    {
        var t = platform.Trim();
        return t.Equals("TikTok Shop", StringComparison.OrdinalIgnoreCase) || t.Equals("TikTok", StringComparison.OrdinalIgnoreCase)
            ? "TikTok" : platform;
    }

    /// <summary>Ảnh nhúng dạng data-URI trong trường "photo".</summary>
    public static bool HasInlinePhoto(JsonElement row) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty("photo", out var p)
        && p.ValueKind == JsonValueKind.String && (p.GetString() ?? "").StartsWith("data:", StringComparison.Ordinal);

    /// <summary>Khóa nội bộ ổn định: nhập lại cùng dữ liệu cho cùng Id (idempotent).</summary>
    private static string StableId(LegacyCategory c, Order o)
    {
        var key = $"{c}|{o.Platform.Trim().ToUpperInvariant()}|{o.OrderId.Trim()}|{o.Track.Trim()}|{o.ReturnOrderId.Trim()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32].ToLowerInvariant();
    }

    private static string Str(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "",
        };
    }

    private static decimal Dec(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : 0m;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static List<string> StrList(JsonElement e, string name)
    {
        var list = new List<string>();
        if (e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array)
            foreach (var x in v.EnumerateArray())
                if (x.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(x.GetString())) list.Add(x.GetString()!);
        return list;
    }

    private static List<OrderItem> Items(JsonElement e)
    {
        var list = new List<OrderItem>();
        if (e.TryGetProperty("items", out var v) && v.ValueKind == JsonValueKind.Array)
            foreach (var x in v.EnumerateArray())
            {
                if (x.ValueKind != JsonValueKind.Object) continue;
                list.Add(new OrderItem
                {
                    Product = Str(x, "product"),
                    Color = Str(x, "color"),
                    Size = Str(x, "size"),
                    Qty = x.TryGetProperty("qty", out var q) && q.ValueKind == JsonValueKind.Number && q.TryGetInt32(out var n) ? n : 0,
                    // Mã SKU của app cũ nằm ở "sku" hoặc "sellerSku" (khóa gộp sản phẩm dùng sku || sellerSku || skuId).
                    Sku = x.TryGetProperty("sku", out _) && Str(x, "sku").Length > 0 ? Str(x, "sku")
                        : x.TryGetProperty("sellerSku", out _) && Str(x, "sellerSku").Length > 0 ? Str(x, "sellerSku")
                        : x.TryGetProperty("sku", out _) ? Str(x, "sku") : null,
                    SkuId = x.TryGetProperty("skuId", out _) ? Str(x, "skuId") : null,
                    ImportPrices = ImportPrices(x),
                });
            }
        return list;
    }

    private static Dictionary<string, long>? ImportPrices(JsonElement item)
    {
        if (!item.TryGetProperty("importPrices", out var p) || p.ValueKind != JsonValueKind.Object) return null;
        var d = new Dictionary<string, long>();
        foreach (var kv in p.EnumerateObject())
            if (kv.Value.ValueKind == JsonValueKind.Number && kv.Value.TryGetDouble(out var n) && !double.IsNaN(n)) d[kv.Name] = (long)Math.Round(n);
        return d;
    }

    /// <summary>Mọi trường chưa có cột riêng (trừ ảnh data-URI, nặng và đã nằm trong RawJson).</summary>
    private static string Extra(JsonElement row)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            foreach (var p in row.EnumerateObject())
            {
                if (Mapped.Contains(p.Name)) continue;
                if (p.Name == "photo" && p.Value.ValueKind == JsonValueKind.String
                    && (p.Value.GetString() ?? "").StartsWith("data:", StringComparison.Ordinal)) continue;
                w.WritePropertyName(p.Name);
                p.Value.WriteTo(w);
            }
            w.WriteEndObject();
        }
        var text = Encoding.UTF8.GetString(ms.ToArray());
        return text == "{}" ? "" : text;
    }
}

