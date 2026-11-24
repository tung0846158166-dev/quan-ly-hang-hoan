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
    public var ps: String = ""      // cột Trạng thái sàn trên PC (vd "Huỷ sau chuẩn bị · Đã huỷ"); PC cũ không gửi
    public var x: String = ""       // các mã khác của đơn, cách nhau dấu cách (mã vận đơn hoàn/giao, mã yêu cầu trả hàng); PC cũ không gửi
    public var rw: Int = 0          // Bảng Hàng Hoàn Về: giờ đơn vào bảng (ms epoch); 0 = không nằm trong bảng; PC cũ không gửi
    public var rws: Int = 0         // Bảng Hàng Hoàn Về: chặng 1 Đã đồng ý, 2 Đang về, 3 Đã về shop, 4 Đã nhận kho
    public var ar: Int = 0          // giờ hàng về shop (ms epoch) nếu sàn đã ghi nhận; 0 = chưa có
    public var rn: String = ""      // v2: hoàn MỘT PHẦN: tên các sản phẩm ĐÃ HOÀN, cách nhau " | " (n vẫn chứa đủ mọi sản phẩm); rỗng = không phân biệt

    /// Đơn nằm trong Bảng Hàng Hoàn Về (khách yêu cầu hoàn, kiện đã "Đã đồng ý").
    public var inReturnWatch: Bool { k == "m" && rw > 0 }
    /// Chặng hiện tại trong Bảng Hàng Hoàn Về: 1 Đã đồng ý, 2 Đang về, 3 Đã về shop, 4 Đã nhận kho (điện thoại vừa quét nhận thì rv = true).
    public var returnWatchStage: Int { rv ? 4 : max(1, min(rws, 4)) }
    public static let returnWatchStageNames = ["", "Đã đồng ý", "Đang về", "Đã về shop", "Đã nhận kho"]

    /// Mọi mã tra được đơn này: mã chính, mã đơn và các mã khác.
    public var lookupCodes: [String] {
        ([c, o] + x.split(separator: " ").map(String.init)).map { $0.uppercased() }.filter { !$0.isEmpty }
    }

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
/// Trạng thái hiển thị thời điểm "Đã nhận tại kho" của một đơn hàng.
public enum ReceivedTimeDisplay: Equatable {
    case received(text: String, fromLocalScan: Bool)
    case receivedUnknownTime
    case notReceived
}

extension OrderRow {
    /// Đơn "Huỷ sau chuẩn bị": shop đã đóng gói rồi đơn mới bị huỷ (theo cột Trạng thái sàn).
    public var isCancelAfterPrep: Bool {
        let t = ps.folding(options: [.diacriticInsensitive, .caseInsensitive], locale: Locale(identifier: "vi_VN"))
            .replacingOccurrences(of: "đ", with: "d").lowercased()
        return t.contains("huy sau chuan bi")
    }

    /// Định dạng ngày (và giờ nếu cần) đồng nhất kiểu Việt Nam `dd/MM/yyyy` hoặc `dd/MM/yyyy HH:mm`.
    public static func formatDisplayDate(_ date: Date, includeTime: Bool, calendar: Calendar = .current) -> String {
        let comps = calendar.dateComponents([.day, .month, .year, .hour, .minute], from: date)
        let d = comps.day ?? 1
        let m = comps.month ?? 1
        let y = comps.year ?? 2026
        if includeTime {
            let h = comps.hour ?? 0
            let min = comps.minute ?? 0
            return String(format: "%02d/%02d/%04d %02d:%02d", d, m, y, h, min)
        }
        return String(format: "%02d/%02d/%04d", d, m, y)
    }

