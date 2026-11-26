import SwiftUI
import Security
import ScannerCore

/// Khóa API lưu trong Keychain của điện thoại (không đồng bộ iCloud, không vào bản sao lưu), không bao giờ hiện lại trên màn hình.
enum AIKeychain {
    private static let service = "qlhh.ai"

    static func load(_ account: String) -> String? {
        let q: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
                                kSecAttrService as String: service,
                                kSecAttrAccount as String: account,
                                kSecReturnData as String: true,
                                kSecMatchLimit as String: kSecMatchLimitOne]
        var out: AnyObject?
        guard SecItemCopyMatching(q as CFDictionary, &out) == errSecSuccess,
              let data = out as? Data,
              let s = String(data: data, encoding: .utf8),
              !s.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
        return s
    }

    static func save(_ account: String, _ value: String) {
        let v = value.trimmingCharacters(in: .whitespacesAndNewlines)
        if v.isEmpty { delete(account); return }
        delete(account)
        let q: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
                                kSecAttrService as String: service,
                                kSecAttrAccount as String: account,
                                kSecValueData as String: Data(v.utf8),
                                kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly]
        SecItemAdd(q as CFDictionary, nil)
    }

    static func delete(_ account: String) {
        let q: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
                                kSecAttrService as String: service,
                                kSecAttrAccount as String: account]
        SecItemDelete(q as CFDictionary)
    }
}

/// Bản chụp danh sách đơn cho công cụ AI chạy nền (khóa khi đọc/ghi, tránh đọc dữ liệu giao diện từ luồng khác).
final class AIRowsBox: @unchecked Sendable {
    private let lock = NSLock()
    private var rows: [OrderRow] = []
    func set(_ r: [OrderRow]) { lock.lock(); rows = r; lock.unlock() }
    func get() -> [OrderRow] { lock.lock(); defer { lock.unlock() }; return rows }
}

/// Dữ kiện (tình trạng kết nối, lịch sử quét) chụp trên luồng chính cho công cụ AI chạy nền.
final class AIFactsBox: @unchecked Sendable {
    private let lock = NSLock()
    private var statusFacts: [String: Any] = [:]
    private var scanFacts: [[String: Any]] = []
    func set(status: [String: Any], scans: [[String: Any]]) { lock.lock(); statusFacts = status; scanFacts = scans; lock.unlock() }
    func status() -> [String: Any] { lock.lock(); defer { lock.unlock() }; return statusFacts }
    func scans() -> [[String: Any]] { lock.lock(); defer { lock.unlock() }; return scanFacts }
}

struct AIMessage: Identifiable {
    let id = UUID()
    let isUser: Bool
    let text: String
    var refs: [AIOrderRef] = []
    /// Giờ gửi (câu hỏi) hoặc giờ nhận (câu trả lời).
    var time: Date = Date()
    /// Với câu trả lời: thời gian chờ AI (giây).
    var elapsed: TimeInterval = 0

    var isError: Bool { !isUser && text.hasPrefix("⚠") }

    /// Giờ hiển thị: hôm nay chỉ giờ:phút:giây, ngày khác thêm ngày/tháng.
    var timeText: String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "vi_VN")
        f.dateFormat = Calendar.current.isDateInToday(time) ? "HH:mm:ss" : "dd/MM HH:mm:ss"
        return f.string(from: time)
    }

    var caption: String {
        if isUser { return timeText }
        guard elapsed >= 0.5 else { return timeText }
        let s = String(format: "%.1f", elapsed).replacingOccurrences(of: ".", with: ",")
        return timeText + " · trả lời sau " + s + " giây"
    }
}

@MainActor
final class AIChatViewModel: ObservableObject {
    @Published var messages: [AIMessage] = []
    @Published var input = ""
    @Published var isBusy = false
    @Published var status = ""
    @Published var hasKey = false
    /// Đã ghép cloud và đã hỏi được cloud: AI đi qua cổng AI của cloud (khóa chỉ nằm trên cloud), khóa trên điện thoại không dùng.
    @Published var usesCloud = false
    private var cloudAI: (groq: Bool, gemini: Bool)?

