import SwiftUI
import UIKit
import AudioToolbox
import ScannerCore

struct Banner: Equatable {
    var code: String
    var text: String
    var color: Color
}

@MainActor
final class SyncEngine: ObservableObject {
    @Published var items: [ScanItem] = []
    @Published var room: String
    @Published var sseConnected = false
    @Published var pcLastSeen: Date?
    @Published var banner: Banner?
    @Published var mode: ScanKind = .returnScan
    @Published var note = ""

    var snapshotProvider: (() -> Data?)?

    private let client = NtfyClient()
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
        room = SyncProtocol.normalizeRoom(UserDefaults.standard.string(forKey: "room") ?? "SHP-2026")
        try? FileManager.default.createDirectory(at: photosDir, withIntermediateDirectories: true)
        if let data = try? Data(contentsOf: storeURL), let list = try? JSONDecoder().decode([ScanItem].self, from: data) {
            items = list
        }
    }

    var pendingCount: Int { items.filter { !$0.isSettled }.count }
    var pcOnline: Bool { pcLastSeen.map { Date().timeIntervalSince($0) < 120 } ?? false }

    func photoURL(_ item: ScanItem) -> URL? { item.photoFile.map { photosDir.appendingPathComponent($0) } }

    // MARK: vòng đời
    func start() {
        guard sseTask == nil else { return }
        startSSE()
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
    }

    private func startSSE() {
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

    // MARK: nhận phản hồi
    private func handleIncoming(_ data: String, opened: Bool) {
        if opened && !sseConnected {
            sseConnected = true
            let hello = SyncProtocol.json(["type": "phone_connected", "room": room, "time": nowMs()])
            let r = room
            Task { try? await client.post(topic: SyncProtocol.topic(room: r), title: "Shopee Connect", body: hello) }
            resendAllPending()
        }
        guard let msg = SyncProtocol.parseIncoming(sseData: data, room: room) else { return }
        switch msg {
        case .hello, .heartbeat:
            pcLastSeen = Date()
        case .ack(let ack):
            pcLastSeen = Date()
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
        case .success: return (it.kind == .returnScan ? "HỢP LỆ" : "ĐÃ TẠO", .green)
        case .duplicate: return ("QUÉT TRÙNG", .orange)
        case .notFound: return ("KHÔNG CÓ TRÊN HỆ THỐNG", .red)
        case .ambiguous: return ("TRÙNG NHIỀU ĐƠN, CẦN KIỂM TRA", .red)
        case .error: return ("PC CHƯA LƯU, ĐANG THỬ LẠI", .red)
        case .pending: return ("ĐANG CHỜ PC", .orange)
        }
    }

    private func showResult(_ it: ScanItem) {
        let d = describe(it)
        banner = Banner(code: it.code, text: d.0, color: d.1)
        let gen = UINotificationFeedbackGenerator()
        switch it.status {
        case .success: gen.notificationOccurred(.success); AudioServicesPlaySystemSound(1057)
        case .duplicate: gen.notificationOccurred(.warning); AudioServicesPlaySystemSound(1053)
        default: gen.notificationOccurred(.error); AudioServicesPlaySystemSound(1053)
        }
    }

    // MARK: quét
    func handleCameraCode(_ raw: String) {
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
        if code == lastRawCode && now.timeIntervalSince(lastRawTime) < 1.2 { return false }
        lastRawCode = code; lastRawTime = now

        if let existing = items.first(where: { $0.code == code && $0.kind == kind && !$0.scanConfirmed }) {
            banner = Banner(code: code, text: "ĐANG THỬ LẠI, CHỜ PC", color: .orange)
            transmit(existing.requestId)
            return true
        }

        seq += 1
        let rid = "\(sessionId)-\(seq)-\(nowMs() % 100000)"
        var photoData = photo
        if kind != .newOrder && photoData == nil && useSnapshot { photoData = snapshotProvider?() }
        if kind == .newOrder { photoData = nil }  // đơn mới không cần ảnh; ảnh nằm ở bước quét hoàn kho
        var file: String? = nil
        if let d = photoData {
            file = rid + ".jpg"
            try? d.write(to: photosDir.appendingPathComponent(file!))
        }
        let item = ScanItem(requestId: rid, code: code, kind: kind, time: nowMs(), photoFile: file)
        items.insert(item, at: 0)
        trimHistory()
        persist()
        banner = Banner(code: code, text: "ĐANG CHỜ PC XÁC NHẬN", color: .orange)
        UIImpactFeedbackGenerator(style: .medium).impactOccurred()
        transmit(rid)
        if file != nil { transmitPhoto(rid) }
        return true
    }

    // MARK: gửi
    private func transmit(_ id: String) {
        guard let i = items.firstIndex(where: { $0.requestId == id }) else { return }
        items[i].lastSendAt = nowMs()
        items[i].attempts += 1
        let it = items[i]
        let r = room
        let body = SyncProtocol.json(SyncProtocol.scanPayload(kind: it.kind, code: it.code, requestId: it.requestId,
            captureId: it.captureId, captureTime: it.time, time: it.time, room: r, note: note))
        persist()
        Task { try? await client.post(topic: SyncProtocol.topic(room: r), title: "Shopee Scan", body: body) }
    }

    private func transmitPhoto(_ id: String) {
        guard let i = items.firstIndex(where: { $0.requestId == id }), let file = items[i].photoFile,
              let data = try? Data(contentsOf: photosDir.appendingPathComponent(file)) else { return }
        items[i].photoSentAt = nowMs()
        items[i].photoAttempts += 1
        let it = items[i]
        let r = room
        persist()
        let dataURL = "data:image/jpeg;base64," + data.base64EncodedString()
        let chunks = SyncProtocol.photoChunks(dataURL: dataURL, code: it.code, kind: it.kind, requestId: it.requestId,
            captureId: it.captureId, captureTime: it.time, room: r, now: nowMs(), photoId: "ph_\(nowMs())_\(it.attempts)")
        Task {
            for c in chunks {
                try? await client.post(topic: SyncProtocol.topic(room: r), title: "Shopee Photo Chunk", body: SyncProtocol.json(c))
                try? await Task.sleep(nanoseconds: 45_000_000)
            }
        }
    }

    private func resendAllPending() {
        for i in items.indices where !items[i].isSettled { items[i].lastSendAt = 0 }
    }

    private func tick() async {
        tickCount += 1
        let n = nowMs()
        var needPoll = false
        for it in items where !it.isSettled {
            if !it.scanConfirmed {
                needPoll = true
                if n >= it.nextRetryDue() { transmit(it.requestId) }
            }
            if it.photoFile != nil && !it.photoConfirmed && !it.reviewRequired && it.photoAttempts < 6
                && n - it.photoSentAt > 30_000 && n - it.time > 8_000 {
                needPoll = true
                transmitPhoto(it.requestId)
            }
        }
        // Hỏi lại ntfy để bắt phản hồi bị lỡ khi mất kết nối.
        if needPoll && tickCount % 3 == 0 {
            let r = room
            if let lines = try? await client.poll(topic: SyncProtocol.topic(room: r), since: "10m") {
                for l in lines { handleIncoming(l, opened: false) }
            }
        }
    }

    // MARK: lịch sử
    func delete(_ item: ScanItem) {
        removePhoto(item)
        items.removeAll { $0.requestId == item.requestId }
        persist()
    }

    func clear(kind: ScanKind) {
        items.filter { $0.kind == kind }.forEach(removePhoto)
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

    private func persist() {
        if let data = try? JSONEncoder().encode(items) { try? data.write(to: storeURL, options: .atomic) }
    }

    private func nowMs() -> Int { Int(Date().timeIntervalSince1970 * 1000) }
}
