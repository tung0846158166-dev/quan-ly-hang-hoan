import XCTest
@testable import ScannerCore

final class SnapshotTests: XCTestCase {
    // Sinh bằng zlib deflate-raw (giống CompressionStream("deflate-raw") của PC).
    let fixture = "Zc9BTsJAFMbxq0y+9ST0DZ3OdHbWsKuAOBKMcUFKk5LKDArEKGHhDbyCblyZuGClSwz34CamoU1MfOv3Xn7/NZbTWQ5DKpZBMxyZX7kljOC49w8LmOs1ShjMwJHB4KI/GnaDSGmthIoFaXB4GIiIiNqqr5PBuQzAMa+WCz/Pc3A4GOyePev7W8/S3DFb7LbVx+p0NHYF6+7eVuBYPMEgBcfdkTCBAckWUUsEImIBmVAZSeCYrP7bF0sYnBb7zzFzxeHrw2HDj/ys5ls77BKJWKpQKVXbpVYkRRyQjttaizBSYR1gp6X1Jas6/lb00h5LTpIr2+k0EcmltZ1BU3B2LKC6oOE2xJ+X/atjxeH7/RGbm80v"

    func testDecodeFixture() throws {
        let p = try SnapshotAssembler.decode(base64: fixture)
        XCTAssertEqual(p.count, 2)
        XCTAssertEqual(p.rows[0].c, "SPXVN067887279218")
        XCTAssertEqual(p.rows[0].co, "Xanh Nâu")
        XCTAssertEqual(p.rows[0].q, 2)
        XCTAssertTrue(p.rows[1].isCancelled)
    }

