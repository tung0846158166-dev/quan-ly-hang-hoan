import XCTest
@testable import ScannerCore

/// Bộ XCTest thuần kiểm tra toàn bộ quy tắc T040:
/// - Chỉ giữ ảnh khi PC ACK quét thành công (Quy tắc 6) và khớp đúng 1 kiện (Quy tắc 1, 2).
/// - Không tìm thấy / mơ hồ (Quy tắc 3) / lỗi / bị từ chối / quét trùng -> xóa ảnh tạm ngay (tệp + tham chiếu).
/// - Chưa có kết quả (mất mạng / chờ PC ACK) -> giữ ảnh tạm, rồi xử lý khi có kết quả; treo quá 7 ngày cảnh báo, chỉ xóa ảnh tạm khi người dùng chọn bỏ (KHÔNG xóa lượt quét, Quy tắc 8).
/// - Hủy/xóa lượt quét ở Lịch sử -> xóa luôn ảnh tạm, KHÔNG động ảnh đã gắn trong kho.
/// - Dọn ảnh tạm mồ côi khi mở app chỉ xóa tệp không còn lượt quét tham chiếu VÀ không có trong chỉ mục kho; ảnh đã gắn không bao giờ bị dọn nhầm.
final class ScanPhotoLifecycleTests: XCTestCase {
    private var tempDirs: [URL] = []

    override func tearDown() {
        for d in tempDirs {
            try? FileManager.default.removeItem(at: d)
        }
        tempDirs.removeAll()
        super.tearDown()
    }

    private func makeEnv() throws -> (tempDir: URL, vaultDir: URL, vault: OrderPhotoDiskStore) {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("t040-lifecycle-test-\(UUID().uuidString)", isDirectory: true)
        let tempDir = root.appendingPathComponent("photos", isDirectory: true)
        let vaultDir = root.appendingPathComponent("order-photos", isDirectory: true)
        try FileManager.default.createDirectory(at: tempDir, withIntermediateDirectories: true)
        try FileManager.default.createDirectory(at: vaultDir, withIntermediateDirectories: true)
        tempDirs.append(root)
        let vault = OrderPhotoDiskStore(directoryURL: vaultDir)
        return (tempDir, vaultDir, vault)
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
        var r = OrderRow(k: k, c: c, o: o, p: "Shopee", n: "Sản phẩm mẫu", co: "Đen", sz: "M", q: 1, rd: "20/11/2026", due: 0, st: st)
        r.ci = ci
        r.pd = pd
        r.rv = rv
        r.x = x
        return r
    }

    private func fakeJPEG(size: Int = 1024, byte: UInt8 = 0x55) -> Data {
        var d = Data([0xFF, 0xD8, 0xFF, 0xE0])
        if size > 6 {
            d.append(Data(repeating: byte, count: size - 6))
        }
        d.append(contentsOf: [0xFF, 0xD9])
        return d
    }

    @discardableResult
    private func writeTempPhoto(name: String, in dir: URL, size: Int = 1024, byte: UInt8 = 0x55) throws -> URL {
        let url = dir.appendingPathComponent(name)
        try fakeJPEG(size: size, byte: byte).write(to: url)
        return url
    }

    // MARK: - 1. Đúng (PC ACK thành công + khớp duy nhất 1 kiện) -> giữ ảnh, gắn vào đúng kiện (tách kiện A/B/C)

