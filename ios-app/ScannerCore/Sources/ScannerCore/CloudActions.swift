import Foundation

// T042: phần LOGIC (test được) để app iPhone chạy theo luồng cloud: thao tác sửa đơn, cách hiển thị kết quả quét,
// trạng thái kết nối, mã QR ghép. Giao diện SwiftUI (ios-app/App) chỉ gọi các hàm này.

/// Các thao tác ghi gửi lên cloud (`POST /v1/ops`, cloud/API.md). `id` là mã kiện trên cloud (= `OrderRow.id` khi chạy cloud).
public enum CloudOps {
    public static let received = "Hàng đã trả"
    public static let notReturned = "Chưa trả"

    public static func setStatus(id: String, status: String) -> [String: Any] { ["type": "setStatus", "id": id, "status": status] }
    /// Theo dõi đơn + lý do (ở app là "ghi chú theo dõi").
    public static func setWatch(id: String, on: Bool, reason: String) -> [String: Any] { ["type": "setWatch", "id": id, "on": on, "reason": reason] }
    public static func delete(id: String) -> [String: Any] { ["type": "delete", "id": id] }
    /// Khiếu nại: "" = bỏ khiếu nại (cloud xóa cả ngày + ghi chú); đặt lần đầu cloud tự ghi ngày khiếu nại.
    public static let complaintStatuses = ["Đang xử lý", "Thành công", "Thất bại"]
    public static func complaint(id: String, status: String) -> [String: Any] { ["type": "editCell", "id": id, "field": "complaintStatus", "value": status] }
    public static func complaintNote(id: String, note: String) -> [String: Any] { ["type": "editCell", "id": id, "field": "complaintNote", "value": note] }
    /// Tạo đơn mới (quét chế độ "Đơn mới"): cloud tự nhận sàn/hãng; mã đã có ở bất kỳ đâu thì KHÔNG tạo lại.
    public static func newOrder(code: String) -> [String: Any] { ["type": "newOrder", "code": code, "origin": "phone"] }
    /// Đơn hủy: đánh dấu đã nhận / chưa nhận.
    public static func cancelReceive(id: String, received: Bool) -> [String: Any] { setStatus(id: id, status: received ? Self.received : Self.notReturned) }
}

extension CloudSyncClient {
    /// Gửi MỘT thao tác ghi. Mất kết nối thì từ chối ngay (không hàng đợi ngoại tuyến). Thành công thì lấy phần thay đổi về ngay.
    /// Trả nil khi thành công, ngược lại là thông báo tiếng Việt.
    public func mutate(_ operation: [String: Any]) async -> String? {
        guard canWrite, let a = currentAPI else { return "Mất kết nối cloud: chưa thể lưu. Kiểm tra mạng rồi thử lại." }
        return await CloudSyncClient.perform(operation, api: a, refresh: { [weak self] in
            guard let self else { return }
            try await self.pull(a)
        })
    }

    /// Phần lõi của `mutate` (tách ra để test không cần WebSocket).
    public static func perform(_ operation: [String: Any], api: CloudAPI, refresh: () async throws -> Void) async -> String? {
        do {
            let r = try await api.op(operation)
            guard r.ok else { return r.errorMessage ?? "Cloud từ chối thao tác." }
            try? await refresh()
            return nil
        } catch let e as CloudAPIError {
            return e.isNetwork ? "Mất kết nối cloud giữa chừng: chưa chắc đã lưu. Kiểm tra lại đơn rồi thử lại." : e.message
        } catch {
            return "Không lưu được. Thử lại."
        }
    }
}

/// Kết quả tạo đơn mới qua cloud.
public struct CloudNewOrderResult: Equatable {
    public let outcome: String      // success | duplicate | invalid | error
    public let message: String
    public let platform: String
    public let orderId: String
    public init(outcome: String, message: String, platform: String = "", orderId: String = "") {
        self.outcome = outcome; self.message = message; self.platform = platform; self.orderId = orderId
    }
    /// Lượt quét: thành công = đã tạo; đã có = trùng (không tạo lại); còn lại = lỗi cần kiểm tra.
    public var status: ScanStatus { outcome == "success" ? .success : outcome == "duplicate" ? .duplicate : .error }
    public var tone: CloudTone { outcome == "success" ? .success : .danger }
}

