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
        var req = URLRequest(url: url, timeoutInterval: 45)
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
    private static let maxToolChars = 6000
    private static let maxHistory = 6
    private static let maxHistoryChars = 500
    /// Ngân sách ước lượng cho MỘT yêu cầu (token). Gói miễn phí Groq giới hạn 7.000-8.000 token mỗi yêu cầu/phút.
    private static let tokenBudget = 4500

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
        Bạn là trợ lý AI của phần mềm 'Quản lý hàng hoàn' (Shopee, TikTok Shop, Lazada). Khi được hỏi bạn là ai: trợ lý AI của phần mềm này (đừng nhận là ChatGPT, Llama hay tên mô hình). Trả lời tiếng Việt, ngắn gọn.
        Bây giờ là \(f.string(from: n)), hôm nay là \(day.string(from: n)).
        QUY TẮC:
        - Số liệu và danh sách đơn PHẢI lấy từ công cụ, không bịa, không nhẩm. Cần số liệu thì gọi công cụ ngay, đừng hỏi lại; câu nhiều phần thì gọi nhiều công cụ rồi trả lời đủ trong MỘT lần.
        - 'chưa về/chưa nhận/đang chờ' = status 'Đang chờ'; 'đã về/đã nhận' = 'Đã nhận'. 'tiền/giá trị' = value (đồng, viết 1.234.567 đ). 'quá hạn/báo đỏ' = chờ quá 24 giờ kể từ lúc hàng về, 'báo vàng' = quá 12 giờ.
        - Đếm theo KIỆN (dòng); một mã đơn có thể có nhiều kiện A/B/C riêng. 'Về hôm nay' = Đã nhận có ngày nhận kho hôm nay (date_field recv).
        - Dữ liệu là bản điện thoại nhận từ máy tính ở lần đồng bộ gần nhất, có thể chưa mới nhất.
        - Liệt kê đơn: ghi mã đơn + mã vận đơn (người dùng chạm được bên dưới); kết quả bị cắt (truncated) thì nói rõ.
        - Câu mơ hồ: chọn cách hiểu hợp lý, làm luôn, nói rõ đã hiểu thế nào.
        - Hỏi cách dùng/ý nghĩa/màn hình nào: gọi app_guide (topic man_hinh, y_nghia_du_lieu, cau_hoi_mau, quet...), không bịa tính năng. Bạn chỉ ĐỌC dữ liệu, không sửa/xóa/nhận hàng.
        - Dữ liệu không có tên, SĐT, địa chỉ người nhận (đã ẩn). Công cụ lỗi hoặc không có dữ liệu: nói thẳng và gợi ý cách hỏi khác.
        CÓ TRONG APP: 5 tab Tổng quan, Quét, Đơn, Lịch sử, Cài đặt (mã phòng, đồng bộ, thông báo, ảnh, Trợ lý AI); nút ✨ hỏi AI; ảnh quét lưu trên điện thoại.
        VÍ DỤ: 'còn bao nhiêu đơn chưa về/tổng tiền' → dùng số liệu có sẵn. 'đơn nào quá hạn/báo đỏ' → list_orders(status Đang chờ, alert red). 'hôm nay về những đơn nào' → list_orders(status Đã nhận, date_field recv, from=to=today). 'sản phẩm hoàn nhiều nhất tháng này' → product_stats. 'tuần này so với tuần trước' → compare_periods. 'sàn/hãng nào nhiều' → aggregate(group_by platform|carrier). 'tìm đơn SPXVN...' → find_order.
        \(snapshotBlock())
        """
    }

    /// Số liệu hiện tại đính sẵn: câu hỏi chỉ cần các số này thì trả lời luôn, KHÔNG gọi công cụ (đỡ tốn token/vòng).
    private func snapshotBlock() -> String {
        let snap = tools.snapshot()
        if snap.isEmpty { return "" }
        return "SỐ LIỆU HIỆN TẠI (tính sẵn, đồng, theo kiện): " + snap + "\n- Chỉ cần các số này thì trả lời NGAY, không gọi công cụ. Cần danh sách/lọc khác/chi tiết/kỳ khác thì gọi công cụ ĐỦ trong một lượt rồi trả lời luôn."
    }

    public func ask(_ question: String) async throws -> AIAnswer {
        var msgs: [[String: Any]] = [["role": "system", "content": systemPrompt()]]
        msgs += history
        msgs.append(["role": "user", "content": question])

        var refs: [AIOrderRef] = []
        var trace: [String] = []
        var used: [String] = []
        guard let toolsData = AIOrderTools.definitionsFor(question).data(using: .utf8),
              let toolsObj = try? JSONSerialization.jsonObject(with: toolsData) else {
            throw AIError("Không đọc được định nghĩa công cụ.")
        }
        defer { tools.releaseCache() }

        var done = Set<String>()
        for round in 0..<Self.maxRounds {
            // 2 vòng cuối: cấm gọi thêm công cụ, buộc chốt đáp án từ dữ liệu đã có.
            var body: [String: Any] = ["messages": msgs, "tools": toolsObj, "tool_choice": round >= Self.maxRounds - 2 ? "none" : "auto", "temperature": 0.1]
            Self.shrinkToBudget(&body)
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
                let json: String
                if !done.insert(name + "|" + args).inserted {
                    // Gọi lại y hệt: không chạy lại, không gửi lại kết quả lớn (đã có ở trên).
                    json = #"{"note":"Đã gọi công cụ này với cùng tham số ở trên; dùng kết quả đó để trả lời."}"#
                } else {
                    let result = tools.execute(name: name, argsJSON: args)
                    refs += result.refs
                    trace.append("\(name)(\(args))")
                    json = result.json.count > Self.maxToolChars ? String(result.json.prefix(Self.maxToolChars)) + "…(đã cắt bớt)" : result.json
                }
                msgs.append(["role": "tool", "tool_call_id": id, "name": name, "content": json])
            }
        }
        throw AIError("Trợ lý gọi công cụ quá nhiều lần mà chưa ra kết quả. Hãy hỏi cụ thể hơn.")
    }

    private func remember(_ q: String, _ a: String) {
        let shortAnswer = a.count > Self.maxHistoryChars ? String(a.prefix(Self.maxHistoryChars)) + "…" : a
        history.append(["role": "user", "content": q])
        history.append(["role": "assistant", "content": shortAnswer])
        while history.count > Self.maxHistory { history.removeFirst() }
    }

    /// Thử kết nối TẤT CẢ mô hình CÙNG LÚC (một yêu cầu rất nhỏ cho mỗi mô hình), nên một mô hình treo không làm cả bước thử bị đứng.
    /// Trả về theo thứ tự mô hình: (nhãn, lỗi hoặc nil nếu dùng được).
    public func testAll() async -> [(label: String, error: String?)] {
        let eps = endpoints
        var results = [(label: String, error: String?)](repeating: ("", nil), count: eps.count)
        await withTaskGroup(of: (Int, String, String?).self) { group in
            for (i, ep) in eps.enumerated() {
                group.addTask { [self] in
                    let body: [String: Any] = ["model": ep.model, "messages": [["role": "user", "content": "Trả lời đúng một từ: ok"]], "max_tokens": 16]
                    do { _ = try await self.post(ep, body); return (i, ep.label, nil) }
                    catch {
                        if (error as? URLError)?.code == .timedOut { return (i, ep.label, "không phản hồi (quá thời gian chờ).") }
                        return (i, ep.label, (error as? AIError)?.message ?? error.localizedDescription)
                    }
                }
            }
            for await r in group { results[r.0] = (r.1, r.2) }
        }
        return results
    }

    // MARK: kích thước yêu cầu

    static func estimateTokens(_ body: [String: Any]) -> Int {
        guard JSONSerialization.isValidJSONObject(body), let d = try? JSONSerialization.data(withJSONObject: body) else { return 0 }
        return Int(Double(d.count) / 2.8)
    }

    /// Chỉ giữ 3 công cụ cốt lõi. Trả true nếu đã bỏ bớt công cụ (đáng thử lại).
    static func keepCoreToolsOnly(_ body: inout [String: Any]) -> Bool {
        guard let tools = body["tools"] as? [[String: Any]] else { return false }
        let core: Set<String> = ["count_orders", "aggregate", "list_orders"]
        let kept = tools.filter { core.contains((($0["function"] as? [String: Any])?["name"] as? String) ?? "") }
        if kept.count == tools.count { return false }
        body["tools"] = kept
        return true
    }

    /// Nếu yêu cầu quá lớn: bỏ bớt lịch sử cũ, rồi chỉ giữ 3 công cụ cốt lõi. Không bao giờ bỏ câu hỏi hiện tại.
    static func shrinkToBudget(_ body: inout [String: Any]) {
        if estimateTokens(body) <= tokenBudget { return }
        if var msgs = body["messages"] as? [[String: Any]] {
            while estimateTokens(body) > tokenBudget, msgs.count > 3, let role = msgs[1]["role"] as? String, role == "user" || role == "assistant" {
                msgs.remove(at: 1)
                body["messages"] = msgs
            }
        }
        if estimateTokens(body) > tokenBudget { _ = keepCoreToolsOnly(&body) }
    }

    // MARK: chuyển dự phòng

    private func postWithFallback(_ body: [String: Any]) async throws -> ([String: Any], AIEndpoint) {
        var errors: [(AIEndpoint, AIError)] = []
        for ep in endpoints {
            if let until = restUntil[ep.label], until > now() { continue }
            var attempt = body
            attempt["model"] = ep.model
            if ep.provider == "gemini" { Self.sanitizeToolsForGemini(&attempt) }
            var waited = false
            for pass in 0..<3 {
                do {
                    let msg = try await post(ep, attempt)
                    return (msg, ep)
                } catch let e as AIError {
                    // Chạm giới hạn theo PHÚT và máy chủ báo chờ chỉ vài giây: chờ rồi thử lại đúng mô hình này, đừng bỏ sang mô hình khác.
                    if e.statusCode == 429 && !waited, let wait = Self.shortRetryDelay(e) {
                        waited = true
                        try await Task.sleep(nanoseconds: UInt64(wait * 1_000_000_000))
                        continue
                    }
                    // 413: yêu cầu vượt giới hạn kích thước của mô hình này: thử lại NGAY với chỉ các công cụ cốt lõi trước khi bỏ sang mô hình khác.
                    if e.statusCode == 413 && pass == 0 && Self.keepCoreToolsOnly(&attempt) { continue }
                    if e.statusCode != 0 {
                        if Self.shouldFallback(e.statusCode) {
                            restUntil[ep.label] = now().addingTimeInterval(Self.restTime(e))
                            errors.append((ep, e))
                            break
                        } else { throw e }
                    } else {
                        errors.append((ep, e))   // lỗi mạng/định dạng: thử mô hình kế tiếp nhưng không bắt nghỉ
                        break
                    }
                } catch {
                    if error is CancellationError { throw error }
                    if (error as? URLError)?.code == .timedOut {
                        restUntil[ep.label] = now().addingTimeInterval(120)   // treo/chậm: cho nghỉ để câu hỏi sau không chờ lại vô ích
                        errors.append((ep, AIError("\(ep.label) không phản hồi (quá thời gian chờ).")))
                    } else {
                        errors.append((ep, AIError("Không kết nối được tới \(ep.provider) (kiểm tra mạng): \(error.localizedDescription)")))
                    }
                    break
                }
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

    /// Thời gian chờ tối đa (giây) cho một lần chờ-rồi-thử-lại khi chạm giới hạn ngắn.
    static var shortRetryMax: TimeInterval = 8

    /// Máy chủ báo "try again in 659ms" / "1.5s" (≤ shortRetryMax) thì trả số giây cần chờ (+0,25 giây), ngược lại nil.
    static func shortRetryDelay(_ e: AIError) -> TimeInterval? {
        var secs: Double?
        if let ra = e.retryAfter { secs = ra }
        else if let re = try? NSRegularExpression(pattern: #"try again in\s+([0-9]+(?:\.[0-9]+)?)\s*(ms|s|m)\b"#, options: [.caseInsensitive]),
                let m = re.firstMatch(in: e.body, range: NSRange(e.body.startIndex..., in: e.body)),
                let r1 = Range(m.range(at: 1), in: e.body), let r2 = Range(m.range(at: 2), in: e.body),
                let v = Double(e.body[r1]) {
            switch e.body[r2].lowercased() {
            case "ms": secs = v / 1000
            case "m": secs = v * 60
            default: secs = v
            }
        }
        guard let s = secs, s <= shortRetryMax else { return nil }
        return max(0, s) + 0.25
    }

    /// Gemini kén lược đồ công cụ: bỏ `properties` rỗng và các khóa nó thường không nhận (additionalProperties, default).
    static func sanitizeToolsForGemini(_ body: inout [String: Any]) {
        guard let tools = body["tools"] as? [[String: Any]] else { return }
        func strip(_ node: Any) -> Any {
            if var d = node as? [String: Any] {
                d.removeValue(forKey: "additionalProperties")
                d.removeValue(forKey: "default")
                for (k, v) in d { d[k] = strip(v) }
                return d
            }
            if let a = node as? [Any] { return a.map { strip($0) } }
            return node
        }
        body["tools"] = tools.map { tool -> [String: Any] in
            var t = tool
            if var fn = t["function"] as? [String: Any] {
                if var p = fn["parameters"] as? [String: Any] {
                    p = (strip(p) as? [String: Any]) ?? p
                    if let props = p["properties"] as? [String: Any], props.isEmpty { fn.removeValue(forKey: "parameters") }
                    else { fn["parameters"] = p }
                }
                t["function"] = fn
            }
            return t
        }
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
        if detail.isEmpty {
            // Không đọc được lý do dạng JSON: hiện nguyên văn (gọn lại), che mã tổ chức.
            let raw = body.split(whereSeparator: { $0.isWhitespace }).joined(separator: " ")
            detail = raw.replacingOccurrences(of: #"org_[A-Za-z0-9]+"#, with: "org_…", options: .regularExpression)
        }
        detail = detail.replacingOccurrences(of: #"org_[A-Za-z0-9]+"#, with: "org_…", options: .regularExpression)
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
