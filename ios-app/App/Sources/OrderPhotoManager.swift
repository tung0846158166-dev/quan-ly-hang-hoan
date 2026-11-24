import SwiftUI
import UIKit
import os
import ScannerCore

/// Bộ nén ảnh JPEG cho lưu trữ cục bộ trên iPhone (T039).
/// - Cạnh dài mục tiêu 1280 px, chất lượng khởi đầu 0.48 (~60-120 KB), zoom lên vẫn đọc rõ chữ số mã vận đơn.
/// - Nếu ảnh vượt ~150 KB: giảm dần chất lượng (tới tối thiểu 0.28); nếu vẫn vượt thì hạ cạnh dài tối đa tới 960 px (KHÔNG bao giờ dưới 960 px).
enum PhotoCompressor {
    static func compressForStorage(_ image: UIImage, config: PhotoCompressionConfig = .storageDefault) -> Data? {
        let w = Double(image.size.width)
        let h = Double(image.size.height)
        guard w > 0, h > 0 else { return nil }

        let dims = config.dimensionSteps(forLongestSide: max(w, h))
        let qualities = config.qualitySteps()
        var smallestData: Data? = nil

        for maxDim in dims {
            let target = PhotoCompressionConfig.scaledSize(width: w, height: h, maxDim: maxDim)
            let size = CGSize(width: target.width, height: target.height)
            let fmt = UIGraphicsImageRendererFormat.default()
            fmt.scale = 1
            fmt.opaque = true
            let rendered = UIGraphicsImageRenderer(size: size, format: fmt).image { _ in
                image.draw(in: CGRect(origin: .zero, size: size))
            }

            for q in qualities {
                guard let data = rendered.jpegData(compressionQuality: CGFloat(q)), !data.isEmpty else { continue }
                if smallestData == nil || data.count < (smallestData?.count ?? Int.max) {
                    smallestData = data
                }
                if data.count <= config.targetMaxBytes {
                    return data
                }
            }
        }
        return smallestData
    }

    static func compressCGImage(_ cg: CGImage, config: PhotoCompressionConfig = .storageDefault) -> Data? {
        compressForStorage(UIImage(cgImage: cg), config: config)
    }
}

/// Quản lý kho ảnh cục bộ (`Documents/order-photos/`) trên iPhone cho toàn bộ SwiftUI Views.
@MainActor
final class OrderPhotoManager: ObservableObject {
    @Published private(set) var revision: Int = 0
    @Published private(set) var storageSummary: PhotoStorageSummary = PhotoStorageSummary()
    @Published private(set) var lastOrphanCleanupLog: String? = nil

    private let store: OrderPhotoDiskStore
    private let logger = Logger(subsystem: "com.quanlyhanghoan.scannerv2", category: "OrderPhotos")
    private let thumbCache: NSCache<NSString, UIImage> = {
        let c = NSCache<NSString, UIImage>()
        c.countLimit = 160
        return c
    }()
    private let fullCache: NSCache<NSString, UIImage> = {
        let c = NSCache<NSString, UIImage>()
        c.countLimit = 24
        return c
    }()

    var corruptedIndexDetected: Bool { store.corruptedIndexDetected }

    init(directoryURL: URL? = nil) {
        let baseDir: URL
        if let custom = directoryURL {
            baseDir = custom
        } else {
            let docs = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first
                ?? FileManager.default.temporaryDirectory
            baseDir = docs.appendingPathComponent("order-photos", isDirectory: true)
        }
        self.store = OrderPhotoDiskStore(directoryURL: baseDir)
        if store.corruptedIndexDetected {
            logger.error("Phát hiện tệp chỉ mục ảnh cục bộ bị hỏng khi khởi động; đã giữ nguyên tệp JPEG và khóa dọn mồ côi.")
        }
        refreshStorage()
    }

    /// Danh sách ảnh của đúng kiện `row` (Quy tắc 1 & 2: không lẫn kiện anh em A/B/C).
    func photos(for row: OrderRow, snapshot: SnapshotStore) -> [OrderPhotoRecord] {
        guard !snapshot.isDemoMode else { return [] }
        let idx = snapshot.snapshotIndex
        return store.photos(
            for: row,
            existingRowIds: Set(idx.idToIndex.keys),
            trackingCodeCounts: idx.trackingCodeCounts
        )
    }

    func photoURL(for record: OrderPhotoRecord) -> URL {
        store.photoURL(for: record)
    }

    func photoURL(forPhotoId id: String) -> URL? {
        store.photoURL(forPhotoId: id)
    }

    func record(byId id: String) -> OrderPhotoRecord? {
        store.record(byId: id)
    }

    func thumbnail(for record: OrderPhotoRecord) -> UIImage? {
        let url = store.photoURL(for: record)
        return cachedImage(at: url, in: thumbCache)
    }

    func fullImage(at url: URL) -> UIImage? {
        cachedImage(at: url, in: fullCache)
    }

