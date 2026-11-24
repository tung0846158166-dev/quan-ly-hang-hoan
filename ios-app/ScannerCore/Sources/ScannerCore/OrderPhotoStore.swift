import Foundation
#if canImport(Darwin)
import Darwin
#elseif canImport(Glibc)
import Glibc
#endif

/// Cấu hình nén ảnh JPEG cho LƯU TRỮ CỤC BỘ trên iPhone (T039).
/// Mục tiêu: ~60-120 KB mỗi ảnh, cạnh dài ~1280 px để khi phóng to vẫn đọc rõ mã vận đơn & chữ số trên đơn;
/// nếu vượt ~150 KB thì hạ dần chất lượng và tối đa chỉ thu nhỏ tới 960 px (KHÔNG giảm cạnh dưới 960 px).
public struct PhotoCompressionConfig: Equatable, Sendable {
    public var maxDimension: Double
    public var minDimension: Double
    public var initialQuality: Double
    public var minQuality: Double
    public var qualityStep: Double
    public var targetMaxBytes: Int

    public init(
        maxDimension: Double = 1280,
        minDimension: Double = 960,
        initialQuality: Double = 0.48,
        minQuality: Double = 0.28,
        qualityStep: Double = 0.06,
        targetMaxBytes: Int = 150 * 1024
    ) {
        self.maxDimension = max(minDimension, maxDimension)
        self.minDimension = max(1, minDimension)
        self.initialQuality = min(1.0, max(0.1, initialQuality))
        self.minQuality = min(self.initialQuality, max(0.1, minQuality))
        self.qualityStep = max(0.01, qualityStep)
        self.targetMaxBytes = max(10 * 1024, targetMaxBytes)
    }

    public static let storageDefault = PhotoCompressionConfig()

    /// Tính kích thước đích giữ đúng tỷ lệ, không phóng to ảnh gốc nhỏ hơn `maxDim`.
    public static func scaledSize(width: Double, height: Double, maxDim: Double) -> (width: Int, height: Int) {
        guard width > 0, height > 0, maxDim > 0 else { return (1, 1) }
        let longest = max(width, height)
        let scale = min(1.0, maxDim / longest)
        return (
            width: max(1, Int((width * scale).rounded())),
            height: max(1, Int((height * scale).rounded()))
        )
    }

    /// Các bậc chất lượng JPEG thử lần lượt (giảm dần cho tới `minQuality`).
    public func qualitySteps() -> [Double] {
        var out: [Double] = []
        var q = initialQuality
        while q > minQuality + 0.001 {
            out.append((q * 100).rounded() / 100)
            q -= qualityStep
        }
        let roundedMin = (minQuality * 100).rounded() / 100
        if out.last != roundedMin {
            out.append(roundedMin)
        }
        return out
    }

    /// Các bậc cạnh dài thử khi ảnh quá lớn (> targetMaxBytes): thử `maxDimension` trước,
    /// nếu vẫn vượt mới thử `minDimension` (KHÔNG bao giờ giảm dưới `minDimension` = 960 px).
    public func dimensionSteps(forLongestSide longest: Double) -> [Double] {
        guard longest > 0 else { return [maxDimension] }
        let first = min(longest, maxDimension)
        if first > minDimension + 1 {
            return [first, minDimension]
        }
        return [first]
    }
}

/// Nguồn tạo ảnh cục bộ trên iPhone.
public enum OrderPhotoSource: String, Codable, Equatable, Sendable {
    case scan
    case manualCamera
    case manualLibrary
    case migrated
}

