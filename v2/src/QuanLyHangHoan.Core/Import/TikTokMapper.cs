using System.Text.RegularExpressions;

namespace QuanLyHangHoan.Core.Import;

public sealed class ImportedItem
{
    public string Product { get; set; } = "";
    public string Color { get; set; } = "";
    public string Size { get; set; } = "";
    public int Qty { get; set; } = 1;
    /// <summary>Số lượng đã hoàn của mặt hàng này (xem <c>OrderItem.ReturnedQty</c>); 0 = không rõ/không thuộc yêu cầu hoàn.</summary>
    public int ReturnedQty { get; set; }
    /// <summary>Giá theo từng ô nguồn (unitReturn, discounted, originalUnit, subtotal, saleworkUnit...). null = dòng trống tự thêm.</summary>
    public Dictionary<string, long>? ImportPrices { get; set; }
    public string? SellerSku { get; set; }
    public string? SkuId { get; set; }
    /// <summary>Chỉ nạp Shopee: tiền của dòng theo cột giá đã chọn (importLineAmount); null = không có.</summary>
    public long? ImportLineAmount { get; set; }
}

/// <summary>Một kiện đã ánh xạ từ file nhập, đúng hình dạng đơn mới mà app cũ đẩy ra bảng chính.</summary>
public sealed class ImportedParcel
{
    public string ShipDate { get; set; } = "";
    public string ReturnDate { get; set; } = "";
    public string RecvDate { get; set; } = "";
    public string Platform { get; set; } = "TikTok Shop";
    public string OrderType { get; set; } = "";
    public bool IsPartialReturn { get; set; }
    public string PlatformStatus { get; set; } = "";
    public string ReturnedAt { get; set; } = "";
    public string PreOrder { get; set; } = "Đơn thường";
    public string OrderId { get; set; } = "";
    public string ReturnOrderId { get; set; } = "";
    public string ForwardTrack { get; set; } = "";
    public string ReturnTrack { get; set; } = "";
    public List<string> TrackingCodes { get; set; } = new();
    public string Track { get; set; } = "";
    public string AltTrack { get; set; } = "";
    public string Carrier { get; set; } = "";
    public long Price { get; set; }
    public bool PriceHasValue { get; set; }
    /// <summary>null khi không có giá nào trong file (app cũ để undefined).</summary>
    public bool? PriceNeedsReview { get; set; }
    /// <summary>mode -> slot nguồn; giá trị null = giá cấp ĐƠN (không cộng theo dòng); thiếu khóa = không có giá.</summary>
    public Dictionary<string, string?> ImportModeSlots { get; set; } = new();
    public string ImportSelectedPriceMode { get; set; } = "tong_gia_tri";
    public Dictionary<string, long> PriceModes { get; set; } = new();
    public Dictionary<string, bool> PriceModeFlags { get; set; } = new();
    public string Status { get; set; } = "Chưa về";
    public string Photo { get; set; } = "";
    public string Note { get; set; } = "";
    public string BuyerNote { get; set; } = "";
    public List<ImportedItem> Items { get; set; } = new();
    /// <summary>Chỉ nạp Shopee: hạn phản hồi của người bán, cơ sở giá ("order"/"line"), chi tiết thô từ file (rawDetails).</summary>
    public string Deadline { get; set; } = "";
    public string ImportPriceBasis { get; set; } = "";
    public Dictionary<string, string>? RawDetails { get; set; }

    public ImportedParcel Clone() => new()
    {
        Deadline = Deadline, ImportPriceBasis = ImportPriceBasis, RawDetails = RawDetails is null ? null : new(RawDetails),
        ShipDate = ShipDate, ReturnDate = ReturnDate, RecvDate = RecvDate, Platform = Platform, OrderType = OrderType, IsPartialReturn = IsPartialReturn,
        PlatformStatus = PlatformStatus, ReturnedAt = ReturnedAt, PreOrder = PreOrder, OrderId = OrderId, ReturnOrderId = ReturnOrderId,
        ForwardTrack = ForwardTrack, ReturnTrack = ReturnTrack, TrackingCodes = new(TrackingCodes), Track = Track, AltTrack = AltTrack,
        Carrier = Carrier, Price = Price, PriceHasValue = PriceHasValue, PriceNeedsReview = PriceNeedsReview,
        ImportModeSlots = new(ImportModeSlots), ImportSelectedPriceMode = ImportSelectedPriceMode, PriceModes = new(PriceModes),
        PriceModeFlags = new(PriceModeFlags), Status = Status, Photo = Photo, Note = Note, BuyerNote = BuyerNote,
        Items = Items.Select(i => new ImportedItem
        {
            Product = i.Product, Color = i.Color, Size = i.Size, Qty = i.Qty, ReturnedQty = i.ReturnedQty, SellerSku = i.SellerSku, SkuId = i.SkuId,
            ImportPrices = i.ImportPrices is null ? null : new(i.ImportPrices), ImportLineAmount = i.ImportLineAmount,
        }).ToList(),
    };
}

public sealed class TikTokMapOptions
{
    /// <summary>chkFilterOnlyReturns (mặc định bật): bỏ dòng không phải đơn hoàn/hủy/thất bại.</summary>
    public bool OnlyReturns { get; set; } = true;
    public bool SkipUnshippedCancel { get; set; }
    /// <summary>tong_gia_tri | sau_chiet_khau | gia_salework | doanh_thu_uoc_tinh</summary>
    public string PriceMode { get; set; } = "tong_gia_tri";
    /// <summary>"Hôm nay" (đồng hồ cố định khi test): làm ngày hoàn mặc định và năm cho ngày thiếu năm.</summary>
    public DateTime Today { get; set; } = DateTime.Now;
}

