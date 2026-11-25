using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuanLyHangHoan.Core.Import;

namespace QuanLyHangHoan.Tests;

/// <summary>
/// Bộ ca thử CHUNG cho bước Google Sheet -> kiện nhập (SheetPipeline.Build + ShopeeMapper.Map, đúng như SheetSyncService):
/// cloud/golden/sheet-vectors.json do CHÍNH bản C# sinh; bản JavaScript chạy trong Apps Script (cloud/sheet-script/qlhh-core.js) phải ra cùng kết quả.
/// Đầu vào: các ca Sheet và ca file Shopee có sẵn (Golden/*.cases.json, từ mã JS cũ) + ca ngẫu nhiên hạt giống cố định. Sinh lại: QLHH_REGEN_GOLDEN=1.
/// </summary>
public sealed class GoldenSheetVectorsTests
{
    private static string Here([CallerFilePath] string here = "") => Path.GetDirectoryName(here)!;
    private static string VectorFile() => Path.GetFullPath(Path.Combine(Here(), "..", "..", "..", "cloud", "golden", "sheet-vectors.json"));
    private static readonly JsonSerializerOptions Opt = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);

    private static string Str(JsonElement v) => v.ValueKind switch { JsonValueKind.String => v.GetString() ?? "", JsonValueKind.Number => v.GetRawText(), _ => "" };
    private static List<Dictionary<string, string>> Tab(JsonElement t) => t.EnumerateArray().Select(r => r.EnumerateObject().ToDictionary(p => p.Name, p => Str(p.Value))).ToList();

    // ---------------------------------------------------------------- ca ngẫu nhiên kiểu Sheet
    private static T Pick<T>(Random r, params T[] a) => a[r.Next(a.Length)];

    private static List<List<Dictionary<string, string>>> RandomTabs(Random r)
    {
        var ids = new[] { "SP1", "SP2", "SP3", "261120ABCDEF", "5.87E+17" };
        string D() => Pick(r, "", "20/11/2026", "20/11/2026 09:00", "2026-11-21 10:30:00", "21/11/26", "dd/mm/yyyy", "5/11");
        var tabs = new List<List<Dictionary<string, string>>>();
        if (r.Next(4) != 0)
            tabs.Add(Enumerable.Range(0, r.Next(1, 4)).Select(_ => new Dictionary<string, string>
            {
                ["Mã"] = Pick(r, "RR1", "RR2", ""), ["Mã đơn"] = Pick(r, ids), ["Loại"] = Pick(r, "Trả hàng + hoàn tiền", "Bom", "Chỉ hoàn tiền", "Huỷ sau chuẩn bị", "Giao thất bại"),
                ["Mã vận đơn"] = Pick(r, "", "SPXVN000000101", "SPXVN000000102", "https://spx.vn/track?spx=SPXVN000000103"),
                ["Trạng thái về"] = Pick(r, "Đang về", "Đã về shop", "Khách huỷ", ""), ["Về shop lúc"] = D(), ["Hạn khiếu nại"] = D(),
                ["Hàng"] = Pick(r, "Áo thun · Đen, M × 2; Quần short · Be, L × 1", "Giày · Trắng, 42", "Áo khoác - Premium Cao Cấp · Xám, XL", "", "Mũ"),
                ["SL"] = Pick(r, "1", "3", "", "2.00"), ["Tiền"] = Pick(r, "", "250000", "199.000", "150000.00"), ["Lý do"] = Pick(r, "", "Sai mô tả", "Người nhận từ chối"),
                ["Ngày"] = D(), ["Cập nhật"] = D(),
            }).ToList());
        if (r.Next(3) == 0)
            tabs.Add(Enumerable.Range(0, r.Next(1, 3)).Select(_ => new Dictionary<string, string>
            {
                ["Mã trả hàng"] = Pick(r, "TH1", "TH2"), ["Mã đơn"] = Pick(r, ids), ["Trạng thái"] = Pick(r, "Đang xử lý", "Đã hoàn tiền", "Khách huỷ"),
                ["Tên sản phẩm"] = Pick(r, "Áo thun", "Quần jean Đen", "Váy - Trắng - M"), ["Ngày yêu cầu"] = D(), ["SL"] = Pick(r, "1", "2", "x"),
            }).ToList());
        if (r.Next(3) == 0)
            tabs.Add(Enumerable.Range(0, r.Next(1, 3)).Select(_ => new Dictionary<string, string>
            {
                ["Mã đơn"] = Pick(r, ids), ["Loại"] = Pick(r, "Huỷ sau chuẩn bị", "Huỷ trước chuẩn bị", "Bom hàng"), ["Ai huỷ"] = Pick(r, "Người mua", "Hệ thống"),
                ["Hành trình"] = Pick(r, "Chưa gửi", "Đang hoàn"), ["Trạng thái"] = Pick(r, "Đã huỷ", ""), ["Tên sản phẩm"] = Pick(r, "Mũ lưỡi trai", "Kính"),
                ["SL"] = "1", ["Ngày"] = D(), ["Mã vận đơn"] = Pick(r, "", "SPXVN000000201"),
            }).ToList());
        if (r.Next(3) == 0)
            tabs.Add(new() { new() { ["Mã đơn"] = Pick(r, ids), ["Mã vận đơn"] = Pick(r, "SPXVN000000301", "SPXVN000000302") } });
        if (r.Next(5) == 0) tabs.Add(new());
        return tabs;
    }

    // ---------------------------------------------------------------- JSON kiện nhập
    private static JsonObject ParcelJson(ImportedParcel p) => new()
    {
        ["shipDate"] = p.ShipDate, ["returnDate"] = p.ReturnDate, ["recvDate"] = p.RecvDate, ["platform"] = p.Platform, ["orderType"] = p.OrderType,
        ["isPartialReturn"] = p.IsPartialReturn, ["platformStatus"] = p.PlatformStatus, ["returnedAt"] = p.ReturnedAt, ["preOrder"] = p.PreOrder,
        ["orderId"] = p.OrderId, ["returnOrderId"] = p.ReturnOrderId, ["forwardTrack"] = p.ForwardTrack, ["returnTrack"] = p.ReturnTrack,
        ["trackingCodes"] = new JsonArray(p.TrackingCodes.Select(x => (JsonNode?)x).ToArray()), ["track"] = p.Track, ["altTrack"] = p.AltTrack,
        ["carrier"] = p.Carrier, ["price"] = p.Price, ["priceHasValue"] = p.PriceHasValue, ["priceNeedsReview"] = p.PriceNeedsReview,
        ["status"] = p.Status, ["note"] = p.Note, ["buyerNote"] = p.BuyerNote, ["deadline"] = p.Deadline, ["importPriceBasis"] = p.ImportPriceBasis,
        ["items"] = new JsonArray(p.Items.Select(i => (JsonNode)new JsonObject
        {
            ["product"] = i.Product, ["color"] = i.Color, ["size"] = i.Size, ["qty"] = i.Qty, ["returnedQty"] = i.ReturnedQty, ["sellerSku"] = i.SellerSku,
        }).ToArray()),
        ["rawDetails"] = p.RawDetails is null ? null : JsonSerializer.SerializeToNode(p.RawDetails),
    };

    private static JsonObject Run(string name, List<List<Dictionary<string, string>>> tabs, string? skipTypes, string? maxDays, bool onlyReturns)
    {
        var input = new JsonObject
        {
            ["name"] = name,
            ["tabs"] = new JsonArray(tabs.Select(t => (JsonNode)new JsonArray(t.Select(r => (JsonNode)new JsonObject(r.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value)))).ToArray())).ToArray()),
            ["skipTypes"] = skipTypes, ["maxDays"] = maxDays, ["onlyReturns"] = onlyReturns,
            ["now"] = new JsonArray(Now.Year, Now.Month, Now.Day, Now.Hour, Now.Minute, Now.Second),
        };
        JsonObject expected;
        try
        {
            var build = SheetPipeline.Build(tabs, skipTypes, maxDays, Now);
            var map = ShopeeMapper.Map(build.Rows, new ShopeeMapOptions { OnlyReturns = onlyReturns, Today = Now });
            expected = new JsonObject
            {
                ["parcels"] = new JsonArray(map.Parcels.Select(p => (JsonNode)ParcelJson(p)).ToArray()),
                ["build"] = new JsonObject
                {
                    ["parcelCount"] = build.Parcels.Count, ["rowCount"] = build.Rows.Count, ["oldRequestsDropped"] = build.OldRequestsDropped,
                    ["skippedCancelTypes"] = build.SkippedCancelTypes, ["trackLookupTabs"] = build.TrackLookupTabs, ["tracksFilled"] = build.TracksFilled, ["tooOld"] = build.TooOld,
                },
                ["skippedSuccess"] = map.SkippedSuccess,
            };
        }
        catch (FormatException) { expected = new JsonObject { ["error"] = "format" }; }
        return new JsonObject { ["input"] = input, ["expected"] = expected };
    }

    private static JsonArray Build()
    {
        var cases = new JsonArray();
        var golden = Path.Combine(Here(), "Golden");
        foreach (var c in JsonDocument.Parse(File.ReadAllText(Path.Combine(golden, "sheet.cases.json"))).RootElement.EnumerateArray())
            cases.Add(Run("sheet:" + c.GetProperty("name").GetString(), c.GetProperty("tabs").EnumerateArray().Select(Tab).ToList(),
                c.TryGetProperty("skipTypes", out var st) ? st.GetString() : null, c.TryGetProperty("maxDays", out var md) ? md.GetString() : null, true));
        foreach (var file in new[] { "shopee.cases.json", "shopee2.cases.json" })
            foreach (var c in JsonDocument.Parse(File.ReadAllText(Path.Combine(golden, file))).RootElement.EnumerateArray())
                foreach (var only in new[] { true, false })
                    cases.Add(Run(file + ":" + c.GetProperty("name").GetString() + (only ? "" : ":all"), new() { Tab(c.GetProperty("rows")) }, null, null, only));
        var r = new Random(20261126);
        for (var n = 0; n < 400; n++)
            cases.Add(Run("random:" + n, RandomTabs(r), r.Next(4) == 0 ? "huy sau;bom" : null, Pick(r, null, "", "3", "01/11/2026", "30"), r.Next(4) != 0));
        return cases;
    }

    [Fact]
    public void Sheet_vectors_file_matches_what_the_csharp_pipeline_produces()
    {
        var text = new JsonObject
        {
            ["description"] = "Bộ ca thử CHUNG: Google Sheet -> kiện nhập (SheetPipeline.Build + ShopeeMapper.Map). Do C# sinh (GoldenSheetVectorsTests). Dữ liệu GIẢ.",
            ["cases"] = Build(),
        }.ToJsonString(Opt);
        var path = VectorFile();
        if (Environment.GetEnvironmentVariable("QLHH_REGEN_GOLDEN") == "1" || !File.Exists(path)) File.WriteAllText(path, text);
        Assert.True(File.ReadAllText(path) == text, "cloud/golden/sheet-vectors.json lệch với C#: chạy lại với QLHH_REGEN_GOLDEN=1 nếu đổi quy tắc có chủ ý");
    }
}
