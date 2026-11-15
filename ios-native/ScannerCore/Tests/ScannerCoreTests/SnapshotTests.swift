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
