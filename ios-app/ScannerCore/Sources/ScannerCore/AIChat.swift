import Foundation

/// Một mô hình cụ thể ở một nhà cung cấp (API tương thích OpenAI): Groq, Google Gemini.
public struct AIEndpoint: Equatable {
    public let provider: String
    public let baseURL: String
    public let apiKey: String
    public let model: String
    public var label: String { provider + "/" + model }
    public init(provider: String, baseURL: String, apiKey: String, model: String) {
        self.provider = provider; self.baseURL = baseURL; self.apiKey = apiKey; self.model = model
    }

    public static let groqBaseURL = "https://api.groq.com/openai/v1"
    public static let geminiBaseURL = "https://generativelanguage.googleapis.com/v1beta/openai"
    public static let defaultGroqModels = "openai/gpt-oss-120b, qwen/qwen3.8-27b, openai/gpt-oss-20b"
    public static let defaultGeminiModels = "gemini-2.5-flash, gemini-2.5-flash-lite"

    /// Tách danh sách mô hình (dấu phẩy, chấm phẩy, xuống dòng) theo thứ tự ưu tiên, bỏ trùng.
    public static func splitModels(_ text: String, fallback: String) -> [String] {
        var out: [String] = []
        for part in text.split(whereSeparator: { ",;\n\r".contains($0) }) {
            let m = part.trimmingCharacters(in: .whitespaces)
            if !m.isEmpty && !out.contains(m) { out.append(m) }
        }
        if out.isEmpty && !fallback.trimmingCharacters(in: .whitespaces).isEmpty { out.append(fallback) }
        return out
    }

    public static func build(provider: String, baseURL: String, apiKey: String, models: String, fallbackModel: String) -> [AIEndpoint] {
        splitModels(models, fallback: fallbackModel).map { AIEndpoint(provider: provider, baseURL: baseURL, apiKey: apiKey, model: $0) }
    }
}

public struct AIAnswer {
    public let text: String
    public let refs: [AIOrderRef]
    public let trace: [String]
    public init(text: String, refs: [AIOrderRef], trace: [String]) { self.text = text; self.refs = refs; self.trace = trace }
}

public struct AIError: Error, LocalizedError, Equatable {
    public let message: String
    public var statusCode: Int = 0
    public var retryAfter: TimeInterval?
    public var body: String = ""
    public init(_ message: String, statusCode: Int = 0, retryAfter: TimeInterval? = nil, body: String = "") {
        self.message = message; self.statusCode = statusCode; self.retryAfter = retryAfter; self.body = body
    }
    public var errorDescription: String? { message }
}

/// Lớp mạng tối giản để thay thế khi kiểm thử (không gọi mạng thật).
public protocol AIHTTPTransport {
    /// Trả về (mã HTTP, dữ liệu, giá trị header Retry-After tính bằng giây nếu có).
    func post(url: URL, headers: [String: String], body: Data) async throws -> (Int, Data, TimeInterval?)
}

public struct URLSessionAITransport: AIHTTPTransport {
    private let session: URLSession
    public init(session: URLSession = .shared) { self.session = session }
    public func post(url: URL, headers: [String: String], body: Data) async throws -> (Int, Data, TimeInterval?) {
        var req = URLRequest(url: url, timeoutInterval: 60)
        req.httpMethod = "POST"
        for (k, v) in headers { req.setValue(v, forHTTPHeaderField: k) }
        req.httpBody = body
        let (data, resp) = try await session.data(for: req)
        let http = resp as? HTTPURLResponse
        var ra: TimeInterval?
        if let s = http?.value(forHTTPHeaderField: "Retry-After"), let d = TimeInterval(s) { ra = d }
        return (http?.statusCode ?? 0, data, ra)
    }
}

/// Trò chuyện với AI và cho AI gọi công cụ chỉ-đọc của `AIOrderTools`.
/// Chuyển dự phòng: thử mô hình đầu danh sách; gặp 429 (hết hạn mức) hoặc lỗi riêng của mô hình thì nhảy sang mô hình kế tiếp
/// và cho mô hình đó "nghỉ" một lúc để các câu hỏi sau không thử lại vô ích.
public final class AIChatService {
    private let transport: AIHTTPTransport
    private let endpoints: [AIEndpoint]
    private let tools: AIOrderTools
    private let now: () -> Date
    private var history: [[String: Any]] = []
    private var restUntil: [String: Date] = [:]
    private static let maxRounds = 6
    private static let maxToolChars = 14000
    private static let maxHistory = 12

