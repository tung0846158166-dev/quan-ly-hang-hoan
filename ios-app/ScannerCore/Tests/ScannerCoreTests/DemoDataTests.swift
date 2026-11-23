import XCTest
@testable import ScannerCore

final class DemoDataTests: XCTestCase {
    private var cal: Calendar = {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "UTC")!
        c.firstWeekday = 2
        return c
    }()

    private func fixedNow() -> Date {
        var comps = DateComponents()
        comps.year = 2026; comps.month = 11; comps.day = 24
        comps.hour = 14; comps.minute = 30; comps.second = 0
        return cal.date(from: comps)!
    }

    func testDemoSnapshotHasExpectedCountsAndCategories() {
        let now = fixedNow()
        let snap = DemoData.snapshot(now: now, calendar: cal)

        XCTAssertEqual(snap.rows.count, 60)
        XCTAssertEqual(snap.count, 60)
        XCTAssertEqual(snap.stats["sent"], 60)
        XCTAssertEqual(snap.stats["dropped"], 0)

        let summary = DashboardMetrics.compute(rows: snap.rows, stats: snap.stats, now: now, calendar: cal)

        // Đủ 3 sàn Shopee, TikTok, Lazada
        let platNames = Set(summary.platforms.filter { $0.total > 0 }.map { $0.name })
        XCTAssertTrue(platNames.contains("Shopee"))
        XCTAssertTrue(platNames.contains("TikTok"))
        XCTAssertTrue(platNames.contains("Lazada"))

        // Đủ quá hạn đỏ, sắp hạn vàng, đang chờ, đã nhận hôm nay
        XCTAssertEqual(summary.overdueCount, 7)
        XCTAssertEqual(summary.soonCount, 8)
        XCTAssertEqual(summary.pendingCount, 26)
        XCTAssertEqual(summary.receivedTodayCount, 6)
        XCTAssertGreaterThan(summary.partialReturnCount, 0)
        XCTAssertEqual(summary.discrepancyCount, 0)

        // Đủ 4 chặng Hàng hoàn về
        XCTAssertGreaterThan(summary.returnWatch.stage1, 0)
        XCTAssertGreaterThan(summary.returnWatch.stage2, 0)
        XCTAssertGreaterThan(summary.returnWatch.stage3, 0)
        XCTAssertGreaterThan(summary.returnWatch.stage4, 0)

        // Dãy 7 ngày đều có dữ liệu
        XCTAssertTrue(summary.has7DayData)
        XCTAssertEqual(summary.last7Days.count, 7)
        XCTAssertTrue(summary.last7Days.allSatisfy { $0.count > 0 })
        XCTAssertGreaterThan(summary.average7Days, 0)

        // Có đơn hủy, lưu trữ, đang theo dõi, hủy sau chuẩn bị
        XCTAssertEqual(snap.rows.filter { $0.k == "c" }.count, 8)
        XCTAssertEqual(snap.rows.filter { $0.k == "a" }.count, 6)
        XCTAssertGreaterThan(snap.rows.filter { $0.wl }.count, 0)
        XCTAssertGreaterThan(snap.rows.filter { $0.isCancelAfterPrep }.count, 0)

        // Đối chiếu stats khớp 100% với rows
        XCTAssertEqual(snap.stats["m_total"], snap.rows.filter { $0.k == "m" }.count)
        XCTAssertEqual(snap.stats["m_danger"], summary.overdueCount)
        XCTAssertEqual(snap.stats["m_warn"], summary.soonCount)
    }

    func testDemoRowsRoundTripCodableAndContainNoRealData() throws {
        let now = fixedNow()
        let snap = DemoData.snapshot(now: now, calendar: cal)

        let data = try JSONEncoder().encode(snap)
        let decoded = try JSONDecoder().decode(SnapshotPayload.self, from: data)
        XCTAssertEqual(decoded.rows.count, snap.rows.count)
        XCTAssertEqual(decoded.stats, snap.stats)

        // Mọi mã vận đơn đều bắt đầu bằng DEMO- và mã đơn bắt đầu bằng DHM- (không chứa dữ liệu thật).
        for row in decoded.rows {
            XCTAssertTrue(row.c.hasPrefix("DEMO-"), "Mã giả phải có tiền tố DEMO-: \(row.c)")
            XCTAssertTrue(row.o.hasPrefix("DHM-"), "Mã đơn giả phải có tiền tố DHM-: \(row.o)")
        }
    }

    func testDemoScanItemsCoverStatusesAndDays() {
        let now = fixedNow()
        let items = DemoData.scanItems(now: now, calendar: cal)
        XCTAssertGreaterThanOrEqual(items.count, 8)

        let statuses = Set(items.map { $0.status })
        XCTAssertTrue(statuses.contains(.success))
        XCTAssertTrue(statuses.contains(.duplicate))
        XCTAssertTrue(statuses.contains(.notFound))
        XCTAssertTrue(statuses.contains(.pending))

        let titles = Set(items.map {
            DashboardMetrics.daySectionTitle(
                for: Date(timeIntervalSince1970: Double($0.time) / 1000),
                now: now,
                calendar: cal
            )
        })
        XCTAssertTrue(titles.contains("Hôm nay"))
        XCTAssertTrue(titles.contains("Hôm qua"))
    }
}