/// Bản ghi chỉ mục của một ảnh lưu trong `Documents/order-photos/`.
/// Quy tắc 1 & 2: khóa chính theo `orderRowId` (mã dòng kiện `OrderRow.id`), dự phòng theo mã vận đơn chuẩn hóa `trackingCode`;
/// TUYỆT ĐỐI KHÔNG khóa theo mã đơn gốc `OrderRow.o` (tránh lẫn ảnh giữa kiện A/B/C cùng đơn).
public struct OrderPhotoRecord: Codable, Identifiable, Equatable, Sendable {
    public var id: String
    public var orderRowId: String
    public var trackingCode: String
    public var fileName: String
    public var createdAtMs: Int
    public var byteSize: Int64
    public var source: OrderPhotoSource
    public var scanRequestId: String?

    public init(
        id: String,
        orderRowId: String,
        trackingCode: String,
        fileName: String,
        createdAtMs: Int,
        byteSize: Int64,
        source: OrderPhotoSource,
        scanRequestId: String? = nil
    ) {
        self.id = id
        self.orderRowId = orderRowId
        self.trackingCode = trackingCode
        self.fileName = fileName
        self.createdAtMs = createdAtMs
        self.byteSize = byteSize
        self.source = source
        self.scanRequestId = scanRequestId
    }
}

public enum OrderPhotoError: Error, Equatable, Sendable {
    case emptyData
    case limitReached(max: Int)
    case ioFailure(String)
}

/// Kết quả tự gắn ảnh từ lượt quét vào kiện trong snapshot.
public enum PhotoAttachOutcome: Equatable {
    case attached(OrderPhotoRecord, OrderRow)
    case ambiguous([OrderRow])
    case notFound
    case limitReached(OrderRow, max: Int)
    case failed(String)
}

/// Tóm tắt dung lượng kho ảnh cục bộ.
public struct PhotoStorageSummary: Equatable, Sendable {
    public var photoCount: Int
    public var totalBytes: Int64

    public init(photoCount: Int = 0, totalBytes: Int64 = 0) {
        self.photoCount = photoCount
        self.totalBytes = totalBytes
    }

    public var formattedMB: String {
        Self.formatMB(totalBytes)
    }

    public static func formatMB(_ bytes: Int64) -> String {
        guard bytes > 0 else { return "0,0 MB" }
        let mb = Double(bytes) / (1024.0 * 1024.0)
        if mb < 0.05 { return "< 0,1 MB" }
        return String(format: "%.1f MB", mb).replacingOccurrences(of: ".", with: ",")
    }
}

/// Xem trước số ảnh và dung lượng sẽ xóa khi người dùng bấm dọn ảnh đơn đã hoàn tất cũ hơn N ngày.
public struct PhotoCleanupPreview: Equatable, Sendable {
    public var recordIds: [String]
    public var photoCount: Int
    public var totalBytes: Int64

    public init(recordIds: [String] = [], photoCount: Int = 0, totalBytes: Int64 = 0) {
        self.recordIds = recordIds
        self.photoCount = photoCount
        self.totalBytes = totalBytes
    }

    public var formattedMB: String {
        PhotoStorageSummary.formatMB(totalBytes)
    }
}

public struct PhotoCleanupResult: Equatable, Sendable {
    public var deletedCount: Int
    public var freedBytes: Int64

    public init(deletedCount: Int, freedBytes: Int64) {
        self.deletedCount = deletedCount
        self.freedBytes = freedBytes
    }
}

public struct OrphanCleanupReport: Equatable, Sendable {
    public var removedFiles: [String]
    public var freedBytes: Int64
    public var skippedReason: String?

    public init(removedFiles: [String] = [], freedBytes: Int64 = 0, skippedReason: String? = nil) {
        self.removedFiles = removedFiles
        self.freedBytes = freedBytes
        self.skippedReason = skippedReason
    }
}

private struct OrderPhotoIndexEnvelope: Codable {
    var version: Int = 1
    var records: [OrderPhotoRecord]
}

