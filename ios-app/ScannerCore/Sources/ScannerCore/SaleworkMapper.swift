import Foundation

// Port of QuanLyHangHoan.Core.Import.TikTokMapper (C#): ImportedItem / ImportedParcel / TikTokMapOptions / TikTokMapResult / Map.

/// One product line of a mapped parcel (C# ImportedItem).
public struct SaleworkItem: Equatable {
    public var product: String = ""
    public var color: String = ""
    public var size: String = ""
    public var qty: Int = 1
    public var returnedQty: Int = 0
    /// nil = placeholder line added when the parcel had no product.
    public var importPrices: [String: Int64]? = nil
    public var sellerSku: String? = nil
    public var skuId: String? = nil

    public init() {}

    func incomingJSON() -> [String: Any] {
        var o: [String: Any] = [
            "product": product, "color": color, "size": size, "qty": qty, "returnedQty": returnedQty,
        ]
        if let ip = importPrices {
            var d: [String: Any] = [:]
            for (k, v) in ip { d[k] = NSNumber(value: v) }
            o["importPrices"] = d
        } else {
            o["importPrices"] = NSNull()
        }
        o["sellerSku"] = sellerSku.map { $0 as Any } ?? NSNull()
        o["skuId"] = skuId.map { $0 as Any } ?? NSNull()
        return o
    }
}

/// A parcel mapped from a Salework / TikTok file (C# ImportedParcel).
public final class SaleworkParcel {
    public var shipDate = ""
    public var returnDate = ""
    public var recvDate = ""
    public var platform = "TikTok Shop"
    public var orderType = ""
    public var isPartialReturn = false
    public var platformStatus = ""
    public var returnedAt = ""
    public var preOrder = "Đơn thường"
    public var orderId = ""
    public var returnOrderId = ""
    public var forwardTrack = ""
    public var returnTrack = ""
    public var trackingCodes: [String] = []
    public var track = ""
    public var altTrack = ""
    public var carrier = ""
    public var price: Int64 = 0
    public var priceHasValue = false
    /// nil when the file had no price at all.
    public var priceNeedsReview: Bool? = nil
    /// mode -> source slot; nil value = order-level price; missing key = no price.
    public var importModeSlots: [String: String?] = [:]
    public var importSelectedPriceMode = "tong_gia_tri"
    public var priceModes: [String: Int64] = [:]
    public var priceModeFlags: [String: Bool] = [:]
    public var status = "Chưa về"
    public var photo = ""
    public var note = ""
    public var buyerNote = ""
    public var items: [SaleworkItem] = []
    public var deadline = ""
    public var importPriceBasis = ""
    public var rawDetails: [String: String]? = nil

    public init() {}

    /// Exactly the object shape of IncomingJson (GoldenMergeVectorsTests.cs): what is POSTed to the cloud. JSONSerialization-compatible.
    public func incomingJSON() -> [String: Any] {
        var slots: [String: Any] = [:]
        for (k, v) in importModeSlots { slots[k] = v.map { $0 as Any } ?? NSNull() }
        var modes: [String: Any] = [:]
        for (k, v) in priceModes { modes[k] = NSNumber(value: v) }
        var flags: [String: Any] = [:]
        for (k, v) in priceModeFlags { flags[k] = v }
        var raw: Any = NSNull()
        if let rd = rawDetails {
            var d: [String: Any] = [:]
            for (k, v) in rd { d[k] = v }
            raw = d
        }
        return [
            "shipDate": shipDate, "returnDate": returnDate, "recvDate": recvDate, "platform": platform, "orderType": orderType,
            "isPartialReturn": isPartialReturn, "platformStatus": platformStatus, "returnedAt": returnedAt, "preOrder": preOrder,
            "orderId": orderId, "returnOrderId": returnOrderId, "forwardTrack": forwardTrack, "returnTrack": returnTrack,
            "trackingCodes": trackingCodes, "track": track, "altTrack": altTrack, "carrier": carrier, "price": NSNumber(value: price),
            "priceHasValue": priceHasValue, "priceNeedsReview": priceNeedsReview.map { $0 as Any } ?? NSNull(),
            "importModeSlots": slots, "importSelectedPriceMode": importSelectedPriceMode, "priceModes": modes,
            "priceModeFlags": flags, "status": status, "note": note, "buyerNote": buyerNote,
            "items": items.map { $0.incomingJSON() },
            "deadline": deadline, "importPriceBasis": importPriceBasis, "rawDetails": raw,
        ]
    }
}

/// C# TikTokMapOptions.
public struct SaleworkMapOptions {
    /// chkFilterOnlyReturns (default on): skip rows that are not return/cancel/failed.
    public var onlyReturns = true
    public var skipUnshippedCancel = false
    /// tong_gia_tri | sau_chiet_khau | gia_salework | doanh_thu_uoc_tinh
    public var priceMode = "tong_gia_tri"
    /// Year of "today" (used for dates without a year).
    public var todayYear: Int

