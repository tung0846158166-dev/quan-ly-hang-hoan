using System.Text.RegularExpressions;

namespace QuanLyHangHoan.Core.Import;

public sealed class ShopeeMapOptions
{
    /// <summary>chkFilterOnlyReturns (mặc định bật): bỏ dòng đơn giao thành công không có yêu cầu hoàn/hủy.</summary>
    public bool OnlyReturns { get; set; } = true;
    public DateTime Today { get; set; } = DateTime.Now;
}

public sealed class ShopeeMapResult
{
    public List<ImportedParcel> Parcels { get; } = new();
    public int SkippedSuccess { get; set; }
    public int MultiItemAppended { get; set; }
}

/// <summary>
/// Ánh xạ bảng file Shopee (Trả hàng/Hoàn tiền, Đơn hủy, Giao không thành công; cả sheet Google đã đổi tên cột) thành kiện.
/// Chuyển từ ingestShopeeRecords(rows, 'Shopee') của app cũ; đối chiếu với đáp án sinh từ mã JS cũ (Golden/shopee.golden.json).
/// Giữ NGUYÊN các điểm lạ của app cũ (vd. trạng thái "Hoàn thành" chứa chữ "hoan" nên không bị coi là giao thành công).
/// </summary>
public static class ShopeeMapper
{
    private enum PriceKind { Order, Line, Unit }
    private sealed record PriceColumn(string Key, PriceKind Kind);
    private sealed class Group
    {
        public ImportedParcel P { get; } = new();
        public Dictionary<string, long> PriceTotals { get; } = new();
    }

    private static string K(string? s) => Regex.Replace(LegacyText.RemoveTones(s), "[^a-z0-9]", "");
    private static string MoneyHeader(string key) => Regex.Replace(K(key), "(?:vnd|usd)$", "");