/// Kho ảnh cục bộ lưu trong `Documents/order-photos/`:
/// - Mỗi ảnh 1 tệp JPEG ghi nguyên tử (ghi tệp tạm -> fsync -> rename).
/// - Một tệp chỉ mục `index.json` (kèm bản dự phòng `index.json.bak`) ghi nguyên tử.
/// - Chịu được tệp chỉ mục hỏng: tự khôi phục từ `.bak`, nếu cả hai hỏng thì bảo toàn toàn bộ tệp JPEG và không tự xóa mồ côi.
/// - Khóa theo `OrderRow.id` + dự phòng mã vận đơn chuẩn hóa (KHÔNG dùng mã đơn gốc `OrderRow.o`).
/// - Tối đa 5 ảnh mỗi kiện; không bao giờ tự xóa ảnh cũ (Quy tắc 8).
public final class OrderPhotoDiskStore {
    public static let maxPhotosPerParcel = 5
    public static let indexFileName = "index.json"
    public static let backupIndexFileName = "index.json.bak"

    public let directoryURL: URL
    public private(set) var records: [OrderPhotoRecord] = []
    /// `true` chỉ khi đọc chỉ mục ban đầu sạch (hoặc kho mới chưa có tệp chỉ mục nào); `false` nếu phát hiện chỉ mục hỏng.
    public private(set) var indexLoadedCleanly: Bool = true
    public private(set) var corruptedIndexDetected: Bool = false

    private let fm: FileManager

    public init(directoryURL: URL, fileManager: FileManager = .default) {
        self.directoryURL = directoryURL
        self.fm = fileManager
        try? fm.createDirectory(at: directoryURL, withIntermediateDirectories: true)
        loadIndex()
    }

    public var indexURL: URL { directoryURL.appendingPathComponent(Self.indexFileName) }
    public var backupIndexURL: URL { directoryURL.appendingPathComponent(Self.backupIndexFileName) }

    public func photoURL(for record: OrderPhotoRecord) -> URL {
        directoryURL.appendingPathComponent(record.fileName)
    }

    public func photoURL(forPhotoId id: String) -> URL? {
        guard let rec = records.first(where: { $0.id == id }) else { return nil }
        let url = photoURL(for: rec)
        return fm.fileExists(atPath: url.path) ? url : nil
    }

    public func record(byId id: String) -> OrderPhotoRecord? {
        records.first(where: { $0.id == id })
    }

    /// Chuẩn hóa mã vận đơn dùng làm khóa dự phòng cho một kiện (CHỈ lấy từ `row.c`, TUYỆT ĐỐI KHÔNG lấy từ `row.o`).
    public static func parcelTrackingKey(for row: OrderRow) -> String {
        let cleaned = TrackingCode.clean(row.c)
        return cleaned.uppercased()
    }

    /// Kiểm tra một đơn đã hoàn tất (đủ điều kiện để người dùng chủ động dọn ảnh cũ theo yêu cầu).
    public static func isCompletedOrder(_ row: OrderRow) -> Bool {
        guard !row.pd else { return false }
        if row.rv || row.isArchived { return true }
        let st = row.st.trimmingCharacters(in: .whitespacesAndNewlines)
        return st == "Hàng đã trả" || st == "Đã nhận" || st == "Không cần nhận"
    }

