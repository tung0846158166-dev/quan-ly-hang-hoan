import Foundation

public struct PlannedNotice: Equatable {
    public var id: String
    public var title: String
    public var body: String
    public var fire: Date
}

/// Lập kế hoạch thông báo cục bộ (iOS giới hạn 64 thông báo chờ nên chỉ lấy tối đa 50 đơn + 7 bản tóm tắt).
public enum NotificationPlanner {
    public static func plan(rows: [OrderRow], now: Date, calendar: Calendar = .current,
                            maxOrders: Int = 50, summaryDays: Int = 7, summaryHour: Int = 8, summaryMinute: Int = 30) -> [PlannedNotice] {
        var out: [PlannedNotice] = []
        let nowMs = Int(now.timeIntervalSince1970 * 1000)

        var perOrder: [PlannedNotice] = []
        for r in rows where r.due > 0 {
            let warnMs = r.due - 12 * 3_600_000
            let desc = [r.n, [r.co, r.sz].filter { !$0.isEmpty }.joined(separator: "/")].filter { !$0.isEmpty }.joined(separator: " ")
            if warnMs > nowMs + 60_000 {
                perOrder.append(PlannedNotice(id: "warn-" + r.id, title: "Đơn sắp đến hạn",
                    body: "\(r.c) \(desc): còn 12 giờ", fire: Date(timeIntervalSince1970: Double(warnMs) / 1000)))
            } else if r.due > nowMs + 60_000 {
                perOrder.append(PlannedNotice(id: "due-" + r.id, title: "Đơn đến hạn",
                    body: "\(r.c) \(desc): đã đến hạn mà chưa về", fire: Date(timeIntervalSince1970: Double(r.due) / 1000)))
            }
        }
        perOrder.sort { $0.fire < $1.fire }
        out += perOrder.prefix(maxOrders)

        let start = calendar.startOfDay(for: now)
        for i in 0..<summaryDays {
            guard let day = calendar.date(byAdding: .day, value: i, to: start),
                  let fire = calendar.date(bySettingHour: summaryHour, minute: summaryMinute, second: 0, of: day),
                  fire > now else { continue }
            let fireMs = Int(fire.timeIntervalSince1970 * 1000)
            let overdue = rows.filter { $0.due > 0 && $0.due <= fireMs }.count
            let soon = rows.filter { $0.due > fireMs && $0.due <= fireMs + 86_400_000 }.count
            if overdue + soon == 0 { continue }
            out.append(PlannedNotice(id: "summary-\(i)", title: "Đơn hoàn cần xử lý",
                                     body: "\(overdue) đơn quá hạn, \(soon) đơn sắp đến hạn", fire: fire))
        }
        return out
    }
}
