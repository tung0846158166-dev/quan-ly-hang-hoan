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
        XCTAssertEqual(Set(names), ["overview", "count_orders", "aggregate", "list_orders", "find_order", "order_detail", "product_stats",
                                   "compare_periods", "order_groups", "return_watch", "scan_history", "system_status", "app_guide"])
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

    // MARK: công cụ chạy ngầm bổ sung

    func testCompareTodayVsYesterdayGivesCountsValueAndPercent() {
        let j = obj(tools().execute(name: "compare_periods", argsJSON: #"{"preset":"today_vs_yesterday","status":"Đã nhận"}"#).json)
        let a = j["periodA"] as? [String: Any]
        let b = j["periodB"] as? [String: Any]
        XCTAssertEqual(a?["count"] as? Int, 1)
        XCTAssertEqual(a?["value"] as? Int, 70_000)
        XCTAssertEqual(b?["count"] as? Int, 1)
        XCTAssertEqual(b?["value"] as? Int, 30_000)
        let v = (j["change"] as? [String: Any])?["value"] as? [String: Any]
        XCTAssertEqual(v?["diff"] as? Int, 40_000)
        XCTAssertEqual(v?["percent"] as? Double, 133.3)
    }

    func testCompareCustomRangesDefaultPreviousPeriodIsEqualLengthBefore() {
        let j = obj(tools().execute(name: "compare_periods", argsJSON: #"{"a_from":"2026-11-24","a_to":"2026-11-25","status":"Đã nhận"}"#).json)
        XCTAssertEqual((j["periodA"] as? [String: Any])?["count"] as? Int, 2)
        XCTAssertEqual((j["periodB"] as? [String: Any])?["from"] as? String, "2026-11-22")
        XCTAssertEqual((j["periodB"] as? [String: Any])?["to"] as? String, "2026-11-23")
        XCTAssertEqual((j["periodB"] as? [String: Any])?["count"] as? Int, 0)
        XCTAssertTrue(tools().execute(name: "compare_periods", argsJSON: "{}").json.contains("error"))
    }

    func testOrderGroupsListsMultiParcelOrders() {
        var a = row("A1", st: "Hàng đã trả", rv: true, pr: 10_000, rcd: "25/11/2026 09:00:00")
        var b = row("A2", st: "Chưa trả", rv: false, pr: 20_000)
        let c = row("B1", st: "Chưa trả", rv: false, pr: 5_000)
        a.o = "SAME"
        b.o = "SAME"
        let rows = [a, b, c]
        let t = AIOrderTools(rows: { rows }, now: { self.now }, calendar: cal)
        let r = t.execute(name: "order_groups", argsJSON: "{}")
        let g = (obj(r.json)["groups"] as? [[String: Any]])?.first
        XCTAssertEqual(g?["orderId"] as? String, "SAME")
        XCTAssertEqual(g?["parcels"] as? Int, 2)
        XCTAssertEqual(g?["received"] as? Int, 1)
        XCTAssertEqual(g?["waiting"] as? Int, 1)
        XCTAssertEqual(g?["value"] as? Int, 30_000)
        XCTAssertEqual(r.refs.count, 2)
        XCTAssertEqual(obj(r.json)["groupsTotal"] as? Int, 1)
    }

    func testReturnWatchCountsByStage() {
        var a = row("W1", st: "Chưa trả", rv: false, pr: 10_000)
        a.rw = ms(24, 8)
        a.rws = 2
        var b = row("W2", st: "Chưa trả", rv: false, pr: 20_000)
        b.rw = ms(25, 8)
        b.rws = 3
        var c = row("W3", st: "Chưa trả", rv: false, pr: 30_000)
        c.rw = ms(25, 9)
        c.rws = 3
        let outside = row("W4", st: "Chưa trả", rv: false, pr: 99_000)
        let rows = [a, b, c, outside]
        let t = AIOrderTools(rows: { rows }, now: { self.now }, calendar: cal)
        let j = obj(t.execute(name: "return_watch", argsJSON: "{}").json)
        XCTAssertEqual(j["total"] as? Int, 3)
        XCTAssertEqual(((j["byStage"] as? [String: Any])?["Đang về"] as? [String: Any])?["count"] as? Int, 1)
        XCTAssertEqual(((j["byStage"] as? [String: Any])?["Đã về shop"] as? [String: Any])?["count"] as? Int, 2)
        XCTAssertEqual((obj(t.execute(name: "return_watch", argsJSON: #"{"stage":"3"}"#).json)["orders"] as? [[String: Any]])?.count, 2)
    }

    func testScanHistoryAndSystemStatusUseProvidersAndReportWhenMissing() {
        let d = data
        let scans: [[String: Any]] = [
            ["time": ms(25, 13), "code": "VN001", "kind": "returnScan", "status": "success", "detail": "Nhận VN001, gọi 0987654321", "confirmed": true],
            ["time": ms(25, 12), "code": "VN999", "kind": "returnScan", "status": "pending", "detail": "Đang chờ", "confirmed": false],
        ]
        let t = AIOrderTools(rows: { d }, now: { self.now }, calendar: cal,
                             statusProvider: { ["pcConnection": "đang kết nối", "pendingScans": 1] }, scanHistory: { scans })
        let h = t.execute(name: "scan_history", argsJSON: #"{"limit":5}"#).json
        XCTAssertFalse(h.contains("0987654321"))
        XCTAssertTrue(h.contains("ẩn SĐT"))
        XCTAssertEqual(obj(h)["total"] as? Int, 2)
        XCTAssertEqual((obj(t.execute(name: "scan_history", argsJSON: #"{"text":"pending"}"#).json)["total"] as? Int), 1)
        let s = obj(t.execute(name: "system_status", argsJSON: "{}").json)
        XCTAssertEqual(s["pcConnection"] as? String, "đang kết nối")
        XCTAssertEqual(s["pendingScans"] as? Int, 1)
        XCTAssertEqual((s["orders"] as? [String: Any])?["active"] as? Int, 6)
        XCTAssertEqual((s["orders"] as? [String: Any])?["archived"] as? Int, 1)
        XCTAssertTrue(tools().execute(name: "scan_history", argsJSON: "{}").json.contains("error"))
        XCTAssertNotNil(obj(tools().execute(name: "system_status", argsJSON: "{}").json)["orders"])
    }

    func testAppGuideListsTopicsFindsByKeywordAndNeverInvents() {
        let t = tools()
        let idx = obj(t.execute(name: "app_guide", argsJSON: "{}").json)["topics"] as? [[String: Any]]
        XCTAssertGreaterThanOrEqual(idx?.count ?? 0, 7)
        XCTAssertTrue(t.execute(name: "app_guide", argsJSON: #"{"topic":"ảnh"}"#).json.contains("điện thoại"))
        XCTAssertTrue(t.execute(name: "app_guide", argsJSON: #"{"topic":"quet"}"#).json.contains("camera"))
        XCTAssertTrue(t.execute(name: "app_guide", argsJSON: #"{"topic":"zzzzqqqq"}"#).json.contains("error"))
    }

    func testExtraGroupByOptionsWork() {
        func first(_ g: String) -> String? {
            let j = obj(tools().execute(name: "aggregate", argsJSON: "{\"status\":\"Đang chờ\",\"group_by\":\"" + g + "\"}").json)
            return (j["groups"] as? [[String: Any]])?.first?["group"] as? String
        }
        XCTAssertEqual(first("waiting_age"), "12-24h")          // 4 nhóm cùng 1 đơn: hòa điểm thì xếp theo tên
        XCTAssertEqual(first("price_range"), "100k-300k")       // 100k và 250k cùng khoảng
        XCTAssertEqual(first("watching"), "không theo dõi")      // 3 đơn không theo dõi, 1 đang theo dõi
        let ages = (obj(tools().execute(name: "aggregate", argsJSON: #"{"status":"Đang chờ","group_by":"waiting_age"}"#).json)["groups"] as? [[String: Any]])?
            .compactMap { $0["group"] as? String } ?? []
        XCTAssertTrue(ages.contains("24-48h"))
        XCTAssertTrue(ages.contains("<12h"))
        XCTAssertNotNil(first("weekday"))
        XCTAssertNotNil(first("arrival_hour"))
    }
}
