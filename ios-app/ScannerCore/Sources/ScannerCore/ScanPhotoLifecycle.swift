import Foundation

/// Lý do loại bỏ ảnh tạm của một lượt quét theo quy tắc T040.
public enum ScanPhotoDiscardReason: String, Equatable, Sendable {
    case notFound       // Không tìm thấy mã
    case ambiguous      // Mơ hồ (nhiều ứng viên, Quy tắc 3)
    case error          // Lỗi xử lý từ PC
    case rejected       // PC từ chối dứt khoát (invalid / locked)
    case duplicate      // Quét trùng kiện đã nhận trước đó
    case limitReached   // Kiện đã đủ tối đa 5 ảnh (không tự xóa ảnh cũ, bỏ ảnh tạm mới)
}

/// Quyết định xử lý ảnh tạm của một lượt quét (T040).
public enum ScanPhotoDecision: Equatable {
    /// Lượt quét không có ảnh tạm cần xử lý.
    case noPhoto
    /// Chưa có kết quả (chờ PC ACK khi mất mạng, hoặc chờ snapshot mới hơn sau khi PC đã ACK thành công):
    /// giữ ảnh TẠM trong hàng đợi, KHÔNG xóa.
    case keepTemporary
    /// ĐÚNG = PC đã ACK quét thành công (Quy tắc 6) VÀ khớp duy nhất 1 kiện (Quy tắc 1 & 2):
    /// giữ ảnh và gắn vào kho ảnh của đúng kiện đó.
    case attach(OrderRow)
    /// KHÔNG ĐÚNG (không tìm thấy / mơ hồ / lỗi / bị từ chối / quét trùng):
    /// xóa ảnh tạm ngay (tệp và mọi tham chiếu).
    case discard(reason: ScanPhotoDiscardReason)
}

/// Báo cáo kết quả một lượt đối soát ảnh tạm (`ScanPhotoLifecycle.reconcile`).
public struct ScanPhotoReconcileReport: Equatable, Sendable {
    public var attachedRequestIds: [String] = []
    public var discardedRequestIds: [String] = []
    public var waitingForFreshSnapshot: Bool = false
    public var changed: Bool = false

    public init(
        attachedRequestIds: [String] = [],
        discardedRequestIds: [String] = [],
        waitingForFreshSnapshot: Bool = false,
        changed: Bool = false
    ) {
        self.attachedRequestIds = attachedRequestIds
        self.discardedRequestIds = discardedRequestIds
        self.waitingForFreshSnapshot = waitingForFreshSnapshot
        self.changed = changed
    }
}

/// Vòng đời ảnh tạm lúc quét (T040 — sửa đè mục 4 & 8 của T039):
/// 1. ĐÚNG (PC ACK thành công + khớp duy nhất 1 kiện) -> chuyển vào `Documents/order-photos/` (khóa theo `OrderRow.id`).
/// 2. Không tìm thấy / mơ hồ (Quy tắc 3) / lỗi / bị từ chối / quét trùng -> XOÁ ảnh tạm ngay (tệp + tham chiếu).
/// 3. Chưa có kết quả (mất mạng / chờ PC ACK) -> giữ ảnh tạm; treo quá 7 ngày thì cảnh báo cho người dùng quyết định
///    (KHÔNG tự xóa lượt quét theo Quy tắc 8; chỉ xóa ẢNH tạm khi người dùng chọn bỏ).
/// 4. Người dùng hủy/xóa lượt quét ở Lịch sử -> xóa luôn ảnh tạm của lượt đó (KHÔNG đụng ảnh đã gắn trong kho).
/// 5. Dọn khi mở app -> quét thư mục ảnh tạm, xóa tệp không còn lượt quét nào tham chiếu VÀ không nằm trong chỉ mục kho
///    (chỉ xóa khi chắc chắn cả chỉ mục kho lẫn danh sách lượt quét đều nạp sạch).
public enum ScanPhotoLifecycle {
    public static let stalePendingDays: Int = 7
    public static let stalePendingThresholdMs: Int = stalePendingDays * 86_400_000

