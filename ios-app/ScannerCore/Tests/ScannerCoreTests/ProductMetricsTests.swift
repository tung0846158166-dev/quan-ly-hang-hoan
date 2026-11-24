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
}
