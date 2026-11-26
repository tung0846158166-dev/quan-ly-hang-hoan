import Foundation

/// Hàng chờ ảnh quét (chủ dự án 26/11/2026): ảnh của kiện cloud ĐÃ xác nhận được lưu vào máy trước, đẩy lên cloud xong thì
/// TỰ XÓA khỏi máy (đỡ nặng); mất mạng thì nằm chờ, có mạng tự gửi lại (cả sau khi tắt/mở app). Lượt QUÉT vẫn cần mạng như cũ.
/// Mã ảnh cố định nên gửi lại nhiều lần không tạo ảnh trùng. Lỗi cố định (kiện không còn, ảnh hỏng/quá lớn, sai quyền) thì bỏ ảnh.
public final class CloudPhotoQueue {
    public struct Entry: Codable, Equatable {
        public let photoId: String
        public let parcelId: String
        public let hasThumb: Bool
        public let createdAt: Date
    }

    private let dir: URL
    private let lock = NSLock()
    private var entries: [Entry] = []
    private var draining = false

    public init(dir: URL) {
        self.dir = dir
        if let d = try? Data(contentsOf: dir.appendingPathComponent("queue.json")),
           let e = try? JSONDecoder().decode([Entry].self, from: d) {
            entries = e.filter { FileManager.default.fileExists(atPath: fullURL($0.photoId).path) }
        }
    }

    public var pendingCount: Int { lock.lock(); defer { lock.unlock() }; return entries.count }
    /// Đang gửi dở (lần gọi `drain` khác đang chạy).
    public var isDraining: Bool { lock.lock(); defer { lock.unlock() }; return draining }
    /// Ảnh bị bỏ vì cloud từ chối hẳn (vd. quá 3 ngày sau lượt quét): (mã ảnh, lý do). Không im lặng bỏ ảnh.
    public var onDropped: ((String, String) -> Void)?

    private func fullURL(_ id: String) -> URL { dir.appendingPathComponent("f_\(id).jpg") }
    private func thumbURL(_ id: String) -> URL { dir.appendingPathComponent("t_\(id).jpg") }

    private func save() {
        guard let d = try? JSONEncoder().encode(entries) else { return }
        try? d.write(to: dir.appendingPathComponent("queue.json"), options: .atomic)
    }

    /// Lưu ảnh vào máy và đưa vào hàng chờ (ghi tệp trước, rồi mới ghi danh sách).
    public func enqueue(photoId: String, parcelId: String, full: Data, thumb: Data?) throws {
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        try full.write(to: fullURL(photoId), options: .atomic)
        if let t = thumb, !t.isEmpty { try t.write(to: thumbURL(photoId), options: .atomic) }
        lock.lock(); defer { lock.unlock() }
        entries.removeAll { $0.photoId == photoId }
        entries.append(Entry(photoId: photoId, parcelId: parcelId, hasThumb: !(thumb ?? Data()).isEmpty, createdAt: Date()))
        save()
    }

    private func remove(_ e: Entry) {
        try? FileManager.default.removeItem(at: fullURL(e.photoId))
        try? FileManager.default.removeItem(at: thumbURL(e.photoId))
        lock.lock(); entries.removeAll { $0.photoId == e.photoId }; save(); lock.unlock()
    }

    /// Gửi lần lượt các ảnh đang chờ. Lên được thì xóa khỏi máy; mất mạng / hết hạn mức thì dừng, để lần sau.
    /// Trả số ảnh đã lên cloud trong lần này.
    @discardableResult
    public func drain(api: CloudAPI) async -> Int {
        lock.lock()
        if draining { lock.unlock(); return 0 }
        draining = true
        let todo = entries
        lock.unlock()
        defer { lock.lock(); draining = false; lock.unlock() }

        var sent = 0
        for e in todo {
            guard let full = try? Data(contentsOf: fullURL(e.photoId)) else { remove(e); continue }
            do {
                try await api.putPhoto(photoId: e.photoId, parcelId: e.parcelId, mime: "image/jpeg", thumb: false, data: full)
                if e.hasThumb, let t = try? Data(contentsOf: thumbURL(e.photoId)) {
                    try? await api.putPhoto(photoId: e.photoId, parcelId: e.parcelId, mime: "image/jpeg", thumb: true, data: t)
                }
                remove(e)
                sent += 1
            } catch let err as CloudAPIError {
                // mất mạng, hết hạn mức, máy chủ lỗi tạm thời, chưa đăng nhập: giữ lại, lần sau gửi tiếp
                if err.isNetwork || err.isQuota || err.isUnauthorized || err.status >= 500 { break }
                remove(e)   // lỗi cố định (kiện không còn, ảnh hỏng/quá lớn, sai quyền): bỏ ảnh, không kẹt hàng chờ
                onDropped?(e.photoId, err.message)
            } catch {
                break
            }
        }
        return sent
    }
}
