import Foundation

/// Thống kê theo biến thể Màu / Size của một sản phẩm bị hoàn.
public struct ProductVariantStat: Identifiable, Equatable, Hashable {
    public var color: String
    public var size: String
    public var units: Int

    public var id: String { "\(color)|\(size)" }

    /// Nhãn hiển thị gọn: "Đen · L", "Đen", hoặc "L".
    public var label: String {
        let parts = [color, size].filter { !$0.isEmpty }
        return parts.isEmpty ? "Không phân loại" : parts.joined(separator: " · ")
    }

    public init(color: String, size: String, units: Int) {
        self.color = color
        self.size = size
        self.units = units
    }
}

/// Thống kê cho một sản phẩm bị hoàn (nhóm theo tên chuẩn hóa, tương đương PC `ProductStatistics`).
public struct ProductStatItem: Identifiable, Equatable, Hashable {
    public var key: String
    public var name: String
    public var units: Int
    public var orders: Int
    public var share: Double
    public var topReason: String
    public var shopeeUnits: Int
    public var tiktokUnits: Int
    public var lazadaUnits: Int
    public var otherUnits: Int
    public var variants: [ProductVariantStat]
    public var orderIds: [String]
    public var premiumUnits: Int
    public var standardUnits: Int

    public var id: String { key }

    public init(
        key: String,
        name: String,
        units: Int,
        orders: Int,
        share: Double,
        topReason: String = "",
        shopeeUnits: Int = 0,
        tiktokUnits: Int = 0,
        lazadaUnits: Int = 0,
        otherUnits: Int = 0,
        variants: [ProductVariantStat] = [],
        orderIds: [String] = [],
        premiumUnits: Int = 0,
        standardUnits: Int = 0
    ) {
        self.key = key
        self.name = name
        self.units = units
        self.orders = orders
        self.share = share
        self.topReason = topReason
        self.shopeeUnits = shopeeUnits
        self.tiktokUnits = tiktokUnits
        self.lazadaUnits = lazadaUnits
        self.otherUnits = otherUnits
        self.variants = variants
        self.orderIds = orderIds
        self.premiumUnits = premiumUnits
        self.standardUnits = standardUnits
    }
}

/// Kết quả tổng hợp thống kê sản phẩm bị hoàn.
public struct ProductStatsSummary: Equatable {
    public var products: [ProductStatItem] = []
    public var totalUnits: Int = 0
    public var ordersWithItems: Int = 0
    public var unknownUnits: Int = 0

    /// Dòng tóm tắt đầu trang giống bản máy tính: "N sản phẩm • M cái bị hoàn trong K đơn".
    public var summaryLine: String {
        ProductMetrics.summaryText(for: self)
    }

    public init(
        products: [ProductStatItem] = [],
        totalUnits: Int = 0,
        ordersWithItems: Int = 0,
        unknownUnits: Int = 0
    ) {
        self.products = products
        self.totalUnits = totalUnits
        self.ordersWithItems = ordersWithItems
        self.unknownUnits = unknownUnits
    }
}

/// Bộ lọc sàn cho màn Thống kê sản phẩm.
public enum ProductPlatformFilter: String, CaseIterable, Identifiable {
    case all = "Tất cả"
    case shopee = "Shopee"
    case tiktok = "TikTok"
    case lazada = "Lazada"

    public var id: String { rawValue }
}

/// Bộ lọc khoảng thời gian cho màn Thống kê sản phẩm.
public enum ProductDateRange: String, CaseIterable, Identifiable {
    case days7 = "7 ngày"
    case days30 = "30 ngày"
    case all = "Tất cả"

    public var id: String { rawValue }
}

/// Một dòng sản phẩm đã tách từ `OrderRow` để hiển thị trên màn Chi tiết đơn.
public struct OrderProductLine: Identifiable, Equatable {
    public var id: Int
    public var name: String
    public var color: String
    public var size: String
    public var isPartialContext: Bool
    public var isReturned: Bool

    public init(id: Int, name: String, color: String, size: String, isPartialContext: Bool, isReturned: Bool) {
        self.id = id
        self.name = name
        self.color = color
        self.size = size
        self.isPartialContext = isPartialContext
        self.isReturned = isReturned
    }
}

/// Logic thuần tính thống kê sản phẩm bị hoàn, khớp quy tắc `ProductStatistics.cs` trên PC v2.
public enum ProductMetrics {
    /// Gộp khoảng trắng thừa thành một dấu cách và cắt hai đầu.
    public static func clean(_ raw: String) -> String {
        raw.split(whereSeparator: { $0.isWhitespace }).joined(separator: " ")
    }

