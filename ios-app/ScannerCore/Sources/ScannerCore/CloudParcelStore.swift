import Foundation

/// Bản sao cục bộ các kiện lấy từ cloud + mốc đồng bộ (`rev`). Dùng để XEM khi mất mạng (chỉ đọc) và dựng danh sách dòng cho giao diện.
/// Có thể lưu ra tệp JSON (ghi nguyên tử). Không bao giờ xóa kiện (cloud cũng không xóa cứng).
public final class CloudParcelStore {
    private struct Disk: Codable {
        var rev: Int
        var parcels: [CloudParcel]
        var watch: [CloudWatchChange]?     // bản lưu cũ chưa có trường này
    }

    private let lock = NSLock()
    private var map: [String: CloudParcel] = [:]
    private var order: [String] = []        // thứ tự ổn định theo lần xuất hiện đầu tiên
    private var revValue = 0
    private var watchMap: [String: CloudWatchChange] = [:]   // id kiện -> tư cách thành viên Bảng Hàng Hoàn Về
    private let fileURL: URL?

    public init(fileURL: URL? = nil) {
        self.fileURL = fileURL
        load()
    }

    public var rev: Int { lock.lock(); defer { lock.unlock() }; return revValue }
    public var count: Int { lock.lock(); defer { lock.unlock() }; return map.count }
    public func parcel(id: String) -> CloudParcel? { lock.lock(); defer { lock.unlock() }; return map[id] }
    public var all: [CloudParcel] { lock.lock(); defer { lock.unlock() }; return order.compactMap { map[$0] } }

    /// Áp một lô thay đổi từ cloud và nhớ mốc mới. Gọi `save()` sau khi bắt kịp.
    public func apply(_ parcels: [CloudParcel], rev newRev: Int, watch: [CloudWatchChange] = []) {
        lock.lock(); defer { lock.unlock() }
        for p in parcels { put(p) }
        for w in watch { watchMap[w.key] = w }
        revValue = max(revValue, newRev)
    }

    /// Tư cách thành viên Bảng Hàng Hoàn Về của một kiện (nil nếu chưa có dấu).
    public func watch(id: String) -> CloudWatchChange? { lock.lock(); defer { lock.unlock() }; return watchMap[id] }

    /// Áp ngay kiện trả về từ thao tác ghi (không đổi mốc đồng bộ; lần `/sync` kế tiếp áp lại cũng không sao).
    public func upsertLocal(_ p: CloudParcel) {
        lock.lock(); defer { lock.unlock() }
        put(p)
    }

    private func put(_ p: CloudParcel) {
        // hai lần kéo chạy song song: bản về sau mà CŨ hơn thì bỏ, không đè trạng thái mới (rà lỗi 26/11/2026)
        if let old = map[p.id], p.rev > 0, old.rev > p.rev { return }
        if map[p.id] == nil { order.append(p.id) }
        map[p.id] = p
    }

    /// Quên toàn bộ (khi ghép sang cloud khác hoặc bỏ ghép).
    public func reset() {
        lock.lock(); defer { lock.unlock() }
        map = [:]; order = []; revValue = 0; watchMap = [:]
        if let u = fileURL { try? FileManager.default.removeItem(at: u) }
    }

    /// Dòng cho giao diện (bỏ kiện đã xóa), đã sắp: đang chờ lên trước, hạn gần trước.
    public func rows(now: Date, calendar: Calendar = CloudRowMapper.vietnamCalendar) -> [OrderRow] {
        lock.lock()
        let marks = watchMap
        lock.unlock()
        return CloudRowMapper.rows(all, now: now, calendar: calendar, watch: marks)
    }

    // ---- lưu/đọc
    public func save() {
        guard let url = fileURL else { return }
        lock.lock()
        let disk = Disk(rev: revValue, parcels: order.compactMap { map[$0] }, watch: Array(watchMap.values))
        lock.unlock()
        guard let data = try? JSONEncoder().encode(disk) else { return }
        try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        let tmp = url.appendingPathExtension("tmp")
        do {
            try data.write(to: tmp, options: .atomic)
            if FileManager.default.fileExists(atPath: url.path) {
                _ = try FileManager.default.replaceItemAt(url, withItemAt: tmp)
            } else {
                try FileManager.default.moveItem(at: tmp, to: url)
            }
        } catch {
            try? FileManager.default.removeItem(at: tmp)
        }
    }

    private func load() {
        guard let url = fileURL, let data = try? Data(contentsOf: url), let disk = try? JSONDecoder().decode(Disk.self, from: data) else { return }
        for p in disk.parcels { put(p) }
        for w in disk.watch ?? [] { watchMap[w.key] = w }
        revValue = disk.rev
    }
}
