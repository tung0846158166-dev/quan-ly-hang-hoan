import Foundation

/// MQTT 3.1.1 tối giản (đủ dùng cho kênh dự phòng): CONNECT, SUBSCRIBE, PUBLISH (QoS 0), PINGREQ.
/// Nhận được PUBLISH QoS 0 hoặc 1 (QoS 1 sẽ được trả PUBACK).
public enum MQTTPacket {
    static func string(_ s: String) -> [UInt8] {
        let b = Array(s.utf8)
        return [UInt8(b.count >> 8 & 0xFF), UInt8(b.count & 0xFF)] + b
    }

    /// Mã hoá độ dài còn lại (1-4 byte, 7 bit mỗi byte).
    public static func remainingLength(_ n: Int) -> [UInt8] {
        var out: [UInt8] = []
        var x = max(0, n)
        repeat {
            var d = UInt8(x % 128)
            x /= 128
            if x > 0 { d |= 0x80 }
            out.append(d)
        } while x > 0
        return out
    }

    public static func connect(clientId: String, keepAlive: Int, username: String? = nil, password: String? = nil) -> Data {
        // Cờ: 0x02 = clean session, 0x80 = có tên người dùng, 0x40 = có mật khẩu (broker riêng).
        var flags: UInt8 = 0x02
        if username != nil { flags |= 0x80 }
        if username != nil && password != nil { flags |= 0x40 }
        var body: [UInt8] = [0x00, 0x04, 0x4D, 0x51, 0x54, 0x54, 0x04, flags]
        body += [UInt8(keepAlive >> 8 & 0xFF), UInt8(keepAlive & 0xFF)]
        body += string(clientId)
        if let u = username { body += string(u) }
        if username != nil, let p = password { body += string(p) }
        return Data([0x10] + remainingLength(body.count) + body)
    }

    public static func subscribe(packetId: UInt16, topic: String) -> Data {
        var body: [UInt8] = [UInt8(packetId >> 8), UInt8(packetId & 0xFF)]
        body += string(topic)
        body.append(0x00)
        return Data([0x82] + remainingLength(body.count) + body)
    }

    public static func publish(topic: String, payload: Data) -> Data {
        let body = string(topic) + [UInt8](payload)
        return Data([0x30] + remainingLength(body.count) + body)
    }

    public static func puback(packetId: UInt16) -> Data {
        Data([0x40, 0x02, UInt8(packetId >> 8), UInt8(packetId & 0xFF)])
    }

    public static let pingReq = Data([0xC0, 0x00])
}

public enum MQTTIncoming: Equatable {
    case connack(code: UInt8)
    case suback
    case publish(topic: String, payload: Data, qos: UInt8, packetId: UInt16)
    case pingResp
    case other
}

/// Ghép các mảnh dữ liệu WebSocket thành gói MQTT hoàn chỉnh (một gói có thể bị cắt hoặc nhiều gói dính nhau).
public struct MQTTDecoder {
    private static let maxPacketSize = 8 * 1024 * 1024
    private var buf: [UInt8] = []
    public init() {}

    public mutating func feed(_ data: Data) -> [MQTTIncoming] {
        buf += [UInt8](data)
        var out: [MQTTIncoming] = []
        while true {
            guard buf.count >= 2 else { break }
            // Đọc độ dài còn lại (tối đa 4 byte).
            var multiplier = 1, value = 0, idx = 1
            var complete = false
            while idx < buf.count && idx <= 4 {
                let b = Int(buf[idx])
                value += (b & 0x7F) * multiplier
                multiplier *= 128
                idx += 1
                if b & 0x80 == 0 { complete = true; break }
            }
            if !complete {
                if idx > 4 { buf.removeAll(); break }   // dữ liệu hỏng
                break                                      // chưa đủ byte độ dài
            }
            if value < 0 || value > Self.maxPacketSize {
                buf.removeAll()
                break
            }
            let total = idx + value
            guard buf.count >= total else { break }
            let first = buf[0]
            let body = Array(buf[idx..<total])
            buf.removeFirst(total)
            out.append(Self.parse(first: first, body: body))
        }
        return out
    }

    private static func parse(first: UInt8, body: [UInt8]) -> MQTTIncoming {
        switch first >> 4 {
        case 2:
            return .connack(code: body.count >= 2 ? body[1] : 0xFF)
        case 9:
            return .suback
        case 13:
            return .pingResp
        case 3:
            let qos = (first >> 1) & 0x03
            guard body.count >= 2 else { return .other }
            let tlen = Int(body[0]) << 8 | Int(body[1])
            guard tlen >= 0, body.count >= 2 + tlen else { return .other }
            let topic = String(decoding: body[2..<(2 + tlen)], as: UTF8.self)
            var i = 2 + tlen
            var pid: UInt16 = 0
            if qos > 0 {
                guard body.count >= i + 2 else { return .other }
                pid = UInt16(body[i]) << 8 | UInt16(body[i + 1])
                i += 2
            }
            guard i <= body.count else { return .other }
            return .publish(topic: topic, payload: Data(body[i...]), qos: qos, packetId: pid)
        default:
            return .other
        }
    }
}

/// Một kết nối MQTT qua WebSocket (wss). Tự nối lại; mỗi broker một đối tượng.
public final class MQTTClient: @unchecked Sendable {
    public let name: String
    private let url: URL?
    private let username: String?
    private let password: String?
    private let lock = NSLock()
    private var task: URLSessionWebSocketTask?
    private var connected = false
    private var lastActivity = Date.distantPast

