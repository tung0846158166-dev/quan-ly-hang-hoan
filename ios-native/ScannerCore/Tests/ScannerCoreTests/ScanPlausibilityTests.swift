import XCTest
@testable import ScannerCore

final class ScanPlausibilityTests: XCTestCase {
    func testAcceptsRealCodes() {
        for s in ["SPXVN067887279218", "*spxvn067887279218*", "https://spx.vn/track?tracking_no=SPXVN062176759678",
                  "587152900046530012988", "862123456789012", "0714082259941", "2611137P8BRQ50", "TTVN1129574777", "GHN123456789"] {
            XCTAssertTrue(TrackingCode.isPlausibleScan(s), s)
        }
    }

    func testRejectsJunk() {
        for s in ["", "  ", "123", "ABCDEFGH", "12 34 56 78 90", "Xin chào các bạn 123456", "https://shopee.vn/", "ĐƠN 2611111UTM1CWP",
                  String(repeating: "1", count: 41), "ab#$%^&*cd1234"] {
            XCTAssertFalse(TrackingCode.isPlausibleScan(s), s)
        }
    }
}