    /// Đánh giá quyết định cho ảnh tạm của một lượt quét.
    public static func evaluateDecision(
        for item: ScanItem,
        rows: [OrderRow],
        index: SnapshotIndex,
        hasSnapshot: Bool,
        snapshotUpdatedAtMs: Int? = nil
    ) -> ScanPhotoDecision {
        guard let file = item.photoFile, !file.isEmpty else {
            return .noPhoto
        }

        // Quy tắc 2 (T040): Không tìm thấy, mơ hồ, quét trùng, lỗi, bị từ chối -> XOÁ ảnh tạm ngay (không cần chờ snapshot).
        switch item.status {
        case .notFound:
            return .discard(reason: .notFound)
        case .ambiguous:
            return .discard(reason: .ambiguous)
        case .duplicate:
            return .discard(reason: .duplicate)
        case .error:
            return .discard(reason: (item.scanConfirmed && item.reviewRequired) ? .rejected : .error)
        case .pending:
            break
        case .success:
            break
        }

        if item.scanConfirmed && item.reviewRequired {
            return .discard(reason: .rejected)
        }

        // Quy tắc 3 (T040) & Quy tắc 6: Chưa có PC ACK thành công -> giữ ảnh TẠM, tuyệt đối không gắn sớm và không xóa.
        guard item.scanConfirmed && item.status == .success else {
            return .keepTemporary
        }

        // PC đã ACK thành công -> chỉ gắn vào kho khi khớp DUY NHẤT 1 kiện trong snapshot.
        guard hasSnapshot else {
            return .keepTemporary
        }

        switch index.matchParcel(for: item.code, in: rows) {
        case .unique(let row):
            return .attach(row)
        case .ambiguous:
            // Quy tắc 3: nhiều ứng viên -> không đoán, không giữ chờ gắn tay -> xóa ảnh tạm.
            return .discard(reason: .ambiguous)
        case .notFound:
            // Nếu snapshot hiện có trên điện thoại cũ hơn thời điểm quét (đơn mới nhập trên PC chưa kịp đẩy delta),
            // giữ ảnh tạm chờ snapshot mới hơn; khi snapshot đã >= thời điểm quét mà vẫn không có -> xóa ảnh tạm.
            if let snapMs = snapshotUpdatedAtMs, snapMs < item.time {
                return .keepTemporary
            }
            return .discard(reason: .notFound)
        }
    }

