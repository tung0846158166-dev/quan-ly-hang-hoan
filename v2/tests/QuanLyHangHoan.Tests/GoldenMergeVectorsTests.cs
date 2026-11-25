using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Tests;

/// <summary>
/// Sinh/kiểm tra bộ ca thử CHUNG cho bước GỘP kiện nhập vào dữ liệu có sẵn (TikTokMerger.Apply nhánh Shopee/Google Sheet):
/// cloud/golden/merge-vectors.json do CHÍNH bản C# sinh; bản TypeScript trên cloud (cloud/core/src/merge.ts) phải ra cùng kết quả.
/// Sinh lại: đặt biến môi trường QLHH_REGEN_GOLDEN=1. Dữ liệu GIẢ, ngẫu nhiên với hạt giống cố định.
/// </summary>
public sealed class GoldenMergeVectorsTests
{
    private static string VectorFile([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "cloud", "golden", "merge-vectors.json"));

    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // ---------------------------------------------------------------- sinh ngẫu nhiên
    private static T Pick<T>(Random r, params T[] a) => a[r.Next(a.Length)];

    private static readonly string[] OrderIds = { "O1", "O2", "261107ABCDEF", "SPXVN000000001" };
    private static readonly string[] Tracks = { "", "", "SPXVN000000001", "SPXVN000000002", "SPXVN000000003", "T9", "https://spx.vn/track?spx=SPXVN000000002" };
    private static readonly string[] Claims = { "", "", "C1", "C2" };
    private static readonly string[] Products = { "Áo thun", "Áo thun nam cổ tròn", "Quần jean", "", "Sản phẩm mới" };
    private static readonly string[] Colors = { "", "Đen", "Trắng", "khách đổi ý không nhận hàng" };
    private static readonly string[] Sizes = { "", "M", "L", "Đen", "giao hàng thất bại vì người nhận không nghe máy" };
    private static readonly string[] Statuses = { "Chưa trả", "Hàng đã trả", "Không cần nhận" };
    private static readonly string[] PStatus = { "", "Đã đồng ý", "Đang vận chuyển", "Đã hoàn tiền" };

    private static List<OrderItem> Items(Random r)
    {
        var n = r.Next(0, 3);
        var l = new List<OrderItem>();
        for (var i = 0; i < n; i++)
            l.Add(new OrderItem { Product = Pick(r, Products), Color = Pick(r, Colors), Size = Pick(r, Sizes), Qty = r.Next(0, 4), ReturnedQty = r.Next(0, 2), Sku = r.Next(5) == 0 ? "SKU" + r.Next(3) : null });
        return l;
    }

    private static Order MakeOrder(Random r, int i) => new()
    {
        Id = "e" + i, Platform = Pick(r, "Shopee", "TikTok", ""), OrderId = Pick(r, OrderIds), ReturnOrderId = Pick(r, Claims),
        Track = Pick(r, Tracks), ReturnTrack = Pick(r, "", "", "SPXVN000000003", "R1"), ForwardTrack = Pick(r, "", "F1", "SPXVN000000001"),
        AltTrack = Pick(r, "", "", "A1"), TrackingCodes = r.Next(4) == 0 ? new() { Pick(r, "SPXVN000000002", "R1", "OLD1") } : new(),
        Status = Pick(r, Statuses), PlatformStatus = Pick(r, PStatus), OrderType = Pick(r, "", "Trả hàng/Hoàn tiền", "Đơn hủy"), Carrier = Pick(r, "", "SPX Express", "GHN"),
        ShipDate = Pick(r, "", "01/11/2026"), ReturnDate = Pick(r, "", "03/11/2026"), ReturnedAt = Pick(r, "", "04/11/2026 10:00"), PreOrder = Pick(r, "", "Đơn thường"),
        Price = Pick(r, 0, 150000, 99000), Note = Pick(r, "", "Sai mô tả"), UserNote = Pick(r, "", "ghi chú tay"), IsWatchlist = r.Next(5) == 0,
        RecvDate = Pick(r, "", "05/11/2026 09:00:00"), Deadline = Pick(r, "", "06/11/2026 10:00"), Items = Items(r),
    };