public sealed class TikTokMapResult
{
    public List<ImportedParcel> Parcels { get; } = new();
    public int Skipped { get; set; }
    public int FailedDelivery { get; set; }
    public int PremiumItems { get; set; }
    public int WithTrack { get; set; }
    public int EmptyTrackCancel { get; set; }
    public int MultiItemAppended { get; set; }
}

/// <summary>
/// Ánh xạ bảng file TikTok Seller Center / Salework (đơn hoàn, hủy, giao không thành công, thất thoát) thành kiện.
/// Chuyển từ ingestTikTokOnlyRecords của app cũ; kết quả được đối chiếu với đáp án sinh từ mã JS cũ (Golden/tiktok.golden.json).
/// Dòng nhập có thể mang khóa đặc biệt "__sourceFileId" (ranh giới giữa các file: dòng sản phẩm phụ không dính sang file khác).
/// </summary>
public static class TikTokMapper
{
    private static readonly string[] TikTokFileKeys =
        { "returnorderid", "returnlogisticstrackingid", "returnreason", "cancelationreturntype", "skuquantityofreturn", "cancelreason", "rtstime", "ordersubstatus" };

    /// <summary>isTikTokExportRows: 5 dòng đầu có cột đặc trưng của file TikTok.</summary>
    public static bool IsTikTokExport(IReadOnlyList<Dictionary<string, string>> rows)
    {
        var keys = new HashSet<string>();
        foreach (var r in rows.Take(5)) foreach (var k in r.Keys) keys.Add(LegacyText.Key(k));
        return TikTokFileKeys.Any(keys.Contains);
    }

    private sealed class Group
    {
        public ImportedParcel P { get; } = new();
        public Dictionary<string, long> Money { get; } = new();
    }

