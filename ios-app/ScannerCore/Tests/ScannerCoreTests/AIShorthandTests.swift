import XCTest
@testable import ScannerCore

/// Cùng bộ ca thử với v2/tests/QuanLyHangHoan.Tests/AiShorthandTests.cs.
final class AIShorthandTests: XCTestCase {
    func testShortQuestionsExpand() {
        let cases: [(String, String)] = [
            ("đỏ", "Liệt kê các kiện đang chờ bị báo đỏ (chờ quá 24 giờ kể từ lúc hàng về)."),
            ("do sp", "Liệt kê các kiện Shopee đang chờ bị báo đỏ (chờ quá 24 giờ kể từ lúc hàng về)."),
            ("Vàng TT", "Liệt kê các kiện TikTok đang chờ bị báo vàng (chờ quá 12 giờ kể từ lúc hàng về)."),
            ("quá hạn", "Liệt kê các kiện đang chờ bị báo đỏ (chờ quá 24 giờ kể từ lúc hàng về)."),
            ("về hn", "Đếm và liệt kê các kiện đã nhận về kho hôm nay."),
            ("bn về hn", "Có bao nhiêu kiện đã nhận về kho hôm nay?"),
            ("đơn về hôm qua", "Đếm và liệt kê các kiện đã nhận về kho hôm qua."),
            ("chờ sp", "Đếm và liệt kê các kiện Shopee đang chờ (chưa về)."),
            ("bn chờ tiền", "Có bao nhiêu kiện đang chờ (chưa về)? Tổng giá trị bao nhiêu?"),
            ("ds chờ lz", "Liệt kê các kiện Lazada đang chờ (chưa về)."),
            ("hủy sp", "Đếm và liệt kê các đơn hủy Shopee."),
            ("top tuần", "Sản phẩm nào hoàn nhiều nhất tuần này?"),
            ("top sp tháng", "Sản phẩm nào hoàn nhiều nhất tháng này trên Shopee?"),
            ("top", "Sản phẩm nào hoàn nhiều nhất tuần này?"),
            ("so tuần", "So sánh tuần này với tuần trước: số kiện hoàn và giá trị."),
            ("so tháng tt", "So sánh tháng này với tháng trước trên TikTok: số kiện hoàn và giá trị."),
            ("hãng tuần", "Đơn vị vận chuyển nào có nhiều kiện hoàn nhất tuần này?"),
            ("sàn", "Sàn nào có nhiều kiện hoàn nhất?"),
            ("tổng", "Tổng quan hiện tại: bao nhiêu kiện đang chờ, đã nhận hôm nay, báo đỏ, báo vàng và tổng giá trị đang chờ."),
            ("tq", "Tổng quan hiện tại: bao nhiêu kiện đang chờ, đã nhận hôm nay, báo đỏ, báo vàng và tổng giá trị đang chờ."),
            ("tìm 5678", "Tìm đơn có mã đơn hoặc mã vận đơn chứa 5678."),
            ("SPXVN06446806070B", "Tìm đơn có mã đơn hoặc mã vận đơn chứa SPXVN06446806070B."),
        ]
        for (input, expected) in cases { XCTAssertEqual(AIShorthand.expand(input), expected, input) }
    }

    func testFullOrUnknownQuestionsAreLeftAlone() {
        for q in ["Hôm nay có bao nhiêu đơn về?", "xin chào", "đỏ sp nhưng bỏ đơn đã lưu trữ", ""] {
            XCTAssertNil(AIShorthand.expand(q), q)
        }
    }

    func testGlossaryMentionsMainShortcuts() {
        for k in ["sp", "tt", "lz", "hn", "hq", "đỏ", "vàng", "bn", "ds", "top", "so", "tq"] {
            XCTAssertTrue(AIShorthand.glossary.contains(k), k)
        }
    }
}
