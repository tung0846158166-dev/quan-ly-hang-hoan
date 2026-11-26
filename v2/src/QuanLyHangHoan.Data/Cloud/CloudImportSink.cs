using System.Text.Json;
using System.Text.Json.Nodes;
using QuanLyHangHoan.Core.Import;

namespace QuanLyHangHoan.Data.Cloud;

/// <summary>Kết quả nạp qua cloud: cùng kiểu kết quả với nạp cục bộ (giao diện dùng chung) + số dòng chờ cũ được thay (Salework).</summary>
public sealed record CloudSinkResult(TikTokImportResult Import, int Superseded);

/// <summary>
/// Chế độ cloud (chủ dự án 26/11/2026): máy tính nạp file / thư mục / Salework theo CÙNG đường với điện thoại — đọc + bỏ cột cá nhân +
/// chuyển đơn NGAY TRÊN MÁY (TikTokMapper / ShopeeMapper), rồi gửi kiện lên cloud gộp (<c>POST /v1/import/mapped</c>, lô 100).
/// Không gộp vào dữ liệu trên máy nữa: cloud là nơi gộp DUY NHẤT, xử lý lần lượt từng lô -> hai máy nạp cùng lúc không tạo đơn trùng.
/// Máy tính nhận kết quả như mọi thay đổi khác từ cloud. Mất kết nối thì không nạp (báo để thử lại).
/// </summary>
public sealed class CloudImportSink(Func<CloudApi?> api, Func<bool> canWrite)
{
    public const int ParcelBatch = 100;
    public const int OrderIdBatch = 500;

    /// <param name="source">"file" | "folder" | "salework" (nhật ký nạp trên cloud).</param>
    /// <param name="supersedeOrderIds">Chỉ Salework "Đang chờ hoàn": mã đơn trong file để cloud thay dòng chờ cũ chưa có mã vận đơn.</param>
    public CloudSinkResult Apply(IReadOnlyList<IReadOnlyList<Dictionary<string, string>>> groups, TikTokMapOptions options, ImportProductMode mode,
        ImportSource platform, string source, IReadOnlyCollection<string>? supersedeOrderIds = null, CancellationToken ct = default)
    {
        if (!canWrite() || api() is not { } cloud)
            return Fail("Chưa kết nối cloud: chưa nạp được. Có mạng lại thì nạp lại (không mất gì, không trùng).");

        var res = new TikTokImportResult();
        var parcels = new List<JsonObject>();
        foreach (var g in groups)
        {
            if (platform == ImportSource.Shopee)
            {
                var sm = ShopeeMapper.Map(g, new ShopeeMapOptions { OnlyReturns = options.OnlyReturns, Today = options.Today });
                res.RowsSkipped += sm.SkippedSuccess;
                parcels.AddRange(sm.Parcels.Select(ToIncoming));
            }
            else
            {
                var m = TikTokMapper.Map(g, options);
                res.RowsSkipped += m.Skipped;
                res.FailedDelivery += m.FailedDelivery;
                parcels.AddRange(m.Parcels.Select(ToIncoming));
            }
        }

        var kind = platform == ImportSource.Shopee ? "shopee" : "tiktok";
        var modeText = mode == ImportProductMode.Replace ? "replace" : "supplement";
        var signature = "pc-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var sent = 0;
        var superseded = 0;
        try
        {
            for (var i = 0; i < parcels.Count; i += ParcelBatch)
            {
                ct.ThrowIfCancellationRequested();
                var batch = parcels.Skip(i).Take(ParcelBatch).ToList();
                var r = cloud.ImportMappedAsync(source, kind, modeText, batch, Array.Empty<string>(), signature, ct).GetAwaiter().GetResult();
                Add(res, r);
                superseded += r.Superseded;
                sent += batch.Count;
            }
            var ids = supersedeOrderIds?.ToList() ?? new List<string>();
            for (var j = 0; j < ids.Count; j += OrderIdBatch)
            {
                var r = cloud.ImportMappedAsync(source, kind, modeText, Array.Empty<JsonObject>(), ids.Skip(j).Take(OrderIdBatch).ToList(), signature, ct)
                    .GetAwaiter().GetResult();
                Add(res, r);
                superseded += r.Superseded;
            }
        }
        catch (CloudApiException ex)
        {
            return Fail($"Gửi được {sent}/{parcels.Count} kiện lên cloud thì lỗi: {ex.Message}. Nạp lại cả file là an toàn (cloud cập nhật, không nhân đôi).", res);
        }
        return new CloudSinkResult(res, superseded);
    }

    private static void Add(TikTokImportResult res, CloudMappedResult r)
    {
        res.Added += r.Added;
        res.Updated += r.Updated;
        res.EnrichedArchived += r.EnrichedArchived;
        res.SkippedDeleted += r.SkippedDeleted;
        res.Ambiguous.AddRange(r.Ambiguous);
    }

    private static CloudSinkResult Fail(string message, TikTokImportResult? partial = null)
    {
        var r = new TikTokImportResult { Ok = false, Message = message };
        if (partial is not null) { r.Added = partial.Added; r.Updated = partial.Updated; r.Ambiguous.AddRange(partial.Ambiguous); }
        return new CloudSinkResult(r, 0);
    }

    /// <summary>Kiện gửi lên cloud (cùng hình dạng kiện nhập của Google Sheet / điện thoại). KHÔNG gửi ghi chú người mua.</summary>
    public static JsonObject ToIncoming(ImportedParcel p) => new()
    {
        ["shipDate"] = p.ShipDate, ["returnDate"] = p.ReturnDate, ["recvDate"] = p.RecvDate, ["platform"] = p.Platform, ["orderType"] = p.OrderType,
        ["isPartialReturn"] = p.IsPartialReturn, ["platformStatus"] = p.PlatformStatus, ["returnedAt"] = p.ReturnedAt, ["preOrder"] = p.PreOrder,
        ["orderId"] = p.OrderId, ["returnOrderId"] = p.ReturnOrderId, ["forwardTrack"] = p.ForwardTrack, ["returnTrack"] = p.ReturnTrack,
        ["trackingCodes"] = new JsonArray(p.TrackingCodes.Select(c => (JsonNode?)c).ToArray()), ["track"] = p.Track, ["altTrack"] = p.AltTrack,
        ["carrier"] = p.Carrier, ["price"] = p.Price, ["priceHasValue"] = p.PriceHasValue, ["priceNeedsReview"] = p.PriceNeedsReview,
        ["importSelectedPriceMode"] = p.ImportSelectedPriceMode, ["status"] = p.Status, ["note"] = p.Note, ["buyerNote"] = "",
        ["items"] = new JsonArray(p.Items.Select(i => (JsonNode)new JsonObject
        {
            ["product"] = i.Product, ["color"] = i.Color, ["size"] = i.Size, ["qty"] = i.Qty, ["returnedQty"] = i.ReturnedQty,
            ["sellerSku"] = i.SellerSku, ["skuId"] = i.SkuId,
        }).ToArray()),
        ["deadline"] = p.Deadline, ["importPriceBasis"] = p.ImportPriceBasis,
        ["rawDetails"] = p.RawDetails is null ? null : JsonSerializer.SerializeToNode(p.RawDetails),
    };
}