    public static TikTokMapResult Map(IReadOnlyList<Dictionary<string, string>> rawRows, TikTokMapOptions? options = null)
    {
        options ??= new TikTokMapOptions();
        var result = new TikTokMapResult();
        if (rawRows.Count == 0) return result;
        var year = options.Today.Year;

        // Tập tên cột từ 50 dòng đầu.
        var keys = new List<string>();
        foreach (var r in rawRows.Take(50)) foreach (var k in r.Keys) if (!keys.Contains(k)) keys.Add(k);

        string? Find(string[] exact, string[]? includes = null, string[]? excludes = null)
        {
            foreach (var ex in exact)
            {
                var exNorm = LegacyText.Key(ex);
                var f = keys.FirstOrDefault(k => LegacyText.Key(k) == exNorm);
                if (f is not null) return f;
            }
            foreach (var p in includes ?? Array.Empty<string>())
            {
                var f = keys.FirstOrDefault(k =>
                {
                    var norm = LegacyText.Key(k);
                    if ((excludes ?? Array.Empty<string>()).Any(e => norm.Contains(e.ToLowerInvariant()))) return false;
                    return norm.Contains(p.ToLowerInvariant());
                });
                if (f is not null) return f;
            }
            return null;
        }

        var returnOrderIdKey = Find(new[] { "Return Order ID", "Mã yêu cầu trả hàng", "Return ID" }, new[] { "returnorderid", "returnid", "mayeucau" });
        var returnLogisticsTrackKey = Find(new[] { "Return Logistics Tracking ID", "Mã vận đơn trả hàng", "Return Tracking ID", "Return Tracking" },
            new[] { "returnlogisticstrackingid", "mvdtrahang", "returntracking" });
        var saleworkReturnedKey = returnLogisticsTrackKey is not null ? null : Find(new[] { "Ngày trả hàng thành công" });
        var saleworkReturnTrackKey = returnLogisticsTrackKey is not null ? null : Find(new[] { "Mã trả hàng", "Mã vận đơn hoàn", "Mã vận đơn hoàn về" });
        var saleworkKindKey = Find(new[] { "Loại đơn hoàn" }, new[] { "loaidonhoan" });
        var returnTypeKey = Find(new[] { "Return Type", "Cancelation/Return Type", "Loại yêu cầu", "Loại hủy" }, new[] { "returntype", "cancelationreturntype", "canceltype", "loaiyeucau" });
        var returnReasonKey = Find(new[] { "Return Reason", "Cancel Reason", "Lý do hoàn", "Lý do trả hàng", "Lý do hủy" }, new[] { "returnreason", "cancelreason", "lydohoan", "lydotrahang", "lydohuy" });
        var returnStatusKey = Find(new[] { "Return Status", "Trạng thái hoàn" }, new[] { "returnstatus", "trangthaihoan" });
        var returnSubStatusKey = Find(new[] { "Return Sub Status", "Trạng thái chi tiết" }, new[] { "returnsubstatus" });
        var timeRequestedKey = Find(new[] { "Time Requested", "Thời gian khiếu nại", "Thời gian hoàn", "Thời gian yêu cầu" }, new[] { "timerequested", "thoigiankhieunai", "thoigianhoan", "ngayhoan", "ngayyeucau" });
        var refundTimeKey = Find(new[] { "Refund Time", "Thời gian hoàn tiền" }, new[] { "refundtime", "thoigianhoantien" });
        var returnUnitPriceKey = Find(new[] { "Return unit price", "Đơn giá hoàn" }, new[] { "returnunitprice", "dongiahoan" });

        var cancelledTimeKey = Find(new[] { "Cancelled Time", "Thời gian hủy" }, new[] { "cancelledtime", "thoigianhuy" });
        var orderSubstatusKey = Find(new[] { "Order Substatus", "Trạng thái phụ" }, new[] { "ordersubstatus", "substatus" });
        var skuQuantityOfReturnKey = Find(new[] { "Sku Quantity of return", "Return Quantity", "Số lượng hoàn" }, new[] { "skuquantityofreturn", "returnquantity", "soluonghoan" });
        var shippedTimeKey = Find(new[] { "Shipped Time", "Thời gian gửi" }, new[] { "shippedtime", "thoigiangui" });
        var rtsTimeKey = Find(new[] { "RTS Time", "Thời gian đóng" }, new[] { "rtstime", "thoigiandong" });
        var buyerMessageKey = Find(new[] { "Buyer Note", "Buyer Message", "Ghi chú của khách", "Ghi chú" }, new[] { "buyernote", "buyermessage", "ghichucuakhach", "ghichukhach", "ghichu" });

        var trackKey = returnLogisticsTrackKey ?? Find(
            new[] { "Mã vận đơn", "Tracking ID", "Mã vận đơn trả hàng", "Mã vận đơn giao hàng", "Return Logistics Tracking ID" },
            new[] { "mavandon", "trackingid", "trackingnumber", "tracking", "mvd", "sovandon", "mabuugui" });
        var forwardTrackKey = Find(new[] { "Tracking ID", "Mã vận đơn giao hàng", "Mã vận đơn" }, new[] { "forwardtracking", "mavandongiaohang" }, new[] { "return", "trahang" });
        var orderedQtyKey = Find(new[] { "Quantity", "Số lượng" }, new[] { "orderedquantity" }, new[] { "return", "hoan" });
        var orderIdKey = Find(new[] { "Order ID", "Mã đơn hàng", "ID đơn hàng" }, new[] { "orderid", "madonhang", "iddonhang", "madon" }, new[] { "masokhieunai", "makhieunai" });
        var carrierKey = Find(new[] { "Shipping Provider Name", "Đơn vị vận chuyển", "Nhà vận chuyển" },
            new[] { "shippingprovidername", "donvivanchuyen", "shippingprovider", "nhavanchuyen", "dvvc", "carrier" });
        var productKey = Find(new[] { "Product Name", "Tên sản phẩm trên sàn", "Tên sản phẩm" }, new[] { "productname", "tensanphamtrensan", "tensanpham", "tensp" }, new[] { "masanpham", "id", "masku", "skuid" });
        var varKey = Find(new[] { "Variation", "SKU Name", "Tên phân loại trên sàn", "Tên phân loại" },
            new[] { "variation", "skuname", "tenphanloaitrensan", "tenphanloai", "phanloai", "tenbienthe" }, new[] { "maphanloai", "masanpham", "masku", "skuid" });
        var sellerSkuKey = Find(new[] { "Seller SKU", "Mã sản phẩm Salework", "SKU phân loại" }, new[] { "sellersku", "masanphamsalework", "skuphanloai" });
        var skuIdKey = Find(new[] { "SKU ID", "Sku ID", "Mã SKU" }, new[] { "skuid", "masku" });
        var qtyKey = Find(new[] { "Return Quantity", "Quantity", "Số lượng", "Số lượng hoàn" }, new[] { "returnquantity", "quantity", "soluong", "qty" }, new[] { "tongsoluong" });

        var priceTongGiaTriKey = Find(new[] { "Order Amount", "Order Total", "Tổng giá trị" }, new[] { "orderamount", "ordertotal", "tonggiatri" });
        var priceSauChietKhauKey = Find(new[] { "SKU Subtotal After Discount", "Tổng tiền hàng sau chiết khấu" }, new[] { "skusubtotalafterdiscount", "tongtienhangsauchietkhau" });
        var priceGiaSanTmdtKey = Find(new[] { "SKU Unit Original Price", "Giá sàn TMĐT", "Đơn giá" }, new[] { "skuunitoriginalprice", "giasantmdt" });
        var priceTongTienHangKey = Find(new[] { "Tổng tiền hàng", "SKU Subtotal Before Discount" }, new[] { "tongtienhang" }, new[] { "sauchietkhau" });
        var priceGiaSaleworkKey = Find(new[] { "Giá Salework" }, new[] { "giasalework" });
        var priceDoanhThuKey = Find(new[] { "Doanh thu ước tính" }, new[] { "doanhthuuoctinh" });

        var shipDateKeys = new[]
        {
            shippedTimeKey, rtsTimeKey, timeRequestedKey,
            Find(new[] { "Thời gian xử lý" }, new[] { "thoigianxuly" }),
            Find(new[] { "Ngày tạo đơn", "Created Time" }, new[] { "ngaytaodon", "createdtime" }),
        }.Where(k => k is not null).Cast<string>().ToList();
        var returnDateKeys = new[]
        {
            timeRequestedKey, cancelledTimeKey, refundTimeKey,
            Find(new[] { "Thời gian đóng" }, new[] { "thoigiandong" }),
            Find(new[] { "Ngày tạo đơn" }, new[] { "ngaytaodon" }),
        }.Where(k => k is not null).Cast<string>().ToList();

        var statusKey = returnStatusKey ?? Find(new[] { "Return Status", "Trạng thái đơn hàng", "Order Status" }, new[] { "returnstatus", "trangthaidonhang", "orderstatus", "trangthai" });
        var noteKey = returnReasonKey ?? Find(new[] { "Return Reason", "Cancel Reason", "Lý do hủy", "Lý do" }, new[] { "returnreason", "cancelreason", "lydohuy", "lydo" });
        var preOrderKey = Find(new[] { "Hàng đặt trước", "Pre-order", "Preorder", "Normal or Pre-order", "Loại đơn hàng" },
            new[] { "hangdattruoc", "dattruoc", "preorder", "normalorpreorder", "loaidonhang" });

        var isTikTokReturnRefundFile = returnOrderIdKey is not null || returnLogisticsTrackKey is not null;

        bool IsSkuSubtotal(string? key) => Regex.IsMatch(Regex.Replace(LegacyText.RemoveTones(key ?? ""), "[^a-z0-9]", ""), "skusubtotal");

        string Get(Dictionary<string, string> row, string? key) => key is not null && row.TryGetValue(key, out var v) ? v ?? "" : "";
        bool Has(Dictionary<string, string> row, string? key) => key is not null && row.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v);

