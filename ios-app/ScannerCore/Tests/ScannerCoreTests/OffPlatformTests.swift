import XCTest
@testable import ScannerCore

/// Chủ dự án 27/11/2026: đơn ngoài sàn (lệnh tạo, danh mục sản phẩm gợi ý) + tra nhanh sản phẩm theo ngày/tuần/tháng. Dữ liệu GIẢ.
final class OffPlatformTests: XCTestCase {
    private var cal: Calendar { ProductMetrics.vietnamCalendar }
    /// Thứ Sáu 27/11/2026 15:00 (tuần bắt đầu thứ Hai 23/11).
    private var now: Date { cal.date(from: DateComponents(year: 2026, month: 11, day: 27, hour: 15))! }

    private func row(_ id: String, _ product: String, qty: Int = 1, color: String = "", size: String = "",
                     status: String = "Chưa trả", recv: String = "", ret: String = "", platform: String = "Shopee") -> OrderRow {
        let json = #"{"id":"\#(id)","track":"T\#(id)","platform":"\#(platform)","status":"\#(status)","recvDate":"\#(recv)","returnDate":"\#(ret)","items":[{"product":"\#(product)","color":"\#(color)","size":"\#(size)","qty":\#(qty)}]}"#
        let p = try! JSONDecoder().decode(CloudParcel.self, from: Data(json.utf8))
        return CloudRowMapper.row(p, now: now)!
    }

    func testOffPlatformOpCarriesItemsAndSkipsBlankLines() {
        let op = CloudOps.offPlatformOrder(code: " ", orderId: " SW-1 ", lines: [NewOrderLine(product: " Áo thun ", color: "Đen", size: "L", qty: 2), NewOrderLine()],
                                           price: 350000, orderType: "Đổi hàng", channel: "Zalo", note: " khách đổi ")
        XCTAssertEqual(op["type"] as? String, "newOrder")
        XCTAssertEqual(op["platform"] as? String, "Ngoài sàn")
        XCTAssertEqual(op["code"] as? String, "")
        XCTAssertEqual(op["orderId"] as? String, "SW-1")
        XCTAssertEqual(OffPlatform.channels.first, "Salework")
        XCTAssertEqual(op["price"] as? Int, 350000)
        XCTAssertEqual(op["channel"] as? String, "Zalo")
        XCTAssertEqual(op["orderType"] as? String, "Đổi hàng")
        XCTAssertEqual(op["note"] as? String, "khách đổi")
        let items = op["items"] as! [[String: Any]]
        XCTAssertEqual(items.count, 1)
        XCTAssertEqual(items[0]["product"] as? String, "Áo thun")
        XCTAssertEqual(items[0]["qty"] as? Int, 2)
        XCTAssertNil(CloudOps.offPlatformOrder(code: "", lines: [NewOrderLine(product: "A")], price: 0, orderType: "", channel: "", note: "")["price"])
    }

    func testFormValidationAndShopStylePrices() {
        XCTAssertEqual(OffPlatform.parsePrice("350000"), 350000)
        XCTAssertEqual(OffPlatform.parsePrice("350.000"), 350000)
        XCTAssertEqual(OffPlatform.parsePrice("350k"), 350000)
        XCTAssertEqual(OffPlatform.parsePrice("1,5tr"), 1_500_000)
        XCTAssertEqual(OffPlatform.parsePrice(""), 0)
        XCTAssertNil(OffPlatform.parsePrice("abc"))
        XCTAssertNotNil(CloudOps.offPlatformError(lines: [NewOrderLine()], priceText: ""))
        XCTAssertNotNil(CloudOps.offPlatformError(lines: [NewOrderLine(product: "", color: "Đỏ")], priceText: ""))
        XCTAssertNotNil(CloudOps.offPlatformError(lines: [NewOrderLine(product: "Áo", qty: 0)], priceText: ""))
        XCTAssertNotNil(CloudOps.offPlatformError(lines: [NewOrderLine(product: "Áo")], priceText: "xyz"))
        XCTAssertNil(CloudOps.offPlatformError(lines: [NewOrderLine(product: "Áo"), NewOrderLine()], priceText: "200k"))
    }

    func testCatalogMergesSpellingsAndSearchesWithoutTones() {
        let rows = [row("1", "Áo thun basic", qty: 2, color: "Đen", size: "L"), row("2", "ao thun  basic", color: "Trắng", size: "M"),
                    row("3", "Áo thun basic", qty: 3, color: "Đen", size: "M"), row("4", "Quần jean ống rộng", color: "Xanh", size: "30"),
                    row("5", "Sản phẩm mới (nhập tay)")]
        let cat = ProductCatalog.build(rows: rows)
        XCTAssertEqual(cat.count, 2)
        XCTAssertEqual(cat[0].name, "Áo thun basic")
        XCTAssertEqual(cat[0].units, 6)
        XCTAssertEqual(cat[0].colors, ["Đen", "Trắng"])
        XCTAssertEqual(cat[0].sizes, ["M", "L"])
        XCTAssertEqual(ProductCatalog.search(cat, query: "quan ong").map { $0.name }, ["Quần jean ống rộng"])
        XCTAssertTrue(ProductCatalog.search(cat, query: "vay").isEmpty)
        XCTAssertNotNil(ProductCatalog.find(cat, name: "ÁO THUN basic"))
        XCTAssertTrue(OffPlatform.isOffPlatform(row("x", "A", platform: "Ngoài sàn")))
    }

