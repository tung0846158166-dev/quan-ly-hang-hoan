import Foundation

/// Tóm tắt 4 chặng Bảng Hàng Hoàn Về cho màn Tổng quan.
public struct ReturnWatchSummary: Equatable {
    public var stage1: Int = 0   // Đã đồng ý
    public var stage2: Int = 0   // Đang về
    public var stage3: Int = 0   // Đã về shop
    public var stage4: Int = 0   // Đã nhận kho

    public var active: Int { stage1 + stage2 + stage3 }
    public var total: Int { stage1 + stage2 + stage3 + stage4 }

    public init(stage1: Int = 0, stage2: Int = 0, stage3: Int = 0, stage4: Int = 0) {
        self.stage1 = stage1
        self.stage2 = stage2
        self.stage3 = stage3
        self.stage4 = stage4
    }
}

/// Một đơn cần xử lý ngay (Báo Đỏ trước, rồi Báo Vàng, xếp theo hạn gần nhất).
public struct UrgentOrder: Identifiable, Equatable {
    public var row: OrderRow
    public var level: DeadlineLevel
    public var deadlineText: String
    public var id: String { row.id }

    public init(row: OrderRow, level: DeadlineLevel, deadlineText: String) {
        self.row = row
        self.level = level
        self.deadlineText = deadlineText
    }
}

/// Thống kê theo từng sàn (Shopee / TikTok / Lazada / khác) trên bảng chính.
public struct PlatformSummary: Identifiable, Equatable {
    public var name: String
    public var total: Int
    public var pending: Int
    public var overdue: Int
    public var id: String { name }

    public init(name: String, total: Int = 0, pending: Int = 0, overdue: Int = 0) {
        self.name = name
        self.total = total
        self.pending = pending
        self.overdue = overdue
    }
}

/// Số đơn đã nhận kho trong một ngày thuộc dãy 7 ngày gần nhất.
public struct DailyReceiveCount: Identifiable, Equatable {
    public var date: Date
    public var label: String
    public var count: Int
    public var isToday: Bool
    public var id: Date { date }

    public init(date: Date, label: String, count: Int, isToday: Bool) {
        self.date = date
        self.label = label
        self.count = count
        self.isToday = isToday
    }
}

/// Toàn bộ số liệu cho màn hình "Tổng quan" — tính thuần từ `rows` và `stats`, dễ kiểm thử bằng `swift test`.
public struct DashboardSummary: Equatable {
    public var pendingCount: Int = 0
    public var receivedTodayCount: Int = 0
    public var overdueCount: Int = 0
    public var soonCount: Int = 0
    public var partialReturnCount: Int = 0
    public var returnWatch = ReturnWatchSummary()
    public var urgentOrders: [UrgentOrder] = []
    public var platforms: [PlatformSummary] = []
    public var last7Days: [DailyReceiveCount] = []
    public var maxDailyCount: Int = 0
    public var average7Days: Double = 0
    public var has7DayData: Bool = false
    public var droppedCount: Int = 0
    public var sentMismatchCount: Int = 0
    public var discrepancyCount: Int { droppedCount + sentMismatchCount }

    /// Tổng mục tiêu hôm nay (đã nhận hôm nay + còn đang chờ về).
    public var todayTargetCount: Int { receivedTodayCount + pendingCount }

    /// Tỉ lệ hoàn thành vòng tiến độ hôm nay trong khoảng 0...1 (an toàn khi rỗng, không chia cho 0).
    public var todayProgress: Double {
        guard todayTargetCount > 0 else { return 0 }
        return min(1.0, Double(receivedTodayCount) / Double(todayTargetCount))
    }

    public init() {}

    /// Tỉ lệ thanh ngang cho một sàn (an toàn khi không có đơn chờ, không chia cho 0).
    public func pendingRatio(for platform: PlatformSummary) -> Double {
        let maxPending = platforms.map { $0.pending }.max() ?? 0
        guard maxPending > 0 else { return 0 }
        return Double(platform.pending) / Double(maxPending)
    }

