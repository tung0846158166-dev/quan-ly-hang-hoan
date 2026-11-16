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
    /// Tăng mỗi khi danh sách đổi (kể cả đổi tại chỗ như ghi chú/theo dõi) để màn hình vẽ lại danh sách đã lọc.
    @Published var revision = 0
    /// Thông báo đã ẩn trên điện thoại (khóa = id đơn + hạn; không đồng bộ với máy tính).
    @Published var dismissed: Set<String> = Set(UserDefaults.standard.stringArray(forKey: "dismissedAlerts") ?? [])

    private let assembler = SnapshotAssembler()
    private var index: [String: OrderRow] = [:]
    private var fileURL: URL { FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0].appendingPathComponent("snapshot.json") }

    init() {
        if let data = try? Data(contentsOf: fileURL), let p = try? JSONDecoder().decode(SnapshotPayload.self, from: data) {
            set(p, save: false)
        }
    }

    var overdueCount: Int { rows.filter { $0.pd && $0.alertLevel(now: nowMs()) == .overdue }.count }
    var soonCount: Int { rows.filter { $0.pd && $0.alertLevel(now: nowMs()) == .soon }.count }
    var hasData: Bool { updatedAt != nil }
    /// Đang ghép dở (thiếu mảnh, chưa lỗi).
    var incomplete: Bool { assembler.lastError == nil && assembler.partsTotal > 0 && assembler.partsReceived < assembler.partsTotal }

    func ingest(_ chunk: SnapshotChunk) {
        let p = assembler.add(chunk)
        assembleInfo = assembler.lastError.map { "Lỗi giải mã: \($0)" } ?? "Đã ghép \(assembler.partsReceived)/\(assembler.partsTotal) mảnh"
        if let p = p { set(p, save: true) }
    }

    private func set(_ p: SnapshotPayload, save: Bool) {
        rows = p.rows
        stats = p.stats
        updatedAt = Date(timeIntervalSince1970: Double(p.time) / 1000)
        index = [:]
        // Đơn đứng trước thắng (danh sách đã xếp đơn đang chờ lên trước), nên mã trùng giữa bảng chính/lưu trữ ưu tiên đơn đang chờ.
        for r in p.rows {
            let kc = r.c.uppercased()
            if index[kc] == nil { index[kc] = r }
            if !r.o.isEmpty { let ko = r.o.uppercased(); if index[ko] == nil { index[ko] = r } }
        }
        if save, let data = try? JSONEncoder().encode(p) { try? data.write(to: fileURL, options: .atomic) }
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

    var alertCount: Int { let a = alerts(); return a.overdue.count + a.soon.count }

    func dismissAlerts(_ list: [OrderRow]) {
        for r in list { dismissed.insert(dismissKey(r)) }
        UserDefaults.standard.set(Array(dismissed), forKey: "dismissedAlerts")
    }

    func restoreDismissed() {
        dismissed = []
        UserDefaults.standard.removeObject(forKey: "dismissedAlerts")
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