    public init(todayYear: Int) { self.todayYear = todayYear }
}

/// C# TikTokMapResult.
public struct SaleworkMapResult {
    public var parcels: [SaleworkParcel] = []
    public var skipped = 0
    public var failedDelivery = 0
    public var premiumItems = 0
    public var withTrack = 0
    public var emptyTrackCancel = 0
    public var multiItemAppended = 0
}

public enum SaleworkMapper {
    private final class Group {
        let p = SaleworkParcel()
        var money: [String: Int64] = [:]
    }

    private static func find(_ keys: [String], _ exact: [String], _ includes: [String] = [], _ excludes: [String] = []) -> String? {
        let normKeys = keys.map { SWText.key($0) }
        for ex in exact {
            let exNorm = SWText.key(ex)
            if let i = normKeys.firstIndex(where: { SWText.ordinalEquals($0, exNorm) }) { return keys[i] }
        }
        for p in includes {
            let pl = SWText.lower(p)
            for (i, norm) in normKeys.enumerated() {
                if excludes.contains(where: { SWText.contains(norm, SWText.lower($0)) }) { continue }
                if SWText.contains(norm, pl) { return keys[i] }
            }
        }
        return nil
    }

    private static func isSkuSubtotal(_ key: String?) -> Bool {
        SWText.contains(SWText.key(key ?? ""), "skusubtotal")
    }

    private static func get(_ row: SaleworkRow, _ key: String?) -> String {
        guard let key = key else { return "" }
        return row[key] ?? ""
    }

    private static func has(_ row: SaleworkRow, _ key: String?) -> Bool {
        guard let key = key, let v = row[key] else { return false }
        return !SWText.isBlank(v)
    }

    /// getReturnParcelKey
    static func parcelKey(_ orderId: String, _ track: String, _ returnOrderId: String, _ returnTrack: String, _ forwardTrack: String) -> String {
        let code = SWText.cleanTrackingCode(!returnTrack.isEmpty ? returnTrack : !track.isEmpty ? track : forwardTrack)
        let id = SWText.upper(SWText.trim(returnOrderId))
        if !code.isEmpty { return "TRACK:" + code + (id.isEmpty ? "" : ":RETURN:" + id) }
        if !id.isEmpty { return "RETURN:" + id }
        return "ORDER:" + SWText.upper(SWText.trim(orderId))
    }

    /// getOrderTrackingCodes
    static func trackingCodesOf(_ p: SaleworkParcel) -> [String] {
        SWText.distinct(([p.track, p.altTrack, p.forwardTrack, p.returnTrack] + p.trackingCodes)
            .map { SWText.cleanTrackingCode($0) }.filter { !$0.isEmpty })
    }

    /// parseImportedTrackingCodes: "Tracking Codes" / "Mã vận đơn lịch sử" (JSON array of strings, else split by ; , |).
    private static func parseImportedTrackingCodes(_ row: SaleworkRow) -> [String] {
        var raw = ""
        if let a = row["Tracking Codes"], !a.isEmpty { raw = a } else if let b = row["Mã vận đơn lịch sử"] { raw = b }
        if raw.isEmpty { return [] }
        var codes: [String]
        if let list = jsonStringList(raw) {
            codes = list
        } else {
            codes = SWText.split(raw, "[;,|]")
        }
        return codes.map { SWText.cleanTrackingCode($0) }.filter { !$0.isEmpty }
    }

