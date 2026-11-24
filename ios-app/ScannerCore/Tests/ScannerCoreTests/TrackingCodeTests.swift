import XCTest
@testable import ScannerCore

final class TrackingCodeTests: XCTestCase {
    // Kết quả mong đợi được sinh ra bằng cách chạy chính hàm extractCleanTrackingCode của mobile_scanner.html.
    func testMatchesWebImplementation() {
        let cases: [(String, String)] = [
        ("SPXVN067887279218", "SPXVN067887279218"),
        ("  *spxvn067887279218* ", "SPXVN067887279218"),
        ("https://spx.vn/track?tracking_no=SPXVN062176759678", "SPXVN062176759678"),
        ("spx.vn/SPXVN0621767596", "SPXVN0621767596"),
        ("TTVN1129574777", "TTVN1129574777"),
        ("587152900046530012988", "587152900046530012988"),
        ("5871529018938824674", "5871529018938824674"),
        ("2611137P8BRQ50", "2611137P8BRQ50"),
        ("SOC123456789ABC", "SOC123456789ABC"),
        ("NIVN12345678ab", "NIVN12345678AB"),
        ("VTP12345678", "VTP12345678"),
        ("GHN123456789", "GHN123456789"),
        ("S123.456-ABCDEFGH", "S123.456-ABCDEFGH"),
        ("862123456789012", "862123456789012"),
        ("ab/CD|SPXVN123456789", "SPXVN123456789"),
        ("(abc12345)", "ABC12345"),
        ("ĐƠN 2611111UTM1CWP", "ĐƠN 2611111UTM1CWP"),
        ("123", "123"),
        ("", ""),
        ("  ", ""),
        ("https://shopee.vn/", ""),
        ("j&t 85123456789012 x", "J&T 85123456789012 X")
        ]
        for (input, expected) in cases {
            XCTAssertEqual(TrackingCode.clean(input), expected, "input: \(input)")
        }
    }

    func testCarrier() {
        XCTAssertEqual(TrackingCode.carrier(for: "SPXVN067887279218"), "SPX Express")
        XCTAssertEqual(TrackingCode.carrier(for: "587152900046530012988"), "J&T Express")
        XCTAssertEqual(TrackingCode.carrier(for: "XYZ"), "")
    }

    func testQRJSONMultilineInvisibleAndPasteCodes() {
        // QR dạng JSON (phẳng và lồng nhau)
        XCTAssertEqual(TrackingCode.clean(#"{"tracking_no":"ttvn1129574777"}"#), "TTVN1129574777")
        XCTAssertEqual(TrackingCode.clean(#"{"data":{"waybill":"spxvn062176759678"}}"#), "SPXVN062176759678")
        // QR nhiều dòng (chữ + mã vận đơn không thuộc regex cố định)
        XCTAssertEqual(TrackingCode.clean("Shopee Express\nMã vận đơn: ttvn1129574777\nHCM"), "TTVN1129574777")
        // URL có khoảng trắng, chữ thường, hoặc fragment
        XCTAssertEqual(TrackingCode.clean("www.spx.vn/track?code=spxvn062176759678"), "SPXVN062176759678")
        XCTAssertEqual(TrackingCode.clean("https://track.example.com/order#TTVN1129574777"), "TTVN1129574777")
        // Ký tự vô hình BOM / zero-width space
        XCTAssertEqual(TrackingCode.clean("\u{FEFF}\u{200B}spxvn067887279218\u{200C}"), "SPXVN067887279218")
        XCTAssertTrue(TrackingCode.isPlausibleScan("\u{FEFF}spxvn067887279218"))
        // Dán nhiều mã (lọc chữ thừa, bỏ trùng, tách URL/JSON)
        let pasted = "Danh sách:\n  spxvn067887279218, https://spx.vn/track?code=SPXVN067887279218; TTVN1129574777 | abc"
        XCTAssertEqual(TrackingCode.extractPasteCodes(pasted), ["SPXVN067887279218", "TTVN1129574777"])
        XCTAssertEqual(TrackingCode.extractPasteCodes(#"{"tracking_number":"TTVN1129574777"}"#), ["TTVN1129574777"])
    }
}
