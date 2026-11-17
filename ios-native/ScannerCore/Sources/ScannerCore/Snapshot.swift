import Foundation

/// Một dòng trong danh sách đơn chờ hoàn mà PC gửi sang (khóa ngắn để gói nhỏ).
public struct OrderRow: Codable, Identifiable, Equatable, Hashable {
    public var k: String      // "m" = bảng chính, "c" = đơn hủy
    public var c: String      // mã vận đơn / mã chính
    public var o: String      // mã đơn hàng
    public var p: String      // sàn
    public var n: String      // sản phẩm
    public var co: String     // màu
    public var sz: String     // size
    public var q: Int
    public var rd: String     // ngày hủy/hoàn (chuỗi từ PC)
    public var due: Int       // hạn xử lý, ms epoch; 0 = chưa tính được
    public var st: String
    public var ty: String = ""      // loại đơn (Giao không thành công / Trả hàng/Hoàn tiền / Đơn hủy / THẤT THU)
    public var pre: Bool = false    // hàng đặt trước
    public var pd: Bool = true      // còn chờ nhận hoàn (bản PC cũ không gửi trường này nên mặc định là chờ)
    public var rv: Bool = false     // đã nhận hoàn kho
    public var wl: Bool = false     // đang theo dõi
    public var wr: String = ""      // lý do/ghi chú theo dõi
    public var nt: String = ""      // ghi chú đơn
    public var rcd: String = ""     // ngày giờ nhận tại kho (khi đã nhận)
    public var lv: Int = -1         // mức cảnh báo do PC tính: 2 = Báo Đỏ (>=72h), 1 = Báo Vàng (>=48h), 0 = an toàn; -1 = PC cũ không gửi
    public var ci: String = ""      // id đơn trong bảng đơn hủy trên PC (để đánh dấu đã nhận từ điện thoại); PC cũ không gửi

    public var id: String { k + "|" + c }
    /// Thuộc nhóm đơn hủy: nằm trong bảng đơn hủy, hoặc ở bảng chính nhưng loại đơn là "Đơn hủy".
    public var isCancelled: Bool { k == "c" || ty == "Đơn hủy" }
    public var isArchived: Bool { k == "a" }
    /// Mức cảnh báo giống cột Hạn Cần Xử Lý trên PC (quá hạn = Báo Đỏ, sắp đến hạn = Báo Vàng); PC cũ thì tính theo `due`.
    public func alertLevel(now: Int) -> DeadlineLevel {
        if lv >= 0 { return lv >= 2 ? .overdue : (lv == 1 ? .soon : .normal) }
        return Deadline.level(due: due, now: now)
    }
}

/// Một đơn trong Hộp Đơn Mới trên PC (chờ đẩy ra bảng chính).
public struct InboxRow: Codable, Identifiable, Equatable, Hashable {
    public var id: String     // id trên PC
    public var c: String      // mã vận đơn
    public var o: String      // mã đơn hàng
    public var p: String      // sàn
    public var n: String      // sản phẩm
    public var t: String      // giờ tạo / giờ quét
    public var u: Bool = false    // lượt quét ở kho chưa có đơn: đẩy ra sẽ ghi Hàng đã trả
    public var dup: Bool = false  // đã có ở bảng khác: đẩy ra chỉ gỡ khỏi hộp

    public init(id: String, c: String, o: String = "", p: String = "", n: String = "", t: String = "", u: Bool = false, dup: Bool = false) {
        self.id = id; self.c = c; self.o = o; self.p = p; self.n = n; self.t = t; self.u = u; self.dup = dup
    }
}

public struct SnapshotPayload: Codable, Equatable {
    public var time: Int
    public var count: Int
    public var rows: [OrderRow]
    /// Bảng đối chiếu số đơn do PC tính trên dữ liệu gốc (PC cũ không gửi thì rỗng).
    public var stats: [String: Int] = [:]
    /// Hộp Đơn Mới trên PC (PC cũ không gửi thì rỗng).
    public var inbox: [InboxRow] = []