    @Published var groqModels: String
    @Published var geminiModels: String

    private var service: AIChatService?
    private var tools: AIOrderTools?
    private weak var store: SnapshotStore?
    private let rowsBox = AIRowsBox()
    private let factsBox = AIFactsBox()
    private weak var engine: SyncEngine?

    init() {
        groqModels = UserDefaults.standard.string(forKey: "ai.models.groq") ?? AIEndpoint.defaultGroqModels
        geminiModels = UserDefaults.standard.string(forKey: "ai.models.gemini") ?? AIEndpoint.defaultGeminiModels
        refreshKeys()
        status = hasKey ? "Sẵn sàng. Hãy hỏi về đơn hàng của bạn." : "Chưa có khóa API. Chạm ⚙ để nhập khóa Groq và/hoặc Google."
    }

    func bind(_ store: SnapshotStore) { self.store = store }
    func bindEngine(_ engine: SyncEngine) {
        self.engine = engine
        Task { await refreshCloudAI() }
    }

    /// Hỏi cloud có khóa AI nào (khi đã ghép). Lỗi mạng thì giữ trạng thái cũ.
    func refreshCloudAI() async {
        guard let e = engine, e.cloudActive, let api = e.cloud.currentAPI else {
            if usesCloud { usesCloud = false; cloudAI = nil; service = nil; refreshKeys() }
            return
        }
        guard let st = try? await api.aiStatus() else { return }
        cloudAI = st
        usesCloud = true
        service = nil
        refreshKeys()
        status = hasKey ? "Sẵn sàng (AI dùng khóa trên cloud). Hãy hỏi về đơn hàng của bạn."
                        : "Cloud chưa có khóa AI: chủ cửa hàng đặt khóa Groq/Google trên cloud."
    }

    private func refreshKeys() {
        if usesCloud, let c = cloudAI { hasKey = c.groq || c.gemini; return }
        hasKey = AIKeychain.load("groq") != nil || AIKeychain.load("gemini") != nil
    }

    private func endpoints() -> [AIEndpoint] {
        if usesCloud, let c = cloudAI {
            return engine?.cloud.currentAPI?.aiEndpoints(groq: c.groq, gemini: c.gemini, groqModels: groqModels, geminiModels: geminiModels) ?? []
        }
        var out: [AIEndpoint] = []
        if let k = AIKeychain.load("groq") {
            out += AIEndpoint.build(provider: "groq", baseURL: AIEndpoint.groqBaseURL, apiKey: k, models: groqModels, fallbackModel: "openai/gpt-oss-120b")
        }
        if let k = AIKeychain.load("gemini") {
            out += AIEndpoint.build(provider: "gemini", baseURL: AIEndpoint.geminiBaseURL, apiKey: k, models: geminiModels, fallbackModel: "gemini-2.5-flash")
        }
        return out
    }

    private func ensureService() -> AIChatService? {
        if let s = service { return s }
        let eps = endpoints()
        if eps.isEmpty { return nil }
        let box = rowsBox
        let fbox = factsBox
        let t = AIOrderTools(rows: { box.get() }, statusProvider: { fbox.status() }, scanHistory: { fbox.scans() })
        tools = t
        let s = AIChatService(endpoints: eps, tools: t)
        service = s
        return s
    }

    var canSend: Bool { hasKey && !isBusy && !input.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }

    func send(_ preset: String? = nil) {
        let q = (preset ?? input).trimmingCharacters(in: .whitespacesAndNewlines)
        guard !q.isEmpty, !isBusy, let svc = ensureService() else { return }
        rowsBox.set(store?.rows ?? [])   // chụp danh sách đơn ngay trên luồng chính; công cụ chạy nền chỉ đọc bản chụp này
        factsBox.set(status: currentStatus(), scans: currentScans())
        input = ""
        let started = Date()
        messages.append(AIMessage(isUser: true, text: q, time: started))
        isBusy = true
        status = "Đang hỏi AI..."
        Task {
            do {
                let a = try await svc.ask(q)
                let done = Date()
                messages.append(AIMessage(isUser: false, text: a.text, refs: a.refs, time: done, elapsed: done.timeIntervalSince(started)))
                status = "Sẵn sàng."
            } catch {
                let msg = (error as? AIError)?.message ?? error.localizedDescription
                messages.append(AIMessage(isUser: false, text: "⚠ " + msg, time: Date()))
                status = "Lỗi."
            }
            isBusy = false
        }
    }

