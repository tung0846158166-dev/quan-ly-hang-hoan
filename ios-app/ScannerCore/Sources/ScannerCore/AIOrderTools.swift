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

    public static let definitionsJSON = """
    [
      {"type":"function","function":{"name":"count_orders","description":"Đếm số kiện hoàn theo bộ lọc. Không có bộ lọc = tổng quan. Trả về tổng và phân loại theo trạng thái, sàn. 'Đơn về hôm nay' = status 'Hàng đã trả' với date_field 'recv' và from=to=hôm nay.","parameters":{"type":"object","properties":{
        "status":{"type":"string","enum":["Hàng đã trả","Chưa trả"],"description":"'Hàng đã trả' = hàng đã về và đã nhận tại kho. 'Chưa trả' = còn chờ nhận."},
        "date_field":{"type":"string","enum":["recv","return"],"description":"Ngày dùng để lọc: recv = ngày nhận tại kho (mặc định), return = ngày khách hoàn/hủy."},
        "from":{"type":"string","description":"Từ ngày (gồm), yyyy-MM-dd hoặc today/yesterday."},
        "to":{"type":"string","description":"Đến ngày (gồm), yyyy-MM-dd hoặc today/yesterday."},
        "platform":{"type":"string","description":"Sàn: Shopee, TikTok Shop, Lazada (khớp một phần)."},
        "product":{"type":"string","description":"Tên sản phẩm (khớp một phần)."},
        "scope":{"type":"string","enum":["active","archived","all"],"description":"active = đang xử lý (mặc định), archived = kho lưu trữ, all = tất cả."}
      }}}},
      {"type":"function","function":{"name":"list_orders","description":"Liệt kê các kiện hoàn theo bộ lọc (tối đa 50 dòng).","parameters":{"type":"object","properties":{
        "status":{"type":"string","enum":["Hàng đã trả","Chưa trả"]},
        "date_field":{"type":"string","enum":["recv","return"]},
        "from":{"type":"string"},"to":{"type":"string"},"platform":{"type":"string"},"product":{"type":"string"},
        "scope":{"type":"string","enum":["active","archived","all"]},
        "limit":{"type":"integer","description":"Số dòng tối đa, mặc định 20, tối đa 50."}
      }}}},
      {"type":"function","function":{"name":"find_order","description":"Tìm kiện theo mã đơn, mã vận đơn hoặc các mã khác của đơn (khớp một phần), ở mọi nơi kể cả lưu trữ.","parameters":{"type":"object","properties":{"code":{"type":"string","description":"Mã cần tìm."}},"required":["code"]}}},
      {"type":"function","function":{"name":"product_stats","description":"Thống kê sản phẩm: số lượng và số đơn theo từng sản phẩm, sắp giảm dần (tối đa 20). Cùng bộ lọc như count_orders.","parameters":{"type":"object","properties":{
        "status":{"type":"string","enum":["Hàng đã trả","Chưa trả"]},
        "date_field":{"type":"string","enum":["recv","return"]},
        "from":{"type":"string"},"to":{"type":"string"},"platform":{"type":"string"},
        "scope":{"type":"string","enum":["active","archived","all"]}
      }}}}
    ]
    """

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
        case "count_orders": return count(args)
        case "list_orders": return list(args)
        case "find_order": return find(args)
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

    struct Filter {
        var status: String?
        var dateField: String
        var from: Date?
        var to: Date?
        var platform: String?
        var product: String?
        var scope: String
    }

    private func str(_ a: [String: Any], _ k: String) -> String? {
        guard let v = a[k] as? String else { return nil }
        let t = v.trimmingCharacters(in: .whitespacesAndNewlines)
        return t.isEmpty ? nil : t
    }

    private func readFilter(_ a: [String: Any]) -> Filter {
        var scope = (str(a, "scope") ?? "active").lowercased()
        if !["active", "archived", "all"].contains(scope) { scope = "active" }
        var field = (str(a, "date_field") ?? "recv").lowercased()
        if !["recv", "return"].contains(field) { field = "recv" }
        return Filter(status: normalizeStatus(str(a, "status")), dateField: field,
                      from: parseDay(str(a, "from")), to: parseDay(str(a, "to")),
                      platform: str(a, "platform"), product: str(a, "product"), scope: scope)
    }

    static let received = "Hàng đã trả"
    static let pendingStatus = "Chưa trả"

    private func normalizeStatus(_ s: String?) -> String? {
        guard let s = s else { return nil }
        let l = s.lowercased()
        if l == Self.received.lowercased() || l.contains("đã trả") || l.contains("đã nhận") || l.contains("đã về") || l == "received" { return Self.received }
        if l == Self.pendingStatus.lowercased() || l.contains("chưa") { return Self.pendingStatus }
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

    static func statusText(_ r: OrderRow) -> String {
        if r.rv { return received }
        let st = r.st.trimmingCharacters(in: .whitespacesAndNewlines)
        return st.isEmpty ? pendingStatus : st
    }

    private func select(_ f: Filter) -> [OrderRow] {
        var out: [OrderRow] = []
        for r in load() {
            switch f.scope {
            case "archived": if !r.isArchived { continue }
            case "all": break
            default: if r.isArchived { continue }
            }
            if let st = f.status {
                if st == Self.received { if !r.rv { continue } }
                else if st == Self.pendingStatus { if r.rv || !r.pd { continue } }
                else if Self.statusText(r) != st { continue }
            }
            if let p = f.platform, !r.p.localizedCaseInsensitiveContains(p) { continue }
            if let p = f.product, !r.n.localizedCaseInsensitiveContains(p) { continue }
            if f.from != nil || f.to != nil {
                let raw = f.dateField == "return" ? r.rd : r.rcd
                guard let d = OrderStats.parseDate(raw, calendar: calendar) else { continue }
                let day = calendar.startOfDay(for: d)
                if let from = f.from, day < from { continue }
                if let to = f.to, day > to { continue }
            }
            out.append(r)
        }
        return out
    }

    private func group(_ rows: [OrderRow], _ key: (OrderRow) -> String) -> [String: Int] {
        var m: [String: Int] = [:]
        for r in rows { m[key(r), default: 0] += 1 }
        return m
    }

    private func count(_ a: [String: Any]) -> AIToolResult {
        let rows = select(readFilter(a))
        let res: [String: Any] = [
            "total": rows.count,
            "byStatus": group(rows) { Self.statusText($0) },
            "byPlatform": group(rows) { $0.p.isEmpty ? "(trống)" : $0.p },
        ]
        return AIToolResult(json: Self.json(res))
    }

    private func list(_ a: [String: Any]) -> AIToolResult {
        let rows = select(readFilter(a))
        var limit = 20
        if let n = a["limit"] as? Int { limit = n } else if let d = a["limit"] as? Double { limit = Int(d) }
        limit = max(1, min(50, limit))
        return rowsResult(rows, limit: limit)
    }

    private func find(_ a: [String: Any]) -> AIToolResult {
        let code = (str(a, "code") ?? "").uppercased()
        if code.count < 3 { return Self.error("Mã cần tìm quá ngắn (tối thiểu 3 ký tự).") }
        let rows = load().filter { r in r.lookupCodes.contains { $0.contains(code) } }
        return rowsResult(rows, limit: 10)
    }

    private func rowsResult(_ rows: [OrderRow], limit: Int) -> AIToolResult {
        var arr: [[String: Any]] = []
        var refs: [AIOrderRef] = []
        for r in rows.prefix(limit) {
            seen[r.id] = r
            refs.append(AIOrderRef(id: r.id, orderCode: r.o, track: r.c, status: Self.statusText(r)))
            arr.append([
                "id": r.id, "orderCode": r.o, "track": r.c, "platform": r.p, "status": Self.statusText(r),
                "type": r.ty, "returnDate": r.rd, "recvDate": r.rcd,
                "product": Self.maskPhones(r.n), "color": r.co, "size": r.sz, "qty": r.q,
                "note": Self.maskPhones(r.nt), "watching": r.wl,
                "archived": r.isArchived, "cancelled": r.isCancelled,
            ])
        }
        let res: [String: Any] = ["total": rows.count, "shown": arr.count, "truncated": rows.count > arr.count, "orders": arr]
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
