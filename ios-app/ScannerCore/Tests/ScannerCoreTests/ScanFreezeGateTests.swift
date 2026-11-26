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

/// Chủ dự án 26/11/2026 (thử thật): giữ nhãn trước camera thì khung đứng/chạy liên tục như tụt khung hình và không biết đã quét chưa.
/// Cùng một nhãn đang giữ = MỘT lượt quét (đứng một lần), nhãn rời khung > 1 giây rồi quay lại mới là lượt mới.
final class ScanHoldTrackerTests: XCTestCase {
    let t0 = Date(timeIntervalSince1970: 1000)

    func testHoldingTheSameLabelIsOneScanAndFreezesOnce() {
        var h = ScanHoldTracker(freeze: 0.2, release: 1.0)
        XCTAssertEqual(h.observe(["SPXVN01"], at: t0), .newScan)
        var t = t0
        for _ in 0..<60 {                                   // giữ nhãn 2,5 giây, 24 hình/giây
            t = t.addingTimeInterval(1.0 / 24)
            XCTAssertEqual(h.observe(["SPXVN01"], at: t), .stillHolding)
        }
        XCTAssertTrue(h.isHolding(at: t.addingTimeInterval(0.5)))
        XCTAssertFalse(h.isFrozen(at: t))                   // chỉ đứng 0,2 giây đầu
        XCTAssertEqual(h.heldCodes, ["SPXVN01"])
    }

    func testLabelThatLeftForMoreThanTheReleaseTimeIsANewScan() {
        var h = ScanHoldTracker(freeze: 0.2, release: 1.0)
        _ = h.observe(["SPXVN01"], at: t0)
        XCTAssertFalse(h.isHolding(at: t0.addingTimeInterval(1.01)))
        XCTAssertEqual(h.observe(["SPXVN01"], at: t0.addingTimeInterval(1.2)), .newScan)
    }

    func testAnotherLabelAfterTheFreezeIsANewScanButNotDuringIt() {
        var h = ScanHoldTracker(freeze: 0.2, release: 1.0)
        _ = h.observe(["SPXVN01"], at: t0)
        XCTAssertEqual(h.observe(["SPXVN02"], at: t0.addingTimeInterval(0.1)), .ignored)   // đang đứng
        XCTAssertEqual(h.observe(["SPXVN02"], at: t0.addingTimeInterval(0.3)), .newScan)
        XCTAssertEqual(h.heldCodes, ["SPXVN02"])
    }

    func testLabelWithTwoBarcodesCountsAsTheSameLabel() {
        var h = ScanHoldTracker(freeze: 0.2, release: 1.0)
        _ = h.observe(["SPXVN01", "2611ORDER"], at: t0)
        XCTAssertEqual(h.observe(["2611ORDER"], at: t0.addingTimeInterval(0.5)), .stillHolding)
    }
}
