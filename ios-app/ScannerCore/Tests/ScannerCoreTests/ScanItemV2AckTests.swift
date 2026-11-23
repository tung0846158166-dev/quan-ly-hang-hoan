import XCTest
@testable import ScannerCore

/// Kết quả PC v2 trả về cho lượt quét: tạo đơn mới thành công, PC từ chối dứt khoát (invalid/locked) thì thôi gửi lại.
final class ScanItemV2AckTests: XCTestCase {
    private func item(_ kind: ScanKind = .newOrder) -> ScanItem {
        ScanItem(requestId: "r1", code: "SPXVN0999", kind: kind, time: 1, photoFile: nil)
    }

    private func ack(_ outcome: String, extra: String? = nil, stage: String = "scan") -> PcAck {
        PcAck(requestId: "r1", code: "SPXVN0999", outcome: outcome, ackStage: stage, photoSaved: nil, captureId: nil, orderNum: nil, extra: extra)
    }

    func testNewOrderSuccessSettlesTheScan() {
        var it = item()
        XCTAssertTrue(it.apply(ack("success", extra: "ĐÃ TẠO ĐƠN MỚI Ở BẢNG CHÍNH (Shopee)")))
        XCTAssertEqual(it.status, .success)
        XCTAssertTrue(it.scanConfirmed)
        XCTAssertTrue(it.isSettled)
        XCTAssertEqual(it.detail, "ĐÃ TẠO ĐƠN MỚI Ở BẢNG CHÍNH (Shopee)")
    }

    func testInvalidAndLockedAreFinalRefusalsNotRetriedForever() {
        for outcome in ["invalid", "locked"] {
            var it = item()
            XCTAssertTrue(it.apply(ack(outcome, extra: "PC từ chối")), outcome)
            XCTAssertTrue(it.scanConfirmed, outcome)          // PC đã trả lời dứt khoát
            XCTAssertTrue(it.reviewRequired, outcome)         // không gửi lại, chờ người kiểm tra
            XCTAssertEqual(it.status, .error, outcome)
            XCTAssertEqual(it.detail, "PC từ chối", outcome)
            XCTAssertTrue(it.isSettled, outcome)
        }
    }

    func testRefusalWithoutReasonStillExplainsAndLaterAcksAreIgnored() {
        var it = item()
        XCTAssertTrue(it.apply(ack("invalid")))
        XCTAssertTrue(it.detail.contains("PC từ chối"))
        XCTAssertFalse(it.apply(ack("success")))              // đã chờ kiểm tra: không bị đổi bởi tin muộn
        XCTAssertEqual(it.status, .error)
    }

    func testRefusalForAnotherRequestIsIgnored() {
        var it = item()
        let other = PcAck(requestId: "khac", code: "SPXVN0999", outcome: "invalid", ackStage: "scan", photoSaved: nil, captureId: nil, orderNum: nil, extra: nil)
        XCTAssertFalse(it.apply(other))
        XCTAssertFalse(it.scanConfirmed)
    }

    func testUnknownOutcomeIsStillIgnored() {
        var it = item()
        XCTAssertFalse(it.apply(ack("khong-biet")))
        XCTAssertEqual(it.status, .pending)
    }
}