    /// Danh sách ảnh của ĐÚNG kiện `row` (sắp mới nhất trước hoặc cũ nhất trước theo thứ tự chụp tăng dần).
    /// - Ưu tiên khớp chính xác `record.orderRowId == row.id`.
    /// - Dự phòng khớp theo mã vận đơn chuẩn hóa `trackingCode` khi PC đổi `OrderRow.id` (vd đơn dời sang Kho lưu trữ `m|...` -> `a|...`),
    ///   với điều kiện bản ghi không thuộc một dòng khác đang tồn tại trong snapshot và mã vận đơn đó không bị dùng chung bởi >= 2 kiện.
    /// - TUYỆT ĐỐI KHÔNG khớp theo mã đơn gốc `row.o` (Quy tắc 1 & 2).
    public func photos(
        for row: OrderRow,
        existingRowIds: Set<String>? = nil,
        trackingCodeCounts: [String: Int]? = nil
    ) -> [OrderPhotoRecord] {
        let rowId = row.id
        let trackKey = Self.parcelTrackingKey(for: row)
        let allowTrackFallback: Bool
        if trackKey.isEmpty {
            allowTrackFallback = false
        } else if let counts = trackingCodeCounts {
            allowTrackFallback = (counts[trackKey] ?? 0) <= 1
        } else {
            allowTrackFallback = true
        }

        let matched = records.filter { rec in
            if rec.orderRowId == rowId { return true }
            guard allowTrackFallback, !rec.trackingCode.isEmpty, rec.trackingCode == trackKey else { return false }
            if let liveIds = existingRowIds, liveIds.contains(rec.orderRowId) {
                // Bản ghi đã thuộc về một dòng kiện khác còn sống trong snapshot -> không gán nhầm sang kiện này.
                return false
            }
            return true
        }
        return matched.sorted { $0.createdAtMs < $1.createdAtMs }
    }

    /// Thêm một ảnh JPEG cho đúng kiện `row`. Tối đa `maxPhotosPerParcel` (5) ảnh mỗi kiện; hết chỗ báo lỗi rõ, KHÔNG tự xóa ảnh cũ.
    @discardableResult
    public func addPhoto(
        jpegData: Data,
        for row: OrderRow,
        createdAtMs: Int,
        source: OrderPhotoSource,
        scanRequestId: String? = nil,
        existingRowIds: Set<String>? = nil,
        trackingCodeCounts: [String: Int]? = nil
    ) -> Result<OrderPhotoRecord, OrderPhotoError> {
        guard !jpegData.isEmpty else { return .failure(.emptyData) }

        // Chống gắn trùng cùng một lượt quét.
        if let rid = scanRequestId, !rid.isEmpty,
           let existing = records.first(where: { $0.scanRequestId == rid }) {
            let url = photoURL(for: existing)
            if fm.fileExists(atPath: url.path) {
                return .success(existing)
            }
        }

        let current = photos(for: row, existingRowIds: existingRowIds, trackingCodeCounts: trackingCodeCounts)
        if current.count >= Self.maxPhotosPerParcel {
            return .failure(.limitReached(max: Self.maxPhotosPerParcel))
        }

        do {
            try fm.createDirectory(at: directoryURL, withIntermediateDirectories: true)
        } catch {
            return .failure(.ioFailure("Không tạo được thư mục ảnh: \(error.localizedDescription)"))
        }

        let id = "ph_\(createdAtMs)_\(String(UUID().uuidString.prefix(8)).lowercased())"
        let fileName = id + ".jpg"
        let destURL = directoryURL.appendingPathComponent(fileName)

        do {
            try Self.atomicWrite(data: jpegData, to: destURL, fileManager: fm)
        } catch {
            return .failure(.ioFailure("Không ghi được tệp ảnh: \(error.localizedDescription)"))
        }

        let rec = OrderPhotoRecord(
            id: id,
            orderRowId: row.id,
            trackingCode: Self.parcelTrackingKey(for: row),
            fileName: fileName,
            createdAtMs: createdAtMs,
            byteSize: Int64(jpegData.count),
            source: source,
            scanRequestId: scanRequestId
        )

        records.append(rec)
        do {
            try persistIndex()
            return .success(rec)
        } catch {
            records.removeAll { $0.id == id }
            try? fm.removeItem(at: destURL)
            return .failure(.ioFailure("Không ghi được chỉ mục ảnh: \(error.localizedDescription)"))
        }
    }