    /// Đối soát toàn bộ ảnh tạm của danh sách `items`:
    /// - Gắn vào kho `vault` nếu ĐÚNG (PC ACK thành công + khớp duy nhất 1 kiện) rồi mới xóa tệp tạm.
    /// - Xóa tệp tạm + mọi tham chiếu nếu kết quả không đúng (không tìm thấy / mơ hồ / lỗi / bị từ chối / trùng).
    /// - Giữ nguyên ảnh tạm nếu đang chờ PC ACK (mất mạng) hoặc chờ snapshot mới hơn.
    /// - TUYỆT ĐỐI không xóa ảnh đã gắn trong `vault`.
    @discardableResult
    public static func reconcile(
        items: inout [ScanItem],
        tempPhotosDir: URL,
        vault: OrderPhotoDiskStore,
        rows: [OrderRow],
        index: SnapshotIndex,
        hasSnapshot: Bool,
        snapshotUpdatedAtMs: Int? = nil,
        fileManager: FileManager = .default
    ) -> ScanPhotoReconcileReport {
        var report = ScanPhotoReconcileReport()
        let existingIds = Set(index.idToIndex.keys)

        for i in items.indices {
            guard let file = items[i].photoFile, !file.isEmpty else { continue }
            let tempURL = tempPhotosDir.appendingPathComponent(file)

            // Trường hợp bản ghi đã gắn thành công vào kho từ trước nhưng còn sót tham chiếu tệp tạm: dọn sạch tệp tạm.
            if let pid = items[i].attachedPhotoId, vault.record(byId: pid) != nil {
                try? fileManager.removeItem(at: tempURL)
                items[i].photoFile = nil
                report.changed = true
                continue
            }

            let decision = evaluateDecision(
                for: items[i],
                rows: rows,
                index: index,
                hasSnapshot: hasSnapshot,
                snapshotUpdatedAtMs: snapshotUpdatedAtMs
            )

            switch decision {
            case .noPhoto:
                continue

            case .keepTemporary:
                if items[i].scanConfirmed && items[i].status == .success {
                    report.waitingForFreshSnapshot = true
                }

            case .attach(let row):
                guard fileManager.fileExists(atPath: tempURL.path),
                      let data = try? Data(contentsOf: tempURL),
                      !data.isEmpty else {
                    // Tệp tạm đã mất hoặc rỗng -> dọn tham chiếu rỗng để không kẹt mãi.
                    items[i].photoFile = nil
                    report.changed = true
                    continue
                }
                let src: OrderPhotoSource = (items[i].photoAttempts > 0 || items[i].photoSentAt > 0) ? .migrated : .scan
                let addResult = vault.addPhoto(
                    jpegData: data,
                    for: row,
                    createdAtMs: items[i].time,
                    source: src,
                    scanRequestId: items[i].requestId,
                    existingRowIds: existingIds,
                    trackingCodeCounts: index.trackingCodeCounts
                )
                switch addResult {
                case .success(let rec):
                    items[i].attachedOrderId = row.id
                    items[i].attachedPhotoId = rec.id
                    items[i].photoFile = nil
                    items[i].photoAttachDismissed = false
                    // Chỉ xóa tệp tạm SAU KHI kho ảnh đã ghi nguyên tử thành công.
                    try? fileManager.removeItem(at: tempURL)
                    if items[i].detail.contains("gửi ngầm") || items[i].detail == "PC đã xác nhận." {
                        items[i].detail = "PC đã xác nhận. Ảnh đã lưu trong chi tiết đơn."
                    }
                    report.attachedRequestIds.append(items[i].requestId)
                    report.changed = true

                case .failure(.limitReached(let maxCount)):
                    // Không tự xóa ảnh cũ trong kho (Quy tắc 8); bỏ ảnh tạm vượt trần.
                    try? fileManager.removeItem(at: tempURL)
                    items[i].photoFile = nil
                    items[i].photoAttachDismissed = false
                    items[i].detail = "Kiện đã đủ tối đa \(maxCount) ảnh; ảnh quét mới không được lưu thêm."
                    report.discardedRequestIds.append(items[i].requestId)
                    report.changed = true

                case .failure(.emptyData):
                    try? fileManager.removeItem(at: tempURL)
                    items[i].photoFile = nil
                    items[i].photoAttachDismissed = false
                    report.discardedRequestIds.append(items[i].requestId)
                    report.changed = true

                case .failure(.ioFailure):
                    // Lỗi I/O tạm thời -> giữ nguyên tệp tạm để thử lại lần sau, tránh mất ảnh hợp lệ.
                    break
                }

            case .discard:
                try? fileManager.removeItem(at: tempURL)
                items[i].photoFile = nil
                items[i].attachedOrderId = nil
                items[i].attachedPhotoId = nil
                items[i].photoAttachDismissed = false
                if items[i].detail.contains("ảnh vẫn giữ trên điện thoại") {
                    items[i].detail = defaultDiscardDetail(for: items[i].status)
                }
                report.discardedRequestIds.append(items[i].requestId)
                report.changed = true
            }
        }

        return report
    }

    private static func defaultDiscardDetail(for status: ScanStatus) -> String {
        switch status {
        case .notFound: return "Không tìm thấy mã trên hệ thống."
        case .ambiguous: return "Trùng nhiều kiện, cần kiểm tra trên máy tính."
        case .duplicate: return "Mã này đã quét trước đó."
        case .error: return "PC không xác nhận được yêu cầu này."
        default: return "Không khớp duy nhất một kiện."
        }
    }

    // MARK: - Cảnh báo ảnh tạm treo quá 7 ngày (Quy tắc 3 T040 & Quy tắc 8 AGENTS)