    /// Xác định thời điểm "Đã nhận tại kho" hiển thị trên màn Chi tiết đơn:
    /// - Chưa nhận (`rv == false`) -> `.notReceived`
    /// - Đã nhận có `rcd` từ PC hoặc điền cục bộ -> định dạng chuẩn `dd/MM/yyyy HH:mm` (hoặc `dd/MM/yyyy`)
    /// - Đã nhận nhưng `rcd` trống và vừa quét nhận trên chính điện thoại (`localScanTimeMs`) -> dùng giờ quét
    /// - Đã nhận nhưng chưa có cả hai -> `.receivedUnknownTime`
    public func resolvedReceivedTime(
        localScanTimeMs: Int? = nil,
        calendar: Calendar = .current
    ) -> ReceivedTimeDisplay {
        guard rv else { return .notReceived }
        let trimmed = rcd.trimmingCharacters(in: .whitespacesAndNewlines)
        if !trimmed.isEmpty {
            if let parsed = OrderStats.parseDate(trimmed, calendar: calendar) {
                let hasTime = trimmed.contains(":")
                return .received(
                    text: Self.formatDisplayDate(parsed, includeTime: hasTime, calendar: calendar),
                    fromLocalScan: false
                )
            }
            return .received(text: trimmed, fromLocalScan: false)
        }
        if let ms = localScanTimeMs, ms > 0 {
            let d = Date(timeIntervalSince1970: Double(ms) / 1000)
            return .received(
                text: Self.formatDisplayDate(d, includeTime: true, calendar: calendar),
                fromLocalScan: true
            )
        }
        return .receivedUnknownTime
    }
}

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
    /// Mã của lần gửi danh sách đầy đủ (PC dùng làm bản gốc cho các gói "phần thay đổi"); PC cũ không gửi.
    public var sid: String = ""

    public init(time: Int, count: Int, rows: [OrderRow], stats: [String: Int] = [:], inbox: [InboxRow] = [], sid: String = "") {
        self.time = time; self.count = count; self.rows = rows; self.stats = stats; self.inbox = inbox; self.sid = sid
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

private enum RowKeys: String, CodingKey { case k, c, o, p, n, co, sz, q, rd, due, st, ty, pre, pd, rv, wl, wr, nt, lv, rcd, ci, ps, x, rw, rws, ar, rn }
private enum PayloadKeys: String, CodingKey { case time, count, rows, stats, inbox, sid }
private enum InboxKeys: String, CodingKey { case id, c, o, p, n, t, u, dup }

extension OrderRow {
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: RowKeys.self)
        self.init(k: c.flexString(.k), c: c.flexString(.c), o: c.flexString(.o), p: c.flexString(.p), n: c.flexString(.n),
                  co: c.flexString(.co), sz: c.flexString(.sz), q: c.flexInt(.q, default: 1), rd: c.flexString(.rd),
                  due: c.flexInt(.due), st: c.flexString(.st),
                  ty: c.flexString(.ty), pre: c.flexBool(.pre, default: false), pd: c.flexBool(.pd, default: true), rv: c.flexBool(.rv, default: false),
                  wl: c.flexBool(.wl, default: false), wr: c.flexString(.wr), nt: c.flexString(.nt), rcd: c.flexString(.rcd), lv: c.flexInt(.lv, default: -1),
                  ci: c.flexString(.ci), ps: c.flexString(.ps), x: c.flexString(.x),
                  rw: c.flexInt(.rw), rws: c.flexInt(.rws), ar: c.flexInt(.ar), rn: c.flexString(.rn))
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
        self.init(time: c.flexInt(.time), count: c.flexInt(.count, default: rows.count), rows: rows, stats: stats, inbox: inbox, sid: c.flexString(.sid))
    }
}

private struct FailableInbox: Decodable {
    let row: InboxRow?
    init(from decoder: Decoder) throws { row = try? InboxRow(from: decoder) }
}

/// Gói "phần thay đổi" PC gửi sau một danh sách đầy đủ: các dòng thêm/đổi và khóa các dòng đã bỏ, tính so với bản gốc `base`.
/// Mỗi gói là tổng thay đổi kể từ bản gốc (không cộng dồn giữa các gói), nên rớt một gói không làm sai, gói sau sửa lại.
public struct SnapshotDelta: Decodable, Equatable {
    public var time: Int
    public var base: String
    public var seq: Int
    public var count: Int
    public var up: [DeltaRow]
    public var rm: [String]
    public var stats: [String: Int]
    public var inbox: [InboxRow]

