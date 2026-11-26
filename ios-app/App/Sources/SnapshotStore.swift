import SwiftUI
import UserNotifications
import ScannerCore

/// Danh sách đơn chờ hoàn do PC gửi sang; dùng để tra cứu, cảnh báo hạn và gợi ý khi quét.
@MainActor
final class SnapshotStore: ObservableObject {
    @Published var rows: [OrderRow] = []
    @Published var updatedAt: Date?
    @Published var notificationsAllowed = false
    @Published var assembleInfo = ""
    @Published var stats: [String: Int] = [:]
    // Số đếm tính sẵn một lần mỗi khi danh sách đổi (tránh quét lại ~5000 đơn trong body).
    @Published private(set) var overdueCount = 0
    @Published private(set) var soonCount = 0
    @Published private(set) var alertCount = 0
    /// Tăng mỗi khi danh sách đổi (kể cả đổi tại chỗ như ghi chú/theo dõi) để màn hình vẽ lại danh sách đã lọc.
    @Published var revision = 0
    /// Thông báo đã ẩn trên điện thoại (khóa = id đơn + hạn; không đồng bộ với máy tính).
    @Published var dismissed: Set<String> = Set(UserDefaults.standard.stringArray(forKey: "dismissedAlerts") ?? [])
    /// Chế độ xem thử dữ liệu mẫu (chỉ trong bộ nhớ, tuyệt đối không ghi đè dữ liệu thật).
    @Published private(set) var isDemoMode = false

    private var savedRealRows: [OrderRow] = []
    private var savedRealStats: [String: Int] = [:]
    private var savedRealUpdatedAt: Date?
    private var savedRealDismissed: Set<String> = []

    private let assembler = SnapshotAssembler()
    private let deltaAssembler = SnapshotAssembler()
    /// Danh sách đầy đủ gần nhất PC gửi (bản gốc để áp các gói "phần thay đổi").
    private var baseRows: [OrderRow] = []
    private var baseSid = ""
    private var lastDeltaSeq = 0
    /// Gói thay đổi không khớp bản gốc trên điện thoại: xin PC gửi lại danh sách đầy đủ.
    var onNeedFull: () -> Void = {}
    /// Gọi mỗi khi danh sách đơn thật (`!isDemoMode`) vừa được nạp từ đĩa hoặc cập nhật từ PC (dùng để gắn ảnh quét chờ khớp).
    var onRowsUpdated: (() -> Void)?
    /// Giải mã, dựng chỉ mục và ghi file chạy ở luồng nền để không làm đứng giao diện khi đang đồng bộ 5.000+ đơn.
    private let work = DispatchQueue(label: "snapshot.work", qos: .userInitiated)
    private var progressReceived = 0
    private var progressTotal = 0
    private var progressError: String?
    private var lastInfoAt = Date.distantPast
    private var index = SnapshotIndex()

    private static var docsDir: URL {
        FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first
            ?? FileManager.default.temporaryDirectory
    }
    private var fileURL: URL { Self.docsDir.appendingPathComponent("snapshot.json") }
    private var baseURL: URL { Self.docsDir.appendingPathComponent("snapshot_base.json") }

    init() {
        let fURL = fileURL
        let bURL = baseURL
        let savedSeq = UserDefaults.standard.integer(forKey: "snapshotLastDeltaSeq")
        let savedSid = UserDefaults.standard.string(forKey: "snapshotLastDeltaSid") ?? ""
        // Đọc cache JSON (~2-4 MB với 5.000 đơn) trên luồng nền để không đóng băng màn hình lúc mở app.
        work.async { [weak self] in
            let cached: SnapshotPayload? = (try? Data(contentsOf: fURL)).flatMap { try? JSONDecoder().decode(SnapshotPayload.self, from: $0) }
            let cachedIdx = cached.map { SnapshotIndex(rows: $0.rows) }
            let base: SnapshotPayload? = (try? Data(contentsOf: bURL)).flatMap { try? JSONDecoder().decode(SnapshotPayload.self, from: $0) }
            Task { @MainActor [weak self] in
                guard let self = self else { return }
                if self.baseSid.isEmpty, let b = base {
                    self.baseRows = b.rows
                    self.baseSid = b.sid
                    if savedSid == b.sid && savedSeq > 0 { self.lastDeltaSeq = savedSeq }
                }
                // Chỉ nạp bản đĩa nếu chưa nhận gói mới từ mạng và không ở chế độ mẫu.
                if self.updatedAt == nil, !self.isDemoMode, let p = cached, let idx = cachedIdx {
                    self.apply(p, index: idx)
                }
            }
        }
    }

