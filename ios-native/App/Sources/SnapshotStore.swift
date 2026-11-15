import SwiftUI
import UserNotifications
import ScannerCore

/// Danh sách đơn chờ hoàn do PC gửi sang; dùng để tra cứu, cảnh báo hạn và gợi ý khi quét.
@MainActor
final class SnapshotStore: ObservableObject {
    @Published var rows: [OrderRow] = []
    @Published var updatedAt: Date?
    @Published var notificationsAllowed = false

    private let assembler = SnapshotAssembler()
    private var index: [String: OrderRow] = [:]
    private var fileURL: URL { FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0].appendingPathComponent("snapshot.json") }

    init() {
        if let data = try? Data(contentsOf: fileURL), let p = try? JSONDecoder().decode(SnapshotPayload.self, from: data) {
            set(p, save: false)
        }
    }

    var overdueCount: Int { rows.filter { Deadline.level(due: $0.due, now: nowMs()) == .overdue }.count }
    var soonCount: Int { rows.filter { Deadline.level(due: $0.due, now: nowMs()) == .soon }.count }
    var hasData: Bool { updatedAt != nil }

    func ingest(_ chunk: SnapshotChunk) {
        if let p = assembler.add(chunk) { set(p, save: true) }
    }

    private func set(_ p: SnapshotPayload, save: Bool) {
        rows = p.rows
        updatedAt = Date(timeIntervalSince1970: Double(p.time) / 1000)
        index = [:]
        for r in p.rows {
            index[r.c.uppercased()] = r
            if !r.o.isEmpty { index[r.o.uppercased()] = r }
        }
        if save, let data = try? JSONEncoder().encode(p) { try? data.write(to: fileURL, options: .atomic) }
        scheduleNotifications()
    }

    func lookup(_ code: String) -> OrderRow? { index[code.uppercased()] }

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
        let planned = NotificationPlanner.plan(rows: rows, now: Date())
        let center = UNUserNotificationCenter.current()
        center.getNotificationSettings { s in
            guard s.authorizationStatus == .authorized || s.authorizationStatus == .provisional else { return }
            center.removeAllPendingNotificationRequests()
            for n in planned {
                let content = UNMutableNotificationContent()
                content.title = n.title
                content.body = n.body
                content.sound = .default
                let secs = max(5, n.fire.timeIntervalSinceNow)
                let trigger = UNTimeIntervalNotificationTrigger(timeInterval: secs, repeats: false)
                center.add(UNNotificationRequest(identifier: n.id, content: content, trigger: trigger))
            }
        }
    }

    private func nowMs() -> Int { Int(Date().timeIntervalSince1970 * 1000) }
}
