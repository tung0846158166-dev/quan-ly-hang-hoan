import Foundation

/// Thông tin ghép thiết bị lưu trên máy (token cần lưu ở nơi an toàn: Keychain, xem App/Sources/CloudSessionKeychain.swift).
public struct CloudSavedSession: Codable, Equatable {
    public var baseURL: String
    public var token: String
    public var deviceId: String
    public var deviceName: String
    public var role: String
    public init(baseURL: String, token: String, deviceId: String, deviceName: String, role: String) {
        self.baseURL = baseURL; self.token = token; self.deviceId = deviceId; self.deviceName = deviceName; self.role = role
    }
}

public protocol CloudSessionStoring: AnyObject {
    func load() -> CloudSavedSession?
    func save(_ s: CloudSavedSession)
    func clear()
}

public final class InMemoryCloudSessionStore: CloudSessionStoring {
    private var value: CloudSavedSession?
    public init(_ initial: CloudSavedSession? = nil) { value = initial }
    public func load() -> CloudSavedSession? { value }
    public func save(_ s: CloudSavedSession) { value = s }
    public func clear() { value = nil }
}

/// Liên kết THẬT với cloud cho điện thoại: ghép bằng mã 6 số, giữ WebSocket, đồng bộ vào `CloudParcelStore`, quét qua cloud.
/// Mất WebSocket (hoặc im lặng quá `silenceTimeout`) = MẤT KẾT NỐI: `canWrite == false`, không quét/sửa được, KHÔNG có hàng đợi ngoại tuyến.
public final class CloudSyncClient: CloudLink, @unchecked Sendable {
    public let store: CloudParcelStore
    private let sessions: CloudSessionStoring
    private let transport: CloudHTTPTransport
    private let lock = NSLock()
    private var _state: CloudState = .unpaired
    private var _error: String?
    private var _lastSync: Date?
    private var _quota: CloudQuota?
    private var session: CloudSavedSession?
    private var api: CloudAPI?
    private var runTask: Task<Void, Never>?

    public var onChange: (() -> Void)?
    /// Có dữ liệu mới từ cloud đã áp vào `store`: giao diện nạp lại danh sách (có thể gọi từ luồng nền).
    public var onDataChanged: (() -> Void)?

    public var pingEvery: TimeInterval = 10
    public var silenceTimeout: TimeInterval = 25
    public var quotaEvery: TimeInterval = 60

    public init(store: CloudParcelStore, sessions: CloudSessionStoring, transport: CloudHTTPTransport = URLSessionCloudTransport()) {
        self.store = store
        self.sessions = sessions
        self.transport = transport
    }

    // ---- CloudLink
    public var state: CloudState { lock.lock(); defer { lock.unlock() }; return _state }
    public var canWrite: Bool { state == .connected || state == .syncing }
    public var lastError: String? { lock.lock(); defer { lock.unlock() }; return _error }
    public var lastSyncAt: Date? { lock.lock(); defer { lock.unlock() }; return _lastSync }
    public var quota: CloudQuota? { lock.lock(); defer { lock.unlock() }; return _quota }
    public var deviceName: String? { lock.lock(); defer { lock.unlock() }; return session?.deviceName }
    public var role: String? { lock.lock(); defer { lock.unlock() }; return session?.role }
    public var baseURL: String? { lock.lock(); defer { lock.unlock() }; return session?.baseURL }
    public var currentAPI: CloudAPI? { lock.lock(); defer { lock.unlock() }; return api }

    private func set(_ s: CloudState, error: String? = nil) {
        lock.lock(); _state = s; _error = error; lock.unlock()
        onChange?()
    }

    /// Gọi lúc mở app: nếu đã ghép từ trước thì nối lại ngay.
    public func start() {
        guard let s = sessions.load() else { return }
        attach(s)
    }

    private func attach(_ s: CloudSavedSession) {
        guard let a = try? CloudAPI(baseURL: s.baseURL, token: s.token, transport: transport) else { return }
        lock.lock()
        runTask?.cancel()
        session = s
        api = a
        lock.unlock()
        set(.connecting)
        let t = Task<Void, Never> { [weak self] in
            guard let me = self else { return }
            await me.run()
        }
        lock.lock(); runTask = t; lock.unlock()
    }