    func testConfirmedSuccessWithUniqueParcelKeepsAndAttachesPhotoToExactParcel() throws {
        let env = try makeEnv()
        let parcelA = makeRow(c: "SPXVN100001A", o: "ORD-SHARED-01")
        let parcelB = makeRow(c: "SPXVN100002B", o: "ORD-SHARED-01")
        let rows = [parcelA, parcelB]
        let idx = SnapshotIndex(rows: rows)

        let tempURL = try writeTempPhoto(name: "sc-ok-1.jpg", in: env.tempDir, size: 2048)
        var item = ScanItem(requestId: "sc-ok-1", code: "SPXVN100001A", kind: .returnScan, time: 1_700_000_000_000, photoFile: "sc-ok-1.jpg")
        XCTAssertTrue(item.apply(PcAck(requestId: "sc-ok-1", code: "SPXVN100001A", outcome: "success", ackStage: "scan", orderNum: "1")))
        var items = [item]

        let report = ScanPhotoLifecycle.reconcile(
            items: &items,
            tempPhotosDir: env.tempDir,
            vault: env.vault,
            rows: rows,
            index: idx,
            hasSnapshot: true
        )

        XCTAssertTrue(report.changed)
        XCTAssertEqual(report.attachedRequestIds, ["sc-ok-1"])
        XCTAssertTrue(report.discardedRequestIds.isEmpty)
        XCTAssertNil(items[0].photoFile, "Tệp tạm phải được gỡ tham chiếu sau khi đã chuyển vào kho")
        XCTAssertEqual(items[0].attachedOrderId, parcelA.id)
        XCTAssertNotNil(items[0].attachedPhotoId)
        XCTAssertFalse(FileManager.default.fileExists(atPath: tempURL.path), "Tệp tạm phải được dọn sau khi kho ghi nguyên tử")

        // Ảnh gắn đúng kiện A, TUYỆT ĐỐI không lẫn sang kiện B cùng mã đơn gốc (Quy tắc 1 & 2)
        let photosA = env.vault.photos(for: parcelA, existingRowIds: Set(idx.idToIndex.keys), trackingCodeCounts: idx.trackingCodeCounts)
        let photosB = env.vault.photos(for: parcelB, existingRowIds: Set(idx.idToIndex.keys), trackingCodeCounts: idx.trackingCodeCounts)
        XCTAssertEqual(photosA.count, 1)
        XCTAssertEqual(photosA[0].id, items[0].attachedPhotoId)
        XCTAssertTrue(FileManager.default.fileExists(atPath: env.vault.photoURL(for: photosA[0]).path))
        XCTAssertTrue(photosB.isEmpty)
    }

    // MARK: - 2. Không tìm thấy / mơ hồ / lỗi / bị từ chối / quét trùng -> xóa ảnh tạm ngay

    func testNotFoundAmbiguousErrorRejectedAndDuplicateImmediatelyDeleteTempPhoto() throws {
        let env = try makeEnv()
        let parcelA = makeRow(c: "SPXVN200001A", o: "ORD-DUP-02", x: "OUT-SHARED")
        let parcelB = makeRow(c: "SPXVN200002B", o: "ORD-DUP-02", x: "OUT-SHARED")
        let rows = [parcelA, parcelB]
        let idx = SnapshotIndex(rows: rows)

        // Kiện A đã có sẵn 1 ảnh hợp lệ từ lần quét đầu tiên -> quét trùng sau đó KHÔNG được làm mất ảnh này!
        guard case .success(let existingPhoto) = env.vault.addPhoto(
            jpegData: fakeJPEG(size: 1800, byte: 0xAA),
            for: parcelA,
            createdAtMs: 1_700_000_000_000,
            source: .scan,
            scanRequestId: "sc-first"
        ) else {
            return XCTFail("Phải lưu sẵn ảnh hợp lệ của kiện A")
        }

        let scenarios: [(id: String, code: String, outcome: String)] = [
            ("sc-nf", "SPXVN999999Z", "not_found"),
            ("sc-amb", "OUT-SHARED", "ambiguous"),
            ("sc-err", "SPXVN200001A", "error"),
            ("sc-inv", "SPXVN200001A", "invalid"),
            ("sc-lck", "SPXVN200001A", "locked"),
            ("sc-dup", "SPXVN200001A", "duplicate")
        ]

        var items: [ScanItem] = []
        var tempURLs: [String: URL] = [:]
        for s in scenarios {
            let file = "\(s.id).jpg"
            tempURLs[s.id] = try writeTempPhoto(name: file, in: env.tempDir, size: 900)
            var it = ScanItem(requestId: s.id, code: s.code, kind: .returnScan, time: 1_700_000_100_000, photoFile: file)
            XCTAssertTrue(it.apply(PcAck(requestId: s.id, code: s.code, outcome: s.outcome, ackStage: "scan")), s.id)
            XCTAssertFalse(it.detail.contains("ảnh vẫn giữ trên điện thoại"), "Không còn chuỗi giữ ảnh T039 cho \(s.id)")
            items.append(it)
        }

        let report = ScanPhotoLifecycle.reconcile(
            items: &items,
            tempPhotosDir: env.tempDir,
            vault: env.vault,
            rows: rows,
            index: idx,
            hasSnapshot: true
        )

        XCTAssertTrue(report.changed)
        XCTAssertTrue(report.attachedRequestIds.isEmpty)
        XCTAssertEqual(Set(report.discardedRequestIds), Set(scenarios.map { $0.id }))

        for it in items {
            XCTAssertNil(it.photoFile, "Phải xóa tham chiếu photoFile cho \(it.requestId)")
            XCTAssertNil(it.attachedOrderId, "Không gắn orderId cho \(it.requestId)")
            XCTAssertNil(it.attachedPhotoId, "Không gắn photoId cho \(it.requestId)")
            if let u = tempURLs[it.requestId] {
                XCTAssertFalse(FileManager.default.fileExists(atPath: u.path), "Phải xóa tệp ảnh tạm của \(it.requestId)")
            }
        }

        // Ảnh cũ của kiện A từ lần quét đầu PHẢI còn nguyên vẹn trong kho!
        let remainingA = env.vault.photos(for: parcelA)
        XCTAssertEqual(remainingA.map { $0.id }, [existingPhoto.id])
        XCTAssertTrue(FileManager.default.fileExists(atPath: env.vault.photoURL(for: existingPhoto).path))
    }

