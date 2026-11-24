import Foundation

/// Một dòng trong danh sách đơn chờ hoàn mà PC gửi sang (khóa ngắn để gói nhỏ).
public struct OrderRow: Codable, Identifiable, Equatable, Hashable {
    public var k: String      // "m" = bảng chính, "c" = đơn hủy, "a" = kho lưu trữ
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
    public var lv: Int = -1         // mức cảnh báo do PC tính: 2 = Báo Đỏ (>=24h), 1 = Báo Vàng (>=12h), 0 = an toàn; -1 = PC cũ không gửi
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
    /// Tên chặng an toàn (không bao giờ ngoài phạm vi mảng).
    public var returnWatchStageName: String {
        let idx = returnWatchStage
        guard idx >= 0 && idx < Self.returnWatchStageNames.count else { return "" }
        return Self.returnWatchStageNames[idx]
    }

    /// Mọi mã tra được đơn này: mã chính, mã đơn và các mã khác.
    public var lookupCodes: [String] {
        ([c, o] + x.split(separator: " ").map(String.init)).map { $0.uppercased() }.filter { !$0.isEmpty }
    }

    /// Khóa định danh an toàn cho SwiftUI List/ForEach: phân biệt kiện khác `ci` hoặc khác mã đơn `o` khi `c` trùng.
    public var id: String {
        if !ci.isEmpty { return "\(k)|\(c)|\(ci)" }
        if !o.isEmpty && o.uppercased() != c.uppercased() { return "\(k)|\(c)|\(o)" }
        return k + "|" + c
    }

    /// Thuộc nhóm đơn hủy: nằm trong bảng đơn hủy, hoặc ở bảng chính nhưng loại đơn là "Đơn hủy".
    public var isCancelled: Bool { k == "c" || ty == "Đơn hủy" }
    public var isArchived: Bool { k == "a" }
    /// Mức cảnh báo giống cột Hạn Cần Xử Lý trên PC (quá hạn = Báo Đỏ, sắp đến hạn = Báo Vàng); PC cũ thì tính theo `due`.
    public func alertLevel(now: Int) -> DeadlineLevel {
        if lv >= 0 { return lv >= 2 ? .overdue : (lv == 1 ? .soon : .normal) }
        return Deadline.level(due: due, now: now)
    }
}

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

public struct SnapshotPayload: Codable, Equatable {
    public var time: Int
    public var count: Int
    public var rows: [OrderRow]
    /// Bảng đối chiếu số đơn do PC tính trên dữ liệu gốc (PC cũ không gửi thì rỗng).
    public var stats: [String: Int] = [:]
    /// Mã của lần gửi danh sách đầy đủ (PC dùng làm bản gốc cho các gói "phần thay đổi"); PC cũ không gửi.
    public var sid: String = ""

    public init(time: Int, count: Int, rows: [OrderRow], stats: [String: Int] = [:], sid: String = "") {
        self.time = time; self.count = count; self.rows = rows; self.stats = stats; self.sid = sid
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
private enum PayloadKeys: String, CodingKey { case time, count, rows, stats, sid }

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

extension SnapshotPayload {
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: PayloadKeys.self)
        let rows = try c.decode([OrderRow].self, forKey: .rows)
        let stats = (try? c.decode([String: Int].self, forKey: .stats)) ?? [:]
        self.init(time: c.flexInt(.time), count: c.flexInt(.count, default: rows.count), rows: rows, stats: stats, sid: c.flexString(.sid))
    }
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

    public init(time: Int, base: String, seq: Int, count: Int = 0, up: [DeltaRow], rm: [String], stats: [String: Int] = [:]) {
        self.time = time; self.base = base; self.seq = seq; self.count = count; self.up = up; self.rm = rm; self.stats = stats
    }

