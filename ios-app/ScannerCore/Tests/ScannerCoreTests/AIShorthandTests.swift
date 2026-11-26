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
            ("chờ spx", "Đếm và liệt kê các kiện của SPX Express đang chờ (chưa về)."),
            ("bn jt", "Có bao nhiêu kiện của J&T Express đang chờ (chưa về)?"),
            ("trễ sp", "Liệt kê các kiện Shopee đang chờ bị báo đỏ (chờ quá 24 giờ kể từ lúc hàng về)."),
            ("cho mình xem đơn đỏ shopee nhé", "Liệt kê các kiện Shopee đang chờ bị báo đỏ (chờ quá 24 giờ kể từ lúc hàng về)."),
            ("Hôm nay có bao nhiêu đơn về?", "Có bao nhiêu kiện đã nhận về kho hôm nay?"),
            ("mất", "Đếm và liệt kê các kiện bị mất."),
            ("hỏng tuần", "Đếm và liệt kê các kiện bị hư hỏng tuần này."),
            ("kcn", "Đếm và liệt kê các kiện không cần nhận."),
            ("td", "Đếm và liệt kê các kiện đang theo dõi."),
            ("lt tháng", "Đếm và liệt kê các kiện trong Kho lưu trữ tháng này."),
            ("gtb tt", "Đếm và liệt kê các kiện TikTok giao không thành công."),
            ("mới hn", "Đếm và liệt kê các kiện mới nạp hôm nay."),
            ("mới", "Đếm và liệt kê các kiện mới nạp hôm nay."),
            ("trùng", "Có đơn nào bị trùng lặp không? Liệt kê nếu có."),
            ("nhật ký hq", "Có những hoạt động gì hôm qua (nhật ký)?"),
            ("tình hình thế nào", "Tổng quan hiện tại: bao nhiêu kiện đang chờ, đã nhận hôm nay, báo đỏ, báo vàng và tổng giá trị đang chờ."),
            ("top spx", "Sản phẩm nào hoàn nhiều nhất tuần này qua SPX Express?"),
            ("sản phẩm hoàn nhiều nhất tháng", "Sản phẩm nào hoàn nhiều nhất tháng này?"),
            ("bn về 7n tiền", "Có bao nhiêu kiện đã nhận về kho 7 ngày qua? Tổng giá trị bao nhiêu?"),
        ]
        for (input, expected) in cases { XCTAssertEqual(AIShorthand.expand(input), expected, input) }
    }

    func testFullOrUnknownQuestionsAreLeftAlone() {
        for q in ["Đơn nào hoàn vì lý do sai size?","xin chào","đỏ sp nhưng bỏ đơn đã lưu trữ","hãng spx",""] {
            XCTAssertNil(AIShorthand.expand(q), q)
        }
    }

    func testEveryQuickAskButtonExpands() {
        for q in AIShorthand.quickAsks { XCTAssertNotNil(AIShorthand.expand(q.short), q.short) }
    }
}