        var groups = new Dictionary<string, Group>();
        var groupOrder = new List<string>();
        var zeroReturned = new HashSet<string>();
        string lastGroupKey = "";
        var lastSkippedRow = false;
        string? lastSourceFileId = null;
        var hasLastSource = false;

        foreach (var row in rawRows)
        {
            var sourceId = row.TryGetValue("__sourceFileId", out var sf) ? sf : null;
            if (!hasLastSource || sourceId != lastSourceFileId)
            {
                lastGroupKey = ""; lastSkippedRow = false; lastSourceFileId = sourceId; hasLastSource = true;
            }

            // 1. mã vận đơn
            var rawTrack = "";
            if (trackKey is not null && Has(row, trackKey))
                rawTrack = LegacyText.CleanTrackingCode(LegacyText.NormalizeScientificOrderId(Get(row, trackKey)));
            if (rawTrack.Length == 0)
                foreach (var k in keys)
                {
                    var nk = Regex.Replace(LegacyText.RemoveTones(k), "[^a-z0-9]", "");
                    if (nk.Contains("mavandon") || nk.Contains("tracking"))
                    {
                        var cand = LegacyText.CleanTrackingCode(LegacyText.NormalizeScientificOrderId(Get(row, k)));
                        if (cand.Length > 0) { rawTrack = cand; break; }
                    }
                }

            // 2. mã đơn / mã yêu cầu hoàn
            var rawOrderId = orderIdKey is not null ? LegacyText.NormalizeScientificOrderId(Get(row, orderIdKey)) : "";
            var rawReturnOrderId = returnOrderIdKey is not null ? LegacyText.NormalizeScientificOrderId(Get(row, returnOrderIdKey)) : "";
            if (rawOrderId.Length == 0 && rawReturnOrderId.Length > 0) rawOrderId = rawReturnOrderId;

            var rawProd = Get(row, productKey).Trim();
            var rawVar = Get(row, varKey).Trim();
            var rawSellerSku = Get(row, sellerSkuKey).Trim();

            var isContinuation = false;
            if (rawTrack.Length == 0 && rawOrderId.Length == 0 && rawReturnOrderId.Length == 0)
            {
                if (lastGroupKey.Length > 0 && !lastSkippedRow && groups.ContainsKey(lastGroupKey) && (rawProd.Length > 0 || rawVar.Length > 0 || rawSellerSku.Length > 0))
                    isContinuation = true;
                else continue;
            }

            var rawStatusText = Get(row, statusKey).Trim();
            var rawSubStatusText = returnSubStatusKey is not null && Get(row, returnSubStatusKey).Length > 0 ? Get(row, returnSubStatusKey).Trim()
                : (orderSubstatusKey is not null && Get(row, orderSubstatusKey).Length > 0 ? Get(row, orderSubstatusKey).Trim() : "");
            var rawCancelReason = Get(row, noteKey).Trim();
            var rawCancelType = Get(row, returnTypeKey).Trim();
            var hasReturnQty = skuQuantityOfReturnKey is not null && Has(row, skuQuantityOfReturnKey);
            var rawRetQty = hasReturnQty ? LegacyText.ParseQuantity(Get(row, skuQuantityOfReturnKey)) : 0;

            if (hasReturnQty && rawRetQty == 0)
            {
                var skippedKey = isContinuation ? lastGroupKey : ParcelKey(rawOrderId, rawTrack, rawReturnOrderId, "", "");
                zeroReturned.Add(skippedKey);
                lastGroupKey = skippedKey;
                if (groups.TryGetValue(skippedKey, out var zg)) zg.P.IsPartialReturn = true;
                lastSkippedRow = false;
                continue;
            }
            var orderedQty = orderedQtyKey is not null ? LegacyText.ParseQuantity(Get(row, orderedQtyKey)) : 0;

            var stNorm = LegacyText.RemoveTones(rawStatusText);
            var subNorm = LegacyText.RemoveTones(rawSubStatusText);
            var reasonNorm = LegacyText.RemoveTones(rawCancelReason);
            var typeNorm = LegacyText.RemoveTones(rawCancelType);

            const RegexOptions I = RegexOptions.IgnoreCase;
            var isCancelledWord = Regex.IsMatch(stNorm, @"\bhuy\b", I) || Regex.IsMatch(stNorm, @"\bcancel(?:led|ed)?\b", I)
                                  || Regex.IsMatch(subNorm, @"\bhuy\b", I) || Regex.IsMatch(subNorm, @"\bcancel(?:led|ed)?\b", I) || typeNorm.Contains("cancel");
            var isFailedDelivery = reasonNorm.Contains("giao goi hang that bai") || reasonNorm.Contains("that bai") || reasonNorm.Contains("fail");
            var returnWords = new Regex(@"\b(?:hoan|tra|return(?:ed|ing)?|refund(?:ed|ing)?)\b", I);
            var hasReturnIdentity = rawReturnOrderId.Length > 0 || (returnLogisticsTrackKey is not null && Get(row, returnLogisticsTrackKey).Trim().Length > 0);
            // v2 (khác app cũ, theo yêu cầu chủ dự án): file Salework có cột "Loại đơn hoàn" = "Trả hàng hoàn tiền - Một phần", trạng thái "THÀNH CÔNG".
            // App cũ bỏ các dòng này vì cột trạng thái không có chữ hoàn/trả; v2 nhận và gắn cờ hoàn một phần. Chỉ áp dụng khi chưa là THẤT THU.
            var saleworkKindNorm = saleworkKindKey is not null ? LegacyText.RemoveTones(Get(row, saleworkKindKey)) : "";
            var isSaleworkPartial = saleworkKindNorm.Contains("mot phan") && !LegacyText.RemoveTones(rawStatusText).Contains("that thu");
            var isReturnOrRefund = returnWords.IsMatch(stNorm) || returnWords.IsMatch(subNorm) || rawRetQty > 0 || hasReturnIdentity || isSaleworkPartial;
            var isReturnOrCancel = isFailedDelivery || isCancelledWord || isReturnOrRefund || stNorm.Contains("that thu");

            if (!isContinuation)
            {
                if ((options.OnlyReturns && !isReturnOrCancel) || (options.SkipUnshippedCancel && rawTrack.Length == 0 && isCancelledWord))
                {
                    result.Skipped++;
                    lastSkippedRow = true;
                    continue;
                }
                lastSkippedRow = false;
            }

            var groupKey = isContinuation ? lastGroupKey : ParcelKey(rawOrderId, rawTrack, rawReturnOrderId, "", "");
            if (!isContinuation) lastGroupKey = groupKey;

            var rawQty = 1;
            if (qtyKey is not null && Has(row, qtyKey))
            {
                var q = LegacyText.ParseQuantity(Get(row, qtyKey));
                if (q > 0) rawQty = q;
            }
            if (hasReturnQty && rawRetQty > 0) rawQty = rawRetQty;

            var rawPreOrder = "Đơn thường";
            if (preOrderKey is not null && Has(row, preOrderKey))
            {
                var po = LegacyText.RemoveTones(Get(row, preOrderKey)).Trim();
                if (po.Contains("dat truoc") || po.Contains("preorder") || po.Contains("pre-order") || po is "co" or "yes" or "true") rawPreOrder = "Hàng đặt trước";
            }

            var valUnit = returnUnitPriceKey is not null ? LegacyText.ParseMoney(Get(row, returnUnitPriceKey)) : 0;
            var valZ = priceTongGiaTriKey is not null ? LegacyText.ParseMoney(Get(row, priceTongGiaTriKey)) : 0;
            var valAM = priceSauChietKhauKey is not null ? LegacyText.ParseMoney(Get(row, priceSauChietKhauKey)) : 0;
            var valK = priceGiaSanTmdtKey is not null ? LegacyText.ParseMoney(Get(row, priceGiaSanTmdtKey)) : 0;
            var valAO = priceTongTienHangKey is not null ? LegacyText.ParseMoney(Get(row, priceTongTienHangKey)) : 0;
            var valG = priceGiaSaleworkKey is not null ? LegacyText.ParseMoney(Get(row, priceGiaSaleworkKey)) : 0;
            var valL = priceDoanhThuKey is not null ? LegacyText.ParseMoney(Get(row, priceDoanhThuKey)) : 0;

            var rawShipDate = "";
            foreach (var dk in shipDateKeys)
                if (Get(row, dk).Length > 0) { var ex = LegacyText.ExtractDate(Get(row, dk), year); if (ex.Length > 0) { rawShipDate = ex; break; } }
            var rawReturnDate = "";
            foreach (var rk in returnDateKeys)
                if (Get(row, rk).Length > 0) { var ex = LegacyText.ExtractDate(Get(row, rk), year); if (ex.Length > 0) { rawReturnDate = ex; break; } }
            if (rawReturnDate.Length == 0 && rawShipDate.Length > 0) rawReturnDate = rawShipDate;
            if (rawShipDate.Length == 0 && rawReturnDate.Length > 0) rawShipDate = rawReturnDate;

            var rawNote = "";
            if (rawCancelReason.Length > 0) rawNote = TranslateReason(rawCancelReason);
            else if (rawCancelType.Length > 0) rawNote = rawCancelType;

            var buyerComment = buyerMessageKey is not null && Get(row, buyerMessageKey).Length > 0 ? Get(row, buyerMessageKey).Trim() : LegacyText.ExtractBuyerNote(row);
            if (LegacyText.IsNoneNote(buyerComment)) buyerComment = "";

            var rawPlatStatus = "";
            var rawReturnedAt = "";
            if (isTikTokReturnRefundFile) rawPlatStatus = TranslateReturnStatus(rawStatusText, rawSubStatusText);
            else if (saleworkReturnTrackKey is not null)
            {
                rawPlatStatus = saleworkReturnedKey is not null && Get(row, saleworkReturnedKey).Trim().Length > 0 ? "Đã về"
                    : (Get(row, saleworkReturnTrackKey).Trim().Length > 0 ? "Đang về" : "Chờ khách gửi hàng");
                rawReturnedAt = saleworkReturnedKey is not null ? Get(row, saleworkReturnedKey).Trim() : "";
            }
            else if (rawCancelReason.Length > 0) rawPlatStatus = rawCancelReason;
            else rawPlatStatus = rawStatusText + (rawSubStatusText.Length > 0 ? " - " + rawSubStatusText : "");

            string orderType;
            if (isFailedDelivery) { orderType = "Giao không thành công"; result.FailedDelivery++; }
            else if (isReturnOrRefund)
                orderType = typeNorm.Contains("refund only") || typeNorm.Contains("chi hoan") ? "Chỉ hoàn tiền" : "Trả hàng/Hoàn tiền";
            else if (isCancelledWord) orderType = "Đơn hủy";
            else if (stNorm.Contains("that thu"))
            {
                orderType = "THẤT THOÁT";
                rawNote = rawNote.Length > 0 ? $"THẤT THOÁT | {rawNote}" : "THẤT THOÁT (Trạng thái Salework)";
            }
            else orderType = "Đang chuyển hoàn / Vận chuyển";

            var pv = LegacyText.ParseProductAndVariation(rawProd, rawVar, rawSellerSku);
            if (Regex.IsMatch(pv.Product, "premium", I) || Regex.IsMatch(pv.Size, "premium", I)) result.PremiumItems++;

            var carrierFromExcel = Get(row, carrierKey).Trim();
            var carrier = LegacyText.DetectTikTokCarrierFromCode(rawTrack);
            if (carrier.Length == 0) carrier = LegacyText.DetectCarrierByTrackingCode(rawTrack) ?? "";
            if (carrier.Length == 0) carrier = carrierFromExcel;
            if (carrier.Length == 0) carrier = rawTrack.Length > 0 ? "J&T Express" : "Chờ bưu cục tiếp nhận";

            var isPartial = (hasReturnQty && rawRetQty > 0 && orderedQty > rawRetQty) || typeNorm.Contains("partial") || reasonNorm.Contains("mot phan") || isSaleworkPartial;

            var existedBefore = groups.ContainsKey(groupKey);
            if (!existedBefore)
            {
                var ng = new Group();
                ng.P.OrderType = orderType;
                ng.P.IsPartialReturn = isPartial;
                ng.P.PreOrder = rawPreOrder;
                ng.P.OrderId = rawOrderId;
                ng.P.ReturnOrderId = rawReturnOrderId;
                ng.P.Track = rawTrack;
                ng.P.Carrier = carrier;
                ng.P.ShipDate = rawShipDate;
                ng.P.ReturnDate = rawReturnDate;
                ng.P.PlatformStatus = rawPlatStatus;
                ng.P.ReturnedAt = rawReturnedAt;
                ng.P.Note = rawNote;
                ng.P.BuyerNote = buyerComment;
                groups[groupKey] = ng;
                groupOrder.Add(groupKey);
                if (rawTrack.Length > 0) result.WithTrack++; else result.EmptyTrackCancel++;
            }
            else result.MultiItemAppended++;

            var entry = groups[groupKey];
            var p = entry.P;
            var returnTrackSourceKey = returnLogisticsTrackKey ?? saleworkReturnTrackKey;
            var returnTrack = returnTrackSourceKey is not null ? LegacyText.CleanTrackingCode(LegacyText.NormalizeScientificOrderId(Get(row, returnTrackSourceKey))) : "";
            if (returnTrack.Length > 0 && p.ReturnTrack.Length == 0) p.ReturnTrack = returnTrack;
            var forwardTrack = forwardTrackKey is not null ? LegacyText.CleanTrackingCode(LegacyText.NormalizeScientificOrderId(Get(row, forwardTrackKey))) : "";
            if (forwardTrack.Length > 0 && p.ForwardTrack.Length == 0) p.ForwardTrack = forwardTrack;
            p.TrackingCodes = TrackingCodesOf(p).Concat(new[] { rawTrack, forwardTrack, returnTrack }).Concat(ParseImportedTrackingCodes(row))
                .Where(c => c.Length > 0).Distinct().ToList();

            var lineMoney = new Dictionary<string, long>();
            void Collect(string slot, string? key, long value, bool sum)
            {
                if (key is null || !Has(row, key)) return;
                if (sum) lineMoney[slot] = value;
                if (sum) entry.Money[slot] = (entry.Money.TryGetValue(slot, out var cur) ? cur : 0) + value;
                else if (!entry.Money.ContainsKey(slot)) entry.Money[slot] = value;
            }
            Collect("unitReturn", returnUnitPriceKey, valUnit * rawQty, true);
            Collect("total", priceTongGiaTriKey, valZ, false);
            Collect("discounted", priceSauChietKhauKey, valAM, IsSkuSubtotal(priceSauChietKhauKey));
            Collect("originalUnit", priceGiaSanTmdtKey, valK * rawQty, true);
            Collect("subtotal", priceTongTienHangKey, valAO, IsSkuSubtotal(priceTongTienHangKey));
            Collect("saleworkUnit", priceGiaSaleworkKey, valG * rawQty, true);
            Collect("revenue", priceDoanhThuKey, valL, false);
            if (isPartial) p.IsPartialReturn = true;

            if (rawPreOrder == "Hàng đặt trước") p.PreOrder = rawPreOrder;
            if (rawTrack.Length > 0 && p.Track.Length == 0)
            {
                p.Track = rawTrack;
                result.WithTrack++;
                if (result.EmptyTrackCancel > 0) result.EmptyTrackCancel--;
            }
            if (rawOrderId.Length > 0 && p.OrderId.Length == 0) p.OrderId = rawOrderId;
            if (rawReturnOrderId.Length > 0 && p.ReturnOrderId.Length == 0) p.ReturnOrderId = rawReturnOrderId;
            if (rawShipDate.Length > 0 && p.ShipDate.Length == 0) p.ShipDate = rawShipDate;
            if (rawReturnDate.Length > 0 && p.ReturnDate.Length == 0) p.ReturnDate = rawReturnDate;
            if (rawPlatStatus.Length > 0 && p.PlatformStatus.Length == 0) p.PlatformStatus = rawPlatStatus;
            if (rawReturnedAt.Length > 0 && p.ReturnedAt.Length == 0) p.ReturnedAt = rawReturnedAt;
            if (rawNote.Length > 0 && p.Note.Length == 0) p.Note = rawNote;
            if (buyerComment.Length > 0 && p.BuyerNote.Length == 0) p.BuyerNote = buyerComment;
            if (carrier.Length > 0 && p.Carrier == "Chờ bưu cục tiếp nhận") p.Carrier = carrier;

            if (pv.Product.Length > 0 || pv.Color.Length > 0 || pv.Size.Length > 0)
                p.Items.Add(new ImportedItem
                {
                    Product = pv.Product, Color = pv.Color, Size = pv.Size, Qty = rawQty, ImportPrices = lineMoney,
                    // Dòng thuộc một YÊU CẦU HOÀN (có mã yêu cầu hoàn hoặc mã vận đơn hoàn) thì số lượng là số đã hoàn; dòng chỉ có mã giao thì chưa biết.
                    ReturnedQty = (p.ReturnOrderId.Length > 0 || p.ReturnTrack.Length > 0) ? rawQty : 0,
                    SellerSku = rawSellerSku.Length > 0 ? rawSellerSku : null,
                    SkuId = skuIdKey is not null && Has(row, skuIdKey) ? Get(row, skuIdKey).Trim() : null,
                });
        }

