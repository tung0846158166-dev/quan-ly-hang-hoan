import XCTest
@testable import ScannerCore

final class ProtocolTests: XCTestCase {
    func testTopicAndRoom() {
        XCTAssertEqual(SyncProtocol.topic(room: "ROOM-TEST-2026"), "agy_shopee_room-test-2026")
        XCTAssertEqual(SyncProtocol.topic(room: "A B!c"), "agy_shopee_abc")
        XCTAssertEqual(SyncProtocol.normalizeRoom("  "), "")          // v2: không còn mã phòng mặc định
        XCTAssertEqual(SyncProtocol.normalizeRoom(" room-1 "), "ROOM-1")
    }

    func testRoomValidationRejectsShortCodes() {
        XCTAssertFalse(SyncProtocol.isValidRoom(""))
        XCTAssertFalse(SyncProtocol.isValidRoom("SHORT-01"))           // mã 8 ký tự: quá ngắn
        XCTAssertFalse(SyncProtocol.isValidRoom("  abc  "))
        XCTAssertTrue(SyncProtocol.isValidRoom("k7Qp-2xMa-9TzW-4bYc-Lr3H"))
    }

    func testScanPayloads() {
        let r = SyncProtocol.scanPayload(kind: .returnScan, code: "SPXVN1", requestId: "s-1", captureId: "s-1", captureTime: 5, time: 6, room: "ROOM-TEST-2026")
        XCTAssertEqual(r["type"] as? String, "barcode")
        XCTAssertNil(r["photo"])
        let n = SyncProtocol.scanPayload(kind: .newOrder, code: "SPXVN067887279218", requestId: "s-3", captureId: "s-3", captureTime: 5, time: 6, room: "ROOM-TEST-2026", note: "Khách gửi hoàn")
        XCTAssertEqual(n["type"] as? String, "new_order_scan")
        XCTAssertEqual(n["cancelled"] as? Bool, false)
        XCTAssertEqual(n["carrier"] as? String, "SPX Express")
        XCTAssertEqual(n["note"] as? String, "Khách gửi hoàn")
    }

    func testParsePong() {
        XCTAssertEqual(SyncProtocol.parseMessage("{\"type\":\"pc_pong\",\"room\":\"R\",\"id\":\"abc\"}", room: "R"), .pong(id: "abc"))
        XCTAssertNil(SyncProtocol.parseMessage("{\"type\":\"pc_pong\",\"room\":\"X\",\"id\":\"abc\"}", room: "R"))
    }

    func testParseAckAndRejectEmptyRoom() {
        let inner = "{\"type\":\"pc_ack\",\"room\":\"ROOM-TEST-2026\",\"requestId\":\"s-1\",\"code\":\"spxvn067887279218\",\"outcome\":\"success\",\"orderNum\":12,\"captureId\":\"s-1\",\"photoSaved\":true,\"ackStage\":\"scan\"}"
        let outer = SyncProtocol.json(["event": "message", "message": inner])
        guard case .ack(let a)? = SyncProtocol.parseIncoming(sseData: outer, room: "ROOM-TEST-2026") else { return XCTFail("không đọc được ack") }
        XCTAssertEqual(a.code, "SPXVN067887279218")
        XCTAssertEqual(a.orderNum, "12")
        XCTAssertEqual(a.photoSaved, true)
        XCTAssertNil(SyncProtocol.parseIncoming(sseData: outer, room: "KHAC"))
        XCTAssertNil(SyncProtocol.parseIncoming(sseData: outer, room: ""))
        XCTAssertNil(SyncProtocol.parseMessage("{\"type\":\"pc_hello\",\"room\":\"\"}", room: ""))
        XCTAssertNil(SyncProtocol.parseIncoming(sseData: "{\"event\":\"open\"}", room: "ROOM-TEST-2026"))
        XCTAssertEqual(SyncProtocol.parseMessage("{\"type\":\"pc_hello\",\"room\":\"ROOM-TEST-2026\"}", room: "ROOM-TEST-2026"), .hello)
    }

    func testItemStateMachine() {
        var item = ScanItem(requestId: "s-1", code: "SPXVN067887279218", kind: .returnScan, time: 1, photoFile: "s-1.jpg")
        XCTAssertFalse(item.isSettled)
        // ack sai request bị bỏ qua
        XCTAssertFalse(item.apply(PcAck(requestId: "x", code: "SPXVN067887279218", outcome: "success", ackStage: nil, photoSaved: nil, captureId: nil, orderNum: nil, extra: nil)))
        // ack giai đoạn ảnh bị bỏ qua (T039 không gửi ảnh sang PC)
        XCTAssertFalse(item.apply(PcAck(requestId: "s-1", code: "SPXVN067887279218", outcome: "success", ackStage: "photo", photoSaved: true, captureId: "s-1", orderNum: nil, extra: nil)))
        XCTAssertFalse(item.isSettled)
        // PC xác nhận lệnh quét -> hoàn tất ngay (không còn chờ ảnh)
        let scanAck = PcAck(requestId: "s-1", code: "SPXVN067887279218", outcome: "success", ackStage: nil, photoSaved: nil, captureId: nil, orderNum: "7", extra: nil)
        XCTAssertTrue(item.apply(scanAck))
        XCTAssertTrue(item.scanConfirmed)
        XCTAssertEqual(item.status, .success)
        XCTAssertTrue(item.isSettled)
    }

    func testDuplicateAndNotFound() {
        var d = ScanItem(requestId: "a", code: "C1", kind: .returnScan, time: 1, photoFile: nil)
        d.apply(PcAck(requestId: "a", code: "C1", outcome: "duplicate", ackStage: nil, photoSaved: nil, captureId: nil, orderNum: nil, extra: nil))
        XCTAssertEqual(d.status, .duplicate)
        XCTAssertTrue(d.isSettled)
        var n = ScanItem(requestId: "b", code: "C2", kind: .returnScan, time: 1, photoFile: "b.jpg")
        n.apply(PcAck(requestId: "b", code: "C2", outcome: "not_found", ackStage: nil, photoSaved: nil, captureId: nil, orderNum: nil, extra: nil))
        XCTAssertEqual(n.status, .notFound)
        XCTAssertTrue(n.reviewRequired)
        XCTAssertTrue(n.isSettled)
    }

    func testRetryScheduleAndNegativeAttempts() {
        var i = ScanItem(requestId: "a", code: "C1", kind: .returnScan, time: 1, photoFile: nil)
        i.lastSendAt = 1000
        XCTAssertEqual(i.nextRetryDue(), 2500)
        i.attempts = -5
        XCTAssertEqual(i.nextRetryDue(), 2500)
        i.attempts = 99
        XCTAssertEqual(i.nextRetryDue(), 61000)
    }
}