    private static ImportedParcel MakeIncoming(Random r)
    {
        var p = new ImportedParcel
        {
            Platform = Pick(r, "Shopee", "TikTok Shop"), OrderId = Pick(r, OrderIds), ReturnOrderId = Pick(r, Claims), Track = Pick(r, Tracks),
            ReturnTrack = Pick(r, "", "SPXVN000000003", "R1"), ForwardTrack = Pick(r, "", "F1", "SPXVN000000001"), AltTrack = Pick(r, "", "A1"),
            TrackingCodes = r.Next(3) == 0 ? new() { Pick(r, "SPXVN000000002", "R1") } : new(),
            PlatformStatus = Pick(r, PStatus), OrderType = Pick(r, "", "Trả hàng/Hoàn tiền", "Đơn hủy", "Giao không thành công"), Carrier = Pick(r, "", "SPX Express", "J&T"),
            ShipDate = Pick(r, "", "2/11", "2026-11-02"), ReturnDate = Pick(r, "", "07/11/2026"), ReturnedAt = Pick(r, "", "08/11/2026 11:00"),
            PreOrder = Pick(r, "Đơn thường", "Hàng đặt trước", ""), Price = Pick(r, 0L, 120000L, 99000L), PriceHasValue = r.Next(2) == 0,
            PriceNeedsReview = Pick<bool?>(r, null, true, false), Note = Pick(r, "", "Hàng lỗi"), BuyerNote = Pick(r, "", "khách ghi"),
            Deadline = Pick(r, "", "09/11/2026 10:00"), IsPartialReturn = r.Next(4) == 0, Status = "Chưa về",
            PriceModes = r.Next(3) == 0 ? new() { ["tong_gia_tri"] = 120000 } : new(), ImportModeSlots = r.Next(2) == 0 ? new() { ["tong_gia_tri"] = null } : new(),
            ImportPriceBasis = Pick(r, "", "order"),
            RawDetails = r.Next(3) == 0 ? new() { ["returnStatus"] = Pick(r, "Đã hủy", "Đang xử lý"), ["isFullReturn"] = Pick(r, "Hoàn một phần", "Hoàn toàn bộ"), ["forwardCarrier"] = "SPX" } : null,
        };
        var n = r.Next(0, 3);
        for (var i = 0; i < n; i++)
            p.Items.Add(new ImportedItem { Product = Pick(r, Products), Color = Pick(r, Colors), Size = Pick(r, Sizes), Qty = r.Next(1, 4), ReturnedQty = r.Next(0, 2), SellerSku = r.Next(5) == 0 ? "SKU" + r.Next(3) : null });
        return p;
    }

    // ---------------------------------------------------------------- JSON (cùng hình dạng TypeScript)
    private static JsonObject Item(OrderItem i) => new()
    {
        ["product"] = i.Product, ["color"] = i.Color, ["size"] = i.Size, ["qty"] = i.Qty, ["returnedQty"] = i.ReturnedQty,
        ["sku"] = i.Sku, ["skuId"] = i.SkuId, ["importPrices"] = i.ImportPrices is null ? null : JsonSerializer.SerializeToNode(i.ImportPrices),
    };

    private static JsonArray Arr(IEnumerable<string> v) => new(v.Select(x => (JsonNode?)x).ToArray());