    /// Tự gắn ảnh từ một lượt quét CHỈ KHI mã quét khớp DUY NHẤT 1 kiện trong snapshot (Quy tắc 1, 2, 3, 8).
    /// Nếu mã mơ hồ (>= 2 ứng viên) hoặc không tìm thấy -> KHÔNG lưu, KHÔNG đoán.
    public func attachIfUnique(
        jpegData: Data,
        rawCode: String,
        in rows: [OrderRow],
        index: SnapshotIndex,
        createdAtMs: Int,
        source: OrderPhotoSource = .scan,
        scanRequestId: String? = nil
    ) -> PhotoAttachOutcome {
        switch index.matchParcel(for: rawCode, in: rows) {
        case .notFound:
            return .notFound
        case .ambiguous(let candidates):
            return .ambiguous(candidates)
        case .unique(let row):
            let existingIds = Set(index.idToIndex.keys)
            switch addPhoto(
                jpegData: jpegData,
                for: row,
                createdAtMs: createdAtMs,
                source: source,
                scanRequestId: scanRequestId,
                existingRowIds: existingIds,
                trackingCodeCounts: index.trackingCodeCounts
            ) {
            case .success(let rec):
                return .attached(rec, row)
            case .failure(.limitReached(let max)):
                return .limitReached(row, max: max)
            case .failure(.emptyData):
                return .failed("Dữ liệu ảnh rỗng.")
            case .failure(.ioFailure(let msg)):
                return .failed(msg)
            }
        }
    }

    /// Xóa một ảnh theo `id` (do người dùng chủ động xác nhận). Lưu chỉ mục nguyên tử trước khi xóa tệp JPEG.
    @discardableResult
    public func deletePhoto(id: String) -> Bool {
        guard let idx = records.firstIndex(where: { $0.id == id }) else { return false }
        let removed = records.remove(at: idx)
        do {
            try persistIndex()
        } catch {
            records.insert(removed, at: idx)
            return false
        }
        try? fm.removeItem(at: photoURL(for: removed))
        return true
    }

    /// Tính số ảnh và tổng dung lượng thật trên đĩa của kho ảnh cục bộ.
    public func storageSummary() -> PhotoStorageSummary {
        var count = 0
        var total: Int64 = 0
        for rec in records {
            let url = photoURL(for: rec)
            if let attrs = try? fm.attributesOfItem(atPath: url.path),
               let size = attrs[.size] as? NSNumber {
                count += 1
                total += size.int64Value
            } else if fm.fileExists(atPath: url.path) {
                count += 1
                total += max(0, rec.byteSize)
            }
        }
        return PhotoStorageSummary(photoCount: count, totalBytes: total)
    }

    /// Tính trước danh sách ảnh của các đơn ĐÃ HOÀN TẤT cũ hơn `olderThanDays` ngày (KHÔNG xóa; chỉ xem trước cho hộp xác nhận).
    public func previewCleanupCompletedOrders(
        rows: [OrderRow],
        index: SnapshotIndex,
        olderThanDays: Int,
        nowMs: Int
    ) -> PhotoCleanupPreview {
        let days = max(1, olderThanDays)
        let cutoffMs = nowMs - days * 86_400_000
        let existingIds = Set(index.idToIndex.keys)
        var matchedIds: [String] = []
        var totalBytes: Int64 = 0

        // Ánh xạ nhanh rowId -> row và trackingCode duy nhất -> row.
        var rowByUniqueTrack: [String: OrderRow] = [:]
        for r in rows {
            let tk = Self.parcelTrackingKey(for: r)
            if !tk.isEmpty && (index.trackingCodeCounts[tk] ?? 0) == 1 {
                rowByUniqueTrack[tk] = r
            }
        }

        for rec in records {
            guard rec.createdAtMs < cutoffMs else { continue }
            let targetRow: OrderRow?
            if let exact = index.row(byId: rec.orderRowId, in: rows) {
                targetRow = exact
            } else if !existingIds.contains(rec.orderRowId), !rec.trackingCode.isEmpty {
                targetRow = rowByUniqueTrack[rec.trackingCode]
            } else {
                targetRow = nil
            }
            guard let row = targetRow, Self.isCompletedOrder(row) else { continue }

            let url = photoURL(for: rec)
            let actualSize: Int64
            if let attrs = try? fm.attributesOfItem(atPath: url.path), let s = attrs[.size] as? NSNumber {
                actualSize = s.int64Value
            } else {
                actualSize = max(0, rec.byteSize)
            }
            matchedIds.append(rec.id)
            totalBytes += actualSize
        }

        return PhotoCleanupPreview(recordIds: matchedIds, photoCount: matchedIds.count, totalBytes: totalBytes)
    }

