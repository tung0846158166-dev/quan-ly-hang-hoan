import Foundation

/// Khung camera "đứng" sau mỗi lần nhận mã (mặc định 0,2 giây, chủ dự án 26/11/2026): người quét thấy rõ mã vừa nhận và kết quả,
/// mã thấy trong lúc đứng bị bỏ (không kéo dài thời gian đứng). 0 = tắt.
public struct ScanFreezeGate {
    public static let defaultDuration: TimeInterval = 0.2
    public let duration: TimeInterval
    private var frozenUntil: Date = .distantPast

    public init(duration: TimeInterval = ScanFreezeGate.defaultDuration) {
        self.duration = max(0, min(2, duration))
    }

    public func isFrozen(at now: Date = Date()) -> Bool { now < frozenUntil }

    /// true = nhận mã này và bắt đầu đứng; false = đang đứng, bỏ qua.
    public mutating func accept(at now: Date = Date()) -> Bool {
        guard !isFrozen(at: now) else { return false }
        frozenUntil = now.addingTimeInterval(duration)
        return true
    }
}