    var hasData: Bool { updatedAt != nil || isDemoMode }
    /// Đang ghép dở (thiếu mảnh, chưa lỗi).
    var incomplete: Bool { !isDemoMode && progressError == nil && progressTotal > 0 && progressReceived < progressTotal }

    /// Xóa bộ ghép mảnh dở khi đổi mã phòng.
    func resetAssemblers() {
        progressReceived = 0
        progressTotal = 0
        progressError = nil
        assembleInfo = ""
        let asm = assembler, dasm = deltaAssembler
        work.async {
            asm.reset()
            dasm.reset()
        }
    }

    /// Bật/tắt chế độ xem thử với dữ liệu mẫu. Dữ liệu thật được giữ nguyên trong bộ nhớ và trên đĩa.
    func setDemoMode(_ on: Bool) {
        if on {
            guard !isDemoMode else { return }
            savedRealRows = rows
            savedRealStats = stats
            savedRealUpdatedAt = updatedAt
            savedRealDismissed = dismissed
            isDemoMode = true

            let demo = DemoData.snapshot()
            rows = demo.rows
            stats = demo.stats
            updatedAt = Date(timeIntervalSince1970: Double(demo.time) / 1000)
            dismissed = []
            index = SnapshotIndex(rows: demo.rows)
            recount()
            revision += 1
        } else {
            guard isDemoMode else { return }
            isDemoMode = false
            rows = savedRealRows
            stats = savedRealStats
            updatedAt = savedRealUpdatedAt
            dismissed = savedRealDismissed
            savedRealRows = []
            savedRealStats = [:]
            savedRealUpdatedAt = nil
            savedRealDismissed = []
            index = SnapshotIndex(rows: rows)
            recount()
            revision += 1
        }
    }

    func ingest(_ chunk: SnapshotChunk) {
        let asm = assembler
        let url = fileURL, burl = baseURL
        work.async { [weak self] in
            let p = asm.add(chunk)
            let received = asm.partsReceived, total = asm.partsTotal, error = asm.lastError
            var built: SnapshotIndex? = nil
            if let p = p {
                built = SnapshotIndex(rows: p.rows)
                if let data = try? JSONEncoder().encode(p) {
                    try? data.write(to: url, options: .atomic)
                    try? data.write(to: burl, options: .atomic)
                }
            }
            Task { @MainActor [weak self] in
                self?.finishIngest(payload: p, index: built, received: received, total: total, error: error)
            }
        }
    }

    private func finishIngest(payload: SnapshotPayload?, index built: SnapshotIndex?, received: Int, total: Int, error: String?) {
        progressReceived = received; progressTotal = total; progressError = error
        // Chỉ cập nhật dòng tiến độ vài lần mỗi giây (mỗi mảnh một lần làm các màn hình vẽ lại liên tục).
        let now = Date()
        if payload != nil || error != nil || now.timeIntervalSince(lastInfoAt) > 0.5 {
            lastInfoAt = now
            assembleInfo = error.map { "Lỗi giải mã: \($0)" } ?? "Đã ghép \(received)/\(total) mảnh"
        }
        if let p = payload, let idx = built {
            baseRows = p.rows
            baseSid = p.sid
            lastDeltaSeq = 0
            UserDefaults.standard.set(p.sid, forKey: "snapshotLastDeltaSid")
            UserDefaults.standard.set(0, forKey: "snapshotLastDeltaSeq")
            apply(p, index: idx)
        }
    }

    /// Gói "phần thay đổi" từ PC: áp lên bản gốc (nhỏ, vài mảnh) thay vì chờ cả danh sách ~100 mảnh.
    func ingestDelta(_ chunk: SnapshotChunk) {
        let asm = deltaAssembler
        let base = baseRows, sid = baseSid
        let url = fileURL
        work.async { [weak self] in
            guard let d = asm.addDelta(chunk) else { return }
            guard !sid.isEmpty, d.base == sid else {
                Task { @MainActor [weak self] in self?.onNeedFull() }
                return
            }
            let rows = d.apply(to: base)
            let p = SnapshotPayload(time: d.time, count: rows.count, rows: rows, stats: d.stats, sid: sid)
            let built = SnapshotIndex(rows: rows)
            Task { @MainActor [weak self] in
                guard let self = self, self.baseSid == sid, d.seq > self.lastDeltaSeq else { return }
                self.lastDeltaSeq = d.seq
                UserDefaults.standard.set(sid, forKey: "snapshotLastDeltaSid")
                UserDefaults.standard.set(d.seq, forKey: "snapshotLastDeltaSeq")
                self.apply(p, index: built)
                self.work.async {
                    if let data = try? JSONEncoder().encode(p) { try? data.write(to: url, options: .atomic) }
                }
            }
        }
    }