    /// Bỏ dấu tiếng Việt, đổi đ/Đ -> d, chuyển chữ thường (tương đương `LegacyText.RemoveTones`).
    public static func removeTones(_ raw: String) -> String {
        let replaced = raw
            .replacingOccurrences(of: "đ", with: "d")
            .replacingOccurrences(of: "Đ", with: "d")
        return replaced
            .folding(options: [.diacriticInsensitive, .caseInsensitive], locale: Locale(identifier: "vi_VN"))
            .lowercased()
    }

    /// Khóa nhóm tên sản phẩm: bỏ dấu, chữ thường, chỉ giữ ký tự `a-z0-9` (tương đương `LegacyText.Key`).
    public static func groupKey(_ raw: String) -> String {
        let base = removeTones(raw)
        var out = String()
        out.reserveCapacity(base.count)
        for scalar in base.unicodeScalars {
            let v = scalar.value
            if (v >= 97 && v <= 122) || (v >= 48 && v <= 57) {
                out.unicodeScalars.append(scalar)
            }
        }
        return out
    }

    /// Tách chuỗi nhiều giá trị phân cách bởi `" | "` (giữ nguyên vị trí phần tử, kể cả ô đầu/cuối rỗng như `" | Đen"`).
    public static func splitPipe(_ raw: String) -> [String] {
        if raw.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { return [] }
        return raw.components(separatedBy: " | ").map { clean($0) }
    }

    /// Dòng tóm tắt đầu trang giống bản PC (`StatisticsViewModel.ProductSummaryText`).
    public static func summaryText(for summary: ProductStatsSummary) -> String {
        if summary.totalUnits == 0 {
            return "Chưa có sản phẩm nào trong bộ lọc hiện tại."
        }
        var text = "\(summary.products.count) sản phẩm • \(summary.totalUnits) cái bị hoàn trong \(summary.ordersWithItems) đơn"
        if summary.unknownUnits > 0 {
            text += " • \(summary.unknownUnits) cái chưa rõ sản phẩm (không tính)"
        }
        return text
    }

    /// Regex khớp đuôi "- Premium Cao Cấp" / "Premium" ở cuối tên sản phẩm (tương đương PC `ProductStatistics.PremiumTail`).
    private static let premiumTailRegex: NSRegularExpression? = try? NSRegularExpression(
        pattern: #"\s*[-–—]?\s*premium(\s+cao\s+c[aấ]p)?\s*$"#,
        options: [.caseInsensitive]
    )

    /// Premium = tên (đã làm sạch), size hoặc màu chứa "premium" không phân biệt hoa/thường.
    public static func isPremium(name: String, color: String = "", size: String = "") -> Bool {
        let cleanedName = clean(name)
        return cleanedName.range(of: "premium", options: .caseInsensitive) != nil
            || size.range(of: "premium", options: .caseInsensitive) != nil
            || color.range(of: "premium", options: .caseInsensitive) != nil
    }

    /// Bỏ đuôi "- Premium Cao Cấp" để hai bản của cùng một mẫu gộp thành một dòng; nếu bỏ xong rỗng thì giữ nguyên tên gốc.
    public static func stripPremium(_ name: String) -> String {
        let cleaned = clean(name)
        guard !cleaned.isEmpty, let regex = premiumTailRegex else { return cleaned }
        let nfc = cleaned.precomposedStringWithCanonicalMapping
        let range = NSRange(nfc.startIndex..., in: nfc)
        let stripped = regex
            .stringByReplacingMatches(in: nfc, options: [], range: range, withTemplate: "")
            .trimmingCharacters(in: .whitespacesAndNewlines)
        return stripped.isEmpty ? cleaned : stripped
    }

    /// Sản phẩm rỗng hoặc giữ chỗ "Chưa rõ sản phẩm..." không tính vào danh sách xếp hạng.
    public static func isUnknownProduct(_ raw: String) -> Bool {
        let cleaned = clean(raw)
        if cleaned.isEmpty { return true }
        let noTone = removeTones(cleaned)
        if noTone.hasPrefix("chua ro san pham") { return true }
        return groupKey(cleaned).isEmpty
    }

    /// Tách các dòng sản phẩm của một `OrderRow` kèm màu/size song song (nếu số phần tử khớp) và cờ hoàn một phần.
    public static func productLines(for row: OrderRow) -> [OrderProductLine] {
        let names = splitPipe(row.n).filter { !$0.isEmpty }
        guard !names.isEmpty else { return [] }

        let returnedRaw = splitPipe(row.rn).filter { !$0.isEmpty }
        let isPartial = !returnedRaw.isEmpty
        let returnedKeys = Set(returnedRaw.map { groupKey($0) }.filter { !$0.isEmpty })

        let colors = splitPipe(row.co)
        let sizes = splitPipe(row.sz)
        let colorsAligned = !colors.isEmpty && colors.count == names.count
        let sizesAligned = !sizes.isEmpty && sizes.count == names.count

        return names.enumerated().map { idx, name in
            let color = colorsAligned ? colors[idx] : ""
            let size = sizesAligned ? sizes[idx] : ""
            let returned = !isPartial || returnedKeys.contains(groupKey(name))
            return OrderProductLine(
                id: idx,
                name: name,
                color: color,
                size: size,
                isPartialContext: isPartial,
                isReturned: returned
            )
        }
    }

