import Foundation

/// Đơn liên quan trong câu trả lời của AI, để giao diện cho chạm mở chi tiết đơn.
public struct AIOrderRef: Equatable, Identifiable {
    public let id: String
    public let orderCode: String
    public let track: String
    public let status: String
    public init(id: String, orderCode: String, track: String, status: String) {
        self.id = id; self.orderCode = orderCode; self.track = track; self.status = status
    }
}

public struct AIToolResult {
    public let json: String
    public let refs: [AIOrderRef]
    public init(json: String, refs: [AIOrderRef] = []) { self.json = json; self.refs = refs }
}

/// Công cụ CHỈ ĐỌC cho trợ lý AI, chạy trên danh sách đơn PC đã gửi sang điện thoại. Số liệu do mã tính, AI chỉ diễn đạt lại.
/// Quyền riêng tư: chỉ trả về các trường trong danh sách trắng (không có tên/SĐT/địa chỉ người nhận trong `OrderRow`),
/// và ghi chú còn bị che mọi dãy giống số điện thoại.
public final class AIOrderTools {
    private let rows: () -> [OrderRow]
    private let now: () -> Date
    private let calendar: Calendar
    private var seen: [String: OrderRow] = [:]
    private var cache: [OrderRow]?

    public init(rows: @escaping () -> [OrderRow], now: @escaping () -> Date = { Date() }, calendar: Calendar = .current) {
        self.rows = rows
        self.now = now
        self.calendar = calendar
    }

    public func row(forId id: String) -> OrderRow? { seen[id] }

    /// Thả bộ nhớ đệm sau mỗi câu hỏi.
    public func releaseCache() { cache = nil }

    private func load() -> [OrderRow] {
        if let c = cache { return c }
        let r = rows()
        cache = r
        return r
    }

    // MARK: che số điện thoại

    /// Che dãy giống số điện thoại trong văn bản tự do. Mã vận đơn có chữ không bị che.
    public static func maskPhones(_ text: String) -> String {
        if text.isEmpty { return "" }
        var t = text
        if let re = try? NSRegularExpression(pattern: #"(\+?84|0)[\s.\-]?\d(?:[\s.\-]?\d){8,9}"#) {
            t = re.stringByReplacingMatches(in: t, range: NSRange(t.startIndex..., in: t), withTemplate: "[ẩn SĐT]")
        }
        if let re = try? NSRegularExpression(pattern: #"(?<![A-Za-z0-9])0\d{8,11}(?![A-Za-z0-9])"#) {
            t = re.stringByReplacingMatches(in: t, range: NSRange(t.startIndex..., in: t), withTemplate: "[ẩn SĐT]")
        }
        return t
    }

    // MARK: định nghĩa công cụ (định dạng OpenAI/Groq/Gemini)

    private static func prop(_ type: String, _ desc: String, _ enumValues: [String] = []) -> [String: Any] {
        var p: [String: Any] = ["type": type, "description": desc]
        if !enumValues.isEmpty { p["enum"] = enumValues }
        return p
    }

    private static func filterProps(withProduct: Bool = true) -> [String: Any] {
        var o: [String: Any] = [:]
        o["status"] = prop("string",
            "Trạng thái nhận hàng. 'Đang chờ' = chưa nhận hàng về kho (người dùng nói 'chưa về', 'chưa nhận', 'đang chờ' đều là đây). 'Đã nhận' = hàng đã về và đã nhận tại kho.",
            ["Đang chờ", "Đã nhận"])
        o["date_field"] = prop("string",
            "Ngày dùng để lọc/gom theo ngày: recv = ngày nhận tại kho (mặc định), return = ngày khách hoàn/hủy, arrived = giờ hàng về shop.",
            ["recv", "return", "arrived"])
        o["from"] = prop("string", "Từ ngày (gồm), yyyy-MM-dd hoặc today/yesterday.")
        o["to"] = prop("string", "Đến ngày (gồm), yyyy-MM-dd hoặc today/yesterday.")
        o["platform"] = prop("string", "Sàn: Shopee, TikTok Shop, Lazada (khớp một phần).")
        o["carrier"] = prop("string", "Đơn vị vận chuyển, ví dụ SPX Express, GHN (khớp một phần).")
        o["order_type"] = prop("string", "Loại đơn, ví dụ 'Trả hàng/Hoàn tiền', 'Giao không thành công', 'Đơn hủy' (khớp một phần).")
        o["platform_status"] = prop("string", "Trạng thái trên sàn (khớp một phần).")
        o["alert"] = prop("string", "Mức cảnh báo cho đơn đang chờ: 'yellow' = quá 12 giờ kể từ lúc hàng về, 'red' = quá 24 giờ, 'warning' = vàng hoặc đỏ, 'safe' = chưa tới 12 giờ.",
                          ["yellow", "red", "warning", "safe"])
        o["watching"] = prop("boolean", "true = chỉ đơn đang theo dõi (có gắn sao).")
        o["min_value"] = prop("number", "Giá trị đơn tối thiểu (đồng).")
        o["max_value"] = prop("number", "Giá trị đơn tối đa (đồng).")
        o["text"] = prop("string", "Tìm trong ghi chú (khớp một phần).")
        o["scope"] = prop("string", "active = đang xử lý (mặc định), archived = kho lưu trữ, all = tất cả.", ["active", "archived", "all"])
        if withProduct { o["product"] = prop("string", "Tên sản phẩm (khớp một phần).") }
        return o
    }

