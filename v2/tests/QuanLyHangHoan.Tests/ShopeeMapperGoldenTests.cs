using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using QuanLyHangHoan.Core.Import;

namespace QuanLyHangHoan.Tests;

/// <summary>Đối chiếu ShopeeMapper với đáp án sinh từ MÃ JS CŨ (ingestShopeeRecords) ở Golden/shopee.*.json. Dữ liệu GIẢ.</summary>
public sealed class ShopeeMapperGoldenTests
{
    private static string GoldenDir([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Golden");
    private static readonly DateTime FrozenToday = new(2026, 11, 23);

    private static string Str(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => "",
    };

    private static string? Opt(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? Str(v) : null;

    private static void Eq(List<string> d, string where, string field, string? exp, string? act)
    {
        if (exp != act) d.Add($"{where}.{field}: đáp án=[{exp}] C#=[{act}]");
    }

    private static void Compare(List<string> d, string w, JsonElement g, ImportedParcel p)
    {
        Eq(d, w, "shipDate", Opt(g, "shipDate"), p.ShipDate);
        Eq(d, w, "returnDate", Opt(g, "returnDate"), p.ReturnDate);
        Eq(d, w, "platform", Opt(g, "platform"), p.Platform);
        Eq(d, w, "platformStatus", Opt(g, "platformStatus"), p.PlatformStatus);
        Eq(d, w, "orderType", Opt(g, "orderType"), p.OrderType);
        Eq(d, w, "isPartialReturn", Opt(g, "isPartialReturn"), p.IsPartialReturn ? "true" : "false");
        Eq(d, w, "preOrder", Opt(g, "preOrder"), p.PreOrder);
        Eq(d, w, "deadline", Opt(g, "deadline"), p.Deadline);
        Eq(d, w, "orderId", Opt(g, "orderId"), p.OrderId);
        Eq(d, w, "track", Opt(g, "track"), p.Track);
        Eq(d, w, "altTrack", Opt(g, "altTrack"), p.AltTrack);
        Eq(d, w, "forwardTrack", Opt(g, "forwardTrack"), p.ForwardTrack);
        Eq(d, w, "returnTrack", Opt(g, "returnTrack"), p.ReturnTrack);
        Eq(d, w, "carrier", Opt(g, "carrier"), p.Carrier);
        Eq(d, w, "forwardCarrier", Opt(g, "forwardCarrier"), p.RawDetails?["forwardCarrier"]);
        Eq(d, w, "returnCarrier", Opt(g, "returnCarrier"), p.RawDetails?["returnCarrier"]);
        Eq(d, w, "price", Opt(g, "price"), p.Price.ToString());
        Eq(d, w, "importPriceBasis", Opt(g, "importPriceBasis"), p.ImportPriceBasis);
        Eq(d, w, "priceNeedsReview", Opt(g, "priceNeedsReview"), p.PriceNeedsReview is null ? null : p.PriceNeedsReview.Value ? "true" : "false");
        Eq(d, w, "status", Opt(g, "status"), p.Status);
        Eq(d, w, "note", Opt(g, "note"), p.Note);
        Eq(d, w, "buyerNote", Opt(g, "buyerNote"), p.BuyerNote);
        Eq(d, w, "trackingCodes", string.Join("|", g.GetProperty("trackingCodes").EnumerateArray().Select(Str)), string.Join("|", p.TrackingCodes));

        var raw = g.GetProperty("rawDetails");
        foreach (var prop in raw.EnumerateObject())
            Eq(d, w, "rawDetails." + prop.Name, Str(prop.Value), p.RawDetails!.TryGetValue(prop.Name, out var v) ? v : "<thiếu>");

        var gi = g.GetProperty("items").EnumerateArray().ToList();
        Eq(d, w, "items.Count", gi.Count.ToString(), p.Items.Count.ToString());
        for (var i = 0; i < Math.Min(gi.Count, p.Items.Count); i++)
        {
            var iw = $"{w}.items[{i}]";
            Eq(d, iw, "product", Opt(gi[i], "product"), p.Items[i].Product);
            Eq(d, iw, "color", Opt(gi[i], "color"), p.Items[i].Color);
            Eq(d, iw, "size", Opt(gi[i], "size"), p.Items[i].Size);
            Eq(d, iw, "qty", Opt(gi[i], "qty"), p.Items[i].Qty.ToString());
            Eq(d, iw, "sku", Opt(gi[i], "sku") ?? "", p.Items[i].SellerSku ?? "");
            Eq(d, iw, "importLineAmount", Opt(gi[i], "importLineAmount"), p.Items[i].ImportLineAmount?.ToString());
        }
    }

    [Theory]
    [InlineData("shopee", 12)]
    [InlineData("shopee2", 10)]
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
            Assert.True(golden[c].GetProperty("error").ValueKind == JsonValueKind.Null, $"[{name}] mã cũ báo lỗi: {golden[c].GetProperty("error")}");
            if (cases[c].TryGetProperty("existing", out var ex) && ex.GetArrayLength() > 0) continue;
            var options = new ShopeeMapOptions { Today = FrozenToday };
            if (cases[c].TryGetProperty("options", out var o) && o.TryGetProperty("onlyReturns", out var or)) options.OnlyReturns = or.GetBoolean();
            var rows = cases[c].GetProperty("rows").EnumerateArray().Select(r => r.EnumerateObject().ToDictionary(p => p.Name, p => Str(p.Value))).ToList();
            var result = ShopeeMapper.Map(rows, options);
            var expected = golden[c].GetProperty("orders").EnumerateArray().ToList();
            if (expected.Count != result.Parcels.Count) { diffs.Add($"[{name}] số kiện: đáp án={expected.Count} C#={result.Parcels.Count}"); continue; }
            for (var i = 0; i < expected.Count; i++) Compare(diffs, $"[{name}]#{i}", expected[i], ShopeeMapper.ForNewRow(result.Parcels[i], FrozenToday));
            checkedCases++;
        }
        Assert.True(checkedCases >= minCases, $"Chỉ đối chiếu {checkedCases} ca");
        Assert.True(diffs.Count == 0, "Có " + diffs.Count + " chênh lệch so với mã cũ:\n" + string.Join("\n", diffs.Take(60)));
    }
}