    private func cachedImage(at url: URL, in cache: NSCache<NSString, UIImage>) -> UIImage? {
        let key = url.path as NSString
        if let hit = cache.object(forKey: key) { return hit }
        guard let img = UIImage(contentsOfFile: url.path) else { return nil }
        cache.setObject(img, forKey: key)
        return img
    }

    /// Thêm ảnh chụp tay / chọn từ thư viện cho đúng kiện `row` (nén ở luồng nền theo cấu hình lưu trữ T039).
    func addManualPhoto(
        image: UIImage,
        for row: OrderRow,
        source: OrderPhotoSource,
        snapshot: SnapshotStore
    ) async -> Result<OrderPhotoRecord, OrderPhotoError> {
        guard !snapshot.isDemoMode else {
            return .failure(.ioFailure("Đang ở chế độ dữ liệu mẫu; tắt chế độ mẫu để lưu ảnh thật."))
        }
        let existingCount = photos(for: row, snapshot: snapshot).count
        if existingCount >= OrderPhotoDiskStore.maxPhotosPerParcel {
            return .failure(.limitReached(max: OrderPhotoDiskStore.maxPhotosPerParcel))
        }
        let compressed: Data? = await Task.detached(priority: .userInitiated) {
            PhotoCompressor.compressForStorage(image, config: .storageDefault)
        }.value
        guard let data = compressed, !data.isEmpty else {
            return .failure(.emptyData)
        }
        return saveData(data, for: row, createdAtMs: Int(Date().timeIntervalSince1970 * 1000), source: source, scanRequestId: nil, snapshot: snapshot)
    }

    /// Thêm ảnh từ dữ liệu thô (`PhotosPickerItem`): giải mã và nén gọn về chuẩn lưu trữ T039 trước khi lưu.
    func addManualPhotoData(
        _ rawData: Data,
        for row: OrderRow,
        source: OrderPhotoSource,
        snapshot: SnapshotStore
    ) async -> Result<OrderPhotoRecord, OrderPhotoError> {
        guard !snapshot.isDemoMode else {
            return .failure(.ioFailure("Đang ở chế độ dữ liệu mẫu; tắt chế độ mẫu để lưu ảnh thật."))
        }
        let existingCount = photos(for: row, snapshot: snapshot).count
        if existingCount >= OrderPhotoDiskStore.maxPhotosPerParcel {
            return .failure(.limitReached(max: OrderPhotoDiskStore.maxPhotosPerParcel))
        }
        let compressed: Data? = await Task.detached(priority: .userInitiated) {
            guard let img = UIImage(data: rawData) else { return nil }
            return PhotoCompressor.compressForStorage(img, config: .storageDefault)
        }.value
        guard let data = compressed, !data.isEmpty else {
            return .failure(.ioFailure("Không đọc được tệp ảnh đã chọn."))
        }
        return saveData(data, for: row, createdAtMs: Int(Date().timeIntervalSince1970 * 1000), source: source, scanRequestId: nil, snapshot: snapshot)
    }

    /// Đối soát ảnh tạm lúc quét (T040):
    /// - PC ACK thành công + khớp đúng 1 kiện -> lưu vào kho `Documents/order-photos/` rồi xóa tệp tạm.
    /// - Không tìm thấy / mơ hồ / lỗi / bị từ chối / trùng -> xóa tệp tạm ngay.
    /// - Chưa có kết quả -> giữ tệp tạm trong hàng đợi, không xóa.
    @discardableResult
    func reconcileScanPhotos(
        items: inout [ScanItem],
        tempPhotosDir: URL,
        snapshot: SnapshotStore
    ) -> ScanPhotoReconcileReport {
        guard !snapshot.isDemoMode else { return ScanPhotoReconcileReport() }
        let snapMs = snapshot.updatedAt.map { Int($0.timeIntervalSince1970 * 1000) }
        let report = ScanPhotoLifecycle.reconcile(
            items: &items,
            tempPhotosDir: tempPhotosDir,
            vault: store,
            rows: snapshot.rows,
            index: snapshot.snapshotIndex,
            hasSnapshot: snapshot.hasData,
            snapshotUpdatedAtMs: snapMs
        )
        if !report.attachedRequestIds.isEmpty {
            logger.info("Đã gắn \(report.attachedRequestIds.count) ảnh quét đã xác nhận vào kho đơn cục bộ.")
            notifyChanged()
        }
        if !report.discardedRequestIds.isEmpty {
            logger.info("Đã xóa \(report.discardedRequestIds.count) ảnh tạm của lượt quét không hợp lệ/mơ hồ/trùng/lỗi.")
        }
        return report
    }