    func row(for ref: AIOrderRef) -> OrderRow? { tools?.row(forId: ref.id) }

    /// Tình trạng kết nối và danh sách (cho công cụ system_status). Không chứa mã phòng hay khóa.
    private func currentStatus() -> [String: Any] {
        var d: [String: Any] = [:]
        if let e = engine {
            d["pcConnection"] = e.channelUp ? "đang kết nối" : "chưa kết nối"
            d["mqttConnected"] = e.mqttConnected
            d["ntfyConnected"] = e.sseConnected
            d["pendingScans"] = e.pendingCount
        }
        if let s = store {
            d["listRows"] = s.rows.count
            d["notificationsAllowed"] = s.notificationsAllowed
            d["demoMode"] = s.isDemoMode
            if let u = s.updatedAt {
                let f = DateFormatter()
                f.locale = Locale(identifier: "vi_VN")
                f.dateFormat = "dd/MM/yyyy HH:mm"
                d["listUpdatedAt"] = f.string(from: u)
            } else {
                d["listUpdatedAt"] = "chưa nhận danh sách"
            }
        }
        d["appVersion"] = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? ""
        return d
    }

    /// Các lượt quét gần đây (mới nhất trước) cho công cụ scan_history.
    private func currentScans() -> [[String: Any]] {
        guard let e = engine else { return [] }
        let sorted = e.items.sorted { $0.time > $1.time }
        var out: [[String: Any]] = []
        for it in sorted.prefix(200) {
            var o: [String: Any] = [:]
            o["time"] = it.time
            o["code"] = it.code
            o["kind"] = String(describing: it.kind)
            o["status"] = String(describing: it.status)
            o["detail"] = it.detail
            o["confirmed"] = it.scanConfirmed
            out.append(o)
        }
        return out
    }

    func newChat() {
        messages.removeAll()
        service?.reset()
        status = hasKey ? "Đã bắt đầu cuộc trò chuyện mới." : status
    }

    /// Lưu khóa (nếu nhập) và danh sách mô hình, rồi thử kết nối thật từng mô hình.
    func saveSettings(groqKey: String, geminiKey: String) {
        if !groqKey.trimmingCharacters(in: .whitespaces).isEmpty { AIKeychain.save("groq", groqKey) }
        if !geminiKey.trimmingCharacters(in: .whitespaces).isEmpty { AIKeychain.save("gemini", geminiKey) }
        if groqModels.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { groqModels = AIEndpoint.defaultGroqModels }
        if geminiModels.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { geminiModels = AIEndpoint.defaultGeminiModels }
        UserDefaults.standard.set(groqModels, forKey: "ai.models.groq")
        UserDefaults.standard.set(geminiModels, forKey: "ai.models.gemini")
        service = nil
        refreshKeys()
        guard hasKey else { status = "Chưa có khóa API."; return }
        let eps = endpoints()
        isBusy = true
        status = "Đang thử kết nối từng mô hình..."
        Task {
            let t = AIOrderTools(rows: { [] })
            let results = await AIChatService(endpoints: eps, tools: t).testAll()
            let ok = results.filter { $0.error == nil }.count
            let lines = results.map { ($0.error == nil ? "✓ " : "✗ ") + $0.label + ($0.error.map { ": " + $0 } ?? "") }
            status = (ok > 0 ? "Dùng được \(ok)/\(results.count) mô hình." : "Không mô hình nào dùng được.") + "\n" + lines.joined(separator: "\n")

            isBusy = false
        }
    }

    func forgetKeys() {
        AIKeychain.delete("groq")
        AIKeychain.delete("gemini")
        service = nil
        refreshKeys()
        status = "Đã xóa các khóa API khỏi điện thoại này."
    }
}

