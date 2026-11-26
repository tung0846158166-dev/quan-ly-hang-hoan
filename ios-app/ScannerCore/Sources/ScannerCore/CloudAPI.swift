import Foundation

/// Lỗi trả về từ cloud hoặc không kết nối được (`status == 0`). `message` là tiếng Việt cho người dùng; không bao giờ chứa token.
public struct CloudAPIError: Error, LocalizedError, Equatable {
    public let code: String
    public let message: String
    public let status: Int
    public init(code: String, message: String, status: Int) { self.code = code; self.message = message; self.status = status }
    public var errorDescription: String? { message }
    public var isUnauthorized: Bool { status == 401 }
    public var isNetwork: Bool { status == 0 }
    public var isQuota: Bool { status == 429 }
}

public protocol CloudHTTPTransport {
    func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse)
}

public struct URLSessionCloudTransport: CloudHTTPTransport {
    public init() {}
    public func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        let (data, resp) = try await URLSession.shared.data(for: request)
        guard let http = resp as? HTTPURLResponse else { throw URLError(.badServerResponse) }
        return (data, http)
    }
}

public struct CloudPaired: Equatable {
    public let deviceId: String, token: String, role: String
}

/// Tư cách thành viên Bảng Hàng Hoàn Về của một kiện: giờ vào bảng (ms) và cờ đã bị bỏ khỏi bảng.
public struct CloudWatchChange: Codable, Equatable {
    public let key: String
    public let since: Int
    public let off: Bool
    public init(key: String, since: Int, off: Bool) { self.key = key; self.since = since; self.off = off }
}

/// Một ảnh của kiện trên cloud.
public struct CloudPhotoInfo: Equatable {
    public let photoId: String
    public let takenAt: Int
    public let deviceName: String
    public let expired: Bool
    public init(photoId: String, takenAt: Int, deviceName: String, expired: Bool) {
        self.photoId = photoId; self.takenAt = takenAt; self.deviceName = deviceName; self.expired = expired
    }
}

public struct CloudSyncPage {
    public let rev: Int
    public let more: Bool
    public let parcels: [CloudParcel]
    public let watch: [CloudWatchChange]
}

/// Kết quả của một thao tác ghi (`/ops`).
public struct CloudOpResult {
    public let opId: String
    public let ok: Bool
    public let rev: Int
    public let result: [String: Any]?
    public let errorCode: String?
    public let errorMessage: String?
}

/// Kết quả quét qua cloud (cloud/API.md mục 5).
public struct CloudScanResult {
    public enum Outcome: String { case received, alreadyReceived, ambiguous, notFound, needsManualCheck, error }
    public let outcome: Outcome
    public let ack: Bool
    public let message: String
    public let parcel: CloudParcel?
    public let receivedBy: String?
    public let receivedAt: String?
}

/// Máy khách HTTP cho cloud (cloud/API.md). Chỉ giữ địa chỉ + token. Mọi lỗi mạng/HTTP đều thành `CloudAPIError` tiếng Việt.
public final class CloudAPI {
    public let baseURL: String
    private let token: String?
    private let transport: CloudHTTPTransport

    public init(baseURL: String, token: String?, transport: CloudHTTPTransport = URLSessionCloudTransport()) throws {
        self.baseURL = try Self.normalize(baseURL)
        self.token = token
        self.transport = transport
    }

    /// "https://x.workers.dev" hoặc ".../" hoặc ".../v1" -> ".../v1". Chỉ nhận https (hoặc http cho máy cục bộ).
    public static func normalize(_ raw: String) throws -> String {
        var u = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        while u.hasSuffix("/") { u.removeLast() }
        if u.lowercased().hasSuffix("/v1") { u = String(u.dropLast(3)) }
        guard let url = URL(string: u), let scheme = url.scheme?.lowercased(), let host = url.host, !host.isEmpty else {
            throw CloudAPIError(code: "invalid", message: "Địa chỉ cloud không hợp lệ (cần bắt đầu bằng https://).", status: 0)
        }
        let local = host == "localhost" || host == "127.0.0.1"
        guard scheme == "https" || (scheme == "http" && local) else {
            throw CloudAPIError(code: "invalid", message: "Địa chỉ cloud không hợp lệ (cần bắt đầu bằng https://).", status: 0)
        }
        return u + "/v1"
    }

