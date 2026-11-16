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
    // Số đếm tính sẵn một lần mỗi khi danh sách đổi (trước đây mỗi lần giao diện vẽ lại đều quét cả ~3000 đơn).
    @Published private(set) var overdueCount = 0
    @Published private(set) var soonCount = 0
    @Published private(set) var alertCount = 0
    /// Tăng mỗi khi danh sách đổi (kể cả đổi tại chỗ như ghi chú/theo dõi) để màn hình vẽ lại danh sách đã lọc.
    @Published var revision = 0
    /// Thông báo đã ẩn trên điện thoại (khóa = id đơn + hạn; không đồng bộ với máy tính).
    @Published var dismissed: Set<String> = Set(UserDefaults.standard.stringArray(forKey: "dismissedAlerts") ?? [])

    private let assembler = SnapshotAssembler()
    /// Giải mã, dựng chỉ mục và ghi file chạy ở luồng nền để không làm đứng giao diện khi đang đồng bộ.
    private let work = DispatchQueue(label: "snapshot.work", qos: .userInitiated)
    private var progressReceived = 0
    private var progressTotal = 0
    private var progressError: String?
    private var lastInfoAt = Date.distantPast
    private var index: [String: OrderRow] = [:]
    private var fileURL: URL { FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0].appendingPathComponent("snapshot.json") }

    init() {
        if let data = try? Data(contentsOf: fileURL), let p = try? JSONDecoder().decode(SnapshotPayload.self, from: data) {
            set(p, save: false)
        }
    }

    var hasData: Bool { updatedAt != nil }
    /// Đang ghép dở (thiếu mảnh, chưa lỗi).
    var incomplete: Bool { progressError == nil && progressTotal > 0 && progressReceived < progressTotal }

    func ingest(_ chunk: SnapshotChunk) {
        let asm = assembler
        let url = fileURL
        work.async { [weak self] in
            let p = asm.add(chunk)
            let received = asm.partsReceived, total = asm.partsTotal, error = asm.lastError
            var built: [String: OrderRow]? = nil
            if let p = p {
                built = Self.buildIndex(p.rows)
                if let data = try? JSONEncoder().encode(p) { try? data.write(to: url, options: .atomic) }
            }
            Task { @MainActor [weak self] in
                self?.finishIngest(payload: p, index: built, received: received, total: total, error: error)
            }
        }
    }

    private func finishIngest(payload: SnapshotPayload?, index built: [String: OrderRow]?, received: Int, total: Int, error: String?) {
        progressReceived = received; progressTotal = total; progressError = error
        // Chỉ cập nhật dòng tiến độ vài lần mỗi giây (mỗi mảnh một lần làm các màn hình vẽ lại liên tục).
        let now = Date()
        if payload != nil || error != nil || now.timeIntervalSince(lastInfoAt) > 0.5 {
            lastInfoAt = now
            assembleInfo = error.map { "Lỗi giải mã: \($0)" } ?? "Đã ghép \(received)/\(total) mảnh"
        }
        if let p = payload, let idx = built { apply(p, index: idx) }
    }

    /// Đơn đứng trước thắng (danh sách đã xếp đơn đang chờ lên trước), nên mã trùng giữa bảng chính/lưu trữ ưu tiên đơn đang chờ.
    nonisolated private static func buildIndex(_ rows: [OrderRow]) -> [String: OrderRow] {
        var index: [String: OrderRow] = [:]
        index.reserveCapacity(rows.count * 2)
        for r in rows {
            let kc = r.c.uppercased()
            if index[kc] == nil { index[kc] = r }
            if !r.o.isEmpty { let ko = r.o.uppercased(); if index[ko] == nil { index[ko] = r } }
        }
        return index
    }

    /// Các mã đã quét hoàn kho trên điện thoại mà PC chưa xác nhận: danh sách mới từ PC chưa có lượt quét này nên không được ghi đè.
    var unconfirmedReturnCodes: () -> Set<String> = { [] }

    private func apply(_ p: SnapshotPayload, index built: [String: OrderRow]) {
        rows = p.rows
        stats = p.stats
        updatedAt = Date(timeIntervalSince1970: Double(p.time) / 1000)
        index = built
        let waiting = unconfirmedReturnCodes()
        if !waiting.isEmpty {
            var touched = false
            for i in rows.indices where rows[i].pd && waiting.contains(rows[i].c.uppercased()) {
                rows[i].st = "Hàng đã trả"; rows[i].rv = true; rows[i].pd = false; touched = true
            }
            if touched { index = Self.buildIndex(rows) }
        }
        recount()
        scheduleNotifications()
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

    private func set(_ p: SnapshotPayload, save: Bool) {
        rows = p.rows
        stats = p.stats
        updatedAt = Date(timeIntervalSince1970: Double(p.time) / 1000)
        index = Self.buildIndex(p.rows)
        recount()
        if save {
            let url = fileURL
            work.async { if let data = try? JSONEncoder().encode(p) { try? data.write(to: url, options: .atomic) } }
        }
        scheduleNotifications()
    }

    func lookup(_ code: String) -> OrderRow? { index[code.uppercased()] }

    /// Ghi lại danh sách hiện tại (giữ nguyên thời điểm đồng bộ) sau khi một đơn đổi theo dõi/ghi chú.
    private func commit() {
        let t = updatedAt.map { Int($0.timeIntervalSince1970 * 1000) } ?? Int(Date().timeIntervalSince1970 * 1000)
        set(SnapshotPayload(time: t, count: rows.count, rows: rows, stats: stats), save: true)
        revision += 1
    }

    /// Sửa ghi chú / theo dõi ngay trên điện thoại (PC nhận cùng nội dung và gửi lại danh sách theo dõi chính thức).
    func applyLocalNote(rowId: String, note: String, watch: Bool) {
        guard let i = rows.firstIndex(where: { $0.id == rowId }) else { return }
        rows[i].wl = watch
        rows[i].wr = watch ? note : ""
        commit()
    }

    /// Sửa đơn ngay trên điện thoại (n, co, sz, q, st, ty); PC làm theo rồi gửi lại danh sách chính thức.
    func applyLocalEdit(rowId: String, fields: [String: Any]) {
        guard let i = rows.firstIndex(where: { $0.id == rowId }) else { return }
        if let v = fields["n"] as? String { rows[i].n = v }
        if let v = fields["co"] as? String { rows[i].co = v }
        if let v = fields["sz"] as? String { rows[i].sz = v }
        if let v = fields["q"] as? Int { rows[i].q = max(1, v) }
        if let v = fields["ty"] as? String, !v.isEmpty { rows[i].ty = v }
        if let v = fields["st"] as? String {
            rows[i].st = v
            let received = v == "Hàng đã trả" || v == "Đã nhận"
            rows[i].rv = received
            rows[i].pd = !(received || v == "Không cần nhận" || v == "Bị mất" || v == "Hư hỏng")
        }
        commit()
    }

    func removeLocal(rowId: String) {
        rows.removeAll { $0.id == rowId }
        commit()
    }

    /// PC báo danh sách đơn đang theo dõi + ghi chú đã đổi: cập nhật các đơn ở bảng chính cho khớp.
    func applyWatchSync(_ items: [WatchItem]) {
        var byKey: [String: String] = [:]
        for it in items {
            if !it.c.isEmpty { byKey[it.c.uppercased()] = it.r }
            if !it.o.isEmpty && byKey[it.o.uppercased()] == nil { byKey[it.o.uppercased()] = it.r }
        }
        var changed = false
        for i in rows.indices where rows[i].k == "m" {
            var reason: String? = byKey[rows[i].c.uppercased()]
            if reason == nil && !rows[i].o.isEmpty { reason = byKey[rows[i].o.uppercased()] }
            let wl = reason != nil
            let wr = reason ?? ""
            if rows[i].wl != wl || rows[i].wr != wr { rows[i].wl = wl; rows[i].wr = wr; changed = true }
        }
        if changed { commit() }
    }

    // MARK: bảng thông báo trên điện thoại
    private func dismissKey(_ r: OrderRow) -> String { "\(r.id)|\(r.due)" }

    /// Đơn đang chờ đã quá hạn / sắp đến hạn trong 24 giờ, bỏ các đơn đã ẩn.
    func alerts() -> (overdue: [OrderRow], soon: [OrderRow]) {
        let now = nowMs()
        var overdue: [OrderRow] = [], soon: [OrderRow] = []
        for r in rows where r.pd && !dismissed.contains(dismissKey(r)) {
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
        UserDefaults.standard.set(Array(dismissed), forKey: "dismissedAlerts")
        recount()
    }

    func restoreDismissed() {
        dismissed = []
        UserDefaults.standard.removeObject(forKey: "dismissedAlerts")
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
        let planned = NotificationPlanner.plan(rows: rows.filter { $0.pd }, now: Date())
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

    private func nowMs() -> Int { Int(Date().timeIntervalSince1970 * 1000) }
}