/// Cửa sổ chat dạng popup (bảng trượt) mở từ nút ✨ nổi. Dùng chung một AIChatViewModel của cả app, nên cuộc trò chuyện còn nguyên khi đổi tab.
struct AIChatView: View {
    @EnvironmentObject var store: SnapshotStore
    @EnvironmentObject var vm: AIChatViewModel
    @EnvironmentObject var router: NotificationRouter
    @Environment(\.dismiss) private var dismiss
    @State private var openRow: OrderRow?
    @FocusState private var focused: Bool

    private let suggestions: [String] = [
        "Hôm nay có bao nhiêu đơn về?",
        "Liệt kê đơn chưa trả của Shopee",
        "Sản phẩm nào hoàn nhiều nhất tuần này?",
        "Tìm đơn có mã chứa 5678",
    ]

    var body: some View {
        NavigationStack {
            VStack(spacing: 0) {
                messagesScroll
                statusLine
                inputBar
            }
            .navigationTitle("Hỏi AI")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .navigationBarLeading) {
                    Button("Mới") { vm.newChat() }.disabled(vm.messages.isEmpty)
                }
                ToolbarItem(placement: .navigationBarTrailing) {
                    Button("Đóng") { dismiss() }
                }
            }
            .sheet(item: $openRow) { row in DetailView(row: row) }
            .onAppear { vm.bind(store); Task { await vm.refreshCloudAI() } }
        }
    }

    private var messagesScroll: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 10) {
                    if !vm.hasKey { keyNotice }
                    if vm.messages.isEmpty { emptyState }
                    ForEach(vm.messages) { m in
                        AIMessageRow(message: m) { ref in
                            if let row = vm.row(for: ref) { openRow = row }
                        }
                        .id(m.id)
                    }
                    if vm.isBusy { AITypingBubble() }
                }
                .padding(14)
            }
            .scrollDismissesKeyboard(.interactively)
            .onChange(of: vm.messages.count) { _ in
                if let last = vm.messages.last {
                    withAnimation { proxy.scrollTo(last.id, anchor: .bottom) }
                }
            }
        }
    }

    private var keyNotice: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Chưa có khóa API. Hãy nhập khóa Groq và/hoặc Google trong Cài đặt.")
                .font(.subheadline)
            Button("Mở Cài đặt trợ lý AI") {
                dismiss()
                router.tab = NotificationRouter.settingsTab
            }
            .font(.subheadline.bold())
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.yellow.opacity(0.22))
        .clipShape(RoundedRectangle(cornerRadius: 10))
    }

    private var emptyState: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("AI chỉ ĐỌC dữ liệu, không sửa gì. Tên, số điện thoại, địa chỉ người nhận được ẩn, không gửi ra ngoài. Dữ liệu là bản đã đồng bộ gần nhất từ máy tính.")
                .font(.footnote)
                .foregroundColor(.secondary)
            Text("Gợi ý câu hỏi:")
                .font(.footnote.bold())
                .padding(.top, 6)
            ForEach(suggestions, id: \.self) { s in
                suggestionButton(s)
            }
        }
    }

    private func suggestionButton(_ s: String) -> some View {
        Button {
            vm.send(s)
        } label: {
            Text(s)
                .font(.subheadline)
                .padding(.horizontal, 12)
                .padding(.vertical, 8)
                .background(Theme.accent.opacity(0.12))
                .clipShape(RoundedRectangle(cornerRadius: 10))
        }
        .disabled(!vm.hasKey || vm.isBusy)
    }

    private var statusLine: some View {
        Text(vm.status)
            .font(.caption)
            .foregroundColor(.secondary)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 14)
            .padding(.top, 4)
    }

    private var inputBar: some View {
        HStack(spacing: 8) {
            TextField("Hỏi về đơn hàng...", text: $vm.input)
                .focused($focused)
                .submitLabel(.send)
                .onSubmit { vm.send() }
                .disabled(!vm.hasKey)
                .padding(.horizontal, 14)
                .padding(.vertical, 10)
                .background(Color(.secondarySystemBackground))
                .clipShape(Capsule())
            Button {
                vm.send()
            } label: {
                Image(systemName: "arrow.up")
                    .font(.system(size: 16, weight: .bold))
                    .foregroundColor(.white)
                    .frame(width: 38, height: 38)
                    .background(Circle().fill(vm.canSend ? Color(red: 0.31, green: 0.27, blue: 0.90) : Color.gray.opacity(0.4)))
            }
            .disabled(!vm.canSend)
        }
        .padding(12)
    }
}