    public init(time: Int, count: Int, rows: [OrderRow], stats: [String: Int] = [:], inbox: [InboxRow] = []) {
        self.time = time; self.count = count; self.rows = rows; self.stats = stats; self.inbox = inbox
    }
}

/// Giải mã "dễ tính": PC có thể gửi số thay cho chuỗi (mã đơn, ngày), null hoặc số lẻ; app vẫn đọc được thay vì bỏ cả danh sách.
private extension KeyedDecodingContainer {
    func flexString(_ key: Key) -> String {
        if let s = try? decode(String.self, forKey: key) { return s }
        if let i = try? decode(Int.self, forKey: key) { return String(i) }
        if let d = try? decode(Double.self, forKey: key), d.isFinite { return d == d.rounded() && abs(d) < 9e15 ? String(Int(d)) : String(d) }
        if let b = try? decode(Bool.self, forKey: key) { return b ? "true" : "false" }
        return ""
    }
    func flexBool(_ key: Key, default def: Bool) -> Bool {
        if let b = try? decode(Bool.self, forKey: key) { return b }
        if let i = try? decode(Int.self, forKey: key) { return i != 0 }
        if let s = try? decode(String.self, forKey: key) {
            let t = s.lowercased()
            if ["1", "true", "yes"].contains(t) { return true }
            if ["0", "false", "no", ""].contains(t) { return false }
        }
        return def
    }
    func flexInt(_ key: Key, default def: Int = 0) -> Int {
        if let i = try? decode(Int.self, forKey: key) { return i }
        if let d = try? decode(Double.self, forKey: key), d.isFinite, abs(d) < 9e18 { return Int(d) }
        if let s = try? decode(String.self, forKey: key), let i = Int(s.trimmingCharacters(in: .whitespaces)) { return i }
        return def
    }
}

private enum RowKeys: String, CodingKey { case k, c, o, p, n, co, sz, q, rd, due, st, ty, pre, pd, rv, wl, wr, nt, lv, rcd, ci }
private enum PayloadKeys: String, CodingKey { case time, count, rows, stats, inbox }
private enum InboxKeys: String, CodingKey { case id, c, o, p, n, t, u, dup }

extension OrderRow {
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: RowKeys.self)
        self.init(k: c.flexString(.k), c: c.flexString(.c), o: c.flexString(.o), p: c.flexString(.p), n: c.flexString(.n),
                  co: c.flexString(.co), sz: c.flexString(.sz), q: c.flexInt(.q, default: 1), rd: c.flexString(.rd),
                  due: c.flexInt(.due), st: c.flexString(.st),
                  ty: c.flexString(.ty), pre: c.flexBool(.pre, default: false), pd: c.flexBool(.pd, default: true), rv: c.flexBool(.rv, default: false),
                  wl: c.flexBool(.wl, default: false), wr: c.flexString(.wr), nt: c.flexString(.nt), rcd: c.flexString(.rcd), lv: c.flexInt(.lv, default: -1),
                  ci: c.flexString(.ci))
    }
}

extension InboxRow {
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: InboxKeys.self)
        self.init(id: c.flexString(.id), c: c.flexString(.c), o: c.flexString(.o), p: c.flexString(.p), n: c.flexString(.n), t: c.flexString(.t),
                  u: c.flexBool(.u, default: false), dup: c.flexBool(.dup, default: false))
    }
}

extension SnapshotPayload {
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: PayloadKeys.self)
        let rows = try c.decode([OrderRow].self, forKey: .rows)
        let stats = (try? c.decode([String: Int].self, forKey: .stats)) ?? [:]
        // Đơn nào trong hộp lỗi thì bỏ đơn đó, không bỏ cả danh sách.
        let inbox = ((try? c.decode([FailableInbox].self, forKey: .inbox)) ?? []).compactMap { $0.row }.filter { !$0.id.isEmpty }
        self.init(time: c.flexInt(.time), count: c.flexInt(.count, default: rows.count), rows: rows, stats: stats, inbox: inbox)
    }
}

