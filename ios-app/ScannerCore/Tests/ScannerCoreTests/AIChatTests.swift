import XCTest
@testable import ScannerCore

/// Trợ lý AI trên iPhone: công cụ truy vấn, quyền riêng tư, vòng gọi công cụ và chuyển dự phòng (máy chủ GIẢ, không gọi mạng).
final class AIChatTests: XCTestCase {
    private var cal: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "Asia/Ho_Chi_Minh")!
        return c
    }
    private var now: Date { cal.date(from: DateComponents(year: 2026, month: 11, day: 25, hour: 14))! }

    private func row(_ k: String = "m", c: String, o: String, n: String = "Áo thun", q: Int = 1, rv: Bool, rcd: String = "", nt: String = "",
                     pr: Int = 0, cr: String = "", ar: Int = 0, lv: Int = -1, wl: Bool = false) -> OrderRow {
        var r = OrderRow(k: k, c: c, o: o, p: "Shopee", n: n, co: "", sz: "", q: q, rd: "20/11/2026", due: 0, st: rv ? "Hàng đã trả" : "Chưa trả")
        r.rv = rv
        r.pd = !rv
        r.rcd = rcd
        r.nt = nt
        r.pr = pr
        r.cr = cr
        r.ar = ar
        r.lv = lv
        r.wl = wl
        return r
    }

    private var sample: [OrderRow] {
        [
            row(c: "VN001", o: "SHP1", n: "Áo thun", q: 2, rv: true, rcd: "25/11/2026 09:00:00"),
            row(c: "VN002", o: "SHP2", n: "Quần jean", rv: true, rcd: "25/11/2026 11:30"),
            row(c: "VN003", o: "SHP3", n: "Áo thun", rv: true, rcd: "24/11/2026 10:00:00"),
            row(c: "VN004", o: "SHP4", rv: false),
            row("a", c: "VN005", o: "SHP5", rv: true, rcd: "25/11/2026 08:00:00"),
        ]
    }

    private func tools(_ rows: [OrderRow]? = nil) -> AIOrderTools {
        let r = rows ?? sample
        return AIOrderTools(rows: { r }, now: { self.now }, calendar: cal)
    }

    private func obj(_ s: String) -> [String: Any] {
        (try? JSONSerialization.jsonObject(with: Data(s.utf8)) as? [String: Any]) ?? [:]
    }

    func testCountReceivedTodayOnlyActive() {
        let r = tools().execute(name: "count_orders", argsJSON: #"{"status":"Hàng đã trả","date_field":"recv","from":"today","to":"today"}"#)
        XCTAssertEqual(obj(r.json)["total"] as? Int, 2)
    }

    func testOverviewAndScopeAll() {
        let o = obj(tools().execute(name: "count_orders", argsJSON: "{}").json)
        XCTAssertEqual(o["total"] as? Int, 4)
        XCTAssertEqual((o["byStatus"] as? [String: Int])?["Hàng đã trả"], 3)
        XCTAssertEqual((o["byStatus"] as? [String: Int])?["Chưa trả"], 1)
        XCTAssertEqual(obj(tools().execute(name: "count_orders", argsJSON: #"{"scope":"all"}"#).json)["total"] as? Int, 5)
    }

    func testListReturnsRefsAndNoRecipientData() {
        let t = tools()
        let r = t.execute(name: "list_orders", argsJSON: #"{"status":"Hàng đã trả","from":"today","to":"today"}"#)
        XCTAssertEqual(r.refs.count, 2)
        XCTAssertEqual(Set(r.refs.map { $0.track }), ["VN001", "VN002"])
        XCTAssertEqual(r.refs[0].track, "VN002")   // mới nhất trước (11:30 sau 09:00)
        XCTAssertFalse(r.json.contains("0912345678"))
    }

    func testPhoneNumbersInNotesAreMaskedButTrackingCodesKept() {
        let t = tools([row(c: "VN123456789012", o: "SHP9", rv: true, rcd: "25/11/2026 09:00:00", nt: "Gọi khách 0987 654 321 hoặc +84912345678 nhé")])
        let j = t.execute(name: "list_orders", argsJSON: "{}").json
        XCTAssertFalse(j.contains("654 321"))
        XCTAssertFalse(j.contains("912345678"))
        XCTAssertTrue(j.contains("ẩn SĐT"))
        XCTAssertTrue(j.contains("VN123456789012"))
    }

    func testFindAndProductStatsAndBadInput() {
        let t = tools()
        XCTAssertEqual(t.execute(name: "find_order", argsJSON: #"{"code":"vn005"}"#).refs.count, 1)
        XCTAssertTrue(t.execute(name: "find_order", argsJSON: #"{"code":"v"}"#).json.contains("error"))
        let p = obj(t.execute(name: "product_stats", argsJSON: #"{"status":"Hàng đã trả"}"#).json)
        let first = (p["products"] as? [[String: Any]])?.first
        XCTAssertEqual(first?["product"] as? String, "Áo thun")
        XCTAssertEqual(first?["qty"] as? Int, 3)
        XCTAssertEqual(first?["orders"] as? Int, 2)
        XCTAssertTrue(t.execute(name: "delete_everything", argsJSON: "{}").json.contains("error"))
        XCTAssertTrue(t.execute(name: "count_orders", argsJSON: "{not json").json.contains("error"))
    }

    // MARK: vòng gọi công cụ và dự phòng

    private final class Fake: AIHTTPTransport {
        var calls: [String] = []   // "host|model"
        let reply: (String, String, [String: Any]) -> (Int, String)
        init(_ reply: @escaping (String, String, [String: Any]) -> (Int, String)) { self.reply = reply }
        func post(url: URL, headers: [String: String], body: Data) async throws -> (Int, Data, TimeInterval?) {
            let obj = (try? JSONSerialization.jsonObject(with: body) as? [String: Any]) ?? [:]
            let model = (obj["model"] as? String) ?? ""
            let host = url.host ?? ""
            calls.append(host + "|" + model)
            let (code, text) = reply(host, model, obj)
            return (code, Data(text.utf8), nil)
        }
    }

    private func ok(_ text: String) -> String {
        let o: [String: Any] = ["choices": [["message": ["role": "assistant", "content": text]]]]
        return String(data: try! JSONSerialization.data(withJSONObject: o), encoding: .utf8)!
    }

    private func toolCall(_ name: String, _ args: String) -> String {
        let o: [String: Any] = ["choices": [["message": ["role": "assistant", "content": NSNull(),
            "tool_calls": [["id": "", "type": "function", "function": ["name": name, "arguments": args]]]]]]]
        return String(data: try! JSONSerialization.data(withJSONObject: o), encoding: .utf8)!
    }

    private let limit = #"{"error":{"message":"Rate limit reached (TPM)"}}"#

    private func chain() -> [AIEndpoint] {
        [AIEndpoint(provider: "groq", baseURL: AIEndpoint.groqBaseURL, apiKey: "k1", model: "openai/gpt-oss-120b"),
         AIEndpoint(provider: "groq", baseURL: AIEndpoint.groqBaseURL, apiKey: "k1", model: "qwen/qwen3.8-27b"),
         AIEndpoint(provider: "groq", baseURL: AIEndpoint.groqBaseURL, apiKey: "k1", model: "openai/gpt-oss-20b"),
         AIEndpoint(provider: "gemini", baseURL: AIEndpoint.geminiBaseURL, apiKey: "k2", model: "gemini-2.5-flash")]
    }

    private func service(_ f: Fake, _ eps: [AIEndpoint]? = nil) -> AIChatService {
        AIChatService(transport: f, endpoints: eps ?? chain(), tools: tools(), now: { self.now })
    }

    func testToolLoopRunsRequestedToolAndReturnsAnswerWithRefs() async throws {
        var turn = 0
        let f = Fake { _, _, req in
            turn += 1
            if turn == 1 { return (200, self.toolCall("list_orders", #"{"status":"Hàng đã trả","from":"today","to":"today"}"#)) }
            let last = (req["messages"] as? [[String: Any]])?.last
            XCTAssertEqual(last?["role"] as? String, "tool")
            XCTAssertEqual(last?["name"] as? String, "list_orders")
            return (200, self.ok("Hôm nay có 2 kiện về."))
        }
        let a = try await service(f).ask("hôm nay có bao nhiêu đơn về?")
        XCTAssertEqual(a.text, "Hôm nay có 2 kiện về.")
        XCTAssertEqual(a.refs.count, 2)
        XCTAssertTrue(a.trace[0].hasPrefix("Mô hình: "))
    }

    func testFirstModelLimitedFallsBackAndRemembersRest() async throws {
        let f = Fake { _, m, _ in m == "openai/gpt-oss-120b" ? (429, self.limit) : (200, self.ok("bởi " + m)) }
        let svc = service(f)
        let a1 = try await svc.ask("1")
        XCTAssertEqual(a1.text, "bởi qwen/qwen3.8-27b")
        let a2 = try await svc.ask("2")
        XCTAssertEqual(a2.text, "bởi qwen/qwen3.8-27b")
        XCTAssertEqual(f.calls, ["api.groq.com|openai/gpt-oss-120b", "api.groq.com|qwen/qwen3.8-27b", "api.groq.com|qwen/qwen3.8-27b"])
    }

    func testAllGroqLimitedThenGoogleAnswers() async throws {
        let f = Fake { host, m, _ in host.contains("groq") ? (429, self.limit) : (200, self.ok("từ " + m)) }
        let a = try await service(f).ask("hi")
        XCTAssertEqual(a.text, "từ gemini-2.5-flash")
        XCTAssertEqual(f.calls.count, 4)
    }

    func testMissingModelIsSkippedAndAllFailingListsEach() async {
        let f404 = Fake { _, m, _ in m == "openai/gpt-oss-120b" ? (404, #"{"error":{"message":"not found"}}"#) : (200, self.ok("ok " + m)) }
        let a = try? await service(f404).ask("hi")
        XCTAssertEqual(a?.text, "ok qwen/qwen3.8-27b")

        let fAll = Fake { _, _, _ in (429, self.limit) }
        let svc = service(fAll)
        do { _ = try await svc.ask("hi"); XCTFail("phải lỗi") } catch {
            let m = (error as? AIError)?.message ?? ""
            XCTAssertTrue(m.contains("openai/gpt-oss-120b"))
            XCTAssertTrue(m.contains("gemini/gemini-2.5-flash"))
            XCTAssertFalse(m.contains("k1"))
        }
        let before = fAll.calls.count
        do { _ = try await svc.ask("hi"); XCTFail("phải lỗi") } catch {
            XCTAssertTrue(((error as? AIError)?.message ?? "").contains("nghỉ"))
        }
        XCTAssertEqual(fAll.calls.count, before)
    }

    func testSingleEndpointKeepsClearMessages() async {
        let one = [AIEndpoint(provider: "groq", baseURL: AIEndpoint.groqBaseURL, apiKey: "k", model: "m")]
        for (code, expect) in [(401, "Khóa API"), (429, "giới hạn"), (404, "mô hình")] {
            let f = Fake { _, _, _ in (code, #"{"error":{"message":"x"}}"#) }
            do { _ = try await service(f, one).ask("hi"); XCTFail("phải lỗi") } catch {
                XCTAssertTrue(((error as? AIError)?.message ?? "").contains(expect), "\(code)")
            }
        }
    }

    func testSplitModelsKeepsOrderAndDedups() {
        XCTAssertEqual(AIEndpoint.splitModels("a, b\nb;c", fallback: "x"), ["a", "b", "c"])
        XCTAssertEqual(AIEndpoint.splitModels("  ", fallback: "x"), ["x"])
    }

    // MARK: gói miễn phí giới hạn token: yêu cầu phải nhỏ

    private func toolNames(_ json: String) -> Set<String> {
        guard let arr = (try? JSONSerialization.jsonObject(with: Data(json.utf8))) as? [[String: Any]] else { return [] }
        return Set(arr.compactMap { ($0["function"] as? [String: Any])?["name"] as? String })
    }

    func testOnlyRelevantToolsAreSentAndPayloadStaysSmall() {
        let today = AIOrderTools.definitionsFor("hôm nay có bao nhiêu đơn về?")
        XCTAssertTrue(toolNames(today).contains("count_orders"))
        XCTAssertTrue(toolNames(today).contains("overview"))
        XCTAssertLessThan(today.count, 7600)
        XCTAssertLessThan(AIOrderTools.definitionsFor("giá trị chưa về và sản phẩm nào nhiều nhất").count, 8200)
        XCTAssertTrue(toolNames(AIOrderTools.definitionsFor("so với hôm qua thì sao")).contains("compare_periods"))
        XCTAssertTrue(toolNames(AIOrderTools.definitionsFor("làm sao để quét")).contains("app_guide"))
        XCTAssertTrue(toolNames(AIOrderTools.definitionsFor("kho xử lý trung bình bao lâu")).contains("turnaround_stats"))
        XCTAssertTrue(toolNames(AIOrderTools.definitionsFor("tìm đơn 2611ABC123XYZ")).contains("find_order"))
        XCTAssertTrue(toolNames(AIOrderTools.definitionsFor("có mã nào bị trùng không")).contains("duplicates"))
        XCTAssertFalse(toolNames(AIOrderTools.definitionsFor("bao nhiêu đơn shopee hôm nay")).contains("app_guide"))
        let odd = toolNames(AIOrderTools.definitionsFor("xyz"))
        XCTAssertTrue(odd.isSuperset(of: ["count_orders", "aggregate", "list_orders"]))
        XCTAssertGreaterThanOrEqual(toolNames(AIOrderTools.definitionsJSON).count, 17)
    }

    func testTypicalRequestIsWellUnderTheFreeTierLimit() async throws {
        var sizes: [Int] = []
        let f = Fake { _, _, req in
            sizes.append(AIChatService.estimateTokens(req))
            return (200, self.ok("xong"))
        }
        let one = [AIEndpoint(provider: "groq", baseURL: AIEndpoint.groqBaseURL, apiKey: "k", model: "m")]
        _ = try await service(f, one).ask("giá trị chưa về đang là bao nhiêu tiền và sản phẩm nào chưa về nhiều nhất")
        XCTAssertLessThan(sizes.first ?? 99999, 4500)
    }

    func testLongHistoryIsTrimmed() async throws {
        let long = String(repeating: "x", count: 3000)
        var lastCount = 0
        var lastTokens = 0
        let f = Fake { _, _, req in
            lastCount = (req["messages"] as? [[String: Any]])?.count ?? 0
            lastTokens = AIChatService.estimateTokens(req)
            return (200, self.ok(long))
        }
        let one = [AIEndpoint(provider: "groq", baseURL: AIEndpoint.groqBaseURL, apiKey: "k", model: "m")]
        let svc = service(f, one)
        for i in 0..<10 { _ = try await svc.ask("câu hỏi số \(i)") }
        XCTAssertLessThanOrEqual(lastCount, 8)
        XCTAssertLessThan(lastTokens, 4500)
    }

    func testHTTP413RetriesSameModelWithCoreToolsOnly() async throws {
        var toolCounts: [Int] = []
        var models: [String] = []
        let f = Fake { _, m, req in
            let n = (req["tools"] as? [[String: Any]])?.count ?? 0
            toolCounts.append(n)
            models.append(m)
            return n > 3 ? (413, #"{"error":{"message":"Request too large for model"}}"#) : (200, self.ok("trả lời gọn"))
        }
        let one = [AIEndpoint(provider: "groq", baseURL: AIEndpoint.groqBaseURL, apiKey: "k", model: "openai/gpt-oss-120b")]
        let a = try await service(f, one).ask("so với hôm qua tình hình hôm nay thế nào")
        XCTAssertEqual(a.text, "trả lời gọn")
        XCTAssertEqual(toolCounts.count, 2)
        XCTAssertGreaterThan(toolCounts[0], 3)
        XCTAssertEqual(toolCounts[1], 3)
        XCTAssertEqual(models[0], models[1])
    }

    // MARK: số liệu tính sẵn, chống vòng lặp, chờ ngắn, Gemini

    func testSnapshotHasCurrentCountsAndNoPersonalData() {
        let snap = tools([
            row(c: "VN001", o: "SHP1", q: 2, rv: true, rcd: "25/11/2026 09:00:00", nt: "Gọi 0912345678"),
            row(c: "VN002", o: "SHP2", rv: true, rcd: "25/11/2026 11:30"),
            row(c: "VN003", o: "SHP3", rv: true, rcd: "24/11/2026 10:00:00"),
            row(c: "VN004", o: "SHP4", n: "Quần jean", q: 3, rv: false, pr: 100000),
        ]).snapshot()
        let j = obj(snap)
        XCTAssertEqual(j["activeOrders"] as? Int, 4)
        XCTAssertEqual((j["waiting"] as? [String: Any])?["count"] as? Int, 1)
        XCTAssertEqual((j["receivedToday"] as? [String: Any])?["count"] as? Int, 2)
        XCTAssertEqual((j["receivedYesterday"] as? [String: Any])?["count"] as? Int, 1)
        XCTAssertEqual((j["received7days"] as? [String: Any])?["count"] as? Int, 3)
        XCTAssertEqual(((j["topWaitingProducts"] as? [[String: Any]])?.first)?["name"] as? String, "Quần jean")
        XCTAssertFalse(snap.contains("0912345678"))
        XCTAssertLessThan(snap.count, 1500)
    }

    func testSystemPromptCarriesSnapshotSoSimpleQuestionNeedsNoTool() async throws {
        var systems: [String] = []
        let f = Fake { _, _, req in
            systems.append(((req["messages"] as? [[String: Any]])?.first?["content"] as? String) ?? "")
            return (200, self.ok("Đang chờ 1 kiện."))
        }
        let one = [AIEndpoint(provider: "groq", baseURL: AIEndpoint.groqBaseURL, apiKey: "k", model: "m")]
        let a = try await service(f, one).ask("đang chờ bao nhiêu đơn?")
        XCTAssertEqual(a.text, "Đang chờ 1 kiện.")
        XCTAssertEqual(systems.count, 1)
        XCTAssertTrue(systems[0].contains("SỐ LIỆU HIỆN TẠI"))
        XCTAssertTrue(systems[0].contains("receivedToday"))
        XCTAssertTrue(systems[0].contains("CÓ TRONG APP"))
    }

    func testLastRoundsForbidToolCallsAndEndWithClearError() async {
        var choices: [String] = []
        var n = 0
        let f = Fake { _, _, req in
            n += 1
            choices.append((req["tool_choice"] as? String) ?? "")
            return (200, self.toolCall("count_orders", "{\"from\":\"\(n)\"}"))
        }
        let one = [AIEndpoint(provider: "groq", baseURL: AIEndpoint.groqBaseURL, apiKey: "k", model: "m")]
        do { _ = try await service(f, one).ask("đếm đơn"); XCTFail("phải báo lỗi") } catch {}
        XCTAssertEqual(choices.first, "auto")
        XCTAssertEqual(Array(choices.suffix(2)), ["none", "none"])
    }

    func testIdenticalRepeatedToolCallIsNotExecutedTwice() async throws {
        var turn = 0
        var toolContents: [String] = []
        let f = Fake { _, _, req in
            turn += 1
            if turn == 1 { return (200, self.toolCall("count_orders", "{}")) }
            if turn == 2 { return (200, self.toolCall("count_orders", "{}")) }
            toolContents = ((req["messages"] as? [[String: Any]]) ?? []).filter { ($0["role"] as? String) == "tool" }.map { ($0["content"] as? String) ?? "" }
            return (200, self.ok("xong"))
        }
        let one = [AIEndpoint(provider: "groq", baseURL: AIEndpoint.groqBaseURL, apiKey: "k", model: "m")]
        let a = try await service(f, one).ask("đếm đơn")
        XCTAssertEqual(a.text, "xong")
        XCTAssertEqual(a.trace.filter { $0.hasPrefix("count_orders") }.count, 1)
        XCTAssertEqual(toolContents.count, 2)
        XCTAssertTrue(toolContents[0].contains("total"))
        XCTAssertTrue(toolContents[1].contains("Đã gọi công cụ này"))
    }

    func testShortPerMinuteLimitWaitsAndRetriesSameModel() async throws {
        let wait = #"{"error":{"message":"Rate limit reached on tokens per minute (TPM). Please try again in 300ms."}}"#
        var n = 0
        let f = Fake { _, _, _ in
            n += 1
            return n == 1 ? (429, wait) : (200, self.ok("xong"))
        }
        let eps = [AIEndpoint(provider: "groq", baseURL: AIEndpoint.groqBaseURL, apiKey: "k", model: "m1"),
                   AIEndpoint(provider: "groq", baseURL: AIEndpoint.groqBaseURL, apiKey: "k", model: "m2")]
        let a = try await service(f, eps).ask("đếm đơn")
        XCTAssertEqual(a.text, "xong")
        XCTAssertEqual(f.calls.map { $0.split(separator: "|").last.map(String.init) ?? "" }, ["m1", "m1"])
    }

    func testLongWaitLimitStillFallsBackToNextModel() async throws {
        let wait = #"{"error":{"message":"Rate limit. Please try again in 45s."}}"#
        let f = Fake { _, m, _ in m == "m1" ? (429, wait) : (200, self.ok("từ m2")) }
        let eps = [AIEndpoint(provider: "groq", baseURL: AIEndpoint.groqBaseURL, apiKey: "k", model: "m1"),
                   AIEndpoint(provider: "groq", baseURL: AIEndpoint.groqBaseURL, apiKey: "k", model: "m2")]
        let a = try await service(f, eps).ask("đếm đơn")
        XCTAssertEqual(a.text, "từ m2")
        XCTAssertEqual(f.calls.count, 2)
    }

    func testGeminiToolsLoseEmptyPropertiesAndUnsupportedKeys() {
        var body: [String: Any] = ["tools": [
            ["type": "function", "function": ["name": "a", "parameters": ["type": "object", "properties": [String: Any]()]]],
            ["type": "function", "function": ["name": "b", "parameters": ["type": "object", "additionalProperties": false,
                "properties": ["x": ["type": "string", "default": "1"]]]]],
        ]]
        AIChatService.sanitizeToolsForGemini(&body)
        let tools = body["tools"] as? [[String: Any]] ?? []
        XCTAssertNil((tools[0]["function"] as? [String: Any])?["parameters"])
        let b = String(data: (try? JSONSerialization.data(withJSONObject: (tools[1]["function"] as? [String: Any])?["parameters"] ?? [:])) ?? Data(), encoding: .utf8) ?? ""
        XCTAssertFalse(b.contains("additionalProperties"))
        XCTAssertFalse(b.contains("default"))
        XCTAssertTrue(b.contains("\"x\""))
    }

    func testBadRequestWithoutReadableReasonShowsRawBodyAndHidesOrgIds() {
        let ep = AIEndpoint(provider: "gemini", baseURL: AIEndpoint.geminiBaseURL, apiKey: "k", model: "g")
        let msg = AIChatService.describe(ep, 400, "Unknown field at tools[3] org_01abcDEF123 oops")
        XCTAssertTrue(msg.contains("Unknown field at tools[3]"))
        XCTAssertFalse(msg.contains("org_01abcDEF123"))
        XCTAssertTrue(msg.contains("org_…"))
    }

    func testAppGuideHasScreenMapDataDictionaryAndSampleQuestions() {
        let t = tools()
        XCTAssertTrue(t.execute(name: "app_guide", argsJSON: #"{"topic":"man_hinh"}"#).json.contains("Lịch sử"))
        XCTAssertTrue(t.execute(name: "app_guide", argsJSON: #"{"topic":"y_nghia_du_lieu"}"#).json.contains("Đang chờ"))
        XCTAssertTrue(t.execute(name: "app_guide", argsJSON: #"{"topic":"cau_hoi_mau"}"#).json.contains("list_orders"))
    }

    func testListRowsAreCompactNoEmptyFields() {
        var r = OrderRow(k: "m", c: "T1", o: "O1", p: "Shopee", n: "Áo", co: "", sz: "", q: 1, rd: "", due: 0, st: "Chưa trả")
        r.pr = 1000
        r.nt = "ghi chú 0987654321"
        let t = AIOrderTools(rows: { [r] }, now: { self.now }, calendar: cal)
        let json = t.execute(name: "list_orders", argsJSON: "{}").json
        let row = (obj(json)["orders"] as? [[String: Any]])?.first ?? [:]
        XCTAssertNil(row["carrier"])
        XCTAssertNil(row["watching"])
        XCTAssertNil(row["qty"])
        XCTAssertFalse(json.contains("0987654321"))
    }
}
