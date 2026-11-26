import SwiftUI
import UIKit
import AudioToolbox
import UserNotifications
import Security
import ScannerCore

struct Banner: Equatable {
    var code: String
    var text: String
    var color: Color
    var sub: String = ""
}

enum OrderActionState: Equatable {
    case pending
    case confirmed
    case failed
}

struct OrderActionFeedback: Equatable {
    var requestId: String
    var state: OrderActionState
    var message: String
}

/// Lệnh sửa đơn cục bộ đang chờ PC xác nhận (lưu bền xuống đĩa kèm `LocalEditOverlay` để không mất khi tắt app hoặc có snapshot mới).
private struct PendingCommand: Codable {
    var requestId: String
    var room: String
    var title: String
    var body: String
    var overlay: LocalEditOverlay
    var attempts: Int = 0
}

@MainActor
final class SyncEngine: ObservableObject {
    @Published var items: [ScanItem] = []
    @Published var room: String
    @Published var sseConnected = false
    @Published var brokerUp: Set<String> = []
    @Published var snapshotChunksSeen = 0
    // Không @Published: cập nhật theo từng mảnh sẽ làm mọi màn hình vẽ lại liên tục khi đang đồng bộ.
    var snapshotLastChunkAt: Date?
    private var chunkCounter = 0
    private var chunkPublishedAt = Date.distantPast
    @Published var pcLastSeen: Date?
    @Published var banner: Banner?
    /// Gợi ý AI mới nhất từ cloud (hiện ở đầu tab Thông báo).
    @Published var aiDigest: CloudAIDigest?
    var digestCheckedAt = Date.distantPast
    @Published var orderActionFeedback: [String: OrderActionFeedback] = [:]
    @Published var mode: ScanKind = .returnScan
    @Published var note = ""
    /// Chế độ xem thử dữ liệu mẫu (trong bộ nhớ, tuyệt đối không phát MQTT/ntfy hay ghi đè dữ liệu thật).
    @Published private(set) var isDemoMode = false
    private var savedRealItems: [ScanItem]?

    var snapshotProvider: (() -> Data?)?
    let snapshot = SnapshotStore()
    let photoManager = OrderPhotoManager()

    /// T042: liên kết cloud. Đã ghép (phiên lưu trong Keychain) thì chạy theo LUỒNG CLOUD thay cho MQTT/ntfy:
    /// danh sách đơn lấy từ cloud, quét/sửa gửi thẳng lên cloud, mất mạng = khóa ghi (không hàng đợi ngoại tuyến).
    let cloud: CloudSyncClient
    @Published private(set) var cloudState: CloudState = .unpaired
    fileprivate var cloudReloadScheduled = false
    /// true khi máy đã ghép cloud (kể cả đang mất kết nối hoặc bị thu hồi: vẫn khóa theo luồng cloud).
    var cloudActive: Bool { cloudState != .unpaired }

    private let client = NtfyClient()
    private var mqttClients: [MQTTClient] = []
    private var mqttTasks: [Task<Void, Never>] = []
    private var mqttGeneration = 0
    private var seenMsgs: [String: Date] = [:]
    private var snapshotRetries = 0
    private var sseTask: Task<Void, Never>?
    private var tickTask: Task<Void, Never>?
    private var started = false
    private var seq = 0
    private let sessionId = String(UUID().uuidString.prefix(6)).lowercased()
    private var lastRawCode = ""
    private var lastRawTime = Date.distantPast
    private var tickCount = 0

    private let docs: URL = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first
        ?? FileManager.default.temporaryDirectory
    private var storeURL: URL { docs.appendingPathComponent("scan_items.json") }
    private var pendingCmdsURL: URL { docs.appendingPathComponent("pending_commands.json") }
    private var photosDir: URL { docs.appendingPathComponent("photos", isDirectory: true) }

    /// Hàng đợi lệnh sửa đơn (`order_note`, `order_edit`, `order_delete`, `cancel_receive`) chờ PC xác nhận.
    private var pendingCommands: [String: PendingCommand] = [:]
    private static let maxPendingCommands = 200
    private static let maxActionFeedbackEntries = 200
    /// `true` chỉ khi tệp `scan_items.json` được nạp sạch (hoặc lần chạy đầu chưa có tệp); khóa dọn ảnh tạm mồ côi nếu đọc lỗi.
    private var scanItemsLoadedCleanly = true

    var pendingNoteCount: Int { pendingCommands.count }

    init() {
        let docsDir = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first ?? FileManager.default.temporaryDirectory
        cloud = CloudSyncClient(store: CloudParcelStore(fileURL: docsDir.appendingPathComponent("cloud_parcels.json")),
                                sessions: KeychainCloudSessionStore())
        room = SyncProtocol.normalizeRoom(UserDefaults.standard.string(forKey: "room") ?? "")   // v2: không có mã phòng mặc định
        try? FileManager.default.createDirectory(at: photosDir, withIntermediateDirectories: true)
        if FileManager.default.fileExists(atPath: storeURL.path) {
            if let data = try? Data(contentsOf: storeURL), let list = try? JSONDecoder().decode([ScanItem].self, from: data) {
                items = list
                scanItemsLoadedCleanly = true
            } else {
                scanItemsLoadedCleanly = false
            }
        }
        if let data = try? Data(contentsOf: pendingCmdsURL),
           let list = try? JSONDecoder().decode([PendingCommand].self, from: data) {
            for cmd in list { pendingCommands[cmd.requestId] = cmd }
        } else if let legacy = UserDefaults.standard.dictionary(forKey: "pendingNotes") as? [String: String], !legacy.isEmpty {
            // Di chuyển hàng đợi cũ (nếu có) sang định dạng bền mới.
            for (rid, body) in legacy {
                let ov = LocalEditOverlay(requestId: rid, action: .note, timeMs: 0)
                pendingCommands[rid] = PendingCommand(requestId: rid, room: room, title: "Shopee Note", body: body, overlay: ov)
            }
            savePendingCommands()
            UserDefaults.standard.removeObject(forKey: "pendingNotes")
        }
    }

    /// Tuỳ chọn bật/tắt trong Cài đặt (mặc định bật).
    static func pref(_ key: String) -> Bool { UserDefaults.standard.object(forKey: key) as? Bool ?? true }

    /// Bật/tắt chế độ xem thử dữ liệu mẫu. Tắt sẽ khôi phục nguyên vẹn lịch sử và danh sách đơn thật.
    func setDemoMode(_ on: Bool) {
        // chủ dự án 26/11/2026: đã có dữ liệu thật, bỏ chế độ xem thử (chỉ còn cho phép TẮT nếu đang bật)
        if on { return }
        if on {
            guard !isDemoMode else { return }
            flushPersist()
            savedRealItems = items
            isDemoMode = true
            items = DemoData.scanItems()
            snapshot.setDemoMode(true)
            banner = nil
            orderActionFeedback = [:]
        } else {
            guard isDemoMode else { return }
            isDemoMode = false
            items = savedRealItems ?? []
            savedRealItems = nil
            snapshot.setDemoMode(false)
            banner = nil
            orderActionFeedback = [:]
        }
    }

    func actionFeedback(for row: OrderRow) -> OrderActionFeedback? {
        if !row.c.isEmpty, let fb = orderActionFeedback[row.c.uppercased()] { return fb }
        if !row.o.isEmpty, let fb = orderActionFeedback[row.o.uppercased()] { return fb }
        return nil
    }

    /// Gửi lại một lượt quét chưa được PC xác nhận (T039: không gửi ảnh sang PC).
    func resend(_ requestId: String) {
        guard !isDemoMode, !room.isEmpty else { return }
        guard let i = items.firstIndex(where: { $0.requestId == requestId }) else { return }
        let it = items[i]
        if !it.scanConfirmed { transmit(requestId) }
        banner = Banner(code: it.code, text: "Đã gửi lại, đang chờ PC xác nhận", color: Theme.warning)
    }

    /// Đưa tất cả lượt quét chưa được máy tính xác nhận vào hàng gửi lại ngay.
    /// Mỗi nhịp app gửi tối đa 6 lượt quét nên không dồn cục; 100 đơn mất khoảng nửa phút.
    @discardableResult
    func resendAllPending(kind: ScanKind? = nil) -> Int {
        guard !isDemoMode else { return 0 }
        var n = 0
        for i in items.indices where !items[i].isSettled && (kind == nil || items[i].kind == kind) {
            n += 1
            if !items[i].scanConfirmed { items[i].lastSendAt = 0; items[i].attempts = 0 }
        }
        if n > 0 { persist() }
        return n
    }

