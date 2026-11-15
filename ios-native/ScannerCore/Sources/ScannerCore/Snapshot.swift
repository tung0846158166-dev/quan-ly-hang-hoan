import Foundation

/// Một dòng trong danh sách đơn chờ hoàn mà PC gửi sang (khóa ngắn để gói nhỏ).
public struct OrderRow: Codable, Identifiable, Equatable, Hashable {
    public var k: String      // "m" = bảng chính, "c" = đơn hủy
    public var c: String      // mã vận đơn / mã chính
    public var o: String      // mã đơn hàng
    public var p: String      // sàn
    public var n: String      // sản phẩm
    public var co: String     // màu
    public var sz: String     // size
    public var q: Int
    public var rd: String     // ngày hủy/hoàn (chuỗi từ PC)
    public var due: Int       // hạn xử lý, ms epoch; 0 = chưa tính được
    public var st: String

    public var id: String { k + "|" + c }
    public var isCancelled: Bool { k == "c" }
}

public struct SnapshotPayload: Codable, Equatable {
    public var time: Int
    public var count: Int
    public var rows: [OrderRow]
}

public struct SnapshotChunk: Equatable {
    public var snapshotId: String
    public var idx: Int
    public var total: Int
    public var chunk: String
}

public enum SnapshotError: Error { case badBase64, badCompression, badJSON }

/// Ghép các mảnh snapshot_chunk thành danh sách đơn.
public final class SnapshotAssembler {
    private var currentId = ""
    private var parts: [Int: String] = [:]
    private var total = 0

    public init() {}

    /// Trả về payload khi đủ mảnh; mảnh của lần gửi cũ hơn bị bỏ.
    public func add(_ c: SnapshotChunk) -> SnapshotPayload? {
        guard c.total > 0, c.idx >= 0, c.idx < c.total else { return nil }
        if c.snapshotId != currentId {
            if !currentId.isEmpty && c.snapshotId < currentId { return nil }
            currentId = c.snapshotId; parts = [:]; total = c.total
        }
        parts[c.idx] = c.chunk
        guard parts.count == total else { return nil }
        let b64 = (0..<total).compactMap { parts[$0] }.joined()
        parts = [:]
        return try? Self.decode(base64: b64)
    }

    public static func decode(base64: String) throws -> SnapshotPayload {
        guard let data = Data(base64Encoded: base64) else { throw SnapshotError.badBase64 }
        guard let raw = try? (data as NSData).decompressed(using: .zlib) as Data else { throw SnapshotError.badCompression }
        guard let p = try? JSONDecoder().decode(SnapshotPayload.self, from: raw) else { throw SnapshotError.badJSON }
        return p
    }
}

public enum DeadlineLevel: Equatable { case overdue, soon, normal, unknown }

public enum Deadline {
    public static func level(due: Int, now: Int, soonWindowMs: Int = 86_400_000) -> DeadlineLevel {
        if due <= 0 { return .unknown }
        let left = due - now
        if left < 0 { return .overdue }
        return left <= soonWindowMs ? .soon : .normal
    }

    public static func duration(_ ms: Int) -> String {
        let totalH = abs(ms) / 3_600_000
        let d = totalH / 24, h = totalH % 24
        return ((d > 0 ? "\(d) ngày " : "") + (h > 0 || d == 0 ? "\(h) giờ" : "")).trimmingCharacters(in: .whitespaces)
    }

    public static func text(due: Int, now: Int) -> String {
        if due <= 0 { return "Chưa có hạn" }
        let left = due - now
        if left > 0 { return "Còn " + duration(left) }
        if -left < 3_600_000 { return "Đã đến hạn" }
        return "Quá hạn " + duration(left)
    }
}
