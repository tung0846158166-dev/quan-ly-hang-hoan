import Foundation

/// Quy tắc ảnh quét khi dùng cloud (cloud/API.md mục 6; giữ nguyên quy tắc T040: chỉ giữ ảnh khi lượt quét được xác nhận đúng MỘT kiện).
public enum CloudPhotoRules {
    /// Mục tiêu dung lượng bản đầy đủ sau khi thu nhỏ (cạnh dài ~1000 px, JPEG ~60%).
    public static let targetFullBytes = 400_000
    /// Cloud từ chối ảnh lớn hơn mức này.
    public static let hardMaxBytes = 1_000_000

    /// Ảnh chỉ được giữ/đẩy lên khi cloud ĐÃ XÁC NHẬN (ack) và lượt quét khớp đúng một kiện (nhận mới hoặc đã nhận trước đó).
    /// Không tìm thấy, mơ hồ, cần kiểm tra thủ công, lỗi, chưa chắc đã ghi: bỏ ảnh.
    public static func shouldUpload(_ r: CloudScanResult) -> Bool {
        r.ack && (r.outcome == .received || r.outcome == .alreadyReceived) && r.parcel != nil
    }

    /// Mã ảnh duy nhất: chữ, số, `_`, `-` (6–80 ký tự), có mã kiện + giờ + số ngẫu nhiên để hai ảnh cùng giây không đè nhau.
    public static func makePhotoId(parcel: CloudParcel, now: Date = Date(), random: UInt32 = UInt32.random(in: 0...UInt32.max)) -> String {
        let code = CloudRowMapper.normalize(parcel.track.isEmpty ? parcel.id : parcel.track)
        let safe = String(code.filter { $0.isASCII && ($0.isLetter || $0.isNumber) }.prefix(40))
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(secondsFromGMT: 0)!
        let c = cal.dateComponents([.year, .month, .day, .hour, .minute, .second], from: now)
        let stamp = String(format: "%04d%02d%02d%02d%02d%02d", c.year ?? 0, c.month ?? 0, c.day ?? 0, c.hour ?? 0, c.minute ?? 0, c.second ?? 0)
        let id = "p_\(safe.isEmpty ? "x" : safe)_\(stamp)_\(String(random, radix: 16))"
        return String(id.prefix(80))
    }
}

/// Đẩy ảnh lên cloud SAU KHI lượt quét được xác nhận. Chỉ điện thoại làm việc này (máy tính chỉ xem ảnh).
public final class CloudPhotoUploader {
    private let api: CloudAPI
    public init(api: CloudAPI) { self.api = api }

    /// Trả mã ảnh nếu đã đẩy bản đầy đủ; nil nếu lượt quét không đủ điều kiện giữ ảnh (không gọi mạng).
    /// Lỗi mạng/cloud ném `CloudAPIError` để giao diện báo (ảnh vẫn nằm trên máy cho tới khi người dùng quyết định).
    /// Bản thu nhỏ (thumb) gửi sau, lỗi thì bỏ qua (cloud tự dùng bản đầy đủ).
    public func upload(after result: CloudScanResult, full: Data, thumb: Data?, now: Date = Date()) async throws -> String? {
        guard CloudPhotoRules.shouldUpload(result), let parcel = result.parcel else { return nil }
        guard !full.isEmpty, full.count <= CloudPhotoRules.hardMaxBytes else {
            throw CloudAPIError(code: "invalid", message: "Ảnh quá lớn hoặc rỗng (tối đa 1 MB sau khi thu nhỏ).", status: 0)
        }
        let id = CloudPhotoRules.makePhotoId(parcel: parcel, now: now)
        try await api.putPhoto(photoId: id, parcelId: parcel.id, mime: "image/jpeg", thumb: false, data: full)
        if let t = thumb, !t.isEmpty, t.count <= CloudPhotoRules.hardMaxBytes {
            try? await api.putPhoto(photoId: id, parcelId: parcel.id, mime: "image/jpeg", thumb: true, data: t)
        }
        return id
    }
}