    /// Ngay cả khi PC ACK success nhưng snapshot địa phương khớp >= 2 ứng viên (mơ hồ, Quy tắc 3) -> xóa ảnh tạm, không đoán.
    func testConfirmedSuccessWithAmbiguousLocalMatchDiscardsTempPhoto() throws {
        let env = try makeEnv()
        let parcelA = makeRow(c: "SPXVN300001A", o: "ORD-AMB-03")
        let parcelB = makeRow(c: "SPXVN300002B", o: "ORD-AMB-03")
        let rows = [parcelA, parcelB]
        let idx = SnapshotIndex(rows: rows)

        let tempURL = try writeTempPhoto(name: "sc-amb-local.jpg", in: env.tempDir)
        var it = ScanItem(requestId: "sc-amb-local", code: "ORD-AMB-03", kind: .returnScan, time: 1_700_000_000_000, photoFile: "sc-amb-local.jpg")
        it.apply(PcAck(requestId: "sc-amb-local", code: "ORD-AMB-03", outcome: "success", ackStage: "scan"))
        var items = [it]

        let report = ScanPhotoLifecycle.reconcile(
            items: &items,
            tempPhotosDir: env.tempDir,
            vault: env.vault,
            rows: rows,
            index: idx,
            hasSnapshot: true
        )

        XCTAssertEqual(report.discardedRequestIds, ["sc-amb-local"])
        XCTAssertNil(items[0].photoFile)
        XCTAssertNil(items[0].attachedPhotoId)
        XCTAssertFalse(FileManager.default.fileExists(atPath: tempURL.path))
        XCTAssertTrue(env.vault.records.isEmpty)
    }

    // MARK: - 3. Chưa có kết quả (chờ PC ACK) -> giữ ảnh tạm, rồi xử lý khi có kết quả

