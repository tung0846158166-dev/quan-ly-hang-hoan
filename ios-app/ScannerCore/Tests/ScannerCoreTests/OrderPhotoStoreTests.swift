import XCTest
@testable import ScannerCore

/// Bộ test thuần cho kho ảnh cục bộ T039:
/// - Khóa ảnh theo kiện (`OrderRow.id` + dự phòng mã vận đơn chuẩn hóa), kiện A KHÔNG lẫn kiện B/C cùng mã đơn gốc (Quy tắc 1, 2).
/// - Mã mơ hồ (nhiều ứng viên) hoặc không tìm thấy KHÔNG tự gắn (Quy tắc 3, 8).
/// - Chỉ mục ghi/đọc nguyên tử và chịu được tệp hỏng (phục hồi từ `.bak`, bảo toàn JPEG, không xóa mồ côi khi chỉ mục hỏng).
/// - Giới hạn 5 ảnh/kiện không tự xóa ảnh cũ, tính tổng dung lượng chính xác, xem trước & xóa ảnh đơn đã hoàn tất cũ hơn N ngày.
final class OrderPhotoStoreTests: XCTestCase {
    private var tempDirs: [URL] = []

    override func tearDown() {
        for d in tempDirs {
            try? FileManager.default.removeItem(at: d)
        }
        tempDirs.removeAll()
        super.tearDown()
    }