    private enum Keys: String, CodingKey { case time, base, seq, count, up, rm, stats }
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: Keys.self)
        let up = try c.decode([DeltaRow].self, forKey: .up)
        let rm = (try? c.decode([String].self, forKey: .rm)) ?? []
        let stats = (try? c.decode([String: Int].self, forKey: .stats)) ?? [:]
        self.init(time: c.flexInt(.time), base: c.flexString(.base), seq: c.flexInt(.seq), count: c.flexInt(.count), up: up, rm: rm, stats: stats)
    }

    /// Khóa từng dòng, giống hệt PC (phoneRowKeys): k|c|o|ci, khóa trùng đánh số #2, #3 theo thứ tự.
    public static func keys(_ rows: [OrderRow]) -> [String] {
        var seen: [String: Int] = [:]
        seen.reserveCapacity(rows.count)
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
        replace.reserveCapacity(up.count)
        for u in up where !u.key.isEmpty { replace[u.key] = u.row }
        var out: [(Int, OrderRow)] = []
        out.reserveCapacity(base.count + up.count)
        var used = Set<String>()
        used.reserveCapacity(up.count)
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

    public init(snapshotId: String, idx: Int, total: Int, chunk: String) {
        self.snapshotId = snapshotId
        self.idx = idx
        self.total = total
        self.chunk = chunk
    }
}

public enum SnapshotError: Error { case badBase64, badCompression, badJSON }

/// Chỉ mục nhẹ (lưu chỉ số `Int` thay vì nhân bản struct `OrderRow`) để tra cứu O(1) theo mã vận đơn và `row.id`
/// trên 5.000+ đơn mà vẫn tiết kiệm RAM, đồng thời phát hiện mã trùng nhiều kiện đang chờ (Quy tắc 2 & 3).
public struct SnapshotIndex: Equatable {
    public private(set) var codeToIndex: [String: Int] = [:]
    public private(set) var idToIndex: [String: Int] = [:]
    public private(set) var ambiguousPendingCodes: Set<String> = []

    public init() {}

    public init(rows: [OrderRow]) {
        codeToIndex.reserveCapacity(rows.count * 2)
        idToIndex.reserveCapacity(rows.count)
        var pendingHits: [String: Int] = [:]
        pendingHits.reserveCapacity(rows.count)

        for (i, r) in rows.enumerated() {
            if idToIndex[r.id] == nil { idToIndex[r.id] = i }
            let kc = r.c.uppercased()
            if !kc.isEmpty && codeToIndex[kc] == nil { codeToIndex[kc] = i }
            if r.pd {
                var seenInRow = Set<String>()
                for code in r.lookupCodes where seenInRow.insert(code).inserted {
                    let count = (pendingHits[code] ?? 0) + 1
                    pendingHits[code] = count
                    if count >= 2 { ambiguousPendingCodes.insert(code) }
                }
            }
        }
        for (i, r) in rows.enumerated() {
            for k in r.lookupCodes.dropFirst() where codeToIndex[k] == nil {
                codeToIndex[k] = i
            }
        }
    }

    public func lookup(_ code: String, in rows: [OrderRow]) -> OrderRow? {
        guard let idx = codeToIndex[code.uppercased()], idx >= 0, idx < rows.count else { return nil }
        return rows[idx]
    }

    public func row(byId id: String, in rows: [OrderRow]) -> OrderRow? {
        guard let idx = idToIndex[id], idx >= 0, idx < rows.count else { return nil }
        return rows[idx]
    }

    public func isAmbiguousPending(_ code: String) -> Bool {
        ambiguousPendingCodes.contains(code.uppercased())
    }
}

/// Thao tác sửa cục bộ trên iPhone đang chờ PC xác nhận (`pc_ack`):
/// giữ nguyên thay đổi khi gói snapshot/delta định kỳ từ PC đổ về trước khi PC kịp xử lý xong lệnh.
public struct LocalEditOverlay: Codable, Equatable {
    public enum Action: String, Codable, Equatable {
        case note
        case editStatus
        case delete
        case cancelReceive
    }

    public var requestId: String
    public var action: Action
    public var rowId: String
    public var note: String?
    public var watch: Bool?
    public var status: String?
    public var cancelIds: [String]?
    public var received: Bool?
    public var timeMs: Int

    public init(requestId: String, action: Action, rowId: String = "", note: String? = nil, watch: Bool? = nil,
                status: String? = nil, cancelIds: [String]? = nil, received: Bool? = nil, timeMs: Int = 0) {
        self.requestId = requestId
        self.action = action
        self.rowId = rowId
        self.note = note
        self.watch = watch
        self.status = status
        self.cancelIds = cancelIds
        self.received = received
        self.timeMs = timeMs
    }