    func testDecodeInboxAndCancelledId() throws {
        let json = """
        {"time":1,"count":1,"rows":[{"k":"c","c":"SPX1","o":"O1","p":"Shopee","n":"","co":"","sz":"","q":1,"rd":"","due":0,"st":"Đơn hủy","ci":"cxl_1"}],
         "inbox":[{"id":"inbox_1","c":"SPX9","p":"Shopee","t":"18/11/2026 05:00:00","u":1},{"id":"inbox_2","c":863,"dup":1},{"c":"thiếu id"},"rác"]}
        """
        let p = try JSONDecoder().decode(SnapshotPayload.self, from: Data(json.utf8))
        XCTAssertEqual(p.rows[0].ci, "cxl_1")
        XCTAssertEqual(p.inbox.map { $0.id }, ["inbox_1", "inbox_2"], "đơn lỗi / thiếu id bị bỏ, không bỏ cả hộp")
        XCTAssertTrue(p.inbox[0].u)
        XCTAssertEqual(p.inbox[1].c, "863")
        XCTAssertTrue(p.inbox[1].dup)
        // PC cũ không gửi hộp đơn mới
        let old = try JSONDecoder().decode(SnapshotPayload.self, from: Data(#"{"time":1,"count":0,"rows":[]}"#.utf8))
        XCTAssertTrue(old.inbox.isEmpty)
        // lưu file rồi đọc lại vẫn giữ hộp
        let again = try JSONDecoder().decode(SnapshotPayload.self, from: JSONEncoder().encode(p))
        XCTAssertEqual(again.inbox, p.inbox)
        XCTAssertEqual(again.rows[0].ci, "cxl_1")
    }

    func testPlatformStatusCancelAfterPrep() throws {
        let json = #"{"time":1,"count":3,"rows":[{"k":"m","c":"A1","ps":"Huỷ sau chuẩn bị · Đã huỷ"},{"k":"m","c":"A2","ps":"HỦY SAU CHUẨN BỊ"},{"k":"m","c":"A3","ps":"Đã đồng ý"}]}"#
        let p = try JSONDecoder().decode(SnapshotPayload.self, from: Data(json.utf8))
        XCTAssertEqual(p.rows.map { $0.isCancelAfterPrep }, [true, true, false])
        XCTAssertEqual(p.rows[0].ps, "Huỷ sau chuẩn bị · Đã huỷ")
    }

    func testInboxPushManyAndCancelReceiveMessages() throws {
        let a = SyncProtocol.inboxPushMany(ids: ["inbox_1", "inbox_2"], room: "R", requestId: "q1")
        let ja = try JSONSerialization.jsonObject(with: Data(a.utf8)) as! [String: Any]
        XCTAssertEqual(ja["type"] as? String, "inbox_push_many")
        XCTAssertEqual(ja["ids"] as? [String], ["inbox_1", "inbox_2"])
        let b = SyncProtocol.cancelReceive(ids: ["cxl_1"], received: true, room: "R", requestId: "q2")
        let jb = try JSONSerialization.jsonObject(with: Data(b.utf8)) as! [String: Any]
        XCTAssertEqual(jb["type"] as? String, "cancel_receive")
        XCTAssertEqual(jb["received"] as? Bool, true)
    }

    func testAssemblerOutOfOrderAndStale() {
        let a = SnapshotAssembler()
        let half = fixture.count / 2
        let s = Array(fixture)
        let p1 = String(s[0..<half]), p2 = String(s[half...])
        XCTAssertNil(a.add(SnapshotChunk(snapshotId: "snap_2", idx: 1, total: 2, chunk: p2)))
        XCTAssertNil(a.add(SnapshotChunk(snapshotId: "snap_1", idx: 0, total: 1, chunk: "xxx")), "bản cũ bị bỏ")
        let done = a.add(SnapshotChunk(snapshotId: "snap_2", idx: 0, total: 2, chunk: p1))
        XCTAssertEqual(done?.rows.count, 2)
    }

    func testParseSnapshotChunkMessage() {
        let m = "{\"type\":\"snapshot_chunk\",\"room\":\"R\",\"snapshotId\":\"snap_1\",\"idx\":0,\"total\":3,\"chunk\":\"abc\"}"
        XCTAssertEqual(SyncProtocol.parseMessage(m, room: "R"), .snapshotChunk(SnapshotChunk(snapshotId: "snap_1", idx: 0, total: 3, chunk: "abc")))
    }

    func testDeadlineText() {
        let now = 1_000_000_000_000
        XCTAssertEqual(Deadline.text(due: now + 30 * 3_600_000, now: now), "Còn 1 ngày 6 giờ")
        XCTAssertEqual(Deadline.text(due: now - (72 + 30) * 3_600_000, now: now), "Quá hạn 4 ngày 6 giờ")
        XCTAssertEqual(Deadline.text(due: now - 600_000, now: now), "Đã đến hạn")
        XCTAssertEqual(Deadline.text(due: 0, now: now), "Chưa có hạn")
        XCTAssertEqual(Deadline.level(due: now + 3_600_000, now: now), .soon)
        XCTAssertEqual(Deadline.level(due: now + 3 * 86_400_000, now: now), .normal)
        XCTAssertEqual(Deadline.level(due: now - 1, now: now), .overdue)
        XCTAssertEqual(Deadline.level(due: 0, now: now), .unknown)
    }

    func testNotificationPlanner() {
        var cal = Calendar(identifier: .gregorian); cal.timeZone = TimeZone(secondsFromGMT: 0)!
        let now = Date(timeIntervalSince1970: 1_000_000_000)      // 2001-09-09 01:46:40 UTC
        let nowMs = 1_000_000_000_000
        func row(_ c: String, due: Int) -> OrderRow { OrderRow(k: "m", c: c, o: "", p: "", n: "Áo", co: "Đen", sz: "L", q: 1, rd: "", due: due, st: "") }
        let rows = [row("A", due: nowMs - 3_600_000 * 5), row("B", due: nowMs + 3_600_000 * 20), row("C", due: nowMs + 3_600_000 * 5), row("D", due: 0)]
        let plan = NotificationPlanner.plan(rows: rows, now: now, calendar: cal)
        // B: cảnh báo trước 12h (còn 20h -> 8h nữa); C: chưa kịp cảnh báo 12h nên báo lúc đến hạn; A quá hạn, D không có hạn
        XCTAssertTrue(plan.contains { $0.id == "warn-m|B" })
        XCTAssertTrue(plan.contains { $0.id == "due-m|C" })
        XCTAssertFalse(plan.contains { $0.id.hasSuffix("m|A") || $0.id.hasSuffix("m|D") })
        let summary = plan.first { $0.id == "summary-1" }
        XCTAssertEqual(summary?.body, "3 đơn quá hạn, 0 đơn sắp đến hạn")
        XCTAssertLessThanOrEqual(plan.count, 57)
    }
}

final class LenientSnapshotTests: XCTestCase {
    /// PC thật có thể gửi số thay chuỗi, null, hoặc số lẻ; danh sách vẫn phải đọc được.
    func testDecodesLooseTypesFromPc() throws {
        let json = """
        {"time":1700000000000,"count":2,"rows":[
          {"k":"m","c":"SPX123","o":123456789,"p":"Shopee","n":"Áo","co":"Đen","sz":38,"q":2.0,"rd":45000,"due":null,"st":"Chưa nhận"},
          {"k":"c","c":"GHN9","o":"","p":"","n":"","co":"","sz":"","q":"3","rd":"2026-11-10","due":1700000500000.0,"st":"Đơn hủy"}
        ]}
        """
        let p = try JSONDecoder().decode(SnapshotPayload.self, from: Data(json.utf8))
        XCTAssertEqual(p.rows.count, 2)
        XCTAssertEqual(p.rows[0].o, "123456789")
        XCTAssertEqual(p.rows[0].sz, "38")
        XCTAssertEqual(p.rows[0].q, 2)
        XCTAssertEqual(p.rows[0].rd, "45000")
        XCTAssertEqual(p.rows[0].due, 0)
        XCTAssertEqual(p.rows[1].q, 3)
        XCTAssertEqual(p.rows[1].due, 1700000500000)
    }

    func testMissingFieldsDefaultInsteadOfFailing() throws {
        let json = #"{"time":5,"rows":[{"c":"ABC123"}]}"#
        let p = try JSONDecoder().decode(SnapshotPayload.self, from: Data(json.utf8))
        XCTAssertEqual(p.rows.first?.c, "ABC123")
        XCTAssertEqual(p.rows.first?.q, 1)
        XCTAssertEqual(p.count, 1)
    }

    func testAssemblerReportsProgressAndError() {
        let a = SnapshotAssembler()
        XCTAssertNil(a.add(SnapshotChunk(snapshotId: "s1", idx: 0, total: 2, chunk: "AAAA")))
        XCTAssertEqual(a.partsReceived, 1)
        XCTAssertEqual(a.partsTotal, 2)
        XCTAssertNil(a.add(SnapshotChunk(snapshotId: "s1", idx: 1, total: 2, chunk: "BBBB")))
        XCTAssertNotNil(a.lastError, "dữ liệu rác phải báo lỗi giải mã, không im lặng")
    }
}

final class OrderRowV2Tests: XCTestCase {
    func testNewFieldsDecodeAndOldPcDefaultsToPending() throws {
        let json = """
        {"time":1,"count":2,"rows":[
          {"k":"a","c":"A1","o":"O1","ty":"Đơn hủy","pre":1,"pd":0,"rv":1,"due":0},
          {"k":"m","c":"B2"}
        ]}
        """
        let p = try JSONDecoder().decode(SnapshotPayload.self, from: Data(json.utf8))
        let a = p.rows[0], b = p.rows[1]
        XCTAssertTrue(a.isArchived); XCTAssertTrue(a.isCancelled); XCTAssertTrue(a.pre); XCTAssertFalse(a.pd); XCTAssertTrue(a.rv)
        XCTAssertTrue(b.pd, "PC bản cũ không có pd thì coi là đơn đang chờ")
        XCTAssertFalse(b.rv); XCTAssertFalse(b.pre); XCTAssertFalse(b.isCancelled)
    }

    func testEncodeThenDecodeKeepsFlags() throws {
        var r = OrderRow(k: "c", c: "X", o: "", p: "", n: "", co: "", sz: "", q: 1, rd: "", due: 0, st: "")
        r.pd = false; r.rv = true; r.pre = true; r.ty = "THẤT THU"
        let data = try JSONEncoder().encode(SnapshotPayload(time: 1, count: 1, rows: [r]))
        let back = try JSONDecoder().decode(SnapshotPayload.self, from: data)
        XCTAssertEqual(back.rows.first, r)
    }
}

final class WatchlistFieldTests: XCTestCase {
    func testWatchlistFieldsDecode() throws {
        let json = #"{"time":1,"rows":[{"k":"m","c":"W1","wl":1,"wr":"Khiếu nại đã gửi","nt":"Gọi shop"},{"k":"m","c":"W2"}]}"#
        let p = try JSONDecoder().decode(SnapshotPayload.self, from: Data(json.utf8))
        XCTAssertTrue(p.rows[0].wl); XCTAssertEqual(p.rows[0].wr, "Khiếu nại đã gửi"); XCTAssertEqual(p.rows[0].nt, "Gọi shop")
        XCTAssertFalse(p.rows[1].wl); XCTAssertEqual(p.rows[1].wr, "")
    }
}

final class StatsFieldTests: XCTestCase {
    func testStatsDecodeAndDefaultEmpty() throws {
        let with = #"{"time":1,"rows":[],"stats":{"m_total":10,"dropped":2}}"#
        let without = #"{"time":1,"rows":[]}"#
        let a = try JSONDecoder().decode(SnapshotPayload.self, from: Data(with.utf8))
        let b = try JSONDecoder().decode(SnapshotPayload.self, from: Data(without.utf8))
        XCTAssertEqual(a.stats["m_total"], 10); XCTAssertEqual(a.stats["dropped"], 2)
        XCTAssertTrue(b.stats.isEmpty)
    }
}