    /// Các mã đã quét hoàn kho thành công trên điện thoại (dùng để đánh dấu trong danh sách đơn).
    var scannedReturnCodes: Set<String> {
        Set(items.filter { $0.kind == .returnScan && $0.scanConfirmed && ($0.status == .success || $0.status == .duplicate) }.map { $0.code.uppercased() })
    }

    /// Giờ quét hoàn kho gần nhất trên chính điện thoại này cho một đơn (ms epoch), dùng khi `rcd` từ PC chưa có.
    func localReturnScanTimeMs(for row: OrderRow) -> Int? {
        let codes = Set(row.lookupCodes)
        guard !codes.isEmpty else { return nil }
        return items.first(where: {
            $0.kind == .returnScan
                && ($0.status == .success || $0.status == .pending)
                && codes.contains($0.code.uppercased())
        })?.time
    }

    var mqttConnected: Bool { !brokerUp.isEmpty }
    var channelUp: Bool { sseConnected || mqttConnected }
    var pendingCount: Int { items.filter { !$0.isSettled }.count }
    var pcOnline: Bool { pcLastSeen.map { Date().timeIntervalSince($0) < 120 } ?? false }

    /// Ghi nhận máy tính còn online. Chỉ cập nhật tối đa 5 giây một lần để tránh vẽ lại liên tục khi đang đồng bộ.
    private func markPcSeen() {
        let now = Date()
        if let t = pcLastSeen, now.timeIntervalSince(t) < 5 { return }
        pcLastSeen = now
    }

    /// Đường dẫn ảnh tạm của lượt quét (chưa gắn vào kiện).
    func photoURL(_ item: ScanItem) -> URL? { item.photoFile.map { photosDir.appendingPathComponent($0) } }

    /// Đường dẫn ảnh hiển thị cho một lượt quét (ưu tiên ảnh đã chuyển vào kho `order-photos/`, sau đó tới ảnh đang giữ ở lượt quét).
    func scanDisplayPhotoURL(_ item: ScanItem) -> URL? {
        if let pid = item.attachedPhotoId, let u = photoManager.photoURL(forPhotoId: pid) {
            return u
        }
        if let u = photoURL(item), FileManager.default.fileExists(atPath: u.path) {
            return u
        }
        return nil
    }

    /// Thống kê các ảnh tạm đang chờ PC xác nhận trong `Documents/photos/`.
    var tempPhotoSummary: PhotoStorageSummary {
        guard !isDemoMode else { return PhotoStorageSummary() }
        return ScanPhotoLifecycle.tempPhotoSummary(for: items, tempPhotosDir: photosDir)
    }

    /// Các lượt quét treo quá 7 ngày chưa có kết quả vẫn còn ảnh tạm (T040 mục 3: chỉ cảnh báo để người dùng quyết định).
    func stalePendingTempPhotoItems(nowMs: Int? = nil) -> [ScanItem] {
        guard !isDemoMode else { return [] }
        return ScanPhotoLifecycle.stalePendingTempPhotoItems(
            in: items,
            tempPhotosDir: photosDir,
            nowMs: nowMs ?? self.nowMs()
        )
    }

    func isStalePendingTempPhoto(_ item: ScanItem, nowMs: Int? = nil) -> Bool {
        guard !isDemoMode, item.hasTemporaryPhoto else { return false }
        let stale = stalePendingTempPhotoItems(nowMs: nowMs)
        return stale.contains { $0.requestId == item.requestId }
    }

    var stalePendingTempPhotoSummary: PhotoStorageSummary {
        guard !isDemoMode else { return PhotoStorageSummary() }
        let stale = stalePendingTempPhotoItems()
        return ScanPhotoLifecycle.tempPhotoSummary(for: stale, tempPhotosDir: photosDir)
    }

    /// Người dùng chọn bỏ ẢNH TẠM của một lượt quét treo quá 7 ngày (KHÔNG xóa lượt quét, Quy tắc 8).
    @discardableResult
    func discardTempPhoto(requestId: String) -> PhotoCleanupResult {
        guard !isDemoMode else { return PhotoCleanupResult(deletedCount: 0, freedBytes: 0) }
        let res = ScanPhotoLifecycle.discardTempPhotos(
            requestIds: [requestId],
            items: &items,
            tempPhotosDir: photosDir
        )
        if res.deletedCount > 0 { persist() }
        return res
    }

    /// Người dùng chọn bỏ toàn bộ ẢNH TẠM của các lượt quét treo quá 7 ngày (KHÔNG xóa lượt quét, Quy tắc 8).
    @discardableResult
    func discardStalePendingTempPhotos() -> PhotoCleanupResult {
        guard !isDemoMode else { return PhotoCleanupResult(deletedCount: 0, freedBytes: 0) }
        let ids = Set(stalePendingTempPhotoItems().map { $0.requestId })
        let res = ScanPhotoLifecycle.discardTempPhotos(
            requestIds: ids,
            items: &items,
            tempPhotosDir: photosDir
        )
        if res.deletedCount > 0 { persist() }
        return res
    }