    func testLookupCountsReceivedAndReturnedTodayWeekMonth() {
        let rows = [
            row("a", "Áo thun đen", qty: 2, status: "Hàng đã trả", recv: "27/11/2026 09:00:00", ret: "25/11/2026 10:00:00"),
            row("b", "Áo thun đen", status: "Hàng đã trả", recv: "23/11/2026 08:00:00", ret: "20/11/2026 10:00:00"),
            row("c", "ao thun den", qty: 4, status: "Hàng đã trả", recv: "22/11/2026 08:00:00", ret: "22/11/2026 08:00:00"),
            row("d", "Áo thun đen", qty: 5, status: "Hàng đã trả", recv: "30/10/2026 08:00:00", ret: "28/10/2026 08:00:00"),
            row("e", "Áo thun đen", qty: 3, ret: "27/11/2026 07:00:00"),
            row("f", "Áo thun đen", qty: 7, recv: "27/11/2026 07:00:00"),
            row("g", "Quần short", qty: 9, status: "Hàng đã trả", recv: "27/11/2026 07:00:00"),
        ]
        let found = ProductMetrics.lookup(rows: rows, query: "ao den", now: now, calendar: cal)
        XCTAssertEqual(found.count, 1)
        XCTAssertEqual(found[0].name, "Áo thun đen")
        XCTAssertEqual(found[0].received, PeriodUnits(today: 2, week: 3, month: 7, total: 12))
        XCTAssertEqual(found[0].returned, PeriodUnits(today: 3, week: 5, month: 10, total: 22))
        XCTAssertEqual(found[0].orders, 6)
        XCTAssertTrue(ProductMetrics.lookup(rows: rows, query: "  ", now: now, calendar: cal).isEmpty)
        XCTAssertEqual(ProductMetrics.weekStart(now, calendar: cal), cal.date(from: DateComponents(year: 2026, month: 11, day: 23))!)
    }
}

extension OffPlatformTests {
    // ảnh 31 (28/11/2026): kiểm kênh Salework bằng mã đơn trong file
    func testSaleworkFileChannelIsCheckedFromOrderIds() {
        XCTAssertEqual(SaleworkImport.looksLikeTikTok(orderIds: ["579123456789012345", "579123456789012346"]), true)
        XCTAssertEqual(SaleworkImport.looksLikeTikTok(orderIds: ["261120ABCDEF12", "2611110XFUC9UB", "579123456789012345"]), false)
        XCTAssertNil(SaleworkImport.looksLikeTikTok(orderIds: []))
        XCTAssertNil(SaleworkImport.looksLikeTikTok(orderIds: ["ABC"]))
        XCTAssertEqual(SaleworkImport.channelCounts(orderIds: ["2611110XFUC9UB"]).shopee, 1)
    }

    func testReceivedStockGroupsRecentReceivedItemsNewestFirst() {
        let rows = [
            row("a", "Áo thun", qty: 2, color: "Đen", size: "L", status: "Hàng đã trả", recv: "27/11/2026 09:00:00"),
            row("b", "ao thun", color: "đen", size: "L", status: "Hàng đã trả", recv: "25/11/2026 09:00:00"),
            row("c", "Áo thun", color: "Đen", size: "M", status: "Hàng đã trả", recv: "26/11/2026 09:00:00"),
            row("d", "Áo thun", qty: 4, color: "Đen", size: "L", status: "Hàng đã trả", recv: "20/11/2026 09:00:00"),
            row("e", "Áo thun", qty: 5, color: "Đen", size: "L", recv: "27/11/2026 09:00:00"),
        ]
        let since = cal.date(from: DateComponents(year: 2026, month: 11, day: 25))!
        let items = ReceivedStock.build(rows: rows, since: since, calendar: cal)
        XCTAssertEqual(items.count, 2)
        let l = items.first { $0.size == "L" }!
        XCTAssertEqual(l.units, 3)
        XCTAssertEqual(l.parcels.map { $0.row.c }, ["Ta", "Tb"])
        XCTAssertEqual(ReceivedStock.build(rows: rows, since: since, query: "ao den l", calendar: cal).map { $0.size }, ["L"])
        XCTAssertTrue(ReceivedStock.build(rows: rows, since: since, query: "quan", calendar: cal).isEmpty)
    }
}