/// Cấu hình trợ lý AI nằm trong Cài đặt: khóa API (Keychain) và danh sách mô hình theo thứ tự ưu tiên.
struct AIKeySettingsView: View {
    @EnvironmentObject var vm: AIChatViewModel
    @State private var groqKey = ""
    @State private var geminiKey = ""

    var body: some View {
        Form {
            if vm.usesCloud {
                Section(footer: Text("Khóa cũ trên điện thoại không còn được dùng; bấm \"Xóa các khóa khỏi điện thoại\" để xóa hẳn.")) {
                    Label("Điện thoại đang dùng cloud: AI dùng khóa Groq/Google đặt trên cloud (chung cho mọi máy), không cần nhập khóa ở đây.",
                          systemImage: "icloud")
                        .font(.footnote)
                }
            } else {
            Section(header: Text("Groq (console.groq.com)")) {
                SecureField("Khóa API Groq (để trống = giữ khóa đã lưu)", text: $groqKey)
                    .textInputAutocapitalization(.never).autocorrectionDisabled(true)
                TextField("Mô hình theo thứ tự ưu tiên", text: $vm.groqModels, axis: .vertical)
                    .textInputAutocapitalization(.never).autocorrectionDisabled(true)
                    .font(.system(.footnote, design: .monospaced))
            }
            Section(header: Text("Google Gemini (aistudio.google.com)"),
                    footer: Text("Hết hạn mức mô hình đầu thì tự chuyển sang mô hình sau, rồi sang Google. Khóa lưu trong Keychain của điện thoại và không hiện lại. Dữ liệu gửi đi không có tên, số điện thoại, địa chỉ người nhận.")) {
                SecureField("Khóa API Google (để trống = giữ khóa đã lưu)", text: $geminiKey)
                    .textInputAutocapitalization(.never).autocorrectionDisabled(true)
                TextField("Mô hình theo thứ tự ưu tiên", text: $vm.geminiModels, axis: .vertical)
                    .textInputAutocapitalization(.never).autocorrectionDisabled(true)
                    .font(.system(.footnote, design: .monospaced))
            }
            }
            Section {
                Button("Lưu và thử kết nối") {
                    vm.saveSettings(groqKey: groqKey, geminiKey: geminiKey)
                    groqKey = ""; geminiKey = ""
                }
                .disabled(vm.isBusy)
                Button("Xóa các khóa khỏi điện thoại", role: .destructive) { vm.forgetKeys() }
            }
            if !vm.status.isEmpty {
                Section(header: Text("Trạng thái")) {
                    Text(vm.status).font(.footnote)
                }
            }
        }
        .navigationTitle("Trợ lý AI")
        .navigationBarTitleDisplayMode(.inline)
    }
}

/// Nút ✨ nổi ở góc phải dưới, có ở mọi tab. Chạm để mở cửa sổ chat.
struct AIFloatingButton: View {
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: "sparkles")
                .font(.system(size: 24, weight: .semibold))
                .foregroundColor(.white)
                .frame(width: 56, height: 56)
                .background(
                    Circle().fill(LinearGradient(colors: [Color(red: 0.39, green: 0.40, blue: 0.95),
                                                          Color(red: 0.55, green: 0.36, blue: 0.96),
                                                          Color(red: 0.93, green: 0.28, blue: 0.60)],
                                                 startPoint: .topLeading, endPoint: .bottomTrailing))
                )
                .shadow(color: Color.black.opacity(0.28), radius: 8, x: 0, y: 4)
        }
        .accessibilityLabel("Hỏi AI về đơn hàng")
    }
}