    public static ShopeeMapResult Map(IReadOnlyList<Dictionary<string, string>> rawRows, ShopeeMapOptions? options = null)
    {
        options ??= new ShopeeMapOptions();
        var result = new ShopeeMapResult();
        if (rawRows.Count == 0) return result;
        var year = options.Today.Year;
        const RegexOptions I = RegexOptions.IgnoreCase;

        var keys = new List<string>();
        var keySeen = new HashSet<string>();
        foreach (var r in rawRows) foreach (var k in r.Keys) if (keySeen.Add(k)) keys.Add(k);

        string? FindKey(string[] patterns, params string[] excludes)
        {
            foreach (var p in patterns)
            {
                var found = keys.FirstOrDefault(k =>
                {
                    var norm = LegacyText.Key(k);
                    if (excludes.Any(ex => norm.Contains(ex.ToLowerInvariant()))) return false;
                    return norm.Contains(p.ToLowerInvariant());
                });
                if (found is not null) return found;
            }
            return null;
        }

        var returnTrackKey = FindKey(new[] { "mavandontrahang", "mvdtrahang", "returntracking" });
        var forwardTrackKey = FindKey(new[] { "mavdchieugiaogoc", "mavandongiaohang", "mavandon", "tracking", "mavan", "mvd", "matheodoi", "makienhang", "idgoihang", "packageid", "mabuugui", "sovandon", "trackingnumber", "trackingid" },
            "mavandontrahang", "mvdtrahang", "returntracking");
        var trackKey = forwardTrackKey ?? returnTrackKey;
        var orderIdKey = FindKey(new[] { "madonhang", "orderid", "madon", "iddonhang", "iddon", "suborderid", "madonhangcon", "order" }, "masokhieunai", "makhieunai");
        var returnCarrierKey = FindKey(new[] { "donvivanchuyentrahang" });
        var forwardCarrierKey = FindKey(new[] { "donvivanchuyengiaohang", "donvivanchuyen", "nhavanchuyen", "shippingprovider", "dvvc", "carrier", "donvigiaohang", "logisticsprovider", "kenhvanchuyen", "shippingcarrier" }, "donvivanchuyentrahang");
        var productKey = FindKey(new[] { "tensanphamtrensan", "tensanpham", "sanpham", "tensp", "productname", "product", "tenhanghoa", "itemname", "tenmathang" }, "masanpham", "id", "masku", "skuid", "skusanpham");
        var varKey = FindKey(new[] { "tenphanloaitrensan", "tenphanloaihang", "tenphanloai", "tuychonbienthe", "tenbienthe", "bienthe", "phanloaihang", "phanloai", "variationname", "variation", "thuoctinh", "specification", "skuname" },
            "maphanloai", "masanpham", "masku", "skuid", "skuphanloai", "idbienthe", "idphanloai", "idgoihang", "packageid");
        var sellerSkuKey = FindKey(new[] { "masanphamsalework", "skuphanloai", "sellersku", "maskuphanloai", "masku", "masanpham", "skusanpham" });
        var skuIdKey = FindKey(new[] { "skuid", "idbienthe", "maphanloai" });
        var explicitColorKey = FindKey(new[] { "mausac", "mau", "color", "colour" }, "masku", "skuid", "id", "lydo", "reason", "note", "ghichu", "chitiet", "huy", "hoan", "tra", "maphanloai");
        var explicitSizeKey = FindKey(new[] { "kichthuoc", "kichco", "size" }, "masku", "skuid", "id", "lydo", "reason", "note", "ghichu", "chitiet", "huy", "hoan", "tra", "status", "trangthai", "maphanloai");
        var noteKey = FindKey(new[] { "lidotrahanghoantien", "lydotrahanghoantien", "lydohoantien", "lydotrahang", "lydohuy", "lidohuy", "lydo", "lido", "reason", "returnreason", "chitiethoantien", "chitietlydo", "noidung", "motanoidung" },
            "ghichucuanguoimuakhitrahang", "nhanxettunguoimua", "ghichucuanguoimuakhidathang", "ghichucuakhach");
        var returnShipmentStatusKey = FindKey(new[] { "trangthaitrahang" }, "trangthaitrahanghoantien");
        var returnMethodKey = FindKey(new[] { "phuongan", "phuonganhoantien" });
        var claimIdKey = FindKey(new[] { "masokhieunai", "makhieunai", "claimid", "returnid", "returnorderid" });
        var buyerKey = FindKey(new[] { "nguoimua", "buyer", "buyerusername", "username", "khachhang", "tenkhachhang" }, "nguoiban");
        var orderDateKey = FindKey(new[] { "ngaydathang", "thoidiemdathang", "ngaydat", "orderdate" });
        var claimTimeKey = FindKey(new[] { "thoigiankhieunai", "thoidiemkhieunai", "claimtime" });
        var isFullReturnKey = FindKey(new[] { "trahanghoantientoanbodonhang", "isallreturn" });
        var returnSuccessTimeKey = FindKey(new[] { "thoigianhoantrahangthanhcong", "ngayhoantrathanhcong", "thoigiangiaohangthanhcong" });
        var deliveryAddressKey = FindKey(new[] { "diachinhanhang", "deliveryaddress" });
        var returnAddressKey = FindKey(new[] { "diachilayhanghoantra", "returnaddress" });
        var sellerResponseDueKey = FindKey(new[] { "nguoibancanphanhoitruoc", "hancanxuly", "thoihanphanhoi" });
        var claimReasonKey = FindKey(new[] { "lidokhieunai", "lydokhieunai" });
        var sellerNoteKey = FindKey(new[] { "ghichucuanguoiban" });
        var compensationKey = FindKey(new[] { "sotiendenbu", "denbu" });
        var orderHandlingTypeKey = FindKey(new[] { "loaixulydonhang", "xulydonhang" });
        var totalValueKey = FindKey(new[] { "tonggiatri", "tonggiatridonhang", "totalamount" });
        var paymentMethodKey = FindKey(new[] { "phuongthucthanhtoan", "paymentmethod" });
        var buyerOrderNoteKey = FindKey(new[] { "ghichucuanguoimuakhidathang" });
        var revenueKey = FindKey(new[] { "doanhthu", "revenue" });
        var forwardShippingFeeKey = FindKey(new[] { "phivanchuyenchieugiao" });
        var returnShippingFeeKey = FindKey(new[] { "phivanchuyenchieuhoantra" });
        var refundAmountKey = FindKey(new[] { "tongsotienhoantra", "sotienhoantra", "refundamount" });
        var refundTimeKey = FindKey(new[] { "thoigianhoantien", "ngayhoantien", "refundtime" });
        var returnQtyPatterns = new[] { "soluonghoan", "soluongsanphamduochoantra", "returnquantity", "returnedquantity", "returnqty", "returnedqty" };
        var returnQtyKey = FindKey(returnQtyPatterns);
        var orderQtyKey = FindKey(new[] { "soluong", "quantity", "sl", "soluongmathang", "itemquantity", "qty" }, returnQtyPatterns.Concat(new[] { "tongsoluong" }).ToArray());

        var priceColumns = new List<PriceColumn>();
        var priceGroups = new (PriceKind kind, string[] aliases)[]
        {
            (PriceKind.Order, new[] { "tonggiatridonhang", "tongtienhangsauchietkhau", "doanhthuuoctinh", "tongsotiennguoimuathanhtoan", "tongtienthanhtoan", "ordertotal" }),
            (PriceKind.Order, new[] { "tongsotienhoantra", "refundamount" }),
            (PriceKind.Order, new[] { "tonggiatri", "totalamount", "orderamount", "tongtien" }),
            (PriceKind.Line, new[] { "thanhtien", "tonggiaban", "linesubtotal", "linetotal", "itemsubtotal", "subtotal" }),
            (PriceKind.Unit, new[] { "giauda", "giauudai", "giaban", "giatien", "dongia", "skuunitprice", "unitprice", "price", "giasantmdt", "giasalework", "giagoc", "giabanle" }),
        };
        foreach (var (kind, aliases) in priceGroups)
            foreach (var alias in aliases)
            {
                var key = keys.FirstOrDefault(k => MoneyHeader(k) == alias);
                if (key is not null && !priceColumns.Any(c => c.Key == key)) priceColumns.Add(new PriceColumn(key, kind));
            }

        var shipDateKeys = new[]
        {
            FindKey(new[] { "thoigiangui", "ngaygui", "shippedtime" }),
            FindKey(new[] { "thoigiandong", "ngaydong", "rtstime" }),
            FindKey(new[] { "thoigianxuly", "ngayxuly" }),
            FindKey(new[] { "ngaytaodon", "thoigiantao", "ngaydat", "orderdate", "thoidiemtao", "thoidiemdathang", "createdtime", "paidtime" }),
            FindKey(new[] { "timerequested", "thoigiankhieunai", "thoigianyeucau", "thoigianhoan", "ngayyeucau" }),
        }.Where(k => k is not null).Cast<string>().ToList();
        var returnDateKeys = new[]
        {
            FindKey(new[] { "timerequested", "thoigiankhieunai", "ngayhuythanhcong", "ngayhoan", "thoigianhoan", "ngaytrahang", "ngayhuy", "thoigianhoantien", "refundtime", "thoigianyeucau", "ngayyeucau" }),
            FindKey(new[] { "ngaygiaohangthanhcong", "thoigiangiao", "deliverytime" }),
        }.Where(k => k is not null).Cast<string>().ToList();
        var statusKey = FindKey(new[] { "trangthaidonhang", "orderstatus", "status", "tinhtrang", "trangthaigoihang" }, "trangthaitrahang", "trangthaitrahanghoantien", "trangthaikhieunai");
        var returnStatusKey = FindKey(new[] { "trangthaitrahanghoantien", "trangthaihoantien", "trangthaikhieunai", "trangthaitrahang", "returnstatus", "refundstatus" });
        var cancelReasonColKey = FindKey(new[] { "lydohuy", "lidohuy" });
        var preOrderKey = FindKey(new[] { "hangdattruoc", "dattruoc", "preorder", "loaidonhang" }, "loaixuly", "xuly");

        string Get(Dictionary<string, string> row, string? key) => key is not null && row.TryGetValue(key, out var v) ? v ?? "" : "";
        bool Has(Dictionary<string, string> row, string? key) => key is not null && row.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v);
        static bool IsNone(string? v) => Regex.IsMatch(LegacyText.RemoveTones(v), @"^(không|khong|none|\-)$", I);
        string Tone(string? s) => LegacyText.RemoveTones(s).ToLowerInvariant();

