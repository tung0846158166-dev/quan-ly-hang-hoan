import XCTest
@testable import ScannerCore

final class ProductMetricsTests: XCTestCase {
    private var cal: Calendar = {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "UTC")!
        c.firstWeekday = 2
        return c
    }()

    private func fixedNow() -> Date {
        var comps = DateComponents()
        comps.year = 2026; comps.month = 11; comps.day = 24
        comps.hour = 12; comps.minute = 0; comps.second = 0
        return cal.date(from: comps)!
    }

    private func makeRow(
        c: String,
        p: String = "Shopee",
        n: String,
        co: String = "",
        sz: String = "",
        q: Int = 1,
        rd: String = "24/11/2026",
        rn: String = "",
        nt: String = ""
    ) -> OrderRow {
        var r = OrderRow(
            k: "m", c: c, o: "DHM-\(c)", p: p, n: n,
            co: co, sz: sz, q: q, rd: rd, due: 0, st: "Chưa trả"
        )
        r.rn = rn
        r.nt = nt
        return r
    }

    func testGroupsByNormalizedNameCaseWhitespaceAndTonesAndHandlesNonPositiveQty() throws {
        let rows = [
            makeRow(c: "DEMO-001", p: "Shopee", n: "Áo Thun Cotton", co: "Đen", sz: "L", q: 2),
            makeRow(c: "DEMO-002", p: "TikTok", n: "  áo   thun cotton  ", co: "Đen", sz: "L", q: 0), // qty <= 0 tính 1
            makeRow(c: "DEMO-003", p: "Shopee", n: "Áo Thun Cotton", co: "Trắng", sz: "M", q: -3),     // qty <= 0 tính 1
            makeRow(c: "DEMO-004", p: "Lazada", n: "ao thun cotton", co: "Trắng", sz: "M", q: 3)
        ]

        let result = ProductMetrics.compute(rows: rows)

        XCTAssertEqual(result.products.count, 1)
        XCTAssertEqual(result.totalUnits, 7) // 2 + 1 + 1 + 3
        XCTAssertEqual(result.ordersWithItems, 4)
        XCTAssertEqual(result.unknownUnits, 0)

        let item = try XCTUnwrap(result.products.first)
        XCTAssertEqual(item.name, "Áo Thun Cotton") // cách viết gặp nhiều nhất (2 lần)
        XCTAssertEqual(item.units, 7)
        XCTAssertEqual(item.orders, 4)
        XCTAssertEqual(item.share, 1.0, accuracy: 0.0001)
        XCTAssertEqual(item.shopeeUnits, 3)
        XCTAssertEqual(item.tiktokUnits, 1)
        XCTAssertEqual(item.lazadaUnits, 3)
        XCTAssertEqual(item.variants.count, 2)
        XCTAssertEqual(item.variants[0], ProductVariantStat(color: "Trắng", size: "M", units: 4))
        XCTAssertEqual(item.variants[1], ProductVariantStat(color: "Đen", size: "L", units: 3))
    }

    func testPartialReturnCountsOnlyReturnedProducts() {
        let rows = [
            makeRow(
                c: "DEMO-P1",
                p: "Shopee",
                n: "Áo sơ mi linen | Quần jean ống rộng | Mũ lưỡi trai",
                co: "Trắng | Xanh | Đen",
                sz: "M | L | Freesize",
                q: 3,
                rn: "Quần jean ống rộng"
            ),
            makeRow(
                c: "DEMO-P2",
                p: "TikTok",
                n: "Áo sơ mi linen | Quần jean ống rộng",
                co: "Be | Đen",
                sz: "S | M",
                q: 2,
                rn: "Áo sơ mi linen | Quần jean ống rộng"
            )
        ]

        let result = ProductMetrics.compute(rows: rows)

        XCTAssertEqual(result.totalUnits, 3) // P1 chỉ tính Quần jean (1) + P2 tính cả 2 (2)
        XCTAssertEqual(result.ordersWithItems, 2)
        XCTAssertEqual(result.products.count, 2)

        XCTAssertEqual(result.products[0].name, "Quần jean ống rộng")
        XCTAssertEqual(result.products[0].units, 2)
        XCTAssertEqual(result.products[0].orders, 2)

        XCTAssertEqual(result.products[1].name, "Áo sơ mi linen")
        XCTAssertEqual(result.products[1].units, 1)
        XCTAssertEqual(result.products[1].orders, 1)

        // Mũ lưỡi trai không hoàn nên không được xuất hiện
        XCTAssertFalse(result.products.contains { $0.name == "Mũ lưỡi trai" })
    }

    func testSkipsEmptyAndUnknownProductsAndCountsSeparately() {
        let rows = [
            makeRow(c: "DEMO-U1", n: "", q: 2),
            makeRow(c: "DEMO-U2", n: "   ", q: 0), // tính 1
            makeRow(c: "DEMO-U3", n: "Chưa rõ sản phẩm (quét trước)", q: 3),
            makeRow(c: "DEMO-U4", n: "chua ro san pham", q: 1),
            makeRow(c: "DEMO-OK", n: "Váy midi xếp ly", co: "Be", sz: "S", q: 2)
        ]

        let result = ProductMetrics.compute(rows: rows)

        XCTAssertEqual(result.products.count, 1)
        XCTAssertEqual(result.products.first?.name, "Váy midi xếp ly")
        XCTAssertEqual(result.totalUnits, 2)
        XCTAssertEqual(result.ordersWithItems, 1)
        XCTAssertEqual(result.unknownUnits, 7) // 2 + 1 + 3 + 1
    }

    func testEmptyInputDoesNotDivideByZero() {
        let result = ProductMetrics.compute(rows: [])
        XCTAssertTrue(result.products.isEmpty)
        XCTAssertEqual(result.totalUnits, 0)
        XCTAssertEqual(result.ordersWithItems, 0)
        XCTAssertEqual(result.unknownUnits, 0)
        XCTAssertFalse(ProductMetrics.hasAnyDate(rows: []))
    }

    func testMismatchedColorSizeCountIsIgnoredWithoutGuessing() {
        let rows = [
            // 2 sản phẩm nhưng chỉ có 1 màu và 1 size -> lệch số phần tử, bỏ qua màu/size của đơn này
            makeRow(
                c: "DEMO-M1",
                n: "Áo khoác gió | Quần short kaki",
                co: "Đen",
                sz: "L",
                q: 2
            ),
            // 2 sản phẩm và có đúng 2 màu, 2 size -> ghép song song đúng vị trí
            makeRow(
                c: "DEMO-M2",
                n: "Áo khoác gió | Quần short kaki",
                co: "Xám | Be",
                sz: "XL | M",
                q: 2
            )
        ]

        let result = ProductMetrics.compute(rows: rows)
        XCTAssertEqual(result.products.count, 2)

        let jacket = result.products.first { $0.name == "Áo khoác gió" }
        XCTAssertEqual(jacket?.units, 2)
        // Chỉ có biến thể từ DEMO-M2, không bị gán nhầm "Đen / L" từ DEMO-M1
        XCTAssertEqual(jacket?.variants, [ProductVariantStat(color: "Xám", size: "XL", units: 1)])

        let shorts = result.products.first { $0.name == "Quần short kaki" }
        XCTAssertEqual(shorts?.units, 2)
        XCTAssertEqual(shorts?.variants, [ProductVariantStat(color: "Be", size: "M", units: 1)])
    }

    func testPlatformAndDateFiltersAndReasonsAndOrderLookup() {
        let now = fixedNow()
        let rows = [
            makeRow(c: "DEMO-F1", p: "Shopee", n: "Áo polo dệt kim", co: "Đen", sz: "L", q: 3, rd: "23/11/2026", nt: "Khách đặt nhầm size"),
            makeRow(c: "DEMO-F2", p: "Shopee", n: "Áo polo dệt kim", co: "Trắng", sz: "M", q: 1, rd: "10/11/2026", nt: "Khách đặt nhầm size"),
            makeRow(c: "DEMO-F3", p: "TikTok", n: "Túi tote canvas", co: "Be", sz: "Freesize", q: 4, rd: "22/11/2026", nt: ""),
            makeRow(c: "DEMO-F4", p: "Lazada", n: "Áo polo dệt kim", co: "Be", sz: "S", q: 2, rd: "01/10/2026", nt: "Đổi ý")
        ]

        XCTAssertTrue(ProductMetrics.hasAnyDate(rows: rows, calendar: cal))

        let allRes = ProductMetrics.compute(rows: rows, platform: .all, dateRange: .all, now: now, calendar: cal)
        XCTAssertEqual(allRes.products.first?.name, "Áo polo dệt kim")
        XCTAssertEqual(allRes.products.first?.units, 6)
        XCTAssertEqual(allRes.products.first?.topReason, "Khách đặt nhầm size")
        XCTAssertEqual(allRes.products.last?.topReason, "") // không có lý do thì để rỗng, không bịa

        let last7 = ProductMetrics.compute(rows: rows, platform: .all, dateRange: .days7, now: now, calendar: cal)
        XCTAssertEqual(last7.totalUnits, 7) // F1 (3) + F3 (4)
        XCTAssertEqual(last7.products.first?.name, "Túi tote canvas")

        let shopeeOnly = ProductMetrics.compute(rows: rows, platform: .shopee, dateRange: .all, now: now, calendar: cal)
        XCTAssertEqual(shopeeOnly.products.count, 1)
        XCTAssertEqual(shopeeOnly.totalUnits, 4)

        let poloKey = ProductMetrics.groupKey("Áo polo dệt kim")
        let matchingOrders = ProductMetrics.orders(forProductKey: poloKey, in: rows)
        XCTAssertEqual(matchingOrders.map { $0.c }, ["DEMO-F1", "DEMO-F2", "DEMO-F4"])
    }

    func testPremiumAndStandardVersionsMergeIntoOneRowWithSplit() throws {
        let rows = [
            makeRow(c: "DEMO-PR1", p: "TikTok", n: "Áo Polo Len Lavi / STRIPES Nam Nữ Unisex - Premium Cao Cấp", co: "Nâu", sz: "L", q: 2),
            makeRow(c: "DEMO-PR2", p: "TikTok", n: "Áo Polo Len Lavi / STRIPES Nam Nữ Unisex", co: "Nâu", sz: "L", q: 3),
            makeRow(c: "DEMO-PR3", p: "TikTok", n: "Áo Polo Len Lavi / STRIPES Nam Nữ Unisex Premium Cao Cấp", co: "Xám", sz: "M", q: 1),
            makeRow(c: "DEMO-PR4", p: "TikTok", n: "Áo Sweater Ronan", co: "Nâu", sz: "L", q: 1)
        ]

        let result = ProductMetrics.compute(rows: rows)

        XCTAssertEqual(result.products.count, 2)
        let polo = try XCTUnwrap(result.products.first)
        XCTAssertEqual(polo.name, "Áo Polo Len Lavi / STRIPES Nam Nữ Unisex")
        XCTAssertEqual(polo.units, 6)
        XCTAssertEqual(polo.premiumUnits, 3)
        XCTAssertEqual(polo.standardUnits, 3)
        XCTAssertEqual(polo.variants.first, ProductVariantStat(color: "Nâu", size: "L", units: 5))

        let sweater = result.products[1]
        XCTAssertEqual(sweater.premiumUnits, 0)
        XCTAssertEqual(sweater.standardUnits, 1)

        // Tra danh sách đơn theo sản phẩm gộp phải trả đủ cả đơn Premium lẫn đơn thường
        let matching = ProductMetrics.orders(forProductKey: polo.key, in: rows)
        XCTAssertEqual(matching.map { $0.c }, ["DEMO-PR1", "DEMO-PR2", "DEMO-PR3"])
    }

    func testPremiumMarkedInSizeOrColorCountsAsPremiumWithoutChangingName() throws {
        let rows = [
            makeRow(c: "DEMO-SZ1", p: "TikTok", n: "Áo Len", co: "Nâu", sz: "L - Premium", q: 2),
            makeRow(c: "DEMO-SZ2", p: "TikTok", n: "Áo Len", co: "Nâu", sz: "L", q: 1),
            makeRow(c: "DEMO-CO1", p: "Shopee", n: "Áo Len", co: "Đen Premium", sz: "M", q: 1)
        ]

        let result = ProductMetrics.compute(rows: rows)
        let only = try XCTUnwrap(result.products.first)
        XCTAssertEqual(result.products.count, 1)
        XCTAssertEqual(only.name, "Áo Len")
        XCTAssertEqual(only.units, 4)
        XCTAssertEqual(only.premiumUnits, 3)
        XCTAssertEqual(only.standardUnits, 1)
    }

    func testNameThatIsOnlyWordPremiumIsKeptAndNeverBecomesEmpty() throws {
        let rows = [
            makeRow(c: "DEMO-ONLYP", p: "Shopee", n: "Premium", co: "", sz: "", q: 1)
        ]

        let result = ProductMetrics.compute(rows: rows)
        let only = try XCTUnwrap(result.products.first)
        XCTAssertEqual(result.products.count, 1)
        XCTAssertEqual(only.name, "Premium")
        XCTAssertEqual(only.units, 1)
        XCTAssertEqual(only.premiumUnits, 1)
        XCTAssertEqual(only.standardUnits, 0)
        XCTAssertEqual(result.unknownUnits, 0)
    }

    func testDemoDataProducesValidProductStats() {
        let now = fixedNow()
        let snap = DemoData.snapshot(now: now, calendar: cal)
        let summary = ProductMetrics.compute(rows: snap.rows)

        XCTAssertGreaterThanOrEqual(summary.products.count, 5)
        XCTAssertGreaterThan(summary.totalUnits, 0)
        XCTAssertEqual(summary.unknownUnits, 0)
        XCTAssertTrue(summary.products.contains { $0.premiumUnits > 0 && $0.standardUnits > 0 })
        XCTAssertTrue(summary.summaryLine.contains("sản phẩm •"))
        XCTAssertTrue(summary.summaryLine.contains("cái bị hoàn trong"))
        for p in summary.products {
            XCTAssertGreaterThan(p.units, 0)
            XCTAssertGreaterThan(p.orders, 0)
            XCTAssertEqual(p.premiumUnits + p.standardUnits, p.units)
            XCTAssertFalse(p.share.isNaN)
        }
    }

    func testSummaryLineMatchesPcFormat() {
        let empty = ProductMetrics.compute(rows: [])
        XCTAssertEqual(empty.summaryLine, "Chưa có sản phẩm nào trong bộ lọc hiện tại.")

        let rows = [
            makeRow(c: "DEMO-S1", p: "Shopee", n: "Áo sơ mi", q: 2),
            makeRow(c: "DEMO-S2", p: "TikTok", n: "Quần tây", q: 1)
        ]
        let normal = ProductMetrics.compute(rows: rows)
        XCTAssertEqual(normal.summaryLine, "2 sản phẩm • 3 cái bị hoàn trong 2 đơn")

        let withUnknown = ProductMetrics.compute(rows: rows + [makeRow(c: "DEMO-S3", n: "Chưa rõ sản phẩm", q: 4)])
        XCTAssertEqual(
            withUnknown.summaryLine,
            "2 sản phẩm • 3 cái bị hoàn trong 2 đơn • 4 cái chưa rõ sản phẩm (không tính)"
        )
    }

    func testSplitPipePreservesEmptyLeadingOrTrailingColorAndSize() {
        let rows = [
            makeRow(
                c: "DEMO-SP1",
                p: "Shopee",
                n: "Áo len | Quần bò",
                co: " | Đen",
                sz: "L | ",
                q: 2
            )
        ]
        let result = ProductMetrics.compute(rows: rows)
        XCTAssertEqual(result.products.count, 2)
        let shirt = result.products.first { $0.name == "Áo len" }
        XCTAssertEqual(shirt?.variants, [ProductVariantStat(color: "", size: "L", units: 1)])
        let jeans = result.products.first { $0.name == "Quần bò" }
        XCTAssertEqual(jeans?.variants, [ProductVariantStat(color: "Đen", size: "", units: 1)])
    }

    // MARK: - T037: lọc theo ngày, ranh giới múi giờ Việt Nam, gom theo ngày & căn cứ ngày

    func testDateRangesRespectVietnamMidnightBoundariesAndMissingDatesOnlyInAll() throws {
        let vnCal = ProductMetrics.vietnamCalendar
        // 00:30 ngày 24/11/2026 giờ Việt Nam (tức 17:30 ngày 23/11/2026 giờ UTC — kiểm chứng không lệch ngày theo UTC)
        var comps = DateComponents()
        comps.timeZone = vnCal.timeZone
        comps.year = 2026; comps.month = 11; comps.day = 24
        comps.hour = 0; comps.minute = 30; comps.second = 0
        let now = try XCTUnwrap(vnCal.date(from: comps))

        let rows = [
            makeRow(c: "VN-T1", p: "Shopee", n: "Áo Polo - Premium Cao Cấp", q: 2, rd: "24/11/2026 00:00:00"),
            makeRow(c: "VN-T2", p: "TikTok", n: "Áo Polo", q: 1, rd: "24/11/2026 23:59:59"),
            makeRow(c: "VN-D1", p: "Shopee", n: "Quần Jean", q: 3, rd: "23/11/2026 23:59:59"),
            makeRow(c: "VN-D6", p: "Lazada", n: "Váy Midi", q: 1, rd: "18/11/2026 00:00:00"),      // đúng biên 7 ngày
            makeRow(c: "VN-D7", p: "Shopee", n: "Váy Midi", q: 4, rd: "17/11/2026 23:59:59"),      // ngoài 7 ngày 1 giây
            makeRow(c: "VN-M1", p: "TikTok", n: "Áo Sơ Mi", q: 2, rd: "01/11/2026 00:00:00"),      // đúng đầu tháng 11
            makeRow(c: "VN-PM", p: "Shopee", n: "Áo Khoác", q: 5, rd: "31/10/2026 23:59:59"),      // tháng trước, vẫn trong 30 ngày (từ 26/10)
            makeRow(c: "VN-OLD", p: "Shopee", n: "Mũ Lưỡi Trai", q: 6, rd: "25/10/2026 23:59:59"), // ngoài 30 ngày 1 giây
            makeRow(c: "VN-ND", p: "Shopee", n: "Túi Tote", q: 7, rd: "")                          // không có ngày: chỉ tính ở Tất cả
        ]

        let today = ProductMetrics.compute(rows: rows, platform: .all, dateRange: .today, now: now, calendar: vnCal)
        XCTAssertEqual(today.totalUnits, 3)
        XCTAssertEqual(today.products.count, 1)
        let poloToday = try XCTUnwrap(today.products.first)
        XCTAssertEqual(poloToday.name, "Áo Polo")
        XCTAssertEqual(poloToday.premiumUnits, 2)
        XCTAssertEqual(poloToday.standardUnits, 1)
        XCTAssertEqual(poloToday.share, 1.0, accuracy: 0.0001)
        XCTAssertEqual(ProductMetrics.rangeSummaryText(for: today), "1 sản phẩm • 3 cái trong khoảng này")

        let days7 = ProductMetrics.compute(rows: rows, platform: .all, dateRange: .days7, now: now, calendar: vnCal)
        XCTAssertEqual(days7.totalUnits, 7) // 2 + 1 + 3 + 1

        let thisMonth = ProductMetrics.compute(rows: rows, platform: .all, dateRange: .thisMonth, now: now, calendar: vnCal)
        XCTAssertEqual(thisMonth.totalUnits, 13) // 2 + 1 + 3 + 1 + 4 + 2 (không gồm 31/10, 25/10 và không có ngày)

        let days30 = ProductMetrics.compute(rows: rows, platform: .all, dateRange: .days30, now: now, calendar: vnCal)
        XCTAssertEqual(days30.totalUnits, 18) // gồm thêm 31/10 (5), không gồm 25/10 và không có ngày

        let all = ProductMetrics.compute(rows: rows, platform: .all, dateRange: .all, now: now, calendar: vnCal)
        XCTAssertEqual(all.totalUnits, 31) // gồm cả 25/10 (6) và không có ngày (7)

        // Chọn ngày tùy chỉnh (kể cả khi truyền ngược from > to vẫn chuẩn hóa đúng)
        let dFrom = try XCTUnwrap(OrderStats.parseDate("17/11/2026", calendar: vnCal))
        let dTo = try XCTUnwrap(OrderStats.parseDate("23/11/2026", calendar: vnCal))
        let custom = ProductMetrics.compute(rows: rows, platform: .all, dateRange: .custom, customFrom: dFrom, customTo: dTo, now: now, calendar: vnCal)
        XCTAssertEqual(custom.totalUnits, 8) // VN-D1 (3) + VN-D6 (1) + VN-D7 (4)
        let customReversed = ProductMetrics.compute(rows: rows, platform: .all, dateRange: .custom, customFrom: dTo, customTo: dFrom, now: now, calendar: vnCal)
        XCTAssertEqual(customReversed.totalUnits, 8)
    }

    func testGroupByDayNewestFirstAndSingleDayFilter() throws {
        let vnCal = ProductMetrics.vietnamCalendar
        let rows = [
            makeRow(c: "DAY-1", p: "Shopee", n: "Áo Len - Premium Cao Cấp", q: 2, rd: "22/11/2026 09:00"),
            makeRow(c: "DAY-2", p: "TikTok", n: "Áo Len", q: 1, rd: "22/11/2026 18:30"),
            makeRow(c: "DAY-3", p: "Shopee", n: "Quần Kaki", q: 2, rd: "22/11/2026 20:00"),
            makeRow(c: "DAY-4", p: "TikTok", n: "Váy Midi | Mũ Vải", q: 2, rd: "24/11/2026 10:00", rn: "Váy Midi"),
            makeRow(c: "DAY-5", p: "Shopee", n: "Áo Sơ Mi", q: 4, rd: "23/11/2026"),
            makeRow(c: "DAY-NO", p: "Shopee", n: "Túi Xách", q: 9, rd: "")
        ]

        let buckets = ProductMetrics.groupByDay(rows: rows, dateBasis: .returnDate, calendar: vnCal)
        XCTAssertEqual(buckets.count, 3)

        // Mới nhất trước: 24/11 -> 23/11 -> 22/11
        XCTAssertEqual(OrderRow.formatDisplayDate(buckets[0].day, includeTime: false, calendar: vnCal), "24/11/2026")
        XCTAssertEqual(buckets[0].totalUnits, 1) // hoàn một phần chỉ tính Váy Midi
        XCTAssertEqual(buckets[0].topProductName, "Váy Midi")
        XCTAssertEqual(buckets[0].topProductUnits, 1)

        XCTAssertEqual(OrderRow.formatDisplayDate(buckets[1].day, includeTime: false, calendar: vnCal), "23/11/2026")
        XCTAssertEqual(buckets[1].totalUnits, 4)
        XCTAssertEqual(buckets[1].topProductName, "Áo Sơ Mi")

        XCTAssertEqual(OrderRow.formatDisplayDate(buckets[2].day, includeTime: false, calendar: vnCal), "22/11/2026")
        XCTAssertEqual(buckets[2].totalUnits, 5) // Áo Len (3) + Quần Kaki (2)
        XCTAssertEqual(buckets[2].productCount, 2)
        XCTAssertEqual(buckets[2].topProductName, "Áo Len")
        XCTAssertEqual(buckets[2].topProductUnits, 3)

        // Bấm vào ngày 22/11 lọc đúng các đơn ngày 22/11
        let day22Rows = ProductMetrics.filter(rows: rows, onDay: buckets[2].day, dateBasis: .returnDate, calendar: vnCal)
        XCTAssertEqual(day22Rows.map { $0.c }, ["DAY-1", "DAY-2", "DAY-3"])
        let day22Summary = ProductMetrics.compute(rows: day22Rows)
        XCTAssertEqual(day22Summary.products.first?.premiumUnits, 2)
        XCTAssertEqual(day22Summary.products.first?.standardUnits, 1)
    }

    func testDateBasisSwitchesBetweenReturnDateAndReceivedDate() throws {
        let vnCal = ProductMetrics.vietnamCalendar
        var comps = DateComponents()
        comps.timeZone = vnCal.timeZone
        comps.year = 2026; comps.month = 11; comps.day = 24; comps.hour = 15
        let now = try XCTUnwrap(vnCal.date(from: comps))

        var recvToday = makeRow(c: "B-1", p: "Shopee", n: "Áo Khoác", q: 2, rd: "10/11/2026")
        recvToday.rv = true
        recvToday.pd = false
        recvToday.rcd = "24/11/2026 09:15:00"

        let retToday = makeRow(c: "B-2", p: "TikTok", n: "Quần Short", q: 3, rd: "24/11/2026")

        let rows = [recvToday, retToday]

        let byReturnToday = ProductMetrics.compute(rows: rows, platform: .all, dateRange: .today, dateBasis: .returnDate, now: now, calendar: vnCal)
        XCTAssertEqual(byReturnToday.totalUnits, 3)
        XCTAssertEqual(byReturnToday.products.first?.name, "Quần Short")

        let byRecvToday = ProductMetrics.compute(rows: rows, platform: .all, dateRange: .today, dateBasis: .receivedDate, now: now, calendar: vnCal)
        XCTAssertEqual(byRecvToday.totalUnits, 2)
        XCTAssertEqual(byRecvToday.products.first?.name, "Áo Khoác")

        let byRecvAll = ProductMetrics.compute(rows: rows, platform: .all, dateRange: .all, dateBasis: .receivedDate, now: now, calendar: vnCal)
        XCTAssertEqual(byRecvAll.totalUnits, 5) // Tất cả gồm cả đơn chưa có ngày nhận
    }
}
