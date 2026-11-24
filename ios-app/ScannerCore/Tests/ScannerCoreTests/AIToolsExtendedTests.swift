import XCTest
@testable import ScannerCore

/// Công cụ AI mở rộng trên iPhone: tổng tiền, gom nhóm, cảnh báo, chi tiết đơn. Dữ liệu GIẢ.
final class AIToolsExtendedTests: XCTestCase {
    private var cal: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "Asia/Ho_Chi_Minh")!
        return c
    }
    private var now: Date { cal.date(from: DateComponents(year: 2026, month: 11, day: 25, hour: 14))! }

    private func ms(_ day: Int, _ hour: Int) -> Int {
        Int(cal.date(from: DateComponents(year: 2026, month: 11, day: day, hour: hour))!.timeIntervalSince1970 * 1000)
    }

    private func row(_ c: String, st: String, rv: Bool, pr: Int, p: String = "Shopee", cr: String = "SPX Express", n: String = "Áo thun",
                     q: Int = 1, ar: Int = 0, lv: Int = -1, rcd: String = "", wl: Bool = false, k: String = "m") -> OrderRow {
        var r = OrderRow(k: k, c: c, o: "O" + c, p: p, n: n, co: "", sz: "", q: q, rd: "20/11/2026", due: 0, st: st)
        r.rv = rv
        r.pd = !rv
        r.pr = pr
        r.cr = cr
        r.ar = ar
        r.lv = lv
        r.rcd = rcd
        r.wl = wl
        return r
    }

    private var data: [OrderRow] {
        [
            row("VN001", st: "Chưa trả", rv: false, pr: 100_000, n: "Áo thun", q: 2, ar: ms(24, 10)),                       // 28 giờ: đỏ
            row("VN002", st: "Chưa trả", rv: false, pr: 250_000, n: "Quần jean", ar: ms(25, 0), wl: true),                  // 14 giờ: vàng
            row("VN003", st: "Chưa trả", rv: false, pr: 40_000, cr: "GHN", n: "Áo thun", ar: ms(25, 10)),                    // 4 giờ: an toàn
            row("VN004", st: "Chưa về", rv: false, pr: 400_000, p: "TikTok Shop", cr: "J&T", n: "Váy"),
            row("VN005", st: "Hàng đã trả", rv: true, pr: 70_000, rcd: "25/11/2026 09:00:00"),
            row("VN006", st: "Hàng đã trả", rv: true, pr: 30_000, cr: "GHN", n: "Quần jean", rcd: "24/11/2026 15:00:00"),
            row("VN008", st: "Chưa trả", rv: false, pr: 999_000, k: "a"),                                                    // lưu trữ
        ]
    }

    private func tools() -> AIOrderTools {
        let d = data
        return AIOrderTools(rows: { d }, now: { self.now }, calendar: cal)
    }

    private func obj(_ s: String) -> [String: Any] {
        (try? JSONSerialization.jsonObject(with: Data(s.utf8)) as? [String: Any]) ?? [:]
    }

    func testWaitingMeansNotReturnedAndNotYetArrivedAndTotalValueIsSummed() {
        let j = obj(tools().execute(name: "count_orders", argsJSON: #"{"status":"chưa về"}"#).json)
        XCTAssertEqual(j["total"] as? Int, 4)
        XCTAssertEqual(j["totalValue"] as? Int, 790_000)
        XCTAssertEqual(j["totalQuantity"] as? Int, 5)
        XCTAssertEqual(obj(tools().execute(name: "count_orders", argsJSON: #"{"status":"Đang chờ"}"#).json)["total"] as? Int, 4)
        XCTAssertEqual(obj(tools().execute(name: "count_orders", argsJSON: #"{"status":"Đã nhận"}"#).json)["total"] as? Int, 2)
    }

    func testAggregateByPlatformSortedByValueAndByCarrier() {
        let j = obj(tools().execute(name: "aggregate", argsJSON: #"{"status":"Đang chờ","group_by":"platform","metric":"value"}"#).json)
        let g = j["groups"] as? [[String: Any]]
        XCTAssertEqual(g?.first?["group"] as? String, "TikTok Shop")      // 400k > 390k
        XCTAssertEqual(j["totalValue"] as? Int, 790_000)
        let c = obj(tools().execute(name: "aggregate", argsJSON: #"{"status":"Đang chờ","group_by":"carrier"}"#).json)
        let first = (c["groups"] as? [[String: Any]])?.first
        XCTAssertEqual(first?["group"] as? String, "SPX Express")
        XCTAssertEqual(first?["count"] as? Int, 2)
    }

    func testAggregateByDayIsChronologicalAndByProductCountsQuantity() {
        let d = obj(tools().execute(name: "aggregate", argsJSON: #"{"status":"Đã nhận","group_by":"day","date_field":"recv"}"#).json)
        let days = (d["groups"] as? [[String: Any]])?.compactMap { $0["group"] as? String }
        XCTAssertEqual(days, ["2026-11-24", "2026-11-25"])
        let p = obj(tools().execute(name: "aggregate", argsJSON: #"{"status":"Đang chờ","group_by":"product","metric":"quantity"}"#).json)
        let first = (p["groups"] as? [[String: Any]])?.first
        XCTAssertEqual(first?["group"] as? String, "Áo thun")
        XCTAssertEqual(first?["quantity"] as? Int, 3)
        XCTAssertEqual(first?["count"] as? Int, 2)
    }

    func testAlertFiltersUseTwelveAndTwentyFourHours() {
        let t = tools()
        let red = t.execute(name: "list_orders", argsJSON: #"{"alert":"red"}"#)
        XCTAssertEqual(red.refs.map { $0.track }, ["VN001"])
        let first = (obj(red.json)["orders"] as? [[String: Any]])?.first
        XCTAssertEqual(first?["hoursWaiting"] as? Double, 28.0)
        XCTAssertEqual(t.execute(name: "list_orders", argsJSON: #"{"alert":"yellow"}"#).refs.map { $0.track }, ["VN002"])
        XCTAssertEqual(t.execute(name: "list_orders", argsJSON: #"{"alert":"warning"}"#).refs.count, 2)
        XCTAssertEqual(t.execute(name: "list_orders", argsJSON: #"{"alert":"safe"}"#).refs.map { $0.track }, ["VN003"])
    }

    func testListSortsByValueAndWaitingDefaultsToOldestFirst() {
        let t = tools()
        let byValue = t.execute(name: "list_orders", argsJSON: #"{"status":"Đang chờ","sort_by":"value","descending":true,"limit":"2"}"#)
        XCTAssertEqual(byValue.refs.map { $0.track }, ["VN004", "VN002"])
        XCTAssertEqual((obj(byValue.json)["orders"] as? [[String: Any]])?.first?["value"] as? Int, 400_000)
        XCTAssertEqual(obj(byValue.json)["totalValue"] as? Int, 790_000)
        XCTAssertEqual(t.execute(name: "list_orders", argsJSON: #"{"status":"Đang chờ"}"#).refs.first?.track, "VN001")
    }

    func testFiltersWatchingAndValueRange() {
        let t = tools()
        XCTAssertEqual(t.execute(name: "list_orders", argsJSON: #"{"watching":true}"#).refs.map { $0.track }, ["VN002"])
        XCTAssertEqual(t.execute(name: "list_orders", argsJSON: #"{"status":"Đang chờ","min_value":100000,"max_value":300000}"#).refs.count, 2)
    }

    func testOverviewSummarisesWaitingAlertsAndToday() {
        let j = obj(tools().execute(name: "overview", argsJSON: "{}").json)
        XCTAssertEqual((j["waiting"] as? [String: Any])?["count"] as? Int, 4)
        XCTAssertEqual((j["waiting"] as? [String: Any])?["value"] as? Int, 790_000)
        let alerts = j["alerts"] as? [String: Any]
        XCTAssertEqual((alerts?["redOver24h"] as? [String: Any])?["count"] as? Int, 1)
        XCTAssertEqual((alerts?["yellowOver12h"] as? [String: Any])?["count"] as? Int, 1)
        XCTAssertEqual((j["receivedToday"] as? [String: Any])?["count"] as? Int, 1)
        XCTAssertEqual((j["receivedToday"] as? [String: Any])?["value"] as? Int, 70_000)
        XCTAssertEqual((j["receivedYesterday"] as? [String: Any])?["count"] as? Int, 1)
        XCTAssertEqual(j["watching"] as? Int, 1)
        XCTAssertEqual(j["archivedOrders"] as? Int, 1)
    }

    func testOrderDetailByCodeAndErrors() {
        let t = tools()
        let r = t.execute(name: "order_detail", argsJSON: #"{"code":"vn001"}"#)
        XCTAssertEqual(r.refs.count, 1)
        let d = (obj(r.json)["orders"] as? [[String: Any]])?.first
        XCTAssertEqual(d?["track"] as? String, "VN001")
        XCTAssertTrue((d?["alert"] as? String ?? "").contains("đỏ"))
        XCTAssertTrue(t.execute(name: "order_detail", argsJSON: "{}").json.contains("error"))
        XCTAssertTrue(t.execute(name: "order_detail", argsJSON: #"{"code":"ZZZZZZ"}"#).json.contains("error"))
    }

    func testDefinitionsAreValidJSONWithAllTools() {
        guard let arr = (try? JSONSerialization.jsonObject(with: Data(AIOrderTools.definitionsJSON.utf8))) as? [[String: Any]] else {
            return XCTFail("định nghĩa phải là mảng JSON")
        }
        let names = arr.compactMap { ($0["function"] as? [String: Any])?["name"] as? String }
        XCTAssertEqual(Set(names), ["overview", "count_orders", "aggregate", "list_orders", "find_order", "order_detail", "product_stats"])
        XCTAssertTrue(AIOrderTools.definitionsJSON.contains("Đang chờ"))
    }

    func testSnapshotRowDecodesPriceAndCarrierAndOldPCStillWorks() throws {
        let withNew = #"{"k":"m","c":"VN1","o":"O1","p":"Shopee","n":"Áo","co":"","sz":"","q":1,"rd":"","due":0,"st":"Chưa trả","pr":125000,"cr":"SPX Express"}"#
        let r1 = try JSONDecoder().decode(OrderRow.self, from: Data(withNew.utf8))
        XCTAssertEqual(r1.pr, 125_000)
        XCTAssertEqual(r1.cr, "SPX Express")
        let old = #"{"k":"m","c":"VN1","o":"O1","p":"Shopee","n":"Áo","co":"","sz":"","q":1,"rd":"","due":0,"st":"Chưa trả"}"#
        let r2 = try JSONDecoder().decode(OrderRow.self, from: Data(old.utf8))
        XCTAssertEqual(r2.pr, 0)
        XCTAssertEqual(r2.cr, "")
    }
}
