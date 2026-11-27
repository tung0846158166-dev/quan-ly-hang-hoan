import Foundation

// Chủ dự án 27/11/2026: bảng ĐƠN NGOÀI SÀN (Facebook, Zalo, tại shop...) tạo nhanh từ dữ liệu sản phẩm sẵn có,
// và tra nhanh một sản phẩm: bao nhiêu cái về kho / bị hoàn trong hôm nay, tuần này, tháng này.
// Cùng quy ước với cloud (neworder.ts) và máy tính (Core.Models.OffPlatform, Core.Stats.ProductLookup).

public enum OffPlatform {
    public static let name = "Ngoài sàn"
    public static let orderTypes = ["Trả hàng/Hoàn tiền", "Đổi hàng", "Giao không thành công"]
    public static let channels = ["Salework", "Facebook", "Zalo", "Tại shop", "Website", "Instagram", "Khác"]

    public static func isOffPlatform(_ platform: String) -> Bool {
        platform.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() == name.lowercased()
    }
    public static func isOffPlatform(_ row: OrderRow) -> Bool { isOffPlatform(row.p) }

    /// Kênh bán đọc lại từ cột trạng thái sàn ("Kênh: Zalo" -> "Zalo").
    public static func channel(of row: OrderRow) -> String {
        row.ps.hasPrefix("Kênh: ") ? String(row.ps.dropFirst(6)).trimmingCharacters(in: .whitespaces) : ""
    }

    /// "350000", "350.000", "350k", "1,5tr" -> số đồng; nil nếu không đọc được; trống = 0.
    public static func parsePrice(_ raw: String) -> Int? {
        var t = raw.lowercased().replacingOccurrences(of: " ", with: "").replacingOccurrences(of: "đ", with: "").replacingOccurrences(of: "vnd", with: "")
        if t.isEmpty { return 0 }
        var mult = 1.0
        if t.hasSuffix("triệu") { mult = 1_000_000; t = String(t.dropLast(5)) }
        else if t.hasSuffix("tr") { mult = 1_000_000; t = String(t.dropLast(2)) }
        else if t.hasSuffix("k") || t.hasSuffix("n") { mult = 1_000; t = String(t.dropLast()) }
        if t.isEmpty { return nil }
        if mult > 1 {
            guard let d = Double(t.replacingOccurrences(of: ",", with: ".")), d >= 0 else { return nil }
            return Int((d * mult).rounded())
        }
        let digits = t.replacingOccurrences(of: ".", with: "").replacingOccurrences(of: ",", with: "")
        guard !digits.isEmpty, digits.allSatisfy({ $0.isASCII && $0.isNumber }) else { return nil }
        return Int(digits)
    }
}

/// Một dòng sản phẩm của đơn tạo tay.
public struct NewOrderLine: Equatable, Identifiable {
    public var id = UUID()
    public var product: String
    public var color: String
    public var size: String
    public var qty: Int
    public init(product: String = "", color: String = "", size: String = "", qty: Int = 1) {
        self.product = product; self.color = color; self.size = size; self.qty = qty
    }
    public var isBlank: Bool {
        product.trimmingCharacters(in: .whitespaces).isEmpty && color.trimmingCharacters(in: .whitespaces).isEmpty && size.trimmingCharacters(in: .whitespaces).isEmpty
    }
}

extension CloudOps {
    /// Tạo đơn ngoài sàn: mã trống thì cloud tự sinh mã "NS..."; KHÔNG có thông tin người mua.
    public static func offPlatformOrder(code: String, orderId: String = "", lines: [NewOrderLine], price: Int, orderType: String, channel: String, note: String) -> [String: Any] {
        let items: [[String: Any]] = lines.filter { !$0.isBlank }.map {
            ["product": $0.product.trimmingCharacters(in: .whitespacesAndNewlines), "color": $0.color.trimmingCharacters(in: .whitespacesAndNewlines),
             "size": $0.size.trimmingCharacters(in: .whitespacesAndNewlines), "qty": max(1, min(9999, $0.qty))]
        }
        var op: [String: Any] = ["type": "newOrder", "code": code.trimmingCharacters(in: .whitespacesAndNewlines), "platform": OffPlatform.name,
                                 "orderId": orderId.trimmingCharacters(in: .whitespacesAndNewlines),
                                 "origin": "phone", "items": items, "orderType": orderType, "channel": channel.trimmingCharacters(in: .whitespacesAndNewlines),
                                 "note": note.trimmingCharacters(in: .whitespacesAndNewlines)]
        if price > 0 { op["price"] = price }
        return op
    }