        var groups = new Dictionary<string, Group>();
        var groupOrder = new List<string>();
        var zeroReturned = new HashSet<string>();
        var lastGroupKey = "";
        var lastSkippedRow = false;
        string? lastSourceFileId = null;
        var hasLastSource = false;

        foreach (var row in rawRows)
        {
            var sourceId = row.TryGetValue("__sourceFileId", out var sf) ? sf : null;
            if (!hasLastSource || sourceId != lastSourceFileId) { lastGroupKey = ""; lastSkippedRow = false; lastSourceFileId = sourceId; hasLastSource = true; }

            string Clean(string? key) => key is null ? "" : LegacyText.CleanTrackingCode(Get(row, key).Trim());
            var rawReturnTrack = Clean(returnTrackKey);
            var rawForwardTrack = Clean(forwardTrackKey);
            var rawTrack = rawReturnTrack.Length > 0 ? rawReturnTrack : rawForwardTrack.Length > 0 ? rawForwardTrack : Clean(trackKey);
            var rawAltTrack = rawReturnTrack.Length > 0 && rawForwardTrack.Length > 0 && rawReturnTrack != rawForwardTrack ? rawForwardTrack : "";

            var rawOrderId = orderIdKey is not null ? LegacyText.NormalizeScientificOrderId(Get(row, orderIdKey)) : "";
            var rawProd = Get(row, productKey).Trim();
            var rawVar = Get(row, varKey).Trim();
            var rawSellerSku = Get(row, sellerSkuKey).Trim();
            var rawClaimId = Get(row, claimIdKey).Trim();

            var isContinuation = false;
            if (rawTrack.Length == 0 && rawOrderId.Length == 0)
            {
                groups.TryGetValue(lastGroupKey, out var previous);
                var previousClaim = (previous?.P.RawDetails?["claimId"] ?? "").Trim().ToUpperInvariant();
                var sameClaim = rawClaimId.Length == 0 || rawClaimId.ToUpperInvariant() == previousClaim;
                if (previous is not null && !lastSkippedRow && sameClaim && (rawProd.Length > 0 || rawVar.Length > 0 || rawSellerSku.Length > 0)) isContinuation = true;
                else if (rawClaimId.Length == 0) continue;
            }

            if (!isContinuation && options.OnlyReturns)
            {
                var rawOrderSt = statusKey is not null ? Tone(Get(row, statusKey)) : "";
                var rawRetSt = returnStatusKey is not null ? Tone(Get(row, returnStatusKey)) : "";
                var retQtyVal = returnQtyKey is not null ? LegacyText.ParseQuantity(Get(row, returnQtyKey)) : 0;
                if (rawOrderSt.Length > 0)
                {
                    var isDelivered = rawOrderSt.Contains("thanh cong") || rawOrderSt.Contains("da giao") || rawOrderSt.Contains("hoan thanh") || rawOrderSt.Contains("delivered") || rawOrderSt.Contains("completed");
                    var hasActiveReturnClaim = rawRetSt.Length > 0 || retQtyVal > 0;
                    var isReturnOrCancel = hasActiveReturnClaim || rawOrderSt.Contains("hoan") || rawOrderSt.Contains("huy") || rawOrderSt.Contains("tra") || rawOrderSt.Contains("that thu")
                        || rawOrderSt.Contains("that bai") || rawOrderSt.Contains("khong thanh") || rawOrderSt.Contains("cancel") || rawOrderSt.Contains("return") || rawOrderSt.Contains("refund");
                    if (isDelivered && !isReturnOrCancel) { result.SkippedSuccess++; lastSkippedRow = true; continue; }
                }
            }
            if (!isContinuation) lastSkippedRow = false;

            var groupKey = isContinuation ? lastGroupKey : TikTokMapper.ParcelKey(rawOrderId, rawTrack, rawClaimId, rawReturnTrack, "");
            if (!isContinuation) lastGroupKey = groupKey;

            var rawQty = 1;
            var hasExplicitReturnQty = false;
            if (returnQtyKey is not null && Has(row, returnQtyKey) && Get(row, returnQtyKey).Trim().Length > 0)
            {
                var rq = LegacyText.ParseQuantity(Get(row, returnQtyKey));
                var explicitReturnContext = rawReturnTrack.Length > 0 || Get(row, claimIdKey).Trim().Length > 0 || Get(row, returnStatusKey).Trim().Length > 0;
                var cancelOrFailed = Regex.IsMatch(Tone(Get(row, "__sourceFileName") + " " + Get(row, statusKey)), "cancel|huy|failed_delivery|that bai|that thu");
                if (rq == 0 && (explicitReturnContext || !cancelOrFailed)) { zeroReturned.Add(groupKey); continue; }
                if (rq > 0) { rawQty = rq; hasExplicitReturnQty = true; }
            }
            if (!hasExplicitReturnQty && orderQtyKey is not null && Has(row, orderQtyKey))
            {
                var oq = LegacyText.ParseQuantity(Get(row, orderQtyKey));
                if (oq > 0) rawQty = oq;
            }

            var fullText = Get(row, isFullReturnKey).Trim().ToLowerInvariant();
            var isShopeePartial = fullText.Contains("một phần") || fullText.Contains("mot phan") || fullText.Contains("partial");
            var isPartialQty = false;
            if (hasExplicitReturnQty && orderQtyKey is not null && Has(row, orderQtyKey))
                if (LegacyText.ParseQuantity(Get(row, orderQtyKey)) > rawQty) isPartialQty = true;

            var rawPreOrder = "Đơn thường";
            if (preOrderKey is not null && Has(row, preOrderKey))
            {
                var po = Tone(Get(row, preOrderKey)).Trim();
                if (po.Contains("dat truoc") || po.Contains("preorder") || po.Contains("pre-order") || po is "co" or "yes" or "true") rawPreOrder = "Hàng đặt trước";
            }

            if (!groups.TryGetValue(groupKey, out var entry)) entry = null;
            // tổng tiền theo cột cho nhóm (tính trước khi tạo nhóm: lưu tạm ở nhóm mới)
            var totals = entry?.PriceTotals ?? new Dictionary<string, long>();
            foreach (var column in priceColumns)
            {
                if (!Has(row, column.Key) || Get(row, column.Key).Trim().Length == 0) continue;
                var value = LegacyText.ParseMoney(Get(row, column.Key));
                var had = totals.TryGetValue(column.Key, out var previous);
                totals[column.Key] = column.Kind == PriceKind.Order ? (had ? previous : value) : previous + value * (column.Kind == PriceKind.Unit ? rawQty : 1);
            }
            var chosen = priceColumns.FirstOrDefault(c => totals.TryGetValue(c.Key, out var t) && t > 0) ?? priceColumns.FirstOrDefault(c => totals.ContainsKey(c.Key));
            var rawPrice = chosen is not null ? totals[chosen.Key] : 0;

            var rawShipDate = "";
            foreach (var dk in shipDateKeys)
                if (Get(row, dk).Length > 0) { var ex = LegacyText.ExtractDate(Get(row, dk), year); if (ex.Length > 0) { rawShipDate = ex; break; } }
            var rawReturnDate = "";
            foreach (var rk in returnDateKeys)
                if (Get(row, rk).Length > 0) { var ex = LegacyText.ExtractDate(Get(row, rk), year); if (ex.Length > 0) { rawReturnDate = ex; break; } }
            if (rawReturnDate.Length == 0 && rawShipDate.Length > 0) rawReturnDate = rawShipDate;
            if (rawShipDate.Length == 0 && rawReturnDate.Length > 0) rawShipDate = rawReturnDate;

            var noteParts = new List<string>();
            var returnShipStRaw = Get(row, returnShipmentStatusKey).Trim();
            var returnShipSt = IsNone(returnShipStRaw) ? "" : returnShipStRaw;
            var returnMethod = Get(row, returnMethodKey).Trim();
            var mainReason = Get(row, noteKey).Trim();
            var cancelReasonText = Get(row, cancelReasonColKey).Trim();

            var buyerComment = LegacyText.ExtractBuyerNote(row);
            if (buyerComment.Length == 0)
                foreach (var abk in new[] { "Ghi chú của Người mua khi trả hàng", "Nhận xét từ Người mua", "Ghi chú của khách", "Ghi chú của Người mua khi đặt hàng", "Ghi chú" })
                {
                    var foundK = keys.FirstOrDefault(k => Regex.Replace(LegacyText.RemoveTones(k), "[^a-z0-9]", "") == Regex.Replace(LegacyText.RemoveTones(abk), "[^a-z0-9]", ""));
                    if (foundK is not null && Get(row, foundK).Length > 0)
                    {
                        var val = Get(row, foundK).Trim();
                        if (val.Length > 0 && !IsNone(val)) { buyerComment = val; break; }
                    }
                }
            if (IsNone(buyerComment)) buyerComment = "";

            if (returnShipSt.Length > 0) noteParts.Add($"[{returnShipSt}]");
            else if (returnMethod.Length > 0 && Tone(returnMethod).Contains("hoan tien ngay")) noteParts.Add($"[{returnMethod}]");
            if (mainReason.Length > 0) noteParts.Add(mainReason);
            else if (cancelReasonText.Length > 0 && !noteParts.Contains(cancelReasonText)) noteParts.Add(cancelReasonText);

            var rawNote = string.Join(" | ", noteParts);
            var rawStatusText = Get(row, statusKey).Trim();
            if (rawStatusText.Length > 0 && rawNote.Length == 0)
            {
                var stNorm = Tone(rawStatusText);
                if (stNorm == "that thu") rawNote = "THẤT THU (Hoàn về kho)";
                else if (stNorm == "huy" || stNorm.Contains("da huy")) rawNote = "Đơn HỦY";
                else if (stNorm.Contains("that bai") || stNorm.Contains("khong thanh")) rawNote = "Giao hàng thất bại";
            }

            var pv = LegacyText.ParseProductAndVariation(rawProd, rawVar, rawSellerSku);
            var cleanProd = pv.Product; var color = pv.Color; var size = pv.Size;
            if (pv.ExtraNote.Length > 0 && rawNote.Length == 0) rawNote = pv.ExtraNote;

            if (explicitColorKey is not null && Get(row, explicitColorKey).Length > 0)
            {
                var cVal = Get(row, explicitColorKey).Trim();
                if (LegacyText.IsValidColorString(cVal)) color = cVal;
                else if (LegacyText.IsReturnReasonText(cVal) && rawNote.Length == 0) rawNote = cVal;
            }
            if (explicitSizeKey is not null && Get(row, explicitSizeKey).Length > 0)
            {
                var sVal = Get(row, explicitSizeKey).Trim();
                if (LegacyText.IsValidSizeString(sVal)) size = sVal.ToUpperInvariant();
                else if (LegacyText.IsReturnReasonText(sVal) && rawNote.Length == 0) rawNote = sVal;
            }
            if (size.Length > 0 && color.Length == 0 && LegacyText.IsKnownColorName(size)) { color = size; size = ""; }
            else if (color.Length > 0 && size.Length == 0 && LegacyText.IsValidSizeString(color) && !LegacyText.IsKnownColorName(color)) { size = color; color = ""; }

            if (Regex.IsMatch(color, @"^\d{4,}$") || LegacyText.IsReturnReasonText(color))
            {
                if (LegacyText.IsReturnReasonText(color) && rawNote.Length == 0) rawNote = color;
                color = "";
            }
            if (Regex.IsMatch(size, @"^\d{4,}$") || LegacyText.IsReturnReasonText(size))
            {
                if (LegacyText.IsReturnReasonText(size) && rawNote.Length == 0) rawNote = size;
                size = "";
            }

            if ((color.Length == 0 || size.Length == 0) && cleanProd.Length > 0)
            {
                var prodParts = Regex.Split(cleanProd, @"[\-\|\/]").Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                if (prodParts.Count > 1)
                {
                    var sizeRe = new Regex(@"^(XXXL|XXL|XL|XS|S|M|L|[2-5]XL|\d{2}|Free\s*size)$", I);
                    var qualityRe = new Regex(@"(?:premium|cao\s*c[aấ]p|th[eê]u|l[oô]ng\s*c[aá]o|d[eệ]t\s*kim|local\s*brand|lavi\s*studio|unisex)", I);
                    foreach (var part in prodParts.Skip(1))
                    {
                        if (qualityRe.IsMatch(part)) continue;
                        if (sizeRe.IsMatch(part) && size.Length == 0 && LegacyText.IsValidSizeString(part)) size = Regex.Replace(part.ToUpperInvariant(), @"\s+", "");
                        else if (color.Length == 0 && !Regex.IsMatch(part, @"^\d+$") && part.Length < 25 && !LegacyText.IsReturnReasonText(part) && LegacyText.IsKnownColorName(part)) color = part;
                    }
                }
            }

            var rawReturnCarrier = Get(row, returnCarrierKey).Trim();
            var rawForwardCarrier = Get(row, forwardCarrierKey).Trim();
            var rawCarrierFromExcel = rawReturnCarrier.Length > 0 ? rawReturnCarrier : rawForwardCarrier;
            string FirstNonEmpty(params string?[] c) => c.FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? "";
            var carrier = FirstNonEmpty(rawReturnCarrier, LegacyText.DetectCarrierByTrackingCode(rawReturnTrack), rawForwardCarrier, LegacyText.DetectCarrierByTrackingCode(rawForwardTrack), "SPX Express");
            if (rawCarrierFromExcel.Length > 0)
            {
                var cUpper = LegacyText.RemoveTones(rawCarrierFromExcel).ToUpperInvariant();
                if (cUpper.Contains("J&T") || cUpper.Contains("JT")) carrier = "J&T Express";
                else if (cUpper.Contains("NINJA")) carrier = "Ninja Van";
                else if (cUpper.Contains("SHOPEE") || cUpper.Contains("SPX") || cUpper.Contains("TRONG NGAY")) carrier = "SPX Express";
                else if (cUpper.Contains("TIET KIEM") || cUpper.Contains("GHTK")) carrier = "GHTK";
                else if (cUpper.Contains("NHANH") || cUpper.Contains("GHN")) carrier = "GHN";
                else if (cUpper.Contains("VIETTEL") || cUpper.Contains("VTP")) carrier = "Viettel Post";
                else if (cUpper.Contains("BEST") || rawTrack.StartsWith("TTVN", StringComparison.Ordinal)) carrier = "Best Express";
                else if (cUpper.Contains("VNPOST") || cUpper.Contains("BUU DIEN")) carrier = "VNPost";
                else if (cUpper.Contains("AHA")) carrier = "AhaMove";
                else if (cUpper.Contains("GREEN") || cUpper.Contains("XANH") || cUpper.Contains("GRAB") || cUpper.Contains("BE ")) carrier = "Khác";
            }

            // Loại đơn
            var srcFileNorm = Get(row, "__sourceFileName").ToLowerInvariant();
            var stNormForType = Tone(rawStatusText);
            var retStNormForType = returnStatusKey is not null ? Tone(Get(row, returnStatusKey)) : "";
            var cancelNormForType = Tone($"{cancelReasonText} {mainReason} {rawNote}");
            var hasReturnQtyColVal = returnQtyKey is not null && LegacyText.ParseQuantity(Get(row, returnQtyKey)) > 0;
            string orderType;
            if (stNormForType.Contains("that thu")) orderType = "THẤT THU";
            else if (srcFileNorm.Contains("failed_delivery")) orderType = "Giao không thành công";
            else if (srcFileNorm.Contains("return_refund")) orderType = "Trả hàng/Hoàn tiền";
            else if (srcFileNorm.Contains("cancel"))
                orderType = cancelNormForType.Contains("giao hang that bai") || cancelNormForType.Contains("giao khong thanh cong") ? "Giao không thành công" : "Đơn hủy";
            else if (cancelNormForType.Contains("giao hang that bai") || cancelNormForType.Contains("giao khong thanh cong") || cancelNormForType.Contains("failed delivery")
                     || stNormForType.Contains("that bai") || stNormForType.Contains("khong thanh")) orderType = "Giao không thành công";
            else if ((retStNormForType.Length > 0 && retStNormForType != "khong" && retStNormForType != "none") || hasReturnQtyColVal
                     || (returnMethod.Length > 0 && Tone(returnMethod) != "khong") || (returnShipSt.Length > 0 && Tone(returnShipSt) != "khong")) orderType = "Trả hàng/Hoàn tiền";
            else if (stNormForType.Contains("huy") || stNormForType.Contains("cancel") || cancelReasonText.Length > 0) orderType = "Đơn hủy";
            else orderType = "Trả hàng/Hoàn tiền";

            string StatusFrom(string? key) { var v = Get(row, key).Trim(); return v.Length > 0 && !IsNone(v) ? v : ""; }
            var platformStatus = StatusFrom(returnStatusKey);
            if (platformStatus.Length == 0) platformStatus = StatusFrom(returnShipmentStatusKey);
            if (platformStatus.Length == 0) platformStatus = StatusFrom(statusKey);
            if (platformStatus.Length == 0)
                platformStatus = orderType == "Trả hàng/Hoàn tiền" ? "Chờ bạn xác nhận hàng trả về" : orderType == "Đơn hủy" ? "Đã hủy" : orderType == "Giao không thành công" ? "Giao hàng thất bại" : "Chờ xử lý";

            if (entry is null)
            {
                string S(string? key) => Get(row, key).Trim();
                string M(string? key) => key is null ? "0" : LegacyText.ParseMoney(Get(row, key)).ToString();
                entry = new Group();
                var raw = new Dictionary<string, string>
                {
                    ["claimId"] = S(claimIdKey), ["buyerUsername"] = S(buyerKey), ["orderDate"] = S(orderDateKey), ["claimTime"] = S(claimTimeKey),
                    ["returnStatus"] = S(returnStatusKey), ["isFullReturn"] = S(isFullReturnKey), ["returnMethod"] = S(returnMethodKey), ["returnReason"] = S(noteKey),
                    ["buyerReturnNote"] = buyerComment, ["refundAmount"] = M(refundAmountKey), ["refundTime"] = S(refundTimeKey),
                    ["returnShippingStatus"] = S(returnShipmentStatusKey), ["returnCarrier"] = rawReturnCarrier, ["returnTrack"] = rawReturnTrack,
                    ["returnSuccessTime"] = S(returnSuccessTimeKey), ["deliveryAddress"] = S(deliveryAddressKey), ["returnAddress"] = S(returnAddressKey),
                    ["sellerResponseDue"] = S(sellerResponseDueKey), ["claimReason"] = S(claimReasonKey), ["sellerNote"] = S(sellerNoteKey),
                    ["compensation"] = M(compensationKey), ["forwardCarrier"] = rawForwardCarrier, ["orderHandlingType"] = S(orderHandlingTypeKey),
                    ["forwardTrack"] = rawForwardTrack, ["totalValue"] = M(totalValueKey), ["paymentMethod"] = S(paymentMethodKey),
                    ["buyerOrderNote"] = S(buyerOrderNoteKey), ["revenue"] = M(revenueKey), ["forwardShippingFee"] = M(forwardShippingFeeKey),
                    ["returnShippingFee"] = M(returnShippingFeeKey),
                };
                var p0 = entry.P;
                p0.Platform = "Shopee";
                p0.PlatformStatus = platformStatus;
                p0.OrderType = orderType;
                p0.IsPartialReturn = isShopeePartial || isPartialQty;
                p0.PreOrder = rawPreOrder;
                p0.Deadline = sellerResponseDueKey is not null ? Get(row, sellerResponseDueKey).Trim() : "";
                p0.OrderId = rawOrderId; p0.Track = rawTrack; p0.AltTrack = rawAltTrack; p0.ForwardTrack = rawForwardTrack; p0.ReturnTrack = rawReturnTrack;
                p0.Carrier = carrier; p0.ShipDate = rawShipDate; p0.ReturnDate = rawReturnDate; p0.Note = rawNote; p0.BuyerNote = buyerComment;
                p0.RawDetails = raw;
                groups[groupKey] = entry; groupOrder.Add(groupKey);
                foreach (var kv in totals) entry.PriceTotals[kv.Key] = kv.Value;
            }
            else result.MultiItemAppended++;

            var p = entry.P;
            p.TrackingCodes = TikTokMapper.TrackingCodesOf(p).Concat(new[] { rawTrack }).Concat(ParseTrackingColumn(row)).Concat(new[] { rawAltTrack })
                .Where(c => c.Length > 0).Distinct().ToList();
            p.PriceHasValue = chosen is not null;
            p.ImportPriceBasis = chosen?.Kind == PriceKind.Order ? "order" : "line";
            if (isShopeePartial || isPartialQty) p.IsPartialReturn = true;
            if (p.PlatformStatus.Length == 0 && platformStatus.Length > 0) p.PlatformStatus = platformStatus;
            if (rawPreOrder == "Hàng đặt trước") p.PreOrder = rawPreOrder;
            if (rawForwardTrack.Length > 0 && p.ForwardTrack.Length == 0) p.ForwardTrack = rawForwardTrack;
            if (rawReturnTrack.Length > 0 && p.ReturnTrack.Length == 0) p.ReturnTrack = rawReturnTrack;
            if (rawTrack.Length > 0 && p.Track.Length == 0) p.Track = rawTrack;
            if (rawAltTrack.Length > 0 && p.AltTrack.Length == 0) p.AltTrack = rawAltTrack;
            if (rawOrderId.Length > 0 && p.OrderId.Length == 0) p.OrderId = rawOrderId;
            p.Price = rawPrice;
            if (rawShipDate.Length > 0 && p.ShipDate.Length == 0) p.ShipDate = rawShipDate;
            if (rawReturnDate.Length > 0 && p.ReturnDate.Length == 0) p.ReturnDate = rawReturnDate;
            if (rawNote.Length > 0 && p.Note.Length == 0) p.Note = rawNote;
            if (buyerComment.Length > 0 && p.BuyerNote.Length == 0) p.BuyerNote = buyerComment;

            if (cleanProd.Length > 0 || color.Length > 0 || size.Length > 0)
            {
                var sku = rawSellerSku.Length > 0 ? rawSellerSku : skuIdKey is not null ? Get(row, skuIdKey).Trim() : "";
                long? lineAmount = chosen is not null && chosen.Kind != PriceKind.Order && Has(row, chosen.Key)
                    ? LegacyText.ParseMoney(Get(row, chosen.Key)) * (chosen.Kind == PriceKind.Unit ? rawQty : 1) : null;
                p.Items.Add(new ImportedItem { Product = cleanProd, SellerSku = sku, SkuId = null, ImportLineAmount = lineAmount, Color = color, Size = size, Qty = rawQty });
            }
        }