extension OffPlatformTests {
    // Chủ dự án 28/11/2026: các kiểu tên của cùng một mẫu gộp một dòng (giống máy tính)
    func testFamilyKeysMergeShortLongPrefixedAndLowercaseNames() {
        XCTAssertEqual(ProductNames.familyKey("Áo Polo LEN THỎ Local Brand Lavi Studio/ ALPHA Nam Nữ Unisex"), "m:alpha:aopolo")
        XCTAssertEqual(ProductNames.familyKey("ALPHA"), "m:alpha")
        XCTAssertEqual(ProductNames.familyKey("[ DEAL MỞ BÁN ] Áo Polo Len Lavi Studio / ALPHA Unisex"), "m:alpha:aopolo")
        XCTAssertEqual(ProductNames.familyKey("Áo Polo Len Khóa Zip 𝐋𝐚𝐯𝐢 𝐒𝐭𝐮𝐝𝐢𝐨/ BETA Unisex"), "m:beta:aopolo")
        XCTAssertEqual(ProductNames.familyKey("Quần Kaki Ống Suông Lavi Studio Trẻ Trung/ DELTA"), "m:delta:quankaki")
        XCTAssertEqual(ProductNames.familyKey("Áo Cardigan Lavi Studio / Logo Thêu Unisex"), "aocardiganlogotheu")
        XCTAssertEqual(ProductNames.familyKey("Áo Cardigan Len Dệt Kim Hàn Quốc Local Brand Lavi Studio / Logo Thêu Unisex Nam Nữ"), "aocardiganlendetkimhanquoclogotheu")
        XCTAssertEqual(ProductNames.familyKey("Áo Cardigan Len Dệt Kim Hàn Quốc Lavi Studio / Logo Thêu Unisex Nam Nữ - Nhận may theo số đo/ thêu tên"), "aocardiganlendetkimhanquoclogotheu")
        let rows = [
            row("1", "Áo Polo Len Lavi Studio/ OMEGA Unisex", color: "Đen", size: "L", status: "Hàng đã trả", recv: "27/11/2026 09:00:00"),
            row("2", "OMEGA", qty: 2, color: "Đen", size: "L", status: "Hàng đã trả", recv: "27/11/2026 10:00:00"),
            row("3", "Áo Polo Len Lavi Studio/ Omega Unisex", color: "Đen", size: "L", status: "Hàng đã trả", recv: "27/11/2026 11:00:00"),
            row("4", "Áo Polo Len Lavi Studio/ OMEGA Unisex - Premium Cao Cấp", color: "Đen", size: "L", status: "Hàng đã trả", recv: "27/11/2026 11:00:00"),
            row("5", "Áo Sweater Nỉ Lavi Studio / ZETA", status: "Hàng đã trả", recv: "27/11/2026 11:00:00"),
            row("6", "Áo Hoodie Lavi Studio/ ZETA", status: "Hàng đã trả", recv: "27/11/2026 11:00:00"),
            row("7", "ZETA", status: "Hàng đã trả", recv: "27/11/2026 11:00:00"),
        ]
        let since = cal.date(from: DateComponents(year: 2026, month: 11, day: 27))!
        let items = ReceivedStock.build(rows: rows, since: since, calendar: cal)
        let omega = items.filter { $0.key.hasPrefix("m:omega") }
        XCTAssertEqual(omega.count, 2)                                         // thường + Premium
        XCTAssertEqual(omega.first { !$0.premium }?.units, 4)
        XCTAssertEqual(omega.first { !$0.premium }?.product, "Áo Polo Len Lavi Studio/ OMEGA Unisex")
        XCTAssertEqual(omega.first { $0.premium }?.units, 1)
        XCTAssertEqual(items.filter { $0.key.hasPrefix("m:zeta") }.count, 3)   // cùng mã khác loại: không gộp, tên ngắn để riêng
        XCTAssertEqual(ReceivedStock.build(rows: rows, since: since, query: "ao polo len", calendar: cal).reduce(0) { $0 + $1.units }, 5)
        XCTAssertEqual(ProductMetrics.compute(rows: rows).products.filter { $0.key.hasPrefix("m:omega") }.count, 1)
    }
}

extension OffPlatformTests {
    // 28/11/2026: mọi đơn có giờ hàng về shop (lọc "đã về / chưa về shop" trên điện thoại)
    func testArrivalTimeIsSetForEveryRowNotOnlyReturnWatch() {
        let json = #"{"id":"v1","track":"TV1","platform":"Shopee","status":"Chưa trả","returnedAt":"27/11/2026 09:30:00","items":[{"product":"Áo","qty":1}]}"#
        let p = try! JSONDecoder().decode(CloudParcel.self, from: Data(json.utf8))
        let r = CloudRowMapper.row(p, now: now)!
        XCTAssertGreaterThan(r.ar, 0)
        XCTAssertEqual(r.rw, 0)                      // không nằm trong bảng Hàng hoàn về
        let none = CloudRowMapper.row(try! JSONDecoder().decode(CloudParcel.self, from: Data(#"{"id":"v2","track":"TV2"}"#.utf8)), now: now)!
        XCTAssertEqual(none.ar, 0)
    }
}
