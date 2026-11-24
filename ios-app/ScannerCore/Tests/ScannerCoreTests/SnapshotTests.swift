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

    func testDecodeCancelledIdIgnoresLegacyInboxField() throws {
        // PC v2 vẫn gửi `"inbox": []` (và PC cũ có thể gửi mảng); bộ giải mã bỏ qua an toàn không cần InboxRow.
        let json = """
        {"time":1,"count":1,"rows":[{"k":"c","c":"SPX1","o":"O1","p":"Shopee","n":"","co":"","sz":"","q":1,"rd":"","due":0,"st":"Đơn hủy","ci":"cxl_1"}],
         "inbox":[{"id":"legacy_1","c":"SPX9"}]}
        """
        let p = try JSONDecoder().decode(SnapshotPayload.self, from: Data(json.utf8))
        XCTAssertEqual(p.rows[0].ci, "cxl_1")
        let again = try JSONDecoder().decode(SnapshotPayload.self, from: JSONEncoder().encode(p))
        XCTAssertEqual(again.rows[0].ci, "cxl_1")
    }

    func testPlatformStatusCancelAfterPrep() throws {
        let json = #"{"time":1,"count":3,"rows":[{"k":"m","c":"A1","ps":"Huỷ sau chuẩn bị · Đã huỷ"},{"k":"m","c":"A2","ps":"HỦY SAU CHUẨN BỊ"},{"k":"m","c":"A3","ps":"Đã đồng ý"}]}"#
        let p = try JSONDecoder().decode(SnapshotPayload.self, from: Data(json.utf8))
        XCTAssertEqual(p.rows.map { $0.isCancelAfterPrep }, [true, true, false])
        XCTAssertEqual(p.rows[0].ps, "Huỷ sau chuẩn bị · Đã huỷ")
    }

    /// Dòng đúng hình dạng PC v2 (PhoneSnapshot.cs) gửi: hoàn MỘT PHẦN có `rn` (tên sản phẩm đã hoàn), `n` vẫn đủ mọi sản phẩm.
    func testPcV2PartialReturnRowKeepsAllProductsAndMarksReturnedOnes() throws {
        let json = #"{"time":1,"sid":"snap_1","count":2,"rows":[{"k":"m","c":"TTVNFAKE00201","o":"5789FAKE000201","p":"TikTok Shop","n":"Quần Jeans | Sơ Mi | Nón","co":"Xanh | Trắng | Đen","sz":"31 | L | Free","q":3,"rd":"21/11/2026","due":1795000000000,"lv":2,"st":"Chưa trả","ty":"THẤT THOÁT","x":"JT88FAKE00201","rn":"Quần Jeans"},{"k":"m","c":"SPX2","o":"O2","p":"Shopee","n":"Áo","q":1,"st":"Chưa trả"}],"stats":{"m_total":2,"sent":2,"dropped":0},"inbox":[]}"#
        let p = try JSONDecoder().decode(SnapshotPayload.self, from: Data(json.utf8))
        XCTAssertEqual(p.rows[0].rn, "Quần Jeans")
        XCTAssertEqual(p.rows[0].n.components(separatedBy: " | ").count, 3)   // giữ đủ 3 sản phẩm
        XCTAssertEqual(p.rows[1].rn, "")                                       // không hoàn một phần: không phân biệt
        XCTAssertEqual(p.stats["dropped"], 0)
        let again = try JSONDecoder().decode(SnapshotPayload.self, from: JSONEncoder().encode(p))
        XCTAssertEqual(again.rows[0].rn, "Quần Jeans")
    }

    func testReturnWatchFields() throws {
        let json = #"{"time":1,"count":3,"rows":[{"k":"m","c":"W1","rw":1795000000000,"rws":3,"ar":1794990000000},{"k":"m","c":"W2","rw":1795000000001,"rws":1,"rv":true},{"k":"m","c":"N1"}]}"#
        let p = try JSONDecoder().decode(SnapshotPayload.self, from: Data(json.utf8))
        XCTAssertEqual(p.rows.map { $0.inReturnWatch }, [true, true, false])
        XCTAssertEqual(p.rows[0].returnWatchStage, 3)
        XCTAssertEqual(p.rows[0].returnWatchStageName, "Đã về shop")
        XCTAssertEqual(p.rows[0].ar, 1794990000000)
        XCTAssertEqual(p.rows[1].returnWatchStage, 4)   // điện thoại đã quét nhận thì là chặng 4
        XCTAssertEqual(p.rows[1].returnWatchStageName, "Đã nhận kho")
        XCTAssertEqual(p.rows[2].rw, 0)                 // PC cũ không gửi
        let again = try JSONDecoder().decode(SnapshotPayload.self, from: JSONEncoder().encode(p))
        XCTAssertEqual(again.rows[0].rws, 3)
    }

    func testCancelReceiveMessage() throws {
        let b = SyncProtocol.cancelReceive(ids: ["cxl_1"], received: true, room: "ROOM-TEST-2026", requestId: "q2")
        let jb = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(b.utf8)) as? [String: Any])
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