    // MARK: vòng đời
    func start() {
        snapshot.onNeedFull = { [weak self] in self?.requestFullIfNeeded() }
        snapshot.onRowsUpdated = { [weak self] in self?.reconcileScanPhotos() }
        snapshot.unconfirmedReturnCodes = { [weak self] in
            Set((self?.items ?? []).filter { $0.kind == .returnScan && !$0.scanConfirmed }.map { $0.code.uppercased() })
        }
        snapshot.unconfirmedReturnScanTimes = { [weak self] in
            var map: [String: Int] = [:]
            for it in (self?.items ?? []) where it.kind == .returnScan && !it.scanConfirmed {
                let key = it.code.uppercased()
                if map[key] == nil { map[key] = it.time }
            }
            return map
        }
        snapshot.pendingLocalOverlays = { [weak self] in
            guard let self = self else { return [] }
            let currentRoom = self.room
            return self.pendingCommands.values
                .filter { $0.room.isEmpty || $0.room == currentRoom }
                .map { $0.overlay }
        }
        guard !started else { return }
        started = true
        // T040: Đối soát ảnh tạm còn tồn từ phiên trước (đúng -> gắn vào kho, không đúng -> xóa ngay, đang chờ -> giữ tạm).
        reconcileScanPhotos()
        // Dọn tệp JPEG mồ côi trong `Documents/order-photos/` và `Documents/photos/` chỉ khi chắc chắn và ghi nhật ký.
        photoManager.runSafeOrphanCleanup()
        photoManager.runSafeTempOrphanCleanup(
            tempPhotosDir: photosDir,
            items: items,
            scanItemsLoadedCleanly: scanItemsLoadedCleanly
        )
        startCloud()
        if !cloudActive {
            startSSE()
            startMQTT()
        }
        tickTask = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: 3_000_000_000)
                await self?.tick()
            }
        }
    }

    /// Gọi khi app trở lại foreground: kiểm tra lại quyền thông báo và đánh thức đồng bộ nếu cần.
    func handleForeground() {
        snapshot.refreshAuthorization()
        if cloudActive { Task { await cloud.syncNow() }; refreshAIDigest(force: true); return }
        guard !isDemoMode, !room.isEmpty else { return }
        if !channelUp {
            sseTask?.cancel(); sseTask = nil
            startSSE()
            startMQTT()
        } else if snapshot.updatedAt == nil || Date().timeIntervalSince(snapshot.updatedAt ?? .distantPast) > 600 {
            requestSnapshot()
        }
    }

    /// Đổi broker MQTT riêng (rỗng = broker công cộng) và nối lại.
    func setPrivateBroker(_ raw: String) {
        let v = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        if v.isEmpty { UserDefaults.standard.removeObject(forKey: "privateBroker") }
        else { UserDefaults.standard.set(v, forKey: "privateBroker") }
        startMQTT()
    }

    func setRoom(_ raw: String) {
        let r = SyncProtocol.normalizeRoom(raw)
        guard r != room else { return }
        room = r
        UserDefaults.standard.set(r, forKey: "room")
        pcLastSeen = nil
        seenMsgs.removeAll()
        orderActionFeedback.removeAll()
        snapshotRetries = 0
        chunkCounter = 0
        snapshotChunksSeen = 0
        snapshotLastChunkAt = nil
        snapshot.resetAssemblers()
        sseTask?.cancel(); sseTask = nil
        sseConnected = false
        startSSE()
        startMQTT()
    }

    private func startSSE() {
        if room.isEmpty || cloudActive { return }   // cloud: không dùng kênh cũ   // v2: chưa cài mã phòng thì không nối
        let watching = room
        sseTask = Task { [weak self] in
            var backoff = 1.0
            while !Task.isCancelled {
                guard let self = self else { return }
                if await self.room != watching { return }
                do {
                    let stream = self.client.events(topic: SyncProtocol.topic(room: watching))
                    for try await data in stream {
                        backoff = 1
                        await self.handleIncoming(data, opened: true)
                    }
                } catch {}
                await MainActor.run { self.sseConnected = false }
                try? await Task.sleep(nanoseconds: UInt64(backoff * 1_000_000_000))
                backoff = min(backoff * 2, 15)
            }
        }
    }

    // MARK: kênh MQTT dự phòng (dùng khi ntfy.sh bị chặn)
    private func startMQTT() {
        mqttTasks.forEach { $0.cancel() }
        mqttTasks = []
        mqttClients = []
        brokerUp = []
        mqttGeneration += 1
        let gen = mqttGeneration
        if room.isEmpty || cloudActive { return }   // cloud: chỉ hủy kết nối cũ, không nối lại
        let topic = SyncProtocol.mqttTopic(room: room)
        for b in SyncProtocol.mqttBrokers {
            let c = MQTTClient(name: b.name, url: b.url)
            mqttClients.append(c)
            let name = b.name
            mqttTasks.append(Task {
                await c.run(topic: topic,
                    onMessage: { [weak self] raw in
                        Task { @MainActor [weak self] in
                            guard let self = self, self.mqttGeneration == gen else { return }
                            self.handleMQTT(raw)
                        }
                    },
                    onState: { [weak self] up in
                        Task { @MainActor [weak self] in
                            guard let self = self, self.mqttGeneration == gen else { return }
                            self.brokerChanged(name, up: up)
                        }
                    })
            })
        }
    }

    private func brokerChanged(_ name: String, up: Bool) {
        let was = mqttConnected
        if up { brokerUp.insert(name) } else { brokerUp.remove(name) }
        if !was && mqttConnected && !sseConnected { channelOpened() }
    }

    private func handleMQTT(_ raw: String) {
        guard !room.isEmpty, let msg = SyncProtocol.parseMessage(raw, room: room) else { return }
        dispatch(msg)
    }

    /// Gửi một bản tin qua mọi kênh đang có. ntfy chỉ dùng khi nó đang nối, hoặc khi MQTT chưa nối được.
    private func broadcast(_ body: String, title: String, room r: String) {
        guard !isDemoMode, !r.isEmpty else { return }
        for c in mqttClients { c.publish(topic: SyncProtocol.mqttTopic(room: r), body: body) }
        if sseConnected || !mqttConnected {
            Task { try? await client.post(topic: SyncProtocol.topic(room: r), title: title, body: body) }
        }
    }

    /// Kênh vừa nối: báo cho PC biết, mở khóa lại hàng đợi lệnh quét/sửa, xin danh sách đơn nếu cũ.
    private func channelOpened() {
        guard !room.isEmpty else { return }
        let hello = SyncProtocol.json(["type": "phone_connected", "room": room, "time": nowMs()])
        broadcast(hello, title: "Shopee Connect", room: room)
        resetPendingForReconnect()
        resendPendingCommands()
        if snapshot.updatedAt == nil || Date().timeIntervalSince(snapshot.updatedAt ?? .distantPast) > 600 { requestSnapshot() }
    }

    /// Cùng một phản hồi có thể tới từ nhiều kênh; chỉ xử lý một lần trong 30 giây.
    private func isDuplicate(_ key: String) -> Bool {
        let now = Date()
        if seenMsgs.count > 400 { seenMsgs = seenMsgs.filter { now.timeIntervalSince($0.value) < 30 } }
        if let t = seenMsgs[key], now.timeIntervalSince(t) < 30 { return true }
        seenMsgs[key] = now
        return false
    }

    // MARK: nhận phản hồi
    private func handleIncoming(_ data: String, opened: Bool) {
        if opened && !sseConnected {
            sseConnected = true
            channelOpened()
        }
        let r = room
        guard !r.isEmpty else { return }
        // Giải mã JSON (mỗi mảnh danh sách đơn khá lớn) ở luồng nền, chỉ phần xử lý kết quả mới về luồng giao diện.
        Task.detached(priority: .userInitiated) { [weak self] in
            guard let msg = SyncProtocol.parseIncoming(sseData: data, room: r) else { return }
            await self?.dispatch(msg)
        }
    }

    /// Đo thời gian khứ hồi điện thoại -> PC -> điện thoại.
    @Published var pingResult: String = ""
    private var pingId = ""
    private var pingSentAt: Date?

    func pingPC() {
        guard !room.isEmpty else { pingResult = "Chưa có mã phòng"; return }
        let id = UUID().uuidString
        pingId = id; pingSentAt = Date(); pingResult = "Đang đo..."
        broadcast(SyncProtocol.json(["type": "latency_ping", "room": room, "id": id]), title: "Shopee Connect", room: room)
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: 8_000_000_000)
            if self.pingId == id {
                self.pingResult = "PC không trả lời sau 8 giây (PC tắt hoặc mất mạng)"
                self.pingId = ""; self.pingSentAt = nil
            }
        }
    }

    private func dispatch(_ msg: PcMessage) {
        switch msg {
        case .pong(let id):
            markPcSeen()
            if id == pingId, let t = pingSentAt {
                pingResult = "PC trả lời sau \(Int(Date().timeIntervalSince(t) * 1000)) ms"
                pingId = ""; pingSentAt = nil
            }
        case .hello, .heartbeat:
            markPcSeen()
        case .snapshotChunk(let ch):
            markPcSeen()
            chunkCounter += 1
            snapshotLastChunkAt = Date()
            if Date().timeIntervalSince(chunkPublishedAt) > 0.5 { chunkPublishedAt = Date(); snapshotChunksSeen = chunkCounter }
            if isDuplicate("s|\(ch.snapshotId)|\(ch.idx)") { return }
            snapshot.ingest(ch)
        case .snapshotDelta(let ch):
            markPcSeen()
            if isDuplicate("d|\(ch.snapshotId)|\(ch.idx)") { return }
            snapshot.ingestDelta(ch)
        case .watchSync(let t, let items):
            markPcSeen()
            if isDuplicate("w|\(t)") { return }
            snapshot.applyWatchSync(items)
        case .ack(let ack) where ["note", "edit", "delete"].contains(ack.ackStage ?? ""):
            markPcSeen()
            if isDuplicate("n|\(ack.requestId)|\(ack.outcome)") { return }
            let isTemporaryError = ack.outcome == "error"
            if !isTemporaryError {
                pendingCommands[ack.requestId] = nil
                savePendingCommands()
            } else if var cmd = pendingCommands[ack.requestId] {
                cmd.attempts += 1
                if cmd.attempts >= 8 {
                    pendingCommands[ack.requestId] = nil
                } else {
                    pendingCommands[ack.requestId] = cmd
                }
                savePendingCommands()
            }
            let ok = ack.outcome == "success" || ack.outcome == "duplicate"
            let msg = ack.extra ?? (ok ? "PC đã cập nhật" : "PC không thực hiện được")
            banner = Banner(code: ack.code, text: msg, color: ok ? Theme.success : Theme.danger)
            let codeKey = ack.code.uppercased()
            if !codeKey.isEmpty {
                storeActionFeedback(key: codeKey, feedback: OrderActionFeedback(
                    requestId: ack.requestId,
                    state: ok ? .confirmed : (isTemporaryError ? .pending : .failed),
                    message: msg
                ))
            }
            for (k, v) in orderActionFeedback where v.requestId == ack.requestId {
                orderActionFeedback[k] = OrderActionFeedback(
                    requestId: ack.requestId,
                    state: ok ? .confirmed : (isTemporaryError ? .pending : .failed),
                    message: msg
                )
            }
            if !ok && !isTemporaryError { requestSnapshot() }   // lấy lại bản đúng từ PC nếu PC từ chối dứt khoát

        case .ack(let ack):
            markPcSeen()
            let saved = ack.photoSaved.map { $0 ? "1" : "0" } ?? "n"
            let stage = ack.ackStage ?? ""
            let cap = ack.captureId ?? ""
            let extra = ack.extra ?? ""
            if isDuplicate("a|\(ack.requestId)|\(ack.code)|\(ack.outcome)|\(stage)|\(cap)|\(saved)|\(extra)") { return }
            guard let i = items.firstIndex(where: { $0.requestId == ack.requestId }) else { return }
            if items[i].apply(ack) {
                if !items[i].isCancelledOrder, items[i].kind == .returnScan, let r = snapshot.lookup(items[i].code), r.isCancelled {
                    items[i].isCancelledOrder = true   // PC cũ không báo cờ đơn hủy
                }
                // T040: Áp dụng quy tắc giữ/xóa ảnh ngay khi có kết quả từ PC.
                reconcileScanPhotos()
                persist()
                if let updated = items.first(where: { $0.requestId == ack.requestId }) {
                    showResult(updated)
                }
            }
        default:
            break
        }
    }

    private func describe(_ it: ScanItem) -> (String, Color) {
        let m = ScanDisplay.message(for: it)
        return (m.label, toneColor(m.tone))
    }

    private func showResult(_ it: ScanItem) {
        let d = describe(it)
        banner = Banner(code: it.code, text: d.0, color: d.1)
        let gen = UINotificationFeedbackGenerator()
        let haptic = Self.pref("hapticOn"), sound = Self.pref("soundOn")
        switch it.status {
        case .success:
            if haptic { gen.notificationOccurred(.success) }
            if sound { AudioServicesPlaySystemSound(1057) }
        case .duplicate:
            if haptic { gen.notificationOccurred(.warning) }
            if sound { AudioServicesPlaySystemSound(1053) }
        default:
            if haptic { gen.notificationOccurred(.error) }
            if sound { AudioServicesPlaySystemSound(1053) }
        }
    }

    // MARK: quét
    private var lastCameraCode = ""
    private var lastCameraTime = Date.distantPast
    /// Camera thấy nhiều mã cùng lúc trên một nhãn: chọn đúng mã vận đơn (mã có trong dữ liệu trên máy trước, rồi mã đúng dạng hãng vận chuyển).
    func handleCameraCodes(_ raws: [String]) {
        let store = snapshot
        guard let best = TrackingCode.pickBest(raws, isKnown: { code in store.lookup(TrackingCode.clean(code)) != nil }) else { return }
        handleCameraCode(best)
    }

    func handleCameraCode(_ raw: String) {
        // Camera hay đọc nhầm hoa văn thành mã vạch: bỏ kết quả không giống mã vận đơn (nhập tay không bị lọc).
        guard TrackingCode.isPlausibleScan(raw) else { return }
        let c = "\(mode.rawValue)|" + TrackingCode.clean(raw), now = Date()
        // Mỗi lần camera còn thấy nhãn thì mốc được gia hạn: chỉ khi nhãn rời khung hình hơn 1 giây rồi quay lại mới tính là lượt quét mới.
        if c == lastCameraCode && now.timeIntervalSince(lastCameraTime) < 1.0 { lastCameraTime = now; return }
        lastCameraCode = c; lastCameraTime = now
        _ = submit(raw: raw, kind: mode, photo: nil, useSnapshot: true)
    }

    func submitManual(_ raw: String) {
        _ = submit(raw: raw, kind: mode, photo: nil, useSnapshot: true)
    }

    private func nextRequestId(prefix: String) -> String {
        seq += 1
        return "\(prefix)-\(sessionId)-\(seq)-\(nowMs() % 100000)"
    }

    @discardableResult
    func submit(raw: String, kind: ScanKind, photo: Data?, useSnapshot: Bool) -> Bool {
        let code = TrackingCode.clean(raw)
        guard code.count >= 6 else { return false }
        let now = Date()
        let rawKey = "\(kind.rawValue)|" + code
        if rawKey == lastRawCode && now.timeIntervalSince(lastRawTime) < 1.2 { return false }
        lastRawCode = rawKey; lastRawTime = now

        let trimmedNote = note.trimmingCharacters(in: .whitespacesAndNewlines)

        if isDemoMode {
            seq += 1
            let scanMs = nowMs()
            let scanDate = Date(timeIntervalSince1970: Double(scanMs) / 1000)
            let rid = "demo-\(seq)-\(scanMs % 100000)"
            var item = ScanItem(requestId: rid, code: code, kind: kind, time: scanMs, photoFile: nil, note: kind == .newOrder ? trimmedNote : "")
            let hint = localHint(code: code, kind: kind)
            if kind == .returnScan && snapshot.isAmbiguousPending(code) {
                item.status = .ambiguous
                item.scanConfirmed = true
                item.reviewRequired = true
                item.detail = "Dữ liệu mẫu: mã trùng nhiều kiện đang chờ, cần kiểm tra trên PC."
                banner = Banner(code: code, text: "Trùng nhiều đơn, cần kiểm tra trên PC", color: Theme.danger, sub: hint)
            } else if kind == .returnScan, let r = snapshot.lookup(code) {
                if r.rv {
                    item.status = .duplicate
                    item.scanConfirmed = true
                    item.detail = "Dữ liệu mẫu: đơn này đã nhận hoàn kho trước đó."
                    banner = Banner(code: code, text: "Đơn đã nhận hoàn kho trước đó", color: Theme.warning, sub: hint)
                } else if r.pd {
                    snapshot.applyLocalEdit(rowId: r.id, fields: ["st": "Hàng đã trả"], receivedAt: scanDate)
                    item.status = .success
                    item.scanConfirmed = true
                    item.orderNum = r.o
                    item.detail = "Dữ liệu mẫu: đã nhận hoàn kho cục bộ."
                    banner = Banner(code: code, text: "Đã nhận hoàn kho (dữ liệu mẫu)", color: Theme.success, sub: hint)
                } else {
                    item.status = .notFound
                    item.scanConfirmed = true
                    item.detail = "Dữ liệu mẫu: đơn không ở trạng thái chờ hoàn."
                    banner = Banner(code: code, text: "Đơn không ở trạng thái chờ hoàn", color: Theme.warning, sub: hint)
                }
            } else if kind == .newOrder {
                item.status = .success
                item.scanConfirmed = true
                item.detail = "Dữ liệu mẫu: đã ghi nhận đơn mới cục bộ."
                banner = Banner(code: code, text: "Đã tạo đơn mẫu cục bộ", color: Theme.teal, sub: "Không gửi gì lên máy tính")
            } else {
                item.status = .notFound
                item.scanConfirmed = true
                item.detail = "Dữ liệu mẫu: chưa thấy mã này trong danh sách mẫu."
                banner = Banner(code: code, text: "Chưa thấy mã này trong dữ liệu mẫu", color: Theme.danger, sub: "Thử quét hoặc nhập mã bắt đầu bằng DEMO-")
            }
            items.insert(item, at: 0)
            if Self.pref("hapticOn") { UIImpactFeedbackGenerator(style: .medium).impactOccurred() }
            return true
        }

        if cloudActive { return submitCloud(code: code, kind: kind, photo: photo, useSnapshot: useSnapshot) }

        if let existing = items.first(where: { $0.code == code && $0.kind == kind && !$0.scanConfirmed }) {
            banner = Banner(code: code, text: "Đang gửi lại, chờ PC xác nhận", color: Theme.warning)
            transmit(existing.requestId)
            return true
        }

        // Tạo đơn mới: đơn đã có trên điện thoại (bảng chính, kho lưu trữ hoặc bảng đơn hủy) thì báo luôn, không tạo lại.
        if kind != .returnScan, snapshot.hasData, let r = snapshot.lookup(code) {
            let where_ = r.k == "a" ? "Kho lưu trữ" : (r.k == "c" ? "Bảng đơn hủy" : "Bảng chính")
            banner = Banner(code: code, text: "Đã tạo rồi: đơn đã có ở \(where_), không tạo lại", color: Theme.danger, sub: r.n)
            if Self.pref("hapticOn") { UINotificationFeedbackGenerator().notificationOccurred(.warning) }
            return false
        }

        let rid = nextRequestId(prefix: "sc")
        var photoData = photo
        if kind != .newOrder && photoData == nil && useSnapshot && Self.pref("sendPhoto") { photoData = snapshotProvider?() }
        if kind == .newOrder { photoData = nil }  // đơn mới không cần ảnh; ảnh nằm ở bước quét hoàn kho
        var savedFile: String? = nil
        if let d = photoData, !d.isEmpty {
            let fileName = rid + ".jpg"
            do {
                try d.write(to: photosDir.appendingPathComponent(fileName), options: .atomic)
                savedFile = fileName
            } catch {
                savedFile = nil
            }
        }
        let scanMs = nowMs()
        let scanDate = Date(timeIntervalSince1970: Double(scanMs) / 1000)
        let item = ScanItem(requestId: rid, code: code, kind: kind, time: scanMs, photoFile: savedFile, note: kind == .newOrder ? trimmedNote : "")
        items.insert(item, at: 0)
        trimHistory()
        // Ghi bền ngay lập tức để tắt app đột ngột ngay sau lúc quét không bị mất lượt quét khỏi hàng đợi.
        flushPersist()

        // Quy tắc 2 & 3: chỉ đánh dấu cục bộ khi khớp DUY NHẤT 1 kiện đang chờ (trùng >= 2 kiện -> để PC báo ambiguous).
        let hint = localHint(code: code, kind: kind)
        if kind == .returnScan && snapshot.hasData && snapshot.isAmbiguousPending(code) {
            banner = Banner(code: code, text: "Trùng nhiều đơn đang chờ, chờ PC kiểm tra", color: Theme.danger, sub: hint)
        } else if kind == .returnScan, snapshot.hasData, let r = snapshot.lookup(code), r.rv {
            banner = Banner(code: code, text: r.isCancelled ? "Đơn hủy: đã quét trước đó" : "Quét trùng: đơn đã nhận hoàn kho trước đó", color: Theme.danger, sub: hint)
        } else if kind == .returnScan, snapshot.hasData, let r = snapshot.lookup(code), r.pd {
            snapshot.applyLocalEdit(rowId: r.id, fields: ["st": "Hàng đã trả"], receivedAt: scanDate)
            banner = Banner(code: code, text: r.isCancelled ? "Đơn hủy: chờ PC ghi nhận" : "Thành công: chờ PC ghi nhận", color: Theme.success, sub: hint)
        } else {
            banner = Banner(code: code, text: "Không có trên điện thoại, chờ PC xác nhận", color: Theme.warning, sub: hint)
        }
        if Self.pref("hapticOn") { UIImpactFeedbackGenerator(style: .medium).impactOccurred() }
        // T040: Chỉ gửi LỆNH QUÉT sang PC; giữ ảnh TẠM trong hàng đợi cho tới khi PC trả ACK (đúng -> gắn vào đơn, sai -> xóa ngay).
        transmit(rid)
        return true
    }

    /// Gợi ý tức thì từ danh sách PC gửi sang (PC vẫn là bên quyết định kết quả).
    private func localHint(code: String, kind: ScanKind) -> String {
        guard kind == .returnScan, snapshot.hasData else { return "" }
        if snapshot.isAmbiguousPending(code) {
            return "Mã này trùng nhiều kiện đang chờ hoàn"
        }
        if let r = snapshot.lookup(code) {
            if r.rv { return "Đơn này đã nhận hoàn kho trước đó: \(r.n)" }
            let v = [r.co, r.sz].filter { !$0.isEmpty }.joined(separator: "/")
            let due = r.due > 0 ? " · " + Deadline.text(due: r.due, now: nowMs()) : ""
            return "Đơn đang chờ hoàn: \(r.n) \(v)\(due)"
        }
        return "Không thấy trong danh sách chờ hoàn (đã nhận hoặc chưa có đơn)"
    }

    // MARK: thao tác đơn (gửi sang PC, giữ bền kèm overlay đến khi PC xác nhận)
    private func storeActionFeedback(key: String, feedback: OrderActionFeedback) {
        if orderActionFeedback.count >= Self.maxActionFeedbackEntries {
            orderActionFeedback.removeAll()
        }
        orderActionFeedback[key] = feedback
    }

    private func setOrderFeedback(for row: OrderRow, requestId: String, state: OrderActionState, message: String) {
        let fb = OrderActionFeedback(requestId: requestId, state: state, message: message)
        if !row.c.isEmpty { storeActionFeedback(key: row.c.uppercased(), feedback: fb) }
        if !row.o.isEmpty { storeActionFeedback(key: row.o.uppercased(), feedback: fb) }
    }

    private func enqueueCommand(_ cmd: PendingCommand) {
        pendingCommands[cmd.requestId] = cmd
        if pendingCommands.count > Self.maxPendingCommands {
            let sorted = pendingCommands.values.sorted { $0.overlay.timeMs < $1.overlay.timeMs }
            for old in sorted.prefix(pendingCommands.count - Self.maxPendingCommands) {
                pendingCommands[old.requestId] = nil
            }
        }
        savePendingCommands()
    }

    private func savePendingCommands() {
        guard !isDemoMode else { return }
        let list = Array(pendingCommands.values)
        let url = pendingCmdsURL
        persistQueue.async {
            if let data = try? JSONEncoder().encode(list) {
                try? data.write(to: url, options: .atomic)
            }
        }
    }

    func sendOrderNote(_ row: OrderRow, note: String, watch: Bool = true) {
        let t = note.trimmingCharacters(in: .whitespacesAndNewlines)
        if isDemoMode {
            snapshot.applyLocalNote(rowId: row.id, note: t, watch: watch)
            let msg = watch ? "Đã lưu theo dõi (dữ liệu mẫu)" : "Đã bỏ theo dõi (dữ liệu mẫu)"
            banner = Banner(code: row.c, text: msg, color: Theme.accent)
            setOrderFeedback(for: row, requestId: "demo-note-\(nowMs())", state: .confirmed, message: msg)
            return
        }
        if cloudActive {
            cloudWrite(rows: [row], ops: [CloudOps.setWatch(id: row.id, on: watch, reason: t)], done: watch ? "Đã lưu theo dõi" : "Đã bỏ theo dõi")
            return
        }
        let rid = nextRequestId(prefix: "note")
        let body = SyncProtocol.orderNote(code: row.c, orderId: row.o, note: t, watch: watch, room: room, requestId: rid)
        let ov = LocalEditOverlay(requestId: rid, action: .note, rowId: row.id, note: t, watch: watch, timeMs: nowMs())
        enqueueCommand(PendingCommand(requestId: rid, room: room, title: "Shopee Note", body: body, overlay: ov))
        broadcast(body, title: "Shopee Note", room: room)
        snapshot.applyLocalNote(rowId: row.id, note: t, watch: watch)
        let msg = "Đang chờ máy tính xác nhận ghi chú..."
        banner = Banner(code: row.c, text: "Đã gửi ghi chú, chờ PC xác nhận", color: Theme.warning)
        setOrderFeedback(for: row, requestId: rid, state: .pending, message: msg)
    }

    func sendOrderEdit(_ row: OrderRow, fields: [String: Any]) {
        if isDemoMode {
            snapshot.applyLocalEdit(rowId: row.id, fields: fields)
            let msg = "Đã cập nhật trạng thái (dữ liệu mẫu)"
            banner = Banner(code: row.c, text: msg, color: Theme.accent)
            setOrderFeedback(for: row, requestId: "demo-edit-\(nowMs())", state: .confirmed, message: msg)
            return
        }
        if cloudActive {
            guard let st = fields["st"] as? String else { return }
            cloudWrite(rows: [row], ops: [CloudOps.setStatus(id: row.id, status: st)], done: "Đã cập nhật trạng thái")
            return
        }
        let rid = nextRequestId(prefix: "edit")
        let body = SyncProtocol.orderEdit(code: row.c, orderId: row.o, fields: fields, room: room, requestId: rid)
        let st = fields["st"] as? String
        let ov = LocalEditOverlay(requestId: rid, action: .editStatus, rowId: row.id, status: st, timeMs: nowMs())
        enqueueCommand(PendingCommand(requestId: rid, room: room, title: "Shopee Edit", body: body, overlay: ov))
        broadcast(body, title: "Shopee Edit", room: room)
        snapshot.applyLocalEdit(rowId: row.id, fields: fields)
        let msg = "Đang chờ máy tính xác nhận đổi trạng thái..."
        banner = Banner(code: row.c, text: "Đã gửi cập nhật đơn, chờ PC xác nhận", color: Theme.warning)
        setOrderFeedback(for: row, requestId: rid, state: .pending, message: msg)
    }

    /// Sửa cùng lúc nhiều đơn (vd đánh dấu đã nhận các đơn Huỷ sau chuẩn bị). Mỗi đơn 1 mã yêu cầu riêng.
    func sendOrderEdits(_ rows: [OrderRow], fields: [String: Any]) {
        guard !rows.isEmpty else { return }
        if isDemoMode {
            for row in rows { snapshot.applyLocalEdit(rowId: row.id, fields: fields) }
            banner = Banner(code: "\(rows.count) đơn", text: "Đã cập nhật cục bộ (dữ liệu mẫu)", color: Theme.accent)
            return
        }
        if cloudActive {
            guard let st = fields["st"] as? String else { return }
            cloudWrite(rows: rows, ops: rows.map { CloudOps.setStatus(id: $0.id, status: st) }, done: "Đã cập nhật \(rows.count) đơn")
            return
        }
        let st = fields["st"] as? String
        let stamp = nowMs()
        for row in rows {
            let rid = nextRequestId(prefix: "edit")
            let body = SyncProtocol.orderEdit(code: row.c, orderId: row.o, fields: fields, room: room, requestId: rid)
            let ov = LocalEditOverlay(requestId: rid, action: .editStatus, rowId: row.id, status: st, timeMs: stamp)
            pendingCommands[rid] = PendingCommand(requestId: rid, room: room, title: "Shopee Edit", body: body, overlay: ov)
            broadcast(body, title: "Shopee Edit", room: room)
            snapshot.applyLocalEdit(rowId: row.id, fields: fields)
        }
        if pendingCommands.count > Self.maxPendingCommands {
            let sorted = pendingCommands.values.sorted { $0.overlay.timeMs < $1.overlay.timeMs }
            for old in sorted.prefix(pendingCommands.count - Self.maxPendingCommands) {
                pendingCommands[old.requestId] = nil
            }
        }
        savePendingCommands()
        banner = Banner(code: "\(rows.count) đơn", text: "Đã gửi cập nhật đơn, chờ PC xác nhận", color: Theme.warning)
    }

    func sendOrderDelete(_ row: OrderRow) {
        if isDemoMode {
            snapshot.removeLocal(rowId: row.id)
            banner = Banner(code: row.c, text: "Đã xóa cục bộ (dữ liệu mẫu)", color: Theme.accent)
            return
        }
        if cloudActive {
            cloudWrite(rows: [row], ops: [CloudOps.delete(id: row.id)], done: "Đã xóa đơn (khôi phục được trên máy tính)")
            return
        }
        let rid = nextRequestId(prefix: "del")
        let body = SyncProtocol.orderDelete(code: row.c, orderId: row.o, room: room, requestId: rid)
        let ov = LocalEditOverlay(requestId: rid, action: .delete, rowId: row.id, timeMs: nowMs())
        enqueueCommand(PendingCommand(requestId: rid, room: room, title: "Shopee Delete", body: body, overlay: ov))
        broadcast(body, title: "Shopee Delete", room: room)
        snapshot.removeLocal(rowId: row.id)
        banner = Banner(code: row.c, text: "Đã gửi xóa đơn, chờ PC xác nhận", color: Theme.warning)
        setOrderFeedback(for: row, requestId: rid, state: .pending, message: "Đang chờ máy tính xác nhận xóa đơn...")
    }

    private func resendPendingCommands() {
        guard !isDemoMode, !room.isEmpty else { return }
        let currentRoom = room
        for cmd in pendingCommands.values where (cmd.room.isEmpty || cmd.room == currentRoom) && cmd.attempts < 8 {
            broadcast(cmd.body, title: cmd.title, room: currentRoom)
        }
    }

    /// Gói thay đổi không khớp bản gốc: xin danh sách đầy đủ, nhưng không quá 1 lần mỗi 45 giây và không khi đang ghép dở.
    private var lastFullRequestAt = Date.distantPast
    private func requestFullIfNeeded() {
        if isDemoMode || room.isEmpty { return }
        if snapshot.incomplete { return }
        if Date().timeIntervalSince(lastFullRequestAt) < 45 { return }
        lastFullRequestAt = Date()
        requestSnapshot()
    }

    func requestSnapshot() {
        guard !isDemoMode, !room.isEmpty else { return }
        let r = room
        let body = SyncProtocol.snapshotRequest(room: r, now: nowMs())
        broadcast(body, title: "Shopee Snapshot Request", room: r)
    }

    /// Bảng đơn hủy: đánh dấu đã nhận / chưa nhận các đơn đã chọn.
    func setCancelledReceived(ids: [String], received: Bool) {
        guard !ids.isEmpty else { return }
        if isDemoMode {
            snapshot.applyCancelledReceivedLocal(ids: Set(ids), received: received)
            banner = Banner(code: "\(ids.count) đơn hủy", text: received ? "Đã đánh dấu đã nhận (dữ liệu mẫu)" : "Đã đánh dấu chưa nhận (dữ liệu mẫu)", color: Theme.accent)
            return
        }
        if cloudActive {
            let rows = ids.compactMap { snapshot.row(byId: $0) }
            cloudWrite(rows: rows, ops: ids.map { CloudOps.cancelReceive(id: $0, received: received) },
                       done: received ? "Đã đánh dấu đã nhận \(ids.count) đơn hủy" : "Đã đánh dấu chưa nhận \(ids.count) đơn hủy",
                       code: "\(ids.count) đơn hủy")
            return
        }
        let rid = nextRequestId(prefix: "cxl")
        let body = SyncProtocol.cancelReceive(ids: ids, received: received, room: room, requestId: rid)
        let ov = LocalEditOverlay(requestId: rid, action: .cancelReceive, cancelIds: ids, received: received, timeMs: nowMs())
        enqueueCommand(PendingCommand(requestId: rid, room: room, title: "Shopee Edit", body: body, overlay: ov))
        broadcast(body, title: "Shopee Edit", room: room)
        snapshot.applyCancelledReceivedLocal(ids: Set(ids), received: received)
        banner = Banner(code: "\(ids.count) đơn hủy", text: received ? "Đã gửi đánh dấu đã nhận, chờ PC" : "Đã gửi đánh dấu chưa nhận, chờ PC", color: Theme.warning)
    }

    // MARK: thống kê & xuất
    func todayStats(kind: ScanKind) -> (total: Int, valid: Int, dup: Int, missing: Int, pending: Int) {
        let start = Int(Calendar.current.startOfDay(for: Date()).timeIntervalSince1970 * 1000)
        var total = 0, valid = 0, dup = 0, missing = 0, pending = 0
        for it in items where it.kind == kind {
            if !it.isSettled { pending += 1 }
            if it.time >= start {
                total += 1
                switch it.status {
                case .success: valid += 1
                case .duplicate: dup += 1
                case .notFound, .ambiguous: missing += 1
                default: break
                }
            }
        }
        return (total, valid, dup, missing, pending)
    }

    private static let csvFormatter: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "dd/MM/yyyy HH:mm:ss"
        return f
    }()

    func csv(kind: ScanKind) -> String {
        Self.buildCSV(items: items, kind: kind)
    }

    nonisolated static func buildCSV(items: [ScanItem], kind: ScanKind) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "dd/MM/yyyy HH:mm:ss"
        var out = "Mã,Thời gian,Trạng thái,Chi tiết\n"
        out.reserveCapacity(items.count * 64)
        for it in items where it.kind == kind {
            let t = f.string(from: Date(timeIntervalSince1970: Double(it.time) / 1000))
            let detail = it.detail.replacingOccurrences(of: "\"", with: "'")
            out += "\(it.code),\(t),\(it.status.rawValue),\"\(detail)\"\n"
        }
        return out
    }

    // MARK: gửi lệnh quét (T039: KHÔNG còn gửi ảnh sang PC; ảnh lưu cục bộ trên điện thoại)
    private func transmit(_ id: String) {
        guard !isDemoMode, !room.isEmpty, !cloudActive else { return }
        guard let i = items.firstIndex(where: { $0.requestId == id }) else { return }
        items[i].lastSendAt = nowMs()
        items[i].attempts += 1
        let it = items[i]
        let r = room
        let body = SyncProtocol.json(SyncProtocol.scanPayload(kind: it.kind, code: it.code, requestId: it.requestId,
            captureId: it.captureId, captureTime: it.time, time: it.time, room: r, note: it.note))
        persist()
        broadcast(body, title: "Shopee Scan", room: r)
    }

    private func resetPendingForReconnect() {
        for i in items.indices where !items[i].isSettled {
            items[i].lastSendAt = 0
        }
    }

    private func tick() async {
        tickCount += 1
        if tickCount % 20 == 0 { snapshot.recount() }   // quá hạn/sắp đến hạn đổi theo thời gian (~1 phút)
        if cloudActive { return }                        // cloud tự đồng bộ qua WebSocket; không gửi lại kiểu MQTT
        if isDemoMode || room.isEmpty { return }
        if snapshotChunksSeen != chunkCounter { snapshotChunksSeen = chunkCounter }
        // Danh sách đơn ghép dở và đã 10 giây không có mảnh mới: xin PC gửi lại (tối đa 4 lần mỗi đợt).
        if snapshot.incomplete, let t = snapshotLastChunkAt, Date().timeIntervalSince(t) > 10, snapshotRetries < 4 {
            snapshotRetries += 1
            snapshotLastChunkAt = Date()
            requestSnapshot()
        } else if !snapshot.incomplete { snapshotRetries = 0 }

        // Gửi lại lệnh sửa cục bộ chưa được PC xác nhận mỗi ~15 giây (5 nhịp).
        if tickCount % 5 == 0 && !pendingCommands.isEmpty && channelUp {
            resendPendingCommands()
        }

        let n = nowMs()
        var needPoll = false
        var scanBudget = 6
        for it in items.reversed() where !it.isSettled {
            if !it.scanConfirmed {
                needPoll = true
                if scanBudget > 0 && n >= it.nextRetryDue() { scanBudget -= 1; transmit(it.requestId) }
            }
        }
        // Hỏi lại ntfy để bắt phản hồi bị lỡ khi mất kết nối.
        if needPoll && tickCount % 3 == 0 && (sseConnected || !mqttConnected) {
            let r = room
            if let lines = try? await client.poll(topic: SyncProtocol.topic(room: r), since: "10m") {
                for l in lines { handleIncoming(l, opened: false) }
            }
        }
    }

    // MARK: vòng đời ảnh tạm -> kho đơn (`Documents/order-photos/`) theo T040

    /// Đối soát toàn bộ ảnh tạm trong hàng đợi theo kết quả PC ACK (T040):
    /// - PC ACK thành công + khớp đúng 1 kiện -> gắn vào kho `Documents/order-photos/`, xóa tệp tạm.
    /// - Không tìm thấy / mơ hồ / lỗi / bị từ chối / quét trùng -> xóa tệp tạm và mọi tham chiếu ngay.
    /// - Chưa có kết quả -> giữ ảnh tạm trong hàng đợi, không xóa.
    func reconcileScanPhotos() {
        guard !isDemoMode else { return }
        let report = photoManager.reconcileScanPhotos(
            items: &items,
            tempPhotosDir: photosDir,
            snapshot: snapshot
        )
        if report.changed {
            persist()
        }
        if report.waitingForFreshSnapshot && channelUp {
            requestFullIfNeeded()
        }
    }

    // MARK: lịch sử (T040: xóa lượt quét -> xóa luôn ảnh tạm; KHÔNG đụng ảnh đã gắn vào đơn trong `Documents/order-photos/`)
    func delete(_ item: ScanItem) {
        if isDemoMode {
            items.removeAll { $0.requestId == item.requestId }
            return
        }
        if ScanPhotoLifecycle.deleteScanItem(requestId: item.requestId, items: &items, tempPhotosDir: photosDir) {
            persist()
        }
    }

    func clear(kind: ScanKind) {
        if isDemoMode {
            items.removeAll { $0.kind == kind }
            return
        }
        if ScanPhotoLifecycle.clearScanItems(kind: kind, items: &items, tempPhotosDir: photosDir) > 0 {
            persist()
        }
    }

    private func trimHistory() {
        let res = ScanItem.trimHistory(items, limit: 800)
        guard !res.dropped.isEmpty else { return }
        // `ScanItem.trimHistory` đã giữ lại mọi mục chưa xác nhận hoặc còn ảnh tạm; ảnh đã gắn nằm an toàn ở `Documents/order-photos/`.
        items = res.kept
    }

    /// Ghi danh sách quét xuống máy: gom nhiều lần gọi liên tiếp thành một, mã hóa + ghi ở luồng nền.
    private var persistScheduled = false
    private let persistQueue = DispatchQueue(label: "items.persist", qos: .utility)

    private func persist() {
        guard !isDemoMode else { return }
        guard !persistScheduled else { return }
        persistScheduled = true
        Task { @MainActor [weak self] in
            try? await Task.sleep(nanoseconds: 300_000_000)
            self?.flushPersist()
        }
    }

    /// Ghi ngay (gọi khi tạo lượt quét mới hoặc khi app chuyển nền để không mất dữ liệu).
    func flushPersist(background: Bool = false) {
        guard !isDemoMode else { return }
        persistScheduled = false
        let itemsCopy = items, url = storeURL
        let cmdsCopy = Array(pendingCommands.values), cmdURL = pendingCmdsURL
        final class OneShotBgTask: @unchecked Sendable {
            private let lock = NSLock()
            private var id: UIBackgroundTaskIdentifier = .invalid
            func set(_ newId: UIBackgroundTaskIdentifier) {
                lock.lock(); id = newId; lock.unlock()
            }
            func finish() {
                lock.lock()
                let current = id
                id = .invalid
                lock.unlock()
                if current != .invalid {
                    DispatchQueue.main.async {
                        UIApplication.shared.endBackgroundTask(current)
                    }
                }
            }
        }
        let bg = OneShotBgTask()
        if background {
            let taskId = UIApplication.shared.beginBackgroundTask(withName: "FlushScanQueue") {
                bg.finish()
            }
            bg.set(taskId)
        }
        persistQueue.async {
            if let data = try? JSONEncoder().encode(itemsCopy) {
                try? data.write(to: url, options: .atomic)
            }
            if let cmdData = try? JSONEncoder().encode(cmdsCopy) {
                try? cmdData.write(to: cmdURL, options: .atomic)
            }
            bg.finish()
        }
    }

    private func nowMs() -> Int { Int(Date().timeIntervalSince1970 * 1000) }
}

