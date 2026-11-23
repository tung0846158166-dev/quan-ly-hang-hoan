import XCTest
@testable import ScannerCore

final class ProtocolTests: XCTestCase {
    func testTopicAndRoom() {
        XCTAssertEqual(SyncProtocol.topic(room: "SHP-2026"), "agy_shopee_shp-2026")
        XCTAssertEqual(SyncProtocol.topic(room: "A B!c"), "agy_shopee_abc")
        XCTAssertEqual(SyncProtocol.normalizeRoom("  "), "")          // v2: không còn mã phòng mặc định
        XCTAssertEqual(SyncProtocol.normalizeRoom(" shp-1 "), "SHP-1")
    }

    func testRoomValidationRejectsShortAndDefaultCodes() {
        XCTAssertFalse(SyncProtocol.isValidRoom(""))
        XCTAssertFalse(SyncProtocol.isValidRoom("SHP-2026"))           // mã mặc định của app cũ: quá ngắn
        XCTAssertFalse(SyncProtocol.isValidRoom("  abc  "))
        XCTAssertTrue(SyncProtocol.isValidRoom("k7Qp-2xMa-9TzW-4bYc-Lr3H"))
    }

    func testScanPayloads() {
        let r = SyncProtocol.scanPayload(kind: .returnScan, code: "SPXVN1", requestId: "s-1", captureId: "s-1", captureTime: 5, time: 6, room: "R")
        XCTAssertEqual(r["type"] as? String, "barcode")
        XCTAssertNil(r["photo"])
        let c = SyncProtocol.scanPayload(kind: .cancelOrder, code: "SPXVN067887279218", requestId: "s-2", captureId: "s-2", captureTime: 5, time: 6, room: "R")
        XCTAssertEqual(c["type"] as? String, "new_order_scan")
        XCTAssertEqual(c["cancelled"] as? Bool, true)
        XCTAssertEqual(c["carrier"] as? String, "SPX Express")
        let n = SyncProtocol.scanPayload(kind: .newOrder, code: "X123456", requestId: "s-3", captureId: "s-3", captureTime: 5, time: 6, room: "R")
        XCTAssertEqual(n["cancelled"] as? Bool, false)
    }

    func testPhotoChunksRoundTrip() {
        let data = "data:image/jpeg;base64," + String(repeating: "AbC1", count: 1500)
        let chunks = SyncProtocol.photoChunks(dataURL: data, code: "C", kind: .returnScan, requestId: "r", captureId: "r",
                                              captureTime: 1, room: "R", now: 100, photoId: "ph_1")
        XCTAssertEqual(chunks.count, 3)
        XCTAssertEqual(chunks.compactMap { $0["total"] as? Int }, [3, 3, 3])
        let joined = chunks.compactMap { $0["chunk"] as? String }.joined()
        XCTAssertEqual(joined, data)
        XCTAssertLessThan(SyncProtocol.json(chunks[0]).utf8.count, 4000)
    }

    func testParseAck() {
        let inner = "{\"type\":\"pc_ack\",\"room\":\"R\",\"requestId\":\"s-1\",\"code\":\"spxvn067887279218\",\"outcome\":\"success\",\"orderNum\":12,\"captureId\":\"s-1\",\"photoSaved\":true,\"ackStage\":\"photo\"}"
        let outer = SyncProtocol.json(["event": "message", "message": inner])
        guard case .ack(let a)? = SyncProtocol.parseIncoming(sseData: outer, room: "R") else { return XCTFail("không đọc được ack") }
        XCTAssertEqual(a.code, "SPXVN067887279218")
        XCTAssertEqual(a.orderNum, "12")
        XCTAssertEqual(a.photoSaved, true)
        XCTAssertNil(SyncProtocol.parseIncoming(sseData: outer, room: "KHAC"))
        XCTAssertNil(SyncProtocol.parseIncoming(sseData: "{\"event\":\"open\"}", room: "R"))
        XCTAssertEqual(SyncProtocol.parseMessage("{\"type\":\"pc_hello\",\"room\":\"R\"}", room: "R"), .hello)
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

    func testRetrySchedule() {
        var i = ScanItem(requestId: "a", code: "C1", kind: .returnScan, time: 1, photoFile: nil)
        i.lastSendAt = 1000
        XCTAssertEqual(i.nextRetryDue(), 2500)
        i.attempts = 99
        XCTAssertEqual(i.nextRetryDue(), 61000)
    }
}
