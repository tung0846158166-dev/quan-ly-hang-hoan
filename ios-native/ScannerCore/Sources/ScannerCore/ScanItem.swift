import Foundation

public enum ScanStatus: String, Codable {
    case pending, success, duplicate, notFound = "not_found", ambiguous, error
}

public struct ScanItem: Codable, Identifiable, Equatable {
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
    public var pushed = false

    public init(requestId: String, code: String, kind: ScanKind, time: Int, photoFile: String?) {
        self.requestId = requestId
        self.captureId = requestId
        self.code = code
        self.kind = kind
        self.time = time
        self.photoFile = photoFile
    }

    /// Đã xong hẳn: PC xác nhận quét và (nếu có ảnh) đã lưu ảnh, hoặc cần kiểm tra thủ công.
    public var isSettled: Bool {
        scanConfirmed && (photoFile == nil || photoConfirmed || reviewRequired || status == .error)
    }

    /// Cập nhật theo phản hồi của PC. Trả về true nếu có thay đổi.
    @discardableResult
    public mutating func apply(_ ack: PcAck) -> Bool {
        guard ack.requestId == requestId, ack.code == code else { return false }
        guard ["success", "duplicate", "not_found", "ambiguous", "error"].contains(ack.outcome) else { return false }
        let isPhoto = ack.ackStage == "photo"
        if (isPhoto || ack.captureId != nil) && ack.captureId != captureId { return false }
        if reviewRequired { return false }
        let unmatched = ack.outcome == "not_found" || ack.outcome == "ambiguous"

        if ack.outcome == "error" {
            if !scanConfirmed { status = .error }
            detail = "PC chưa lưu thành công; app giữ lại và sẽ thử lại."
            return true
        }
        if isPhoto {
            if ack.photoSaved == true { photoConfirmed = true } else if !unmatched { return false }
        } else {
            scanConfirmed = true
            status = ScanStatus(rawValue: ack.outcome) ?? .success
            orderNum = ack.orderNum
            if ack.photoSaved == true && ack.captureId == captureId { photoConfirmed = true }
        }
        if unmatched {
            reviewRequired = true
            scanConfirmed = true
            status = ScanStatus(rawValue: ack.outcome) ?? .notFound
        }
        if let extra = ack.extra, !extra.isEmpty { detail = extra }
        else if reviewRequired { detail = "Cần kiểm tra: ảnh vẫn giữ trên điện thoại, chưa gắn vào đơn." }
        else if photoFile != nil && !photoConfirmed { detail = "Đã xác nhận quét. Ảnh đang gửi ngầm." }
        else if status == .duplicate { detail = "Mã này đã quét trước đó." }
        else { detail = "PC đã xác nhận." }
        return true
    }

    /// Lần gửi lại kế tiếp (ms) theo bậc 1.5s, 3s, 6s, 15s, 30s, 60s.
    public static let retrySchedule: [Int] = [1500, 3000, 6000, 15000, 30000, 60000]
    public func nextRetryDue() -> Int {
        let step = Self.retrySchedule[min(attempts, Self.retrySchedule.count - 1)]
        return lastSendAt + step
    }
}