    private static func tool(_ name: String, _ desc: String, _ props: [String: Any], required: [String] = []) -> [String: Any] {
        var params: [String: Any] = ["type": "object", "properties": props]
        if !required.isEmpty { params["required"] = required }
        return ["type": "function", "function": ["name": name, "description": desc, "parameters": params] as [String: Any]]
    }

    private static func buildDefinitions() -> String {
        var listProps = filterProps()
        listProps["sort_by"] = prop("string", "Sắp xếp theo: arrived (giờ hàng về), recv_date, return_date, value (giá trị). Mặc định: đơn đang chờ xếp cũ nhất trước, còn lại mới nhất trước.",
                                    ["arrived", "recv_date", "return_date", "value"])
        listProps["descending"] = prop("boolean", "true = giảm dần, false = tăng dần.")
        listProps["limit"] = prop("integer", "Số dòng tối đa, mặc định 20, tối đa 50.")

        var aggProps = filterProps()
        aggProps["group_by"] = prop("string", "Gom nhóm theo: status, platform, carrier, order_type, platform_status, product, day, week, month (theo date_field), alert (mức cảnh báo), none (chỉ tổng).",
                                    ["none", "status", "platform", "carrier", "order_type", "platform_status", "product", "day", "week", "month", "alert"])
        aggProps["metric"] = prop("string", "Sắp xếp nhóm theo: count (số kiện), value (tổng giá trị), quantity (tổng số lượng). Mặc định count. Gom theo ngày/tuần/tháng luôn xếp theo thời gian.",
                                  ["count", "value", "quantity"])
        aggProps["top"] = prop("integer", "Số nhóm tối đa, mặc định 20, tối đa 60.")

        let tools: [[String: Any]] = [
            tool("overview", "Tổng quan nhanh: hôm nay nhận bao nhiêu, đang chờ bao nhiêu và tổng giá trị, số đơn cảnh báo vàng/đỏ, đơn đang theo dõi. Dùng khi hỏi chung 'tình hình hôm nay', 'có gì cần xử lý'.", [:]),
            tool("count_orders", "Đếm kiện hoàn theo bộ lọc. Trả về tổng số kiện, tổng giá trị (đồng), tổng số lượng và phân loại theo trạng thái, sàn. 'Đơn về hôm nay' = status 'Đã nhận', date_field 'recv', from=to=today.", filterProps()),
            tool("aggregate", "Thống kê gom nhóm: đếm kiện / tổng giá trị / tổng số lượng theo trạng thái, sàn, đơn vị vận chuyển, loại đơn, sản phẩm, ngày, tuần, tháng hoặc mức cảnh báo. Dùng cho 'bao nhiêu ... theo ...', 'tổng tiền ...', 'nhiều nhất', 'xu hướng'.", aggProps),
            tool("list_orders", "Liệt kê kiện hoàn theo bộ lọc (tối đa 50 dòng), kèm giá trị, đơn vị vận chuyển, giờ hàng về, mức cảnh báo, số giờ chờ.", listProps),
            tool("find_order", "Tìm kiện theo mã đơn, mã vận đơn hoặc các mã khác của đơn (khớp một phần), ở mọi nơi kể cả lưu trữ.", ["code": prop("string", "Mã cần tìm (tối thiểu 3 ký tự).")], required: ["code"]),
            tool("order_detail", "Chi tiết một đơn: sản phẩm, màu, size, số lượng, giá trị, các mã, mốc thời gian, ghi chú, theo dõi, mức cảnh báo. Dùng khi hỏi sâu về MỘT đơn.",
                 ["code": prop("string", "Mã đơn hoặc mã vận đơn (khớp một phần)."), "id": prop("string", "Mã nội bộ của đơn (từ list_orders/find_order).")]),
            tool("product_stats", "Thống kê sản phẩm: số lượng và số đơn theo từng sản phẩm, giảm dần (tối đa 20). Dùng cho 'sản phẩm nào ... nhiều nhất'.", filterProps(withProduct: false)),
        ]
        guard JSONSerialization.isValidJSONObject(tools),
              let data = try? JSONSerialization.data(withJSONObject: tools, options: [.withoutEscapingSlashes]),
              let s = String(data: data, encoding: .utf8) else { return "[]" }
        return s
    }

