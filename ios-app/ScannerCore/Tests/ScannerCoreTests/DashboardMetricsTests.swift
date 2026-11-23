import XCTest
@testable import ScannerCore

final class DashboardMetricsTests: XCTestCase {
    private var cal: Calendar = {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "UTC")!
        c.firstWeekday = 2
        return c
    }()

    private func makeDate(_ year: Int, _ month: Int, _ day: Int, _ hour: Int = 9, _ minute: Int = 0) -> Date {
        var comps = DateComponents()
        comps.year = year; comps.month = month; comps.day = day
        comps.hour = hour; comps.minute = minute; comps.second = 0
        return cal.date(from: comps)!
    }

    private func makeRow(
        k: String = "m",
        c: String,
        o: String = "",
        p: String = "Shopee",
        n: String = "Sản phẩm mẫu",
        pd: Bool = true,
        rv: Bool = false,
        due: Int = 0,
        lv: Int = -1,
        rcd: String = "",
        rw: Int = 0,
        rws: Int = 0,
        rn: String = ""
    ) -> OrderRow {
        var r = OrderRow(k: k, c: c, o: o, p: p, n: n, co: "", sz: "", q: 1, rd: "", due: due, st: rv ? "Hàng đã trả" : "Chưa trả")
        r.pd = pd; r.rv = rv; r.lv = lv; r.rcd = rcd; r.rw = rw; r.rws = rws; r.rn = rn
        return r
    }

    func testEmptyRowsDoesNotDivideByZero() {
        let now = makeDate(2026, 11, 24, 9, 0)
        let res = DashboardMetrics.compute(rows: [], stats: [:], now: now, calendar: cal)
        XCTAssertEqual(res.pendingCount, 0)
        XCTAssertEqual(res.receivedTodayCount, 0)
        XCTAssertEqual(res.overdueCount, 0)
        XCTAssertEqual(res.soonCount, 0)
        XCTAssertEqual(res.partialReturnCount, 0)
        XCTAssertEqual(res.returnWatch, ReturnWatchSummary())
        XCTAssertTrue(res.urgentOrders.isEmpty)
        XCTAssertEqual(res.last7Days.count, 7)
        XCTAssertFalse(res.has7DayData)
        XCTAssertEqual(res.maxDailyCount, 0)
        XCTAssertEqual(res.discrepancyCount, 0)
        for plat in res.platforms {
            XCTAssertEqual(res.pendingRatio(for: plat), 0)
        }
        for day in res.last7Days {
            XCTAssertEqual(res.dailyRatio(for: day), 0)
        }
    }

    func testGreetingByHour() {
        XCTAssertEqual(DashboardMetrics.greeting(for: makeDate(2026, 11, 24, 7, 30), calendar: cal), "Chào buổi sáng")
        XCTAssertEqual(DashboardMetrics.greeting(for: makeDate(2026, 11, 24, 12, 0), calendar: cal), "Chào buổi trưa")
        XCTAssertEqual(DashboardMetrics.greeting(for: makeDate(2026, 11, 24, 15, 45), calendar: cal), "Chào buổi chiều")
        XCTAssertEqual(DashboardMetrics.greeting(for: makeDate(2026, 11, 24, 21, 10), calendar: cal), "Chào buổi tối")
        XCTAssertEqual(DashboardMetrics.greeting(for: makeDate(2026, 11, 24, 2, 0), calendar: cal), "Chào buổi tối")
    }

    func testPlatformAndAlertCounts() {
        let now = makeDate(2026, 11, 24, 10, 0)
        let nowMs = Int(now.timeIntervalSince1970 * 1000)
        let rows = [
            makeRow(c: "SPX01", p: "Shopee", pd: true, due: nowMs - 3_600_000, lv: 2),
            makeRow(c: "SPX02", p: "Shopee", pd: true, due: nowMs + 7_200_000, lv: 1),
            makeRow(c: "TT01", p: "TikTok Shop", pd: true, due: nowMs - 7_200_000, lv: 2),
            makeRow(c: "TT02", p: "TikTok", pd: false, rv: true, rcd: "24/11/2026 08:15"),
            makeRow(c: "LZ01", p: "Lazada", pd: true, due: nowMs + 100_000_000, lv: 0),
            makeRow(k: "a", c: "ARC01", p: "Shopee", pd: false, rv: true, rcd: "24/11/2026 07:00"),
            makeRow(k: "c", c: "CXL01", p: "Shopee", pd: true, due: nowMs - 1_000_000, lv: 2)
        ]

        let res = DashboardMetrics.compute(rows: rows, now: now, calendar: cal)
        XCTAssertEqual(res.pendingCount, 4)        // chỉ tính bảng chính (k == "m")
        XCTAssertEqual(res.overdueCount, 2)        // SPX01 + TT01
        XCTAssertEqual(res.soonCount, 1)           // SPX02
        XCTAssertEqual(res.receivedTodayCount, 2)  // TT02 + ARC01

        let shopee = res.platforms.first { $0.name == "Shopee" }
        let tiktok = res.platforms.first { $0.name == "TikTok" }
        let lazada = res.platforms.first { $0.name == "Lazada" }
        XCTAssertEqual(shopee, PlatformSummary(name: "Shopee", total: 2, pending: 2, overdue: 1))
        XCTAssertEqual(tiktok, PlatformSummary(name: "TikTok", total: 2, pending: 1, overdue: 1))
        XCTAssertEqual(lazada, PlatformSummary(name: "Lazada", total: 1, pending: 1, overdue: 0))
        XCTAssertEqual(res.pendingRatio(for: shopee!), 1.0, accuracy: 0.001)
        XCTAssertEqual(res.pendingRatio(for: tiktok!), 0.5, accuracy: 0.001)
    }

    func testReturnWatch4Stages() {
        let now = makeDate(2026, 11, 24, 10, 0)
        let rows = [
            makeRow(c: "RW1", rw: 100, rws: 1),
            makeRow(c: "RW2", rw: 101, rws: 2),
            makeRow(c: "RW3", rw: 102, rws: 3),
            makeRow(c: "RW4", pd: false, rv: true, rw: 103, rws: 2), // rv = true => chặng 4
            makeRow(c: "RW5", rw: 104, rws: 4),
            makeRow(c: "NO_RW", rw: 0, rws: 3),                      // không nằm trong bảng Hàng Hoàn Về
            makeRow(k: "a", c: "ARC_RW", rw: 105, rws: 3)            // kho lưu trữ không tính
        ]

        let res = DashboardMetrics.compute(rows: rows, now: now, calendar: cal)
        XCTAssertEqual(res.returnWatch, ReturnWatchSummary(stage1: 1, stage2: 1, stage3: 1, stage4: 2))
        XCTAssertEqual(res.returnWatch.active, 3)
        XCTAssertEqual(res.returnWatch.total, 5)
    }

    func testPartialReturnCount() {
        let now = makeDate(2026, 11, 24, 10, 0)
        let rows = [
            makeRow(c: "P1", n: "Áo | Quần", rn: "Áo"),
            makeRow(c: "P2", n: "Giày | Tất", rn: "  Tất  "),
            makeRow(c: "P3", n: "Áo", rn: ""),
            makeRow(c: "P4", n: "Áo", rn: "   ")
        ]

        let res = DashboardMetrics.compute(rows: rows, now: now, calendar: cal)
        XCTAssertEqual(res.partialReturnCount, 2)
    }

    func testUrgentOrdersTop5RedFirstThenYellowSortedByDue() {
        let now = makeDate(2026, 11, 24, 10, 0)
        let nowMs = Int(now.timeIntervalSince1970 * 1000)
        let rows = [
            makeRow(c: "Y2", due: nowMs + 10 * 3_600_000, lv: 1),
            makeRow(c: "Y1", due: nowMs + 2 * 3_600_000, lv: 1),
            makeRow(c: "R3", due: nowMs - 1 * 3_600_000, lv: 2),
            makeRow(c: "R1", due: nowMs - 20 * 3_600_000, lv: 2),
            makeRow(c: "R2", due: nowMs - 5 * 3_600_000, lv: 2),
            makeRow(c: "Y3", due: nowMs + 18 * 3_600_000, lv: 1),
            makeRow(c: "OK", due: nowMs + 96 * 3_600_000, lv: 0),
            makeRow(c: "DONE", pd: false, rv: true, due: nowMs - 50 * 3_600_000, lv: 2)
        ]

        let res = DashboardMetrics.compute(rows: rows, now: now, calendar: cal, urgentLimit: 5)
        XCTAssertEqual(res.urgentOrders.map { $0.row.c }, ["R1", "R2", "R3", "Y1", "Y2"])
        XCTAssertEqual(res.urgentOrders.map { $0.level }, [.overdue, .overdue, .overdue, .soon, .soon])
    }

    func testLast7DaysSeriesAndHidesWhenNoData() {
        let now = makeDate(2026, 11, 24, 14, 0) // Thứ 3 24/11/2026
        let rows = [
            makeRow(c: "D1", pd: false, rv: true, rcd: "24/11/2026 09:00"),
            makeRow(c: "D2", pd: false, rv: true, rcd: "24/11/2026 11:30"),
            makeRow(c: "D3", pd: false, rv: true, rcd: "22/11/2026 15:00"),
            makeRow(c: "D4", pd: false, rv: true, rcd: "18/11/2026 08:00"), // đúng 6 ngày trước -> có tính
            makeRow(c: "OLD", pd: false, rv: true, rcd: "17/11/2026 23:59") // 7 ngày trước -> ngoài khung
        ]

        let res = DashboardMetrics.compute(rows: rows, now: now, calendar: cal)
        XCTAssertTrue(res.has7DayData)
        XCTAssertEqual(res.last7Days.count, 7)
        XCTAssertEqual(res.last7Days.map { $0.count }, [1, 0, 0, 0, 1, 0, 2])
        XCTAssertEqual(res.maxDailyCount, 2)
        XCTAssertTrue(res.last7Days.last?.isToday == true)
        XCTAssertEqual(res.dailyRatio(for: res.last7Days.last!), 1.0, accuracy: 0.001)
        XCTAssertEqual(res.dailyRatio(for: res.last7Days.first!), 0.5, accuracy: 0.001)
    }

    func testDiscrepancyFromStatsDroppedAndSentMismatch() {
        let now = makeDate(2026, 11, 24, 10, 0)
        let rows = [makeRow(c: "A1"), makeRow(c: "A2")]

        let ok = DashboardMetrics.compute(rows: rows, stats: ["sent": 2, "dropped": 0], now: now, calendar: cal)
        XCTAssertEqual(ok.discrepancyCount, 0)

        let dropped = DashboardMetrics.compute(rows: rows, stats: ["sent": 2, "dropped": 3], now: now, calendar: cal)
        XCTAssertEqual(dropped.droppedCount, 3)
        XCTAssertEqual(dropped.sentMismatchCount, 0)
        XCTAssertEqual(dropped.discrepancyCount, 3)

        let mismatch = DashboardMetrics.compute(rows: rows, stats: ["sent": 5, "dropped": 1], now: now, calendar: cal)
        XCTAssertEqual(mismatch.droppedCount, 1)
        XCTAssertEqual(mismatch.sentMismatchCount, 3)
        XCTAssertEqual(mismatch.discrepancyCount, 4)
    }

    func testTodayProgressAndAverage7DaysAndDaySectionTitle() {
        let now = makeDate(2026, 11, 24, 14, 0)
        let rows = [
            makeRow(c: "R1", pd: false, rv: true, rcd: "24/11/2026 09:00"),
            makeRow(c: "R2", pd: false, rv: true, rcd: "24/11/2026 11:00"),
            makeRow(c: "P1", pd: true, rv: false),
            makeRow(c: "P2", pd: true, rv: false)
        ]
        let res = DashboardMetrics.compute(rows: rows, now: now, calendar: cal)
        XCTAssertEqual(res.todayTargetCount, 4)
        XCTAssertEqual(res.todayProgress, 0.5, accuracy: 0.001)
        XCTAssertEqual(res.average7Days, 2.0 / 7.0, accuracy: 0.001)
        XCTAssertGreaterThan(res.average7DaysRatio, 0)

        XCTAssertEqual(DashboardMetrics.daySectionTitle(for: makeDate(2026, 11, 24, 8, 0), now: now, calendar: cal), "Hôm nay")
        XCTAssertEqual(DashboardMetrics.daySectionTitle(for: makeDate(2026, 11, 23, 22, 0), now: now, calendar: cal), "Hôm qua")
        XCTAssertEqual(DashboardMetrics.daySectionTitle(for: makeDate(2026, 11, 20, 10, 0), now: now, calendar: cal), "20/11/2026")
    }
}