    public func pair(baseURL: String, code: String, deviceName: String) async -> String? {
        // `code`: mã ghép 6 số (máy chủ tạo) HOẶC mật khẩu cửa hàng (>= 8 ký tự, chủ đặt trên cloud)
        let c = code.trimmingCharacters(in: .whitespacesAndNewlines)
        let isPairCode = c.count == 6 && c.allSatisfy({ $0.isNumber })
        guard isPairCode || c.count >= 8 else { return "Nhập mật khẩu cửa hàng (ít nhất 8 ký tự) hoặc mã ghép 6 số." }
        let name = deviceName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty else { return "Hãy đặt tên cho thiết bị này." }
        do {
            let anon = try CloudAPI(baseURL: baseURL, token: nil, transport: transport)
            let p = isPairCode ? try await anon.pairComplete(code: c, deviceName: name, kind: "phone")
                               : try await anon.pairJoin(joinKey: c, deviceName: name, kind: "phone")
            let root = String(anon.baseURL.dropLast(3))   // bỏ "/v1"
            if let old = baseURLValue(), old.lowercased() != root.lowercased() { store.reset() }
            let s = CloudSavedSession(baseURL: root, token: p.token, deviceId: p.deviceId, deviceName: name, role: p.role)
            sessions.save(s)
            attach(s)
            return nil
        } catch let e as CloudAPIError {
            return e.message
        } catch {
            return "Không ghép được thiết bị. Thử lại."
        }
    }

    private func baseURLValue() -> String? { lock.lock(); defer { lock.unlock() }; return session?.baseURL }

    public func syncNow() async {
        guard let a = currentAPI else { return }
        do { try await pull(a); if let q = try? await a.quota() { setQuota(q) } }
        catch let e as CloudAPIError { lock.lock(); _error = e.message; lock.unlock(); onChange?() }
        catch { }
    }

    public func unpair() {
        lock.lock()
        runTask?.cancel(); runTask = nil
        session = nil; api = nil; _quota = nil; _lastSync = nil
        lock.unlock()
        sessions.clear()
        store.reset()
        set(.unpaired)
    }

    private func setQuota(_ q: CloudQuota) { lock.lock(); _quota = q; lock.unlock(); onChange?() }

    // ---- quét qua cloud (cần mạng; mất mạng thì từ chối, không xếp hàng)
    public func scan(code: String) async -> CloudScanResult {
        guard canWrite, let a = currentAPI else {
            return CloudScanResult(outcome: .error, ack: false, message: "Mất kết nối cloud: chưa thể quét. Kiểm tra mạng rồi thử lại.",
                                   parcel: nil, receivedBy: nil, receivedAt: nil)
        }
        do {
            let r = try await a.scan(code: code)
            if let p = r.parcel { store.upsertLocal(p); scheduleSave(); onDataChanged?() }
            return r
        } catch let e as CloudAPIError {
            // không biết cloud đã nhận hay chưa: KHÔNG ack, yêu cầu kiểm tra lại đơn trước khi quét lại
            let msg = e.isNetwork ? "Mất kết nối cloud giữa chừng: chưa chắc kiện đã được nhận. Hãy kiểm tra lại đơn trước khi quét lại." : e.message
            return CloudScanResult(outcome: .error, ack: false, message: msg, parcel: nil, receivedBy: nil, receivedAt: nil)
        } catch {
            return CloudScanResult(outcome: .error, ack: false, message: "Không quét được. Thử lại.", parcel: nil, receivedBy: nil, receivedAt: nil)
        }
    }

    // ---- đồng bộ
    /// Lấy toàn bộ phần thay đổi từ mốc đã lưu cho tới khi bắt kịp.
    private var saveTask: Task<Void, Never>?
    /// Ghi bản sao ra tệp sau ~3 giây yên lặng (nhiều lần kéo liên tiếp chỉ ghi một lần). Mất tệp chưa ghi cũng an toàn:
    /// mốc `rev` trong tệp cũ hơn nên lần mở sau tự kéo lại phần còn thiếu từ cloud.
    func scheduleSave() {
        lock.lock()
        saveTask?.cancel()
        let st = store
        saveTask = Task.detached(priority: .utility) {
            try? await Task.sleep(nanoseconds: 3_000_000_000)
            if Task.isCancelled { return }
            st.save()
        }
        lock.unlock()
    }

