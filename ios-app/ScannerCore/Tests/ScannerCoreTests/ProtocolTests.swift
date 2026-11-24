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

    func testPhotoChunksRoundTripAndSafeChunkSize() {
        let data = "data:image/jpeg;base64," + String(repeating: "AbC1", count: 1500)
        let chunks = SyncProtocol.photoChunks(dataURL: data, code: "C", kind: .returnScan, requestId: "r", captureId: "r",
                                              captureTime: 1, room: "ROOM-TEST-2026", now: 100, photoId: "ph_1")
        XCTAssertEqual(chunks.count, 3)
        XCTAssertEqual(chunks.compactMap { $0["total"] as? Int }, [3, 3, 3])
        let joined = chunks.compactMap { $0["chunk"] as? String }.joined()
        XCTAssertEqual(joined, data)
        XCTAssertLessThan(SyncProtocol.json(chunks[0]).utf8.count, 4000)

        // chunkSize <= 0 không chia cho 0
        let safeChunks = SyncProtocol.photoChunks(dataURL: "abc", code: "C", kind: .returnScan, requestId: "r", captureId: "r",
                                                  captureTime: 1, room: "ROOM-TEST-2026", now: 100, photoId: "ph_2", chunkSize: 0)
        XCTAssertEqual(safeChunks.count, 3)
        XCTAssertEqual(safeChunks.compactMap { $0["chunk"] as? String }.joined(), "abc")
    }

    func testParseAckAndRejectEmptyRoom() {
        let inner = "{\"type\":\"pc_ack\",\"room\":\"ROOM-TEST-2026\",\"requestId\":\"s-1\",\"code\":\"spxvn067887279218\",\"outcome\":\"success\",\"orderNum\":12,\"captureId\":\"s-1\",\"photoSaved\":true,\"ackStage\":\"photo\"}"
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
        let scanAck = PcAck(requestId: "s-1", code: "SPXVN067887279218", outcome: "success", ackStage: nil, photoSaved: nil, captureId: nil, orderNum: "7", extra: nil)
        XCTAssertTrue(item.apply(scanAck))
        XCTAssertTrue(item.scanConfirmed)
        XCTAssertEqual(item.status, .success)
        XCTAssertFalse(item.isSettled, "còn chờ ảnh")
        // ack sai request / sai captureId bị bỏ qua
        XCTAssertFalse(item.apply(PcAck(requestId: "x", code: "SPXVN067887279218", outcome: "success", ackStage: nil, photoSaved: nil, captureId: nil, orderNum: nil, extra: nil)))
        XCTAssertFalse(item.apply(PcAck(requestId: "s-1", code: "SPXVN067887279218", outcome: "success", ackStage: "photo", photoSaved: true, captureId: "khac", orderNum: nil, extra: nil)))
        // photoSaved == false tuyệt đối KHÔNG coi ảnh đã lưu (Quy tắc 6)
        XCTAssertFalse(item.apply(PcAck(requestId: "s-1", code: "SPXVN067887279218", outcome: "success", ackStage: "photo", photoSaved: false, captureId: "s-1", orderNum: nil, extra: nil)))
        XCTAssertFalse(item.photoConfirmed)
        XCTAssertTrue(item.apply(PcAck(requestId: "s-1", code: "SPXVN067887279218", outcome: "success", ackStage: "photo", photoSaved: true, captureId: "s-1", orderNum: nil, extra: nil)))
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