    private func makeTempDir() throws -> URL {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("order-photos-test-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        tempDirs.append(dir)
        return dir
    }

    private func makeRow(
        k: String = "m",
        c: String,
        o: String,
        ci: String = "",
        pd: Bool = true,
        rv: Bool = false,
        st: String = "Chưa trả",
        x: String = ""
    ) -> OrderRow {
        var r = OrderRow(k: k, c: c, o: o, p: "Shopee", n: "Áo mẫu", co: "Đen", sz: "L", q: 1, rd: "20/11/2026", due: 0, st: st)
        r.ci = ci
        r.pd = pd
        r.rv = rv
        r.x = x
        return r
    }

    private func fakeJPEG(size: Int = 1024, byte: UInt8 = 0xAB) -> Data {
        var d = Data([0xFF, 0xD8, 0xFF, 0xE0])
        if size > 6 {
            d.append(Data(repeating: byte, count: size - 6))
        }
        d.append(contentsOf: [0xFF, 0xD9])
        return d
    }

    // MARK: - 1. Khóa ảnh theo kiện (Quy tắc 1 & 2) và dự phòng mã vận đơn khi PC đổi id

    func testParcelKeyingIsolatesSiblingsSharingSameOrderId() throws {
        let dir = try makeTempDir()
        let store = OrderPhotoDiskStore(directoryURL: dir)

        // Ba kiện A, B, C cùng mã đơn gốc ORD-SIB-01 nhưng khác mã vận đơn riêng.
        let parcelA = makeRow(c: "SPXVN000001A", o: "ORD-SIB-01")
        let parcelB = makeRow(c: "SPXVN000002B", o: "ORD-SIB-01")
        let parcelC = makeRow(c: "SPXVN000003C", o: "ORD-SIB-01")
        let rows = [parcelA, parcelB, parcelC]
        let idx = SnapshotIndex(rows: rows)
        let liveIds = Set(idx.idToIndex.keys)

        let addA = store.addPhoto(
            jpegData: fakeJPEG(size: 2048, byte: 0x11),
            for: parcelA,
            createdAtMs: 1_700_000_000_000,
            source: .scan,
            scanRequestId: "req-a",
            existingRowIds: liveIds,
            trackingCodeCounts: idx.trackingCodeCounts
        )
        guard case .success(let recA) = addA else {
            return XCTFail("Phải lưu được ảnh cho kiện A")
        }
        XCTAssertEqual(recA.orderRowId, parcelA.id)
        XCTAssertEqual(recA.trackingCode, "SPXVN000001A")

        // Kiện A thấy đúng 1 ảnh; kiện B và C TUYỆT ĐỐI KHÔNG thấy ảnh của kiện A dù chung mã đơn ORD-SIB-01.
        XCTAssertEqual(store.photos(for: parcelA, existingRowIds: liveIds, trackingCodeCounts: idx.trackingCodeCounts).map { $0.id }, [recA.id])
        XCTAssertTrue(store.photos(for: parcelB, existingRowIds: liveIds, trackingCodeCounts: idx.trackingCodeCounts).isEmpty)
        XCTAssertTrue(store.photos(for: parcelC, existingRowIds: liveIds, trackingCodeCounts: idx.trackingCodeCounts).isEmpty)

        // Khi PC chuyển kiện A từ bảng chính ("m") sang kho lưu trữ ("a") làm OrderRow.id đổi:
        // liên kết ảnh vẫn còn nhờ dự phòng theo mã vận đơn chuẩn hóa (và kiện B/C vẫn không lẫn).
        let parcelAArchived = makeRow(k: "a", c: "SPXVN000001A", o: "ORD-SIB-01", pd: false, rv: true, st: "Hàng đã trả")
        let newRows = [parcelAArchived, parcelB, parcelC]
        let newIdx = SnapshotIndex(rows: newRows)
        let newLiveIds = Set(newIdx.idToIndex.keys)

        XCTAssertNotEqual(parcelA.id, parcelAArchived.id)
        XCTAssertEqual(store.photos(for: parcelAArchived, existingRowIds: newLiveIds, trackingCodeCounts: newIdx.trackingCodeCounts).map { $0.id }, [recA.id])
        XCTAssertTrue(store.photos(for: parcelB, existingRowIds: newLiveIds, trackingCodeCounts: newIdx.trackingCodeCounts).isEmpty)
        XCTAssertTrue(store.photos(for: parcelC, existingRowIds: newLiveIds, trackingCodeCounts: newIdx.trackingCodeCounts).isEmpty)
    }

    func testSharedTrackingCodeDoesNotLeakAcrossDistinctRowIds() throws {
        let dir = try makeTempDir()
        let store = OrderPhotoDiskStore(directoryURL: dir)

        let row1 = makeRow(k: "c", c: "SHARED123456", o: "O1", ci: "cxl_1")
        let row2 = makeRow(k: "c", c: "SHARED123456", o: "O2", ci: "cxl_2")
        let rows = [row1, row2]
        let idx = SnapshotIndex(rows: rows)
        let liveIds = Set(idx.idToIndex.keys)

        guard case .success(let rec1) = store.addPhoto(
            jpegData: fakeJPEG(size: 512),
            for: row1,
            createdAtMs: 1_700_000_000_000,
            source: .manualCamera,
            existingRowIds: liveIds,
            trackingCodeCounts: idx.trackingCodeCounts
        ) else { return XCTFail("Phải thêm được ảnh cho row1") }

        XCTAssertEqual(store.photos(for: row1, existingRowIds: liveIds, trackingCodeCounts: idx.trackingCodeCounts).map { $0.id }, [rec1.id])
        XCTAssertTrue(store.photos(for: row2, existingRowIds: liveIds, trackingCodeCounts: idx.trackingCodeCounts).isEmpty,
                      "row2 không được nhận nhầm ảnh thuộc row1 khi chung mã vận đơn")
    }

    // MARK: - 2. Mã mơ hồ / không tìm thấy KHÔNG tự gắn (Quy tắc 3 & 8)

    func testAmbiguousAndNotFoundCodesNeverAutoAttach() throws {
        let dir = try makeTempDir()
        let store = OrderPhotoDiskStore(directoryURL: dir)

        let parcelA = makeRow(c: "SPXVN100001A", o: "ORD-MULTI-99", x: "OUTBOUND777")
        let parcelB = makeRow(c: "SPXVN100002B", o: "ORD-MULTI-99", x: "OUTBOUND777")
        let rows = [parcelA, parcelB]
        let idx = SnapshotIndex(rows: rows)

        // Quét mã đơn gốc trùng 2 kiện -> ambiguous, KHÔNG tự gắn
        let byOrder = store.attachIfUnique(
            jpegData: fakeJPEG(size: 1000),
            rawCode: "ORD-MULTI-99",
            in: rows,
            index: idx,
            createdAtMs: 1_700_000_001_000,
            scanRequestId: "scan-amb-1"
        )
        if case .ambiguous(let candidates) = byOrder {
            XCTAssertEqual(candidates.count, 2)
        } else {
            XCTFail("Mã đơn có 2 kiện phải trả .ambiguous")
        }
        XCTAssertTrue(store.records.isEmpty, "Mã mơ hồ không được lưu vào kho ảnh đơn")

        // Quét mã giao đi dùng chung -> ambiguous, KHÔNG tự gắn
        let bySharedOutbound = store.attachIfUnique(
            jpegData: fakeJPEG(size: 1000),
            rawCode: "OUTBOUND777",
            in: rows,
            index: idx,
            createdAtMs: 1_700_000_002_000,
            scanRequestId: "scan-amb-2"
        )
        if case .ambiguous(let candidates) = bySharedOutbound {
            XCTAssertEqual(candidates.count, 2)
        } else {
            XCTFail("Mã giao chung phải trả .ambiguous")
        }
        XCTAssertTrue(store.records.isEmpty)

        // Quét mã không tồn tại -> notFound, KHÔNG tự gắn
        let missing = store.attachIfUnique(
            jpegData: fakeJPEG(size: 1000),
            rawCode: "SPXVN999999Z",
            in: rows,
            index: idx,
            createdAtMs: 1_700_000_003_000,
            scanRequestId: "scan-miss-1"
        )
        XCTAssertEqual(missing, .notFound)
        XCTAssertTrue(store.records.isEmpty)

        // Quét đúng mã vận đơn riêng của kiện B -> khớp DUY NHẤT 1 kiện -> tự gắn vào đúng kiện B
        let exactB = store.attachIfUnique(
            jpegData: fakeJPEG(size: 1500),
            rawCode: "spxvn100002b",
            in: rows,
            index: idx,
            createdAtMs: 1_700_000_004_000,
            scanRequestId: "scan-ok-b"
        )
        guard case .attached(let recB, let matchedRow) = exactB else {
            return XCTFail("Mã vận đơn riêng phải tự gắn vào đúng kiện B")
        }
        XCTAssertEqual(matchedRow.id, parcelB.id)
        XCTAssertEqual(recB.orderRowId, parcelB.id)
        XCTAssertTrue(store.photos(for: parcelA).isEmpty)
        XCTAssertEqual(store.photos(for: parcelB).count, 1)
    }

    // MARK: - 3. Tối đa 5 ảnh/kiện, không tự xóa ảnh cũ

    func testMaxFivePhotosPerParcelDoesNotOverwriteOldPhotos() throws {
        let dir = try makeTempDir()
        let store = OrderPhotoDiskStore(directoryURL: dir)
        let row = makeRow(c: "SPXVN555555", o: "ORD-555")

        var ids: [String] = []
        for i in 1...5 {
            let res = store.addPhoto(
                jpegData: fakeJPEG(size: 800 + i * 10, byte: UInt8(i)),
                for: row,
                createdAtMs: 1_700_000_000_000 + i * 1000,
                source: .manualCamera
            )
            guard case .success(let rec) = res else {
                return XCTFail("Ảnh thứ \(i) phải lưu thành công")
            }
            ids.append(rec.id)
        }

        // Ảnh thứ 6 phải bị từ chối rõ ràng và giữ nguyên đủ 5 ảnh cũ.
        let sixth = store.addPhoto(
            jpegData: fakeJPEG(size: 900, byte: 0x06),
            for: row,
            createdAtMs: 1_700_000_099_000,
            source: .manualLibrary
        )
        XCTAssertEqual(sixth, .failure(.limitReached(max: 5)))
        XCTAssertEqual(store.photos(for: row).map { $0.id }, ids)
    }

    // MARK: - 4. Ghi/đọc chỉ mục nguyên tử & chịu được tệp hỏng

    func testAtomicPersistenceAndCorruptIndexResilience() throws {
        let dir = try makeTempDir()
        let row = makeRow(c: "SPXVN888888", o: "ORD-888")

        let store1 = OrderPhotoDiskStore(directoryURL: dir)
        guard case .success(let rec) = store1.addPhoto(
            jpegData: fakeJPEG(size: 4096),
            for: row,
            createdAtMs: 1_700_000_100_000,
            source: .scan,
            scanRequestId: "req-atomic"
        ) else { return XCTFail("Phải ghi được ảnh") }

        let jpegURL = store1.photoURL(for: rec)
        XCTAssertTrue(FileManager.default.fileExists(atPath: jpegURL.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: store1.indexURL.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: store1.backupIndexURL.path))

        // Mở lại từ đĩa -> đọc đúng bản ghi, không sót tệp `.tmp`
        let store2 = OrderPhotoDiskStore(directoryURL: dir)
        XCTAssertTrue(store2.indexLoadedCleanly)
        XCTAssertFalse(store2.corruptedIndexDetected)
        XCTAssertEqual(store2.photos(for: row), [rec])
        let dirFiles = try FileManager.default.contentsOfDirectory(atPath: dir.path)
        XCTAssertFalse(dirFiles.contains { $0.contains(".tmp.") }, "Không được sót tệp tạm sau ghi nguyên tử")

        // Làm hỏng `index.json` -> phải tự phục hồi từ `index.json.bak` và không mất tệp JPEG
        try Data("{corrupt-json".utf8).write(to: store1.indexURL)
        let recovered = OrderPhotoDiskStore(directoryURL: dir)
        XCTAssertTrue(recovered.corruptedIndexDetected)
        XCTAssertEqual(recovered.photos(for: row), [rec])
        XCTAssertTrue(FileManager.default.fileExists(atPath: jpegURL.path))

        // Làm hỏng CẢ `index.json` LẪN `index.json.bak` -> không crash, giữ nguyên JPEG, từ chối dọn mồ côi
        try Data("BROKEN_PRIMARY".utf8).write(to: store1.indexURL)
        try Data("BROKEN_BACKUP".utf8).write(to: store1.backupIndexURL)
        let degraded = OrderPhotoDiskStore(directoryURL: dir)
        XCTAssertTrue(degraded.corruptedIndexDetected)
        XCTAssertFalse(degraded.indexLoadedCleanly)
        XCTAssertTrue(degraded.records.isEmpty)
        XCTAssertTrue(FileManager.default.fileExists(atPath: jpegURL.path), "Không được xóa tệp JPEG khi chỉ mục hỏng")

        let orphanReport = degraded.cleanOrphanFiles(minAgeSeconds: 0)
        XCTAssertTrue(orphanReport.removedFiles.isEmpty)
        XCTAssertNotNil(orphanReport.skippedReason)
        XCTAssertTrue(FileManager.default.fileExists(atPath: jpegURL.path), "Không được xóa mồ côi khi chưa chắc chắn")
    }

    func testSafeOrphanCleanupOnlyDeletesOldUnreferencedJpegsWhenIndexIsClean() throws {
        let dir = try makeTempDir()
        let store = OrderPhotoDiskStore(directoryURL: dir)
        let row = makeRow(c: "SPXVN777777", o: "ORD-777")
        guard case .success(let rec) = store.addPhoto(
            jpegData: fakeJPEG(size: 1024),
            for: row,
            createdAtMs: 1_700_000_200_000,
            source: .scan
        ) else { return XCTFail() }

        // Tạo 1 tệp JPEG mồ côi cũ và 1 tệp JPEG mồ côi vừa tạo
        let oldOrphan = dir.appendingPathComponent("orphan_old.jpg")
        try fakeJPEG(size: 700).write(to: oldOrphan)
        let pastDate = Date(timeIntervalSinceNow: -600)
        try FileManager.default.setAttributes([.modificationDate: pastDate], ofItemAtPath: oldOrphan.path)

        let freshOrphan = dir.appendingPathComponent("orphan_fresh.jpg")
        try fakeJPEG(size: 500).write(to: freshOrphan)

        let report = store.cleanOrphanFiles(minAgeSeconds: 120, now: Date())
        XCTAssertNil(report.skippedReason)
        XCTAssertEqual(report.removedFiles, ["orphan_old.jpg"])
        XCTAssertEqual(report.freedBytes, 700)
        XCTAssertFalse(FileManager.default.fileExists(atPath: oldOrphan.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: freshOrphan.path), "Tệp vừa tạo chưa quá ngưỡng an toàn không bị xóa")
        XCTAssertTrue(FileManager.default.fileExists(atPath: store.photoURL(for: rec).path), "Ảnh có trong chỉ mục không bao giờ bị xóa")
    }

    // MARK: - 5. Tính tổng dung lượng & dọn ảnh đơn đã hoàn tất cũ hơn N ngày

    func testStorageSummaryAndCompletedOrderCleanupPreviewAndExecute() throws {
        let dir = try makeTempDir()
        let store = OrderPhotoDiskStore(directoryURL: dir)

        let dayMs = 86_400_000
        let nowMs = 1_800_000_000_000

        let completedOld = makeRow(c: "SPXVN-DONE-OLD", o: "O-OLD", pd: false, rv: true, st: "Hàng đã trả")
        let completedRecent = makeRow(c: "SPXVN-DONE-NEW", o: "O-NEW", pd: false, rv: true, st: "Hàng đã trả")
        let pendingOld = makeRow(c: "SPXVN-PEND-OLD", o: "O-PEND", pd: true, rv: false, st: "Chưa trả")
        let rows = [completedOld, completedRecent, pendingOld]
        let idx = SnapshotIndex(rows: rows)

        guard case .success(let r1) = store.addPhoto(jpegData: fakeJPEG(size: 100_000), for: completedOld, createdAtMs: nowMs - 45 * dayMs, source: .scan),
              case .success(let r2) = store.addPhoto(jpegData: fakeJPEG(size: 80_000), for: completedRecent, createdAtMs: nowMs - 5 * dayMs, source: .scan),
              case .success(let r3) = store.addPhoto(jpegData: fakeJPEG(size: 120_000), for: pendingOld, createdAtMs: nowMs - 60 * dayMs, source: .scan) else {
            return XCTFail("Phải thêm được 3 ảnh mẫu")
        }

        let summary = store.storageSummary()
        XCTAssertEqual(summary.photoCount, 3)
        XCTAssertEqual(summary.totalBytes, 300_000)
        XCTAssertEqual(summary.formattedMB, "0,3 MB")

        // Xem trước xóa đơn đã hoàn tất cũ hơn 30 ngày: CHỈ chọn r1 (đã hoàn tất + 45 ngày);
        // KHÔNG chạm r2 (mới 5 ngày) và KHÔNG chạm r3 (đang chờ hoàn dù 60 ngày).
        let preview = store.previewCleanupCompletedOrders(rows: rows, index: idx, olderThanDays: 30, nowMs: nowMs)
        XCTAssertEqual(preview.recordIds, [r1.id])
        XCTAssertEqual(preview.photoCount, 1)
        XCTAssertEqual(preview.totalBytes, 100_000)

        // Thực hiện xóa sau khi người dùng xác nhận
        let result = store.executeCleanup(recordIds: preview.recordIds)
        XCTAssertEqual(result.deletedCount, 1)
        XCTAssertEqual(result.freedBytes, 100_000)

        let after = store.storageSummary()
        XCTAssertEqual(after.photoCount, 2)
        XCTAssertEqual(after.totalBytes, 200_000)
        XCTAssertTrue(store.photos(for: completedOld).isEmpty)
        XCTAssertEqual(store.photos(for: completedRecent).map { $0.id }, [r2.id])
        XCTAssertEqual(store.photos(for: pendingOld).map { $0.id }, [r3.id])
    }

    // MARK: - 6. Cấu hình nén ảnh lưu trữ (1280 px, không dưới 960 px, mục tiêu <= 150 KB)

    func testCompressionConfigEnforcesFloorAndAspectRatio() {
        let cfg = PhotoCompressionConfig.storageDefault
        XCTAssertEqual(cfg.maxDimension, 1280)
        XCTAssertEqual(cfg.minDimension, 960)
        XCTAssertEqual(cfg.targetMaxBytes, 150 * 1024)

        let scaled = PhotoCompressionConfig.scaledSize(width: 1080, height: 1920, maxDim: cfg.maxDimension)
        XCTAssertEqual(scaled.width, 720)
        XCTAssertEqual(scaled.height, 1280)

        // Ảnh nhỏ hơn 1280 không bị phóng to
        let small = PhotoCompressionConfig.scaledSize(width: 600, height: 800, maxDim: cfg.maxDimension)
        XCTAssertEqual(small.width, 600)
        XCTAssertEqual(small.height, 800)

        // Bước giảm cạnh dài: thử 1280 rồi tối đa 960, không bao giờ < 960
        let dims = cfg.dimensionSteps(forLongestSide: 4032)
        XCTAssertEqual(dims, [1280, 960])
        XCTAssertTrue(dims.allSatisfy { $0 >= 960 })

        // Các bậc chất lượng giảm dần đều về >= minQuality
        let qs = cfg.qualitySteps()
        XCTAssertEqual(qs.first, 0.48)
        XCTAssertEqual(qs.last, 0.28)
        XCTAssertGreaterThanOrEqual(qs.count, 4)
    }
}
