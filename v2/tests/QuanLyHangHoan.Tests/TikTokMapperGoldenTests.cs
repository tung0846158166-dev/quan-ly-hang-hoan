using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using QuanLyHangHoan.Core.Import;

namespace QuanLyHangHoan.Tests;

/// <summary>
/// Đối chiếu bộ ánh xạ C# với "đáp án chuẩn" sinh từ MÃ JS CŨ (v2/tools/legacy-oracle, Golden/*.json). Dữ liệu GIẢ.
/// Mỗi ca chạy cùng dòng giả; mọi chênh lệch được gom lại để thấy hết một lượt.
/// </summary>
public sealed class TikTokMapperGoldenTests
{
    private static string GoldenDir([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Golden");

    private static string Str(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => "",
    };

    private static List<Dictionary<string, string>> Rows(JsonElement rows) =>
        rows.EnumerateArray().Select(r => r.EnumerateObject().ToDictionary(p => p.Name, p => Str(p.Value))).ToList();

    private static readonly DateTime FrozenToday = new(2026, 11, 23);

    private static void Eq(List<string> diffs, string where, string field, string? expected, string? actual)
    {
        if (expected != actual) diffs.Add($"{where}.{field}: đáp án=[{expected}] C#=[{actual}]");
    }

    private static string? OptStr(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? Str(v) : null;

    private static void CompareParcel(List<string> diffs, string where, JsonElement g, ImportedParcel p)
    {
        Eq(diffs, where, "shipDate", OptStr(g, "shipDate"), p.ShipDate);
        Eq(diffs, where, "returnDate", OptStr(g, "returnDate"), p.ReturnDate);
        Eq(diffs, where, "recvDate", OptStr(g, "recvDate"), p.RecvDate);
        Eq(diffs, where, "platform", OptStr(g, "platform"), p.Platform);
        Eq(diffs, where, "orderType", OptStr(g, "orderType"), p.OrderType);
        Eq(diffs, where, "isPartialReturn", OptStr(g, "isPartialReturn"), p.IsPartialReturn ? "true" : "false");
        Eq(diffs, where, "platformStatus", OptStr(g, "platformStatus"), p.PlatformStatus);
        Eq(diffs, where, "returnedAt", OptStr(g, "returnedAt"), p.ReturnedAt);
        Eq(diffs, where, "preOrder", OptStr(g, "preOrder"), p.PreOrder);
        Eq(diffs, where, "orderId", OptStr(g, "orderId"), p.OrderId);
        Eq(diffs, where, "returnOrderId", OptStr(g, "returnOrderId"), p.ReturnOrderId);
        Eq(diffs, where, "forwardTrack", OptStr(g, "forwardTrack"), p.ForwardTrack);
        Eq(diffs, where, "returnTrack", OptStr(g, "returnTrack"), p.ReturnTrack);
        Eq(diffs, where, "track", OptStr(g, "track"), p.Track);
        Eq(diffs, where, "altTrack", OptStr(g, "altTrack"), p.AltTrack);
        Eq(diffs, where, "carrier", OptStr(g, "carrier"), p.Carrier);
        Eq(diffs, where, "price", OptStr(g, "price"), p.Price.ToString());
        Eq(diffs, where, "priceNeedsReview", OptStr(g, "priceNeedsReview"), p.PriceNeedsReview is null ? null : p.PriceNeedsReview.Value ? "true" : "false");
        Eq(diffs, where, "importSelectedPriceMode", OptStr(g, "importSelectedPriceMode"), p.ImportSelectedPriceMode);
        Eq(diffs, where, "status", OptStr(g, "status"), p.Status);
        Eq(diffs, where, "photo", OptStr(g, "photo"), p.Photo);
        Eq(diffs, where, "note", OptStr(g, "note"), p.Note);
        Eq(diffs, where, "buyerNote", OptStr(g, "buyerNote"), p.BuyerNote);

        var codes = g.GetProperty("trackingCodes").EnumerateArray().Select(Str).ToList();
        Eq(diffs, where, "trackingCodes", string.Join("|", codes), string.Join("|", p.TrackingCodes));

        var pm = g.GetProperty("priceModes");
        foreach (var mode in new[] { "tong_gia_tri", "sau_chiet_khau", "gia_salework", "doanh_thu_uoc_tinh" })
            Eq(diffs, where, "priceModes." + mode, OptStr(pm, mode), p.PriceModes.TryGetValue(mode, out var v) ? v.ToString() : null);
        foreach (var flag in new[] { "hasOrderLevelZ", "hasOrderLevelAM", "hasOrderLevelAO", "hasOrderLevelL" })
            Eq(diffs, where, "priceModes." + flag, OptStr(pm, flag), p.PriceModeFlags.TryGetValue(flag, out var f) ? (f ? "true" : "false") : null);

        var slots = g.TryGetProperty("importModeSlots", out var sl) ? sl : default;
        foreach (var mode in new[] { "tong_gia_tri", "sau_chiet_khau", "gia_salework", "doanh_thu_uoc_tinh" })
        {
            var expected = slots.ValueKind == JsonValueKind.Object && slots.TryGetProperty(mode, out var ev) ? (ev.ValueKind == JsonValueKind.Null ? "<null>" : Str(ev)) : "<absent>";
            var actual = p.ImportModeSlots.TryGetValue(mode, out var av) ? (av ?? "<null>") : "<absent>";
            Eq(diffs, where, "importModeSlots." + mode, expected, actual);
        }

        var gi = g.GetProperty("items").EnumerateArray().ToList();
        Eq(diffs, where, "items.Count", gi.Count.ToString(), p.Items.Count.ToString());
        for (var i = 0; i < Math.Min(gi.Count, p.Items.Count); i++)
        {
            var w = $"{where}.items[{i}]";
            Eq(diffs, w, "product", OptStr(gi[i], "product"), p.Items[i].Product);
            Eq(diffs, w, "color", OptStr(gi[i], "color"), p.Items[i].Color);
            Eq(diffs, w, "size", OptStr(gi[i], "size"), p.Items[i].Size);
            Eq(diffs, w, "qty", OptStr(gi[i], "qty"), p.Items[i].Qty.ToString());
            Eq(diffs, w, "sellerSku", OptStr(gi[i], "sellerSku"), p.Items[i].SellerSku);
            Eq(diffs, w, "skuId", OptStr(gi[i], "skuId"), p.Items[i].SkuId);
            string Prices(IEnumerable<KeyValuePair<string, string>> kv) => string.Join(",", kv.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}"));
            var expPrices = gi[i].TryGetProperty("importPrices", out var ip) && ip.ValueKind == JsonValueKind.Object
                ? Prices(ip.EnumerateObject().Select(x => new KeyValuePair<string, string>(x.Name, Str(x.Value)))) : "<absent>";
            var actPrices = p.Items[i].ImportPrices is null ? "<absent>" : Prices(p.Items[i].ImportPrices!.Select(x => new KeyValuePair<string, string>(x.Key, x.Value.ToString())));
            Eq(diffs, w, "importPrices", expPrices, actPrices);
        }
    }

    [Theory]
    [InlineData("tiktok", 28)]
    [InlineData("tiktok2", 15)]
    public void Mapper_matches_legacy_js_output_for_every_golden_case_without_existing_orders(string set, int minCases)
    {
        var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenDir(), set + ".cases.json"))).RootElement.EnumerateArray().ToList();
        var golden = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenDir(), set + ".golden.json"))).RootElement.EnumerateArray().ToList();
        Assert.Equal(cases.Count, golden.Count);

        var diffs = new List<string>();
        var checkedCases = 0;
        for (var c = 0; c < cases.Count; c++)
        {
            var name = cases[c].GetProperty("name").GetString()!;
            Assert.Equal(name, golden[c].GetProperty("name").GetString());
            Assert.True(golden[c].GetProperty("error").ValueKind == JsonValueKind.Null, $"[{name}] mã cũ báo lỗi: {golden[c].GetProperty("error")}");
            if (cases[c].TryGetProperty("existing", out var ex) && ex.GetArrayLength() > 0) continue; // ca có đơn sẵn: kiểm ở bước nạp vào DB

            var mode = cases[c].TryGetProperty("mode", out var m) ? m.GetString()! : "tong_gia_tri";
            var options = new TikTokMapOptions { PriceMode = mode, Today = FrozenToday };
            if (cases[c].TryGetProperty("options", out var o))
            {
                if (o.TryGetProperty("onlyReturns", out var or)) options.OnlyReturns = or.GetBoolean();
                if (o.TryGetProperty("skipUnshippedCancel", out var sc)) options.SkipUnshippedCancel = sc.GetBoolean();
            }
            var result = TikTokMapper.Map(Rows(cases[c].GetProperty("rows")), options);
            var expected = golden[c].GetProperty("orders").EnumerateArray().ToList();

            if (expected.Count != result.Parcels.Count) { diffs.Add($"[{name}] số kiện: đáp án={expected.Count} C#={result.Parcels.Count}"); continue; }
            for (var i = 0; i < expected.Count; i++) CompareParcel(diffs, $"[{name}]#{i}", expected[i], TikTokMapper.ForNewRow(result.Parcels[i], FrozenToday));
            checkedCases++;
        }

        Assert.True(checkedCases >= minCases, $"Chỉ đối chiếu {checkedCases} ca");
        Assert.True(diffs.Count == 0, "Có " + diffs.Count + " chênh lệch so với mã cũ:\n" + string.Join("\n", diffs.Take(60)));
    }

    [Fact]
    public void Detects_tiktok_export_by_characteristic_columns()
    {
        Assert.True(TikTokMapper.IsTikTokExport(new List<Dictionary<string, string>> { new() { ["Order ID"] = "1", ["Return Order ID"] = "2" } }));
        Assert.True(TikTokMapper.IsTikTokExport(new List<Dictionary<string, string>> { new() { ["Order ID"] = "1", ["Cancel Reason"] = "x" } }));
        Assert.False(TikTokMapper.IsTikTokExport(new List<Dictionary<string, string>> { new() { ["Mã đơn hàng"] = "1", ["Tên sản phẩm"] = "x" } }));
    }
}