// MARK: - T042: luồng cloud (đã ghép thiết bị)
// Cùng tệp với SyncEngine để dùng các hàm private (persist, photosDir, nextRequestId...).
extension SyncEngine {
    /// Gọi trong `start()`: nối callback, khôi phục phiên đã ghép (nếu có) và hiện ngay bản sao đã tải lần trước.
    fileprivate func startCloud() {
        cloud.onChange = { [weak self] in
            Task { @MainActor [weak self] in self?.cloudStatusChanged() }
        }
        cloud.onDataChanged = { [weak self] in
            Task { @MainActor [weak self] in self?.scheduleCloudReload() }
        }
        cloud.start()
        cloudState = cloud.state
        if cloudActive { applyCloudRows() }
    }

    private func cloudStatusChanged() {
        let was = cloudActive
        cloudState = cloud.state
        if !was && cloudActive {
            // vừa ghép: tắt kênh MQTT/ntfy cũ, chuyển hẳn sang cloud
            sseTask?.cancel(); sseTask = nil
            sseConnected = false
            startMQTT()                 // đang cloud: chỉ hủy kết nối cũ, không nối lại
            applyCloudRows()
        } else if was && !cloudActive {
            // quên ghép: quay lại kiểu cũ nếu đã có mã phòng
            snapshot.applyCloud(rows: [], at: Date())
            startSSE()
            startMQTT()
        }
    }

