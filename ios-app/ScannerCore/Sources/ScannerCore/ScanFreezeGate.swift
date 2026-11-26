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

/// Theo dõi nhãn đang giữ trước camera: cùng một nhãn (có mã chung với lần trước) liên tục trong khung = MỘT lượt quét.
/// Khung camera chỉ đứng một lần lúc nhận lượt mới; nhãn rời khung quá `release` giây rồi quay lại mới là lượt mới.
public struct ScanHoldTracker {
    public enum Event: Equatable { case newScan, stillHolding, ignored }

    public private(set) var heldCodes: Set<String> = []
    private var gate: ScanFreezeGate
    private let release: TimeInterval
    private var lastSeen: Date = .distantPast

    public init(freeze: TimeInterval = ScanFreezeGate.defaultDuration, release: TimeInterval = 1.0) {
        gate = ScanFreezeGate(duration: freeze)
        self.release = release
    }

    public var freezeDuration: TimeInterval { gate.duration }
    public func isFrozen(at now: Date = Date()) -> Bool { gate.isFrozen(at: now) }
    public func isHolding(at now: Date = Date()) -> Bool { !heldCodes.isEmpty && now.timeIntervalSince(lastSeen) <= release }

    public mutating func observe(_ codes: [String], at now: Date = Date()) -> Event {
        let set = Set(codes.filter { !$0.isEmpty })
        guard !set.isEmpty else { return .ignored }
        if isHolding(at: now), !set.isDisjoint(with: heldCodes) {
            lastSeen = now
            heldCodes.formUnion(set)
            return .stillHolding
        }
        guard gate.accept(at: now) else { return .ignored }
        heldCodes = set
        lastSeen = now
        return .newScan
    }
}
