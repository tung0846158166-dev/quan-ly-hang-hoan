import XCTest
@testable import ScannerCore

/// Nhãn và màu kết quả quét theo quy ước của chủ dự án.
final class ScanDisplayTests: XCTestCase {
    private func item(_ kind: ScanKind, _ status: ScanStatus, confirmed: Bool = true, cancelled: Bool = false) -> ScanItem {
        var it = ScanItem(requestId: "r1", code: "VN001", kind: kind, time: 1, photoFile: nil)
        it.status = status
        it.scanConfirmed = confirmed
        it.isCancelledOrder = cancelled
        return it
    }

    func testReturnScanLabelsAndColors() {
        XCTAssertEqual(ScanDisplay.make(for: item(.returnScan, .success)), ScanDisplay(label: "Thành công", tone: .success))
        XCTAssertEqual(ScanDisplay.make(for: item(.returnScan, .duplicate)), ScanDisplay(label: "Quét trùng", tone: .danger))
        XCTAssertEqual(ScanDisplay.make(for: item(.returnScan, .notFound)), ScanDisplay(label: "Không có", tone: .warning))
    }

    func testCancelledOrderIsGreenWhenScannedNowAndRedWhenAlreadyScanned() {
        XCTAssertEqual(ScanDisplay.make(for: item(.returnScan, .success, cancelled: true)), ScanDisplay(label: "Đơn hủy", tone: .success))
        XCTAssertEqual(ScanDisplay.make(for: item(.returnScan, .duplicate, cancelled: true)), ScanDisplay(label: "Đơn hủy", tone: .danger))
        XCTAssertEqual(ScanDisplay.message(for: item(.returnScan, .success, cancelled: true)).tone, .success)
        XCTAssertEqual(ScanDisplay.message(for: item(.returnScan, .duplicate, cancelled: true)).tone, .danger)
        XCTAssertTrue(ScanDisplay.message(for: item(.returnScan, .duplicate, cancelled: true)).label.contains("Đơn hủy"))
    }

    func testNewOrderTabLabels() {
        XCTAssertEqual(ScanDisplay.make(for: item(.newOrder, .success)), ScanDisplay(label: "Tạo thành công", tone: .success))
        XCTAssertEqual(ScanDisplay.make(for: item(.newOrder, .duplicate)), ScanDisplay(label: "Đã tạo rồi", tone: .danger))
        XCTAssertTrue(ScanDisplay.message(for: item(.newOrder, .duplicate)).label.hasPrefix("Đã tạo rồi"))
        XCTAssertTrue(ScanDisplay.message(for: item(.newOrder, .success)).label.hasPrefix("Tạo thành công"))
    }

    func testPendingAndErrorStaysYellowAndRed() {
        XCTAssertEqual(ScanDisplay.make(for: item(.returnScan, .pending, confirmed: false)).tone, .warning)
        XCTAssertEqual(ScanDisplay.make(for: item(.returnScan, .error, confirmed: false)).label, "Lỗi, đang thử lại")
        XCTAssertEqual(ScanDisplay.make(for: item(.returnScan, .ambiguous)).tone, .danger)
    }

    func testAckWithCancelledFlagMarksTheItemAndOldPcWithoutFlagDoesNot() {
        var a = ScanItem(requestId: "r1", code: "VN001", kind: .returnScan, time: 1, photoFile: nil)
        let withFlag = PcAck(requestId: "r1", code: "VN001", outcome: "success", ackStage: "scan", extra: "ok", cancelled: true)
        XCTAssertTrue(a.apply(withFlag))
        XCTAssertTrue(a.isCancelledOrder)
        XCTAssertEqual(ScanDisplay.make(for: a).label, "Đơn hủy")

        var b = ScanItem(requestId: "r2", code: "VN002", kind: .returnScan, time: 1, photoFile: nil)
        XCTAssertTrue(b.apply(PcAck(requestId: "r2", code: "VN002", outcome: "success", ackStage: "scan", extra: "ok")))
        XCTAssertFalse(b.isCancelledOrder)
        XCTAssertEqual(ScanDisplay.make(for: b).label, "Thành công")
    }

    func testParsingAckReadsCancelledAndOldFilesStillDecode() throws {
        let msg = #"{"type":"pc_ack","room":"R","requestId":"r1","code":"VN001","outcome":"duplicate","extra":"x","ackStage":"scan","cancelled":true}"#
        if case .ack(let ack) = SyncProtocol.parseMessage(msg, room: "R") ?? .other {
            XCTAssertEqual(ack.cancelled, true)
        } else { XCTFail("phải là ack") }
        // tệp lượt quét cũ (không có isCancelledOrder) vẫn đọc được
        var it = ScanItem(requestId: "r9", code: "VN9", kind: .returnScan, time: 5, photoFile: nil)
        it.isCancelledOrder = true
        let data = try JSONEncoder().encode(it)
        XCTAssertTrue(try JSONDecoder().decode(ScanItem.self, from: data).isCancelledOrder)
        let old = #"{"requestId":"r1","code":"VN1","kind":"returnScan","time":5}"#
        XCTAssertFalse(try JSONDecoder().decode(ScanItem.self, from: Data(old.utf8)).isCancelledOrder)
    }
}