        foreach (var gk in groupOrder)
        {
            var p = groups[gk].P;
            var claim = p.RawDetails?["claimId"] ?? "";
            if (zeroReturned.Contains(TikTokMapper.ParcelKey(p.OrderId, p.Track, claim, p.ReturnTrack, p.ForwardTrack))) p.IsPartialReturn = true;
            if (p.Items.Count == 0) p.Items.Add(new ImportedItem { Qty = 1 });
            if (p.PriceHasValue) p.PriceNeedsReview = false;
            result.Parcels.Add(p);
        }
        return result;
    }

    private static IEnumerable<string> ParseTrackingColumn(Dictionary<string, string> row)
    {
        var raw = row.TryGetValue("Tracking Codes", out var a) && a.Length > 0 ? a : row.TryGetValue("Mã vận đơn lịch sử", out var b) ? b : "";
        if (raw.Length == 0) return Array.Empty<string>();
        IEnumerable<string> codes;
        try { codes = System.Text.Json.JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>(); }
        catch (System.Text.Json.JsonException) { codes = Regex.Split(raw, @"[;,|]"); }
        return codes.Select(c => LegacyText.CleanTrackingCode(c ?? "")).Where(c => c.Length > 0).ToList();
    }

    /// <summary>Hình dạng DÒNG MỚI app cũ đẩy ra bảng chính cho kiện Shopee (mặc định khi tạo dòng).</summary>
    public static ImportedParcel ForNewRow(ImportedParcel raw, DateTime today)
    {
        var p = raw.Clone();
        var cleanTrack = LegacyText.CleanTrackingCode(p.Track);
        if (p.ReturnDate.Length == 0) p.ReturnDate = p.ShipDate.Length > 0 ? p.ShipDate : today.ToString("dd/MM/yyyy");
        var rs = p.RawDetails?["returnStatus"] ?? "";
        if (p.PlatformStatus.Length == 0)
            p.PlatformStatus = rs.Length > 0 ? rs : p.OrderType == "Đơn hủy" ? "Đã hủy" : p.OrderType == "Giao không thành công" ? "Giao hàng thất bại" : "Chờ bạn xác nhận hàng trả về";
        if (p.OrderType.Length == 0) p.OrderType = "Trả hàng/Hoàn tiền";
        if ((p.RawDetails?["isFullReturn"] ?? "").ToLowerInvariant().Contains("một phần")) p.IsPartialReturn = true;
        p.TrackingCodes = TikTokMapper.TrackingCodesOf(p);
        if (cleanTrack.Length > 0) p.Track = cleanTrack;
        p.AltTrack = LegacyText.CleanTrackingCode(p.AltTrack).Length > 0 ? LegacyText.CleanTrackingCode(p.AltTrack) : p.AltTrack;
        if (p.Carrier.Length == 0) p.Carrier = "SPX Express";
        p.Status = p.OrderType == "Đơn hủy" && cleanTrack.Length == 0 && p.Track.Length == 0 ? "Không cần nhận" : rs.Contains("hủy") ? "Không cần nhận" : "Chưa trả";
        if (p.Items.Count == 0) p.Items.Add(new ImportedItem { Qty = 1 });
        return p;
    }
}
