import SwiftUI
import UIKit
import AudioToolbox
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
    @Published var orderActionFeedback: [String: OrderActionFeedback] = [:]
    @Published var mode: ScanKind = .returnScan
    @Published var note = ""
    /// Chế độ xem thử dữ liệu mẫu (trong bộ nhớ, tuyệt đối không phát MQTT/ntfy hay ghi đè dữ liệu thật).
    @Published private(set) var isDemoMode = false
    private var savedRealItems: [ScanItem]?

    var snapshotProvider: (() -> Data?)?
    let snapshot = SnapshotStore()

    private let client = NtfyClient()
    private var mqttClients: [MQTTClient] = []
    private var mqttTasks: [Task<Void, Never>] = []
    private var mqttGeneration = 0
    private var seenMsgs: [String: Date] = [:]
    private var snapshotRetries = 0
    private var sseTask: Task<Void, Never>?
    private var tickTask: Task<Void, Never>?
    private var seq = 0
    private let sessionId = String(UUID().uuidString.prefix(6)).lowercased()
    private var lastRawCode = ""
    private var lastRawTime = Date.distantPast
    private var tickCount = 0

    private let docs = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0]
    private var storeURL: URL { docs.appendingPathComponent("scan_items.json") }
    private var photosDir: URL { docs.appendingPathComponent("photos", isDirectory: true) }

    init() {
        room = SyncProtocol.normalizeRoom(UserDefaults.standard.string(forKey: "room") ?? "")   // v2: không có mã phòng mặc định
        try? FileManager.default.createDirectory(at: photosDir, withIntermediateDirectories: true)
        if let data = try? Data(contentsOf: storeURL), let list = try? JSONDecoder().decode([ScanItem].self, from: data) {
            items = list
        }
    }

    /// Tuỳ chọn bật/tắt trong Cài đặt (mặc định bật).
    static func pref(_ key: String) -> Bool { UserDefaults.standard.object(forKey: key) as? Bool ?? true }

    /// Bật/tắt chế độ xem thử dữ liệu mẫu. Tắt sẽ khôi phục nguyên vẹn lịch sử và danh sách đơn thật.
    func setDemoMode(_ on: Bool) {
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

    /// Gửi lại một mã chưa được PC xác nhận (và ảnh nếu có).
    func resend(_ requestId: String) {
        guard !isDemoMode else { return }
        guard let i = items.firstIndex(where: { $0.requestId == requestId }) else { return }
        let it = items[i]
        if it.photoFile != nil && !it.photoConfirmed { items[i].photoAttempts = 0 }   // cho phép tự thử gửi ảnh lại
        transmit(requestId)
        if it.photoFile != nil && !it.photoConfirmed && !it.reviewRequired { transmitPhoto(requestId) }
        banner = Banner(code: it.code, text: "Đã gửi lại, đang chờ PC xác nhận", color: Theme.warning)
    }

    /// Đưa tất cả lượt quét chưa được máy tính xác nhận (và ảnh chưa gửi xong) vào hàng gửi lại ngay.
    /// Mỗi nhịp app gửi tối đa 6 lượt quét + 1 ảnh nên không dồn cục; 100 đơn mất khoảng nửa phút.
    @discardableResult
    func resendAllPending(kind: ScanKind? = nil) -> Int {
        guard !isDemoMode else { return 0 }
        var n = 0
        for i in items.indices where !items[i].isSettled && (kind == nil || items[i].kind == kind) {
            n += 1
            if !items[i].scanConfirmed { items[i].lastSendAt = 0; items[i].attempts = 0 }
            if items[i].photoFile != nil && !items[i].photoConfirmed && !items[i].reviewRequired {
                items[i].photoAttempts = 0; items[i].photoSentAt = 0
            }
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
    /// Ghi nhận máy tính còn online. Chỉ cập nhật tối đa 5 giây một lần: mỗi mảnh danh sách đều gọi hàm này,
    /// cập nhật liên tục làm mọi màn hình (cả màn Quét có camera) vẽ lại hàng trăm lần mỗi lần đồng bộ -> nóng máy, giật.
    private func markPcSeen() {
        let now = Date()
        if let t = pcLastSeen, now.timeIntervalSince(t) < 5 { return }
        pcLastSeen = now
    }

    func photoURL(_ item: ScanItem) -> URL? { item.photoFile.map { photosDir.appendingPathComponent($0) } }

    // MARK: vòng đời
    func start() {
        snapshot.onNeedFull = { [weak self] in self?.requestFullIfNeeded() }
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
        guard sseTask == nil else { return }
        startSSE()
        startMQTT()
        tickTask = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: 3_000_000_000)
                await self?.tick()
            }
        }
    }

    func setRoom(_ raw: String) {
        let r = SyncProtocol.normalizeRoom(raw)
        guard r != room else { return }
        room = r
        UserDefaults.standard.set(r, forKey: "room")
        pcLastSeen = nil
        sseTask?.cancel(); sseTask = nil
        startSSE()
        startMQTT()
    }

    private func startSSE() {
        if room.isEmpty { return }   // v2: chưa cài mã phòng thì không nối
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
        if room.isEmpty { return }   // v2: chưa cài mã phòng thì không nối (không có mã phòng mặc định)
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
        guard let msg = SyncProtocol.parseMessage(raw, room: room) else { return }
        dispatch(msg)
    }

    /// Gửi một bản tin qua mọi kênh đang có. ntfy chỉ dùng khi nó đang nối, hoặc khi MQTT chưa nối được.
    private func broadcast(_ body: String, title: String, room r: String) {
        guard !isDemoMode else { return }
        for c in mqttClients { c.publish(topic: SyncProtocol.mqttTopic(room: r), body: body) }
        if sseConnected || !mqttConnected {
            Task { try? await client.post(topic: SyncProtocol.topic(room: r), title: title, body: body) }
        }
    }

    /// Kênh vừa nối: báo cho PC biết, gửi lại hàng đợi, xin danh sách đơn nếu cũ.
    private func channelOpened() {
        let hello = SyncProtocol.json(["type": "phone_connected", "room": room, "time": nowMs()])
        broadcast(hello, title: "Shopee Connect", room: room)
        resendAllPending()
        resendPendingNotes()
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
        // Giải mã JSON (mỗi mảnh danh sách đơn khá lớn) ở luồng nền, chỉ phần xử lý kết quả mới về luồng giao diện.
        let r = room
        Task.detached(priority: .userInitiated) { [weak self] in
            guard let msg = SyncProtocol.parseIncoming(sseData: data, room: r) else { return }
            await self?.dispatch(msg)
        }
    }

    private func dispatch(_ msg: PcMessage) {
        switch msg {
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
            // PC đã trả lời (thành công hay từ chối) thì thôi gửi lại.
            pendingNotes[ack.requestId] = nil
            UserDefaults.standard.set(pendingNotes, forKey: "pendingNotes")
            let ok = ack.outcome == "success" || ack.outcome == "duplicate"
            let msg = ack.extra ?? (ok ? "PC đã cập nhật" : "PC không thực hiện được")
            banner = Banner(code: ack.code, text: msg, color: ok ? .green : .red)
            let codeKey = ack.code.uppercased()
            if !codeKey.isEmpty {
                orderActionFeedback[codeKey] = OrderActionFeedback(
                    requestId: ack.requestId,
                    state: ok ? .confirmed : .failed,
                    message: msg
                )
            }
            for (k, v) in orderActionFeedback where v.requestId == ack.requestId {
                orderActionFeedback[k] = OrderActionFeedback(
                    requestId: ack.requestId,
                    state: ok ? .confirmed : .failed,
                    message: msg
                )
            }
            if !ok { requestSnapshot() }   // lấy lại bản đúng từ PC nếu PC từ chối

        case .ack(let ack) where ack.ackStage == "push":
            markPcSeen()
            if isDuplicate("p|\(ack.requestId)|\(ack.code)|\(ack.outcome)|\(ack.extra ?? "")") { return }
            let ok = ack.outcome == "success" || ack.outcome == "duplicate"
            if ok { for i in items.indices where items[i].kind == .newOrder && items[i].code == ack.code { items[i].pushed = true } ; persist() }
            banner = Banner(code: ack.code, text: ack.extra ?? (ok ? "Đã đẩy ra bảng chính" : "Chưa đẩy được"), color: ok ? .green : .red)
        case .ack(let ack):
            markPcSeen()
            let saved = ack.photoSaved == true ? "1" : "0"
            let stage = ack.ackStage ?? ""
            let cap = ack.captureId ?? ""
            let extra = ack.extra ?? ""
            if isDuplicate("a|\(ack.requestId)|\(ack.code)|\(ack.outcome)|\(stage)|\(cap)|\(saved)|\(extra)") { return }
            guard let i = items.firstIndex(where: { $0.requestId == ack.requestId }) else { return }
            if items[i].apply(ack) {
                persist()
                showResult(items[i])
            }
        default:
            break
        }
    }

    private func describe(_ it: ScanItem) -> (String, Color) {
        switch it.status {
        case .success: return (it.kind == .returnScan ? "Hợp lệ, PC đã ghi nhận" : "Đã tạo đơn mới trên PC", Theme.success)
        case .duplicate: return ("Mã này đã quét trùng trước đó", Theme.warning)
        case .notFound: return ("Không tìm thấy đơn trên hệ thống", Theme.danger)
        case .ambiguous: return ("Trùng nhiều đơn, cần kiểm tra trên PC", Theme.danger)
        case .error: return (it.scanConfirmed ? "PC từ chối, cần kiểm tra lại" : "PC chưa lưu được, đang tự thử lại", Theme.danger)
        case .pending: return ("Đang chờ máy tính xác nhận", Theme.warning)
        }
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
    func handleCameraCode(_ raw: String) {
        // Camera hay đọc nhầm hoa văn thành mã vạch: bỏ kết quả không giống mã vận đơn (nhập tay không bị lọc).
        guard TrackingCode.isPlausibleScan(raw) else { return }
        // Camera vẫn nhìn thấy cùng nhãn sau khi đã quét: không tạo thêm lượt "quét trùng" khi nhãn vẫn nằm trước camera.
        // Khóa theo cả chế độ quét: đổi sang chế độ khác (Tạo đơn mới, Đơn hủy…) thì quét lại cùng nhãn phải nhận ngay.
        let c = "\(mode.rawValue)|" + TrackingCode.clean(raw), now = Date()
        // Mỗi lần camera còn thấy nhãn thì mốc được gia hạn: chỉ khi nhãn rời khung hình hơn 1 giây rồi quay lại mới tính là lượt quét mới.
        if c == lastCameraCode && now.timeIntervalSince(lastCameraTime) < 1.0 { lastCameraTime = now; return }
        lastCameraCode = c; lastCameraTime = now
        _ = submit(raw: raw, kind: mode, photo: nil, useSnapshot: true)
    }

    func submitManual(_ raw: String) {
        _ = submit(raw: raw, kind: mode, photo: nil, useSnapshot: true)
    }

    @discardableResult
    func submit(raw: String, kind: ScanKind, photo: Data?, useSnapshot: Bool) -> Bool {
        let code = TrackingCode.clean(raw)
        guard code.count >= 6 else { return false }
        let now = Date()
        let rawKey = "\(kind.rawValue)|" + code
        if rawKey == lastRawCode && now.timeIntervalSince(lastRawTime) < 1.2 { return false }
        lastRawCode = rawKey; lastRawTime = now

        if isDemoMode {
            seq += 1
            let scanMs = nowMs()
            let scanDate = Date(timeIntervalSince1970: Double(scanMs) / 1000)
            let rid = "demo-\(seq)-\(scanMs % 100000)"
            var item = ScanItem(requestId: rid, code: code, kind: kind, time: scanMs, photoFile: nil)
            let hint = localHint(code: code, kind: kind)
            if kind == .returnScan, let r = snapshot.lookup(code) {
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

        if let existing = items.first(where: { $0.code == code && $0.kind == kind && !$0.scanConfirmed }) {
            banner = Banner(code: code, text: "Đang gửi lại, chờ PC xác nhận", color: Theme.warning)
            transmit(existing.requestId)
            return true
        }

        // Tạo đơn mới / đơn hủy: đơn đã có trên điện thoại (bảng chính, kho lưu trữ hoặc bảng đơn hủy) thì báo luôn, không tạo lại.
        if kind != .returnScan, snapshot.hasData, let r = snapshot.lookup(code) {
            let where_ = r.k == "a" ? "Kho lưu trữ" : (r.k == "c" ? "Bảng đơn hủy" : "Bảng chính")
            banner = Banner(code: code, text: "Đơn đã có ở \(where_), không tạo lại", color: Theme.warning, sub: r.n)
            if Self.pref("hapticOn") { UINotificationFeedbackGenerator().notificationOccurred(.warning) }
            return false
        }

        seq += 1
        let rid = "\(sessionId)-\(seq)-\(nowMs() % 100000)"
        var photoData = photo
        if kind != .newOrder && photoData == nil && useSnapshot && Self.pref("sendPhoto") { photoData = snapshotProvider?() }
        if kind == .newOrder { photoData = nil }  // đơn mới không cần ảnh; ảnh nằm ở bước quét hoàn kho
        var file: String? = nil
        if let d = photoData {
            file = rid + ".jpg"
            // File ảnh phải có trước khi gửi: ghi ngay (vài chục KB, nhanh) nhưng không mã hóa/ghi cả danh sách ở đây nữa.
            try? d.write(to: photosDir.appendingPathComponent(file!))
        }
        let scanMs = nowMs()
        let scanDate = Date(timeIntervalSince1970: Double(scanMs) / 1000)
        let item = ScanItem(requestId: rid, code: code, kind: kind, time: scanMs, photoFile: file)
        items.insert(item, at: 0)
        trimHistory()
        persist()
        // Dùng dữ liệu đã lưu trên điện thoại ngay cả khi máy tính tắt: có đơn đang chờ thì đánh dấu đã nhận luôn,
        // lệnh quét vẫn nằm trong hàng chờ và tự gửi sang máy tính khi máy tính bật. Chưa có đơn trên điện thoại thì chờ máy tính xác nhận.
        let hint = localHint(code: code, kind: kind)
        if kind == .returnScan, snapshot.hasData, let r = snapshot.lookup(code), r.rv {
            banner = Banner(code: code, text: "Quét trùng: đơn đã nhận hoàn kho trước đó", color: Theme.danger, sub: hint)
        } else if kind == .returnScan, snapshot.hasData, let r = snapshot.lookup(code), r.pd {
            snapshot.applyLocalEdit(rowId: r.id, fields: ["st": "Hàng đã trả"], receivedAt: scanDate)
            banner = Banner(code: code, text: "Đã nhận hoàn kho, chờ PC ghi nhận", color: Theme.success, sub: hint)
        } else {
            banner = Banner(code: code, text: "Chưa có trên điện thoại, chờ PC xác nhận", color: Theme.warning, sub: hint)
        }
        if Self.pref("hapticOn") { UIImpactFeedbackGenerator(style: .medium).impactOccurred() }
        transmit(rid)
        if file != nil { transmitPhoto(rid) }
        return true
    }

    /// Gợi ý tức thì từ danh sách PC gửi sang (PC vẫn là bên quyết định kết quả).
    private func localHint(code: String, kind: ScanKind) -> String {
        guard kind == .returnScan, snapshot.hasData else { return "" }
        if let r = snapshot.lookup(code) {
            if r.rv { return "Đơn này đã nhận hoàn kho trước đó: \(r.n)" }
            let v = [r.co, r.sz].filter { !$0.isEmpty }.joined(separator: "/")
            let due = r.due > 0 ? " · " + Deadline.text(due: r.due, now: nowMs()) : ""
            return "Đơn đang chờ hoàn: \(r.n) \(v)\(due)"
        }
        return "Không thấy trong danh sách chờ hoàn (đã nhận hoặc chưa có đơn)"
    }

    // MARK: ghi chú đơn (gửi sang PC, giữ trong hàng đợi đến khi PC xác nhận)
    private var pendingNotes: [String: String] = UserDefaults.standard.dictionary(forKey: "pendingNotes") as? [String: String] ?? [:]
    var pendingNoteCount: Int { pendingNotes.count }

    private func setOrderFeedback(for row: OrderRow, requestId: String, state: OrderActionState, message: String) {
        let fb = OrderActionFeedback(requestId: requestId, state: state, message: message)
        if !row.c.isEmpty { orderActionFeedback[row.c.uppercased()] = fb }
        if !row.o.isEmpty { orderActionFeedback[row.o.uppercased()] = fb }
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
        let rid = "note-\(nowMs())"
        let body = SyncProtocol.orderNote(code: row.c, orderId: row.o, note: t, watch: watch, room: room, requestId: rid)
        pendingNotes[rid] = body
        UserDefaults.standard.set(pendingNotes, forKey: "pendingNotes")
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
        let rid = "edit-\(nowMs())"
        let body = SyncProtocol.orderEdit(code: row.c, orderId: row.o, fields: fields, room: room, requestId: rid)
        pendingNotes[rid] = body
        UserDefaults.standard.set(pendingNotes, forKey: "pendingNotes")
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
        let base = nowMs()
        for (i, row) in rows.enumerated() {
            let rid = "edit-\(base)-\(i)"
            let body = SyncProtocol.orderEdit(code: row.c, orderId: row.o, fields: fields, room: room, requestId: rid)
            pendingNotes[rid] = body
            broadcast(body, title: "Shopee Edit", room: room)
            snapshot.applyLocalEdit(rowId: row.id, fields: fields)
        }
        UserDefaults.standard.set(pendingNotes, forKey: "pendingNotes")
        banner = Banner(code: "\(rows.count) đơn", text: "Đã gửi cập nhật đơn, chờ PC xác nhận", color: Theme.warning)
    }

    func sendOrderDelete(_ row: OrderRow) {
        if isDemoMode {
            snapshot.removeLocal(rowId: row.id)
            banner = Banner(code: row.c, text: "Đã xóa cục bộ (dữ liệu mẫu)", color: Theme.accent)
            return
        }
        let rid = "del-\(nowMs())"
        let body = SyncProtocol.orderDelete(code: row.c, orderId: row.o, room: room, requestId: rid)
        pendingNotes[rid] = body
        UserDefaults.standard.set(pendingNotes, forKey: "pendingNotes")
        broadcast(body, title: "Shopee Delete", room: room)
        snapshot.removeLocal(rowId: row.id)
        banner = Banner(code: row.c, text: "Đã gửi xóa đơn, chờ PC xác nhận", color: Theme.warning)
        setOrderFeedback(for: row, requestId: rid, state: .pending, message: "Đang chờ máy tính xác nhận xóa đơn...")
    }

    private func resendPendingNotes() {
        guard !isDemoMode else { return }
        for b in pendingNotes.values { broadcast(b, title: "Shopee Note", room: room) }
    }

    /// Gói thay đổi không khớp bản gốc: xin danh sách đầy đủ, nhưng không quá 1 lần mỗi 45 giây và không khi đang ghép dở.
    private var lastFullRequestAt = Date.distantPast
    private func requestFullIfNeeded() {
        if isDemoMode { return }
        if snapshot.incomplete { return }
        if Date().timeIntervalSince(lastFullRequestAt) < 45 { return }
        lastFullRequestAt = Date()
        requestSnapshot()
    }

    func requestSnapshot() {
        guard !isDemoMode else { return }
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
        let rid = "cxl-\(nowMs())"
        let body = SyncProtocol.cancelReceive(ids: ids, received: received, room: room, requestId: rid)
        pendingNotes[rid] = body
        UserDefaults.standard.set(pendingNotes, forKey: "pendingNotes")
        broadcast(body, title: "Shopee Edit", room: room)
        snapshot.applyCancelledReceivedLocal(ids: Set(ids), received: received)
        banner = Banner(code: "\(ids.count) đơn hủy", text: received ? "Đã gửi đánh dấu đã nhận, chờ PC" : "Đã gửi đánh dấu chưa nhận, chờ PC", color: Theme.warning)
    }

    // MARK: thống kê & xuất
    func todayStats(kind: ScanKind) -> (total: Int, valid: Int, dup: Int, missing: Int, pending: Int) {
        let start = Int(Calendar.current.startOfDay(for: Date()).timeIntervalSince1970 * 1000)
        let list = items.filter { $0.kind == kind && $0.time >= start }
        return (list.count, list.filter { $0.status == .success }.count, list.filter { $0.status == .duplicate }.count,
                list.filter { $0.status == .notFound || $0.status == .ambiguous }.count, items.filter { $0.kind == kind && !$0.isSettled }.count)
    }

    func csv(kind: ScanKind) -> String {
        let f = DateFormatter(); f.dateFormat = "dd/MM/yyyy HH:mm:ss"
        var out = "Mã,Thời gian,Trạng thái,Chi tiết\n"
        for it in items where it.kind == kind {
            let t = f.string(from: Date(timeIntervalSince1970: Double(it.time) / 1000))
            let detail = it.detail.replacingOccurrences(of: "\"", with: "'")
            out += "\(it.code),\(t),\(it.status.rawValue),\"\(detail)\"\n"
        }
        return out
    }

    // MARK: gửi
    private func transmit(_ id: String) {
        guard !isDemoMode else { return }
        guard let i = items.firstIndex(where: { $0.requestId == id }) else { return }
        items[i].lastSendAt = nowMs()
        items[i].attempts += 1
        let it = items[i]
        let r = room
        let body = SyncProtocol.json(SyncProtocol.scanPayload(kind: it.kind, code: it.code, requestId: it.requestId,
            captureId: it.captureId, captureTime: it.time, time: it.time, room: r, note: note))
        persist()
        broadcast(body, title: "Shopee Scan", room: r)
    }

    private func transmitPhoto(_ id: String) {
        guard !isDemoMode else { return }
        guard let i = items.firstIndex(where: { $0.requestId == id }), let file = items[i].photoFile else { return }
        items[i].photoSentAt = nowMs()
        items[i].photoAttempts += 1
        let it = items[i]
        let r = room
        persist()
        let url = photosDir.appendingPathComponent(file)
        let stamp = nowMs()
        // Ảnh chỉ đi qua MỘT broker MQTT (không nhân ba); ntfy chỉ dùng khi nó đang nối hoặc MQTT chưa nối được.
        let mq = mqttClients.last(where: { $0.isConnected })
        let useNtfy = sseConnected || mq == nil
        let mqTopic = SyncProtocol.mqttTopic(room: r)
        let ntfyTopic = SyncProtocol.topic(room: r)
        let client = self.client
        // Đọc file, mã hóa base64 và cắt mảnh ở luồng nền (trước đây làm ngay trên luồng giao diện).
        Task.detached(priority: .utility) {
            guard let data = try? Data(contentsOf: url) else { return }
            let dataURL = "data:image/jpeg;base64," + data.base64EncodedString()
            let chunks = SyncProtocol.photoChunks(dataURL: dataURL, code: it.code, kind: it.kind, requestId: it.requestId,
                captureId: it.captureId, captureTime: it.time, room: r, now: stamp, photoId: "ph_\(stamp)_\(it.attempts)")
            for c in chunks {
                let body = SyncProtocol.json(c)
                mq?.publish(topic: mqTopic, body: body)
                if useNtfy {
                    try? await client.post(topic: ntfyTopic, title: "Shopee Photo Chunk", body: body)
                    try? await Task.sleep(nanoseconds: 45_000_000)
                } else {
                    try? await Task.sleep(nanoseconds: 15_000_000)
                }
            }
        }
    }

    private func resendAllPending() {
        for i in items.indices where !items[i].isSettled { items[i].lastSendAt = 0 }
    }

    private func tick() async {
        tickCount += 1
        if tickCount % 30 == 0 { snapshot.recount() }   // quá hạn/sắp đến hạn đổi theo thời gian
        if isDemoMode { return }
        if snapshotChunksSeen != chunkCounter { snapshotChunksSeen = chunkCounter }
        // Danh sách đơn ghép dở và đã 10 giây không có mảnh mới: xin PC gửi lại (tối đa 4 lần mỗi đợt).
        if snapshot.incomplete, let t = snapshotLastChunkAt, Date().timeIntervalSince(t) > 10, snapshotRetries < 4 {
            snapshotRetries += 1
            snapshotLastChunkAt = Date()
            requestSnapshot()
        } else if !snapshot.incomplete { snapshotRetries = 0 }
        let n = nowMs()
        var needPoll = false
        // Giới hạn số lượt gửi lại mỗi nhịp, ưu tiên lượt quét cũ nhất: tránh dồn hàng loạt cùng lúc khi nối lại kênh
        // (nhiều lượt quét + ảnh bị đẩy cùng lúc làm kẹt luồng giao diện và có thể làm broker ngắt kết nối).
        var scanBudget = 6, photoBudget = 1
        let backlog = items.reduce(0) { $0 + (($1.isSettled || $1.scanConfirmed) ? 0 : 1) }
        for it in items.reversed() where !it.isSettled {
            if !it.scanConfirmed {
                needPoll = true
                if scanBudget > 0 && n >= it.nextRetryDue() { scanBudget -= 1; transmit(it.requestId) }
            }
            if it.photoFile != nil && !it.photoConfirmed && !it.reviewRequired && it.photoAttempts < 6
                && n - it.photoSentAt > 30_000 && n - it.time > 8_000 {
                needPoll = true
                // Đang còn nhiều lượt quét chưa được xác nhận thì dành đường truyền cho lượt quét trước, ảnh gửi sau.
                if photoBudget > 0 && backlog <= 10 { photoBudget -= 1; transmitPhoto(it.requestId) }
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

    // MARK: lịch sử
    func delete(_ item: ScanItem) {
        if !isDemoMode { removePhoto(item) }
        items.removeAll { $0.requestId == item.requestId }
        persist()
    }

    func clear(kind: ScanKind) {
        if !isDemoMode { items.filter { $0.kind == kind }.forEach(removePhoto) }
        items.removeAll { $0.kind == kind }
        persist()
    }

    private func removePhoto(_ it: ScanItem) {
        if let u = photoURL(it) { try? FileManager.default.removeItem(at: u) }
    }

    private func trimHistory() {
        guard items.count > 800 else { return }
        let drop = items.suffix(items.count - 800).filter { $0.isSettled }
        drop.forEach(removePhoto)
        let ids = Set(drop.map { $0.requestId })
        items.removeAll { ids.contains($0.requestId) }
    }

    /// Ghi danh sách quét xuống máy: gom nhiều lần gọi liên tiếp thành một, mã hóa + ghi ở luồng nền
    /// (trước đây mỗi lần gửi lại đều mã hóa cả ~800 dòng ngay trên luồng giao diện nên màn hình quét bị đứng khi quét nhiều).
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

    /// Ghi ngay (gọi khi app chuyển nền để không mất lượt quét cuối).
    func flushPersist() {
        guard !isDemoMode else { return }
        persistScheduled = false
        let snapshot = items, url = storeURL
        persistQueue.async {
            if let data = try? JSONEncoder().encode(snapshot) { try? data.write(to: url, options: .atomic) }
        }
    }

    private func nowMs() -> Int { Int(Date().timeIntervalSince1970 * 1000) }
}