extension CloudSyncClient {
    /// Tạo đơn mới qua cloud. Mất kết nối thì từ chối ngay (không hàng đợi ngoại tuyến).
    public func newOrder(code: String) async -> CloudNewOrderResult {
        guard canWrite, let a = currentAPI else { return CloudNewOrderResult(outcome: "error", message: "Mất kết nối cloud: chưa tạo được đơn. Kiểm tra mạng rồi thử lại.") }
        return await CloudSyncClient.performNewOrder(code: code, api: a, refresh: { [weak self] in
            guard let self else { return }
            try await self.pull(a)
        })
    }

    /// Phần lõi của `newOrder` (tách ra để test không cần WebSocket).
    public static func performNewOrder(code: String, api: CloudAPI, refresh: () async throws -> Void) async -> CloudNewOrderResult {
        do {
            let r = try await api.op(CloudOps.newOrder(code: code))
            guard r.ok else { return CloudNewOrderResult(outcome: "error", message: r.errorMessage ?? "Cloud từ chối tạo đơn.") }
            let x = r.result ?? [:]
            let res = CloudNewOrderResult(outcome: x["outcome"] as? String ?? "error", message: x["message"] as? String ?? "",
                                          platform: x["platform"] as? String ?? "", orderId: x["orderId"] as? String ?? "")
            if res.outcome == "success" { try? await refresh() }
            return res
        } catch let e as CloudAPIError {
            return CloudNewOrderResult(outcome: "error", message: e.isNetwork ? "Mất kết nối cloud giữa chừng: chưa chắc đã tạo. Tìm lại mã trước khi quét lại." : e.message)
        } catch {
            return CloudNewOrderResult(outcome: "error", message: "Không tạo được đơn. Thử lại.")
        }
    }
}

/// Màu gợi ý cho giao diện (app tự đổi sang màu của Theme).
public enum CloudTone: String, Equatable { case success, info, warning, danger, neutral }

/// Kết quả quét qua cloud -> trạng thái lượt quét + câu báo. Chỉ `received` là đã nhận (cloud đã ghi bền, quy tắc 6).
public enum CloudScanPresentation {
    public struct View: Equatable {
        public let status: ScanStatus
        public let text: String
        public let tone: CloudTone
        public let confirmed: Bool
        public let reviewRequired: Bool
    }

    /// "26/11/2026 13:58:07" -> "13:58 26/11" (gọn cho dòng kết quả trên camera); dạng khác giữ nguyên.
    public static func shortTime(_ s: String) -> String {
        let t = s.trimmingCharacters(in: .whitespaces)
        guard let re = try? NSRegularExpression(pattern: #"^(\d{1,2})/(\d{1,2})/\d{4}\s+(\d{1,2}):(\d{2})"#),
              let m = re.firstMatch(in: t, range: NSRange(t.startIndex..., in: t)) else { return t }
        func g(_ i: Int) -> String { Range(m.range(at: i), in: t).map { String(t[$0]) } ?? "" }
        return "\(g(3)):\(g(4)) \(g(1))/\(g(2))"
    }

    public static func map(_ r: CloudScanResult) -> View {
        switch r.outcome {
        case .received:
            return View(status: .success, text: "Đã nhận hoàn kho", tone: .success, confirmed: true, reviewRequired: false)
        case .alreadyReceived:
            let who = [r.receivedBy, r.receivedAt.map(shortTime)].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " lúc ")
            return View(status: .duplicate, text: who.isEmpty ? "Quét trùng: kiện đã nhận trước đó" : "Quét trùng: đã nhận bởi \(who)",
                        tone: .danger, confirmed: true, reviewRequired: false)
        case .ambiguous:
            return View(status: .ambiguous, text: r.message.isEmpty ? "Trùng nhiều kiện, cần kiểm tra thủ công" : r.message,
                        tone: .danger, confirmed: true, reviewRequired: true)
        case .notFound:
            return View(status: .notFound, text: "Không tìm thấy kiện trên cloud", tone: .warning, confirmed: true, reviewRequired: false)
        case .needsManualCheck:
            return View(status: .ambiguous, text: r.message.isEmpty ? "Cần kiểm tra thủ công" : r.message, tone: .warning, confirmed: true, reviewRequired: true)
        case .error:
            // không chắc cloud đã nhận: KHÔNG coi là xác nhận
            return View(status: .error, text: r.message.isEmpty ? "Không quét được. Thử lại." : r.message, tone: .danger, confirmed: false, reviewRequired: true)
        }
    }
}

/// Chữ + màu + quyền ghi theo trạng thái kết nối (dải trạng thái trên cùng mọi tab).
public enum CloudStatusText {
    public static func label(_ s: CloudState) -> String {
        switch s {
        case .connected: return "Đã kết nối cloud"
        case .syncing: return "Đang đồng bộ"
        case .connecting: return "Đang kết nối..."
        case .disconnected: return "MẤT KẾT NỐI"
        case .unpaired: return "Chưa ghép thiết bị"
        case .revoked: return "Thiết bị đã bị thu hồi"
        }
    }