    /// Danh sách lượt quét đang chờ kết quả đã treo quá `thresholdDays` ngày (mặc định 7 ngày) và vẫn còn tệp ảnh tạm trên đĩa.
    /// Chỉ dùng để cảnh báo cho người dùng quyết định; KHÔNG tự xóa lượt quét hay ảnh tạm.
    public static func stalePendingTempPhotoItems(
        in items: [ScanItem],
        tempPhotosDir: URL,
        nowMs: Int,
        thresholdDays: Int = stalePendingDays,
        fileManager: FileManager = .default
    ) -> [ScanItem] {
        let thresholdMs = max(1, thresholdDays) * 86_400_000
        return items.filter { it in
            guard let file = it.photoFile, !file.isEmpty else { return false }
            let stillWaiting = !it.scanConfirmed || (it.status == .success && it.attachedPhotoId == nil)
            guard stillWaiting else { return false }
            guard nowMs - it.time >= thresholdMs else { return false }
            return fileManager.fileExists(atPath: tempPhotosDir.appendingPathComponent(file).path)
        }
    }

    /// Tính số lượng và tổng dung lượng các tệp ảnh tạm đang thuộc danh sách lượt quét cho trước.
    public static func tempPhotoSummary(
        for items: [ScanItem],
        tempPhotosDir: URL,
        fileManager: FileManager = .default
    ) -> PhotoStorageSummary {
        var count = 0
        var total: Int64 = 0
        var seenFiles = Set<String>()
        for it in items {
            guard let file = it.photoFile, !file.isEmpty, seenFiles.insert(file).inserted else { continue }
            let url = tempPhotosDir.appendingPathComponent(file)
            guard let attrs = try? fileManager.attributesOfItem(atPath: url.path) else { continue }
            count += 1
            if let size = attrs[.size] as? NSNumber {
                total += max(0, size.int64Value)
            }
        }
        return PhotoStorageSummary(photoCount: count, totalBytes: total)
    }

    /// Người dùng chủ động chọn bỏ ẢNH TẠM của các lượt quét treo quá 7 ngày:
    /// - CHỈ xóa tệp ảnh tạm trong `tempPhotosDir` và đặt `photoFile = nil`.
    /// - TUYỆT ĐỐI KHÔNG xóa lượt quét (`ScanItem`) khỏi hàng đợi / lịch sử (Quy tắc 8).
    /// - TUYỆT ĐỐI KHÔNG chạm kho ảnh đã gắn (`Documents/order-photos/`).
    @discardableResult
    public static func discardTempPhotos(
        requestIds: Set<String>,
        items: inout [ScanItem],
        tempPhotosDir: URL,
        fileManager: FileManager = .default
    ) -> PhotoCleanupResult {
        guard !requestIds.isEmpty else { return PhotoCleanupResult(deletedCount: 0, freedBytes: 0) }
        var deleted = 0
        var freed: Int64 = 0

        for i in items.indices where requestIds.contains(items[i].requestId) {
            guard let file = items[i].photoFile, !file.isEmpty else { continue }
            let url = tempPhotosDir.appendingPathComponent(file)
            if let attrs = try? fileManager.attributesOfItem(atPath: url.path),
               let size = attrs[.size] as? NSNumber {
                freed += max(0, size.int64Value)
            }
            if fileManager.fileExists(atPath: url.path) {
                try? fileManager.removeItem(at: url)
                deleted += 1
            }
            items[i].photoFile = nil
            items[i].photoAttachDismissed = false
        }
        return PhotoCleanupResult(deletedCount: deleted, freedBytes: freed)
    }

    // MARK: - Hủy / xóa lượt quét ở Lịch sử (Quy tắc 4 & 5 T040)

    /// Người dùng hủy/xóa một lượt quét trong Lịch sử:
    /// - Xóa luôn ảnh tạm của lượt đó (nếu còn ở `tempPhotosDir`).
    /// - Xóa lượt quét khỏi `items`.
    /// - Ảnh đã gắn vào đơn đúng trong kho `Documents/order-photos/` KHÔNG bị xóa.
    @discardableResult
    public static func deleteScanItem(
        requestId: String,
        items: inout [ScanItem],
        tempPhotosDir: URL,
        fileManager: FileManager = .default
    ) -> Bool {
        guard let idx = items.firstIndex(where: { $0.requestId == requestId }) else { return false }
        let removed = items.remove(at: idx)
        if let file = removed.photoFile, !file.isEmpty {
            try? fileManager.removeItem(at: tempPhotosDir.appendingPathComponent(file))
        }
        return true
    }

