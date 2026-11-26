import XCTest
@testable import ScannerCore

/// Nạp Salework từ điện thoại (26/11/2026): gửi kiện đã chuyển sẵn lên cloud theo lô. Dữ liệu GIẢ.
final class CloudImportUploadTests: XCTestCase {
    private final class Transport: CloudHTTPTransport {
        var bodies: [[String: Any]] = []
        var paths: [String] = []
        var failAt: Int? = nil
        func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
            paths.append(request.url?.path ?? "")
            if let b = request.httpBody, let o = (try? JSONSerialization.jsonObject(with: b)) as? [String: Any] { bodies.append(o) }
            let n = bodies.count
            let body: String
            let code: Int
            if let f = failAt, n == f {
                code = 503; body = #"{"error":{"code":"unavailable","message":"Cloud tạm lỗi"}}"#
            } else {
                let sent = (bodies.last?["parcels"] as? [Any])?.count ?? 0
                code = 200; body = #"{"added":\#(sent),"updated":0,"superseded":\#(sent == 0 ? 1 : 0),"ambiguous":[],"written":\#(sent)}"#
            }
            return (Data(body.utf8), HTTPURLResponse(url: request.url!, statusCode: code, httpVersion: nil, headerFields: nil)!)
        }
    }

    private func parcels(_ n: Int) -> [[String: Any]] { (0..<n).map { ["orderId": "O\($0)", "track": "T\($0)"] } }

    func testSendsParcelsInBatchesOf100ThenOrderIdsLast() async throws {
        let t = Transport()
        let api = try CloudAPI(baseURL: "https://a.workers.dev", token: "tok", transport: t)
        var steps: [Int] = []
        let r = try await CloudImportUpload.salework(api: api, parcels: parcels(250), orderIds: (0..<620).map { "O\($0)" }) { done, _ in steps.append(done) }
        // 3 lô kiện (100, 100, 50) rồi 2 lô mã đơn (500, 120) để thay dòng chờ cũ
        XCTAssertEqual(t.bodies.map { ($0["parcels"] as? [Any])?.count ?? -1 }, [100, 100, 50, 0, 0])
        XCTAssertEqual(t.bodies.map { ($0["orderIds"] as? [Any])?.count ?? -1 }, [0, 0, 0, 500, 120])
        XCTAssertTrue(t.bodies.allSatisfy { $0["source"] as? String == "salework" })
        XCTAssertTrue(t.paths.allSatisfy { $0.hasSuffix("/import/mapped") })
        XCTAssertEqual(r.added, 250)
        XCTAssertEqual(r.superseded, 2)
        XCTAssertEqual(steps.last, 250)
    }

    func testStopsAtTheFirstFailedBatchAndReportsHowFarItGot() async throws {
        let t = Transport()
        t.failAt = 2
        let api = try CloudAPI(baseURL: "https://a.workers.dev", token: "tok", transport: t)
        do {
            _ = try await CloudImportUpload.salework(api: api, parcels: parcels(250), orderIds: ["O1"]) { _, _ in }
            XCTFail("phải báo lỗi")
        } catch let e as CloudImportUpload.Failure {
            XCTAssertEqual(e.sentParcels, 100)          // lô 1 đã lên; nạp lại cả file sau cũng an toàn (cập nhật, không nhân đôi)
            XCTAssertTrue(e.message.contains("Cloud tạm lỗi"))
        }
        XCTAssertEqual(t.bodies.count, 2)
    }
}
