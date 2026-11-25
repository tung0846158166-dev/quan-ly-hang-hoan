import Foundation

/// Trạng thái kết nối với cloud (cloud/API.md mục 10).
public enum CloudState: String, Equatable {
    case unpaired      // chưa ghép thiết bị
    case connecting
    case syncing       // đang lấy phần thay đổi
    case connected
    case disconnected  // MẤT KẾT NỐI: khóa mọi thao tác ghi (quét, sửa), chỉ xem dữ liệu đã tải
    case revoked       // token bị chủ thu hồi
}

/// Mức dùng hạn mức trong ngày (số đếm ước lượng của ứng dụng, cloud/API.md mục 9).
public struct CloudQuota: Equatable {
    public var day: String
    public var requestsUsed: Int, requestsLimit: Int
    public var rowsWrittenUsed: Int, rowsWrittenLimit: Int
    public var rowsReadUsed: Int, rowsReadLimit: Int
    public var level: String   // "ok" | "warn" | "stop"
    public init(day: String, requestsUsed: Int, requestsLimit: Int, rowsWrittenUsed: Int, rowsWrittenLimit: Int,
                rowsReadUsed: Int, rowsReadLimit: Int, level: String) {
        self.day = day
        self.requestsUsed = requestsUsed; self.requestsLimit = requestsLimit
        self.rowsWrittenUsed = rowsWrittenUsed; self.rowsWrittenLimit = rowsWrittenLimit
        self.rowsReadUsed = rowsReadUsed; self.rowsReadLimit = rowsReadLimit
        self.level = level
    }
    public var requestsPercent: Double { requestsLimit <= 0 ? 0 : Double(requestsUsed) * 100 / Double(requestsLimit) }
    public var rowsWrittenPercent: Double { rowsWrittenLimit <= 0 ? 0 : Double(rowsWrittenUsed) * 100 / Double(rowsWrittenLimit) }
}

/// Liên kết với cloud nhìn từ giao diện. Giao diện CHỈ đọc `canWrite` để khóa/mở thao tác ghi: mất mạng là khóa, không có hàng đợi ngoại tuyến.
public protocol CloudLink: AnyObject {
    var state: CloudState { get }
    /// Chỉ true khi `connected` hoặc `syncing`.
    var canWrite: Bool { get }
    var lastError: String? { get }
    var lastSyncAt: Date? { get }
    var quota: CloudQuota? { get }
    var deviceName: String? { get }
    /// "owner" | "staff" | nil khi chưa ghép.
    var role: String? { get }
    var baseURL: String? { get }
    /// Gọi khi bất kỳ thuộc tính nào đổi (có thể từ luồng nền: giao diện tự đưa về luồng chính).
    var onChange: (() -> Void)? { get set }

    /// Ghép bằng mã 6 số do chủ tạo; trả nil nếu thành công, ngược lại thông báo lỗi tiếng Việt.
    func pair(baseURL: String, code: String, deviceName: String) async -> String?
    /// Lấy ngay phần thay đổi từ cloud.
    func syncNow() async
    /// Quên ghép trên máy này (không thu hồi trên cloud).
    func unpair()
}

/// Cài đặt GIẢ cho giao diện thử và XCTest (không mạng).
public final class FakeCloudLink: CloudLink {
    public private(set) var state: CloudState = .unpaired
    public var canWrite: Bool { state == .connected || state == .syncing }
    public private(set) var lastError: String?
    public private(set) var lastSyncAt: Date?
    public private(set) var quota: CloudQuota?
    public private(set) var deviceName: String?
    public private(set) var role: String?
    public private(set) var baseURL: String?
    public var onChange: (() -> Void)?
    public private(set) var syncNowCalls = 0
    public private(set) var unpairCalls = 0
    public static let validCode = "123456"

    public init() {}

    public func set(_ state: CloudState, error: String? = nil, quota: CloudQuota? = nil, lastSync: Date? = nil,
                    role: String? = nil, deviceName: String? = nil, baseURL: String? = nil) {
        self.state = state
        self.lastError = error
        if let quota { self.quota = quota }
        if let lastSync { self.lastSyncAt = lastSync }
        if let role { self.role = role }
        if let deviceName { self.deviceName = deviceName }
        if let baseURL { self.baseURL = baseURL }
        onChange?()
    }

    public func pair(baseURL: String, code: String, deviceName: String) async -> String? {
        if code != Self.validCode { return "Mã ghép không đúng hoặc đã hết hạn." }
        set(.connected, lastSync: Date(), role: "staff", deviceName: deviceName, baseURL: baseURL)
        return nil
    }

    public func syncNow() async { syncNowCalls += 1; lastSyncAt = Date(); onChange?() }

    public func unpair() {
        unpairCalls += 1
        state = .unpaired; role = nil; deviceName = nil
        onChange?()
    }
}