    /// Lỗi nhập của form đơn ngoài sàn; nil = hợp lệ.
    public static func offPlatformError(lines: [NewOrderLine], priceText: String) -> String? {
        let filled = lines.filter { !$0.isBlank }
        if filled.isEmpty { return "Hãy thêm ít nhất một sản phẩm." }
        if filled.contains(where: { $0.product.trimmingCharacters(in: .whitespaces).isEmpty }) { return "Có dòng chưa ghi tên sản phẩm." }
        if filled.contains(where: { $0.qty < 1 || $0.qty > 9999 }) { return "Số lượng phải từ 1 đến 9999." }
        if OffPlatform.parsePrice(priceText) == nil { return "Giá trị đơn không đọc được (ví dụ: 350000, 350k, 1,2tr)." }
        return nil
    }
}

extension CloudSyncClient {
    /// Gửi một lệnh tạo đơn bất kỳ (đơn ngoài sàn). Mất kết nối thì từ chối ngay.
    public func createOrder(_ op: [String: Any]) async -> CloudNewOrderResult {
        guard canWrite, let a = currentAPI else { return CloudNewOrderResult(outcome: "error", message: "Mất kết nối cloud: chưa tạo được đơn. Kiểm tra mạng rồi thử lại.") }
        return await CloudSyncClient.performCreateOrder(op, api: a, refresh: { [weak self] in
            guard let self else { return }
            try await self.pull(a)
        })
    }

    /// Phần lõi của `createOrder` (tách ra để test không cần WebSocket).
    public static func performCreateOrder(_ op: [String: Any], api: CloudAPI, refresh: () async throws -> Void) async -> CloudNewOrderResult {
        do {
            let r = try await api.op(op)
            guard r.ok else { return CloudNewOrderResult(outcome: "error", message: r.errorMessage ?? "Cloud từ chối tạo đơn.") }
            let x = r.result ?? [:]
            let res = CloudNewOrderResult(outcome: x["outcome"] as? String ?? "error", message: x["message"] as? String ?? "",
                                          platform: x["platform"] as? String ?? "", orderId: x["orderId"] as? String ?? "", code: x["code"] as? String ?? "")
            if res.outcome == "success" { try? await refresh() }
            return res
        } catch let e as CloudAPIError {
            return CloudNewOrderResult(outcome: "error", message: e.isNetwork ? "Mất kết nối cloud giữa chừng: chưa chắc đã tạo. Tìm lại đơn trước khi tạo lại." : e.message)
        } catch {
            return CloudNewOrderResult(outcome: "error", message: "Không tạo được đơn. Thử lại.")
        }
    }
}

// MARK: - Danh mục sản phẩm (gợi ý khi tạo đơn)

/// Một sản phẩm đã có trong dữ liệu đơn: tên hay dùng nhất + màu / size đã gặp (nhiều nhất trước).
public struct CatalogProduct: Identifiable, Equatable, Hashable {
    public var key: String
    public var name: String
    public var units: Int
    public var colors: [String]
    public var sizes: [String]
    public var id: String { key }
}

public enum ProductCatalog {
    /// Dựng danh mục từ mọi đơn (bỏ sản phẩm giữ chỗ "Chưa rõ sản phẩm" / "Sản phẩm mới (...)").
    public static func build(rows: [OrderRow]) -> [CatalogProduct] {
        struct Acc { var names: [String: Int] = [:]; var colors: [String: Int] = [:]; var sizes: [String: Int] = [:]; var units = 0 }
        var map: [String: Acc] = [:]
        for r in rows {
            let lines = ProductMetrics.productLines(for: r)
            let unitsPer = lines.count == 1 ? max(1, r.q) : 1
            for l in lines {
                let name = ProductMetrics.clean(l.name)
                if ProductMetrics.isUnknownProduct(name) || name.hasPrefix("Sản phẩm mới (") { continue }
                let key = ProductMetrics.groupKey(name)
                if key.isEmpty { continue }
                var a = map[key] ?? Acc()
                a.names[name, default: 0] += 1
                let c = ProductMetrics.clean(l.color), s = ProductMetrics.clean(l.size)
                if !c.isEmpty { a.colors[c, default: 0] += unitsPer }
                if !s.isEmpty { a.sizes[s, default: 0] += unitsPer }
                a.units += unitsPer
                map[key] = a
            }
        }
        func top(_ d: [String: Int]) -> [String] {
            Array(d.sorted { $0.value != $1.value ? $0.value > $1.value : $0.key < $1.key }.map { $0.key }.prefix(30))
        }
        return map.map { key, a in
            CatalogProduct(key: key, name: a.names.sorted { $0.value != $1.value ? $0.value > $1.value : $0.key < $1.key }.first?.key ?? key,
                           units: a.units, colors: top(a.colors), sizes: top(a.sizes))
        }.sorted { $0.units != $1.units ? $0.units > $1.units : $0.name < $1.name }
    }

