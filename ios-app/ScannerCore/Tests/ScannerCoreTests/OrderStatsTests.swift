import XCTest
@testable import ScannerCore

final class OrderStatsTests: XCTestCase {
    var cal: Calendar = {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "UTC")!
        c.firstWeekday = 2
        return c
    }()

    private func row(_ k: String, _ c: String, rd: String, pd: Bool = true, rv: Bool = false, ty: String = "", due: Int = 0, lv: Int = -1) -> OrderRow {
        var r = OrderRow(k: k, c: c, o: "", p: "Shopee", n: "", co: "", sz: "", q: 1, rd: rd, due: due, st: "")
        r.pd = pd; r.rv = rv; r.ty = ty; r.lv = lv
        return r
    }

    func testParseDateFormats() {
        XCTAssertNotNil(OrderStats.parseDate("31/12/2026 14:30", calendar: cal))
        XCTAssertNotNil(OrderStats.parseDate("2026-12-31", calendar: cal))
        XCTAssertNotNil(OrderStats.parseDate("05-01-2026 08:00:15", calendar: cal))
        XCTAssertNil(OrderStats.parseDate("", calendar: cal))
        XCTAssertNil(OrderStats.parseDate("dd/mm/yyyy", calendar: cal))
        XCTAssertNil(OrderStats.parseDate("32/13/2026", calendar: cal))
        let d = OrderStats.parseDate("05/01/2026", calendar: cal)!
        XCTAssertEqual(cal.component(.day, from: d), 5)
        XCTAssertEqual(cal.component(.month, from: d), 1)
    }

    func testBucketsByDayWeekMonth() {
        let rows = [
            row("m", "A", rd: "02/11/2026 10:00", pd: false, rv: true),
            row("a", "B", rd: "02/11/2026 23:00", pd: false, rv: true),
            row("m", "C", rd: "03/11/2026"),
            row("m", "D", rd: "09/11/2026"),
            row("m", "E", rd: ""),
        ]
        let day = OrderStats.compute(rows: rows, period: .day, now: 0, calendar: cal)
        XCTAssertEqual(day.buckets.count, 3)
        XCTAssertEqual(day.unknownDate, 1)
        XCTAssertEqual(day.total, 5)
        XCTAssertEqual(day.buckets.last?.total, 2)   // 02/11 có 2 đơn, nằm cuối vì xếp mới nhất trước
        let week = OrderStats.compute(rows: rows, period: .week, now: 0, calendar: cal)
        XCTAssertEqual(week.buckets.count, 2)        // 02-08/11 và 09-15/11 (thứ Hai 02/11/2026, 09/11/2026)
        XCTAssertEqual(week.buckets.last?.total, 3)
        let month = OrderStats.compute(rows: rows, period: .month, now: 0, calendar: cal)
        XCTAssertEqual(month.buckets.count, 1)
        XCTAssertEqual(month.buckets[0].received, 2)
        XCTAssertEqual(month.pending, 3)
    }

    func testOverdueAndSoonOnlyForPending() {
        let now = 1_000_000_000_000
        let rows = [
            row("m", "A", rd: "02/11/2026", due: now - 1000),                 // quá hạn
            row("m", "B", rd: "02/11/2026", due: now + 3_600_000),            // sắp đến hạn (< 24h)
            row("m", "C", rd: "02/11/2026", pd: false, rv: true, due: now - 1000), // đã nhận: không tính quá hạn
            row("m", "D", rd: "02/11/2026", due: now + 5 * 86_400_000),       // còn xa
        ]
        let r = OrderStats.compute(rows: rows, period: .month, now: now, calendar: cal)
        XCTAssertEqual(r.overdue, 1)
        XCTAssertEqual(r.soon, 1)
        XCTAssertEqual(r.buckets[0].overdue, 1)
    }

    func testCancelledUsesDueEvenWhenPcLevelIsZero() {
        let now = 1_000_000_000_000
        // máy tính gửi sẵn mức cảnh báo cho đơn hủy (2 = quá hạn, 1 = sắp hạn); thiếu mức (-1) thì dựa vào hạn xử lý
        let r = [row("c", "X", rd: "02/11/2026", due: now + 9_000_000_000, lv: 2), row("c", "Y", rd: "02/11/2026", due: now + 3_600_000, lv: -1)]
        let res = OrderStats.compute(rows: r, period: .day, now: now, calendar: cal)
        XCTAssertEqual(res.overdue, 1)
        XCTAssertEqual(res.soon, 1)
    }

    func testReceivedByReceiveDate() {
        var a = row("m", "A", rd: "01/11/2026", pd: false, rv: true); a.rcd = "05/11/2026 09:00"
        var b = row("a", "B", rd: "01/11/2026", pd: false, rv: true); b.rcd = "05/11/2026 17:00"
        var c = row("m", "C", rd: "01/11/2026", pd: false, rv: true); c.rcd = "06/11/2026"
        let rows = [a, b, c].map { r -> OrderRow in var x = r; x.rd = x.rcd; x.pd = false; return x }
        let res = OrderStats.compute(rows: rows, period: .day, now: 0, calendar: cal)
        XCTAssertEqual(res.buckets.count, 2)
        XCTAssertEqual(res.buckets.last?.total, 2)
    }

    func testEachOrderInExactlyOneGroup() {
        let rows = [row("m", "A", rd: "02/11/2026", pd: false, rv: true), row("m", "B", rd: "02/11/2026"), row("m", "C", rd: "02/11/2026", pd: false, rv: false)]
        let r = OrderStats.compute(rows: rows, period: .day, now: 0, calendar: cal)
        XCTAssertEqual(r.total, r.received + r.pending + r.other)
        XCTAssertEqual(r.other, 1)
        XCTAssertEqual(r.buckets[0].total, r.buckets[0].received + r.buckets[0].pending + r.buckets[0].other)
        XCTAssertEqual(r.buckets[0].main, 3)
    }

    func testReturnVsCancelSplit() {
        XCTAssertTrue(OrderStats.isReturn(row("m", "A", rd: "")))
        XCTAssertTrue(OrderStats.isReturn(row("a", "B", rd: "")), "đơn trong kho lưu trữ tính vào đơn hoàn")
        XCTAssertTrue(OrderStats.isCancel(row("c", "C", rd: "")))
        XCTAssertFalse(OrderStats.isReturn(row("c", "C", rd: "")))
        XCTAssertTrue(OrderStats.isReturn(row("m", "D", rd: "", ty: "Đơn hủy")), "đơn loại Đơn hủy ở bảng chính vẫn tính ở bảng chính như máy tính")
    }
}