    func testPendingScanKeepsTempPhotoUntilPcAckThenAttachesOrDeletes() throws {
        let env = try makeEnv()
        let parcel = makeRow(c: "SPXVN400001", o: "ORD-PEND-04")
        let rows = [parcel]
        let idx = SnapshotIndex(rows: rows)

        let tempOk = try writeTempPhoto(name: "sc-pend-ok.jpg", in: env.tempDir, size: 1500)
        let tempBad = try writeTempPhoto(name: "sc-pend-bad.jpg", in: env.tempDir, size: 1500)
        var items = [
            ScanItem(requestId: "sc-pend-ok", code: "SPXVN400001", kind: .returnScan, time: 1_700_000_000_000, photoFile: "sc-pend-ok.jpg"),
            ScanItem(requestId: "sc-pend-bad", code: "SPXVN400999", kind: .returnScan, time: 1_700_000_001_000, photoFile: "sc-pend-bad.jpg")
        ]

        // Giai đoạn 1: mất mạng / chưa có PC ACK -> KHÔNG gắn vào kho sớm và KHÔNG xóa ảnh tạm!
        let r1 = ScanPhotoLifecycle.reconcile(
            items: &items,
            tempPhotosDir: env.tempDir,
            vault: env.vault,
            rows: rows,
            index: idx,
            hasSnapshot: true
        )
        XCTAssertFalse(r1.changed)
        XCTAssertTrue(env.vault.records.isEmpty, "Chưa có PC ACK thì tuyệt đối chưa gắn vào kho")
        XCTAssertTrue(FileManager.default.fileExists(atPath: tempOk.path), "Đang chờ PC ACK phải giữ ảnh tạm")
        XCTAssertTrue(FileManager.default.fileExists(atPath: tempBad.path), "Đang chờ PC ACK phải giữ ảnh tạm")
        XCTAssertEqual(items[0].photoFile, "sc-pend-ok.jpg")
        XCTAssertEqual(items[1].photoFile, "sc-pend-bad.jpg")

        // Giai đoạn 2: có mạng, PC trả ACK (1 đúng, 1 không tìm thấy) -> đối soát áp dụng đúng mục 1 & 2
        XCTAssertTrue(items[0].apply(PcAck(requestId: "sc-pend-ok", code: "SPXVN400001", outcome: "success", ackStage: "scan")))
        XCTAssertTrue(items[1].apply(PcAck(requestId: "sc-pend-bad", code: "SPXVN400999", outcome: "not_found", ackStage: "scan")))

        let r2 = ScanPhotoLifecycle.reconcile(
            items: &items,
            tempPhotosDir: env.tempDir,
            vault: env.vault,
            rows: rows,
            index: idx,
            hasSnapshot: true
        )
        XCTAssertEqual(r2.attachedRequestIds, ["sc-pend-ok"])
        XCTAssertEqual(r2.discardedRequestIds, ["sc-pend-bad"])
        XCTAssertNotNil(items[0].attachedPhotoId)
        XCTAssertNil(items[0].photoFile)
        XCTAssertNil(items[1].attachedPhotoId)
        XCTAssertNil(items[1].photoFile)
        XCTAssertFalse(FileManager.default.fileExists(atPath: tempOk.path))
        XCTAssertFalse(FileManager.default.fileExists(atPath: tempBad.path))
        XCTAssertEqual(env.vault.photos(for: parcel).count, 1)
    }

    func testConfirmedSuccessWaitsIfSnapshotIsOlderThanScanThenAttachesWhenFreshSnapshotArrives() throws {
        let env = try makeEnv()
        let scanTime = 1_700_000_500_000
        let staleSnapTime = 1_700_000_100_000
        let freshSnapTime = 1_700_000_600_000

        let tempURL = try writeTempPhoto(name: "sc-fresh.jpg", in: env.tempDir, size: 1200)
        var it = ScanItem(requestId: "sc-fresh", code: "SPXVN500001", kind: .returnScan, time: scanTime, photoFile: "sc-fresh.jpg")
        it.apply(PcAck(requestId: "sc-fresh", code: "SPXVN500001", outcome: "success", ackStage: "scan"))
        var items = [it]

        // Snapshot cũ chưa chứa đơn mới nhập trên PC -> giữ tạm & yêu cầu snapshot mới
        let oldRows = [makeRow(c: "SPXVN-OTHER", o: "O-OTHER")]
        let rOld = ScanPhotoLifecycle.reconcile(
            items: &items,
            tempPhotosDir: env.tempDir,
            vault: env.vault,
            rows: oldRows,
            index: SnapshotIndex(rows: oldRows),
            hasSnapshot: true,
            snapshotUpdatedAtMs: staleSnapTime
        )
        XCTAssertTrue(rOld.waitingForFreshSnapshot)
        XCTAssertFalse(rOld.changed)
        XCTAssertTrue(FileManager.default.fileExists(atPath: tempURL.path))

        // Snapshot mới hơn đã có đơn -> gắn vào kho và dọn tệp tạm
        let newParcel = makeRow(c: "SPXVN500001", o: "O-NEW-500")
        let newRows = oldRows + [newParcel]
        let rNew = ScanPhotoLifecycle.reconcile(
            items: &items,
            tempPhotosDir: env.tempDir,
            vault: env.vault,
            rows: newRows,
            index: SnapshotIndex(rows: newRows),
            hasSnapshot: true,
            snapshotUpdatedAtMs: freshSnapTime
        )
        XCTAssertEqual(rNew.attachedRequestIds, ["sc-fresh"])
        XCTAssertEqual(items[0].attachedOrderId, newParcel.id)
        XCTAssertNil(items[0].photoFile)
        XCTAssertFalse(FileManager.default.fileExists(atPath: tempURL.path))
        XCTAssertEqual(env.vault.photos(for: newParcel).count, 1)
    }