private struct FailableInbox: Decodable {
    let row: InboxRow?
    init(from decoder: Decoder) throws { row = try? InboxRow(from: decoder) }
}

public struct SnapshotChunk: Equatable {
    public var snapshotId: String
    public var idx: Int
    public var total: Int
    public var chunk: String
}

public enum SnapshotError: Error { case badBase64, badCompression, badJSON }

/// Ghép các mảnh snapshot_chunk thành danh sách đơn.
public final class SnapshotAssembler {
    private var currentId = ""
    private var parts: [Int: String] = [:]
    private var total = 0
    /// Để chẩn đoán: số mảnh đã nhận / tổng của lần gửi gần nhất, và lỗi ghép/giải mã nếu có.
    public private(set) var partsReceived = 0
    public private(set) var partsTotal = 0
    public private(set) var lastError: String?

    public init() {}

    /// Trả về payload khi đủ mảnh; mảnh của lần gửi cũ hơn bị bỏ.
    public func add(_ c: SnapshotChunk) -> SnapshotPayload? {
        guard c.total > 0, c.idx >= 0, c.idx < c.total else { return nil }
        if c.snapshotId != currentId {
            if !currentId.isEmpty && c.snapshotId < currentId { return nil }
            currentId = c.snapshotId; parts = [:]; total = c.total
        }
        parts[c.idx] = c.chunk
        partsReceived = parts.count; partsTotal = total
        guard parts.count == total else { return nil }
        let b64 = (0..<total).compactMap { parts[$0] }.joined()
        parts = [:]
        do {
            let p = try Self.decode(base64: b64)
            lastError = nil
            return p
        } catch {
            lastError = Self.describeFailure(error, base64: b64, total: total)
            return nil
        }
    }

    /// Mô tả lỗi kèm kích thước và đầu dữ liệu để biết hỏng ở bước nào (base64, giải nén hay JSON).
    static func describeFailure(_ error: Error, base64: String, total: Int) -> String {
        var s = "\(error) (\(total) mảnh, base64 \(base64.count) ký tự"
        if let data = Data(base64Encoded: base64), let raw = try? (data as NSData).decompressed(using: .zlib) as Data {
            s += ", giải nén \(raw.count) byte, đầu: " + String(decoding: raw.prefix(80), as: UTF8.self)
        }
        return s + ")"
    }

    public static func decode(base64: String) throws -> SnapshotPayload {
        guard let data = Data(base64Encoded: base64) else { throw SnapshotError.badBase64 }
        guard let raw = try? (data as NSData).decompressed(using: .zlib) as Data else { throw SnapshotError.badCompression }
        guard let p = try? JSONDecoder().decode(SnapshotPayload.self, from: raw) else { throw SnapshotError.badJSON }
        return p
    }
}

public enum DeadlineLevel: Equatable { case overdue, soon, normal, unknown }

public enum Deadline {
    public static func level(due: Int, now: Int, soonWindowMs: Int = 86_400_000) -> DeadlineLevel {
        if due <= 0 { return .unknown }
        let left = due - now
        if left < 0 { return .overdue }
        return left <= soonWindowMs ? .soon : .normal
    }

    public static func duration(_ ms: Int) -> String {
        let totalH = abs(ms) / 3_600_000
        let d = totalH / 24, h = totalH % 24
        return ((d > 0 ? "\(d) ngày " : "") + (h > 0 || d == 0 ? "\(h) giờ" : "")).trimmingCharacters(in: .whitespaces)
    }

    public static func text(due: Int, now: Int) -> String {
        if due <= 0 { return "Chưa có hạn" }
        let left = due - now
        if left > 0 { return "Còn " + duration(left) }
        if -left < 3_600_000 { return "Đã đến hạn" }
        return "Quá hạn " + duration(left)
    }
}