    /// Địa chỉ WebSocket (có token trong tham số, như API quy định).
    public func webSocketURL() -> URL? {
        guard let t = token, var c = URLComponents(string: baseURL + "/ws") else { return nil }
        c.scheme = c.scheme == "https" ? "wss" : "ws"
        c.queryItems = [URLQueryItem(name: "token", value: t)]
        return c.url
    }

    // ---- gửi nhận
    private func request(_ method: String, _ path: String, body: Data? = nil, contentType: String = "application/json") -> URLRequest? {
        guard let url = URL(string: baseURL + path) else { return nil }
        var r = URLRequest(url: url)
        r.httpMethod = method
        r.timeoutInterval = 30
        if let t = token, !t.isEmpty { r.setValue("Bearer " + t, forHTTPHeaderField: "Authorization") }
        if let b = body { r.httpBody = b; r.setValue(contentType, forHTTPHeaderField: "Content-Type") }
        return r
    }

    private func perform(_ method: String, _ path: String, body: Data? = nil, contentType: String = "application/json") async throws -> Data {
        guard let req = request(method, path, body: body, contentType: contentType) else {
            throw CloudAPIError(code: "invalid", message: "Địa chỉ cloud không hợp lệ.", status: 0)
        }
        do {
            let (data, resp) = try await transport.send(req)
            if (200..<300).contains(resp.statusCode) { return data }
            throw Self.error(from: data, status: resp.statusCode)
        } catch let e as CloudAPIError {
            throw e
        } catch let e as URLError where e.code == .timedOut {
            throw CloudAPIError(code: "timeout", message: "Cloud không phản hồi kịp (quá thời gian chờ).", status: 0)
        } catch is CancellationError {
            throw CancellationError()
        } catch {
            throw CloudAPIError(code: "network", message: "Không kết nối được tới cloud. Kiểm tra mạng.", status: 0)
        }
    }

