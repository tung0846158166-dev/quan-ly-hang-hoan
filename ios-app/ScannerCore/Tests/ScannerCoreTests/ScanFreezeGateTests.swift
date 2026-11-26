import XCTest
@testable import ScannerCore

/// Chủ dự án 26/11/2026: nhận được mã thì khung camera ĐỨNG ~0,2 giây (thấy mã vừa quét + kết quả) rồi mới quét tiếp.
final class ScanFreezeGateTests: XCTestCase {
    func testFirstCodeIsAcceptedThenEverythingIsIgnoredWhileFrozen() {
        var g = ScanFreezeGate(duration: 0.2)
        let t0 = Date(timeIntervalSince1970: 1000)
        XCTAssertTrue(g.accept(at: t0))
        XCTAssertTrue(g.isFrozen(at: t0.addingTimeInterval(0.1)))
        XCTAssertFalse(g.accept(at: t0.addingTimeInterval(0.1)))      // đang đứng: bỏ
        XCTAssertFalse(g.accept(at: t0.addingTimeInterval(0.19)))
        XCTAssertFalse(g.isFrozen(at: t0.addingTimeInterval(0.2)))
        XCTAssertTrue(g.accept(at: t0.addingTimeInterval(0.2)))        // hết 0,2 giây: quét tiếp
    }

    func testIgnoredCodesDoNotExtendTheFreeze() {
        var g = ScanFreezeGate(duration: 0.2)
        let t0 = Date(timeIntervalSince1970: 1000)
        _ = g.accept(at: t0)
        _ = g.accept(at: t0.addingTimeInterval(0.15))
        XCTAssertTrue(g.accept(at: t0.addingTimeInterval(0.21)))
    }

    func testDurationIsClampedToASaneRange() {
        XCTAssertEqual(ScanFreezeGate(duration: -1).duration, 0)
        XCTAssertEqual(ScanFreezeGate(duration: 10).duration, 2)
        var off = ScanFreezeGate(duration: 0)                            // 0 = tắt: không bao giờ đứng
        let t0 = Date(timeIntervalSince1970: 1000)
        XCTAssertTrue(off.accept(at: t0))
        XCTAssertTrue(off.accept(at: t0))
    }
}
