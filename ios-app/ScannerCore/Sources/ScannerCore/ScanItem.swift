import Foundation

public enum ScanStatus: String, Codable, Sendable {
    case pending, success, duplicate, notFound = "not_found", ambiguous, error
}

public struct ScanItem: Codable, Identifiable, Equatable, Sendable {
    public var id: String { requestId }
    public var requestId: String
    public var captureId: String
    public var code: String
    public var kind: ScanKind
    public var time: Int
    public var status: ScanStatus = .pending
    public var scanConfirmed = false
    public var photoConfirmed = false
    public var reviewRequired = false
    public var photoFile: String?
    public var photoSentAt: Int = 0
    public var photoAttempts: Int = 0
    public var lastSendAt: Int = 0
    public var attempts: Int = 0
    public var orderNum: String?
    public var detail: String = "Đang chờ máy tính xác nhận"
    /// Ghi chú gắn lúc quét đơn mới (lưu bền cùng lượt quét để gửi lại đúng ghi chú khi mất mạng).
    public var note: String = ""
    /// T039/T040: Mã dòng kiện (`OrderRow.id`) đã gắn ảnh cục bộ (nil nếu chưa gắn hoặc không có ảnh).
    public var attachedOrderId: String? = nil
    /// T039/T040: ID bản ghi ảnh trong `OrderPhotoDiskStore` sau khi chuyển vào `Documents/order-photos/`.
    public var attachedPhotoId: String? = nil
    /// Giữ trường cũ từ T039 để giải mã tương thích ngược với `scan_items.json` đã lưu; không còn dùng ở T040.
    public var photoAttachDismissed: Bool = false
    /// Kiện thuộc loại "Đơn hủy" trên hệ thống (PC báo trong ACK, hoặc suy từ danh sách trên điện thoại): đổi nhãn kết quả thành "Đơn hủy".
    public var isCancelledOrder: Bool = false

    public init(requestId: String, code: String, kind: ScanKind, time: Int, photoFile: String?, note: String = "") {
        self.requestId = requestId
        self.captureId = requestId
        self.code = code
        self.kind = kind
        self.time = time
        self.photoFile = photoFile
        self.note = note
    }

    private enum CodingKeys: String, CodingKey {
        case requestId, captureId, code, kind, time, status
        case scanConfirmed, photoConfirmed, reviewRequired
        case photoFile, photoSentAt, photoAttempts, lastSendAt, attempts
        case orderNum, detail, note
        case attachedOrderId, attachedPhotoId, photoAttachDismissed, isCancelledOrder
    }