    /// Tỉ lệ chiều cao cột trong biểu đồ 7 ngày (an toàn khi rỗng, không chia cho 0).
    public func dailyRatio(for day: DailyReceiveCount) -> Double {
        guard maxDailyCount > 0 else { return 0 }
        return Double(day.count) / Double(maxDailyCount)
    }

    /// Tỉ lệ đường trung bình trong biểu đồ 7 ngày (0...1, không chia cho 0).
    public var average7DaysRatio: Double {
        guard maxDailyCount > 0 else { return 0 }
        return min(1.0, average7Days / Double(maxDailyCount))
    }
}

public enum DashboardMetrics {
    /// Lời chào theo khung giờ trong ngày.
    public static func greeting(for date: Date, calendar: Calendar = .current) -> String {
        let hour = calendar.component(.hour, from: date)
        switch hour {
        case 5..<11: return "Chào buổi sáng"
        case 11..<14: return "Chào buổi trưa"
        case 14..<18: return "Chào buổi chiều"
        default: return "Chào buổi tối"
        }
    }

    /// Đơn hoàn một phần: PC gửi trường `rn` (tên sản phẩm đã hoàn) không rỗng.
    public static func isPartialReturn(_ row: OrderRow) -> Bool {
        !row.rn.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    /// Chuẩn hóa tên sàn hiển thị trên Tổng quan: Shopee / TikTok / Lazada / tên khác.
    public static func normalizedPlatform(_ raw: String) -> String {
        let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty { return "Khác" }
        let lower = trimmed.lowercased()
        if lower.contains("shopee") { return "Shopee" }
        if lower.contains("tiktok") { return "TikTok" }
        if lower.contains("lazada") { return "Lazada" }
        return trimmed
    }

    /// Nhãn ngắn thứ trong tuần tiếng Việt cho biểu đồ 7 ngày (CN, T2..T7).
    public static func shortWeekdayLabel(for date: Date, calendar: Calendar = .current) -> String {
        let wd = calendar.component(.weekday, from: date) // 1 = Chủ Nhật, 2 = Thứ 2 ... 7 = Thứ 7
        switch wd {
        case 1: return "CN"
        case 2: return "T2"
        case 3: return "T3"
        case 4: return "T4"
        case 5: return "T5"
        case 6: return "T6"
        case 7: return "T7"
        default: return ""
        }
    }

    /// Nhãn nhóm ngày cho màn Lịch sử: "Hôm nay", "Hôm qua", hoặc "dd/MM/yyyy".
    public static func daySectionTitle(for date: Date, now: Date = Date(), calendar: Calendar = .current) -> String {
        let dayStart = calendar.startOfDay(for: date)
        let todayStart = calendar.startOfDay(for: now)
        if dayStart == todayStart { return "Hôm nay" }
        if let yesterday = calendar.date(byAdding: .day, value: -1, to: todayStart), dayStart == yesterday {
            return "Hôm qua"
        }
        let comps = calendar.dateComponents([.day, .month, .year], from: dayStart)
        let d = comps.day ?? 1
        let m = comps.month ?? 1
        let y = comps.year ?? 2026
        return String(format: "%02d/%02d/%04d", d, m, y)
    }

    /// Tính toàn bộ số liệu Tổng quan trong một lần duyệt dữ liệu.
    public static func compute(
        rows: [OrderRow],
        stats: [String: Int] = [:],
        now: Date = Date(),
        calendar: Calendar = .current,
        urgentLimit: Int = 5
    ) -> DashboardSummary {
        var summary = DashboardSummary()
        let nowMs = Int(now.timeIntervalSince1970 * 1000)
        let todayStart = calendar.startOfDay(for: now)

        // Chuẩn bị khung 7 ngày gần nhất (từ 6 ngày trước tới hôm nay).
        var dayStarts: [Date] = []
        dayStarts.reserveCapacity(7)
        for offset in stride(from: -6, through: 0, by: 1) {
            if let d = calendar.date(byAdding: .day, value: offset, to: todayStart) {
                dayStarts.append(d)
            }
        }
        let daySet = Set(dayStarts)
        var dailyMap: [Date: Int] = [:]

        var urgentCandidates: [(row: OrderRow, level: DeadlineLevel)] = []
        var platMap: [String: PlatformSummary] = [
            "Shopee": PlatformSummary(name: "Shopee"),
            "TikTok": PlatformSummary(name: "TikTok"),
            "Lazada": PlatformSummary(name: "Lazada")
        ]
        var extraPlatNames: [String] = []

        for r in rows {
            if isPartialReturn(r) {
                summary.partialReturnCount += 1
            }

            // Đếm đơn đã nhận theo ngày nhận `rcd` cho "Đã nhận hôm nay" và dãy 7 ngày.
            if r.rv, let recvDate = OrderStats.parseDate(r.rcd, calendar: calendar) {
                let recvDay = calendar.startOfDay(for: recvDate)
                if recvDay == todayStart {
                    summary.receivedTodayCount += 1
                }
                if daySet.contains(recvDay) {
                    dailyMap[recvDay, default: 0] += 1
                }
            }

            // Bảng Hàng Hoàn Về 4 chặng.
            if r.inReturnWatch {
                switch r.returnWatchStage {
                case 1: summary.returnWatch.stage1 += 1
                case 2: summary.returnWatch.stage2 += 1
                case 3: summary.returnWatch.stage3 += 1
                default: summary.returnWatch.stage4 += 1
                }
            }

            // Các số liệu chính trên bảng chính (`k == "m"`).
            if r.k == "m" {
                let pname = normalizedPlatform(r.p)
                if platMap[pname] == nil {
                    platMap[pname] = PlatformSummary(name: pname)
                    extraPlatNames.append(pname)
                }
                platMap[pname]?.total += 1

                if r.pd {
                    summary.pendingCount += 1
                    platMap[pname]?.pending += 1

                    let level = r.alertLevel(now: nowMs)
                    if level == .overdue {
                        summary.overdueCount += 1
                        platMap[pname]?.overdue += 1
                        urgentCandidates.append((r, .overdue))
                    } else if level == .soon {
                        summary.soonCount += 1
                        urgentCandidates.append((r, .soon))
                    }
                }
            }
        }

        // Chọn tối đa `urgentLimit` đơn cần xử lý ngay: đỏ trước, rồi vàng, sắp theo hạn gần nhất (due > 0 trước).
        urgentCandidates.sort { a, b in
            let rankA = a.level == .overdue ? 0 : 1
            let rankB = b.level == .overdue ? 0 : 1
            if rankA != rankB { return rankA < rankB }
            let dueA = a.row.due > 0 ? a.row.due : Int.max
            let dueB = b.row.due > 0 ? b.row.due : Int.max
            if dueA != dueB { return dueA < dueB }
            return a.row.c < b.row.c
        }
        summary.urgentOrders = urgentCandidates.prefix(max(0, urgentLimit)).map { item in
            UrgentOrder(
                row: item.row,
                level: item.level,
                deadlineText: Deadline.text(due: item.row.due, now: nowMs)
            )
        }

        // Sắp xếp sàn: Shopee, TikTok, Lazada trước, sau đó các sàn khác nếu có.
        let orderedNames = ["Shopee", "TikTok", "Lazada"] + extraPlatNames.sorted()
        summary.platforms = orderedNames.compactMap { name in
            guard let item = platMap[name] else { return nil }
            if ["Shopee", "TikTok", "Lazada"].contains(name) || item.total > 0 {
                return item
            }
            return nil
        }

        // Dãy 7 ngày gần nhất.
        var total7 = 0
        summary.last7Days = dayStarts.map { day in
            let c = dailyMap[day] ?? 0
            total7 += c
            return DailyReceiveCount(
                date: day,
                label: shortWeekdayLabel(for: day, calendar: calendar),
                count: c,
                isToday: day == todayStart
            )
        }
        summary.maxDailyCount = summary.last7Days.map { $0.count }.max() ?? 0
        summary.average7Days = summary.last7Days.isEmpty ? 0 : Double(total7) / Double(summary.last7Days.count)
        summary.has7DayData = total7 > 0

        // Cảnh báo lệch số đơn với máy tính (dropped > 0 hoặc sent != rows.count).
        summary.droppedCount = max(0, stats["dropped"] ?? 0)
        if let sent = stats["sent"] {
            summary.sentMismatchCount = abs(sent - rows.count)
        }

        return summary
    }
}