    /// JsonSerializer.Deserialize<List<string>>: "null" -> [], array of strings/nulls -> list, anything else -> nil (JsonException).
    private static func jsonStringList(_ raw: String) -> [String]? {
        guard let data = raw.data(using: .utf8),
              let obj = try? JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed]) else { return nil }
        if obj is NSNull { return [] }
        guard let arr = obj as? [Any] else { return nil }
        var out: [String] = []
        for e in arr {
            if e is NSNull { out.append(""); continue }
            guard let s = e as? String else { return nil }
            out.append(s)
        }
        return out
    }

    private static let reasonMap: [String: String] = [
        "change of mind": "Đổi ý / Không còn nhu cầu",
        "ordered wrong size": "Đặt nhầm size",
        "item too big or too small": "Mặc rộng / chật không vừa",
        "poor quality": "Chất lượng kém",
        "product is defective or doesn't work": "Hàng lỗi / Không sử dụng được",
        "product doesn't match description": "Hàng không đúng mô tả",
        "missing parts or accessories": "Thiếu hàng / phụ kiện",
        "package damaged": "Bưu kiện hư hỏng",
        "suspected counterfeit": "Nghi ngờ hàng giả",
        "delayed delivery": "Giao hàng chậm trễ",
        "wrong product sent": "Shop gửi sai sản phẩm",
        "received the wrong item": "Shop gửi sai sản phẩm",
        "received wrong item": "Shop gửi sai sản phẩm",
    ]

    private static func translateReason(_ en: String) -> String {
        reasonMap[SWText.trim(SWText.lower(en))] ?? en
    }

    private static let subStatusMap: [String: String] = [
        "awaiting customer shipment": "Chờ khách gửi hàng",
        "return shipped": "Đang vận chuyển về",
        "refund apply": "Chờ shop duyệt hoàn tiền",
        "return rejected": "Từ chối hoàn trả",
        "request canceled": "Khách đã hủy yêu cầu",
        "refund success": "Đã hoàn tiền",
        "completed": "Đã hoàn tất",
    ]

    private static func translateReturnStatus(_ status: String, _ subStatus: String) -> String {
        if let vi = subStatusMap[SWText.trim(SWText.lower(subStatus))] { return vi }
        if !subStatus.isEmpty { return subStatus }
        if status == "In Process" { return "Đang xử lý" }
        if status == "To Process" { return "Chờ xử lý" }
        if status == "Refund rejected" { return "Từ chối hoàn" }
        return status.isEmpty ? "Chờ xử lý" : status
    }

    /// TikTokMapper.Map. Throws SaleworkImportError when a quantity cell is invalid (C# FormatException).
    public static func map(_ rawRows: [SaleworkRow], options: SaleworkMapOptions) throws -> SaleworkMapResult {
        var result = SaleworkMapResult()
        if rawRows.isEmpty { return result }
        let year = options.todayYear

        // Column names from the first 50 rows.
        var keys: [String] = []
        var keySeen = Set<[UInt16]>()
        for r in rawRows.prefix(50) {
            for k in r.keys where keySeen.insert(Array(k.utf16)).inserted { keys.append(k) }
        }
        func f(_ exact: [String], _ includes: [String] = [], _ excludes: [String] = []) -> String? { find(keys, exact, includes, excludes) }

        let returnOrderIdKey = f(["Return Order ID", "Mã yêu cầu trả hàng", "Return ID"], ["returnorderid", "returnid", "mayeucau"])
        let returnLogisticsTrackKey = f(["Return Logistics Tracking ID", "Mã vận đơn trả hàng", "Return Tracking ID", "Return Tracking"],
                                        ["returnlogisticstrackingid", "mvdtrahang", "returntracking"])
        let saleworkReturnedKey = returnLogisticsTrackKey != nil ? nil : f(["Ngày trả hàng thành công"])
        let saleworkReturnTrackKey = returnLogisticsTrackKey != nil ? nil : f(["Mã trả hàng", "Mã vận đơn hoàn", "Mã vận đơn hoàn về"])
        let saleworkKindKey = f(["Loại đơn hoàn"], ["loaidonhoan"])
        let returnTypeKey = f(["Return Type", "Cancelation/Return Type", "Loại yêu cầu", "Loại hủy"], ["returntype", "cancelationreturntype", "canceltype", "loaiyeucau"])
        let returnReasonKey = f(["Return Reason", "Cancel Reason", "Lý do hoàn", "Lý do trả hàng", "Lý do hủy"], ["returnreason", "cancelreason", "lydohoan", "lydotrahang", "lydohuy"])
        let returnStatusKey = f(["Return Status", "Trạng thái hoàn"], ["returnstatus", "trangthaihoan"])
        let returnSubStatusKey = f(["Return Sub Status", "Trạng thái chi tiết"], ["returnsubstatus"])
        let timeRequestedKey = f(["Time Requested", "Thời gian khiếu nại", "Thời gian hoàn", "Thời gian yêu cầu"], ["timerequested", "thoigiankhieunai", "thoigianhoan", "ngayhoan", "ngayyeucau"])
        let refundTimeKey = f(["Refund Time", "Thời gian hoàn tiền"], ["refundtime", "thoigianhoantien"])
        let returnUnitPriceKey = f(["Return unit price", "Đơn giá hoàn"], ["returnunitprice", "dongiahoan"])

        let cancelledTimeKey = f(["Cancelled Time", "Thời gian hủy"], ["cancelledtime", "thoigianhuy"])
        let orderSubstatusKey = f(["Order Substatus", "Trạng thái phụ"], ["ordersubstatus", "substatus"])
        let skuQuantityOfReturnKey = f(["Sku Quantity of return", "Return Quantity", "Số lượng hoàn"], ["skuquantityofreturn", "returnquantity", "soluonghoan"])
        let shippedTimeKey = f(["Shipped Time", "Thời gian gửi"], ["shippedtime", "thoigiangui"])
        let rtsTimeKey = f(["RTS Time", "Thời gian đóng"], ["rtstime", "thoigiandong"])
        let buyerMessageKey = f(["Buyer Note", "Buyer Message", "Ghi chú của khách", "Ghi chú"], ["buyernote", "buyermessage", "ghichucuakhach", "ghichukhach", "ghichu"])

        let trackKey = returnLogisticsTrackKey ?? f(
            ["Mã vận đơn", "Tracking ID", "Mã vận đơn trả hàng", "Mã vận đơn giao hàng", "Return Logistics Tracking ID"],
            ["mavandon", "trackingid", "trackingnumber", "tracking", "mvd", "sovandon", "mabuugui"])
        let forwardTrackKey = f(["Tracking ID", "Mã vận đơn giao hàng", "Mã vận đơn"], ["forwardtracking", "mavandongiaohang"], ["return", "trahang"])
        let orderedQtyKey = f(["Quantity", "Số lượng"], ["orderedquantity"], ["return", "hoan"])
        let orderIdKey = f(["Order ID", "Mã đơn hàng", "ID đơn hàng"], ["orderid", "madonhang", "iddonhang", "madon"], ["masokhieunai", "makhieunai"])
        let carrierKey = f(["Shipping Provider Name", "Đơn vị vận chuyển", "Nhà vận chuyển"],
                           ["shippingprovidername", "donvivanchuyen", "shippingprovider", "nhavanchuyen", "dvvc", "carrier"])
        let productKey = f(["Product Name", "Tên sản phẩm trên sàn", "Tên sản phẩm"], ["productname", "tensanphamtrensan", "tensanpham", "tensp"], ["masanpham", "id", "masku", "skuid"])
        let varKey = f(["Variation", "SKU Name", "Tên phân loại trên sàn", "Tên phân loại"],
                       ["variation", "skuname", "tenphanloaitrensan", "tenphanloai", "phanloai", "tenbienthe"], ["maphanloai", "masanpham", "masku", "skuid"])
        let sellerSkuKey = f(["Seller SKU", "Mã sản phẩm Salework", "SKU phân loại"], ["sellersku", "masanphamsalework", "skuphanloai"])
        let skuIdKey = f(["SKU ID", "Sku ID", "Mã SKU"], ["skuid", "masku"])
        let qtyKey = f(["Return Quantity", "Quantity", "Số lượng", "Số lượng hoàn"], ["returnquantity", "quantity", "soluong", "qty"], ["tongsoluong"])

        let priceTongGiaTriKey = f(["Order Amount", "Order Total", "Tổng giá trị"], ["orderamount", "ordertotal", "tonggiatri"])
        let priceSauChietKhauKey = f(["SKU Subtotal After Discount", "Tổng tiền hàng sau chiết khấu"], ["skusubtotalafterdiscount", "tongtienhangsauchietkhau"])
        let priceGiaSanTmdtKey = f(["SKU Unit Original Price", "Giá sàn TMĐT", "Đơn giá"], ["skuunitoriginalprice", "giasantmdt"])
        let priceTongTienHangKey = f(["Tổng tiền hàng", "SKU Subtotal Before Discount"], ["tongtienhang"], ["sauchietkhau"])
        let priceGiaSaleworkKey = f(["Giá Salework"], ["giasalework"])
        let priceDoanhThuKey = f(["Doanh thu ước tính"], ["doanhthuuoctinh"])

        let shipDateKeys: [String] = [
            shippedTimeKey, rtsTimeKey, timeRequestedKey,
            f(["Thời gian xử lý"], ["thoigianxuly"]),
            f(["Ngày tạo đơn", "Created Time"], ["ngaytaodon", "createdtime"]),
        ].compactMap { $0 }
        let returnDateKeys: [String] = [
            timeRequestedKey, cancelledTimeKey, refundTimeKey,
            f(["Thời gian đóng"], ["thoigiandong"]),
            f(["Ngày tạo đơn"], ["ngaytaodon"]),
        ].compactMap { $0 }

        let statusKey = returnStatusKey ?? f(["Return Status", "Trạng thái đơn hàng", "Order Status"], ["returnstatus", "trangthaidonhang", "orderstatus", "trangthai"])
        let noteKey = returnReasonKey ?? f(["Return Reason", "Cancel Reason", "Lý do hủy", "Lý do"], ["returnreason", "cancelreason", "lydohuy", "lydo"])
        let preOrderKey = f(["Hàng đặt trước", "Pre-order", "Preorder", "Normal or Pre-order", "Loại đơn hàng"],
                            ["hangdattruoc", "dattruoc", "preorder", "normalorpreorder", "loaidonhang"])

        let isTikTokReturnRefundFile = returnOrderIdKey != nil || returnLogisticsTrackKey != nil

        var groups: [String: Group] = [:]
        var groupOrder: [String] = []
        var zeroReturned = Set<String>()
        var lastGroupKey = ""
        var lastSkippedRow = false
        var lastSourceFileId: String? = nil
        var hasLastSource = false

        let returnWords = #"\b(?:hoan|tra|return(?:ed|ing)?|refund(?:ed|ing)?)\b"#

        for row in rawRows {
            let sourceId = row["__sourceFileId"]
            if !hasLastSource || sourceId != lastSourceFileId {
                lastGroupKey = ""; lastSkippedRow = false; lastSourceFileId = sourceId; hasLastSource = true
            }

            // 1. tracking code
            var rawTrack = ""
            if trackKey != nil && has(row, trackKey) {
                rawTrack = SWText.cleanTrackingCode(SWText.normalizeScientificOrderId(get(row, trackKey)))
            }
            if rawTrack.isEmpty {
                for k in keys {
                    let nk = SWText.key(k)
                    if SWText.contains(nk, "mavandon") || SWText.contains(nk, "tracking") {
                        let cand = SWText.cleanTrackingCode(SWText.normalizeScientificOrderId(get(row, k)))
                        if !cand.isEmpty { rawTrack = cand; break }
                    }
                }
            }

            // 2. order id / return order id
            var rawOrderId = orderIdKey != nil ? SWText.normalizeScientificOrderId(get(row, orderIdKey)) : ""
            let rawReturnOrderId = returnOrderIdKey != nil ? SWText.normalizeScientificOrderId(get(row, returnOrderIdKey)) : ""
            if rawOrderId.isEmpty && !rawReturnOrderId.isEmpty { rawOrderId = rawReturnOrderId }

            let rawProd = SWText.trim(get(row, productKey))
            let rawVar = SWText.trim(get(row, varKey))
            let rawSellerSku = SWText.trim(get(row, sellerSkuKey))

            var isContinuation = false
            if rawTrack.isEmpty && rawOrderId.isEmpty && rawReturnOrderId.isEmpty {
                if !lastGroupKey.isEmpty && !lastSkippedRow && groups[lastGroupKey] != nil && (!rawProd.isEmpty || !rawVar.isEmpty || !rawSellerSku.isEmpty) {
                    isContinuation = true
                } else {
                    continue
                }
            }

            let rawStatusText = SWText.trim(get(row, statusKey))
            var rawSubStatusText = ""
            if returnSubStatusKey != nil && !get(row, returnSubStatusKey).isEmpty {
                rawSubStatusText = SWText.trim(get(row, returnSubStatusKey))
            } else if orderSubstatusKey != nil && !get(row, orderSubstatusKey).isEmpty {
                rawSubStatusText = SWText.trim(get(row, orderSubstatusKey))
            }
            let rawCancelReason = SWText.trim(get(row, noteKey))
            let rawCancelType = SWText.trim(get(row, returnTypeKey))
            let hasReturnQty = skuQuantityOfReturnKey != nil && has(row, skuQuantityOfReturnKey)
            let rawRetQty = try hasReturnQty ? SWText.parseQuantity(get(row, skuQuantityOfReturnKey)) : 0

            if hasReturnQty && rawRetQty == 0 {
                let skippedKey = isContinuation ? lastGroupKey : parcelKey(rawOrderId, rawTrack, rawReturnOrderId, "", "")
                zeroReturned.insert(skippedKey)
                lastGroupKey = skippedKey
                if let zg = groups[skippedKey] { zg.p.isPartialReturn = true }
                lastSkippedRow = false
                continue
            }
            let orderedQty = try orderedQtyKey != nil ? SWText.parseQuantity(get(row, orderedQtyKey)) : 0

            let stNorm = SWText.removeTones(rawStatusText)
            let subNorm = SWText.removeTones(rawSubStatusText)
            let reasonNorm = SWText.removeTones(rawCancelReason)
            let typeNorm = SWText.removeTones(rawCancelType)

            let isCancelledWord = SWText.isMatch(#"\bhuy\b"#, stNorm, ci: true) || SWText.isMatch(#"\bcancel(?:led|ed)?\b"#, stNorm, ci: true)
                || SWText.isMatch(#"\bhuy\b"#, subNorm, ci: true) || SWText.isMatch(#"\bcancel(?:led|ed)?\b"#, subNorm, ci: true)
                || SWText.contains(typeNorm, "cancel")
            let isFailedDelivery = SWText.contains(reasonNorm, "giao goi hang that bai") || SWText.contains(reasonNorm, "that bai") || SWText.contains(reasonNorm, "fail")
            let hasReturnIdentity = !rawReturnOrderId.isEmpty || (returnLogisticsTrackKey != nil && !SWText.trim(get(row, returnLogisticsTrackKey)).isEmpty)
            let saleworkKindNorm = saleworkKindKey != nil ? SWText.removeTones(get(row, saleworkKindKey)) : ""
            let isSaleworkPartial = SWText.contains(saleworkKindNorm, "mot phan") && !SWText.contains(SWText.removeTones(rawStatusText), "that thu")
            let isReturnOrRefund = SWText.isMatch(returnWords, stNorm, ci: true) || SWText.isMatch(returnWords, subNorm, ci: true)
                || rawRetQty > 0 || hasReturnIdentity || isSaleworkPartial
            let isReturnOrCancel = isFailedDelivery || isCancelledWord || isReturnOrRefund || SWText.contains(stNorm, "that thu")

            if !isContinuation {
                if (options.onlyReturns && !isReturnOrCancel) || (options.skipUnshippedCancel && rawTrack.isEmpty && isCancelledWord) {
                    result.skipped += 1
                    lastSkippedRow = true
                    continue
                }
                lastSkippedRow = false
            }

            let groupKey = isContinuation ? lastGroupKey : parcelKey(rawOrderId, rawTrack, rawReturnOrderId, "", "")
            if !isContinuation { lastGroupKey = groupKey }

            var rawQty = 1
            if qtyKey != nil && has(row, qtyKey) {
                let q = try SWText.parseQuantity(get(row, qtyKey))
                if q > 0 { rawQty = q }
            }
            if hasReturnQty && rawRetQty > 0 { rawQty = rawRetQty }

            var rawPreOrder = "Đơn thường"
            if preOrderKey != nil && has(row, preOrderKey) {
                let po = SWText.trim(SWText.removeTones(get(row, preOrderKey)))
                if SWText.contains(po, "dat truoc") || SWText.contains(po, "preorder") || SWText.contains(po, "pre-order")
                    || po == "co" || po == "yes" || po == "true" {
                    rawPreOrder = "Hàng đặt trước"
                }
            }

            let valUnit = returnUnitPriceKey != nil ? SWText.parseMoney(get(row, returnUnitPriceKey)) : 0
            let valZ = priceTongGiaTriKey != nil ? SWText.parseMoney(get(row, priceTongGiaTriKey)) : 0
            let valAM = priceSauChietKhauKey != nil ? SWText.parseMoney(get(row, priceSauChietKhauKey)) : 0
            let valK = priceGiaSanTmdtKey != nil ? SWText.parseMoney(get(row, priceGiaSanTmdtKey)) : 0
            let valAO = priceTongTienHangKey != nil ? SWText.parseMoney(get(row, priceTongTienHangKey)) : 0
            let valG = priceGiaSaleworkKey != nil ? SWText.parseMoney(get(row, priceGiaSaleworkKey)) : 0
            let valL = priceDoanhThuKey != nil ? SWText.parseMoney(get(row, priceDoanhThuKey)) : 0

            var rawShipDate = ""
            for dk in shipDateKeys where !get(row, dk).isEmpty {
                let ex = SWText.extractDate(get(row, dk), year)
                if !ex.isEmpty { rawShipDate = ex; break }
            }
            var rawReturnDate = ""
            for rk in returnDateKeys where !get(row, rk).isEmpty {
                let ex = SWText.extractDate(get(row, rk), year)
                if !ex.isEmpty { rawReturnDate = ex; break }
            }
            if rawReturnDate.isEmpty && !rawShipDate.isEmpty { rawReturnDate = rawShipDate }
            if rawShipDate.isEmpty && !rawReturnDate.isEmpty { rawShipDate = rawReturnDate }

            var rawNote = ""
            if !rawCancelReason.isEmpty { rawNote = translateReason(rawCancelReason) }
            else if !rawCancelType.isEmpty { rawNote = rawCancelType }

            var buyerComment = buyerMessageKey != nil && !get(row, buyerMessageKey).isEmpty
                ? SWText.trim(get(row, buyerMessageKey)) : SWText.extractBuyerNote(row)
            if SWText.isNoneNote(buyerComment) { buyerComment = "" }

            var rawPlatStatus = ""
            var rawReturnedAt = ""
            if isTikTokReturnRefundFile {
                rawPlatStatus = translateReturnStatus(rawStatusText, rawSubStatusText)
            } else if saleworkReturnTrackKey != nil {
                if saleworkReturnedKey != nil && !SWText.trim(get(row, saleworkReturnedKey)).isEmpty {
                    rawPlatStatus = "Đã về"
                } else {
                    rawPlatStatus = !SWText.trim(get(row, saleworkReturnTrackKey)).isEmpty ? "Đang về" : "Chờ khách gửi hàng"
                }
                rawReturnedAt = saleworkReturnedKey != nil ? SWText.trim(get(row, saleworkReturnedKey)) : ""
            } else if !rawCancelReason.isEmpty {
                rawPlatStatus = rawCancelReason
            } else {
                rawPlatStatus = rawStatusText + (rawSubStatusText.isEmpty ? "" : " - " + rawSubStatusText)
            }

            let orderType: String
            if isFailedDelivery {
                orderType = "Giao không thành công"
                result.failedDelivery += 1
            } else if isReturnOrRefund {
                orderType = SWText.contains(typeNorm, "refund only") || SWText.contains(typeNorm, "chi hoan") ? "Chỉ hoàn tiền" : "Trả hàng/Hoàn tiền"
            } else if isCancelledWord {
                orderType = "Đơn hủy"
            } else if SWText.contains(stNorm, "that thu") {
                orderType = "THẤT THOÁT"
                rawNote = !rawNote.isEmpty ? "THẤT THOÁT | \(rawNote)" : "THẤT THOÁT (Trạng thái Salework)"
            } else {
                orderType = "Đang chuyển hoàn / Vận chuyển"
            }

            let pv = SWText.parseProductAndVariation(rawProd, rawVar, rawSellerSku)
            if SWText.isMatch("premium", pv.product, ci: true) || SWText.isMatch("premium", pv.size, ci: true) { result.premiumItems += 1 }

            let carrierFromExcel = SWText.trim(get(row, carrierKey))
            var carrier = SWText.detectTikTokCarrierFromCode(rawTrack)
            if carrier.isEmpty { carrier = SWText.detectCarrierByTrackingCode(rawTrack) ?? "" }
            if carrier.isEmpty { carrier = carrierFromExcel }
            if carrier.isEmpty { carrier = !rawTrack.isEmpty ? "J&T Express" : "Chờ bưu cục tiếp nhận" }

            let isPartial = (hasReturnQty && rawRetQty > 0 && orderedQty > rawRetQty) || SWText.contains(typeNorm, "partial")
                || SWText.contains(reasonNorm, "mot phan") || isSaleworkPartial

            if groups[groupKey] == nil {
                let ng = Group()
                ng.p.orderType = orderType
                ng.p.isPartialReturn = isPartial
                ng.p.preOrder = rawPreOrder
                ng.p.orderId = rawOrderId
                ng.p.returnOrderId = rawReturnOrderId
                ng.p.track = rawTrack
                ng.p.carrier = carrier
                ng.p.shipDate = rawShipDate
                ng.p.returnDate = rawReturnDate
                ng.p.platformStatus = rawPlatStatus
                ng.p.returnedAt = rawReturnedAt
                ng.p.note = rawNote
                ng.p.buyerNote = buyerComment
                groups[groupKey] = ng
                groupOrder.append(groupKey)
                if !rawTrack.isEmpty { result.withTrack += 1 } else { result.emptyTrackCancel += 1 }
            } else {
                result.multiItemAppended += 1
            }

            guard let entry = groups[groupKey] else { continue }
            let p = entry.p
            let returnTrackSourceKey = returnLogisticsTrackKey ?? saleworkReturnTrackKey
            let returnTrack = returnTrackSourceKey != nil
                ? SWText.cleanTrackingCode(SWText.normalizeScientificOrderId(get(row, returnTrackSourceKey))) : ""
            if !returnTrack.isEmpty && p.returnTrack.isEmpty { p.returnTrack = returnTrack }
            let forwardTrack = forwardTrackKey != nil
                ? SWText.cleanTrackingCode(SWText.normalizeScientificOrderId(get(row, forwardTrackKey))) : ""
            if !forwardTrack.isEmpty && p.forwardTrack.isEmpty { p.forwardTrack = forwardTrack }
            p.trackingCodes = SWText.distinct((trackingCodesOf(p) + [rawTrack, forwardTrack, returnTrack] + parseImportedTrackingCodes(row))
                .filter { !$0.isEmpty })

            var lineMoney: [String: Int64] = [:]
            func collect(_ slot: String, _ key: String?, _ value: Int64, _ sum: Bool) {
                if key == nil || !has(row, key) { return }
                if sum {
                    lineMoney[slot] = value
                    entry.money[slot] = (entry.money[slot] ?? 0) &+ value
                } else if entry.money[slot] == nil {
                    entry.money[slot] = value
                }
            }
            let q64 = Int64(rawQty)
            collect("unitReturn", returnUnitPriceKey, valUnit &* q64, true)
            collect("total", priceTongGiaTriKey, valZ, false)
            collect("discounted", priceSauChietKhauKey, valAM, isSkuSubtotal(priceSauChietKhauKey))
            collect("originalUnit", priceGiaSanTmdtKey, valK &* q64, true)
            collect("subtotal", priceTongTienHangKey, valAO, isSkuSubtotal(priceTongTienHangKey))
            collect("saleworkUnit", priceGiaSaleworkKey, valG &* q64, true)
            collect("revenue", priceDoanhThuKey, valL, false)
            if isPartial { p.isPartialReturn = true }

            if rawPreOrder == "Hàng đặt trước" { p.preOrder = rawPreOrder }
            if !rawTrack.isEmpty && p.track.isEmpty {
                p.track = rawTrack
                result.withTrack += 1
                if result.emptyTrackCancel > 0 { result.emptyTrackCancel -= 1 }
            }
            if !rawOrderId.isEmpty && p.orderId.isEmpty { p.orderId = rawOrderId }
            if !rawReturnOrderId.isEmpty && p.returnOrderId.isEmpty { p.returnOrderId = rawReturnOrderId }
            if !rawShipDate.isEmpty && p.shipDate.isEmpty { p.shipDate = rawShipDate }
            if !rawReturnDate.isEmpty && p.returnDate.isEmpty { p.returnDate = rawReturnDate }
            if !rawPlatStatus.isEmpty && p.platformStatus.isEmpty { p.platformStatus = rawPlatStatus }
            if !rawReturnedAt.isEmpty && p.returnedAt.isEmpty { p.returnedAt = rawReturnedAt }
            if !rawNote.isEmpty && p.note.isEmpty { p.note = rawNote }
            if !buyerComment.isEmpty && p.buyerNote.isEmpty { p.buyerNote = buyerComment }
            if !carrier.isEmpty && p.carrier == "Chờ bưu cục tiếp nhận" { p.carrier = carrier }

            if !pv.product.isEmpty || !pv.color.isEmpty || !pv.size.isEmpty {
                var item = SaleworkItem()
                item.product = pv.product
                item.color = pv.color
                item.size = pv.size
                item.qty = rawQty
                item.importPrices = lineMoney
                item.returnedQty = (!p.returnOrderId.isEmpty || !p.returnTrack.isEmpty) ? rawQty : 0
                item.sellerSku = rawSellerSku.isEmpty ? nil : rawSellerSku
                item.skuId = skuIdKey != nil && has(row, skuIdKey) ? SWText.trim(get(row, skuIdKey)) : nil
                p.items.append(item)
            }
        }

        let baseSlots = ["unitReturn", "discounted", "total", "originalUnit", "subtotal"]
        let slotsByMode: [(String, [String])] = [
            ("tong_gia_tri", baseSlots),
            ("sau_chiet_khau", ["discounted"] + baseSlots),
            ("gia_salework", ["subtotal", "saleworkUnit"] + baseSlots),
            ("doanh_thu_uoc_tinh", ["revenue"] + baseSlots),
        ]

        for gk in groupOrder {
            guard let g = groups[gk] else { continue }
            let p = g.p
            let money = g.money
            if zeroReturned.contains(parcelKey(p.orderId, p.track, p.returnOrderId, p.returnTrack, p.forwardTrack)) { p.isPartialReturn = true }

            func m(_ slot: String) -> Int64? { money[slot] }
            p.priceHasValue = !money.isEmpty
            let priceVal = m("unitReturn") ?? m("discounted") ?? m("total") ?? m("originalUnit") ?? m("subtotal") ?? 0
            p.priceModes = [
                "tong_gia_tri": priceVal,
                "sau_chiet_khau": m("discounted") ?? priceVal,
                "gia_salework": m("subtotal") ?? m("saleworkUnit") ?? priceVal,
                "doanh_thu_uoc_tinh": m("revenue") ?? priceVal,
            ]
            p.priceModeFlags = [
                "hasOrderLevelZ": m("unitReturn") == nil && m("total") != nil,
                "hasOrderLevelAM": m("discounted") != nil && !isSkuSubtotal(priceSauChietKhauKey),
                "hasOrderLevelAO": m("subtotal") != nil && !isSkuSubtotal(priceTongTienHangKey),
                "hasOrderLevelL": m("revenue") != nil,
            ]
            p.price = p.priceModes[options.priceMode] ?? priceVal

            var slotsOut: [String: String?] = [:]
            for (mode, slots) in slotsByMode {
                guard let slot = slots.first(where: { money[$0] != nil }) else { continue }
                let orderLevel = slot == "total" || slot == "revenue"
                    || (slot == "discounted" && !isSkuSubtotal(priceSauChietKhauKey))
                    || (slot == "subtotal" && !isSkuSubtotal(priceTongTienHangKey))
                slotsOut.updateValue(orderLevel ? nil : slot, forKey: mode)
            }
            p.importModeSlots = slotsOut
            p.importSelectedPriceMode = options.priceMode
            if p.items.isEmpty {
                var placeholder = SaleworkItem()
                placeholder.qty = 1
                p.items.append(placeholder)
            }
            if p.priceHasValue { p.priceNeedsReview = false }
            result.parcels.append(p)
        }
        return result
    }
}