        foreach (var gk in groupOrder)
        {
            var g = groups[gk];
            var p = g.P;
            var money = g.Money;
            if (zeroReturned.Contains(ParcelKey(p.OrderId, p.Track, p.ReturnOrderId, p.ReturnTrack, p.ForwardTrack))) p.IsPartialReturn = true;

            long? M(string slot) => money.TryGetValue(slot, out var v) ? v : null;
            p.PriceHasValue = money.Count > 0;
            var priceVal = M("unitReturn") ?? M("discounted") ?? M("total") ?? M("originalUnit") ?? M("subtotal") ?? 0;
            p.PriceModes = new Dictionary<string, long>
            {
                ["tong_gia_tri"] = priceVal,
                ["sau_chiet_khau"] = M("discounted") ?? priceVal,
                ["gia_salework"] = M("subtotal") ?? M("saleworkUnit") ?? priceVal,
                ["doanh_thu_uoc_tinh"] = M("revenue") ?? priceVal,
            };
            p.PriceModeFlags = new Dictionary<string, bool>
            {
                ["hasOrderLevelZ"] = M("unitReturn") is null && M("total") is not null,
                ["hasOrderLevelAM"] = M("discounted") is not null && !IsSkuSubtotal(priceSauChietKhauKey),
                ["hasOrderLevelAO"] = M("subtotal") is not null && !IsSkuSubtotal(priceTongTienHangKey),
                ["hasOrderLevelL"] = M("revenue") is not null,
            };
            p.Price = p.PriceModes.TryGetValue(options.PriceMode, out var sel) ? sel : priceVal;

            var baseSlots = new[] { "unitReturn", "discounted", "total", "originalUnit", "subtotal" };
            var slotsByMode = new Dictionary<string, string[]>
            {
                ["tong_gia_tri"] = baseSlots,
                ["sau_chiet_khau"] = new[] { "discounted" }.Concat(baseSlots).ToArray(),
                ["gia_salework"] = new[] { "subtotal", "saleworkUnit" }.Concat(baseSlots).ToArray(),
                ["doanh_thu_uoc_tinh"] = new[] { "revenue" }.Concat(baseSlots).ToArray(),
            };
            p.ImportModeSlots = new Dictionary<string, string?>();
            foreach (var (mode, slots) in slotsByMode)
            {
                var slot = slots.FirstOrDefault(s => money.ContainsKey(s));
                var orderLevel = slot is "total" or "revenue"
                                 || (slot == "discounted" && !IsSkuSubtotal(priceSauChietKhauKey))
                                 || (slot == "subtotal" && !IsSkuSubtotal(priceTongTienHangKey));
                if (slot is null) continue; // không có giá nào: app cũ để undefined (không có khóa)
                p.ImportModeSlots[mode] = orderLevel ? null : slot;
            }
            p.ImportSelectedPriceMode = options.PriceMode;
            if (p.Items.Count == 0) p.Items.Add(new ImportedItem { Qty = 1 });

            // registerImportedParcel: có giá thì không cần đối chiếu giá.
            if (p.PriceHasValue) p.PriceNeedsReview = false;
            result.Parcels.Add(p);
        }
        return result;
    }

    /// <summary>
    /// Hình dạng DÒNG MỚI đẩy ra bảng chính: chỉ lúc tạo dòng mới app cũ mới điền mặc định (ngày hoàn = ngày gửi hoặc hôm nay,
    /// trạng thái sàn "Chờ xử lý", hãng "J&amp;T Express", mã đơn = mã yêu cầu hoàn, "Không cần nhận" cho đơn hủy chưa có mã).
    /// Khi CẬP NHẬT đơn có sẵn thì dùng dữ liệu thô (không có các mặc định này).
    /// </summary>
    public static ImportedParcel ForNewRow(ImportedParcel raw, DateTime today)
    {
        var p = raw.Clone();
        var cleanTrack = LegacyText.CleanTrackingCode(p.Track);
        if (p.ReturnDate.Length == 0) p.ReturnDate = p.ShipDate.Length > 0 ? p.ShipDate : today.ToString("dd/MM/yyyy");
        if (p.PlatformStatus.Length == 0) p.PlatformStatus = "Chờ xử lý";
        if (p.OrderId.Length == 0) p.OrderId = p.ReturnOrderId;
        p.TrackingCodes = TrackingCodesOf(p).Distinct().ToList();
        if (cleanTrack.Length > 0) p.Track = cleanTrack;
        if (p.Carrier.Length == 0) p.Carrier = "J&T Express";
        p.Status = p.OrderType == "Đơn hủy" && cleanTrack.Length == 0 && p.Track.Length == 0 ? "Không cần nhận" : "Chưa về";
        return p;
    }

    /// <summary>getOrderTrackingCodes cho kiện nhập.</summary>
    public static List<string> TrackingCodesOf(ImportedParcel p) =>
        new[] { p.Track, p.AltTrack, p.ForwardTrack, p.ReturnTrack }.Concat(p.TrackingCodes)
            .Select(c => LegacyText.CleanTrackingCode(c)).Where(c => c.Length > 0).Distinct().ToList();

    /// <summary>parseImportedTrackingCodes: cột "Tracking Codes" / "Mã vận đơn lịch sử" (JSON hoặc phân tách ; , |).</summary>
    private static IEnumerable<string> ParseImportedTrackingCodes(Dictionary<string, string> row)
    {
        var raw = row.TryGetValue("Tracking Codes", out var a) && a.Length > 0 ? a : row.TryGetValue("Mã vận đơn lịch sử", out var b) ? b : "";
        if (raw.Length == 0) return Array.Empty<string>();
        IEnumerable<string> codes;
        try { codes = System.Text.Json.JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>(); }
        catch (System.Text.Json.JsonException) { codes = Regex.Split(raw, @"[;,|]"); }
        return codes.Select(c => LegacyText.CleanTrackingCode(c ?? "")).Where(c => c.Length > 0).ToList();
    }

    /// <summary>getReturnParcelKey</summary>
    public static string ParcelKey(string orderId, string track, string returnOrderId, string returnTrack, string forwardTrack)
    {
        var code = LegacyText.CleanTrackingCode(returnTrack.Length > 0 ? returnTrack : track.Length > 0 ? track : forwardTrack);
        var id = returnOrderId.Trim().ToUpperInvariant();
        return code.Length > 0 ? "TRACK:" + code + (id.Length > 0 ? ":RETURN:" + id : "")
            : id.Length > 0 ? "RETURN:" + id
            : "ORDER:" + orderId.Trim().ToUpperInvariant();
    }

    private static string TranslateReason(string en)
    {
        var map = new Dictionary<string, string>
        {
            ["change of mind"] = "Đổi ý / Không còn nhu cầu",
            ["ordered wrong size"] = "Đặt nhầm size",
            ["item too big or too small"] = "Mặc rộng / chật không vừa",
            ["poor quality"] = "Chất lượng kém",
            ["product is defective or doesn't work"] = "Hàng lỗi / Không sử dụng được",
            ["product doesn't match description"] = "Hàng không đúng mô tả",
            ["missing parts or accessories"] = "Thiếu hàng / phụ kiện",
            ["package damaged"] = "Bưu kiện hư hỏng",
            ["suspected counterfeit"] = "Nghi ngờ hàng giả",
            ["delayed delivery"] = "Giao hàng chậm trễ",
            ["wrong product sent"] = "Shop gửi sai sản phẩm",
            ["received the wrong item"] = "Shop gửi sai sản phẩm",
            ["received wrong item"] = "Shop gửi sai sản phẩm",
        };
        return map.TryGetValue(en.ToLowerInvariant().Trim(), out var vi) ? vi : en;
    }

    private static string TranslateReturnStatus(string status, string subStatus)
    {
        var sub = new Dictionary<string, string>
        {
            ["awaiting customer shipment"] = "Chờ khách gửi hàng",
            ["return shipped"] = "Đang vận chuyển về",
            ["refund apply"] = "Chờ shop duyệt hoàn tiền",
            ["return rejected"] = "Từ chối hoàn trả",
            ["request canceled"] = "Khách đã hủy yêu cầu",
            ["refund success"] = "Đã hoàn tiền",
            ["completed"] = "Đã hoàn tất",
        };
        if (sub.TryGetValue((subStatus ?? "").ToLowerInvariant().Trim(), out var vi)) return vi;
        if (!string.IsNullOrEmpty(subStatus)) return subStatus;
        if (status == "In Process") return "Đang xử lý";
        if (status == "To Process") return "Chờ xử lý";
        if (status == "Refund rejected") return "Từ chối hoàn";
        return string.IsNullOrEmpty(status) ? "Chờ xử lý" : status;
    }
}