    public init(transport: AIHTTPTransport = URLSessionAITransport(), endpoints: [AIEndpoint], tools: AIOrderTools, now: @escaping () -> Date = { Date() }) {
        self.transport = transport
        self.endpoints = endpoints
        self.tools = tools
        self.now = now
    }

    public func reset() { history.removeAll() }

    private func systemPrompt() -> String {
        let n = now()
        let f = DateFormatter()
        f.locale = Locale(identifier: "vi_VN")
        f.dateFormat = "dd/MM/yyyy HH:mm"
        let day = DateFormatter()
        day.dateFormat = "yyyy-MM-dd"
        return """
        Bạn là trợ lý AI tích hợp trong phần mềm 'Quản lý hàng hoàn' của một shop bán hàng trên Shopee, TikTok Shop, Lazada. Khi được hỏi bạn là ai, hãy nói bạn là trợ lý AI của phần mềm này (đừng nhận là ChatGPT, Llama hay tên mô hình nào). Trả lời bằng tiếng Việt, ngắn gọn, rõ ràng.
        Bây giờ là \(f.string(from: n)), hôm nay là \(day.string(from: n)).
        CÁCH LÀM VIỆC:
        - Bạn có công cụ truy vấn dữ liệu của phần mềm: overview (tổng quan), count_orders (đếm + tổng tiền), aggregate (gom nhóm theo trạng thái/sàn/đơn vị vận chuyển/loại đơn/sản phẩm/ngày/tuần/tháng/mức cảnh báo, theo số kiện, tổng tiền hoặc số lượng), list_orders (danh sách), find_order (tìm theo mã), order_detail (chi tiết một đơn), product_stats (sản phẩm).
        - Câu hỏi cần số liệu thì GỌI CÔNG CỤ NGAY, không hỏi lại 'bạn có muốn tôi làm không'. Câu hỏi nhiều phần (ví dụ 'giá trị chưa về và sản phẩm nào nhiều nhất') thì gọi nhiều công cụ liên tiếp rồi trả lời đủ mọi phần trong MỘT câu trả lời.
        - Mọi con số và danh sách đơn PHẢI lấy từ công cụ. Tuyệt đối không bịa, không đoán, không tự tính nhẩm khi công cụ đã cho số.
        - Từ ngữ: 'chưa về', 'chưa nhận', 'đang chờ' đều là đơn chưa nhận hàng về kho (status 'Đang chờ'). 'Đã về', 'đã nhận' là 'Đã nhận'. 'Giá trị', 'tiền' là giá trị đơn (đồng), trường 'value'/'totalValue'. 'Quá hạn' / 'báo đỏ' là chờ quá 24 giờ kể từ lúc hàng về, 'báo vàng' là quá 12 giờ.
        - Tiền viết dạng 1.234.567 đ. Số kiện/số lượng viết rõ đơn vị.
        - Một mã đơn có thể có nhiều kiện hoàn (A/B/C), mỗi kiện một dòng riêng. Đếm theo KIỆN (dòng), nói rõ 'kiện' hoặc 'dòng'.
        - 'Hôm nay có bao nhiêu đơn về' nghĩa là status 'Đã nhận' với ngày nhận tại kho là hôm nay (date_field recv).
        - Dữ liệu là bản điện thoại nhận từ máy tính ở lần đồng bộ gần nhất, có thể chưa mới nhất.
        - Khi liệt kê đơn, ghi mã đơn và mã vận đơn; người dùng có thể chạm vào đơn bên dưới để xem chi tiết. Nếu kết quả bị cắt (truncated) hãy nói rõ.
        - Câu hỏi mơ hồ: chọn cách hiểu hợp lý nhất và làm luôn, nói rõ bạn đã hiểu thế nào. Chỉ hỏi lại khi thật sự không thể đoán.
        - Bạn chỉ ĐỌC dữ liệu. Không thể sửa, xóa, nhận hàng hay gửi gì ra ngoài.
        - Dữ liệu không có tên, số điện thoại, địa chỉ người nhận (đã được ẩn). Nếu được hỏi, nói rằng thông tin đó đã bị ẩn.
        - Nếu công cụ báo lỗi hoặc không có dữ liệu cho câu hỏi, nói thẳng điều đó và gợi ý cách hỏi khác dùng được.
        """
    }