    public static JsonObject OrderJson(Order o) => new()
    {
        ["id"] = o.Id, ["orderId"] = o.OrderId, ["returnOrderId"] = o.ReturnOrderId, ["track"] = o.Track, ["returnTrack"] = o.ReturnTrack,
        ["altTrack"] = o.AltTrack, ["forwardTrack"] = o.ForwardTrack, ["trackingCodes"] = Arr(o.TrackingCodes), ["scanAliases"] = Arr(o.ScanAliases),
        ["platform"] = o.Platform, ["orderType"] = o.OrderType, ["platformStatus"] = o.PlatformStatus, ["preOrder"] = o.PreOrder, ["status"] = o.Status,
        ["carrier"] = o.Carrier, ["shipDate"] = o.ShipDate, ["returnDate"] = o.ReturnDate, ["recvDate"] = o.RecvDate, ["returnedAt"] = o.ReturnedAt,
        ["deadline"] = o.Deadline, ["price"] = o.Price, ["note"] = o.Note, ["buyerNote"] = o.BuyerNote, ["userNote"] = o.UserNote,
        ["importWeek"] = o.ImportWeek, ["importedAt"] = o.ImportedAt, ["isWatchlist"] = o.IsWatchlist, ["watchlistReason"] = o.WatchlistReason,
        ["isPartialReturn"] = o.IsPartialReturn, ["photoId"] = o.PhotoId, ["items"] = new JsonArray(o.Items.Select(i => (JsonNode)Item(i)).ToArray()),
        ["import"] = new JsonObject
        {
            ["priceModes"] = JsonSerializer.SerializeToNode(o.Import.PriceModes), ["priceModeFlags"] = JsonSerializer.SerializeToNode(o.Import.PriceModeFlags),
            ["importModeSlots"] = JsonSerializer.SerializeToNode(o.Import.ImportModeSlots), ["importSelectedPriceMode"] = o.Import.ImportSelectedPriceMode,
            ["priceHasValue"] = o.Import.PriceHasValue, ["priceNeedsReview"] = o.Import.PriceNeedsReview, ["importPriceBasis"] = o.Import.ImportPriceBasis,
            ["forwardCarrier"] = o.Import.ForwardCarrier, ["returnCarrier"] = o.Import.ReturnCarrier, ["rawDetails"] = JsonSerializer.SerializeToNode(o.Import.RawDetails),
        },
    };

    private static JsonObject IncomingJson(ImportedParcel p) => new()
    {
        ["shipDate"] = p.ShipDate, ["returnDate"] = p.ReturnDate, ["recvDate"] = p.RecvDate, ["platform"] = p.Platform, ["orderType"] = p.OrderType,
        ["isPartialReturn"] = p.IsPartialReturn, ["platformStatus"] = p.PlatformStatus, ["returnedAt"] = p.ReturnedAt, ["preOrder"] = p.PreOrder,
        ["orderId"] = p.OrderId, ["returnOrderId"] = p.ReturnOrderId, ["forwardTrack"] = p.ForwardTrack, ["returnTrack"] = p.ReturnTrack,
        ["trackingCodes"] = Arr(p.TrackingCodes), ["track"] = p.Track, ["altTrack"] = p.AltTrack, ["carrier"] = p.Carrier, ["price"] = p.Price,
        ["priceHasValue"] = p.PriceHasValue, ["priceNeedsReview"] = p.PriceNeedsReview, ["importModeSlots"] = JsonSerializer.SerializeToNode(p.ImportModeSlots),
        ["importSelectedPriceMode"] = p.ImportSelectedPriceMode, ["priceModes"] = JsonSerializer.SerializeToNode(p.PriceModes),
        ["priceModeFlags"] = JsonSerializer.SerializeToNode(p.PriceModeFlags), ["status"] = p.Status, ["note"] = p.Note, ["buyerNote"] = p.BuyerNote,
        ["items"] = new JsonArray(p.Items.Select(i => (JsonNode)new JsonObject
        {
            ["product"] = i.Product, ["color"] = i.Color, ["size"] = i.Size, ["qty"] = i.Qty, ["returnedQty"] = i.ReturnedQty,
            ["importPrices"] = i.ImportPrices is null ? null : JsonSerializer.SerializeToNode(i.ImportPrices), ["sellerSku"] = i.SellerSku, ["skuId"] = i.SkuId,
        }).ToArray()),
        ["deadline"] = p.Deadline, ["importPriceBasis"] = p.ImportPriceBasis, ["rawDetails"] = p.RawDetails is null ? null : JsonSerializer.SerializeToNode(p.RawDetails),
    };

