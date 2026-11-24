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

    /// PC v2 (SyncProtocol.BuildPhotoAck) gửi `ackStage: "photo"`, `outcome: "success"`, `captureId` mà KHÔNG gửi `photoSaved`.
    /// Ảnh phải được xác nhận lưu thành công; ngược lại `outcome: "error"` hoặc `photoSaved: false` tuyệt đối KHÔNG được coi là đã lưu (Quy tắc 6).
    func testPcV2PhotoAckWithoutPhotoSavedFieldConfirmsPhotoAndEnforcesRule6() {
        var it = ScanItem(requestId: "r-img", code: "SPXVN0999", kind: .returnScan, time: 100, photoFile: "r-img.jpg")
        XCTAssertTrue(it.apply(PcAck(requestId: "r-img", code: "SPXVN0999", outcome: "success", ackStage: "scan", photoSaved: nil, captureId: "r-img", orderNum: nil, extra: nil)))
        XCTAssertTrue(it.scanConfirmed)
        XCTAssertFalse(it.photoConfirmed)
        XCTAssertFalse(it.isSettled, "ảnh chưa ACK chưa được coi là hoàn tất")

        // Lỗi tạm thời khi lưu ảnh: vẫn giữ chưa lưu
        XCTAssertTrue(it.apply(PcAck(requestId: "r-img", code: "SPXVN0999", outcome: "error", ackStage: "photo", photoSaved: nil, captureId: "r-img", orderNum: nil, extra: "DB locked")))
        XCTAssertFalse(it.photoConfirmed)
        XCTAssertFalse(it.isSettled)
        XCTAssertEqual(it.status, .success, "lỗi ảnh không làm mất trạng thái quét thành công")

        // photoSaved == false: tuyệt đối không coi đã lưu
        XCTAssertFalse(it.apply(PcAck(requestId: "r-img", code: "SPXVN0999", outcome: "success", ackStage: "photo", photoSaved: false, captureId: "r-img", orderNum: nil, extra: nil)))
        XCTAssertFalse(it.photoConfirmed)

        // PC v2 BuildPhotoAck: ackStage="photo", outcome="success", photoSaved=nil, khớp captureId
        XCTAssertTrue(it.apply(PcAck(requestId: "r-img", code: "SPXVN0999", outcome: "success", ackStage: "photo", photoSaved: nil, captureId: "r-img", orderNum: nil, extra: "Đã lưu ảnh")))
        XCTAssertTrue(it.photoConfirmed)
        XCTAssertTrue(it.isSettled)
    }

    func testScanItemBackwardCompatibleDecodeAndNotePersistence() throws {
        // JSON cũ chưa có trường `note` và còn trường thừa `pushed`
        let legacyJSON = #"{"requestId":"old-1","captureId":"old-1","code":"SPXVN123456","kind":"returnScan","time":1000,"status":"success","scanConfirmed":true,"photoConfirmed":false,"reviewRequired":false,"photoSentAt":0,"photoAttempts":0,"lastSendAt":0,"attempts":1,"detail":"OK","pushed":true}"#
        let decoded = try JSONDecoder().decode(ScanItem.self, from: Data(legacyJSON.utf8))
        XCTAssertEqual(decoded.requestId, "old-1")
        XCTAssertEqual(decoded.note, "")

        let withNote = ScanItem(requestId: "new-1", code: "SPXVN999999", kind: .newOrder, time: 2000, photoFile: nil, note: "Khách hẹn hoàn")
        let roundTrip = try JSONDecoder().decode(ScanItem.self, from: JSONEncoder().encode(withNote))
        XCTAssertEqual(roundTrip.note, "Khách hẹn hoàn")
    }

    func testTrimHistoryDropsOnlySettledBeyondLimit() {
        var list: [ScanItem] = []
        for i in 0..<5 {
            var it = ScanItem(requestId: "id-\(i)", code: "SPXVN00000\(i)", kind: .returnScan, time: 500 - i, photoFile: "id-\(i).jpg")
            if i == 3 {
                // Mục #3 ở đuôi nhưng CHƯA ACK ảnh -> không được xóa
                it.scanConfirmed = true
                it.photoConfirmed = false
            } else {
                it.scanConfirmed = true
                it.photoConfirmed = true
            }
            list.append(it)
        }
        let res = ScanItem.trimHistory(list, limit: 2)
        XCTAssertEqual(res.kept.map { $0.requestId }, ["id-0", "id-1", "id-3"])
        XCTAssertEqual(res.dropped.map { $0.requestId }, ["id-2", "id-4"])
    }
}
