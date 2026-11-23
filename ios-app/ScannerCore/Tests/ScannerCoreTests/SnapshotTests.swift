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

    func testReturnWatchFields() throws {
        let json = #"{"time":1,"count":3,"rows":[{"k":"m","c":"W1","rw":1795000000000,"rws":3,"ar":1794990000000},{"k":"m","c":"W2","rw":1795000000001,"rws":1,"rv":true},{"k":"m","c":"N1"}]}"#
        let p = try JSONDecoder().decode(SnapshotPayload.self, from: Data(json.utf8))
        XCTAssertEqual(p.rows.map { $0.inReturnWatch }, [true, true, false])
        XCTAssertEqual(p.rows[0].returnWatchStage, 3)
        XCTAssertEqual(p.rows[0].ar, 1794990000000)
        XCTAssertEqual(p.rows[1].returnWatchStage, 4)   // điện thoại đã quét nhận thì là chặng 4
        XCTAssertEqual(p.rows[2].rw, 0)                 // PC cũ không gửi
        let again = try JSONDecoder().decode(SnapshotPayload.self, from: JSONEncoder().encode(p))
        XCTAssertEqual(again.rows[0].rws, 3)
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

final class ExtraCodesTests: XCTestCase {
    func testExtraCodesDecodeAndLookup() throws {
        let json = #"{"time":1,"rows":[{"k":"m","c":"SPXVN06229297911A","o":"26102054X4PVRC","x":"SPXVN06654167505B 2611060KRRWN0U0"},{"k":"m","c":"B2"}]}"#
        let p = try JSONDecoder().decode(SnapshotPayload.self, from: Data(json.utf8))
        XCTAssertEqual(p.rows[0].lookupCodes, ["SPXVN06229297911A", "26102054X4PVRC", "SPXVN06654167505B", "2611060KRRWN0U0"])
        XCTAssertEqual(p.rows[1].x, "")
        XCTAssertEqual(p.rows[1].lookupCodes, ["B2"])
        let back = try JSONDecoder().decode(SnapshotPayload.self, from: JSONEncoder().encode(p))
        XCTAssertEqual(back.rows, p.rows)
    }
}


final class DeltaSyncTests: XCTestCase {
    // Dữ liệu do chính hàm của PC (buildPhoneSnapshotRows / buildPhoneDelta) tạo ra: O1 nhận, xoá O3, thêm O5, O2 thêm mã hoàn, O4 có 2 dòng trùng.
    let baseJSON = ##"{"time":1,"sid":"snap_100","count":5,"rows":[{"k":"m","c":"T4","o":"O4","p":"Shopee","n":"Áo O4","co":"","sz":"","q":1,"rd":"16/11/2026 10:00","due":1795057200000,"st":"Chưa trả","ty":"Trả hàng/Hoàn tiền","lv":1},{"k":"m","c":"T4","o":"O4","p":"Shopee","n":"Áo O4","co":"","sz":"","q":1,"rd":"16/11/2026 10:00","due":1795057200000,"st":"Chưa trả","ty":"Trả hàng/Hoàn tiền","lv":1},{"k":"m","c":"T1","o":"O1","p":"Shopee","n":"Áo O1","co":"","sz":"","q":1,"rd":"17/11/2026 10:00","due":1795143600000,"st":"Chưa trả","ty":"Trả hàng/Hoàn tiền","lv":0},{"k":"m","c":"T2","o":"O2","p":"Shopee","n":"Áo O2","co":"","sz":"","q":1,"rd":"18/11/2026 10:00","due":1795230000000,"st":"Chưa trả","ty":"Trả hàng/Hoàn tiền","lv":0},{"k":"m","c":"T3","o":"O3","p":"Shopee","n":"Áo O3","co":"","sz":"","q":1,"rd":"","due":0,"st":"Hàng đã trả","ty":"Trả hàng/Hoàn tiền","lv":0,"pd":0,"rv":1}]}"##
    let deltaJSON = ##"{"time":2,"base":"snap_100","seq":1,"count":5,"up":[{"_k":"m|T2|O2|","k":"m","c":"T2","o":"O2","p":"Shopee","n":"Áo O2","co":"","sz":"","q":1,"rd":"18/11/2026 10:00","due":1795230000000,"st":"Chưa trả","ty":"Trả hàng/Hoàn tiền","x":"R2","lv":0},{"_k":"m|T5|O5|","k":"m","c":"T5","o":"O5","p":"Shopee","n":"Áo O5","co":"","sz":"","q":1,"rd":"19/11/2026 09:00","due":1795312800000,"st":"Chưa trả","ty":"Trả hàng/Hoàn tiền","lv":0},{"_k":"m|T1|O1|","k":"m","c":"T1","o":"O1","p":"Shopee","n":"Áo O1","co":"","sz":"","q":1,"rd":"17/11/2026 10:00","due":0,"st":"Hàng đã trả","ty":"Trả hàng/Hoàn tiền","rcd":"19/11/2026 02:00:00","lv":0,"pd":0,"rv":1}],"rm":["m|T3|O3|"],"stats":{"m_total":5},"inbox":[]}"##

    func testDeltaFromPcAppliesToSameRows() throws {
        let base = try JSONDecoder().decode(SnapshotPayload.self, from: Data(baseJSON.utf8))
        XCTAssertEqual(base.sid, "snap_100")
        XCTAssertEqual(SnapshotDelta.keys(base.rows), ["m|T4|O4|", "m|T4|O4|#2", "m|T1|O1|", "m|T2|O2|", "m|T3|O3|"])
        let d = try JSONDecoder().decode(SnapshotDelta.self, from: Data(deltaJSON.utf8))
        XCTAssertEqual(d.base, "snap_100"); XCTAssertEqual(d.seq, 1)
        let rows = d.apply(to: base.rows)
        XCTAssertEqual(rows.map { "\($0.c)/\($0.o)/\($0.rv ? 1 : 0)/\($0.x)" }, ["T4/O4/0/", "T4/O4/0/", "T2/O2/0/R2", "T5/O5/0/", "T1/O1/1/"])
    }

    func testAssemblerKeepsInProgressWhenNewerStarts() {
        let a = SnapshotAssembler()
        XCTAssertNil(a.addRaw(SnapshotChunk(snapshotId: "snap_9", idx: 0, total: 2, chunk: "AA")))
        XCTAssertNil(a.addRaw(SnapshotChunk(snapshotId: "snap_10", idx: 0, total: 2, chunk: "BB")))
        XCTAssertEqual(a.addRaw(SnapshotChunk(snapshotId: "snap_9", idx: 1, total: 2, chunk: "CC")), "AACC")
        XCTAssertEqual(a.addRaw(SnapshotChunk(snapshotId: "snap_10", idx: 1, total: 2, chunk: "DD")), "BBDD")
        // lần cũ hơn lần đã xong thì bỏ
        XCTAssertNil(a.addRaw(SnapshotChunk(snapshotId: "snap_8", idx: 0, total: 1, chunk: "EE")))
    }

    func testOlderComparesNumbers() {
        XCTAssertTrue(SnapshotAssembler.older("snap_999", than: "snap_1000"))
        XCTAssertFalse(SnapshotAssembler.older("delta_1000", than: "delta_999"))
    }

    func testProtocolParsesDelta() {
        let m = #"{"type":"snapshot_delta","room":"R","deltaId":"delta_5","idx":0,"total":1,"chunk":"x"}"#
        XCTAssertEqual(SyncProtocol.parseMessage(m, room: "R"), .snapshotDelta(SnapshotChunk(snapshotId: "delta_5", idx: 0, total: 1, chunk: "x")))
    }
}