    // MARK: - 4. Treo quá 7 ngày -> cảnh báo & chỉ xóa ảnh tạm khi người dùng chọn bỏ (KHÔNG xóa lượt quét, Quy tắc 8)

    func testStalePendingOver7DaysWarnsAndUserDiscardOnlyRemovesTempPhotoNotScanItem() throws {
        let env = try makeEnv()
        let dayMs = 86_400_000
        let nowMs = 1_800_000_000_000

        let staleURL = try writeTempPhoto(name: "sc-stale.jpg", in: env.tempDir, size: 2200)
        let recentURL = try writeTempPhoto(name: "sc-recent.jpg", in: env.tempDir, size: 1100)

        var items = [
            ScanItem(requestId: "sc-stale", code: "SPXVN600001", kind: .returnScan, time: nowMs - 8 * dayMs, photoFile: "sc-stale.jpg"),
            ScanItem(requestId: "sc-recent", code: "SPXVN600002", kind: .returnScan, time: nowMs - 2 * dayMs, photoFile: "sc-recent.jpg")
        ]

        let stale = ScanPhotoLifecycle.stalePendingTempPhotoItems(in: items, tempPhotosDir: env.tempDir, nowMs: nowMs)
        XCTAssertEqual(stale.map { $0.requestId }, ["sc-stale"])

        let summary = ScanPhotoLifecycle.tempPhotoSummary(for: stale, tempPhotosDir: env.tempDir)
        XCTAssertEqual(summary.photoCount, 1)
        XCTAssertEqual(summary.totalBytes, 2200)

        // Người dùng chọn bỏ ảnh tạm của lượt treo quá 7 ngày
        let res = ScanPhotoLifecycle.discardTempPhotos(requestIds: ["sc-stale"], items: &items, tempPhotosDir: env.tempDir)
        XCTAssertEqual(res.deletedCount, 1)
        XCTAssertEqual(res.freedBytes, 2200)
        XCTAssertFalse(FileManager.default.fileExists(atPath: staleURL.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: recentURL.path))

        // Quy tắc 8: Lượt quét sc-stale PHẢI CÒN NGUYÊN trong hàng đợi (chỉ bỏ ảnh tạm)
        XCTAssertEqual(items.count, 2)
        XCTAssertEqual(items[0].requestId, "sc-stale")
        XCTAssertFalse(items[0].scanConfirmed)
        XCTAssertNil(items[0].photoFile)
        XCTAssertEqual(items[1].photoFile, "sc-recent.jpg")
    }

    // MARK: - 5. Hủy / xóa lượt quét trong Lịch sử -> xóa ảnh tạm, giữ nguyên ảnh đã gắn vào đơn

    func testDeleteScanItemRemovesTempPhotoButPreservesVaultPhoto() throws {
        let env = try makeEnv()
        let parcel = makeRow(c: "SPXVN700001", o: "ORD-700")
        let rows = [parcel]
        let idx = SnapshotIndex(rows: rows)

        let pendingTemp = try writeTempPhoto(name: "sc-del-pend.jpg", in: env.tempDir, size: 1400)
        let attachedTemp = try writeTempPhoto(name: "sc-del-att.jpg", in: env.tempDir, size: 1600)

        var attachedItem = ScanItem(requestId: "sc-del-att", code: "SPXVN700001", kind: .returnScan, time: 1_700_000_000_000, photoFile: "sc-del-att.jpg")
        attachedItem.apply(PcAck(requestId: "sc-del-att", code: "SPXVN700001", outcome: "success", ackStage: "scan"))

        let pendingItem = ScanItem(requestId: "sc-del-pend", code: "SPXVN700002", kind: .returnScan, time: 1_700_000_010_000, photoFile: "sc-del-pend.jpg")

        var items = [pendingItem, attachedItem]
        ScanPhotoLifecycle.reconcile(
            items: &items,
            tempPhotosDir: env.tempDir,
            vault: env.vault,
            rows: rows,
            index: idx,
            hasSnapshot: true
        )
        XCTAssertFalse(FileManager.default.fileExists(atPath: attachedTemp.path))
        guard let vaultRec = env.vault.photos(for: parcel).first else {
            return XCTFail("Ảnh đã xác nhận phải nằm trong kho")
        }
        let vaultPhotoURL = env.vault.photoURL(for: vaultRec)
        XCTAssertTrue(FileManager.default.fileExists(atPath: vaultPhotoURL.path))

        // Hủy/xóa lượt quét đang chờ -> phải xóa luôn ảnh tạm của lượt đó (Quy tắc 4 T040)
        XCTAssertTrue(ScanPhotoLifecycle.deleteScanItem(requestId: "sc-del-pend", items: &items, tempPhotosDir: env.tempDir))
        XCTAssertFalse(FileManager.default.fileExists(atPath: pendingTemp.path))
        XCTAssertEqual(items.map { $0.requestId }, ["sc-del-att"])

        // Xóa luôn dòng lịch sử của lượt đã gắn -> ảnh trong kho `order-photos/` KHÔNG ĐƯỢC XÓA (Quy tắc 5 T040)
        XCTAssertTrue(ScanPhotoLifecycle.deleteScanItem(requestId: "sc-del-att", items: &items, tempPhotosDir: env.tempDir))
        XCTAssertTrue(items.isEmpty)
        XCTAssertEqual(env.vault.photos(for: parcel).map { $0.id }, [vaultRec.id])
        XCTAssertTrue(FileManager.default.fileExists(atPath: vaultPhotoURL.path))
    }