    /// Các mã đã quét hoàn kho trên điện thoại mà PC chưa xác nhận: danh sách mới từ PC chưa có lượt quét này nên không được ghi đè.
    var unconfirmedReturnCodes: () -> Set<String> = { [] }
    /// Thời điểm quét (ms epoch) theo mã vận đơn cho các lượt quét chưa được PC xác nhận để điền giờ nhận tại kho tức thì.
    var unconfirmedReturnScanTimes: () -> [String: Int] = { [:] }
    /// Các thao tác sửa cục bộ chưa được PC xác nhận (ghi chú, đổi trạng thái, xóa, nhận đơn hủy).
    var pendingLocalOverlays: () -> [LocalEditOverlay] = { [] }

    private static let localTimeFormatter: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "dd/MM/yyyy HH:mm:ss"
        return f
    }()

    private static func formatLocalTimestamp(_ date: Date) -> String {
        localTimeFormatter.string(from: date)
    }

    private func apply(_ p: SnapshotPayload, index built: SnapshotIndex) {
        if isDemoMode {
            // Đang xem dữ liệu mẫu: lưu nền dữ liệu thật vừa nhận để khi tắt mẫu sẽ thấy ngay bản mới nhất.
            savedRealRows = p.rows
            savedRealStats = p.stats
            savedRealUpdatedAt = Date(timeIntervalSince1970: Double(p.time) / 1000)
            return
        }
        var waitingTimes = unconfirmedReturnScanTimes()
        if waitingTimes.isEmpty {
            for code in unconfirmedReturnCodes() { waitingTimes[code] = 0 }
        }
        let overlays = pendingLocalOverlays()
        let merged = LocalEditOverlay.apply(
            to: p.rows,
            unconfirmedReturnScanTimes: waitingTimes,
            overlays: overlays
        )
        rows = merged.rows
        stats = p.stats
        updatedAt = Date(timeIntervalSince1970: Double(p.time) / 1000)
        index = merged.touched ? SnapshotIndex(rows: rows) : built
        recount()
        revision += 1
        scheduleNotifications()
        onRowsUpdated?()
    }

    /// Chế độ cloud: danh sách lấy thẳng từ bản sao cloud trên máy (`CloudParcelStore.rows`). Cloud đã là nguồn gốc nên
    /// không phủ thao tác "chờ PC xác nhận"; mọi màn hình đọc `rows` như cũ.
    func applyCloud(rows newRows: [OrderRow], index prebuilt: SnapshotIndex? = nil, at: Date) {
        if isDemoMode {
            savedRealRows = newRows
            savedRealUpdatedAt = at
            return
        }
        rows = newRows
        updatedAt = at
        index = prebuilt ?? SnapshotIndex(rows: newRows)
        recount()
        revision += 1
        scheduleNotifications()
        onRowsUpdated?()
    }

    /// Tính lại số quá hạn / sắp đến hạn / thông báo; gọi khi danh sách, danh sách ẩn đổi hoặc theo thời gian (mỗi phút).
    func recount() {
        let now = nowMs()
        var o = 0, s = 0, a = 0
        for r in rows where r.pd {
            switch r.alertLevel(now: now) {
            case .overdue: o += 1; if !dismissed.contains(dismissKey(r)) { a += 1 }
            case .soon: s += 1; if !dismissed.contains(dismissKey(r)) { a += 1 }
            default: break
            }
        }
        if overdueCount != o { overdueCount = o }
        if soonCount != s { soonCount = s }
        if alertCount != a { alertCount = a }
    }

    /// Tra cứu nhanh O(1) theo mã vận đơn / mã đơn / mã phụ.
    func lookup(_ code: String) -> OrderRow? { index.lookup(code, in: rows) }

    /// Tra cứu nhanh O(1) theo `OrderRow.id` (tránh duyệt tuyến tính 5.000 đơn trong DetailView/Router).
    func row(byId id: String) -> OrderRow? { index.row(byId: id, in: rows) }

    /// Kiểm tra mã có khớp từ 2 kiện đang chờ trở lên hay không (Quy tắc 2 & 3: tránh tự nhận nhầm kiện A/B).
    func isAmbiguousPending(_ code: String) -> Bool { index.isAmbiguousPending(code) }

    /// Chỉ mục hiện tại (chỉ đọc) phục vụ tra cứu/gắn ảnh cục bộ.
    var snapshotIndex: SnapshotIndex { index }

    /// Khớp mã quét vào kiện trong snapshot (duy nhất 1 kiện / mơ hồ nhiều ứng viên / không tìm thấy).
    func matchParcel(for rawCode: String) -> ParcelMatchResult {
        index.matchParcel(for: rawCode, in: rows)
    }

    /// Ghi lại danh sách hiện tại (giữ nguyên thời điểm đồng bộ) sau khi thao tác cục bộ.
    private func commit(rebuildIndex: Bool = false) {
        if rebuildIndex {
            index = SnapshotIndex(rows: rows)
        }
        recount()
        revision += 1
        guard !isDemoMode else { return }
        let t = updatedAt.map { Int($0.timeIntervalSince1970 * 1000) } ?? nowMs()
        let payload = SnapshotPayload(time: t, count: rows.count, rows: rows, stats: stats, sid: baseSid)
        let url = fileURL
        work.async {
            if let data = try? JSONEncoder().encode(payload) { try? data.write(to: url, options: .atomic) }
        }
        scheduleNotifications()
    }

    /// Sửa ghi chú / theo dõi ngay trên điện thoại (PC nhận cùng nội dung và gửi lại danh sách theo dõi chính thức).
    func applyLocalNote(rowId: String, note: String, watch: Bool) {
        guard let i = index.idToIndex[rowId] ?? rows.firstIndex(where: { $0.id == rowId }), i < rows.count else { return }
        rows[i].wl = watch
        rows[i].wr = watch ? note : ""
        commit(rebuildIndex: false)
    }

    /// Sửa đơn ngay trên điện thoại (trạng thái `st`); PC làm theo rồi gửi lại danh sách chính thức.
    func applyLocalEdit(rowId: String, fields: [String: Any], receivedAt: Date = Date()) {
        guard let i = index.idToIndex[rowId] ?? rows.firstIndex(where: { $0.id == rowId }), i < rows.count else { return }
        var pdChanged = false
        if let v = fields["n"] as? String { rows[i].n = v }
        if let v = fields["co"] as? String { rows[i].co = v }
        if let v = fields["sz"] as? String { rows[i].sz = v }
        if let v = fields["q"] as? Int { rows[i].q = max(1, v) }
        if let v = fields["ty"] as? String, !v.isEmpty { rows[i].ty = v }
        if let v = fields["st"] as? String {
            let oldPd = rows[i].pd
            rows[i].st = v
            let received = v == "Hàng đã trả" || v == "Đã nhận"
            rows[i].rv = received
            rows[i].pd = !(received || v == "Không cần nhận" || v == "Bị mất")
            pdChanged = (oldPd != rows[i].pd)
            if received {
                if rows[i].rcd.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    rows[i].rcd = Self.formatLocalTimestamp(receivedAt)
                }
            } else {
                rows[i].rcd = ""
            }
        }
        commit(rebuildIndex: pdChanged)
    }

    /// Đánh dấu đã nhận / chưa nhận ngay trên điện thoại cho các đơn hủy đã chọn.
    func applyCancelledReceivedLocal(ids: Set<String>, received: Bool) {
        var changed = false
        let stamp = Self.formatLocalTimestamp(Date())
        for i in rows.indices where rows[i].k == "c" && ids.contains(rows[i].ci) && rows[i].rv != received {
            rows[i].rv = received
            rows[i].pd = !received
            rows[i].lv = 0
            rows[i].rcd = received ? stamp : ""
            changed = true
        }
        if changed { commit(rebuildIndex: true) }
    }

    func removeLocal(rowId: String) {
        let before = rows.count
        rows.removeAll { $0.id == rowId }
        if rows.count != before { commit(rebuildIndex: true) }
    }

    /// PC báo danh sách đơn đang theo dõi + ghi chú đã đổi: cập nhật các đơn ở bảng chính cho khớp.
    /// Quy tắc 2: ưu tiên khớp đúng mã vận đơn `c`; chỉ fallback sang `o` khi bản ghi theo dõi không có `c` (tránh gán nhầm kiện anh em A/B).
    func applyWatchSync(_ items: [WatchItem]) {
        var byTrack: [String: String] = [:]
        var byOrderOnly: [String: String] = [:]
        for it in items {
            let tc = it.c.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
            let oc = it.o.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
            if !tc.isEmpty {
                byTrack[tc] = it.r
            } else if !oc.isEmpty {
                byOrderOnly[oc] = it.r
            }
        }
        var changed = false
        for i in rows.indices where rows[i].k == "m" {
            let rc = rows[i].c.uppercased()
            let ro = rows[i].o.uppercased()
            var reason: String? = rc.isEmpty ? nil : byTrack[rc]
            if reason == nil && !ro.isEmpty {
                reason = byOrderOnly[ro]
            }
            let wl = reason != nil
            let wr = reason ?? ""
            if rows[i].wl != wl || rows[i].wr != wr {
                rows[i].wl = wl
                rows[i].wr = wr
                changed = true
            }
        }
        if changed { commit(rebuildIndex: false) }
    }

    // MARK: bảng thông báo trên điện thoại
    private func dismissKey(_ r: OrderRow) -> String { "\(r.id)|\(r.due)" }

    /// Đơn đang chờ đã quá hạn / sắp đến hạn trong 24 giờ, bỏ các đơn đã ẩn.
    func alerts() -> (overdue: [OrderRow], soon: [OrderRow]) {
        Self.computeAlerts(rows: rows, dismissed: dismissed, now: nowMs())
    }

    nonisolated static func computeAlerts(rows: [OrderRow], dismissed: Set<String>, now: Int) -> (overdue: [OrderRow], soon: [OrderRow]) {
        var overdue: [OrderRow] = [], soon: [OrderRow] = []
        for r in rows where r.pd {
            let key = "\(r.id)|\(r.due)"
            if dismissed.contains(key) { continue }
            switch r.alertLevel(now: now) {
            case .overdue: overdue.append(r)
            case .soon: soon.append(r)
            default: break
            }
        }
        overdue.sort { $0.due < $1.due }   // quá hạn lâu nhất lên đầu
        soon.sort { $0.due < $1.due }
        return (overdue, soon)
    }

    func dismissAlerts(_ list: [OrderRow]) {
        for r in list { dismissed.insert(dismissKey(r)) }
        if !isDemoMode {
            UserDefaults.standard.set(Array(dismissed), forKey: "dismissedAlerts")
        }
        recount()
    }

    func restoreDismissed() {
        dismissed = []
        if !isDemoMode {
            UserDefaults.standard.removeObject(forKey: "dismissedAlerts")
        }
        recount()
    }

    /// Id đơn nằm trong id thông báo ("warn-<id>" hoặc "due-<id>"); bản tóm tắt hằng ngày không có.
    static func rowId(fromNoticeId id: String) -> String? {
        if id.hasPrefix("warn-") { return String(id.dropFirst(5)) }
        if id.hasPrefix("due-") { return String(id.dropFirst(4)) }
        return nil
    }

    // MARK: thông báo cục bộ
    func requestNotificationPermission() {
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound, .badge]) { ok, _ in
            Task { @MainActor in
                self.notificationsAllowed = ok
                if ok { self.scheduleNotifications() }
            }
        }
    }

    func refreshAuthorization() {
        UNUserNotificationCenter.current().getNotificationSettings { s in
            let ok = s.authorizationStatus == .authorized || s.authorizationStatus == .provisional
            Task { @MainActor in self.notificationsAllowed = ok }
        }
    }

    func scheduleNotifications() {
        guard !isDemoMode else { return }
        let pending = rows.filter { $0.pd }
        work.async {
            let planned = NotificationPlanner.plan(rows: pending, now: Date())
            let center = UNUserNotificationCenter.current()
            center.getNotificationSettings { s in
                guard s.authorizationStatus == .authorized || s.authorizationStatus == .provisional else { return }
                center.removeAllPendingNotificationRequests()
                for n in planned {
                    let content = UNMutableNotificationContent()
                    content.title = n.title
                    content.body = n.body
                    content.sound = .default
                    if let rid = Self.rowId(fromNoticeId: n.id) { content.userInfo = ["rowId": rid] }
                    let secs = max(5, n.fire.timeIntervalSinceNow)
                    let trigger = UNTimeIntervalNotificationTrigger(timeInterval: secs, repeats: false)
                    center.add(UNNotificationRequest(identifier: n.id, content: content, trigger: trigger))
                }
            }
        }
    }

    private func nowMs() -> Int { Int(Date().timeIntervalSince1970 * 1000) }
}