    /// Các từ khóa (bỏ dấu) của câu tìm.
    public static func terms(_ query: String) -> [String] {
        query.split(whereSeparator: { $0.isWhitespace }).map { ProductMetrics.groupKey(String($0)) }.filter { !$0.isEmpty }
    }

    public static func matches(_ key: String, _ terms: [String]) -> Bool { !terms.isEmpty && terms.allSatisfy { key.contains($0) } }

    /// Sản phẩm khớp mọi từ (không phân biệt dấu); tên bắt đầu bằng câu tìm lên trước, rồi nhiều cái nhất.
    public static func search(_ products: [CatalogProduct], query: String, limit: Int = 12) -> [CatalogProduct] {
        let t = terms(query)
        if t.isEmpty { return Array(products.prefix(limit)) }
        let whole = ProductMetrics.groupKey(query)
        let hits = products.filter { matches($0.key, t) }
        let sorted = hits.enumerated().sorted { a, b in
            let pa = a.element.key.hasPrefix(whole) ? 0 : 1, pb = b.element.key.hasPrefix(whole) ? 0 : 1
            if pa != pb { return pa < pb }
            if a.element.units != b.element.units { return a.element.units > b.element.units }
            return a.offset < b.offset
        }.map { $0.element }
        return Array(sorted.prefix(limit))
    }

    public static func find(_ products: [CatalogProduct], name: String) -> CatalogProduct? {
        let k = ProductMetrics.groupKey(name)
        return k.isEmpty ? nil : products.first { $0.key == k }
    }
}

// MARK: - Tra nhanh sản phẩm theo kỳ

/// Số cái: hôm nay, tuần này (từ thứ 2), tháng này, tất cả.
public struct PeriodUnits: Equatable {
    public var today = 0, week = 0, month = 0, total = 0
    public init(today: Int = 0, week: Int = 0, month: Int = 0, total: Int = 0) { self.today = today; self.week = week; self.month = month; self.total = total }
}

/// `received`: đã VỀ KHO (theo ngày nhận kho). `returned`: bị hoàn (theo ngày hoàn, kể cả chưa về).
public struct ProductLookupItem: Identifiable, Equatable {
    public var key: String
    public var name: String
    public var received: PeriodUnits
    public var returned: PeriodUnits
    public var orders: Int
    public var id: String { key }
}

extension ProductMetrics {
    /// Các dòng sản phẩm ĐƯỢC TÍNH của một đơn (cùng quy tắc `compute`): hoàn một phần chỉ tính sản phẩm đã hoàn; bỏ đuôi Premium; bỏ "Chưa rõ".
    public static func countedItems(_ row: OrderRow) -> [(key: String, name: String, units: Int)] {
        let names = splitPipe(row.n).filter { !$0.isEmpty }
        if names.isEmpty { return [] }
        let returnedRaw = splitPipe(row.rn).filter { !$0.isEmpty }
        let returnedKeys = Set(returnedRaw.map { groupKey($0) }.filter { !$0.isEmpty })
        let unitsPer = names.count == 1 ? (row.q > 0 ? row.q : 1) : 1
        var out: [(key: String, name: String, units: Int)] = []
        for raw in names {
            if !returnedRaw.isEmpty && !returnedKeys.contains(groupKey(raw)) { continue }
            let name = stripPremium(raw)
            if isUnknownProduct(name) { continue }
            let key = groupKey(name)
            if key.isEmpty { continue }
            out.append((key, name, unitsPer))
        }
        return out
    }

    /// Thứ 2 của tuần chứa ngày `d`.
    public static func weekStart(_ d: Date, calendar: Calendar) -> Date {
        let day = calendar.startOfDay(for: d)
        let wd = calendar.component(.weekday, from: day)   // 1 = CN ... 7 = T7
        return calendar.date(byAdding: .day, value: -((wd + 5) % 7), to: day) ?? day
    }

