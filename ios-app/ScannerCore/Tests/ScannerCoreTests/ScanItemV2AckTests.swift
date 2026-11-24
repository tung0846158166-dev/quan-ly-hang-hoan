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

    /// T039: Lượt quét CHỈ cần `scanConfirmed` là hoàn tất (`isSettled`), không chờ ACK ảnh từ PC nữa.
    /// Các bản tin `ackStage: "photo"` cũ (nếu còn sót) bị bỏ qua an toàn; lỗi lưu lệnh quét (`outcome: "error"`) vẫn chưa xác nhận (Quy tắc 6).
    func testScanSettlesOnScanConfirmedWithoutWaitingForPhotoAck() {
        var it = ScanItem(requestId: "r-img", code: "SPXVN0999", kind: .returnScan, time: 100, photoFile: "r-img.jpg")
        XCTAssertFalse(it.isSettled)

        // Lỗi tạm thời lúc PC lưu lệnh quét: KHÔNG được coi là đã quét (Quy tắc 6)
        XCTAssertTrue(it.apply(PcAck(requestId: "r-img", code: "SPXVN0999", outcome: "error", ackStage: "scan", photoSaved: nil, captureId: "r-img", orderNum: nil, extra: "DB locked")))
        XCTAssertFalse(it.scanConfirmed)
        XCTAssertFalse(it.isSettled)

        // PC xác nhận lệnh quét -> hoàn tất ngay dù có `photoFile` và `photoConfirmed == false`
        XCTAssertTrue(it.apply(PcAck(requestId: "r-img", code: "SPXVN0999", outcome: "success", ackStage: "scan", photoSaved: nil, captureId: "r-img", orderNum: "12", extra: nil)))
        XCTAssertTrue(it.scanConfirmed)
        XCTAssertFalse(it.photoConfirmed)
        XCTAssertTrue(it.isSettled, "T039: chỉ cần scanConfirmed là xong, không chờ ACK ảnh")
        XCTAssertFalse(it.detail.contains("gửi ngầm"))

        // Gói ackStage == "photo" đến muộn bị bỏ qua an toàn
        XCTAssertFalse(it.apply(PcAck(requestId: "r-img", code: "SPXVN0999", outcome: "success", ackStage: "photo", photoSaved: true, captureId: "r-img", orderNum: nil, extra: nil)))
    }

    func testScanItemBackwardCompatibleDecodeAndNewLocalPhotoFields() throws {
        // JSON cũ từ bản trước T039 (có photoConfirmed/photoSentAt/photoAttempts/pushed, chưa có note/attachedOrderId/attachedPhotoId)
        let legacyJSON = #"{"requestId":"old-1","captureId":"old-1","code":"SPXVN123456","kind":"returnScan","time":1000,"status":"success","scanConfirmed":true,"photoConfirmed":false,"reviewRequired":false,"photoFile":"old-1.jpg","photoSentAt":900,"photoAttempts":3,"lastSendAt":0,"attempts":1,"detail":"Đã xác nhận quét. Ảnh đang gửi ngầm.","pushed":true}"#
        let decoded = try JSONDecoder().decode(ScanItem.self, from: Data(legacyJSON.utf8))
        XCTAssertEqual(decoded.requestId, "old-1")
        XCTAssertEqual(decoded.photoFile, "old-1.jpg")
        XCTAssertFalse(decoded.photoConfirmed)
        XCTAssertEqual(decoded.photoAttempts, 3)
        XCTAssertTrue(decoded.isSettled, "Bản ghi cũ đã scanConfirmed nay coi là hoàn tất dù ảnh chưa gửi")
        XCTAssertTrue(decoded.hasTemporaryPhoto)
        XCTAssertNil(decoded.attachedOrderId)
        XCTAssertNil(decoded.attachedPhotoId)
        XCTAssertFalse(decoded.photoAttachDismissed)
        XCTAssertEqual(decoded.note, "")

        var withLocal = ScanItem(requestId: "new-1", code: "SPXVN999999", kind: .newOrder, time: 2000, photoFile: nil, note: "Khách hẹn hoàn")
        withLocal.attachedOrderId = "m|SPXVN999999|O1"
        withLocal.attachedPhotoId = "ph_2000_abcd"
        withLocal.photoAttachDismissed = true
        let roundTrip = try JSONDecoder().decode(ScanItem.self, from: JSONEncoder().encode(withLocal))
        XCTAssertEqual(roundTrip.note, "Khách hẹn hoàn")
        XCTAssertEqual(roundTrip.attachedOrderId, "m|SPXVN999999|O1")
        XCTAssertEqual(roundTrip.attachedPhotoId, "ph_2000_abcd")
        XCTAssertTrue(roundTrip.photoAttachDismissed)
    }

    func testTrimHistoryKeepsUnsettledAndUnattachedPhotoItems() {
        var list: [ScanItem] = []
        for i in 0..<6 {
            var it = ScanItem(requestId: "id-\(i)", code: "SPXVN00000\(i)", kind: .returnScan, time: 500 - i, photoFile: nil)
            if i == 3 {
                // Mục #3 ở đuôi nhưng CHƯA scanConfirmed -> không được xóa (Quy tắc 6)
                it.scanConfirmed = false
            } else if i == 4 {
                // Mục #4 ở đuôi đã scanConfirmed nhưng còn ảnh chưa gắn vào đơn -> không được xóa (Quy tắc 8)
                it.scanConfirmed = true
                it.photoFile = "id-4.jpg"
            } else {
                it.scanConfirmed = true
                it.attachedPhotoId = "ph-\(i)"
            }
            list.append(it)
        }
        let res = ScanItem.trimHistory(list, limit: 2)
        XCTAssertEqual(res.kept.map { $0.requestId }, ["id-0", "id-1", "id-3", "id-4"])
        XCTAssertEqual(res.dropped.map { $0.requestId }, ["id-2", "id-5"])
    }
}