    /// Thực hiện xóa danh sách ảnh đã được người dùng xác nhận từ `previewCleanupCompletedOrders`.
    @discardableResult
    public func executeCleanup(recordIds: [String]) -> PhotoCleanupResult {
        let idSet = Set(recordIds)
        guard !idSet.isEmpty else { return PhotoCleanupResult(deletedCount: 0, freedBytes: 0) }
        let targets = records.filter { idSet.contains($0.id) }
        guard !targets.isEmpty else { return PhotoCleanupResult(deletedCount: 0, freedBytes: 0) }

        let backup = records
        records.removeAll { idSet.contains($0.id) }
        do {
            try persistIndex()
        } catch {
            records = backup
            return PhotoCleanupResult(deletedCount: 0, freedBytes: 0)
        }

        var freed: Int64 = 0
        for rec in targets {
            let url = photoURL(for: rec)
            if let attrs = try? fm.attributesOfItem(atPath: url.path), let s = attrs[.size] as? NSNumber {
                freed += s.int64Value
            } else {
                freed += max(0, rec.byteSize)
            }
            try? fm.removeItem(at: url)
        }
        return PhotoCleanupResult(deletedCount: targets.count, freedBytes: freed)
    }

    /// Dọn tệp JPEG mồ côi (không có trong chỉ mục) CHỈ KHI chắc chắn:
    /// - Chỉ mục đã nạp sạch (`indexLoadedCleanly == true`, không có lỗi hỏng chỉ mục).
    /// - Chỉ xét tệp `.jpg` / `.jpeg` đã yên tĩnh ít nhất `minAgeSeconds` (tránh đụng tệp đang ghi).
    /// - Trả về báo cáo chi tiết các tệp đã dọn để ghi nhật ký.
    @discardableResult
    public func cleanOrphanFiles(minAgeSeconds: TimeInterval = 120, now: Date = Date()) -> OrphanCleanupReport {
        guard indexLoadedCleanly && !corruptedIndexDetected else {
            return OrphanCleanupReport(skippedReason: "Bỏ qua dọn tệp mồ côi vì chỉ mục ảnh từng bị lỗi đọc.")
        }
        guard let urls = try? fm.contentsOfDirectory(
            at: directoryURL,
            includingPropertiesForKeys: [.isRegularFileKey, .fileSizeKey, .contentModificationDateKey],
            options: [.skipsHiddenFiles]
        ) else {
            return OrphanCleanupReport(skippedReason: "Không đọc được thư mục ảnh cục bộ.")
        }

        let referenced = Set(records.map { $0.fileName })
        var removed: [String] = []
        var freed: Int64 = 0

        for url in urls {
            let ext = url.pathExtension.lowercased()
            guard ext == "jpg" || ext == "jpeg" else { continue }
            let name = url.lastPathComponent
            if referenced.contains(name) { continue }
            guard let vals = try? url.resourceValues(forKeys: [.isRegularFileKey, .fileSizeKey, .contentModificationDateKey]),
                  vals.isRegularFile == true else { continue }
            if let mod = vals.contentModificationDate, now.timeIntervalSince(mod) < minAgeSeconds {
                continue
            }
            let size = Int64(vals.fileSize ?? 0)
            do {
                try fm.removeItem(at: url)
                removed.append(name)
                freed += max(0, size)
            } catch {
                // Không xóa được thì bỏ qua an toàn.
            }
        }
        return OrphanCleanupReport(removedFiles: removed.sorted(), freedBytes: freed, skippedReason: nil)
    }

