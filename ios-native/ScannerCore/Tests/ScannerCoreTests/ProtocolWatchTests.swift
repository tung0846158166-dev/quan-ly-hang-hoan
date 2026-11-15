import XCTest
@testable import ScannerCore

final class ProtocolWatchTests: XCTestCase {
    func testWatchSyncParses() {
        let msg = #"{"type":"watch_sync","room":"SHP-2026","time":123,"items":[{"c":"T1","o":"O1","r":"ghi chú"},{"c":"T2","o":"","r":""}]}"#
        guard case .watchSync(let t, let items)? = SyncProtocol.parseMessage(msg, room: "SHP-2026") else { return XCTFail("không đọc được watch_sync") }
        XCTAssertEqual(t, 123)
        XCTAssertEqual(items, [WatchItem(c: "T1", o: "O1", r: "ghi chú"), WatchItem(c: "T2", o: "", r: "")])
    }

    func testOrderNoteCarriesWatchFlag() {
        let json = SyncProtocol.orderNote(code: "T1", orderId: "O1", note: "x", watch: false, room: "SHP-2026", requestId: "n1")
        let d = (try? JSONSerialization.jsonObject(with: Data(json.utf8))) as? [String: Any]
        XCTAssertEqual(d?["watch"] as? Bool, false)
        XCTAssertEqual(d?["note"] as? String, "x")
    }
}
