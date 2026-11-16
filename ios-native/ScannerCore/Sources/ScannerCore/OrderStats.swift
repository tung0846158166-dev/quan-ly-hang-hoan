import Foundation

public enum StatsPeriod: String, CaseIterable, Identifiable {
    case day, week, month
    public var id: String { rawValue }
    public var title: String { self == .day ? "Ngày" : (self == .week ? "Tuần" : "Tháng") }
}

/// Số đơn trong một ngày / tuần / tháng (theo ngày hoàn/hủy khách bấm).
public struct StatsBucket: Identifiable, Equatable {
    public let start: Date
    public var total = 0
    public var received = 0
    public var pending = 0
    public var overdue = 0     // còn chờ và đã quá hạn
    public var soon = 0        // còn chờ và sắp đến hạn trong 24 giờ
    public var id: Date { start }
    public init(start: Date) { self.start = start }
}

public struct StatsResult: Equatable {
    public var buckets: [StatsBucket] = []   // mới nhất trước
    public var unknownDate = 0               // đơn không đọc được ngày
    public var total = 0
    public var received = 0
    public var pending = 0
    public var overdue = 0
    public var soon = 0
    public init() {}
}

public enum OrderStats {
    /// Đơn hủy: bảng đơn hủy (k = "c") và đơn loại "Đơn hủy". Đơn hoàn: bảng chính + kho lưu trữ, còn lại.
    public static func isReturn(_ r: OrderRow) -> Bool { !r.isCancelled }
    public static func isCancel(_ r: OrderRow) -> Bool { r.isCancelled }

    /// Đọc ngày dạng 31/12/2026, 31-12-2026 14:30, 2026-12-31 hoặc 2026/12/31 08:00:15.
    public static func parseDate(_ raw: String, calendar: Calendar = .current) -> Date? {
        let s = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        if s.isEmpty { return nil }
        let parts = s.split(whereSeparator: { $0 == " " || $0 == "T" }).map(String.init)
        guard let datePart = parts.first else { return nil }
        let nums = datePart.split(whereSeparator: { $0 == "/" || $0 == "-" || $0 == "." }).compactMap { Int($0) }
        guard nums.count == 3 else { return nil }
        var y = 0, m = 0, d = 0
        if datePart.split(whereSeparator: { $0 == "/" || $0 == "-" || $0 == "." }).first?.count == 4 {
            y = nums[0]; m = nums[1]; d = nums[2]
        } else {
            d = nums[0]; m = nums[1]; y = nums[2]
            if y < 100 { y += 2000 }
        }
        guard (1...12).contains(m), (1...31).contains(d), y >= 2000, y <= 2100 else { return nil }
        var comps = DateComponents()
        comps.year = y; comps.month = m; comps.day = d
        if parts.count > 1 {
            let t = parts[1].split(separator: ":").compactMap { Int($0) }
            if t.count >= 2 { comps.hour = t[0]; comps.minute = t[1]; if t.count > 2 { comps.second = t[2] } }
        }
        return calendar.date(from: comps)
    }

    public static func bucketStart(_ date: Date, period: StatsPeriod, calendar: Calendar = .current) -> Date {
        var cal = calendar
        switch period {
        case .day:
            return cal.startOfDay(for: date)
        case .week:
            cal.firstWeekday = 2   // tuần bắt đầu từ thứ Hai
            return cal.dateInterval(of: .weekOfYear, for: date)?.start ?? cal.startOfDay(for: date)
        case .month:
            return cal.dateInterval(of: .month, for: date)?.start ?? cal.startOfDay(for: date)
        }
    }

    /// `rows` đã lọc sẵn (chỉ đơn hoàn hoặc chỉ đơn hủy). `now`: mili giây epoch.
    public static func compute(rows: [OrderRow], period: StatsPeriod, now: Int, calendar: Calendar = .current) -> StatsResult {
        var result = StatsResult()
        var map: [Date: StatsBucket] = [:]
        for r in rows {
            var level = DeadlineLevel.normal
            if r.pd { level = r.alertLevel(now: now) }
            result.total += 1
            if r.rv { result.received += 1 }
            if r.pd { result.pending += 1 }
            if r.pd && level == .overdue { result.overdue += 1 }
            if r.pd && level == .soon { result.soon += 1 }
            guard let d = parseDate(r.rd, calendar: calendar) else { result.unknownDate += 1; continue }
            let key = bucketStart(d, period: period, calendar: calendar)
            var b = map[key] ?? StatsBucket(start: key)
            b.total += 1
            if r.rv { b.received += 1 }
            if r.pd { b.pending += 1 }
            if r.pd && level == .overdue { b.overdue += 1 }
            if r.pd && level == .soon { b.soon += 1 }
            map[key] = b
        }
        result.buckets = map.values.sorted { $0.start > $1.start }
        return result
    }
}