    /// Gõ tên sản phẩm -> số cái về kho / bị hoàn hôm nay, tuần này, tháng này (mọi đơn trong `rows`, kể cả lưu trữ).
    public static func lookup(rows: [OrderRow], query: String, now: Date = Date(), calendar: Calendar = ProductMetrics.vietnamCalendar, limit: Int = 8) -> [ProductLookupItem] {
        let t = ProductCatalog.terms(query)
        if t.isEmpty { return [] }
        let today = calendar.startOfDay(for: now)
        let week = weekStart(now, calendar: calendar)
        let month = calendar.date(from: calendar.dateComponents([.year, .month], from: today)) ?? today
        struct Acc { var names: [String: Int] = [:]; var orders = Set<String>(); var rec = PeriodUnits(); var ret = PeriodUnits() }
        var map: [String: Acc] = [:]
        func add(_ p: inout PeriodUnits, _ d: Date?, _ units: Int, countTotal: Bool) {
            if countTotal { p.total += units }
            guard let d else { return }
            let day = calendar.startOfDay(for: d)
            if day > today { return }
            if day == today { p.today += units }
            if day >= week { p.week += units }
            if day >= month { p.month += units }
        }
        for r in rows {
            let items = countedItems(r).filter { ProductCatalog.matches($0.key, t) }
            if items.isEmpty { continue }
            let recv = r.rv ? OrderStats.parseDate(r.rcd, calendar: calendar) : nil
            let ret = OrderStats.parseDate(r.rd, calendar: calendar)
            for it in items {
                var a = map[it.key] ?? Acc()
                a.names[it.name, default: 0] += 1
                a.orders.insert(r.id)
                if recv != nil { add(&a.rec, recv, it.units, countTotal: true) }
                add(&a.ret, ret, it.units, countTotal: true)
                map[it.key] = a
            }
        }
        let items = map.map { key, a in
            ProductLookupItem(key: key, name: a.names.sorted { $0.value != $1.value ? $0.value > $1.value : $0.key < $1.key }.first?.key ?? key,
                              received: a.rec, returned: a.ret, orders: a.orders.count)
        }.sorted {
            if $0.received.month != $1.received.month { return $0.received.month > $1.received.month }
            if $0.returned.month != $1.returned.month { return $0.returned.month > $1.returned.month }
            if $0.received.total + $0.returned.total != $1.received.total + $1.returned.total { return $0.received.total + $0.returned.total > $1.received.total + $1.returned.total }
            return $0.name < $1.name
        }
        return Array(items.prefix(max(1, limit)))
    }
}

// MARK: - Hàng hoàn sẵn kho (chủ dự án 27/11/2026)

/// Một dòng hàng sẵn kho: sản phẩm + màu + size, tổng số cái, các kiện chứa nó (mới nhận trước).
public struct ReceivedStockItem: Identifiable, Equatable {
    public var key: String
    public var product: String
    public var color: String
    public var size: String
    public var units: Int
    public var lastReceived: Date
    /// (kiện, số cái, lúc nhận) — mới nhận trước.
    public var parcels: [ReceivedStockParcel]
    public var id: String { key }
}

public struct ReceivedStockParcel: Equatable, Identifiable {
    public var row: OrderRow
    public var units: Int
    public var receivedAt: Date
    public var id: String { row.id }
}

public enum ReceivedStock {
    /// Sản phẩm của các kiện hoàn ĐÃ NHẬN tại kho từ `since`, gom theo sản phẩm + màu + size. Có đơn mới trùng sản phẩm thì lấy luôn
    /// hàng hoàn đóng đơn, khỏi cất kệ. Tìm theo tên, màu, size (không dấu cũng được).
    public static func build(rows: [OrderRow], since: Date, query: String = "", calendar: Calendar = ProductMetrics.vietnamCalendar) -> [ReceivedStockItem] {
        let terms = ProductCatalog.terms(query)
        var map: [String: ReceivedStockItem] = [:]
        for r in rows where r.rv {
            guard let at = OrderStats.parseDate(r.rcd, calendar: calendar), at >= since else { continue }
            let lines = ProductMetrics.productLines(for: r)
            let unitsPer = lines.count == 1 ? max(1, r.q) : 1
            for l in lines where l.isReturned {
                let name = ProductMetrics.clean(l.name)
                if ProductMetrics.isUnknownProduct(name) { continue }
                let nk = ProductMetrics.groupKey(name), ck = ProductMetrics.groupKey(l.color), sk = ProductMetrics.groupKey(l.size)
                if nk.isEmpty { continue }
                if !terms.isEmpty && !terms.allSatisfy({ (nk + ck + "|" + sk).contains($0) }) { continue }
                let key = "\(nk)|\(ck)|\(sk)"
                var item = map[key] ?? ReceivedStockItem(key: key, product: name, color: ProductMetrics.clean(l.color), size: ProductMetrics.clean(l.size),
                                                         units: 0, lastReceived: at, parcels: [])
                item.units += unitsPer
                if let i = item.parcels.firstIndex(where: { $0.row.id == r.id }) { item.parcels[i].units += unitsPer }
                else { item.parcels.append(ReceivedStockParcel(row: r, units: unitsPer, receivedAt: at)) }
                if at > item.lastReceived { item.lastReceived = at }
                map[key] = item
            }
        }
        return map.values.map { var x = $0; x.parcels.sort { $0.receivedAt > $1.receivedAt }; return x }
            .sorted { a, b in
                if a.product.lowercased() != b.product.lowercased() { return a.product.lowercased() < b.product.lowercased() }
                if a.color != b.color { return a.color < b.color }
                return a.size < b.size
            }
    }
}