    private static JsonArray Build()
    {
        var r = new Random(20261126);
        var today = new DateTime(2026, 11, 26);
        var cases = new JsonArray();
        var r2 = new Random(20261127);
        for (var n = 0; n < 1000; n++)
        {
            var main = Enumerable.Range(0, r.Next(0, 4)).Select(i => MakeOrder(r, i)).ToList();
            var others = Enumerable.Range(0, r.Next(0, 3)).Select(i => new OtherEntry(MakeOrder(r, 100 + i), r.Next(2) == 0 ? OtherWhere.Archive : OtherWhere.Deleted)).ToList();
            var incoming = Enumerable.Range(0, r.Next(1, 4)).Select(_ => MakeIncoming(r)).ToList();
            if (n >= 800)
            {
                // 200 ca dồn vào nhánh "Shopee chưa có mã vận đơn cùng mã đơn" (quyết định 26/11/2026); bộ sinh riêng để 800 ca đầu không đổi
                foreach (var o in main) { o.Platform = Pick(r2, "Shopee", "Shopee", "TikTok"); if (r2.Next(3) != 0) { o.Track = o.ReturnTrack = o.ForwardTrack = o.AltTrack = ""; o.TrackingCodes = new(); } if (r2.Next(3) != 0) o.ReturnOrderId = ""; o.OrderId = Pick(r2, "O1", "O2"); }
                foreach (var p in incoming) { p.Platform = "Shopee"; p.OrderId = Pick(r2, "O1", "O2"); if (r2.Next(3) != 0) p.ReturnOrderId = ""; if (p.Track.Length == 0 && p.ReturnTrack.Length == 0) p.Track = Pick(r2, "SPXVN000000011", "SPXVN000000012"); }
            }
            var mode = r.Next(4) == 0 ? ImportProductMode.Replace : ImportProductMode.Supplement;

            var input = new JsonObject
            {
                ["main"] = new JsonArray(main.Select(o => (JsonNode)OrderJson(o)).ToArray()),
                ["others"] = new JsonArray(others.Select(e => (JsonNode)new JsonObject { ["where"] = e.Where == OtherWhere.Archive ? "archive" : "deleted", ["order"] = OrderJson(e.Order) }).ToArray()),
                ["incoming"] = new JsonArray(incoming.Select(p => (JsonNode)IncomingJson(p)).ToArray()),
                ["mode"] = mode == ImportProductMode.Replace ? "replace" : "supplement",
                ["today"] = today.ToString("dd/MM/yyyy"), ["year"] = today.Year,
            };

            var res = TikTokMerger.Apply(main, others, incoming, mode, today, ShopeeMapper.ForNewRow);
            var addedSet = res.Added.ToHashSet();
            var k = 0;
            foreach (var o in res.Added) o.Id = "new" + (k++);   // id mới: đánh số theo thứ tự thêm (TS dùng cùng bộ sinh)
            cases.Add(new JsonObject
            {
                ["input"] = input,
                ["expected"] = new JsonObject
                {
                    ["main"] = new JsonArray(main.Select(o => (JsonNode)OrderJson(o)).ToArray()),
                    ["others"] = new JsonArray(others.Select(e => (JsonNode)OrderJson(e.Order)).ToArray()),
                    ["updated"] = Arr(res.Updated.Select(o => o.Id)),
                    ["enrichedArchived"] = Arr(res.EnrichedArchived.Select(o => o.Id)),
                    ["ambiguous"] = new JsonArray(res.Ambiguous.Select(a => (JsonNode)new JsonObject
                    {
                        ["platform"] = a.Platform, ["orderId"] = a.OrderId, ["track"] = a.Track, ["returnOrderId"] = a.ReturnOrderId,
                        ["candidates"] = a.Candidates, ["orderConflict"] = a.OrderConflict,
                    }).ToArray()),
                    ["skippedArchivedUnchanged"] = res.SkippedArchivedUnchanged, ["skippedDeleted"] = res.SkippedDeleted, ["enriched"] = res.Enriched,
                },
            });
        }
        return cases;
    }

    [Fact]
    public void Merge_vectors_file_matches_what_the_csharp_merger_produces()
    {
        var text = new JsonObject
        {
            ["description"] = "Bộ ca thử CHUNG cho bước gộp kiện nhập (TikTokMerger.Apply, nhánh Shopee/Google Sheet). Do C# sinh (GoldenMergeVectorsTests). Dữ liệu GIẢ.",
            ["cases"] = Build(),
        }.ToJsonString(Opt);
        var path = VectorFile();
        if (Environment.GetEnvironmentVariable("QLHH_REGEN_GOLDEN") == "1" || !File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        Assert.True(File.ReadAllText(path) == text, "cloud/golden/merge-vectors.json lệch với C#: chạy lại với QLHH_REGEN_GOLDEN=1 nếu đổi quy tắc có chủ ý");
    }
}