final class ReceivedTimeDisplayTests: XCTestCase {
    func testResolvedReceivedTimeCoversPcDateLocalScanAndUnreceived() throws {
        let vnCal = ProductMetrics.vietnamCalendar

        // 1. Chưa nhận -> luôn trả .notReceived (kể cả nếu còn rcd cũ sót lại)
        var pending = OrderRow(k: "m", c: "DEMO-R1", o: "DHM-R1", p: "Shopee", n: "Áo", co: "", sz: "", q: 1, rd: "20/11/2026", due: 0, st: "Chưa nhận")
        pending.rv = false
        pending.rcd = "21/11/2026 08:00:00"
        XCTAssertEqual(pending.resolvedReceivedTime(localScanTimeMs: 1_795_400_000_000, calendar: vnCal), .notReceived)

        // 2. Đã nhận có rcd đầy đủ giờ từ PC -> định dạng dd/MM/yyyy HH:mm
        var fromPc = pending
        fromPc.rv = true
        fromPc.rcd = "24/11/2026 14:35:20"
        XCTAssertEqual(fromPc.resolvedReceivedTime(calendar: vnCal), .received(text: "24/11/2026 14:35", fromLocalScan: false))

        // 3. Đã nhận có rcd chỉ gồm ngày -> định dạng dd/MM/yyyy
        var dateOnly = pending
        dateOnly.rv = true
        dateOnly.rcd = "2026-11-24"
        XCTAssertEqual(dateOnly.resolvedReceivedTime(calendar: vnCal), .received(text: "24/11/2026", fromLocalScan: false))

        // 4. Vừa quét nhận trên chính điện thoại (rcd trống) -> lấy giờ quét theo lịch Việt Nam
        var scannedLocal = pending
        scannedLocal.rv = true
        scannedLocal.rcd = ""
        var comps = DateComponents()
        comps.timeZone = vnCal.timeZone
        comps.year = 2026; comps.month = 11; comps.day = 24; comps.hour = 0; comps.minute = 30
        let scanDate = try XCTUnwrap(vnCal.date(from: comps))
        let scanMs = Int(scanDate.timeIntervalSince1970 * 1000)
        XCTAssertEqual(scannedLocal.resolvedReceivedTime(localScanTimeMs: scanMs, calendar: vnCal), .received(text: "24/11/2026 00:30", fromLocalScan: true))

        // 5. Đã nhận nhưng không có rcd và không có giờ quét cục bộ -> .receivedUnknownTime
        XCTAssertEqual(scannedLocal.resolvedReceivedTime(localScanTimeMs: nil, calendar: vnCal), .receivedUnknownTime)
    }
}

final class SnapshotAuditSafetyTests: XCTestCase {
    // Bản nén chuẩn từ SnapshotTests.fixture (2 dòng).
    let fixture = "Zc9BTsJAFMbxq0y+9ST0DZ3OdHbWsKuAOBKMcUFKk5LKDArEKGHhDbyCblyZuGClSwz34CamoU1MfOv3Xn7/NZbTWQ5DKpZBMxyZX7kljOC49w8LmOs1ShjMwJHB4KI/GnaDSGmthIoFaXB4GIiIiNqqr5PBuQzAMa+WCz/Pc3A4GOyePev7W8/S3DFb7LbVx+p0NHYF6+7eVuBYPMEgBcfdkTCBAckWUUsEImIBmVAZSeCYrP7bF0sYnBb7zzFzxeHrw2HDj/ys5ls77BKJWKpQKVXbpVYkRRyQjttaizBSYR1gp6X1Jas6/lb00h5LTpIr2+k0EcmltZ1BU3B2LKC6oOE2xJ+X/atjxeH7/RGbm80v"

