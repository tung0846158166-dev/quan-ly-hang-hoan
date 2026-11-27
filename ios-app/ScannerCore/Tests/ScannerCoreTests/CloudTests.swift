import XCTest
@testable import ScannerCore

/// Máy khách cloud trên iPhone: ánh xạ kiện -> dòng, bản sao cục bộ, API (máy chủ GIẢ), quét khi mất mạng. Dữ liệu GIẢ.
final class CloudTests: XCTestCase {
    private var cal: Calendar { CloudRowMapper.vietnamCalendar }
    private var now: Date { cal.date(from: DateComponents(year: 2026, month: 11, day: 25, hour: 14))! }

    private func parcel(_ json: String) -> CloudParcel {
        try! JSONDecoder().decode(CloudParcel.self, from: Data(json.utf8))
    }

    // MARK: giải mã kiện

    func testParcelDecodesTolerantlyAndKeepsWhitelistedExtraAsStrings() {
        let p = parcel(#"""
        {"id":"a","orderId":123456,"track":"TRK1","price":"150000","isWatchlist":1,"rev":7,
         "items":[{"product":"Áo","color":"Đen","size":"M","qty":2,"returnedQty":1}],
         "extra":{"returnSuccessTime":"24/11/2026 10:00","priceNeedsReview":true}}
        """#)
        XCTAssertEqual(p.orderId, "123456")       // số đổi sang chuỗi
        XCTAssertEqual(p.price, 150000)
        XCTAssertTrue(p.isWatchlist)
        XCTAssertEqual(p.rev, 7)
        XCTAssertEqual(p.items.first?.returnedQty, 1)
        XCTAssertEqual(p.extra["returnSuccessTime"], "24/11/2026 10:00")
        XCTAssertEqual(p.extra["priceNeedsReview"], "true")
        XCTAssertEqual(parcel(#"{"id":"b"}"#).status, "")     // thiếu trường: rỗng, không lỗi
    }

    // MARK: ánh xạ dòng điện thoại

    func testRowMapsPendingParcelWithAlertAndOtherCodes() {
        let p = parcel(#"""
        {"id":"a","orderId":"O1","returnOrderId":"RR1","track":"TRK1","returnTrack":"RT1","trackingCodes":["old-1"],"scanAliases":["al 1"],
         "forwardTrack":"SHARED","platform":"TikTok","orderType":"Trả hàng","status":"Chưa trả","carrier":"SPX","price":99000.4,
         "returnedAt":"25/11/2026 01:00:00","note":"Sai mô tả","userNote":"gọi lại","platformStatus":"Đã hoàn",
         "items":[{"product":"Áo","color":"Đen","size":"M","qty":2},{"product":"Quần","color":"","size":"L","qty":0}],"isWatchlist":true,"watchlistReason":"khiếu nại"}
        """#)
        let r = CloudRowMapper.row(p, now: now)!
        XCTAssertEqual(r.k, "m"); XCTAssertEqual(r.c, "TRK1"); XCTAssertEqual(r.o, "O1")
        // thao tác và ảnh qua cloud dùng MÃ KIỆN cloud, không phải khóa dòng hiển thị
        XCTAssertEqual(r.pid, "a"); XCTAssertEqual(r.cloudId, "a"); XCTAssertNotEqual(r.id, "a")
        XCTAssertEqual(r.p, "TikTok Shop")
        XCTAssertEqual(r.n, "Áo | Quần")
        XCTAssertEqual(r.co, "Đen | ")                 // giống C#: ghép cả ô trống
        XCTAssertEqual(r.q, 3)                         // 2 + (qty<=0 tính 1)
        XCTAssertEqual(r.lv, 1)                        // chờ 13 giờ: vàng
        XCTAssertTrue(r.pd); XCTAssertFalse(r.rv)
        XCTAssertGreaterThan(r.due, 0)
        XCTAssertEqual(r.pr, 99000)
        XCTAssertEqual(r.cr, "SPX")
        XCTAssertEqual(r.ps, "Đã hoàn")
        XCTAssertEqual(r.nt, "Sai mô tả | gọi lại")
        XCTAssertTrue(r.wl); XCTAssertEqual(r.wr, "khiếu nại")
        let codes = Set(r.x.split(separator: " ").map(String.init))
        XCTAssertEqual(codes, ["RT1", "RR1", "OLD-1", "AL1"])   // KHÔNG có mã giao đi dùng chung
        XCTAssertFalse(r.x.contains("SHARED"))
    }

    func testAlertLevelsAndReceivedParcels() {
        func lv(_ hoursAgo: Double, status: String = "Chưa trả") -> Int {
            let arrived = now.addingTimeInterval(-hoursAgo * 3600)
            let f = DateFormatter(); f.calendar = cal; f.timeZone = cal.timeZone; f.dateFormat = "dd/MM/yyyy HH:mm:ss"
            return CloudRowMapper.row(parcel("{\"id\":\"a\",\"track\":\"T\",\"status\":\"\(status)\",\"returnedAt\":\"\(f.string(from: arrived))\"}"), now: now)!.lv
        }
        XCTAssertEqual(lv(6), 0)
        XCTAssertEqual(lv(12), 1)
        XCTAssertEqual(lv(23), 1)
        XCTAssertEqual(lv(24), 2)
        XCTAssertEqual(lv(48, status: "Hàng đã trả"), 0)          // đã nhận: không cảnh báo
        let done = CloudRowMapper.row(parcel(#"{"id":"a","track":"T","status":"Hàng đã trả","recvDate":"25/11/2026 09:00:00"}"#), now: now)!
        XCTAssertTrue(done.rv); XCTAssertFalse(done.pd); XCTAssertEqual(done.rcd, "25/11/2026 09:00:00"); XCTAssertEqual(done.due, 0)
        let lost = CloudRowMapper.row(parcel(#"{"id":"a","track":"T","status":"Bị mất"}"#), now: now)!
        XCTAssertFalse(lost.pd); XCTAssertFalse(lost.rv)
    }

    func testSalewareTimeBeforeDateIsReversedAndExtraReturnSuccessTimeIsUsed() {
        let a = CloudRowMapper.arrivedAt(parcel(#"{"id":"a","returnedAt":"10:15:22 21/11/2026"}"#))!
        let comps = cal.dateComponents([.day, .month, .year, .hour, .minute], from: a)
        XCTAssertEqual([comps.day, comps.month, comps.year, comps.hour, comps.minute], [21, 11, 2026, 10, 15])
        XCTAssertNotNil(CloudRowMapper.arrivedAt(parcel(#"{"id":"a","extra":{"returnSuccessTime":"24/11/2026 10:00"}}"#)))
        XCTAssertNil(CloudRowMapper.arrivedAt(parcel(#"{"id":"a"}"#)))
    }

    func testKindsCancelledArchivedDeletedAndPartialReturn() {
        let c = CloudRowMapper.row(parcel(#"{"id":"c1","orderId":"O9","orderType":"Đơn hủy","status":"Chưa trả"}"#), now: now)!
        XCTAssertEqual(c.k, "c"); XCTAssertEqual(c.st, "Đơn hủy"); XCTAssertEqual(c.ci, "c1"); XCTAssertEqual(c.c, "O9")   // không có mã vận đơn: lấy mã đơn
        XCTAssertEqual(CloudRowMapper.row(parcel(#"{"id":"a","track":"T","isArchived":true}"#), now: now)!.k, "a")
        XCTAssertNil(CloudRowMapper.row(parcel(#"{"id":"a","track":"T","isDeleted":true}"#), now: now))
        XCTAssertNil(CloudRowMapper.row(parcel(#"{"id":"a"}"#), now: now))                 // không có mã nào
        let part = CloudRowMapper.row(parcel(#"{"id":"a","track":"T","items":[{"product":"Áo","qty":1,"returnedQty":1},{"product":"Quần","qty":1}]}"#), now: now)!
        XCTAssertEqual(part.rn, "Áo")
        let all = CloudRowMapper.row(parcel(#"{"id":"a","track":"T","items":[{"product":"Áo","qty":1,"returnedQty":1}]}"#), now: now)!
        XCTAssertEqual(all.rn, "")                                                        // hoàn hết: không phân biệt
    }

    func testRowsSortPendingFirstThenSoonestDeadlineAndSkipDeleted() {
        func p(_ id: String, _ status: String, _ at: String) -> CloudParcel {
            parcel("{\"id\":\"\(id)\",\"track\":\"\(id)\",\"status\":\"\(status)\",\"returnedAt\":\"\(at)\"}")
        }
        let rows = CloudRowMapper.rows([
            p("done", "Hàng đã trả", "25/11/2026 01:00:00"),
            p("late", "Chưa trả", "25/11/2026 12:00:00"),
            p("old", "Chưa trả", "24/11/2026 12:00:00"),
            parcel(#"{"id":"del","track":"del","isDeleted":true}"#),
            p("none", "Chưa trả", ""),
        ], now: now)
        XCTAssertEqual(rows.map { $0.c }, ["old", "late", "none", "done"])
    }

    // MARK: bản sao cục bộ

    func testStoreAppliesUpsertsKeepsRevAndPersistsAtomically() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("qlhh-cloud-" + UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("parcels.json")
        let s = CloudParcelStore(fileURL: url)
        s.apply([parcel(#"{"id":"a","track":"A","status":"Chưa trả"}"#), parcel(#"{"id":"b","track":"B"}"#)], rev: 5)
        XCTAssertEqual(s.rev, 5); XCTAssertEqual(s.count, 2)
        s.apply([parcel(#"{"id":"a","track":"A","status":"Hàng đã trả"}"#)], rev: 9)
        XCTAssertEqual(s.count, 2)                       // cập nhật, không nhân đôi
        XCTAssertEqual(s.parcel(id: "a")?.status, "Hàng đã trả")
        s.upsertLocal(parcel(#"{"id":"b","track":"B","status":"Bị mất"}"#))
        XCTAssertEqual(s.rev, 9)                         // áp kiện lẻ không đổi mốc
        s.save()

        let again = CloudParcelStore(fileURL: url)
        XCTAssertEqual(again.rev, 9)
        XCTAssertEqual(again.all.map { $0.id }, ["a", "b"])
        XCTAssertEqual(again.parcel(id: "b")?.status, "Bị mất")
        again.reset()
        XCTAssertEqual(again.count, 0); XCTAssertEqual(again.rev, 0)
        XCTAssertFalse(FileManager.default.fileExists(atPath: url.path))
    }

    /// Lỗi 27/11/2026: đơn lưu trong máy từ bản app CHƯA có trường khiếu nại không bao giờ được tải lại -> bảng Khiếu nại thiếu đơn.
    /// Bộ nhớ đệm của bản cũ (không có số phiên bản) phải bị coi là cũ: giữ đơn để xem khi mất mạng nhưng đặt mốc về 0 để tải lại toàn bộ.
    func testOldCacheWithoutFieldVersionForcesFullResync() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("qlhh-cloud-" + UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: dir) }
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let url = dir.appendingPathComponent("parcels.json")
        // tệp do bản app cũ ghi: không có "fields", đơn thiếu complaintStatus
        try Data(#"{"rev":120,"parcels":[{"id":"a","track":"A","status":"Chưa trả"}],"watch":[]}"#.utf8).write(to: url)
        let s = CloudParcelStore(fileURL: url)
        XCTAssertEqual(s.count, 1)                 // vẫn xem được khi mất mạng
        XCTAssertEqual(s.rev, 0)                   // lần đồng bộ tới tải lại từ đầu
        // tải lại: cùng kiện nay có khiếu nại -> phải được ghi đè
        s.apply([parcel(#"{"id":"a","track":"A","status":"Chưa trả","complaintStatus":"Đang xử lý","rev":50}"#)], rev: 130)
        XCTAssertEqual(s.rows(now: Date()).first?.kn, "Đang xử lý")
        s.save()
        let again = CloudParcelStore(fileURL: url)
        XCTAssertEqual(again.rev, 130)             // bản lưu đúng phiên bản: giữ mốc, không tải lại nữa
        XCTAssertEqual(again.parcel(id: "a")?.complaintStatus, "Đang xử lý")
    }

    // MARK: API

    private final class FakeTransport: CloudHTTPTransport {
        var requests: [URLRequest] = []
        let reply: (URLRequest) throws -> (Int, String)
        init(_ reply: @escaping (URLRequest) throws -> (Int, String)) { self.reply = reply }
        func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
            requests.append(request)
            let (code, body) = try reply(request)
            let resp = HTTPURLResponse(url: request.url!, statusCode: code, httpVersion: nil, headerFields: nil)!
            return (Data(body.utf8), resp)
        }
    }

    func testBaseURLNormalisationAndInsecureRejected() throws {
        XCTAssertEqual(try CloudAPI.normalize("https://a.workers.dev"), "https://a.workers.dev/v1")
        XCTAssertEqual(try CloudAPI.normalize(" https://a.workers.dev/ "), "https://a.workers.dev/v1")
        XCTAssertEqual(try CloudAPI.normalize("https://a.workers.dev/v1"), "https://a.workers.dev/v1")
        XCTAssertEqual(try CloudAPI.normalize("http://127.0.0.1:8799"), "http://127.0.0.1:8799/v1")
        for bad in ["", "http://example.com", "ftp://a.b", "khong-phai-url"] {
            XCTAssertThrowsError(try CloudAPI.normalize(bad), bad)
        }
    }

    func testSyncParsesParcelsAndSendsBearerTokenWithoutLeakingItInErrors() async throws {
        let t = FakeTransport { r in
            if r.url!.path == "/v1/sync" {
                return (200, #"{"rev":7,"more":true,"changes":[{"type":"parcel","rev":7,"parcel":{"id":"a","track":"T"}},{"type":"returnWatch","rev":6}]}"#)
            }
            return (401, #"{"error":{"code":"unauthorized","message":"Thiết bị đã bị thu hồi."}}"#)
        }
        let api = try CloudAPI(baseURL: "https://x.workers.dev", token: "SECRET-TOKEN", transport: t)
        let page = try await api.sync(since: 3)
        XCTAssertEqual(page.rev, 7); XCTAssertTrue(page.more); XCTAssertEqual(page.parcels.map { $0.id }, ["a"])
        XCTAssertTrue(t.requests[0].url!.absoluteString.contains("since=3"))
        XCTAssertEqual(t.requests[0].value(forHTTPHeaderField: "Authorization"), "Bearer SECRET-TOKEN")
        do { _ = try await api.me(); XCTFail("phải lỗi") }
        catch let e as CloudAPIError {
            XCTAssertTrue(e.isUnauthorized); XCTAssertEqual(e.message, "Thiết bị đã bị thu hồi.")
            XCTAssertFalse(e.message.contains("SECRET-TOKEN"))
        }
        let net = try CloudAPI(baseURL: "https://x.workers.dev", token: "t", transport: FakeTransport { _ in throw URLError(.notConnectedToInternet) })
        do { _ = try await net.sync(since: 0); XCTFail("phải lỗi") }
        catch let e as CloudAPIError { XCTAssertTrue(e.isNetwork); XCTAssertTrue(e.message.contains("kết nối")) }
    }

    func testScanMapsOutcomesAndAutoGeneratesOpId() async throws {
        let t = FakeTransport { _ in
            (200, #"{"results":[{"opId":"x","ok":true,"rev":9,"result":{"outcome":"alreadyReceived","ack":true,"message":"Kiện đã nhận trước đó.","receivedBy":"Máy A","receivedAt":"25/11/2026 09:00:00","parcel":{"id":"a","track":"T","status":"Hàng đã trả"}}}]}"#)
        }
        let api = try CloudAPI(baseURL: "https://x.workers.dev", token: "t", transport: t)
        let r = try await api.scan(code: "trk")
        XCTAssertEqual(r.outcome, .alreadyReceived); XCTAssertTrue(r.ack)
        XCTAssertEqual(r.receivedBy, "Máy A"); XCTAssertEqual(r.parcel?.status, "Hàng đã trả")
        let body = String(data: t.requests[0].httpBody ?? Data(), encoding: .utf8) ?? ""
        XCTAssertTrue(body.contains("\"opId\""))
        XCTAssertTrue(body.contains("\"scan\""))
    }

    // MARK: Bảng Hàng Hoàn Về

    func testReturnWatchFieldsOnlyForMainRowsStillInTheBoardWithStages() {
        let w = CloudWatchChange(key: "a", since: 1_764_000_000_123, off: false)
        func row(_ json: String, watch: CloudWatchChange? = w) -> OrderRow {
            CloudRowMapper.row(parcel(json), now: now, watch: watch)!
        }
        let s1 = row(#"{"id":"a","track":"T","status":"Chưa trả","platformStatus":"Đã đồng ý"}"#)
        XCTAssertEqual(s1.rw, 1_764_000_000_123); XCTAssertEqual(s1.rws, 1); XCTAssertEqual(s1.ar, 0)
        XCTAssertTrue(s1.inReturnWatch)
        XCTAssertEqual(row(#"{"id":"a","track":"T","platformStatus":"Đang vận chuyển về"}"#).rws, 2)
        XCTAssertEqual(row(#"{"id":"a","track":"T","platformStatus":"Đã đồng ý","returnTrack":"RT9"}"#).rws, 2)
        let s3 = row(#"{"id":"a","track":"T","platformStatus":"Đã đồng ý","returnedAt":"25/11/2026 01:00:00"}"#)
        XCTAssertEqual(s3.rws, 3); XCTAssertGreaterThan(s3.ar, 0)
        XCTAssertEqual(row(#"{"id":"a","track":"T","status":"Hàng đã trả"}"#).rws, 4)

        // không vào bảng: bị bỏ (off), không có dấu, đơn hủy, kho lưu trữ, "Không cần nhận"/"Bị mất"
        XCTAssertEqual(row(#"{"id":"a","track":"T"}"#, watch: CloudWatchChange(key: "a", since: 5, off: true)).rw, 0)
        XCTAssertEqual(row(#"{"id":"a","track":"T"}"#, watch: nil).rw, 0)
        XCTAssertEqual(row(#"{"id":"a","track":"T","orderType":"Đơn hủy"}"#).rw, 0)
        XCTAssertEqual(row(#"{"id":"a","track":"T","isArchived":true}"#).rw, 0)
        XCTAssertEqual(row(#"{"id":"a","track":"T","status":"Bị mất"}"#).rw, 0)
        XCTAssertEqual(row(#"{"id":"a","track":"T","status":"Không cần nhận"}"#).rw, 0)
    }

    func testStoreKeepsReturnWatchMarksAcrossSaveLoadAndFeedsRows() {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("qlhh-cloud-" + UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("parcels.json")
        let s = CloudParcelStore(fileURL: url)
        s.apply([parcel(#"{"id":"a","track":"A","platformStatus":"Đã đồng ý"}"#)], rev: 3,
                watch: [CloudWatchChange(key: "a", since: 111, off: false)])
        XCTAssertEqual(s.watch(id: "a")?.since, 111)
        XCTAssertEqual(s.rows(now: now).first?.rw, 111)
        s.apply([], rev: 4, watch: [CloudWatchChange(key: "a", since: 111, off: true)])     // bị bỏ khỏi bảng
        XCTAssertEqual(s.rows(now: now).first?.rw, 0)
        s.save()

        let again = CloudParcelStore(fileURL: url)
        XCTAssertEqual(again.watch(id: "a")?.off, true)
        again.reset()
        XCTAssertNil(again.watch(id: "a"))
    }

    func testSyncPageParsesReturnWatchChanges() async throws {
        let t = FakeTransport { _ in
            (200, #"{"rev":4,"more":false,"changes":[{"type":"parcel","rev":3,"parcel":{"id":"a","track":"T"}},{"type":"returnWatch","rev":4,"key":"a","since":1764000000123,"off":true}]}"#)
        }
        let api = try CloudAPI(baseURL: "https://x.workers.dev", token: "t", transport: t)
        let page = try await api.sync(since: 0)
        XCTAssertEqual(page.parcels.map { $0.id }, ["a"])
        XCTAssertEqual(page.watch, [CloudWatchChange(key: "a", since: 1_764_000_000_123, off: true)])
    }

    // MARK: ảnh quét

    private func scanResult(_ outcome: CloudScanResult.Outcome, ack: Bool, withParcel: Bool = true) -> CloudScanResult {
        CloudScanResult(outcome: outcome, ack: ack, message: "", parcel: withParcel ? parcel(#"{"id":"a1","track":"trk-9"}"#) : nil, receivedBy: nil, receivedAt: nil)
    }

    func testPhotoIsKeptOnlyWhenCloudConfirmedExactlyOneParcel() {
        XCTAssertTrue(CloudPhotoRules.shouldUpload(scanResult(.received, ack: true)))
        XCTAssertTrue(CloudPhotoRules.shouldUpload(scanResult(.alreadyReceived, ack: true)))
        XCTAssertFalse(CloudPhotoRules.shouldUpload(scanResult(.received, ack: false)))             // chưa chắc đã ghi bền
        XCTAssertFalse(CloudPhotoRules.shouldUpload(scanResult(.ambiguous, ack: false)))
        XCTAssertFalse(CloudPhotoRules.shouldUpload(scanResult(.notFound, ack: false, withParcel: false)))
        XCTAssertFalse(CloudPhotoRules.shouldUpload(scanResult(.needsManualCheck, ack: false)))
        XCTAssertFalse(CloudPhotoRules.shouldUpload(scanResult(.error, ack: false, withParcel: false)))
        XCTAssertFalse(CloudPhotoRules.shouldUpload(scanResult(.received, ack: true, withParcel: false)))
    }

    func testPhotoIdIsSafeUniqueAndWithinLimits() {
        let p = parcel(#"{"id":"a1","track":"spx vn/0123 456"}"#)
        let a = CloudPhotoRules.makePhotoId(parcel: p, now: now, random: 255)
        XCTAssertTrue(a.hasPrefix("p_SPXVN0123456_"))
        XCTAssertTrue(a.hasSuffix("_ff"))
        XCTAssertNotNil(a.range(of: #"^[A-Za-z0-9_-]{6,80}$"#, options: .regularExpression))
        XCTAssertNotEqual(a, CloudPhotoRules.makePhotoId(parcel: p, now: now, random: 256))      // cùng giây vẫn khác nhau
        let long = parcel("{\"id\":\"a\",\"track\":\"" + String(repeating: "A", count: 200) + "\"}")
        XCTAssertLessThanOrEqual(CloudPhotoRules.makePhotoId(parcel: long, now: now).count, 80)
    }

    func testUploaderSendsFullThenThumbOnlyAfterConfirmedScan() async throws {
        let t = FakeTransport { _ in (200, #"{"ok":true}"#) }
        let up = CloudPhotoUploader(api: try CloudAPI(baseURL: "https://x.workers.dev", token: "t", transport: t))

        let skipped = try await up.upload(after: scanResult(.ambiguous, ack: false), full: Data([1, 2, 3]), thumb: Data([9]))
        XCTAssertNil(skipped)
        XCTAssertTrue(t.requests.isEmpty)                       // không đủ điều kiện: không gọi mạng

        let id = try await up.upload(after: scanResult(.received, ack: true), full: Data([1, 2, 3]), thumb: Data([9]), now: now)
        XCTAssertNotNil(id)
        XCTAssertEqual(t.requests.count, 2)
        XCTAssertEqual(t.requests[0].httpMethod, "PUT")
        let u0 = t.requests[0].url!.absoluteString, u1 = t.requests[1].url!.absoluteString
        XCTAssertTrue(u0.contains("parcelId=a1")); XCTAssertFalse(u0.contains("kind=thumb"))
        XCTAssertTrue(u1.contains("kind=thumb"))
        XCTAssertEqual(t.requests[0].httpBody, Data([1, 2, 3]))
        XCTAssertTrue(u0.contains(id!))
    }

    func testUploaderRejectsOversizedOrEmptyPhotoWithoutCallingTheNetwork() async throws {
        let t = FakeTransport { _ in XCTFail("không được gọi mạng"); return (200, "{}") }
        let up = CloudPhotoUploader(api: try CloudAPI(baseURL: "https://x.workers.dev", token: "t", transport: t))
        do { _ = try await up.upload(after: scanResult(.received, ack: true), full: Data(count: CloudPhotoRules.hardMaxBytes + 1), thumb: nil); XCTFail("phải lỗi") }
        catch let e as CloudAPIError { XCTAssertEqual(e.code, "invalid") }
        do { _ = try await up.upload(after: scanResult(.received, ack: true), full: Data(), thumb: nil); XCTFail("phải lỗi") }
        catch let e as CloudAPIError { XCTAssertEqual(e.code, "invalid") }
        XCTAssertTrue(t.requests.isEmpty)
    }

    // MARK: quét khi mất mạng

    func testScanIsRefusedWithoutConnectionAndNeverAcked() async {
        let client = CloudSyncClient(store: CloudParcelStore(), sessions: InMemoryCloudSessionStore(),
                                     transport: FakeTransport { _ in XCTFail("không được gọi mạng"); return (200, "{}") })
        XCTAssertEqual(client.state, .unpaired)
        XCTAssertFalse(client.canWrite)
        let r = await client.scan(code: "TRK")
        XCTAssertEqual(r.outcome, .error); XCTAssertFalse(r.ack)
        XCTAssertTrue(r.message.contains("Mất kết nối"))
    }

    func testPairingValidatesInputBeforeAnyNetworkCall() async {
        let t = FakeTransport { _ in (200, "{}") }
        let client = CloudSyncClient(store: CloudParcelStore(), sessions: InMemoryCloudSessionStore(), transport: t)
        let a = await client.pair(baseURL: "https://a.workers.dev", code: "12ab", deviceName: "Máy")
        XCTAssertEqual(a, "Nhập mật khẩu cửa hàng (ít nhất 8 ký tự) hoặc mã ghép 6 số.")
        let b = await client.pair(baseURL: "https://a.workers.dev", code: "123456", deviceName: "  ")
        XCTAssertEqual(b, "Hãy đặt tên cho thiết bị này.")
        let c = await client.pair(baseURL: "http://khong-an-toan.com", code: "123456", deviceName: "Máy")
        XCTAssertNotNil(c)
        XCTAssertTrue(t.requests.isEmpty)
        XCTAssertEqual(client.state, .unpaired)
    }

    /// Chủ dự án 26/11/2026: điện thoại tự ghép bằng MẬT KHẨU CỬA HÀNG, không cần máy tính tạo mã. Mã 6 số vẫn dùng được.
    func testShopPasswordGoesToJoinAndSixDigitsStillUsePairCode() async {
        var paths: [String] = []
        var bodies: [[String: Any]] = []
        let t = FakeTransport { r in
            paths.append(r.url!.path)
            if let b = r.httpBody, let o = (try? JSONSerialization.jsonObject(with: b)) as? [String: Any] { bodies.append(o) }
            return (401, #"{"error":{"code":"unauthorized","message":"Mật khẩu cửa hàng không đúng."}}"#)
        }
        let client = CloudSyncClient(store: CloudParcelStore(), sessions: InMemoryCloudSessionStore(), transport: t)
        let e1 = await client.pair(baseURL: "https://a.workers.dev", code: " matkhau-cuahang ", deviceName: "iPhone kho 1")
        XCTAssertEqual(e1, "Mật khẩu cửa hàng không đúng.")
        XCTAssertEqual(paths.last, "/v1/pair/join")
        XCTAssertEqual(bodies.last?["joinKey"] as? String, "matkhau-cuahang")
        XCTAssertEqual(bodies.last?["kind"] as? String, "phone")
        _ = await client.pair(baseURL: "https://a.workers.dev", code: "123456", deviceName: "iPhone kho 1")
        XCTAssertEqual(paths.last, "/v1/pair/complete")
        XCTAssertEqual(client.state, .unpaired)
    }

    // chủ dự án 26/11/2026: lướt sang app khác < 15 giây rồi quay lại không được mất kết nối
    func testResumePolicyKeepsAFreshConnectionAndReconnectsOtherwise() {
        let now = Date()
        XCTAssertFalse(CloudResumePolicy.shouldReconnect(state: .connected, lastHeard: now.addingTimeInterval(-8), now: now, pingEvery: 10))
        XCTAssertTrue(CloudResumePolicy.shouldReconnect(state: .connected, lastHeard: now.addingTimeInterval(-60), now: now, pingEvery: 10))
        XCTAssertTrue(CloudResumePolicy.shouldReconnect(state: .disconnected, lastHeard: now, now: now, pingEvery: 10))
        XCTAssertTrue(CloudResumePolicy.shouldReconnect(state: .connecting, lastHeard: now, now: now, pingEvery: 10))
        XCTAssertFalse(CloudResumePolicy.shouldReconnect(state: .revoked, lastHeard: .distantPast, now: now, pingEvery: 10))
        XCTAssertFalse(CloudResumePolicy.shouldReconnect(state: .unpaired, lastHeard: .distantPast, now: now, pingEvery: 10))
    }

    // rà lỗi 26/11/2026: lần kéo về sau mang bản CŨ hơn không được đè bản mới
    func testOlderParcelRevisionNeverOverwritesNewer() {
        let s = CloudParcelStore()
        s.apply([parcel(#"{"id":"a","track":"A","status":"Hàng đã trả","rev":21}"#)], rev: 21)
        s.apply([parcel(#"{"id":"a","track":"A","status":"Chưa trả","rev":15}"#)], rev: 15)
        XCTAssertEqual(s.parcel(id: "a")?.status, "Hàng đã trả")
        s.upsertLocal(parcel(#"{"id":"a","track":"A","status":"Bị mất","rev":22}"#))
        XCTAssertEqual(s.parcel(id: "a")?.status, "Bị mất")
        XCTAssertEqual(s.rev, 21)
    }

    // Khiếu nại (chủ dự án 26/11/2026): trường từ cloud -> dòng hiển thị; lệnh gửi lên cloud
    func testComplaintFieldsReachTheRowAndOpsAreBuilt() {
        let now = Date()
        let r = CloudRowMapper.row(parcel(#"{"id":"a","track":"T","complaintStatus":"Đang xử lý","complaintAt":"26/11/2026 10:00:00","complaintNote":"gửi Shopee","complaintDoneAt":"27/11/2026 09:00:00"}"#), now: now)!
        XCTAssertEqual(r.kn, "Đang xử lý")
        XCTAssertEqual(r.kna, "26/11/2026 10:00:00")
        XCTAssertEqual(r.knn, "gửi Shopee")
        XCTAssertEqual(r.knd, "27/11/2026 09:00:00")
        XCTAssertTrue(r.isComplaint)
        let plain = CloudRowMapper.row(parcel(#"{"id":"b","track":"T2"}"#), now: now)!
        XCTAssertFalse(plain.isComplaint)
        let op = CloudOps.complaint(id: "a", status: "Thành công")
        XCTAssertEqual(op["type"] as? String, "editCell")
        XCTAssertEqual(op["field"] as? String, "complaintStatus")
        XCTAssertEqual(op["value"] as? String, "Thành công")
        XCTAssertEqual(CloudOps.complaintNote(id: "a", note: "x")["field"] as? String, "complaintNote")
        XCTAssertEqual(CloudOps.complaintStatuses, ["Đang xử lý", "Thành công", "Thất bại"])
    }

    // Chủ dự án 27/11/2026: khiếu nại / đổi trạng thái kèm lý do; trang chủ có 3 khiếu nại gần nhất
    func testComplaintReasonOpAndRecentComplaints() {
        let op = CloudOps.complaint(id: "a", status: "Thất bại", reason: "  sàn từ chối ")
        XCTAssertEqual(op["reason"] as? String, "sàn từ chối")
        XCTAssertEqual(op["value"] as? String, "Thất bại")
        XCTAssertNil(CloudOps.complaint(id: "a", status: "", reason: " ")["reason"])
        let now = Date()
        func r(_ id: String, _ kn: String, _ at: String) -> OrderRow {
            CloudRowMapper.row(parcel("{\"id\":\"\(id)\",\"track\":\"T\(id)\",\"complaintStatus\":\"\(kn)\",\"complaintAt\":\"\(at)\"}"), now: now)!
        }
        let plain = CloudRowMapper.row(parcel(#"{"id":"z","track":"TZ"}"#), now: now)!
        let rows = [r("a", "Đang xử lý", "20/11/2026 10:00:00"), plain, r("b", "Thất bại", "25/11/2026 10:00:00"),
                    r("c", "Thành công", "27/11/2026 08:00:00"), r("d", "Đang xử lý", "26/11/2026 10:00:00")]
        XCTAssertEqual(CloudOps.recentComplaints(rows).map { $0.id }, ["c", "d", "b"])
        XCTAssertEqual(CloudOps.lastComplaintLine("27/11/2026 09:00 · Đang xử lý: thiếu áo\n27/11/2026 10:00 · Thất bại: sàn từ chối\n"), "27/11/2026 10:00 · Thất bại: sàn từ chối")
        let long = String(repeating: "x", count: 700) + "\nMỚI"
        XCTAssertTrue(CloudRowMapper.row(parcel("{\"id\":\"l\",\"track\":\"TL\",\"complaintStatus\":\"Đang xử lý\",\"complaintNote\":\"\(long.replacingOccurrences(of: "\n", with: "\\n"))\"}"), now: now)!.knn.hasSuffix("MỚI"))
    }
}