    /// Giải mã tương thích ngược với tệp `scan_items.json` cũ (giữ các trường ảnh cũ để đọc không lỗi, thêm trường mới tùy chọn).
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let rid = try c.decode(String.self, forKey: .requestId)
        self.requestId = rid
        self.captureId = (try? c.decodeIfPresent(String.self, forKey: .captureId)) ?? rid
        self.code = try c.decode(String.self, forKey: .code)
        self.kind = (try? c.decode(ScanKind.self, forKey: .kind)) ?? .returnScan
        self.time = try c.decode(Int.self, forKey: .time)
        self.status = (try? c.decodeIfPresent(ScanStatus.self, forKey: .status)) ?? .pending
        self.scanConfirmed = (try? c.decodeIfPresent(Bool.self, forKey: .scanConfirmed)) ?? false
        self.photoConfirmed = (try? c.decodeIfPresent(Bool.self, forKey: .photoConfirmed)) ?? false
        self.reviewRequired = (try? c.decodeIfPresent(Bool.self, forKey: .reviewRequired)) ?? false
        self.photoFile = try? c.decodeIfPresent(String.self, forKey: .photoFile)
        self.photoSentAt = (try? c.decodeIfPresent(Int.self, forKey: .photoSentAt)) ?? 0
        self.photoAttempts = (try? c.decodeIfPresent(Int.self, forKey: .photoAttempts)) ?? 0
        self.lastSendAt = (try? c.decodeIfPresent(Int.self, forKey: .lastSendAt)) ?? 0
        self.attempts = (try? c.decodeIfPresent(Int.self, forKey: .attempts)) ?? 0
        self.orderNum = try? c.decodeIfPresent(String.self, forKey: .orderNum)
        self.detail = (try? c.decodeIfPresent(String.self, forKey: .detail)) ?? "Đang chờ máy tính xác nhận"
        self.note = (try? c.decodeIfPresent(String.self, forKey: .note)) ?? ""
        self.attachedOrderId = try? c.decodeIfPresent(String.self, forKey: .attachedOrderId)
        self.attachedPhotoId = try? c.decodeIfPresent(String.self, forKey: .attachedPhotoId)
        self.photoAttachDismissed = (try? c.decodeIfPresent(Bool.self, forKey: .photoAttachDismissed)) ?? false
        self.isCancelledOrder = (try? c.decodeIfPresent(Bool.self, forKey: .isCancelledOrder)) ?? false
    }

    /// T040: Lượt quét đang giữ ảnh tạm trong hàng đợi (chờ PC xác nhận hoặc chờ gắn vào kho đơn).
    public var hasTemporaryPhoto: Bool {
        if let f = photoFile, !f.isEmpty, attachedPhotoId == nil {
            return true
        }
        return false
    }

    /// T039: Lượt quét chỉ cần PC xác nhận lệnh quét (`scanConfirmed`) là xong; không còn chờ ACK ảnh từ PC.
    /// Quy tắc 6 vẫn giữ chặt cho lệnh quét: chỉ coi đã quét khi PC đã lưu bền và trả ACK.
    public var isSettled: Bool {
        scanConfirmed
    }

    /// Cập nhật theo phản hồi của PC. Trả về true nếu có thay đổi.
    @discardableResult
    public mutating func apply(_ ack: PcAck) -> Bool {
        guard ack.requestId == requestId, ack.code == code else { return false }
        guard ["success", "duplicate", "not_found", "ambiguous", "error", "invalid", "locked"].contains(ack.outcome) else { return false }
        // T039: Bỏ luồng gửi ảnh sang PC -> bỏ qua mọi ACK giai đoạn ảnh (nếu có gói cũ còn sót).
        if ack.ackStage == "photo" { return false }
        if ack.captureId != nil && ack.captureId != captureId { return false }
        if reviewRequired { return false }
        // v2: PC TỪ CHỐI dứt khoát (yêu cầu không hợp lệ / đơn đang khóa): thôi gửi lại, báo lý do, người dùng kiểm tra thủ công.
        if ack.outcome == "invalid" || ack.outcome == "locked" {
            scanConfirmed = true
            reviewRequired = true
            status = .error
            if let extra = ack.extra, !extra.isEmpty { detail = extra } else { detail = "PC từ chối yêu cầu này; cần kiểm tra trên máy tính." }
            return true
        }
        let unmatched = ack.outcome == "not_found" || ack.outcome == "ambiguous"

        if ack.outcome == "error" {
            if !scanConfirmed { status = .error }
            detail = "PC chưa lưu thành công; app giữ lại và sẽ thử lại."
            return true
        }

        scanConfirmed = true
        status = ScanStatus(rawValue: ack.outcome) ?? .success
        orderNum = ack.orderNum
        if ack.cancelled == true { isCancelledOrder = true }

        if unmatched {
            reviewRequired = true
            scanConfirmed = true
            status = ScanStatus(rawValue: ack.outcome) ?? .notFound
        }
        if let extra = ack.extra, !extra.isEmpty { detail = extra }
        else if status == .ambiguous { detail = "Trùng nhiều kiện, cần kiểm tra trên máy tính." }
        else if status == .notFound { detail = "Không tìm thấy mã trên hệ thống." }
        else if reviewRequired { detail = "Cần kiểm tra lại trên máy tính." }
        else if status == .duplicate { detail = "Mã này đã quét trước đó." }
        else { detail = "PC đã xác nhận." }
        return true
    }

    /// Lần gửi lại kế tiếp (ms) theo bậc 1.5s, 3s, 6s, 15s, 30s, 60s.
    public static let retrySchedule: [Int] = [1500, 3000, 6000, 15000, 30000, 60000]
    public func nextRetryDue() -> Int {
        let idx = max(0, min(attempts, Self.retrySchedule.count - 1))
        return lastSendAt + Self.retrySchedule[idx]
    }

    /// Giới hạn lịch sử: giữ tối đa `limit` mục đã hoàn tất mới nhất;
    /// TUYỆT ĐỐI không bỏ lượt chưa xác nhận (`!isSettled`) hoặc lượt còn giữ ảnh tạm chờ gắn vào đơn (Quy tắc 6 & 8).
    public static func trimHistory(_ items: [ScanItem], limit: Int = 800) -> (kept: [ScanItem], dropped: [ScanItem]) {
        let safeLimit = max(0, limit)
        guard items.count > safeLimit else { return (items, []) }
        let tailStart = safeLimit
        var kept: [ScanItem] = Array(items.prefix(tailStart))
        var dropped: [ScanItem] = []
        for it in items.suffix(from: tailStart) {
            if it.isSettled && !it.hasTemporaryPhoto {
                dropped.append(it)
            } else {
                kept.append(it)
            }
        }
        return (kept, dropped)
    }
}
