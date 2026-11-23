using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Tests;

/// <summary>Cả luồng nạp Shopee (ánh xạ + ghép vào bảng đơn) so với mã JS cũ, kể cả ca có đơn sẵn. Dữ liệu GIẢ.</summary>
public sealed class ShopeeMergerGoldenTests
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
    private static string P(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var v) ? Str(v) : "";
    private static bool Has(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var v) && v.ValueKind != JsonValueKind.Null;

    private static Order ToOrder(JsonElement e) => new()
    {
        ShipDate = P(e, "shipDate"), ReturnDate = P(e, "returnDate"), Platform = ParcelMerge.NormalizePlatform(P(e, "platform")), OrderType = P(e, "orderType"),
        PlatformStatus = P(e, "platformStatus"), OrderId = P(e, "orderId"), Track = P(e, "track"), Carrier = P(e, "carrier"),
        Price = decimal.TryParse(P(e, "price"), out var pr) ? pr : 0, Status = P(e, "status"), Note = P(e, "note"), PreOrder = P(e, "preOrder"),
        TrackingCodes = Has(e, "trackingCodes") ? e.GetProperty("trackingCodes").EnumerateArray().Select(Str).ToList() : new(),
        Items = Has(e, "items") ? e.GetProperty("items").EnumerateArray().Select(i => new OrderItem
            { Product = P(i, "product"), Color = P(i, "color"), Size = P(i, "size"), Qty = int.TryParse(P(i, "qty"), out var q) ? q : 1 }).ToList() : new(),
    };

    private static void Eq(List<string> d, string w, string f, string? exp, string? act)
    {
        if (exp != act) d.Add($"{w}.{f}: mã cũ=[{exp}] C#=[{act}]");
    }

    [Theory]
    [InlineData("shopee", 12)]
    [InlineData("shopee2", 10)]
    public void Whole_flow_matches_legacy_js(string set, int minCases)
    {
        var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenDir(), set + ".cases.json"))).RootElement.EnumerateArray().ToList();
        var golden = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenDir(), set + ".golden.json"))).RootElement.EnumerateArray().ToList();
        var diffs = new List<string>();
        var done = 0;
        for (var c = 0; c < cases.Count; c++)
        {
            var k = cases[c];
            var name = k.GetProperty("name").GetString()!;
            var options = new ShopeeMapOptions { Today = FrozenToday };
            if (k.TryGetProperty("options", out var o) && o.TryGetProperty("onlyReturns", out var or)) options.OnlyReturns = or.GetBoolean();
            var main = k.TryGetProperty("existing", out var ex) ? ex.EnumerateArray().Select(ToOrder).ToList() : new List<Order>();
            var rows = k.GetProperty("rows").EnumerateArray().Select(r => r.EnumerateObject().ToDictionary(p => p.Name, p => Str(p.Value))).ToList();
            var map = ShopeeMapper.Map(rows, options);
            TikTokMerger.Apply(main, new List<OtherEntry>(), map.Parcels, ImportProductMode.Supplement, FrozenToday, ShopeeMapper.ForNewRow);

            var g = golden[c].GetProperty("orders").EnumerateArray().ToList();
            if (g.Count != main.Count) { diffs.Add($"[{name}] số dòng: mã cũ={g.Count} C#={main.Count}"); continue; }
            for (var i = 0; i < g.Count; i++)
            {
                var w = $"[{name}]#{i}";
                Eq(diffs, w, "orderId", P(g[i], "orderId"), main[i].OrderId);
                Eq(diffs, w, "track", P(g[i], "track"), main[i].Track);
                Eq(diffs, w, "platform", ParcelMerge.NormalizePlatform(P(g[i], "platform")), main[i].Platform);
                Eq(diffs, w, "orderType", P(g[i], "orderType"), main[i].OrderType);
                Eq(diffs, w, "platformStatus", P(g[i], "platformStatus"), main[i].PlatformStatus);
                Eq(diffs, w, "status", P(g[i], "status"), main[i].Status);
                Eq(diffs, w, "price", Has(g[i], "price") ? P(g[i], "price") : "0", main[i].Price.ToString());
                Eq(diffs, w, "note", P(g[i], "note"), main[i].Note);
                Eq(diffs, w, "deadline", P(g[i], "deadline"), main[i].Deadline);
                Eq(diffs, w, "trackingCodes", string.Join("|", g[i].GetProperty("trackingCodes").EnumerateArray().Select(Str)), string.Join("|", main[i].TrackingCodes));
                Eq(diffs, w, "importPriceBasis", Has(g[i], "importPriceBasis") ? P(g[i], "importPriceBasis") : null, main[i].Import.ImportPriceBasis);
                if (Has(g[i], "rawDetails"))
                    foreach (var prop in g[i].GetProperty("rawDetails").EnumerateObject())
                        Eq(diffs, w, "rawDetails." + prop.Name, Str(prop.Value), main[i].Import.RawDetails.TryGetValue(prop.Name, out var v) ? v : "<thiếu>");
                var gi = g[i].GetProperty("items").EnumerateArray().ToList();
                Eq(diffs, w, "items.Count", gi.Count.ToString(), main[i].Items.Count.ToString());
                for (var n = 0; n < Math.Min(gi.Count, main[i].Items.Count); n++)
                {
                    Eq(diffs, w + $".items[{n}]", "product", P(gi[n], "product"), main[i].Items[n].Product);
                    Eq(diffs, w + $".items[{n}]", "color", P(gi[n], "color"), main[i].Items[n].Color);
                    Eq(diffs, w + $".items[{n}]", "size", P(gi[n], "size"), main[i].Items[n].Size);
                    Eq(diffs, w + $".items[{n}]", "qty", P(gi[n], "qty"), main[i].Items[n].Qty.ToString());
                }
            }
            done++;
        }
        Assert.True(done >= minCases, $"Chỉ đối chiếu {done} ca");
        Assert.True(diffs.Count == 0, $"{diffs.Count} chênh lệch với mã cũ:\n" + string.Join("\n", diffs.Take(60)));
    }
}
