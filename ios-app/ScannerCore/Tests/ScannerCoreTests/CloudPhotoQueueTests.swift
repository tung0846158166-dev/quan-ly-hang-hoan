import XCTest
@testable import ScannerCore

/// Chủ dự án 26/11/2026: ảnh quét lưu vào máy trước, đẩy lên cloud xong thì TỰ XÓA khỏi máy; mất mạng thì chờ, có mạng tự gửi lại.
/// Chỉ áp dụng cho ảnh của kiện cloud ĐÃ xác nhận (lượt quét vẫn cần mạng). Dữ liệu GIẢ.
final class CloudPhotoQueueTests: XCTestCase {
    private final class Transport: CloudHTTPTransport {
        var requests: [URLRequest] = []
        var reply: (URLRequest) throws -> (Int, String)
        init(_ reply: @escaping (URLRequest) throws -> (Int, String)) { self.reply = reply }
        func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
            requests.append(request)
            let (code, body) = try reply(request)
            return (Data(body.utf8), HTTPURLResponse(url: request.url!, statusCode: code, httpVersion: nil, headerFields: nil)!)
        }
    }

    private var dir: URL!
    override func setUp() { dir = FileManager.default.temporaryDirectory.appendingPathComponent("qlhh-pq-\(UUID().uuidString)") }
    override func tearDown() { try? FileManager.default.removeItem(at: dir) }

    private func files() -> [String] { ((try? FileManager.default.contentsOfDirectory(atPath: dir.path)) ?? []).filter { $0.hasSuffix(".jpg") } }

    func testUploadedPhotosAreDeletedFromThePhone() async throws {
        let t = Transport { _ in (200, #"{"ok":true}"#) }
        let api = try CloudAPI(baseURL: "https://a.workers.dev", token: "tok", transport: t)
        let q = CloudPhotoQueue(dir: dir)
        try q.enqueue(photoId: "p_A_1", parcelId: "pa", full: Data("FULL".utf8), thumb: Data("TH".utf8))
        XCTAssertEqual(q.pendingCount, 1)
        XCTAssertEqual(files().count, 2)                         // ảnh đầy đủ + ảnh nhỏ nằm trong máy trước
        let sent = await q.drain(api: api)
        XCTAssertEqual(sent, 1)
        XCTAssertEqual(q.pendingCount, 0)
        XCTAssertTrue(files().isEmpty)                           // lên cloud xong: xóa khỏi máy
        XCTAssertTrue(t.requests.contains { $0.url!.absoluteString.contains("/photos/p_A_1") && $0.url!.absoluteString.contains("parcelId=pa") })
    }

    func testOfflinePhotosStayAndAreSentLaterAcrossRestarts() async throws {
        var online = false
        let t = Transport { _ in
            if !online { throw URLError(.notConnectedToInternet) }
            return (200, #"{"ok":true}"#)
        }
        let api = try CloudAPI(baseURL: "https://a.workers.dev", token: "tok", transport: t)
        let q = CloudPhotoQueue(dir: dir)
        try q.enqueue(photoId: "p_B_1", parcelId: "pb", full: Data("F1".utf8), thumb: nil)
        try q.enqueue(photoId: "p_B_2", parcelId: "pb", full: Data("F2".utf8), thumb: nil)
        let none = await q.drain(api: api)
        XCTAssertEqual(none, 0)
        XCTAssertEqual(q.pendingCount, 2)                        // mất mạng: KHÔNG mất ảnh
        XCTAssertEqual(files().count, 2)

        let reopened = CloudPhotoQueue(dir: dir)                 // tắt/mở lại app
        XCTAssertEqual(reopened.pendingCount, 2)
        online = true
        let sent = await reopened.drain(api: api)
        XCTAssertEqual(sent, 2)
        XCTAssertEqual(reopened.pendingCount, 0)
        XCTAssertTrue(files().isEmpty)
    }

    func testPhotosTheCloudWillNeverAcceptAreDroppedNotRetriedForever() async throws {
        // kiện đã bị xóa / ảnh hỏng: cloud trả lỗi cố định -> bỏ ảnh (không kẹt hàng chờ, không nặng máy)
        let t = Transport { r in
            r.url!.absoluteString.contains("p_C_bad") ? (404, #"{"error":{"code":"not_found","message":"Không tìm thấy kiện."}}"#) : (200, #"{"ok":true}"#)
        }
        let api = try CloudAPI(baseURL: "https://a.workers.dev", token: "tok", transport: t)
        let q = CloudPhotoQueue(dir: dir)
        try q.enqueue(photoId: "p_C_bad", parcelId: "gone", full: Data("X".utf8), thumb: nil)
        try q.enqueue(photoId: "p_C_ok", parcelId: "pc", full: Data("Y".utf8), thumb: nil)
        _ = await q.drain(api: api)
        XCTAssertEqual(q.pendingCount, 0)
        XCTAssertTrue(files().isEmpty)
    }
}