    public static let definitionsJSON: String = buildDefinitions()

    // MARK: thực thi

    public func execute(name: String, argsJSON: String?) -> AIToolResult {
        var args: [String: Any] = [:]
        if let s = argsJSON, !s.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            guard let data = s.data(using: .utf8),
                  let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
                return Self.error("Tham số không phải JSON hợp lệ.")
            }
            args = obj
        }
        switch name {
        case "overview": return overview()
        case "count_orders": return count(args)
        case "aggregate": return aggregate(args)
        case "list_orders": return list(args)
        case "find_order": return find(args)
        case "order_detail": return detail(args)
        case "product_stats": return productStats(args)
        default: return Self.error("Không có công cụ '\(name)'.")
        }
    }

    private static func json(_ obj: Any) -> String {
        guard JSONSerialization.isValidJSONObject(obj),
              let data = try? JSONSerialization.data(withJSONObject: obj, options: [.withoutEscapingSlashes]),
              let s = String(data: data, encoding: .utf8) else { return "{}" }
        return s
    }

    private static func error(_ message: String) -> AIToolResult { AIToolResult(json: json(["error": message])) }

    // MARK: đọc tham số (chịu được số/chữ lẫn lộn)

    private func str(_ a: [String: Any], _ k: String) -> String? {
        guard let v = a[k] else { return nil }
        var s: String?
        if let t = v as? String { s = t }
        else if let n = v as? NSNumber { s = n.stringValue }
        guard let r = s?.trimmingCharacters(in: .whitespacesAndNewlines), !r.isEmpty else { return nil }
        return r
    }

    private func int(_ a: [String: Any], _ k: String) -> Int? {
        guard let s = str(a, k), let d = Double(s) else { return nil }
        return Int(d)
    }

    private func dbl(_ a: [String: Any], _ k: String) -> Double? {
        guard let s = str(a, k) else { return nil }
        return Double(s)
    }

    private func bool(_ a: [String: Any], _ k: String) -> Bool? {
        guard let s = str(a, k)?.lowercased() else { return nil }
        if ["true", "1", "có", "yes"].contains(s) { return true }
        if ["false", "0", "không", "no"].contains(s) { return false }
        return nil
    }

    struct Filter {
        var status: String?
        var dateField: String
        var from: Date?
        var to: Date?
        var platform: String?
        var carrier: String?
        var product: String?
        var scope: String
        var orderType: String?
        var platformStatus: String?
        var alert: String?
        var watching: Bool?
        var minValue: Double?
        var maxValue: Double?
        var text: String?
    }

    private func readFilter(_ a: [String: Any]) -> Filter {
        var scope = (str(a, "scope") ?? "active").lowercased()
        if !["active", "archived", "all"].contains(scope) { scope = "active" }
        var field = (str(a, "date_field") ?? "recv").lowercased()
        if !["recv", "return", "arrived"].contains(field) { field = "recv" }
        var alert = str(a, "alert")?.lowercased()
        if let al = alert, !["yellow", "red", "warning", "safe"].contains(al) {
            alert = (al == "overdue" || al == "đỏ") ? "red" : (al == "vàng" ? "yellow" : nil)
        }
        return Filter(status: normalizeStatus(str(a, "status")), dateField: field,
                      from: parseDay(str(a, "from")), to: parseDay(str(a, "to")),
                      platform: str(a, "platform"), carrier: str(a, "carrier"), product: str(a, "product"), scope: scope,
                      orderType: str(a, "order_type"), platformStatus: str(a, "platform_status"), alert: alert,
                      watching: bool(a, "watching"), minValue: dbl(a, "min_value"), maxValue: dbl(a, "max_value"), text: str(a, "text"))
    }

    static let waiting = "@waiting"
    static let receivedKey = "@received"
    static let receivedText = "Hàng đã trả"

    private func normalizeStatus(_ s: String?) -> String? {
        guard let s = s else { return nil }
        let l = s.lowercased()
        if l.contains("đã trả") || l.contains("đã nhận") || l.contains("đã về") || l == "received" { return Self.receivedKey }
        if l.contains("chưa") || l.contains("chờ") || l.contains("pending") || l.contains("waiting") { return Self.waiting }
        return s
    }

    private func parseDay(_ s: String?) -> Date? {
        guard let s = s else { return nil }
        let today = calendar.startOfDay(for: now())
        switch s.lowercased() {
        case "today", "hôm nay": return today
        case "yesterday", "hôm qua": return calendar.date(byAdding: .day, value: -1, to: today)
        default: break
        }
        if let d = OrderStats.parseDate(s, calendar: calendar) { return calendar.startOfDay(for: d) }
        return nil
    }

    // MARK: trạng thái, cảnh báo, ngày

    static func isWaiting(_ r: OrderRow) -> Bool { r.pd && !r.rv }

    static func statusText(_ r: OrderRow) -> String {
        if r.rv { return receivedText }
        let st = r.st.trimmingCharacters(in: .whitespacesAndNewlines)
        return st.isEmpty ? "Chưa trả" : st
    }

    private enum Level { case done, none, safe, yellow, red }

    private func level(_ r: OrderRow) -> Level {
        guard Self.isWaiting(r) else { return .done }
        let nowMs = Int(now().timeIntervalSince1970 * 1000)
        if r.lv >= 0 { return r.lv >= 2 ? .red : (r.lv == 1 ? .yellow : .safe) }
        if r.ar > 0 {
            let hours = Double(nowMs - r.ar) / 3_600_000
            return hours >= 24 ? .red : (hours >= 12 ? .yellow : .safe)
        }
        if r.due > 0 {
            let hours = Double(nowMs - (r.due - 86_400_000)) / 3_600_000
            return hours >= 24 ? .red : (hours >= 12 ? .yellow : .safe)
        }
        return .none
    }

    private static func levelName(_ l: Level) -> String {
        switch l {
        case .red: return "đỏ (quá 24 giờ)"
        case .yellow: return "vàng (quá 12 giờ)"
        case .safe: return "an toàn"
        case .none: return "chưa có giờ hàng về"
        case .done: return "đã xử lý"
        }
    }

    /// Giờ hàng về shop (ms epoch): từ `ar`, nếu thiếu thì suy từ hạn xử lý trừ 24 giờ.
    private func arrivedMs(_ r: OrderRow) -> Int? {
        if r.ar > 0 { return r.ar }
        if r.due > 0 && Self.isWaiting(r) { return r.due - 86_400_000 }
        return nil
    }

    private func dateOf(_ r: OrderRow, _ field: String) -> Date? {
        switch field {
        case "return": return OrderStats.parseDate(r.rd, calendar: calendar)
        case "arrived":
            guard let ms = arrivedMs(r) else { return nil }
            return Date(timeIntervalSince1970: Double(ms) / 1000)
        default: return OrderStats.parseDate(r.rcd, calendar: calendar)
        }
    }

    private func matches(_ r: OrderRow, _ f: Filter) -> Bool {
        switch f.scope {
        case "archived": if !r.isArchived { return false }
        case "all": break
        default: if r.isArchived { return false }
        }
        if let st = f.status {
            if st == Self.receivedKey { if !r.rv { return false } }
            else if st == Self.waiting { if !Self.isWaiting(r) { return false } }
            else if Self.statusText(r).lowercased() != st.lowercased() { return false }
        }
        if let p = f.platform, !r.p.localizedCaseInsensitiveContains(p) { return false }
        if let c = f.carrier, !r.cr.localizedCaseInsensitiveContains(c) { return false }
        if let t = f.orderType, !r.ty.localizedCaseInsensitiveContains(t) { return false }
        if let t = f.platformStatus, !r.ps.localizedCaseInsensitiveContains(t) { return false }
        if let p = f.product, !r.n.localizedCaseInsensitiveContains(p) { return false }
        if let w = f.watching, r.wl != w { return false }
        if let mn = f.minValue, Double(r.pr) < mn { return false }
        if let mx = f.maxValue, Double(r.pr) > mx { return false }
        if let t = f.text, !r.nt.localizedCaseInsensitiveContains(t) { return false }
        if let al = f.alert {
            let lv = level(r)
            switch al {
            case "yellow": if lv != .yellow { return false }
            case "red": if lv != .red { return false }
            case "warning": if lv != .yellow && lv != .red { return false }
            case "safe": if lv != .safe { return false }
            default: break
            }
        }
        if f.from != nil || f.to != nil {
            guard let d = dateOf(r, f.dateField) else { return false }
            let day = calendar.startOfDay(for: d)
            if let from = f.from, day < from { return false }
            if let to = f.to, day > to { return false }
        }
        return true
    }

    private func select(_ f: Filter) -> [OrderRow] { load().filter { matches($0, f) } }

    private static func totalValue(_ rows: [OrderRow]) -> Int { rows.reduce(0) { $0 + $1.pr } }
    private static func totalQty(_ rows: [OrderRow]) -> Int { rows.reduce(0) { $0 + max($1.q, 1) } }

    private func group(_ rows: [OrderRow], _ key: (OrderRow) -> String) -> [String: Int] {
        var m: [String: Int] = [:]
        for r in rows { m[key(r), default: 0] += 1 }
        return m
    }

    private static func label(_ s: String) -> String { s.isEmpty ? "(trống)" : s }

    // MARK: công cụ

    private func overview() -> AIToolResult {
        let today = calendar.startOfDay(for: now())
        let yesterday = calendar.date(byAdding: .day, value: -1, to: today) ?? today
        let active = load().filter { !$0.isArchived }
        let waiting = active.filter { Self.isWaiting($0) }
        func receivedOn(_ day: Date) -> [OrderRow] {
            active.filter { r in
                guard r.rv, let d = OrderStats.parseDate(r.rcd, calendar: calendar) else { return false }
                return calendar.startOfDay(for: d) == day
            }
        }
        let rToday = receivedOn(today)
        let rYesterday = receivedOn(yesterday)
        let red = waiting.filter { level($0) == .red }
        let yellow = waiting.filter { level($0) == .yellow }
        func box(_ rs: [OrderRow]) -> [String: Any] {
            var b: [String: Any] = [:]
            b["count"] = rs.count
            b["value"] = Self.totalValue(rs)
            return b
        }
        var waitingBox = box(waiting)
        waitingBox["byPlatform"] = group(waiting) { Self.label($0.p) }
        var alertsBox: [String: Any] = [:]
        alertsBox["redOver24h"] = box(red)
        alertsBox["yellowOver12h"] = box(yellow)
        var res: [String: Any] = [:]
        res["activeOrders"] = active.count
        res["waiting"] = waitingBox
        res["alerts"] = alertsBox
        res["receivedToday"] = box(rToday)
        res["receivedYesterday"] = box(rYesterday)
        res["watching"] = active.filter { $0.wl }.count
        res["archivedOrders"] = load().filter { $0.isArchived }.count
        return AIToolResult(json: Self.json(res))
    }

    private func count(_ a: [String: Any]) -> AIToolResult {
        let rows = select(readFilter(a))
        var res: [String: Any] = [:]
        res["total"] = rows.count
        res["totalValue"] = Self.totalValue(rows)
        res["totalQuantity"] = Self.totalQty(rows)
        res["byStatus"] = group(rows) { Self.statusText($0) }
        res["byPlatform"] = group(rows) { Self.label($0.p) }
        return AIToolResult(json: Self.json(res))
    }

    private func dayKey(_ r: OrderRow, _ field: String, _ format: String) -> String {
        guard let d = dateOf(r, field) else { return "(không có ngày)" }
        let f = DateFormatter()
        f.calendar = calendar
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = format
        return f.string(from: d)
    }

    private func weekKey(_ r: OrderRow, _ field: String) -> String {
        guard let d = dateOf(r, field) else { return "(không có ngày)" }
        var cal = calendar
        cal.firstWeekday = 2
        let start = cal.dateInterval(of: .weekOfYear, for: d)?.start ?? d
        let f = DateFormatter()
        f.calendar = calendar
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd"
        return f.string(from: start) + " (tuần)"
    }

    private func aggregate(_ a: [String: Any]) -> AIToolResult {
        let f = readFilter(a)
        let rows = select(f)
        let groupBy = (str(a, "group_by") ?? "none").lowercased()
        let metric = (str(a, "metric") ?? "count").lowercased()
        let top = max(1, min(60, int(a, "top") ?? 20))

        var buckets: [String: [OrderRow]] = [:]
        for r in rows {
            let key: String
            switch groupBy {
            case "status": key = Self.statusText(r)
            case "platform": key = Self.label(r.p)
            case "carrier": key = Self.label(r.cr)
            case "order_type": key = Self.label(r.ty)
            case "platform_status": key = Self.label(r.ps)
            case "alert": key = Self.levelName(level(r))
            case "product": key = Self.label(r.n.trimmingCharacters(in: .whitespacesAndNewlines))
            case "day": key = dayKey(r, f.dateField, "yyyy-MM-dd")
            case "week": key = weekKey(r, f.dateField)
            case "month": key = dayKey(r, f.dateField, "yyyy-MM")
            default: key = "tất cả"
            }
            buckets[key, default: []].append(r)
        }

        struct Row { var key: String; var count: Int; var value: Int; var qty: Int }
        let list: [Row] = buckets.map { Row(key: $0.key, count: $0.value.count, value: Self.totalValue($0.value), qty: Self.totalQty($0.value)) }
        let chronological = ["day", "week", "month"].contains(groupBy)
        var ordered: [Row]
        if chronological {
            ordered = list.sorted { $0.key < $1.key }
        } else {
            let byMetric: (Row) -> Int = { r in
                switch metric {
                case "value": return r.value
                case "quantity": return r.qty
                default: return r.count
                }
            }
            ordered = list.sorted { x, y in
                let mx = byMetric(x)
                let my = byMetric(y)
                return mx != my ? mx > my : x.key < y.key
            }
        }
        let shown: [Row] = chronological ? Array(ordered.suffix(top)) : Array(ordered.prefix(top))
        let groups: [[String: Any]] = shown.map { ["group": Self.maskPhones($0.key), "count": $0.count, "value": $0.value, "quantity": $0.qty] }
        var res: [String: Any] = [:]
        res["groupBy"] = groupBy
        res["ordersCounted"] = rows.count
        res["totalValue"] = Self.totalValue(rows)
        res["totalQuantity"] = Self.totalQty(rows)
        res["groupsTotal"] = list.count
        res["truncated"] = list.count > shown.count
        res["groups"] = groups
        return AIToolResult(json: Self.json(res))
    }

    private func list(_ a: [String: Any]) -> AIToolResult {
        let f = readFilter(a)
        var rows = select(f)
        let limit = max(1, min(50, int(a, "limit") ?? 20))
        let givenSort: String? = str(a, "sort_by")?.lowercased()
        let waitingOnly: Bool = (f.status == Self.waiting)
        let defaultSort: String = waitingOnly ? "arrived" : (f.dateField == "return" ? "return_date" : "recv_date")
        let sortKey: String = givenSort ?? defaultSort
        let defaultDesc: Bool = givenSort == nil ? !waitingOnly : (sortKey != "arrived")
        let descending: Bool = bool(a, "descending") ?? defaultDesc
        func key(_ r: OrderRow) -> Date {
            let d: Date?
            switch sortKey {
            case "arrived": d = dateOf(r, "arrived")
            case "return_date": d = dateOf(r, "return")
            default: d = dateOf(r, "recv")
            }
            return d ?? (descending ? Date.distantPast : Date.distantFuture)
        }
        if sortKey == "value" {
            rows.sort { descending ? $0.pr > $1.pr : $0.pr < $1.pr }
        } else {
            rows.sort { descending ? key($0) > key($1) : key($0) < key($1) }
        }
        return rowsResult(rows, limit: limit, detailed: false)
    }

    private func find(_ a: [String: Any]) -> AIToolResult {
        let code = (str(a, "code") ?? "").uppercased()
        if code.count < 3 { return Self.error("Mã cần tìm quá ngắn (tối thiểu 3 ký tự).") }
        let rows = load().filter { r in r.lookupCodes.contains { $0.contains(code) } }
        return rowsResult(rows, limit: 10, detailed: false)
    }

    private func detail(_ a: [String: Any]) -> AIToolResult {
        let id = str(a, "id")
        let code = (str(a, "code") ?? "").uppercased()
        if id == nil && code.count < 3 { return Self.error("Cần mã nội bộ (id) hoặc mã đơn/mã vận đơn (tối thiểu 3 ký tự).") }
        let found: [OrderRow]
        if let id = id { found = load().filter { $0.id == id } }
        else { found = Array(load().filter { r in r.lookupCodes.contains { $0.contains(code) } }.prefix(3)) }
        if found.isEmpty { return Self.error("Không tìm thấy đơn nào khớp.") }
        return rowsResult(found, limit: 3, detailed: true)
    }

    private func rowJSON(_ r: OrderRow, detailed: Bool) -> [String: Any] {
        let lv = level(r)
        var j: [String: Any] = [:]
        j["id"] = r.id
        j["orderCode"] = r.o
        j["track"] = r.c
        j["platform"] = r.p
        j["carrier"] = r.cr
        j["status"] = Self.statusText(r)
        j["type"] = r.ty
        j["platformStatus"] = r.ps
        j["value"] = r.pr
        j["returnDate"] = r.rd
        j["recvDate"] = r.rcd
        j["alert"] = Self.levelName(lv)
        j["product"] = Self.maskPhones(r.n)
        j["color"] = r.co
        j["size"] = r.sz
        j["qty"] = r.q
        j["note"] = Self.maskPhones(r.nt)
        j["watching"] = r.wl
        j["archived"] = r.isArchived
        j["cancelled"] = r.isCancelled
        if let ms = arrivedMs(r) {
            let f = DateFormatter()
            f.calendar = calendar
            f.locale = Locale(identifier: "vi_VN")
            f.dateFormat = "dd/MM/yyyy HH:mm"
            j["arrivedAt"] = f.string(from: Date(timeIntervalSince1970: Double(ms) / 1000))
            if lv == .safe || lv == .yellow || lv == .red {
                let nowMs = Int(now().timeIntervalSince1970 * 1000)
                j["hoursWaiting"] = (Double(nowMs - ms) / 3_600_000 * 10).rounded() / 10
            }
        }
        if detailed {
            j["watchReason"] = Self.maskPhones(r.wr)
            j["otherCodes"] = r.x
            if !r.rn.isEmpty { j["returnedProducts"] = Self.maskPhones(r.rn) }
        }
        return j
    }

    private func rowsResult(_ rows: [OrderRow], limit: Int, detailed: Bool) -> AIToolResult {
        var arr: [[String: Any]] = []
        var refs: [AIOrderRef] = []
        for r in rows.prefix(limit) {
            seen[r.id] = r
            refs.append(AIOrderRef(id: r.id, orderCode: r.o, track: r.c, status: Self.statusText(r)))
            arr.append(rowJSON(r, detailed: detailed))
        }
        var res: [String: Any] = [:]
        res["total"] = rows.count
        res["totalValue"] = Self.totalValue(rows)
        res["shown"] = arr.count
        res["truncated"] = rows.count > arr.count
        res["orders"] = arr
        return AIToolResult(json: Self.json(res), refs: refs)
    }

    private func productStats(_ a: [String: Any]) -> AIToolResult {
        let rows = select(readFilter(a))
        var qty: [String: Int] = [:]
        var orders: [String: Int] = [:]
        for r in rows {
            let key = r.n.trimmingCharacters(in: .whitespacesAndNewlines)
            if key.isEmpty { continue }
            qty[key, default: 0] += r.q
            orders[key, default: 0] += 1
        }
        let top = qty.sorted { $0.value != $1.value ? $0.value > $1.value : $0.key < $1.key }.prefix(20)
        let arr: [[String: Any]] = top.map { ["product": Self.maskPhones($0.key), "qty": $0.value, "orders": orders[$0.key] ?? 0] }
        return AIToolResult(json: Self.json(["ordersCounted": rows.count, "products": arr]))
    }
}
