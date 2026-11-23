using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Tests;

/// <summary>
/// Toàn bộ luồng nạp file TikTok/Salework (ánh xạ + khớp kiện + gộp vào đơn có sẵn + kho lưu trữ + đã xóa) đối chiếu với đáp án
/// sinh từ MÃ JS CŨ. Ca đánh dấu "v2Deviates" (khớp nhiều kiện) cố ý khác app cũ nên được kiểm riêng. Dữ liệu GIẢ.
/// </summary>
public sealed class TikTokMergerGoldenTests
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

    private static List<Dictionary<string, string>> Rows(JsonElement rows) =>
        rows.EnumerateArray().Select(r => r.EnumerateObject().ToDictionary(p => p.Name, p => Str(p.Value))).ToList();

    private static OrderItem ToItem(JsonElement i) => new()
    {
        Product = P(i, "product"), Color = P(i, "color"), Size = P(i, "size"), Qty = int.TryParse(P(i, "qty"), out var q) ? q : 1,
        Sku = Has(i, "sellerSku") ? P(i, "sellerSku") : Has(i, "sku") ? P(i, "sku") : null,
        SkuId = Has(i, "skuId") ? P(i, "skuId") : null,
        ImportPrices = Has(i, "importPrices") && i.GetProperty("importPrices").ValueKind == JsonValueKind.Object
            ? i.GetProperty("importPrices").EnumerateObject().ToDictionary(x => x.Name, x => long.Parse(Str(x.Value))) : null,
    };

    private static Order ToOrder(JsonElement e)
    {
        var o = new Order
        {
            ShipDate = P(e, "shipDate"), ReturnDate = P(e, "returnDate"), RecvDate = P(e, "recvDate"), Platform = ParcelMerge.NormalizePlatform(P(e, "platform")),
            OrderType = P(e, "orderType"), IsPartialReturn = P(e, "isPartialReturn") == "true", PlatformStatus = P(e, "platformStatus"),
            ReturnedAt = P(e, "returnedAt"), PreOrder = P(e, "preOrder"), OrderId = P(e, "orderId"), ReturnOrderId = P(e, "returnOrderId"),
            ForwardTrack = P(e, "forwardTrack"), ReturnTrack = P(e, "returnTrack"), Track = P(e, "track"), AltTrack = P(e, "altTrack"),
            Carrier = P(e, "carrier"), Price = decimal.TryParse(P(e, "price"), out var pr) ? pr : 0, Status = P(e, "status"),
            PhotoId = P(e, "photo"), Note = P(e, "note"), BuyerNote = P(e, "buyerNote"), UserNote = P(e, "userNote"),
            TrackingCodes = Has(e, "trackingCodes") ? e.GetProperty("trackingCodes").EnumerateArray().Select(Str).ToList() : new(),
            Items = Has(e, "items") ? e.GetProperty("items").EnumerateArray().Select(ToItem).ToList() : new(),
        };
        return o;
    }

    private static void Eq(List<string> d, string where, string field, string? exp, string? act)
    {
        if (exp != act) d.Add($"{where}.{field}: mã cũ=[{exp}] C#=[{act}]");
    }

    private static void CompareOrder(List<string> d, string w, JsonElement g, Order o, bool compareImportMeta)
    {
        Eq(d, w, "shipDate", P(g, "shipDate"), o.ShipDate);
        Eq(d, w, "returnDate", P(g, "returnDate"), o.ReturnDate);
        Eq(d, w, "recvDate", P(g, "recvDate"), o.RecvDate);
        Eq(d, w, "platform", ParcelMerge.NormalizePlatform(P(g, "platform")), o.Platform);
        Eq(d, w, "orderType", P(g, "orderType"), o.OrderType);
        Eq(d, w, "isPartialReturn", P(g, "isPartialReturn") == "true" ? "true" : "false", o.IsPartialReturn ? "true" : "false");
        Eq(d, w, "platformStatus", P(g, "platformStatus"), o.PlatformStatus);
        Eq(d, w, "returnedAt", P(g, "returnedAt"), o.ReturnedAt);
        Eq(d, w, "preOrder", P(g, "preOrder"), o.PreOrder);
        Eq(d, w, "orderId", P(g, "orderId"), o.OrderId);
        Eq(d, w, "returnOrderId", P(g, "returnOrderId"), o.ReturnOrderId);
        Eq(d, w, "forwardTrack", P(g, "forwardTrack"), o.ForwardTrack);
        Eq(d, w, "returnTrack", P(g, "returnTrack"), o.ReturnTrack);
        Eq(d, w, "track", P(g, "track"), o.Track);
        Eq(d, w, "altTrack", P(g, "altTrack"), o.AltTrack);
        Eq(d, w, "carrier", P(g, "carrier"), o.Carrier);
        Eq(d, w, "price", Has(g, "price") ? P(g, "price") : "0", o.Price.ToString());
        Eq(d, w, "status", P(g, "status"), o.Status);
        Eq(d, w, "note", P(g, "note"), o.Note);
        Eq(d, w, "buyerNote", P(g, "buyerNote"), o.BuyerNote);
        Eq(d, w, "userNote", P(g, "userNote"), o.UserNote);
        var gc = Has(g, "trackingCodes") ? string.Join("|", g.GetProperty("trackingCodes").EnumerateArray().Select(Str)) : "";
        Eq(d, w, "trackingCodes", gc, string.Join("|", o.TrackingCodes));

        var gi = Has(g, "items") ? g.GetProperty("items").EnumerateArray().ToList() : new List<JsonElement>();
        Eq(d, w, "items.Count", gi.Count.ToString(), o.Items.Count.ToString());
        for (var i = 0; i < Math.Min(gi.Count, o.Items.Count); i++)
        {
            var wi = $"{w}.items[{i}]";
            Eq(d, wi, "product", P(gi[i], "product"), o.Items[i].Product);
            Eq(d, wi, "color", P(gi[i], "color"), o.Items[i].Color);
            Eq(d, wi, "size", P(gi[i], "size"), o.Items[i].Size);
            Eq(d, wi, "qty", P(gi[i], "qty"), o.Items[i].Qty.ToString());
            Eq(d, wi, "sku", Has(gi[i], "sellerSku") ? P(gi[i], "sellerSku") : Has(gi[i], "sku") ? P(gi[i], "sku") : null, o.Items[i].Sku);
            Eq(d, wi, "skuId", Has(gi[i], "skuId") ? P(gi[i], "skuId") : null, o.Items[i].SkuId);
            string Pr(IEnumerable<KeyValuePair<string, string>> kv) => string.Join(",", kv.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}"));
            var exp = Has(gi[i], "importPrices") && gi[i].GetProperty("importPrices").ValueKind == JsonValueKind.Object
                ? Pr(gi[i].GetProperty("importPrices").EnumerateObject().Select(x => new KeyValuePair<string, string>(x.Name, Str(x.Value)))) : "<absent>";
            var act = o.Items[i].ImportPrices is null ? "<absent>" : Pr(o.Items[i].ImportPrices!.Select(x => new KeyValuePair<string, string>(x.Key, x.Value.ToString())));
            Eq(d, wi, "importPrices", exp, act);
        }

        if (!compareImportMeta) return;
        var im = o.Import;
        Eq(d, w, "priceHasValue", Has(g, "priceHasValue") ? P(g, "priceHasValue") : null, im.PriceHasValue is null ? null : im.PriceHasValue.Value ? "true" : "false");
        Eq(d, w, "priceNeedsReview", Has(g, "priceNeedsReview") ? P(g, "priceNeedsReview") : null, im.PriceNeedsReview is null ? null : im.PriceNeedsReview.Value ? "true" : "false");
        Eq(d, w, "importSelectedPriceMode", Has(g, "importSelectedPriceMode") ? P(g, "importSelectedPriceMode") : null, im.ImportSelectedPriceMode);
        if (Has(g, "priceModes"))
        {
            var pm = g.GetProperty("priceModes");
            foreach (var mode in new[] { "tong_gia_tri", "sau_chiet_khau", "gia_salework", "doanh_thu_uoc_tinh" })
                Eq(d, w, "priceModes." + mode, Has(pm, mode) ? P(pm, mode) : null, im.PriceModes.TryGetValue(mode, out var v) ? v.ToString() : null);
            foreach (var f in new[] { "hasOrderLevelZ", "hasOrderLevelAM", "hasOrderLevelAO", "hasOrderLevelL" })
                Eq(d, w, "priceModes." + f, Has(pm, f) ? P(pm, f) : null, im.PriceModeFlags.TryGetValue(f, out var b) ? (b ? "true" : "false") : null);
        }
        if (Has(g, "importModeSlots") && g.GetProperty("importModeSlots").ValueKind == JsonValueKind.Object)
        {
            var sl = g.GetProperty("importModeSlots");
            foreach (var mode in new[] { "tong_gia_tri", "sau_chiet_khau", "gia_salework", "doanh_thu_uoc_tinh" })
            {
                var exp = sl.TryGetProperty(mode, out var ev) ? (ev.ValueKind == JsonValueKind.Null ? "<null>" : Str(ev)) : "<absent>";
                var act = im.ImportModeSlots.TryGetValue(mode, out var av) ? (av ?? "<null>") : "<absent>";
                Eq(d, w, "importModeSlots." + mode, exp, act);
            }
        }
    }

    [Theory]
    [InlineData("tiktok", 30)]
    [InlineData("tiktok2", 15)]
    [InlineData("tiktok3", 8)]
    public void Whole_flow_matches_legacy_js_for_every_golden_case(string set, int minCases)
    {
        var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenDir(), set + ".cases.json"))).RootElement.EnumerateArray().ToList();
        var golden = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenDir(), set + ".golden.json"))).RootElement.EnumerateArray().ToList();
        Assert.Equal(cases.Count, golden.Count);

        var diffs = new List<string>();
        var checkedCases = 0;
        for (var c = 0; c < cases.Count; c++)
        {
            var k = cases[c];
            var name = k.GetProperty("name").GetString()!;
            if (k.TryGetProperty("v2Deviates", out _)) continue;
            Assert.True(golden[c].GetProperty("error").ValueKind == JsonValueKind.Null, $"[{name}] mã cũ báo lỗi");

            var options = new TikTokMapOptions { PriceMode = k.TryGetProperty("mode", out var m) ? m.GetString()! : "tong_gia_tri", Today = FrozenToday };
            var productMode = ImportProductMode.Supplement;
            JsonElement opt = default;
            if (k.TryGetProperty("options", out opt))
            {
                if (opt.TryGetProperty("onlyReturns", out var or)) options.OnlyReturns = or.GetBoolean();
                if (opt.TryGetProperty("skipUnshippedCancel", out var sc)) options.SkipUnshippedCancel = sc.GetBoolean();
                if (opt.TryGetProperty("productMode", out var pmode) && pmode.GetString() == "replace") productMode = ImportProductMode.Replace;
            }

            var main = k.TryGetProperty("existing", out var ex) ? ex.EnumerateArray().Select(ToOrder).ToList() : new List<Order>();
            var others = new List<OtherEntry>();
            if (opt.ValueKind == JsonValueKind.Object && opt.TryGetProperty("archived", out var ar))
                others.AddRange(ar.EnumerateArray().Select(a => new OtherEntry(ToOrder(a), OtherWhere.Archive)));
            if (opt.ValueKind == JsonValueKind.Object && opt.TryGetProperty("deleted", out var de))
                others.AddRange(de.EnumerateArray().Select(a => new OtherEntry(ToOrder(a), OtherWhere.Deleted)));

            var map = TikTokMapper.Map(Rows(k.GetProperty("rows")), options);
            TikTokMerger.Apply(main, others, map.Parcels, productMode, FrozenToday);

            var gOrders = golden[c].GetProperty("orders").EnumerateArray().ToList();
            if (gOrders.Count != main.Count) { diffs.Add($"[{name}] số dòng bảng chính: mã cũ={gOrders.Count} C#={main.Count}"); continue; }
            for (var i = 0; i < gOrders.Count; i++) CompareOrder(diffs, $"[{name}]#{i}", gOrders[i], main[i], compareImportMeta: true);

            var gArch = golden[c].TryGetProperty("archived", out var ga) ? ga.EnumerateArray().ToList() : new List<JsonElement>();
            var arch = others.Where(o => o.Where == OtherWhere.Archive).Select(o => o.Order).ToList();
            if (gArch.Count != arch.Count) { diffs.Add($"[{name}] số dòng kho lưu trữ: mã cũ={gArch.Count} C#={arch.Count}"); continue; }
            for (var i = 0; i < gArch.Count; i++) CompareOrder(diffs, $"[{name}]#archived{i}", gArch[i], arch[i], compareImportMeta: false);
            checkedCases++;
        }

        Assert.True(checkedCases >= minCases, $"Chỉ đối chiếu {checkedCases} ca");
        Assert.True(diffs.Count == 0, $"{diffs.Count} chênh lệch với mã cũ:\n" + string.Join("\n", diffs.Take(60)));
    }

    [Fact]
    public void Ambiguous_candidates_are_reported_and_nothing_is_created_unlike_the_old_app()
    {
        var a = new Order { Platform = "TikTok", OrderId = "O1", Track = "AAA11111", TrackingCodes = { "SHARED11" }, Items = { new OrderItem { Product = "Shirt" } } };
        var b = new Order { Platform = "TikTok", OrderId = "O1", Track = "BBB22222", TrackingCodes = { "SHARED11" }, Items = { new OrderItem { Product = "Shirt" } } };
        var main = new List<Order> { a, b };
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["Order ID"] = "O1", ["Tracking ID"] = "SHARED11", ["Product Name"] = "Pants", ["Order Status"] = "Refunded", ["Quantity"] = "1" },
        };

        var outcome = TikTokMerger.Apply(main, new List<OtherEntry>(), TikTokMapper.Map(rows, new TikTokMapOptions { Today = FrozenToday }).Parcels,
            ImportProductMode.Supplement, FrozenToday);

        Assert.Single(outcome.Ambiguous);
        Assert.Equal(2, outcome.Ambiguous[0].Candidates);
        Assert.Equal(2, main.Count);                               // không tạo dòng thứ ba
        Assert.Equal("Shirt", a.Items.Single().Product);           // không sửa kiện nào
    }

    [Fact]
    public void Warehouse_fields_are_never_overwritten_by_an_import()
    {
        var existing = new Order
        {
            Platform = "TikTok", OrderId = "O1", ReturnOrderId = "C1", Track = "RET22222", ReturnTrack = "RET22222", Status = ReceiveStatus.Received,
            RecvDate = "10/11/2026 09:00:00", PhotoId = "p_X", UserNote = "đã kiểm", IsWatchlist = true, WatchlistReason = "khách hay hoàn",
            Items = { new OrderItem { Product = "Shirt" } },
        };
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["Order ID"] = "O1", ["Return Order ID"] = "C1", ["Return Logistics Tracking ID"] = "RET22222", ["Product Name"] = "Shirt", ["Return Status"] = "In Process", ["Quantity"] = "1" },
        };

        TikTokMerger.Apply(new List<Order> { existing }, new List<OtherEntry>(), TikTokMapper.Map(rows, new TikTokMapOptions { Today = FrozenToday }).Parcels,
            ImportProductMode.Supplement, FrozenToday);

        Assert.Equal(ReceiveStatus.Received, existing.Status);
        Assert.Equal("10/11/2026 09:00:00", existing.RecvDate);
        Assert.Equal("p_X", existing.PhotoId);
        Assert.Equal("đã kiểm", existing.UserNote);
        Assert.True(existing.IsWatchlist);
        Assert.Equal("khách hay hoàn", existing.WatchlistReason);
    }
}