    /// Kiểm tra danh sách đơn có ít nhất một dòng đọc được ngày (`rd` hoặc `rcd`) hay không.
    public static func hasAnyDate(rows: [OrderRow], calendar: Calendar = .current) -> Bool {
        for r in rows {
            if OrderStats.parseDate(r.rd, calendar: calendar) != nil { return true }
            if OrderStats.parseDate(r.rcd, calendar: calendar) != nil { return true }
        }
        return false
    }

    /// Lọc danh sách đơn theo sàn và khoảng thời gian (7 ngày / 30 ngày / Tất cả).
    public static func filter(
        rows: [OrderRow],
        platform: ProductPlatformFilter = .all,
        dateRange: ProductDateRange = .all,
        now: Date = Date(),
        calendar: Calendar = .current
    ) -> [OrderRow] {
        let todayStart = calendar.startOfDay(for: now)
        let cutoff: Date?
        switch dateRange {
        case .all:
            cutoff = nil
        case .days7:
            cutoff = calendar.date(byAdding: .day, value: -6, to: todayStart)
        case .days30:
            cutoff = calendar.date(byAdding: .day, value: -29, to: todayStart)
        }

        return rows.filter { row in
            if platform != .all {
                if DashboardMetrics.normalizedPlatform(row.p) != platform.rawValue {
                    return false
                }
            }
            if let cutoff = cutoff {
                guard let d = OrderStats.parseDate(row.rd, calendar: calendar)
                    ?? OrderStats.parseDate(row.rcd, calendar: calendar) else {
                    return false
                }
                let day = calendar.startOfDay(for: d)
                if day < cutoff { return false }
            }
            return true
        }
    }

    /// Tính thống kê sản phẩm sau khi áp bộ lọc sàn và khoảng thời gian.
    public static func compute(
        rows: [OrderRow],
        platform: ProductPlatformFilter,
        dateRange: ProductDateRange,
        now: Date = Date(),
        calendar: Calendar = .current
    ) -> ProductStatsSummary {
        let filtered = filter(rows: rows, platform: platform, dateRange: dateRange, now: now, calendar: calendar)
        return compute(rows: filtered)
    }

    /// Tính thống kê sản phẩm thuần từ danh sách `OrderRow`.
    public static func compute(rows: [OrderRow]) -> ProductStatsSummary {
        var groups: [String: Accumulator] = [:]
        var totalUnits = 0
        var unknownUnits = 0
        var ordersWithItems = 0

        for row in rows {
            let names = splitPipe(row.n).filter { !$0.isEmpty }
            let rowQty = row.q > 0 ? row.q : 1
            if names.isEmpty {
                unknownUnits += rowQty
                continue
            }

            let returnedRaw = splitPipe(row.rn).filter { !$0.isEmpty }
            let isPartial = !returnedRaw.isEmpty
            let returnedKeys = Set(returnedRaw.map { groupKey($0) }.filter { !$0.isEmpty })

            let colors = splitPipe(row.co)
            let sizes = splitPipe(row.sz)
            // Chỉ ghép màu/size song song khi số phần tử khớp với danh sách sản phẩm; lệch thì bỏ qua, không đoán.
            let colorsAligned = !colors.isEmpty && colors.count == names.count
            let sizesAligned = !sizes.isEmpty && sizes.count == names.count

            let plat = DashboardMetrics.normalizedPlatform(row.p)
            let reason = clean(row.nt)
            let validReason = (!reason.isEmpty && reason.count <= 120) ? reason : ""

            let unitsPerItem = names.count == 1 ? rowQty : 1
            var countedOrder = false

            for (idx, rawName) in names.enumerated() {
                if isPartial {
                    let rawKey = groupKey(rawName)
                    if !returnedKeys.contains(rawKey) { continue }
                }

                let color = colorsAligned ? colors[idx] : ""
                let size = sizesAligned ? sizes[idx] : ""
                let premium = isPremium(name: rawName, color: color, size: size)
                let mergedName = stripPremium(rawName)

                if isUnknownProduct(mergedName) {
                    unknownUnits += unitsPerItem
                    continue
                }

                let key = groupKey(mergedName)
                if key.isEmpty {
                    unknownUnits += unitsPerItem
                    continue
                }

                var acc = groups[key] ?? Accumulator(key: key)
                acc.add(
                    spelling: mergedName,
                    units: unitsPerItem,
                    orderId: row.id,
                    platform: plat,
                    reason: validReason,
                    color: color,
                    size: size,
                    isPremium: premium
                )
                groups[key] = acc
                totalUnits += unitsPerItem
                countedOrder = true
            }

            if countedOrder {
                ordersWithItems += 1
            }
        }

        let products = groups.values
            .map { $0.toStat(totalUnits: totalUnits) }
            .sorted { a, b in
                if a.units != b.units { return a.units > b.units }
                return a.name.localizedCaseInsensitiveCompare(b.name) == .orderedAscending
            }

        return ProductStatsSummary(
            products: products,
            totalUnits: totalUnits,
            ordersWithItems: ordersWithItems,
            unknownUnits: unknownUnits
        )
    }