    /// Dọn tệp ảnh tạm mồ côi trong `Documents/photos/` khi mở app (Quy tắc 6 T040):
    /// chỉ xóa tệp không còn lượt quét nào tham chiếu VÀ không nằm trong chỉ mục kho; chắc chắn mới xóa và ghi nhật ký.
    func runSafeTempOrphanCleanup(
        tempPhotosDir: URL,
        items: [ScanItem],
        scanItemsLoadedCleanly: Bool
    ) {
        let report = ScanPhotoLifecycle.cleanOrphanTempPhotos(
            tempPhotosDir: tempPhotosDir,
            items: items,
            vault: store,
            scanItemsLoadedCleanly: scanItemsLoadedCleanly,
            minAgeSeconds: 120
        )
        if let skipped = report.skippedReason {
            logger.notice("Dọn ảnh tạm mồ côi: \(skipped, privacy: .public)")
            if lastOrphanCleanupLog == nil {
                lastOrphanCleanupLog = skipped
            }
        } else if !report.removedFiles.isEmpty {
            let mb = PhotoStorageSummary.formatMB(report.freedBytes)
            let msg = "Đã dọn \(report.removedFiles.count) tệp ảnh tạm mồ côi (\(mb))."
            logger.info("\(msg, privacy: .public) Tệp: \(report.removedFiles.joined(separator: ", "), privacy: .public)")
            if let existing = lastOrphanCleanupLog, !existing.isEmpty {
                lastOrphanCleanupLog = existing + " " + msg
            } else {
                lastOrphanCleanupLog = msg
            }
        }
    }

    private func saveData(
        _ data: Data,
        for row: OrderRow,
        createdAtMs: Int,
        source: OrderPhotoSource,
        scanRequestId: String?,
        snapshot: SnapshotStore
    ) -> Result<OrderPhotoRecord, OrderPhotoError> {
        let idx = snapshot.snapshotIndex
        let res = store.addPhoto(
            jpegData: data,
            for: row,
            createdAtMs: createdAtMs,
            source: source,
            scanRequestId: scanRequestId,
            existingRowIds: Set(idx.idToIndex.keys),
            trackingCodeCounts: idx.trackingCodeCounts
        )
        if case .success = res {
            notifyChanged()
        }
        return res
    }

    @discardableResult
    func deletePhoto(id: String) -> Bool {
        if let rec = store.record(byId: id) {
            let key = store.photoURL(for: rec).path as NSString
            thumbCache.removeObject(forKey: key)
            fullCache.removeObject(forKey: key)
        }
        let ok = store.deletePhoto(id: id)
        if ok { notifyChanged() }
        return ok
    }

    /// Xem trước số ảnh/MB của các đơn ĐÃ HOÀN TẤT cũ hơn `olderThanDays` ngày (chỉ xem trước, chưa xóa).
    func previewCleanupCompletedOrders(snapshot: SnapshotStore, olderThanDays: Int) -> PhotoCleanupPreview {
        guard !snapshot.isDemoMode, snapshot.hasData else { return PhotoCleanupPreview() }
        let nowMs = Int(Date().timeIntervalSince1970 * 1000)
        return store.previewCleanupCompletedOrders(
            rows: snapshot.rows,
            index: snapshot.snapshotIndex,
            olderThanDays: olderThanDays,
            nowMs: nowMs
        )
    }

    /// Xóa ảnh theo danh sách đã được người dùng xác nhận.
    @discardableResult
    func executeCleanup(recordIds: [String]) -> PhotoCleanupResult {
        for id in recordIds {
            if let rec = store.record(byId: id) {
                let key = store.photoURL(for: rec).path as NSString
                thumbCache.removeObject(forKey: key)
                fullCache.removeObject(forKey: key)
            }
        }
        let res = store.executeCleanup(recordIds: recordIds)
        if res.deletedCount > 0 {
            logger.info("Người dùng đã dọn \(res.deletedCount) ảnh đơn hoàn tất, giải phóng \(res.freedBytes) byte.")
            notifyChanged()
        }
        return res
    }

    /// Dọn tệp JPEG mồ côi trong `Documents/order-photos/` CHỈ KHI chỉ mục đã nạp sạch và tệp đủ cũ; luôn ghi nhật ký.
    func runSafeOrphanCleanup() {
        let report = store.cleanOrphanFiles(minAgeSeconds: 120)
        if let skipped = report.skippedReason {
            logger.notice("Dọn tệp ảnh mồ côi: \(skipped, privacy: .public)")
            lastOrphanCleanupLog = skipped
        } else if !report.removedFiles.isEmpty {
            let mb = PhotoStorageSummary.formatMB(report.freedBytes)
            let msg = "Đã dọn \(report.removedFiles.count) tệp ảnh mồ côi (\(mb))."
            logger.info("\(msg, privacy: .public) Tệp: \(report.removedFiles.joined(separator: ", "), privacy: .public)")
            lastOrphanCleanupLog = msg
            refreshStorage()
        }
    }

    func refreshStorage() {
        storageSummary = store.storageSummary()
    }

    private func notifyChanged() {
        refreshStorage()
        revision += 1
    }
}
