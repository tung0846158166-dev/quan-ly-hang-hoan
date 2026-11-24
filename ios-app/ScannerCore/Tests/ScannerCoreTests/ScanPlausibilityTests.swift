import XCTest
@testable import ScannerCore

final class ScanPlausibilityTests: XCTestCase {
    func testAcceptsRealCodes() {
        for s in ["SPXVN067887279218", "*spxvn067887279218*", "https://spx.vn/track?tracking_no=SPXVN062176759678",
                  "587152900046530012988", "862123456789012", "0714082259941", "2611137P8BRQ50", "TTVN1129574777", "GHN123456789"] {
            XCTAssertTrue(TrackingCode.isPlausibleScan(s), s)
        }
    }

    func testPickBestPrefersKnownThenCarrierThenFirst() {
        let order = "2611137P8BRQ50"          // mã đơn hàng (không phải mã vận đơn)
        let spx = "SPXVN067887279218"
        // 1) Mã có trong dữ liệu trên máy thắng, dù đứng sau.
        XCTAssertEqual(TrackingCode.pickBest([order, spx], isKnown: { $0 == spx }), spx)
        XCTAssertEqual(TrackingCode.pickBest([spx, order], isKnown: { $0 == order }), order)
        // 2) Không mã nào có sẵn: mã đúng dạng hãng vận chuyển thắng mã lạ đứng trước.
        XCTAssertEqual(TrackingCode.pickBest([order, spx], isKnown: { _ in false }), spx)
        // 3) Không có gì đặc biệt: lấy mã hợp lệ đầu tiên; bỏ qua rác.
        XCTAssertEqual(TrackingCode.pickBest(["ABCDEFGH", order], isKnown: { _ in false }), order)
        XCTAssertNil(TrackingCode.pickBest(["ABCDEFGH", "12 34", "https://shopee.vn/"], isKnown: { _ in false }))
        XCTAssertNil(TrackingCode.pickBest([], isKnown: { _ in true }))
        // QR dạng đường dẫn: lấy mã vận đơn trong đó và so khớp với dữ liệu.
        let qr = "https://spx.vn/track?tracking_no=SPXVN062176759678"
        XCTAssertEqual(TrackingCode.pickBest([order, qr], isKnown: { $0 == "SPXVN062176759678" }), qr)
    }

    func testRejectsJunk() {
        for s in ["", "  ", "123", "ABCDEFGH", "12 34 56 78 90", "Xin chào các bạn 123456", "https://shopee.vn/", "ĐƠN 2611111UTM1CWP",
                  String(repeating: "1", count: 41), "ab#$%^&*cd1234"] {
            XCTAssertFalse(TrackingCode.isPlausibleScan(s), s)
        }
    }
}