    /// Người dùng xóa toàn bộ lịch sử của một loại quét (`kind`):
    /// - Xóa toàn bộ ảnh tạm thuộc các lượt đó; KHÔNG chạm kho ảnh đã gắn vào đơn.
    @discardableResult
    public static func clearScanItems(
        kind: ScanKind,
        items: inout [ScanItem],
        tempPhotosDir: URL,
        fileManager: FileManager = .default
    ) -> Int {
        var removedCount = 0
        for it in items where it.kind == kind {
            removedCount += 1
            if let file = it.photoFile, !file.isEmpty {
                try? fileManager.removeItem(at: tempPhotosDir.appendingPathComponent(file))
            }
        }
        items.removeAll { $0.kind == kind }
        return removedCount
    }

    // MARK: - Dọn tệp ảnh tạm mồ côi khi mở app (Quy tắc 6 T040)

    /// Quét thư mục ảnh tạm (`Documents/photos/`) khi mở app và chỉ xóa các tệp `.jpg`/`.jpeg`
    /// KHÔNG còn lượt quét nào tham chiếu (`item.photoFile`) VÀ KHÔNG nằm trong chỉ mục kho (`vault.records`).
    /// Chỉ chạy khi chắc chắn cả chỉ mục kho lẫn danh sách lượt quét đều đã nạp sạch (chắc chắn mới xóa).
    @discardableResult
    public static func cleanOrphanTempPhotos(
        tempPhotosDir: URL,
        items: [ScanItem],
        vault: OrderPhotoDiskStore,
        scanItemsLoadedCleanly: Bool,
        minAgeSeconds: TimeInterval = 120,
        now: Date = Date(),
        fileManager: FileManager = .default
    ) -> OrphanCleanupReport {
        guard scanItemsLoadedCleanly else {
            return OrphanCleanupReport(skippedReason: "Bỏ qua dọn ảnh tạm mồ côi vì tệp lịch sử quét chưa đọc sạch.")
        }
        guard vault.indexLoadedCleanly && !vault.corruptedIndexDetected else {
            return OrphanCleanupReport(skippedReason: "Bỏ qua dọn ảnh tạm mồ côi vì chỉ mục kho ảnh từng bị lỗi đọc.")
        }
        // Bảo vệ tuyệt đối: không bao giờ quét nhầm vào chính thư mục kho ảnh đã gắn.
        if tempPhotosDir.standardizedFileURL == vault.directoryURL.standardizedFileURL {
            return OrphanCleanupReport(skippedReason: "Thư mục ảnh tạm trùng thư mục kho ảnh.")
        }
        guard let urls = try? fileManager.contentsOfDirectory(
            at: tempPhotosDir,
            includingPropertiesForKeys: [.isRegularFileKey, .fileSizeKey, .contentModificationDateKey],
            options: [.skipsHiddenFiles]
        ) else {
            return OrphanCleanupReport(skippedReason: "Không đọc được thư mục ảnh tạm.")
        }

        let scanReferenced = Set(items.compactMap { $0.photoFile }.filter { !$0.isEmpty })
        let vaultReferenced = Set(vault.records.map { $0.fileName })

        var removed: [String] = []
        var freed: Int64 = 0

        for url in urls {
            let ext = url.pathExtension.lowercased()
            guard ext == "jpg" || ext == "jpeg" else { continue }
            let name = url.lastPathComponent
            if scanReferenced.contains(name) || vaultReferenced.contains(name) { continue }
            guard let vals = try? url.resourceValues(forKeys: [.isRegularFileKey, .fileSizeKey, .contentModificationDateKey]),
                  vals.isRegularFile == true else { continue }
            if let mod = vals.contentModificationDate, now.timeIntervalSince(mod) < minAgeSeconds {
                continue
            }
            let size = Int64(vals.fileSize ?? 0)
            do {
                try fileManager.removeItem(at: url)
                removed.append(name)
                freed += max(0, size)
            } catch {
                // Không xóa được thì bỏ qua an toàn.
            }
        }
        return OrphanCleanupReport(removedFiles: removed.sorted(), freedBytes: freed, skippedReason: nil)
    }
}