    // MARK: - 6. Dọn ảnh tạm mồ côi khi mở app & bảo vệ ảnh đang tham chiếu / ảnh đã gắn trong kho

    func testStartupTempOrphanCleanupDeletesOnlyUnreferencedOldTempFilesAndProtectsVault() throws {
        let env = try makeEnv()
        let parcel = makeRow(c: "SPXVN800001", o: "ORD-800")

        guard case .success(let vaultRec) = env.vault.addPhoto(
            jpegData: fakeJPEG(size: 2048),
            for: parcel,
            createdAtMs: 1_700_000_000_000,
            source: .scan,
            scanRequestId: "sc-vault"
        ) else { return XCTFail("Phải tạo được ảnh trong kho") }

        let oldDate = Date(timeIntervalSinceNow: -600)

        // 1. Tệp mồ côi thực sự trong thư mục ảnh tạm (cũ > 120s, không có trong items, không có trong chỉ mục kho) -> PHẢI XÓA
        let orphanTemp = try writeTempPhoto(name: "orphan_temp_old.jpg", in: env.tempDir, size: 777)
        try FileManager.default.setAttributes([.modificationDate: oldDate], ofItemAtPath: orphanTemp.path)

        // 2. Tệp ảnh tạm đang được lượt quét chờ tham chiếu -> KHÔNG XÓA
        let activeTemp = try writeTempPhoto(name: "active_pending.jpg", in: env.tempDir, size: 888)
        try FileManager.default.setAttributes([.modificationDate: oldDate], ofItemAtPath: activeTemp.path)

        // 3. Tệp ảnh tạm có tên trùng với bản ghi trong chỉ mục kho -> KHÔNG XÓA (điều kiện VÀ trong mục 6 T040)
        let matchingVaultName = try writeTempPhoto(name: vaultRec.fileName, in: env.tempDir, size: 999)
        try FileManager.default.setAttributes([.modificationDate: oldDate], ofItemAtPath: matchingVaultName.path)

        // 4. Tệp mồ côi vừa tạo (< 120s) -> KHÔNG XÓA (chưa đủ tuổi an toàn)
        let freshTemp = try writeTempPhoto(name: "fresh_temp.jpg", in: env.tempDir, size: 555)

        let items = [
            ScanItem(requestId: "sc-active", code: "SPXVN800002", kind: .returnScan, time: 1_700_000_100_000, photoFile: "active_pending.jpg")
        ]

        // Nếu tệp lịch sử đọc lỗi (`scanItemsLoadedCleanly == false`) -> KHÔNG xóa bất kỳ tệp nào!
        let skippedReport = ScanPhotoLifecycle.cleanOrphanTempPhotos(
            tempPhotosDir: env.tempDir,
            items: [],
            vault: env.vault,
            scanItemsLoadedCleanly: false,
            minAgeSeconds: 120,
            now: Date()
        )
        XCTAssertNotNil(skippedReport.skippedReason)
        XCTAssertTrue(skippedReport.removedFiles.isEmpty)
        XCTAssertTrue(FileManager.default.fileExists(atPath: orphanTemp.path))

        // Khi nạp sạch cả chỉ mục kho lẫn lịch sử quét -> CHỈ xóa `orphan_temp_old.jpg`
        let report = ScanPhotoLifecycle.cleanOrphanTempPhotos(
            tempPhotosDir: env.tempDir,
            items: items,
            vault: env.vault,
            scanItemsLoadedCleanly: true,
            minAgeSeconds: 120,
            now: Date()
        )
        XCTAssertNil(report.skippedReason)
        XCTAssertEqual(report.removedFiles, ["orphan_temp_old.jpg"])
        XCTAssertEqual(report.freedBytes, 777)

        XCTAssertFalse(FileManager.default.fileExists(atPath: orphanTemp.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: activeTemp.path), "Ảnh tạm đang có lượt quét tham chiếu không được xóa")
        XCTAssertTrue(FileManager.default.fileExists(atPath: matchingVaultName.path), "Tệp trùng tên bản ghi kho không được xóa")
        XCTAssertTrue(FileManager.default.fileExists(atPath: freshTemp.path), "Tệp mới tạo chưa quá ngưỡng an toàn không được xóa")
        // Ảnh đã gắn trong thư mục kho `Documents/order-photos/` hoàn toàn không bị đụng tới!
        XCTAssertTrue(FileManager.default.fileExists(atPath: env.vault.photoURL(for: vaultRec).path))
    }