    public init(name: String, url: String) {
        self.name = name
        // Địa chỉ broker riêng có thể mang tên:mật-khẩu (wss://tên:mật-khẩu@máy-chủ/mqtt): tách ra, không gửi trong URL.
        if var comps = URLComponents(string: url), comps.user != nil {
            self.username = comps.user
            self.password = comps.password
            comps.user = nil
            comps.password = nil
            self.url = comps.url
        } else {
            self.username = nil
            self.password = nil
            self.url = URL(string: url)
        }
    }

    public var isConnected: Bool {
        lock.lock(); defer { lock.unlock() }
        return connected
    }

    private func set(task t: URLSessionWebSocketTask?, connected c: Bool) {
        lock.lock()
        task = t
        connected = c
        if c { lastActivity = Date() }
        lock.unlock()
    }

    private func markConnected(_ c: Bool) {
        lock.lock()
        connected = c
        if c { lastActivity = Date() }
        lock.unlock()
    }

    private func touchActivity() {
        lock.lock()
        lastActivity = Date()
        lock.unlock()
    }

    private func secondsSinceActivity() -> TimeInterval {
        lock.lock(); defer { lock.unlock() }
        return Date().timeIntervalSince(lastActivity)
    }

    /// Gửi một bản tin (QoS 0). Không làm gì nếu chưa nối. Gửi theo đúng thứ tự gọi.
    public func publish(topic: String, body: String) {
        lock.lock()
        let t = connected ? task : nil
        lock.unlock()
        guard let t = t else { return }
        t.send(.data(MQTTPacket.publish(topic: topic, payload: Data(body.utf8)))) { _ in }
    }

    /// Chạy vòng lặp: nối, đăng ký topic, nhận bản tin; mất kết nối (hoặc treo 4G) thì tự hủy và thử lại.
    public func run(topic: String,
                    onMessage: @escaping @Sendable (String) -> Void,
                    onState: @escaping @Sendable (Bool) -> Void) async {
        guard let targetURL = url else {
            onState(false)
            return
        }
        var backoff = 1.0
        while !Task.isCancelled {
            let ws = URLSession.shared.webSocketTask(with: targetURL, protocols: ["mqtt"])
            ws.maximumMessageSize = 4 * 1024 * 1024
            set(task: ws, connected: false)
            ws.resume()
            let watchdog = Task { [weak self] in
                try? await Task.sleep(nanoseconds: 12_000_000_000)
                if self?.isConnected == false { ws.cancel(with: .goingAway, reason: nil) }
            }
            var pinger: Task<Void, Never>?
            await withTaskCancellationHandler {
                do {
                    let clientId = "ql" + String(UUID().uuidString.prefix(8)).lowercased()
                    try await ws.send(.data(MQTTPacket.connect(clientId: clientId, keepAlive: 30, username: username, password: password)))
                    var decoder = MQTTDecoder()
                    while !Task.isCancelled {
                        let msg = try await ws.receive()
                        self.touchActivity()
                        let data: Data
                        switch msg {
                        case .data(let d): data = d
                        case .string(let s): data = Data(s.utf8)
                        @unknown default: continue
                        }
                        for packet in decoder.feed(data) {
                            switch packet {
                            case .connack(let code):
                                if code != 0 { throw URLError(.userAuthenticationRequired) }
                                try await ws.send(.data(MQTTPacket.subscribe(packetId: 1, topic: topic)))
                            case .suback:
                                self.markConnected(true)
                                backoff = 1
                                onState(true)
                                pinger?.cancel()
                                pinger = Task { [weak self] in
                                    while !Task.isCancelled {
                                        try? await Task.sleep(nanoseconds: 25_000_000_000)
                                        guard !Task.isCancelled, let self = self else { return }
                                        // Phát hiện kết nối nửa chết (chuyển Wi-Fi/4G không gửi PINGRESP): hủy để nối lại ngay.
                                        if self.secondsSinceActivity() > 55 {
                                            ws.cancel(with: .goingAway, reason: nil)
                                            return
                                        }
                                        ws.send(.data(MQTTPacket.pingReq)) { _ in }
                                    }
                                }
                            case .publish(let t, let payload, let qos, let pid):
                                if qos == 1 { ws.send(.data(MQTTPacket.puback(packetId: pid))) { _ in } }
                                if t == topic, let s = String(data: payload, encoding: .utf8) { onMessage(s) }
                            case .pingResp, .other:
                                break
                            }
                        }
                    }
                } catch {}
            } onCancel: {
                ws.cancel(with: .goingAway, reason: nil)
            }
            watchdog.cancel()
            pinger?.cancel()
            set(task: nil, connected: false)
            onState(false)
            ws.cancel(with: .goingAway, reason: nil)
            if Task.isCancelled { return }
            try? await Task.sleep(nanoseconds: UInt64(backoff * 1_000_000_000))
            backoff = min(backoff * 2, 15)
        }
    }
}

public extension SyncProtocol {
    /// Topic MQTT dùng chung với PC và bản web cũ. Phân biệt hoa/thường nên dùng đúng mã phòng đã chuẩn hoá (chữ hoa).
    static func mqttTopic(room: String) -> String {
        "agy_shopee_return_scanner_2026/" + room
    }

    /// Broker riêng do người dùng nhập (UserDefaults "privateBroker"); rỗng = dùng 3 broker công cộng.
    static var mqttBrokers: [(name: String, url: String)] {
        let custom = (UserDefaults.standard.string(forKey: "privateBroker") ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        if !custom.isEmpty, let u = URL(string: custom), u.scheme == "wss" || u.scheme == "ws" {
            return [("riêng", custom)]
        }
        return publicMqttBrokers
    }

    static let publicMqttBrokers: [(name: String, url: String)] = [
        ("emqx", "wss://broker.emqx.io:8084/mqtt"),
        ("hivemq", "wss://broker.hivemq.com:8884/mqtt"),
        ("mosquitto", "wss://test.mosquitto.org:8081/")
    ]
}