    private static func formatTimestamp(_ ms: Int, calendar: Calendar = .current) -> String {
        let d = Date(timeIntervalSince1970: Double(ms) / 1000)
        let comps = calendar.dateComponents([.day, .month, .year, .hour, .minute, .second], from: d)
        return String(format: "%02d/%02d/%04d %02d:%02d:%02d",
                      comps.day ?? 1, comps.month ?? 1, comps.year ?? 2026,
                      comps.hour ?? 0, comps.minute ?? 0, comps.second ?? 0)
    }

    /// Ghép cả lượt quét hoàn kho chưa xác nhận VÀ các lệnh sửa cục bộ chưa xác nhận lên `rows` mới nhận từ PC.
    /// Quy tắc 2 & 3: lượt quét hoàn kho CHỈ đánh dấu cục bộ khi khớp DUY NHẤT 1 kiện đang chờ (trùng >= 2 kiện -> để PC xử lý `ambiguous`).
    public static func apply(
        to sourceRows: [OrderRow],
        unconfirmedReturnScanTimes: [String: Int],
        overlays: [LocalEditOverlay],
        calendar: Calendar = .current
    ) -> (rows: [OrderRow], touched: Bool) {
        var rows = sourceRows
        var touched = false

        if !unconfirmedReturnScanTimes.isEmpty {
            // Đếm số kiện đang chờ khớp với từng mã quét -> chỉ nhận cục bộ khi khớp ĐÚNG 1 kiện (Quy tắc 2 & 3).
            var matchIndices: [String: [Int]] = [:]
            for i in rows.indices where rows[i].pd {
                var seenInRow = Set<String>()
                for code in rows[i].lookupCodes where unconfirmedReturnScanTimes[code] != nil && seenInRow.insert(code).inserted {
                    matchIndices[code, default: []].append(i)
                }
            }
            for (code, idxs) in matchIndices where idxs.count == 1 {
                let i = idxs[0]
                guard rows[i].pd else { continue }
                rows[i].st = "Hàng đã trả"
                rows[i].rv = true
                rows[i].pd = false
                if rows[i].rcd.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
                   let ms = unconfirmedReturnScanTimes[code], ms > 0 {
                    rows[i].rcd = formatTimestamp(ms, calendar: calendar)
                }
                touched = true
            }
        }

        guard !overlays.isEmpty else { return (rows, touched) }
        let ordered = overlays.sorted { $0.timeMs < $1.timeMs }
        for ov in ordered {
            switch ov.action {
            case .note:
                if let i = rows.firstIndex(where: { $0.id == ov.rowId }) {
                    let w = ov.watch ?? true
                    rows[i].wl = w
                    rows[i].wr = w ? (ov.note ?? "") : ""
                    touched = true
                }
            case .editStatus:
                if let st = ov.status, let i = rows.firstIndex(where: { $0.id == ov.rowId }) {
                    rows[i].st = st
                    let received = st == "Hàng đã trả" || st == "Đã nhận"
                    rows[i].rv = received
                    rows[i].pd = !(received || st == "Không cần nhận" || st == "Bị mất")
                    if received {
                        if rows[i].rcd.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, ov.timeMs > 0 {
                            rows[i].rcd = formatTimestamp(ov.timeMs, calendar: calendar)
                        }
                    } else {
                        rows[i].rcd = ""
                    }
                    touched = true
                }
            case .delete:
                let before = rows.count
                rows.removeAll { $0.id == ov.rowId }
                if rows.count != before { touched = true }
            case .cancelReceive:
                guard let idList = ov.cancelIds, !idList.isEmpty, let rec = ov.received else { continue }
                let idSet = Set(idList)
                let stamp = ov.timeMs > 0 ? formatTimestamp(ov.timeMs, calendar: calendar) : ""
                for i in rows.indices where rows[i].k == "c" && idSet.contains(rows[i].ci) {
                    rows[i].rv = rec
                    rows[i].pd = !rec
                    rows[i].lv = 0
                    rows[i].rcd = rec ? stamp : ""
                    touched = true
                }
            }
        }
        return (rows, touched)
    }
}