    // MARK: - Đọc / ghi chỉ mục nguyên tử & chịu lỗi tệp hỏng

    private func loadIndex() {
        let primaryExists = fm.fileExists(atPath: indexURL.path)
        let backupExists = fm.fileExists(atPath: backupIndexURL.path)

        if !primaryExists && !backupExists {
            records = []
            indexLoadedCleanly = true
            corruptedIndexDetected = false
            return
        }

        if primaryExists {
            if let loaded = decodeIndex(at: indexURL) {
                records = loaded
                indexLoadedCleanly = true
                corruptedIndexDetected = false
                return
            }
            // Tệp chính hỏng: lưu giữ lại để chẩn đoán/khôi phục, thử đọc bản dự phòng `.bak`.
            corruptedIndexDetected = true
            let stamp = Int(Date().timeIntervalSince1970)
            let quarantine = directoryURL.appendingPathComponent("index.json.corrupt.\(stamp)")
            try? fm.moveItem(at: indexURL, to: quarantine)
        }

        if backupExists, let recovered = decodeIndex(at: backupIndexURL) {
            records = recovered
            // Khôi phục được từ `.bak` -> ghi lại tệp chính nguyên tử, vẫn đánh dấu `corruptedIndexDetected = true` cho phiên này.
            try? persistIndex()
            indexLoadedCleanly = false
            return
        }

        // Cả tệp chính lẫn tệp dự phòng đều không đọc được: giữ nguyên mọi tệp JPEG, không tự xóa mồ côi!
        records = []
        indexLoadedCleanly = false
        corruptedIndexDetected = true
    }

    private func decodeIndex(at url: URL) -> [OrderPhotoRecord]? {
        guard let data = try? Data(contentsOf: url), !data.isEmpty else { return nil }
        if let env = try? JSONDecoder().decode(OrderPhotoIndexEnvelope.self, from: data) {
            return env.records
        }
        if let plain = try? JSONDecoder().decode([OrderPhotoRecord].self, from: data) {
            return plain
        }
        return nil
    }

    private func persistIndex() throws {
        let env = OrderPhotoIndexEnvelope(version: 1, records: records)
        let data = try JSONEncoder().encode(env)
        // 1. Ghi bản dự phòng trước (nếu tệp chính hiện có và hợp lệ), hoặc ghi thẳng tệp chính nguyên tử rồi cập nhật `.bak`.
        try Self.atomicWrite(data: data, to: indexURL, fileManager: fm)
        try? Self.atomicWrite(data: data, to: backupIndexURL, fileManager: fm)
    }

    /// Ghi nguyên tử: ghi vào tệp tạm cùng thư mục -> `synchronizeFile()` (fsync) -> `rename` POSIX.
    public static func atomicWrite(data: Data, to destination: URL, fileManager: FileManager = .default) throws {
        let dir = destination.deletingLastPathComponent()
        try fileManager.createDirectory(at: dir, withIntermediateDirectories: true)
        let tmpName = "." + destination.lastPathComponent + ".tmp." + UUID().uuidString
        let tmpURL = dir.appendingPathComponent(tmpName)
        do {
            try data.write(to: tmpURL, options: [])
            if let handle = try? FileHandle(forUpdating: tmpURL) {
                try? handle.synchronize()
                try? handle.close()
            }
            if rename(tmpURL.path, destination.path) != 0 {
                let code = errno
                try? fileManager.removeItem(at: tmpURL)
                throw NSError(domain: NSPOSIXErrorDomain, code: Int(code), userInfo: [
                    NSLocalizedDescriptionKey: "rename thất bại (errno \(code))"
                ])
            }
        } catch {
            try? fileManager.removeItem(at: tmpURL)
            throw error
        }
    }
}