    /// Gom nhiều lô /sync liên tiếp thành một lần dựng lại danh sách.
    private func scheduleCloudReload() {
        guard !cloudReloadScheduled else { return }
        cloudReloadScheduled = true
        Task { @MainActor [weak self] in
            try? await Task.sleep(nanoseconds: 300_000_000)
            guard let self else { return }
            self.cloudReloadScheduled = false
            self.applyCloudRows()
        }
    }

    /// Dựng danh sách dòng từ bản sao cloud (chạy nền) rồi đưa vào `snapshot` mà mọi màn hình đang đọc.

    /// Hỏi cloud bản gợi ý mới (tối đa 5 phút/lần). Bản mới hơn bản đã báo thì hiện thông báo trên iPhone.
    func refreshAIDigest(force: Bool = false) {
        guard cloudActive, let api = cloud.currentAPI, force || Date().timeIntervalSince(digestCheckedAt) > 300 else { return }
        digestCheckedAt = Date()
        Task { @MainActor [weak self] in
            guard let self, let list = try? await api.aiDigests() else { return }
            self.aiDigest = list.max(by: { $0.id < $1.id })
            let key = "aiDigestLastSeen"
            let seen = UserDefaults.standard.object(forKey: key) as? Int
            if let n = CloudAIDigest.toNotify(list, lastSeen: seen) {
                let c = UNMutableNotificationContent()
                c.title = "✨ AI gợi ý lúc \(n.slot)"
                c.body = n.text
                c.sound = .default
                try? await UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: "ai-digest-\(n.id)", content: c,
                    trigger: UNTimeIntervalNotificationTrigger(timeInterval: 1, repeats: false)))
            }
            if let m = list.map({ $0.id }).max() { UserDefaults.standard.set(m, forKey: key) }
        }
    }

    func applyCloudRows() {
        refreshAIDigest()
        let store = cloud.store
        let now = Date()
        let at = cloud.lastSyncAt ?? now
        Task.detached(priority: .userInitiated) { [weak self] in
            let rows = store.rows(now: now)
            await MainActor.run { self?.snapshot.applyCloud(rows: rows, at: at) }
        }
    }

    // MARK: ghép / bỏ ghép

    /// Ghép bằng mã 6 số (hoặc mã QR `qlhh://pair?...`). nil = thành công.
    func pairCloud(baseURL: String, code: String, deviceName: String) async -> String? {
        let err = await cloud.pair(baseURL: baseURL, code: code, deviceName: deviceName)
        cloudStatusChanged()
        return err
    }

    func unpairCloud() {
        cloud.unpair()
        cloudStatusChanged()
    }

    // MARK: quét qua cloud

    fileprivate func submitCloud(code: String, kind: ScanKind, photo: Data?, useSnapshot: Bool) -> Bool {
        guard cloud.canWrite else {
            banner = Banner(code: code, text: "MẤT KẾT NỐI CLOUD: chưa quét được", color: Theme.danger,
                            sub: CloudStatusText.lockReason(cloud.state) ?? "Kiểm tra mạng rồi thử lại")
            if Self.pref("hapticOn") { UINotificationFeedbackGenerator().notificationOccurred(.error) }
            if Self.pref("soundOn") { AudioServicesPlaySystemSound(1053) }
            return false
        }
        let rid = nextRequestId(prefix: "sc")
        var photoData = photo
        if photoData == nil && useSnapshot && Self.pref("sendPhoto") { photoData = snapshotProvider?() }
        var savedFile: String? = nil
        if let d = photoData, !d.isEmpty {
            let fileName = rid + ".jpg"
            if (try? d.write(to: photosDir.appendingPathComponent(fileName), options: .atomic)) != nil { savedFile = fileName }
        }
        var item = ScanItem(requestId: rid, code: code, kind: kind, time: nowMs(), photoFile: savedFile)
        item.detail = "Đang gửi lên cloud..."
        items.insert(item, at: 0)
        trimHistory()
        flushPersist()
        banner = Banner(code: code, text: "Đang gửi lên cloud...", color: Theme.info)
        if Self.pref("hapticOn") { UIImpactFeedbackGenerator(style: .medium).impactOccurred() }

        let fullPhoto = photoData
        if kind != .returnScan {
            // chế độ "Đơn mới": tạo đơn qua cloud (mã đã có ở bất kỳ đâu thì không tạo lại)
            Task { @MainActor [weak self] in
                guard let self else { return }
                let r = await self.cloud.newOrder(code: code)
                if let i = self.items.firstIndex(where: { $0.requestId == rid }) {
                    self.items[i].status = r.status
                    self.items[i].scanConfirmed = true
                    self.items[i].reviewRequired = r.status == .error
                    self.items[i].detail = r.message
                    if !r.orderId.isEmpty { self.items[i].orderNum = r.orderId }
                }
                self.banner = Banner(code: code, text: r.message.isEmpty ? "Không tạo được đơn" : r.message, color: Self.color(r.tone), sub: r.platform)
                if Self.pref("hapticOn") { UINotificationFeedbackGenerator().notificationOccurred(r.status == .success ? .success : .error) }
                self.persist()
            }
            return true
        }
        Task { @MainActor [weak self] in
            guard let self else { return }
            let r = await self.cloud.scan(code: code)
            let v = CloudScanPresentation.map(r)
            if let i = self.items.firstIndex(where: { $0.requestId == rid }) {
                self.items[i].status = v.status
                self.items[i].scanConfirmed = true          // cloud đã trả lời (lỗi: status .error + cần kiểm tra, KHÔNG phải đã nhận)
                self.items[i].reviewRequired = v.reviewRequired
                self.items[i].detail = v.text
                if let p = r.parcel { self.items[i].orderNum = p.orderId }
            }
            self.banner = Banner(code: code, text: v.text, color: Self.color(v.tone), sub: r.parcel?.orderId ?? "")
            if Self.pref("hapticOn") {
                UINotificationFeedbackGenerator().notificationOccurred(v.tone == .success ? .success : (v.tone == .danger ? .error : .warning))
            }
            // ảnh chỉ lên cloud khi cloud xác nhận đúng MỘT kiện (quy tắc 6)
            if CloudPhotoRules.shouldUpload(r), let data = fullPhoto, !data.isEmpty, let api = self.cloud.currentAPI {
                do {
                    _ = try await CloudPhotoUploader(api: api).upload(after: r, full: data, thumb: nil)
                } catch {
                    self.banner = Banner(code: code, text: v.text, color: Self.color(v.tone), sub: "Chưa tải được ảnh lên cloud")
                }
            }
            self.persist()
            self.reconcileScanPhotos()
        }
        return true
    }

    // MARK: sửa đơn qua cloud

    /// Gửi các thao tác ghi lên cloud. Mất kết nối thì từ chối ngay, không đổi gì trên máy (không hàng đợi ngoại tuyến).
    fileprivate func cloudWrite(rows: [OrderRow], ops: [[String: Any]], done: String, code: String? = nil) {
        let label = code ?? (rows.count == 1 ? rows[0].c : "\(rows.count) đơn")
        guard cloud.canWrite else {
            banner = Banner(code: label, text: "MẤT KẾT NỐI CLOUD: chưa lưu được", color: Theme.danger,
                            sub: CloudStatusText.lockReason(cloud.state) ?? "")
            for row in rows { setOrderFeedback(for: row, requestId: "cloud-\(nowMs())", state: .failed, message: "Mất kết nối cloud: chưa lưu.") }
            return
        }
        let rid = nextRequestId(prefix: "cloud")
        for row in rows { setOrderFeedback(for: row, requestId: rid, state: .pending, message: "Đang lưu lên cloud...") }
        Task { @MainActor [weak self] in
            guard let self else { return }
            var failed: String?
            for op in ops {
                if let err = await self.cloud.mutate(op) { failed = err; break }
            }
            for row in rows {
                self.setOrderFeedback(for: row, requestId: rid, state: failed == nil ? .confirmed : .failed, message: failed ?? done)
            }
            self.banner = Banner(code: label, text: failed == nil ? done : "Chưa lưu được", color: failed == nil ? Theme.success : Theme.danger,
                                 sub: failed ?? "")
        }
    }

    fileprivate static func color(_ t: CloudTone) -> Color {
        switch t {
        case .success: return Theme.success
        case .info: return Theme.info
        case .warning: return Theme.warning
        case .danger: return Theme.danger
        case .neutral: return .gray
        }
    }
}

/// Lưu phiên ghép cloud trong Keychain (token KHÔNG nằm trong UserDefaults hay tệp thường; chỉ máy này, sau lần mở khóa đầu).
final class KeychainCloudSessionStore: CloudSessionStoring {
    private let service = "com.quanlyhanghoan.cloud"
    private let account = "session"

    private var query: [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account]
    }

    func load() -> CloudSavedSession? {
        var q = query
        q[kSecReturnData as String] = true
        q[kSecMatchLimit as String] = kSecMatchLimitOne
        var out: AnyObject?
        guard SecItemCopyMatching(q as CFDictionary, &out) == errSecSuccess, let data = out as? Data else { return nil }
        return try? JSONDecoder().decode(CloudSavedSession.self, from: data)
    }

    func save(_ s: CloudSavedSession) {
        guard let data = try? JSONEncoder().encode(s) else { return }
        SecItemDelete(query as CFDictionary)
        var q = query
        q[kSecValueData as String] = data
        q[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        SecItemAdd(q as CFDictionary, nil)
    }

    func clear() { SecItemDelete(query as CFDictionary) }
}