    /// Lượt 1 có mảnh hỏng => giải mã lỗi NHƯNG KHÔNG khóa `lastDone` để lượt 2 của CÙNG `snapshotId` vẫn ghép thành công.
    func testAssemblerRecoversOnSecondPassAfterCorruptChunk() {
        let a = SnapshotAssembler()
        let half = fixture.count / 2
        let s = Array(fixture)
        let p1 = String(s[0..<half]), p2 = String(s[half...])

        // Lượt 1: mảnh 1 hỏng
        XCTAssertNil(a.add(SnapshotChunk(snapshotId: "snap_500", idx: 0, total: 2, chunk: p1)))
        XCTAssertNil(a.add(SnapshotChunk(snapshotId: "snap_500", idx: 1, total: 2, chunk: "!!!CORRUPT!!!")))
        XCTAssertNotNil(a.lastError)

        // Lượt 2 (PC gửi lặp cùng snap_500): phải ghép và giải mã thành công
        XCTAssertNil(a.add(SnapshotChunk(snapshotId: "snap_500", idx: 0, total: 2, chunk: p1)))
        let recovered = a.add(SnapshotChunk(snapshotId: "snap_500", idx: 1, total: 2, chunk: p2))
        XCTAssertEqual(recovered?.rows.count, 2)
        XCTAssertNil(a.lastError)
    }

    /// Mảnh sai `total` hoặc `total` vượt ngưỡng phải bị bỏ qua an toàn.
    func testAssemblerRejectsInconsistentOrHugeTotal() {
        let a = SnapshotAssembler()
        XCTAssertNil(a.addRaw(SnapshotChunk(snapshotId: "snap_huge", idx: 0, total: 999_999, chunk: "AA")))
        XCTAssertNil(a.addRaw(SnapshotChunk(snapshotId: "snap_1", idx: 0, total: 2, chunk: "AA")))
        XCTAssertNil(a.addRaw(SnapshotChunk(snapshotId: "snap_1", idx: 1, total: 3, chunk: "BB")))
        XCTAssertEqual(a.addRaw(SnapshotChunk(snapshotId: "snap_1", idx: 1, total: 2, chunk: "CC")), "AACC")
    }

    /// Quy tắc 1, 2, 3: 2 kiện anh em cùng mã đơn gốc `ORD1`:
    /// - Quét mã vận đơn riêng `T1` chỉ nhận kiện A, KHÔNG nhận kiện B.
    /// - Quét mã đơn gốc `ORD1` (khớp 2 kiện đang chờ => ambiguous) KHÔNG tự nhận kiện nào!
    func testLocalOverlayEnforcesSiblingAndAmbiguousRules() {
        let vnCal = ProductMetrics.vietnamCalendar
        var a = OrderRow(k: "m", c: "T1", o: "ORD1", p: "Shopee", n: "Áo A", co: "", sz: "", q: 1, rd: "20/11/2026", due: 0, st: "Chưa trả")
        a.pd = true; a.rv = false
        var b = OrderRow(k: "m", c: "T2", o: "ORD1", p: "Shopee", n: "Quần B", co: "", sz: "", q: 1, rd: "20/11/2026", due: 0, st: "Chưa trả")
        b.pd = true; b.rv = false
        let rows = [a, b]

        // Index phát hiện ORD1 là ambiguous, còn T1 và T2 là đơn nhất
        let idx = SnapshotIndex(rows: rows)
        XCTAssertTrue(idx.isAmbiguousPending("ORD1"))
        XCTAssertFalse(idx.isAmbiguousPending("T1"))
        XCTAssertFalse(idx.isAmbiguousPending("T2"))

        // Quét ORD1 (ambiguous): KHÔNG đánh dấu kiện nào
        let amb = LocalEditOverlay.apply(to: rows, unconfirmedReturnScanTimes: ["ORD1": 1_795_400_000_000], overlays: [], calendar: vnCal)
        XCTAssertFalse(amb.touched)
        XCTAssertFalse(amb.rows[0].rv)
        XCTAssertFalse(amb.rows[1].rv)

        // Quét T1: CHỈ đánh dấu kiện A, kiện B giữ nguyên chưa nhận
        let single = LocalEditOverlay.apply(to: rows, unconfirmedReturnScanTimes: ["T1": 1_795_400_000_000], overlays: [], calendar: vnCal)
        XCTAssertTrue(single.touched)
        XCTAssertTrue(single.rows[0].rv)
        XCTAssertFalse(single.rows[0].pd)
        XCTAssertFalse(single.rows[0].rcd.isEmpty)
        XCTAssertFalse(single.rows[1].rv)
        XCTAssertTrue(single.rows[1].pd)
    }