    func testStartupTempOrphanCleanupSkipsWhenVaultIndexIsCorrupted() throws {
        let env = try makeEnv()
        let orphanTemp = try writeTempPhoto(name: "orphan_when_corrupt.jpg", in: env.tempDir, size: 600)
        try FileManager.default.setAttributes([.modificationDate: Date(timeIntervalSinceNow: -600)], ofItemAtPath: orphanTemp.path)

        // Làm hỏng cả chỉ mục kho chính và dự phòng
        try Data("BROKEN".utf8).write(to: env.vault.indexURL)
        try Data("BROKEN_BAK".utf8).write(to: env.vault.backupIndexURL)
        let corruptVault = OrderPhotoDiskStore(directoryURL: env.vaultDir)
        XCTAssertFalse(corruptVault.indexLoadedCleanly)

        let report = ScanPhotoLifecycle.cleanOrphanTempPhotos(
            tempPhotosDir: env.tempDir,
            items: [],
            vault: corruptVault,
            scanItemsLoadedCleanly: true,
            minAgeSeconds: 0,
            now: Date()
        )
        XCTAssertNotNil(report.skippedReason)
        XCTAssertTrue(report.removedFiles.isEmpty)
        XCTAssertTrue(FileManager.default.fileExists(atPath: orphanTemp.path))
    }

    // MARK: - 7. Kiện đã đủ tối đa 5 ảnh -> không tự xóa ảnh cũ trong kho, bỏ ảnh tạm vượt trần

    func testConfirmedSuccessWhenParcelAlreadyHas5PhotosKeepsExisting5AndDiscardsExtraTemp() throws {
        let env = try makeEnv()
        let parcel = makeRow(c: "SPXVN900001", o: "ORD-900")
        let rows = [parcel]
        let idx = SnapshotIndex(rows: rows)

        var existingIds: [String] = []
        for i in 1...5 {
            guard case .success(let rec) = env.vault.addPhoto(
                jpegData: fakeJPEG(size: 800 + i),
                for: parcel,
                createdAtMs: 1_700_000_000_000 + i * 1000,
                source: .manualCamera
            ) else { return XCTFail() }
            existingIds.append(rec.id)
        }

        let extraTemp = try writeTempPhoto(name: "sc-6th.jpg", in: env.tempDir, size: 900)
        var it = ScanItem(requestId: "sc-6th", code: "SPXVN900001", kind: .returnScan, time: 1_700_000_999_000, photoFile: "sc-6th.jpg")
        it.apply(PcAck(requestId: "sc-6th", code: "SPXVN900001", outcome: "success", ackStage: "scan"))
        var items = [it]

        let report = ScanPhotoLifecycle.reconcile(
            items: &items,
            tempPhotosDir: env.tempDir,
            vault: env.vault,
            rows: rows,
            index: idx,
            hasSnapshot: true
        )

        XCTAssertEqual(report.discardedRequestIds, ["sc-6th"])
        XCTAssertNil(items[0].photoFile)
        XCTAssertFalse(FileManager.default.fileExists(atPath: extraTemp.path))
        XCTAssertEqual(env.vault.photos(for: parcel).map { $0.id }, existingIds, "5 ảnh cũ trong kho phải còn nguyên")
    }
}