/// Ghép các mảnh snapshot_chunk (hoặc snapshot_delta) thành dữ liệu.
/// Giữ song song 2 lần gửi gần nhất: lần gửi mới chen vào không làm bỏ lần đang ghép dở.
/// Chỉ chốt `lastDone` SAU KHI giải mã thành công để lần gửi lại (lượt 2) vẫn phục hồi được nếu lượt 1 hỏng mảnh.
public final class SnapshotAssembler {
    private static let maxTotalChunks = 20_000
    private var parts: [String: [Int: String]] = [:]
    private var totals: [String: Int] = [:]
    private var lastDone = ""
    /// Để chẩn đoán: số mảnh đã nhận / tổng của lần gửi gần nhất, và lỗi ghép/giải mã nếu có.
    public private(set) var partsReceived = 0
    public private(set) var partsTotal = 0
    public private(set) var lastError: String?

    public init() {}

    public func reset() {
        parts.removeAll()
        totals.removeAll()
        lastDone = ""
        partsReceived = 0
        partsTotal = 0
        lastError = nil
    }

    /// Trả về payload khi đủ mảnh và giải mã thành công; lần gửi cũ hơn lần đã ghép xong bị bỏ.
    public func add(_ c: SnapshotChunk) -> SnapshotPayload? {
        guard let b64 = collectComplete(c) else { return nil }
        do {
            let p = try Self.decode(base64: b64)
            commitDone(c.snapshotId)
            lastError = nil
            return p
        } catch {
            // Hỏng mảnh: xóa tập mảnh lỗi của id này nhưng KHÔNG khóa `lastDone` để lượt gửi lại cùng id vẫn ghép lại được.
            parts[c.snapshotId] = nil
            totals[c.snapshotId] = nil
            lastError = Self.describeFailure(error, base64: b64, total: c.total)
            return nil
        }
    }

    public func addDelta(_ c: SnapshotChunk) -> SnapshotDelta? {
        guard let b64 = collectComplete(c) else { return nil }
        do {
            let d = try JSONDecoder().decode(SnapshotDelta.self, from: try Self.inflate(base64: b64))
            commitDone(c.snapshotId)
            lastError = nil
            return d
        } catch {
            parts[c.snapshotId] = nil
            totals[c.snapshotId] = nil
            lastError = Self.describeFailure(error, base64: b64, total: c.total)
            return nil
        }
    }

    /// Ghép mảnh; trả về chuỗi base64 đầy đủ khi đủ mảnh (chốt hoàn tất ngay cho caller tự giải mã).
    public func addRaw(_ c: SnapshotChunk) -> String? {
        guard let b64 = collectComplete(c) else { return nil }
        commitDone(c.snapshotId)
        return b64
    }

    private func collectComplete(_ c: SnapshotChunk) -> String? {
        guard c.total > 0, c.total <= Self.maxTotalChunks, c.idx >= 0, c.idx < c.total else { return nil }
        let id = c.snapshotId
        guard !id.isEmpty else { return nil }
        if !lastDone.isEmpty && (id == lastDone || Self.older(id, than: lastDone)) { return nil }
        if parts[id] == nil {
            parts[id] = [:]
            totals[id] = c.total
            // chỉ giữ 2 lần gửi mới nhất
            let ids = parts.keys.sorted { Self.older($0, than: $1) }
            for old in ids.dropLast(2) { parts[old] = nil; totals[old] = nil }
        }
        guard parts[id] != nil, let total = totals[id], c.total == total else { return nil }
        parts[id]?[c.idx] = c.chunk
        // Báo tiến độ của lần gửi mới nhất đang theo dõi (tránh nhảy lùi khi mảnh cũ chen vào).
        if let newest = parts.keys.max(by: { Self.older($0, than: $1) }), newest == id {
            partsReceived = parts[id]?.count ?? 0
            partsTotal = total
        }
        guard let p = parts[id], p.count == total else { return nil }
        var out = String()
        out.reserveCapacity(total * 2400)
        for i in 0..<total {
            guard let piece = p[i] else { return nil }
            out += piece
        }
        return out
    }

    private func commitDone(_ id: String) {
        lastDone = id
        for k in parts.keys where k == id || Self.older(k, than: id) {
            parts[k] = nil
            totals[k] = nil
        }
    }

    /// So thời điểm trong mã "snap_<ms>" / "delta_<ms>" (so chuỗi sai khi số chữ số khác nhau).
    static func older(_ a: String, than b: String) -> Bool {
        if a == b { return false }
        let na = Int(a.split(separator: "_").last ?? "") ?? 0
        let nb = Int(b.split(separator: "_").last ?? "") ?? 0
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