    /// Ghép lệnh sửa cục bộ chưa được PC xác nhận (note, editStatus, delete, cancelReceive) khi snapshot mới từ PC về.
    func testLocalOverlayPreservesPendingEditsAcrossFreshSnapshot() {
        let vnCal = ProductMetrics.vietnamCalendar
        var r1 = OrderRow(k: "m", c: "T1", o: "O1", p: "Shopee", n: "Áo", co: "", sz: "", q: 1, rd: "", due: 0, st: "Chưa trả")
        r1.pd = true; r1.rv = false
        var r2 = OrderRow(k: "m", c: "T2", o: "O2", p: "Shopee", n: "Quần", co: "", sz: "", q: 1, rd: "", due: 0, st: "Chưa trả")
        r2.pd = true; r2.rv = false
        var r3 = OrderRow(k: "m", c: "T3", o: "O3", p: "Shopee", n: "Nón", co: "", sz: "", q: 1, rd: "", due: 0, st: "Chưa trả")
        r3.pd = true; r3.rv = false
        var c1 = OrderRow(k: "c", c: "C1", o: "OC1", p: "Shopee", n: "Hủy", co: "", sz: "", q: 1, rd: "", due: 0, st: "Đơn hủy")
        c1.ci = "cxl_99"; c1.pd = true; c1.rv = false

        let overlays = [
            LocalEditOverlay(requestId: "n1", action: .note, rowId: r1.id, note: "Chờ kiểm tra", watch: true, timeMs: 100),
            LocalEditOverlay(requestId: "e1", action: .editStatus, rowId: r2.id, status: "Hàng đã trả", timeMs: 1_795_400_000_000),
            LocalEditOverlay(requestId: "d1", action: .delete, rowId: r3.id, timeMs: 200),
            LocalEditOverlay(requestId: "c1", action: .cancelReceive, cancelIds: ["cxl_99"], received: true, timeMs: 1_795_400_000_000)
        ]

        let res = LocalEditOverlay.apply(to: [r1, r2, r3, c1], unconfirmedReturnScanTimes: [:], overlays: overlays, calendar: vnCal)
        XCTAssertTrue(res.touched)
        XCTAssertEqual(res.rows.count, 3)   // r3 đã bị xóa cục bộ
        XCTAssertTrue(res.rows[0].wl)
        XCTAssertEqual(res.rows[0].wr, "Chờ kiểm tra")
        XCTAssertTrue(res.rows[1].rv)
        XCTAssertFalse(res.rows[1].pd)
        XCTAssertFalse(res.rows[1].rcd.isEmpty)
        XCTAssertTrue(res.rows[2].rv)
        XCTAssertEqual(res.rows[2].ci, "cxl_99")
    }

    /// 5.000 đơn: SnapshotIndex dựng chỉ mục nhẹ và tra cứu O(1) theo mã lẫn theo `id`.
    func testSnapshotIndexHandles5000RowsAndDistinctIds() {
        var rows: [OrderRow] = []
        rows.reserveCapacity(5000)
        for i in 0..<5000 {
            var r = OrderRow(k: "m", c: "SPXVN\(String(format: "%010d", i))", o: "ORD\(i)", p: "Shopee", n: "SP \(i)", co: "", sz: "", q: 1, rd: "20/11/2026", due: 0, st: "Chưa trả")
            r.pd = true
            rows.append(r)
        }
        let idx = SnapshotIndex(rows: rows)
        XCTAssertEqual(idx.lookup("SPXVN0000004999", in: rows)?.o, "ORD4999")
        XCTAssertEqual(idx.row(byId: rows[2500].id, in: rows)?.c, "SPXVN0000002500")

        // Hai đơn hủy cùng mã vận đơn nhưng khác `ci` có `id` khác nhau (không trùng khóa SwiftUI ForEach)
        var x1 = OrderRow(k: "c", c: "SAME", o: "O1", p: "Shopee", n: "", co: "", sz: "", q: 1, rd: "", due: 0, st: "Đơn hủy")
        x1.ci = "id_1"
        var x2 = OrderRow(k: "c", c: "SAME", o: "O2", p: "Shopee", n: "", co: "", sz: "", q: 1, rd: "", due: 0, st: "Đơn hủy")
        x2.ci = "id_2"
        XCTAssertNotEqual(x1.id, x2.id)
    }
}