    func pull(_ a: CloudAPI) async throws {
        var changed = false
        while true {
            let page = try await a.sync(since: store.rev, limit: 500)
            if !page.parcels.isEmpty || !page.watch.isEmpty || page.rev != store.rev {
                store.apply(page.parcels, rev: page.rev, watch: page.watch)
                changed = changed || !page.parcels.isEmpty || !page.watch.isEmpty
            }
            if !page.more { break }
        }
        if changed { scheduleSave() }
        lock.lock(); _lastSync = Date(); lock.unlock()
        if changed { onDataChanged?() }
        onChange?()
    }

    private func run() async {
        var backoff: UInt64 = 2
        while !Task.isCancelled {
            guard let a = currentAPI, let url = a.webSocketURL() else { set(.unpaired); return }
            set(.connecting)
            let ws = URLSession.shared.webSocketTask(with: url)
            ws.resume()
            do {
                let first = try await ws.receive()        // máy chủ gửi ngay {"t":"rev"} khi nối thành công; token sai thì lỗi ở đây
                set(.syncing)
                try await pull(a)
                if let q = try? await a.quota() { setQuota(q) }
                set(.connected)
                backoff = 2
                try await listen(ws, api: a, first: first)
                if Task.isCancelled { ws.cancel(with: .goingAway, reason: nil); return }
                set(.disconnected, error: "Mất kết nối cloud. Đang thử nối lại...")
            } catch is CancellationError {
                ws.cancel(with: .goingAway, reason: nil)
                return
            } catch {
                ws.cancel(with: .goingAway, reason: nil)
                if Task.isCancelled { return }
                if let revoked = await isRevoked(a), revoked {
                    set(.revoked, error: "Thiết bị đã bị thu hồi khỏi cloud.")
                    return
                }
                let msg = (error as? CloudAPIError)?.message ?? "Mất kết nối cloud. Đang thử nối lại..."
                set(.disconnected, error: msg)
            }
            try? await Task.sleep(nanoseconds: backoff * 1_000_000_000)
            backoff = min(30, backoff * 2)
        }
    }

    /// nil = không xác định được (mất mạng); true = token bị từ chối.
    private func isRevoked(_ a: CloudAPI) async -> Bool? {
        do { _ = try await a.me(); return false }
        catch let e as CloudAPIError { return e.isNetwork ? nil : e.isUnauthorized }
        catch { return nil }
    }

    private func handle(_ message: URLSessionWebSocketTask.Message, api a: CloudAPI) async throws {
        var text = ""
        switch message {
        case .string(let s): text = s
        case .data(let d): text = String(data: d, encoding: .utf8) ?? ""
        @unknown default: break
        }
        guard let obj = (try? JSONSerialization.jsonObject(with: Data(text.utf8))) as? [String: Any], let t = obj["t"] as? String else { return }
        if t == "rev", CloudAPI.int(obj["rev"]) > store.rev {
            set(.syncing)
            try await pull(a)
            set(.connected)
        } else if t == "quota", let q = try? await a.quota() {
            setQuota(q)
        }
    }

    private func listen(_ ws: URLSessionWebSocketTask, api a: CloudAPI, first: URLSessionWebSocketTask.Message) async throws {
        try await handle(first, api: a)
        let seen = LastSeen()
        try await withThrowingTaskGroup(of: Void.self) { group in
            group.addTask { [self] in
                while !Task.isCancelled {
                    let m = try await ws.receive()
                    seen.touch()
                    try await handle(m, api: a)
                }
            }
            group.addTask { [self] in
                var lastQuota = Date()
                while !Task.isCancelled {
                    try await Task.sleep(nanoseconds: UInt64(pingEvery * 1_000_000_000))
                    if Date().timeIntervalSince(seen.value) > silenceTimeout {
                        throw CloudAPIError(code: "silence", message: "Mất kết nối cloud. Đang thử nối lại...", status: 0)
                    }
                    try await ws.send(.string("{\"t\":\"ping\"}"))
                    if Date().timeIntervalSince(lastQuota) > quotaEvery {
                        lastQuota = Date()
                        if let q = try? await a.quota() { setQuota(q) }
                    }
                }
            }
            defer { group.cancelAll() }
            _ = try await group.next()
        }
    }
}

/// Thời điểm nhận tin gần nhất (dùng chung giữa hai tác vụ).
private final class LastSeen: @unchecked Sendable {
    private let lock = NSLock()
    private var t = Date()
    func touch() { lock.lock(); t = Date(); lock.unlock() }
    var value: Date { lock.lock(); defer { lock.unlock() }; return t }
}
