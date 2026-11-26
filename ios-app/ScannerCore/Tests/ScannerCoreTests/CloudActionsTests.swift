import XCTest
@testable import ScannerCore

/// T042: logic để app iPhone chạy theo luồng cloud (thao tác sửa, hiển thị kết quả quét, trạng thái, mã QR ghép). Dữ liệu GIẢ.
final class CloudActionsTests: XCTestCase {
    private final class Transport: CloudHTTPTransport {
        var requests: [URLRequest] = []
        var bodies: [[String: Any]] = []
        let reply: (URLRequest) throws -> (Int, String)
        init(_ reply: @escaping (URLRequest) throws -> (Int, String)) { self.reply = reply }
        func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
            requests.append(request)
            if let b = request.httpBody, let o = (try? JSONSerialization.jsonObject(with: b)) as? [String: Any] { bodies.append(o) }
            let (code, body) = try reply(request)
            return (Data(body.utf8), HTTPURLResponse(url: request.url!, statusCode: code, httpVersion: nil, headerFields: nil)!)
        }
    }

    private func result(_ o: CloudScanResult.Outcome, message: String = "", by: String? = nil, at: String? = nil) -> CloudScanResult {
        CloudScanResult(outcome: o, ack: o == .received, message: message, parcel: nil, receivedBy: by, receivedAt: at)
    }

    // MARK: thao tác ghi

    func testOpsBuildTheCloudOperations() {
        XCTAssertEqual(CloudOps.setStatus(id: "p1", status: "Hàng đã trả")["type"] as? String, "setStatus")
        let w = CloudOps.setWatch(id: "p1", on: true, reason: "khách khiếu nại")
        XCTAssertEqual(w["on"] as? Bool, true); XCTAssertEqual(w["reason"] as? String, "khách khiếu nại")
        XCTAssertEqual(CloudOps.delete(id: "p9")["id"] as? String, "p9")
        XCTAssertEqual(CloudOps.cancelReceive(id: "c1", received: true)["status"] as? String, "Hàng đã trả")
        XCTAssertEqual(CloudOps.cancelReceive(id: "c1", received: false)["status"] as? String, "Chưa trả")
    }

    func testPerformSendsOneOpWithAnIdAndRefreshesOnlyOnSuccess() async throws {
        let t = Transport { _ in (200, #"{"results":[{"opId":"x","ok":true,"rev":5,"result":{}}]}"#) }
        let api = try CloudAPI(baseURL: "https://a.workers.dev", token: "tok", transport: t)
        var refreshed = 0
        let ok = await CloudSyncClient.perform(CloudOps.setStatus(id: "p1", status: "Hàng đã trả"), api: api, refresh: { refreshed += 1 })
        XCTAssertNil(ok)
        XCTAssertEqual(refreshed, 1)
        let op = (t.bodies.first?["ops"] as? [[String: Any]])?.first
        XCTAssertEqual(op?["type"] as? String, "setStatus")
        XCTAssertFalse((op?["opId"] as? String ?? "").isEmpty)

        let bad = Transport { _ in (200, #"{"results":[{"opId":"x","ok":false,"error":{"code":"conflict","message":"Trạng thái đã đổi"}}]}"#) }
        let api2 = try CloudAPI(baseURL: "https://a.workers.dev", token: "tok", transport: bad)
        let msg = await CloudSyncClient.perform(CloudOps.delete(id: "p1"), api: api2, refresh: { refreshed += 1 })
        XCTAssertEqual(msg, "Trạng thái đã đổi")
        XCTAssertEqual(refreshed, 1)
    }

    func testMutateIsRefusedWhenNotConnectedWithoutTouchingTheNetwork() async {
        let t = Transport { _ in XCTFail("không được gọi mạng"); return (200, "{}") }
        let client = CloudSyncClient(store: CloudParcelStore(), sessions: InMemoryCloudSessionStore(), transport: t)
        let msg = await client.mutate(CloudOps.setStatus(id: "p1", status: "Hàng đã trả"))
        XCTAssertTrue(msg?.contains("Mất kết nối") ?? false)
        XCTAssertTrue(t.requests.isEmpty)
    }

    // MARK: kết quả quét

    func testScanOutcomesMapToStatusesAndOnlyReceivedIsAConfirmedSuccess() {
        XCTAssertEqual(CloudScanPresentation.map(result(.received)).status, .success)
        let dup = CloudScanPresentation.map(result(.alreadyReceived, by: "Điện thoại A", at: "10:05"))
        XCTAssertEqual(dup.status, .duplicate)
        XCTAssertTrue(dup.text.contains("Điện thoại A"))
        let amb = CloudScanPresentation.map(result(.ambiguous, message: "2 kiện cùng khớp mã này"))
        XCTAssertEqual(amb.status, .ambiguous); XCTAssertTrue(amb.reviewRequired); XCTAssertEqual(amb.text, "2 kiện cùng khớp mã này")
        XCTAssertEqual(CloudScanPresentation.map(result(.notFound)).status, .notFound)
        let err = CloudScanPresentation.map(result(.error, message: "Mất kết nối cloud giữa chừng"))
        XCTAssertEqual(err.status, .error)
        XCTAssertFalse(err.confirmed)                              // không chắc cloud đã nhận: không xác nhận
        XCTAssertEqual(err.tone, .danger)
    }

    // MARK: trạng thái + khóa

    func testStatusLabelsTonesAndLockReasons() {
        XCTAssertEqual(CloudStatusText.label(.disconnected), "MẤT KẾT NỐI")
        XCTAssertEqual(CloudStatusText.tone(.disconnected), .danger)
        XCTAssertEqual(CloudStatusText.tone(.connected), .success)
        XCTAssertNil(CloudStatusText.lockReason(.connected))
        XCTAssertNil(CloudStatusText.lockReason(.syncing))
        for s in [CloudState.connecting, .disconnected, .unpaired, .revoked] { XCTAssertNotNil(CloudStatusText.lockReason(s), s.rawValue) }
    }

    func testQuotaNotes() {
        func q(_ level: String, _ used: Int) -> CloudQuota {
            CloudQuota(day: "2026-11-26", requestsUsed: 0, requestsLimit: 100_000, rowsWrittenUsed: used, rowsWrittenLimit: 100_000,
                       rowsReadUsed: 0, rowsReadLimit: 5_000_000, level: level)
        }
        XCTAssertNil(CloudStatusText.quotaNote(nil))
        XCTAssertNil(CloudStatusText.quotaNote(q("ok", 1000)))
        XCTAssertEqual(CloudStatusText.quotaNote(q("warn", 55_000)), "Sắp hết hạn mức hôm nay (55% lượt ghi).")
        XCTAssertTrue(CloudStatusText.quotaNote(q("stop", 80_000))?.contains("07:00") ?? false)
    }

    // MARK: ảnh trên cloud (xem ảnh máy khác chụp)

    func testParcelPhotosListParsesAndSkipsBadEntries() async throws {
        let t = Transport { r in
            XCTAssertTrue(r.url!.path.hasSuffix("/parcels/p1/photos"))
            return (200, #"[{"photoId":"photo_a","takenAt":1764000000000,"deviceName":"Điện thoại A","expired":false},{"photoId":""},{"takenAt":5}]"#)
        }
        let api = try CloudAPI(baseURL: "https://a.workers.dev", token: "tok", transport: t)
        let list = try await api.parcelPhotos(parcelId: "p1")
        XCTAssertEqual(list, [CloudPhotoInfo(photoId: "photo_a", takenAt: 1_764_000_000_000, deviceName: "Điện thoại A", expired: false)])
    }

    func testPhotoFilesDownloadOnceThenReadFromDisk() async throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("qlhh-cloudphoto-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: dir) }
        let t = Transport { _ in (200, "JPEGDATA") }
        let api = try CloudAPI(baseURL: "https://a.workers.dev", token: "tok", transport: t)
        let u1 = try await CloudPhotoFiles.fetch(api: api, dir: dir, photoId: "photo_a", thumb: true)
        let u2 = try await CloudPhotoFiles.fetch(api: api, dir: dir, photoId: "photo_a", thumb: true)
        XCTAssertEqual(u1, u2)
        XCTAssertEqual(t.requests.count, 1)
        XCTAssertEqual(try Data(contentsOf: u1), Data("JPEGDATA".utf8))
        XCTAssertNotEqual(CloudPhotoFiles.url(dir: dir, photoId: "photo_a", thumb: false), u1)
        XCTAssertFalse(CloudPhotoFiles.url(dir: dir, photoId: "../x", thumb: false).lastPathComponent.contains("/"))
        XCTAssertEqual(CloudPhotoFiles.prune(dir: dir, olderThanDays: 90), 0)
        XCTAssertEqual(CloudPhotoFiles.prune(dir: dir, olderThanDays: 90, now: Date().addingTimeInterval(100 * 86_400)), 1)
    }

    // MARK: AI qua cổng cloud (khóa chỉ nằm trên cloud)

    func testAIGoesThroughTheCloudGatewayWithTheDeviceToken() async throws {
        let t = Transport { r in
            XCTAssertTrue(r.url!.path.hasSuffix("/v1/ai/status"))
            XCTAssertEqual(r.value(forHTTPHeaderField: "Authorization"), "Bearer tok")
            return (200, #"{"groq":false,"gemini":true,"nvidia":true,"cloudflare":true,"usedToday":3,"dailyLimit":3000}"#)
        }
        let api = try CloudAPI(baseURL: "https://a.workers.dev", token: "tok", transport: t)
        let st = try await api.aiStatus()
        XCTAssertFalse(st.groq); XCTAssertTrue(st.gemini); XCTAssertTrue(st.nvidia); XCTAssertTrue(st.cloudflare)
        let eps = api.aiEndpoints(st, groqModels: "m1", geminiModels: "g1, g2", nvidiaModels: "n1", cloudflareModels: "c1")
        XCTAssertEqual(eps.map { $0.label }, ["gemini/g1", "gemini/g2", "nvidia/n1", "cloudflare/c1"])   // Google -> NVIDIA -> Cloudflare
        XCTAssertEqual(eps[2].baseURL, "https://a.workers.dev/v1/ai/nvidia")
        XCTAssertEqual(eps[3].baseURL, "https://a.workers.dev/v1/ai/cloudflare")
        XCTAssertTrue(eps.allSatisfy { $0.apiKey == "tok" })
        XCTAssertTrue(try CloudAPI(baseURL: "https://a.workers.dev", token: nil).aiEndpoints(st, groqModels: "m", geminiModels: "g").isEmpty)
    }

    func testRetiredGeminiModelsAreDropped() {
        XCTAssertEqual(AIEndpoint.cleanGeminiModels("gemini-2.5-flash, gemini-4-hydrogen, gemini-3.5-flash-lite"), "gemini-4-hydrogen, gemini-3.5-flash-lite")
        XCTAssertEqual(AIEndpoint.cleanGeminiModels("gemini-2.5-flash, gemini-2.5-flash-lite"), AIEndpoint.defaultGeminiModels)
        XCTAssertFalse(AIEndpoint.defaultGeminiModels.contains("gemini-2."))
    }

    // MARK: mã QR ghép

    func testPairLinkParsesAndRoundTrips() {
        let s = CloudPairLink.make(url: "https://qlhh.example.workers.dev", code: "123456")
        let p = CloudPairLink.parse(s)
        XCTAssertEqual(p?.url, "https://qlhh.example.workers.dev")
        XCTAssertEqual(p?.code, "123456")
        XCTAssertNil(CloudPairLink.parse("qlhh://pair?u=https://a.workers.dev"))              // thiếu mã
        XCTAssertNil(CloudPairLink.parse("qlhh://pair?u=https://a.workers.dev&c=12ab56"))     // mã không phải số
        XCTAssertNil(CloudPairLink.parse("https://pair?u=https://a.workers.dev&c=123456"))    // sai scheme
        XCTAssertNil(CloudPairLink.parse("qlhh://pair?u=http://khong-an-toan.com&c=123456"))  // không https
        XCTAssertNil(CloudPairLink.parse("SPXVN000000101"))                                    // mã vận đơn thường
    }
}