    public static func tone(_ s: CloudState) -> CloudTone {
        switch s {
        case .connected: return .success
        case .syncing: return .info
        case .connecting: return .warning
        case .disconnected, .revoked: return .danger
        case .unpaired: return .neutral
        }
    }

    /// Câu giải thích khi bị khóa ghi (nil = ghi được).
    public static func lockReason(_ s: CloudState) -> String? {
        switch s {
        case .connected, .syncing: return nil
        case .connecting: return "Đang kết nối cloud: chờ một chút rồi quét."
        case .disconnected: return "Mất kết nối cloud: không quét/sửa được. Dữ liệu đang xem là bản đã tải, chỉ xem."
        case .unpaired: return "Chưa ghép thiết bị với cloud (Cài đặt > Cloud)."
        case .revoked: return "Thiết bị đã bị chủ cửa hàng thu hồi: ghép lại trong Cài đặt > Cloud."
        }
    }

    /// Câu hạn mức cho chủ cửa hàng (nil = bình thường).
    public static func quotaNote(_ q: CloudQuota?) -> String? {
        guard let q else { return nil }
        if q.level == "stop" { return "Hết hạn mức hôm nay: thao tác ghi tạm dừng tới 07:00 sáng mai (giờ Việt Nam)." }
        if q.level == "warn" { return "Sắp hết hạn mức hôm nay (\(Int(q.rowsWrittenPercent.rounded()))% lượt ghi)." }
        return nil
    }
}

/// Ảnh cloud lưu tạm trong thư mục cache của điện thoại (để xem ảnh máy khác chụp). Tải một lần, lần sau đọc tệp.
public enum CloudPhotoFiles {
    public static func url(dir: URL, photoId: String, thumb: Bool) -> URL {
        let safe = String(photoId.map { $0.isLetter || $0.isNumber || $0 == "_" || $0 == "-" ? $0 : "_" })
        return dir.appendingPathComponent((thumb ? "t_" : "p_") + safe + ".img")
    }

    /// Có sẵn tệp thì trả luôn (không gọi mạng); chưa có thì tải và ghi nguyên tử.
    public static func fetch(api: CloudAPI, dir: URL, photoId: String, thumb: Bool) async throws -> URL {
        let u = url(dir: dir, photoId: photoId, thumb: thumb)
        if FileManager.default.fileExists(atPath: u.path) { return u }
        let data = try await api.photo(photoId: photoId, thumb: thumb)
        guard !data.isEmpty else { throw CloudAPIError(code: "not_found", message: "Ảnh trống.", status: 404) }
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        try data.write(to: u, options: .atomic)
        return u
    }

    /// Xóa ảnh tạm cũ hơn `days` ngày (cloud cũng tự xóa ảnh sau 90 ngày).
    @discardableResult
    public static func prune(dir: URL, olderThanDays days: Int, now: Date = Date()) -> Int {
        guard let files = try? FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: [.contentModificationDateKey]) else { return 0 }
        var n = 0
        for f in files {
            let d = (try? f.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? now
            if now.timeIntervalSince(d) > Double(days) * 86_400, (try? FileManager.default.removeItem(at: f)) != nil { n += 1 }
        }
        return n
    }
}

/// Mã QR ghép thiết bị: `qlhh://pair?u=<địa chỉ Worker>&c=<mã 6 số>`.
public enum CloudPairLink {
    public static func parse(_ raw: String) -> (url: String, code: String)? {
        guard let comps = URLComponents(string: raw.trimmingCharacters(in: .whitespacesAndNewlines)),
              comps.scheme?.lowercased() == "qlhh", comps.host?.lowercased() == "pair" else { return nil }
        let items = comps.queryItems ?? []
        guard let u = items.first(where: { $0.name == "u" })?.value, !u.isEmpty,
              let c = items.first(where: { $0.name == "c" })?.value, c.count == 6, c.allSatisfy({ $0.isNumber }),
              (try? CloudAPI.normalize(u)) != nil else { return nil }
        return (u, c)
    }

    public static func make(url: String, code: String) -> String {
        var comps = URLComponents()
        comps.scheme = "qlhh"; comps.host = "pair"
        comps.queryItems = [URLQueryItem(name: "u", value: url), URLQueryItem(name: "c", value: code)]
        return comps.string ?? ""
    }
}