    public init(time: Int, base: String, seq: Int, count: Int = 0, up: [DeltaRow], rm: [String], stats: [String: Int] = [:], inbox: [InboxRow] = []) {
        self.time = time; self.base = base; self.seq = seq; self.count = count; self.up = up; self.rm = rm; self.stats = stats; self.inbox = inbox
    }

    private enum Keys: String, CodingKey { case time, base, seq, count, up, rm, stats, inbox }
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: Keys.self)
        let up = try c.decode([DeltaRow].self, forKey: .up)
        let rm = (try? c.decode([String].self, forKey: .rm)) ?? []
        let stats = (try? c.decode([String: Int].self, forKey: .stats)) ?? [:]
        let inbox = ((try? c.decode([FailableInbox].self, forKey: .inbox)) ?? []).compactMap { $0.row }.filter { !$0.id.isEmpty }
        self.init(time: c.flexInt(.time), base: c.flexString(.base), seq: c.flexInt(.seq), count: c.flexInt(.count), up: up, rm: rm, stats: stats, inbox: inbox)
    }

    /// Khóa từng dòng, giống hệt PC (phoneRowKeys): k|c|o|ci, khóa trùng đánh số #2, #3 theo thứ tự.
    public static func keys(_ rows: [OrderRow]) -> [String] {
        var seen: [String: Int] = [:]
        return rows.map { r in
            let b = [r.k, r.c, r.o, r.ci].joined(separator: "|")
            let n = (seen[b] ?? 0) + 1
            seen[b] = n
            return n > 1 ? b + "#\(n)" : b
        }
    }

    /// Áp gói thay đổi lên bản gốc; xếp lại như PC: đơn đang chờ lên trước, hạn gần trước.
    public func apply(to base: [OrderRow]) -> [OrderRow] {
        let removed = Set(rm)
        var replace: [String: OrderRow] = [:]
        for u in up where !u.key.isEmpty { replace[u.key] = u.row }
        var out: [(Int, OrderRow)] = []
        out.reserveCapacity(base.count + up.count)
        var used = Set<String>()
        for (i, (k, r)) in zip(Self.keys(base), base).enumerated() {
            if removed.contains(k) { continue }
            if let n = replace[k] { out.append((i, n)); used.insert(k) } else { out.append((i, r)) }
        }
        var next = base.count
        for u in up where !u.key.isEmpty && !used.contains(u.key) && !removed.contains(u.key) {
            used.insert(u.key); out.append((next, u.row)); next += 1
        }
        return out.sorted { a, b in
            let pa = a.1.pd ? 0 : 1, pb = b.1.pd ? 0 : 1
            if pa != pb { return pa < pb }
            let da = a.1.due > 0 ? a.1.due : Int.max, db = b.1.due > 0 ? b.1.due : Int.max
            if da != db { return da < db }
            return a.0 < b.0
        }.map { $0.1 }
    }
}

/// Một dòng trong gói thay đổi: khóa `_k` cùng các trường của dòng.
public struct DeltaRow: Decodable, Equatable {
    public var key: String
    public var row: OrderRow
    public init(key: String, row: OrderRow) { self.key = key; self.row = row }
    private enum K: String, CodingKey { case _k }
    public init(from decoder: Decoder) throws {
        row = try OrderRow(from: decoder)
        key = (try? decoder.container(keyedBy: K.self).decode(String.self, forKey: ._k)) ?? ""
    }
}

public struct SnapshotChunk: Equatable {
    public var snapshotId: String
    public var idx: Int
    public var total: Int
    public var chunk: String
}

public enum SnapshotError: Error { case badBase64, badCompression, badJSON }