    static func error(from data: Data, status: Int) -> CloudAPIError {
        var code: String
        var msg: String
        switch status {
        case 401: code = "unauthorized"; msg = "Thiết bị chưa được ghép hoặc đã bị thu hồi."
        case 403: code = "forbidden"; msg = "Không đủ quyền thực hiện việc này."
        case 404: code = "not_found"; msg = "Cloud từ chối yêu cầu."
        case 409: code = "conflict"; msg = "Cloud từ chối yêu cầu."
        case 429: code = "quota_exceeded"; msg = "Cloud tạm dừng vì hết hạn mức hôm nay."
        case 500...: code = "http_\(status)"; msg = "Cloud đang gặp sự cố, thử lại sau."
        default: code = "http_\(status)"; msg = "Cloud từ chối yêu cầu."
        }
        if let obj = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any], let e = obj["error"] as? [String: Any] {
            if let c = e["code"] as? String, !c.isEmpty { code = c }
            if let m = e["message"] as? String, !m.isEmpty { msg = m }
        }
        return CloudAPIError(code: code, message: msg, status: status)
    }

    private func json(_ method: String, _ path: String, body: [String: Any]? = nil) async throws -> [String: Any] {
        var data: Data?
        if let b = body { data = try JSONSerialization.data(withJSONObject: b) }
        let out = try await perform(method, path, body: data)
        if out.isEmpty { return [:] }
        guard let obj = (try? JSONSerialization.jsonObject(with: out)) as? [String: Any] else {
            throw CloudAPIError(code: "invalid_response", message: "Cloud trả về dữ liệu không đúng định dạng.", status: 0)
        }
        return obj
    }

    // ---- ghép thiết bị
    public func pairComplete(code: String, deviceName: String, kind: String = "phone") async throws -> CloudPaired {
        let r = try await json("POST", "/pair/complete", body: ["code": code, "deviceName": deviceName, "kind": kind])
        return CloudPaired(deviceId: r["deviceId"] as? String ?? "", token: r["token"] as? String ?? "", role: r["role"] as? String ?? "staff")
    }

    /// Ghép bằng mật khẩu cửa hàng (chủ đặt trên cloud): điện thoại tự ghép, không cần máy tính tạo mã.
    public func pairJoin(joinKey: String, deviceName: String, kind: String = "phone") async throws -> CloudPaired {
        let r = try await json("POST", "/pair/join", body: ["joinKey": joinKey, "deviceName": deviceName, "kind": kind])
        return CloudPaired(deviceId: r["deviceId"] as? String ?? "", token: r["token"] as? String ?? "", role: r["role"] as? String ?? "staff")
    }

    public func me() async throws -> (deviceId: String, deviceName: String, role: String) {
        let r = try await json("GET", "/me")
        return (r["deviceId"] as? String ?? "", r["deviceName"] as? String ?? "", r["role"] as? String ?? "")
    }

    // ---- đồng bộ
    public func sync(since: Int, limit: Int = 500) async throws -> CloudSyncPage {
        let r = try await json("GET", "/sync?since=\(since)&limit=\(limit)")
        var parcels: [CloudParcel] = []
        var watch: [CloudWatchChange] = []
        if let changes = r["changes"] as? [[String: Any]] {
            for c in changes {
                let type = c["type"] as? String
                if type == "parcel" {
                    if let p = c["parcel"], let d = try? JSONSerialization.data(withJSONObject: p),
                       let parcel = try? JSONDecoder().decode(CloudParcel.self, from: d) { parcels.append(parcel) }
                } else if type == "returnWatch", let key = c["key"] as? String, !key.isEmpty {
                    watch.append(CloudWatchChange(key: key, since: Self.int(c["since"]), off: c["off"] as? Bool ?? false))
                }
            }
        }
        return CloudSyncPage(rev: Self.int(r["rev"], default: since), more: r["more"] as? Bool ?? false, parcels: parcels, watch: watch)
    }

    static func int(_ v: Any?, default def: Int = 0) -> Int {
        if let i = v as? Int { return i }
        if let d = v as? Double, d.isFinite { return Int(d) }
        if let s = v as? String, let i = Int(s) { return i }
        return def
    }

    // ---- thao tác ghi
    public func ops(_ ops: [[String: Any]]) async throws -> [CloudOpResult] {
        let r = try await json("POST", "/ops", body: ["ops": ops])
        var out: [CloudOpResult] = []
        for x in (r["results"] as? [[String: Any]]) ?? [] {
            let err = x["error"] as? [String: Any]
            out.append(CloudOpResult(opId: x["opId"] as? String ?? "", ok: x["ok"] as? Bool ?? false, rev: Self.int(x["rev"]),
                                     result: x["result"] as? [String: Any], errorCode: err?["code"] as? String, errorMessage: err?["message"] as? String))
        }
        return out
    }

    /// Một thao tác, tự sinh `opId` nếu thiếu. Gửi lặp cùng `opId` là an toàn.
    public func op(_ operation: [String: Any]) async throws -> CloudOpResult {
        var o = operation
        if o["opId"] == nil { o["opId"] = UUID().uuidString }
        let res = try await ops([o])
        return res.first ?? CloudOpResult(opId: o["opId"] as? String ?? "", ok: false, rev: 0, result: nil,
                                          errorCode: "invalid_response", errorMessage: "Cloud không trả kết quả.")
    }

    /// Quét một mã. Chỉ `ack == true` khi cloud đã ghi bền (quy tắc 6). Không cần mạng thì KHÔNG gọi hàm này (mất mạng = khóa).
    public func scan(code: String) async throws -> CloudScanResult {
        let res = try await op(["type": "scan", "code": code])
        guard res.ok, let r = res.result else {
            return CloudScanResult(outcome: .error, ack: false, message: res.errorMessage ?? "Cloud từ chối thao tác quét.", parcel: nil, receivedBy: nil, receivedAt: nil)
        }
        var parcel: CloudParcel?
        if let p = r["parcel"], let d = try? JSONSerialization.data(withJSONObject: p) { parcel = try? JSONDecoder().decode(CloudParcel.self, from: d) }
        return CloudScanResult(outcome: CloudScanResult.Outcome(rawValue: r["outcome"] as? String ?? "") ?? .error, ack: r["ack"] as? Bool ?? false,
                               message: r["message"] as? String ?? "", parcel: parcel, receivedBy: r["receivedBy"] as? String, receivedAt: r["receivedAt"] as? String)
    }

    // ---- hạn mức
    public func quota() async throws -> CloudQuota {
        let r = try await json("GET", "/quota")
        func n(_ a: String, _ b: String) -> Int { Self.int((r[a] as? [String: Any])?[b]) }
        return CloudQuota(day: r["day"] as? String ?? "", requestsUsed: n("requests", "used"), requestsLimit: n("requests", "limit"),
                          rowsWrittenUsed: n("rowsWritten", "used"), rowsWrittenLimit: n("rowsWritten", "limit"),
                          rowsReadUsed: n("rowsRead", "used"), rowsReadLimit: n("rowsRead", "limit"), level: r["level"] as? String ?? "ok")
    }

    // ---- ảnh (chỉ điện thoại chụp; ảnh mới chỉ nhận sau khi vừa quét xác nhận đúng một kiện)
    public func putPhoto(photoId: String, parcelId: String, mime: String = "image/jpeg", thumb: Bool = false, data: Data) async throws {
        let enc = { (s: String) in s.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? s }
        let path = "/photos/\(enc(photoId))?parcelId=\(enc(parcelId))&mime=\(enc(mime))" + (thumb ? "&kind=thumb" : "")
        _ = try await perform("PUT", path, body: data, contentType: mime)
    }

    public func photo(photoId: String, thumb: Bool) async throws -> Data {
        let id = photoId.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed) ?? photoId
        return try await perform("GET", "/photos/\(id)" + (thumb ? "?size=thumb" : ""))
    }

    /// Cổng AI: nhà cung cấp nào đã có khóa trên cloud (khóa Groq/Gemini chỉ nằm trên cloud).
    public func aiStatus() async throws -> (groq: Bool, gemini: Bool) {
        let data = try await perform("GET", "/ai/status")
        let o = ((try? JSONSerialization.jsonObject(with: data)) as? [String: Any]) ?? [:]
        return (o["groq"] as? Bool ?? false, o["gemini"] as? Bool ?? false)
    }

    /// Chuỗi mô hình AI đi qua cổng AI của cloud: dùng NGUYÊN logic gọi AI, chỉ đổi địa chỉ gốc và dùng token thiết bị thay khóa API.
    public func aiEndpoints(groq: Bool, gemini: Bool, groqModels: String, geminiModels: String) -> [AIEndpoint] {
        guard let t = token, !t.isEmpty else { return [] }
        var out: [AIEndpoint] = []
        if groq { out += AIEndpoint.build(provider: "groq", baseURL: baseURL + "/ai/groq", apiKey: t, models: groqModels, fallbackModel: "openai/gpt-oss-120b") }
        if gemini { out += AIEndpoint.build(provider: "gemini", baseURL: baseURL + "/ai/gemini", apiKey: t, models: geminiModels, fallbackModel: AIEndpoint.fallbackGeminiModel) }
        return out
    }

    /// Danh sách ảnh của một kiện trên cloud (mới nhất trước), kể cả ảnh do máy khác chụp.
    public func parcelPhotos(parcelId: String) async throws -> [CloudPhotoInfo] {
        let id = parcelId.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed) ?? parcelId
        let data = try await perform("GET", "/parcels/\(id)/photos")
        let arr = ((try? JSONSerialization.jsonObject(with: data)) as? [[String: Any]]) ?? []
        return arr.compactMap { x in
            guard let pid = x["photoId"] as? String, !pid.isEmpty else { return nil }
            return CloudPhotoInfo(photoId: pid, takenAt: Self.int(x["takenAt"]), deviceName: x["deviceName"] as? String ?? "",
                                  expired: x["expired"] as? Bool ?? false)
        }
    }

    public func history(parcelId: String) async throws -> [[String: Any]] {
        let id = parcelId.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed) ?? parcelId
        let data = try await perform("GET", "/parcels/\(id)/history")
        return ((try? JSONSerialization.jsonObject(with: data)) as? [[String: Any]]) ?? []
    }
}