    /// Lấy danh sách `OrderRow` đóng góp vào một nhóm sản phẩm (`key`), giữ nguyên thứ tự gốc.
    public static func orders(forProductKey key: String, in rows: [OrderRow]) -> [OrderRow] {
        guard !key.isEmpty else { return [] }
        return rows.filter { row in
            let names = splitPipe(row.n).filter { !$0.isEmpty }
            guard !names.isEmpty else { return false }
            let returnedRaw = splitPipe(row.rn).filter { !$0.isEmpty }
            let isPartial = !returnedRaw.isEmpty
            let returnedKeys = Set(returnedRaw.map { groupKey($0) }.filter { !$0.isEmpty })

            for rawName in names {
                if isPartial && !returnedKeys.contains(groupKey(rawName)) { continue }
                let mergedName = stripPremium(rawName)
                if isUnknownProduct(mergedName) { continue }
                let k = groupKey(mergedName)
                if k == key { return true }
            }
            return false
        }
    }

    // MARK: - Bộ gom nội bộ
    private struct VariantKey: Hashable {
        let color: String
        let size: String
    }

    private struct Accumulator {
        let key: String
        var spellings: [String: Int] = [:]
        var orderIdSet: Set<String> = []
        var orderIds: [String] = []
        var reasons: [String: Int] = [:]
        var reasonDisplay: [String: String] = [:]
        var variants: [VariantKey: Int] = [:]
        var units = 0
        var premiumUnits = 0
        var shopeeUnits = 0
        var tiktokUnits = 0
        var lazadaUnits = 0
        var otherUnits = 0

        mutating func add(
            spelling: String,
            units u: Int,
            orderId: String,
            platform: String,
            reason: String,
            color: String,
            size: String,
            isPremium: Bool
        ) {
            spellings[spelling, default: 0] += 1
            units += u
            if isPremium { premiumUnits += u }

            if orderIdSet.insert(orderId).inserted {
                orderIds.append(orderId)
            }

            if !reason.isEmpty {
                let rk = reason.lowercased()
                reasons[rk, default: 0] += 1
                if reasonDisplay[rk] == nil { reasonDisplay[rk] = reason }
            }

            if !color.isEmpty || !size.isEmpty {
                variants[VariantKey(color: color, size: size), default: 0] += u
            }

            switch platform {
            case "Shopee": shopeeUnits += u
            case "TikTok": tiktokUnits += u
            case "Lazada": lazadaUnits += u
            default: otherUnits += u
            }
        }

        func toStat(totalUnits: Int) -> ProductStatItem {
            let bestName = spellings
                .sorted { a, b in
                    if a.value != b.value { return a.value > b.value }
                    return a.key < b.key
                }
                .first?.key ?? key

            let bestReasonKey = reasons
                .sorted { a, b in
                    if a.value != b.value { return a.value > b.value }
                    return a.key < b.key
                }
                .first?.key
            let topReason = bestReasonKey.flatMap { reasonDisplay[$0] } ?? ""

            let variantList = variants
                .map { ProductVariantStat(color: $0.key.color, size: $0.key.size, units: $0.value) }
                .sorted { a, b in
                    if a.units != b.units { return a.units > b.units }
                    let cmpColor = a.color.localizedCaseInsensitiveCompare(b.color)
                    if cmpColor != .orderedSame { return cmpColor == .orderedAscending }
                    return a.size.localizedCaseInsensitiveCompare(b.size) == .orderedAscending
                }

            let share = totalUnits == 0 ? 0.0 : Double(units) / Double(totalUnits)

            return ProductStatItem(
                key: key,
                name: bestName,
                units: units,
                orders: orderIdSet.count,
                share: share,
                topReason: topReason,
                shopeeUnits: shopeeUnits,
                tiktokUnits: tiktokUnits,
                lazadaUnits: lazadaUnits,
                otherUnits: otherUnits,
                variants: variantList,
                orderIds: orderIds,
                premiumUnits: premiumUnits,
                standardUnits: units - premiumUnits
            )
        }
    }
}