/// Ghép các mảnh snapshot_chunk (hoặc snapshot_delta) thành dữ liệu.
/// Giữ song song 2 lần gửi gần nhất: lần gửi mới chen vào không làm bỏ lần đang ghép dở (trước đây PC gửi liên tục thì có thể không bao giờ ghép xong).
public final class SnapshotAssembler {
    private var parts: [String: [Int: String]] = [:]
    private var totals: [String: Int] = [:]
    private var lastDone = ""
    /// Để chẩn đoán: số mảnh đã nhận / tổng của lần gửi gần nhất, và lỗi ghép/giải mã nếu có.
    public private(set) var partsReceived = 0
    public private(set) var partsTotal = 0
    public private(set) var lastError: String?

    public init() {}

    /// Trả về payload khi đủ mảnh; lần gửi cũ hơn lần đã ghép xong bị bỏ.
    public func add(_ c: SnapshotChunk) -> SnapshotPayload? {
        guard let b64 = addRaw(c) else { return nil }
        do {
            let p = try Self.decode(base64: b64)
            lastError = nil
            return p
        } catch {
            lastError = Self.describeFailure(error, base64: b64, total: c.total)
            return nil
        }
    }

    public func addDelta(_ c: SnapshotChunk) -> SnapshotDelta? {
        guard let b64 = addRaw(c) else { return nil }
        do {
            let d = try JSONDecoder().decode(SnapshotDelta.self, from: try Self.inflate(base64: b64))
            lastError = nil
            return d
        } catch {
            lastError = Self.describeFailure(error, base64: b64, total: c.total)
            return nil
        }
    }

    /// Ghép mảnh; trả về chuỗi base64 đầy đủ khi đủ mảnh.
    public func addRaw(_ c: SnapshotChunk) -> String? {
        guard c.total > 0, c.idx >= 0, c.idx < c.total else { return nil }
        let id = c.snapshotId
        if !lastDone.isEmpty && Self.older(id, than: lastDone) { return nil }
        if id == lastDone { return nil }
        if parts[id] == nil {
            parts[id] = [:]; totals[id] = c.total
            // chỉ giữ 2 lần gửi mới nhất
            let ids = parts.keys.sorted { Self.older($0, than: $1) }
            for old in ids.dropLast(2) { parts[old] = nil; totals[old] = nil }
        }
        guard parts[id] != nil, let total = totals[id] else { return nil }
        parts[id]?[c.idx] = c.chunk
        let got = parts[id]?.count ?? 0
        partsReceived = got; partsTotal = total
        guard got == total, let p = parts[id] else { return nil }
        let b64 = (0..<total).compactMap { p[$0] }.joined()
        lastDone = id
        // bỏ luôn các lần gửi cũ hơn lần vừa xong
        for k in parts.keys where k == id || Self.older(k, than: id) { parts[k] = nil; totals[k] = nil }
        return b64
    }

    /// So thời điểm trong mã "snap_<ms>" / "delta_<ms>" (so chuỗi sai khi số chữ số khác nhau).
    static func older(_ a: String, than b: String) -> Bool {
        let na = Int(a.split(separator: "_").last ?? "") ?? 0, nb = Int(b.split(separator: "_").last ?? "") ?? 0
        return na != nb ? na < nb : a < b
    }

    /// Mô tả lỗi kèm kích thước và đầu dữ liệu để biết hỏng ở bước nào (base64, giải nén hay JSON).
    static func describeFailure(_ error: Error, base64: String, total: Int) -> String {
        var s = "\(error) (\(total) mảnh, base64 \(base64.count) ký tự"
        if let data = Data(base64Encoded: base64), let raw = try? (data as NSData).decompressed(using: .zlib) as Data {
            s += ", giải nén \(raw.count) byte, đầu: " + String(decoding: raw.prefix(80), as: UTF8.self)
        }
        return s + ")"
    }

    static func inflate(base64: String) throws -> Data {
        guard let data = Data(base64Encoded: base64) else { throw SnapshotError.badBase64 }
        guard let raw = try? (data as NSData).decompressed(using: .zlib) as Data else { throw SnapshotError.badCompression }
        return raw
    }

    public static func decode(base64: String) throws -> SnapshotPayload {
        let raw = try inflate(base64: base64)
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