    public func ask(_ question: String) async throws -> AIAnswer {
        var msgs: [[String: Any]] = [["role": "system", "content": systemPrompt()]]
        msgs += history
        msgs.append(["role": "user", "content": question])

        var refs: [AIOrderRef] = []
        var trace: [String] = []
        var used: [String] = []
        guard let toolsData = AIOrderTools.definitionsJSON.data(using: .utf8),
              let toolsObj = try? JSONSerialization.jsonObject(with: toolsData) else {
            throw AIError("Không đọc được định nghĩa công cụ.")
        }
        defer { tools.releaseCache() }

        for _ in 0..<Self.maxRounds {
            let body: [String: Any] = ["messages": msgs, "tools": toolsObj, "tool_choice": "auto", "temperature": 0.1]
            let (message, ep) = try await postWithFallback(body)
            if !used.contains(ep.label) { used.append(ep.label) }
            let calls = message["tool_calls"] as? [[String: Any]] ?? []
            if calls.isEmpty {
                var text = ((message["content"] as? String) ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
                if text.isEmpty { text = "(Không có câu trả lời.)" }
                remember(question, text)
                var seenIds = Set<String>()
                let distinct = refs.filter { seenIds.insert($0.id).inserted }.prefix(50)
                trace.insert("Mô hình: " + used.joined(separator: " → "), at: 0)
                return AIAnswer(text: text, refs: Array(distinct), trace: trace)
            }
            var assistant: [String: Any] = ["role": "assistant", "content": (message["content"] as? String) ?? "", "tool_calls": calls]
            assistant["content"] = (message["content"] as? String) ?? ""
            msgs.append(assistant)
            for call in calls {
                var id = (call["id"] as? String) ?? ""
                if id.isEmpty { id = UUID().uuidString }
                let fn = call["function"] as? [String: Any]
                let name = (fn?["name"] as? String) ?? ""
                let args = (fn?["arguments"] as? String) ?? "{}"
                let result = tools.execute(name: name, argsJSON: args)
                refs += result.refs
                trace.append("\(name)(\(args))")
                let json = result.json.count > Self.maxToolChars ? String(result.json.prefix(Self.maxToolChars)) + "…(đã cắt bớt)" : result.json
                msgs.append(["role": "tool", "tool_call_id": id, "name": name, "content": json])
            }
        }
        throw AIError("Trợ lý gọi công cụ quá nhiều lần mà chưa ra kết quả. Hãy hỏi cụ thể hơn.")
    }

    private func remember(_ q: String, _ a: String) {
        history.append(["role": "user", "content": q])
        history.append(["role": "assistant", "content": a])
        while history.count > Self.maxHistory { history.removeFirst() }
    }

    /// Thử kết nối TỪNG mô hình bằng một yêu cầu rất nhỏ. Trả về (nhãn, lỗi hoặc nil nếu dùng được).
    public func testAll() async -> [(label: String, error: String?)] {
        var res: [(label: String, error: String?)] = []
        for ep in endpoints {
            let body: [String: Any] = ["model": ep.model, "messages": [["role": "user", "content": "Trả lời đúng một từ: ok"]], "max_tokens": 16]
            do { _ = try await post(ep, body); res.append((ep.label, nil)) }
            catch { res.append((ep.label, (error as? AIError)?.message ?? error.localizedDescription)) }
        }
        return res
    }

    // MARK: chuyển dự phòng

    private func postWithFallback(_ body: [String: Any]) async throws -> ([String: Any], AIEndpoint) {
        var errors: [(AIEndpoint, AIError)] = []
        for ep in endpoints {
            if let until = restUntil[ep.label], until > now() { continue }
            var attempt = body
            attempt["model"] = ep.model
            do {
                let msg = try await post(ep, attempt)
                return (msg, ep)
            } catch let e as AIError {
                if e.statusCode != 0 {
                    if Self.shouldFallback(e.statusCode) {
                        restUntil[ep.label] = now().addingTimeInterval(Self.restTime(e))
                        errors.append((ep, e))
                    } else { throw e }
                } else {
                    errors.append((ep, e))   // lỗi mạng/định dạng: thử mô hình kế tiếp nhưng không bắt nghỉ
                }
            } catch {
                if error is CancellationError { throw error }
                errors.append((ep, AIError("Không kết nối được tới \(ep.provider) (kiểm tra mạng): \(error.localizedDescription)")))
            }
        }
        if errors.isEmpty {
            let soonest = restUntil.values.min() ?? now()
            let wait = max(1, Int((soonest.timeIntervalSince(now()) / 60).rounded(.up)))
            throw AIError("Tất cả mô hình đang nghỉ vì hết hạn mức. Thử lại sau khoảng \(wait) phút.")
        }
        if endpoints.count == 1 { throw errors[0].1 }
        var msg = "Cả \(endpoints.count) mô hình đều không dùng được lúc này:"
        for (ep, e) in errors { msg += "\n• \(ep.label): \(e.message)" }
        throw AIError(msg)
    }

    static func shouldFallback(_ code: Int) -> Bool {
        [400, 401, 403, 404, 413, 429, 500, 502, 503].contains(code)
    }

    static func restTime(_ e: AIError) -> TimeInterval {
        if let ra = e.retryAfter { return min(max(ra, 5), 7200) }
        let b = e.body.lowercased()
        switch e.statusCode {
        case 429: return (b.contains("per day") || b.contains("tpd") || b.contains("rpd") || b.contains("daily")) ? 1800 : 60
        case 404: return 6 * 3600
        case 401, 403: return 600
        case 400: return 120
        default: return 30
        }
    }

    private func post(_ ep: AIEndpoint, _ body: [String: Any]) async throws -> [String: Any] {
        guard let url = URL(string: ep.baseURL.trimmingCharacters(in: CharacterSet(charactersIn: "/")) + "/chat/completions"),
              JSONSerialization.isValidJSONObject(body),
              let data = try? JSONSerialization.data(withJSONObject: body, options: [.withoutEscapingSlashes]) else {
            throw AIError("Không tạo được yêu cầu gửi tới \(ep.provider).")
        }
        let headers = ["Content-Type": "application/json", "Authorization": "Bearer " + ep.apiKey]
        let (code, resp, ra) = try await transport.post(url: url, headers: headers, body: data)
        let text = String(data: resp, encoding: .utf8) ?? ""
        if !(200..<300).contains(code) {
            throw AIError(Self.describe(ep, code, text), statusCode: code, retryAfter: ra, body: text)
        }
        guard let root = try? JSONSerialization.jsonObject(with: resp) as? [String: Any],
              let choices = root["choices"] as? [[String: Any]],
              let message = choices.first?["message"] as? [String: Any] else {
            throw AIError("\(ep.provider) trả về dữ liệu không đúng định dạng.")
        }
        return message
    }

    static func describe(_ ep: AIEndpoint, _ code: Int, _ body: String) -> String {
        var detail = ""
        if let d = body.data(using: .utf8), let obj = try? JSONSerialization.jsonObject(with: d) {
            if let o = obj as? [String: Any], let e = o["error"] as? [String: Any], let m = e["message"] as? String { detail = m }
            else if let a = obj as? [[String: Any]], let e = a.first?["error"] as? [String: Any], let m = e["message"] as? String { detail = m }
        }
        let who = ep.provider
        let head: String
        switch code {
        case 401, 403: head = "Khóa API \(who) không đúng hoặc đã bị thu hồi."
        case 404: head = "Không tìm thấy mô hình hoặc địa chỉ (\(who)): kiểm tra tên mô hình."
        case 429: head = "Đã chạm giới hạn của \(who) (hết hạn mức hoặc gọi quá nhanh)."
        case 400: head = "\(who) từ chối yêu cầu (có thể mô hình không hỗ trợ gọi công cụ)."
        default: head = "\(who) báo lỗi \(code)."
        }
        if detail.isEmpty { return head }
        return head + " Chi tiết: " + (detail.count > 300 ? String(detail.prefix(300)) + "…" : detail)
    }
}
