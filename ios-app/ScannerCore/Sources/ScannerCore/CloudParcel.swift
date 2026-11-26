import Foundation

/// Sản phẩm trong một kiện trên cloud (cloud/API.md mục 1).
public struct CloudParcelItem: Codable, Equatable {
    public var product: String
    public var color: String
    public var size: String
    public var qty: Int
    public var returnedQty: Int
    public var sku: String?
    public var skuId: String?

    public init(product: String = "", color: String = "", size: String = "", qty: Int = 0, returnedQty: Int = 0, sku: String? = nil, skuId: String? = nil) {
        self.product = product; self.color = color; self.size = size; self.qty = qty; self.returnedQty = returnedQty
        self.sku = sku; self.skuId = skuId
    }

    private enum Keys: String, CodingKey { case product, color, size, qty, returnedQty, sku, skuId }

    /// Giải mã dễ tính: thiếu trường thì lấy giá trị rỗng/0; số đến dạng chuỗi hoặc số lẻ vẫn đọc được.
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: Keys.self)
        func str(_ k: Keys) -> String {
            if let s = try? c.decode(String.self, forKey: k) { return s }
            if let i = try? c.decode(Int.self, forKey: k) { return String(i) }
            return ""
        }
        func int(_ k: Keys) -> Int {
            if let i = try? c.decode(Int.self, forKey: k) { return i }
            if let d = try? c.decode(Double.self, forKey: k), d.isFinite { return Int(d) }
            if let s = try? c.decode(String.self, forKey: k), let i = Int(s.trimmingCharacters(in: .whitespaces)) { return i }
            return 0
        }
        self.init(product: str(.product), color: str(.color), size: str(.size), qty: int(.qty), returnedQty: int(.returnedQty),
                  sku: try? c.decode(String.self, forKey: .sku), skuId: try? c.decode(String.self, forKey: .skuId))
    }
}

/// Một KIỆN trên cloud ("slim": không có dữ liệu người mua/dữ liệu thô). Giải mã dễ tính: thiếu trường thì lấy giá trị rỗng, số/chuỗi đổi lẫn được.
public struct CloudParcel: Codable, Equatable, Identifiable {
    public var id: String
    public var orderId = "", returnOrderId = "", track = "", returnTrack = "", altTrack = "", forwardTrack = ""
    public var trackingCodes: [String] = [], scanAliases: [String] = []
    public var platform = "", orderType = "", platformStatus = "", preOrder = "", status = "", carrier = ""
    public var shipDate = "", returnDate = "", recvDate = "", returnedAt = "", deadline = ""
    public var price: Double = 0
    public var note = "", userNote = "", importWeek = "", importedAt = ""
    public var isWatchlist = false, isPartialReturn = false
    public var watchlistReason = "", photoId = ""
    /// Khiếu nại: "" = không; "Đang xử lý" | "Thành công" | "Thất bại".
    public var complaintStatus = "", complaintAt = "", complaintNote = "", complaintDoneAt = ""
    public var items: [CloudParcelItem] = []
    /// Chỉ các khóa trong danh sách trắng (returnReason, returnStatus, returnSuccessTime, ...), giá trị đã đổi sang chuỗi.
    public var extra: [String: String] = [:]
    public var isArchived = false, isDeleted = false
    public var archivedAt = "", deletedAt = "", deletedFrom = ""
    public var rev: Int = 0
    public var updatedAt: Int = 0

    public init(id: String) { self.id = id }

    private enum Keys: String, CodingKey {
        case id, orderId, returnOrderId, track, returnTrack, altTrack, forwardTrack, trackingCodes, scanAliases
        case platform, orderType, platformStatus, preOrder, status, carrier
        case shipDate, returnDate, recvDate, returnedAt, deadline, price, note, userNote, importWeek, importedAt
        case isWatchlist, isPartialReturn, watchlistReason, photoId, items, extra
        case complaintStatus, complaintAt, complaintNote, complaintDoneAt
        case isArchived, isDeleted, archivedAt, deletedAt, deletedFrom, rev, updatedAt
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: Keys.self)
        self.init(id: c.str(.id))
        orderId = c.str(.orderId); returnOrderId = c.str(.returnOrderId); track = c.str(.track); returnTrack = c.str(.returnTrack)
        altTrack = c.str(.altTrack); forwardTrack = c.str(.forwardTrack)
        trackingCodes = c.strings(.trackingCodes); scanAliases = c.strings(.scanAliases)
        platform = c.str(.platform); orderType = c.str(.orderType); platformStatus = c.str(.platformStatus); preOrder = c.str(.preOrder)
        status = c.str(.status); carrier = c.str(.carrier)
        shipDate = c.str(.shipDate); returnDate = c.str(.returnDate); recvDate = c.str(.recvDate); returnedAt = c.str(.returnedAt); deadline = c.str(.deadline)
        price = c.num(.price); note = c.str(.note); userNote = c.str(.userNote); importWeek = c.str(.importWeek); importedAt = c.str(.importedAt)
        isWatchlist = c.flag(.isWatchlist); isPartialReturn = c.flag(.isPartialReturn)
        watchlistReason = c.str(.watchlistReason); photoId = c.str(.photoId)
        complaintStatus = c.str(.complaintStatus); complaintAt = c.str(.complaintAt); complaintNote = c.str(.complaintNote); complaintDoneAt = c.str(.complaintDoneAt)
        items = (try? c.decode([CloudParcelItem].self, forKey: .items)) ?? []
        if let scalars = try? c.decode([String: CloudScalar].self, forKey: .extra) {
            for (k, v) in scalars { extra[k] = v.text }
        }
        isArchived = c.flag(.isArchived); isDeleted = c.flag(.isDeleted)
        archivedAt = c.str(.archivedAt); deletedAt = c.str(.deletedAt); deletedFrom = c.str(.deletedFrom)
        rev = Int(c.num(.rev)); updatedAt = Int(c.num(.updatedAt))
    }
}

/// Giá trị đơn lẻ (chuỗi/số/bool) của `extra`, đổi hết sang chuỗi.
struct CloudScalar: Decodable {
    let text: String
    init(from decoder: Decoder) throws {
        let c = try decoder.singleValueContainer()
        if let s = try? c.decode(String.self) { text = s }
        else if let b = try? c.decode(Bool.self) { text = b ? "true" : "false" }
        else if let i = try? c.decode(Int.self) { text = String(i) }
        else if let d = try? c.decode(Double.self) { text = String(d) }
        else { text = "" }
    }
}

private extension KeyedDecodingContainer {
    func str(_ key: Key) -> String {
        if let s = try? decode(String.self, forKey: key) { return s }
        if let i = try? decode(Int.self, forKey: key) { return String(i) }
        if let d = try? decode(Double.self, forKey: key), d.isFinite { return d == d.rounded() && abs(d) < 9e15 ? String(Int(d)) : String(d) }
        return ""
    }
    func num(_ key: Key) -> Double {
        if let d = try? decode(Double.self, forKey: key), d.isFinite { return d }
        if let s = try? decode(String.self, forKey: key), let d = Double(s.trimmingCharacters(in: .whitespaces)) { return d }
        return 0
    }
    func flag(_ key: Key) -> Bool {
        if let b = try? decode(Bool.self, forKey: key) { return b }
        if let i = try? decode(Int.self, forKey: key) { return i != 0 }
        return false
    }
    func strings(_ key: Key) -> [String] { (try? decode([String].self, forKey: key)) ?? [] }
}
