import Foundation
import WebKit

/// Điện thoại tự vào Salework xuất file "Đang chờ hoàn" (26/11/2026: nạp không cần máy tính bật).
/// Cùng các bước với máy tính (RealSaleworkController + SaleworkPageScript): mở trang Đơn hoàn -> tab "Đang chờ hoàn" -> rê "Xuất excel"
/// -> "Theo bộ lọc" -> "Bắt Đầu Xuất" -> nhận file. Đăng nhập Salework một lần trên điện thoại; phiên lưu trong trình duyệt của app
/// (không gửi mật khẩu lên cloud). KHÔNG bao giờ bấm nút có chữ "cập nhật" (bắt Salework đồng bộ lại TikTok).
@MainActor
final class SaleworkRunner: NSObject, ObservableObject {
    static let defaultURL = "https://salework.net/login/lavistudio"
    private static let urlKey = "saleworkPageUrl"

    @Published private(set) var lines: [String] = []
    @Published private(set) var busy = false

    let webView: WKWebView
    private var downloadCont: CheckedContinuation<(String, Data), Error>?
    private var downloadName = "salework.xlsx"
    private var downloadFile: URL?
    /// File về TRƯỚC khi kịp chờ (trang tải rất nhanh): giữ lại để lần chờ lấy luôn.
    private var pendingDownload: Result<(String, Data), Error>?
    private var expectingDownload = false
    private var downloadToken = UUID()

    var savedURL: String { UserDefaults.standard.string(forKey: Self.urlKey) ?? Self.defaultURL }

    override init() {
        let cfg = WKWebViewConfiguration()
        cfg.websiteDataStore = .default()                       // giữ phiên đăng nhập Salework trên máy
        cfg.defaultWebpagePreferences.preferredContentMode = .desktop
        let ucc = WKUserContentController()
        ucc.addUserScript(WKUserScript(source: Self.blobHook, injectionTime: .atDocumentStart, forMainFrameOnly: false))
        cfg.userContentController = ucc
        webView = WKWebView(frame: CGRect(x: 0, y: 0, width: 1280, height: 900), configuration: cfg)
        super.init()
        ucc.add(WeakMessageHandler(self), name: "qlhhDownload")
        webView.navigationDelegate = self
        webView.uiDelegate = self
        webView.customUserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15"
    }

    func say(_ s: String) { lines.append(s); if lines.count > 60 { lines.removeFirst(lines.count - 60) } }
    func clearLog() { lines.removeAll() }

    func open(_ raw: String? = nil) {
        guard let u = URL(string: raw ?? savedURL), u.scheme == "https" else { return }
        webView.load(URLRequest(url: u))
    }

    /// Lưu trang đang mở làm trang Đơn hoàn (không lưu trang đăng nhập).
    func saveCurrentPage() -> String? {
        guard let u = webView.url?.absoluteString, u.hasPrefix("https://") else { return "Chưa mở trang nào." }
        if u.range(of: #"login|signin|dang-?nhap"#, options: [.regularExpression, .caseInsensitive]) != nil {
            return "Đây là trang đăng nhập. Hãy đăng nhập rồi mở trang Đơn hoàn trước khi lưu."
        }
        UserDefaults.standard.set(u, forKey: Self.urlKey)
        return nil
    }

    // MARK: - xuất file

    struct Stop: LocalizedError {
        let message: String
        var needsLogin = false
        var errorDescription: String? { message }
    }

    /// Chạy hết các bước, trả (tên file, nội dung). Lỗi: `Stop` với câu dễ hiểu (+ `needsLogin` khi phải đăng nhập lại).
    func exportFile() async throws -> (String, Data) {
        busy = true
        defer { busy = false; setArmed(false) }
        say("Mở trang Salework…")
        open()
        var st = try await waitState(timeout: 45, label: "nút \"Xuất excel\"") { $0.bool("hasExport") || $0.bool("hasPassword") }
        if st == nil {
            say("Chưa thấy nút \"Xuất excel\": vào Đơn hoàn bằng menu…")
            if (try? await act("Đơn hoàn", wait: 1.5)) == nil {
                try await act("Kiểm tra, đối soát")
                try await pause(0.8)
                try await act("Đơn hoàn")
            }
            st = try await waitState(timeout: 30, label: "nút \"Xuất excel\"") { $0.bool("hasExport") || $0.bool("hasPassword") }
        }
        guard let s0 = st else { throw Stop(message: "Trang Salework không hiện nút \"Xuất excel\". Mở trang Đơn hoàn rồi bấm \"Lưu trang này\".") }
        if s0.bool("hasPassword") && !s0.bool("hasExport") {
            throw Stop(message: "Salework yêu cầu đăng nhập. Đăng nhập trong khung trên, mở trang Đơn hoàn, bấm \"Lưu trang này\" rồi thử lại.", needsLogin: true)
        }
        say("Chọn tab \"Đang chờ hoàn\"…")
        try await act("Đang chờ hoàn", mode: "prefix")
        try await pause(0.8)
        let s2 = try await page("state")
        if !s2.bool("channelTiktok") {
            throw Stop(message: "Không thấy kênh Tiktok trên trang. Chọn kênh Tiktok trong khung trên, bấm \"Lưu trang này\" rồi thử lại.")
        }
        say("Mở menu \"Xuất excel\"…")
        try await act("Xuất excel", hover: true)
        try await pause(0.8)
        var seen = try await page("exists", ["text": "Theo bộ lọc", "mode": "prefix", "inDialog": true]).bool("ok")
        var k = 0
        while !seen && k < 3 {
            try await pause(1.2 + Double(k) * 0.8)
            seen = try await page("exists", ["text": "Theo bộ lọc", "mode": "prefix", "inDialog": true]).bool("ok")
            if seen { break }
            try await act("Xuất excel", hover: true)
            try await pause(1.5)
            seen = try await page("exists", ["text": "Theo bộ lọc", "mode": "prefix", "inDialog": true]).bool("ok")
            k += 1
        }
        if !seen {
            try await act("Xuất excel")
            try await pause(0.8)
        }
        say("Chọn \"Theo bộ lọc\"…")
        try await act("Theo bộ lọc", mode: "prefix", inDialog: true)
        try await pause(0.6)
        say("Bấm \"Bắt Đầu Xuất\", chờ file…")
        pendingDownload = nil
        expectingDownload = true
        defer { expectingDownload = false }
        setArmed(true)
        try await act("Bắt Đầu Xuất", mode: "prefix", inDialog: true)
        let got = try await waitDownload(timeout: 120)
        say("Đã nhận file \(got.0) (\(got.1.count / 1024) KB).")
        return got
    }

    private func pause(_ s: Double) async throws { try await Task.sleep(nanoseconds: UInt64(s * 1_000_000_000)) }

    private func waitState(timeout: Double, label: String, _ ok: ([String: Any]) -> Bool) async throws -> [String: Any]? {
        let end = Date().addingTimeInterval(timeout)
        while Date() < end {
            if let s = try? await page("state"), ok(s) { return s }
            try await pause(0.6)
        }
        return nil
    }

    /// Tìm rồi bấm (hoặc rê chuột vào) phần tử có chữ `text`.
    private func act(_ text: String, mode: String = "exact", inDialog: Bool = false, hover: Bool = false, wait: Double = 8) async throws {
        let arg: [String: Any] = ["text": text, "mode": mode, "inDialog": inDialog]
        let end = Date().addingTimeInterval(wait)
        var ex = try await page("exists", arg)
        while !ex.bool("ok") && Date() < end {
            try await pause(0.4)
            ex = try await page("exists", arg)
        }
        guard ex.bool("ok") else { throw Stop(message: (ex["error"] as? String).map { "Salework: \($0)" } ?? "Không thấy \"\(text)\" trên trang Salework.") }
        if hover {
            let h = try await page("hover", arg)
            guard h.bool("ok") else { throw Stop(message: "Không mở được menu \"\(text)\".") }
            return
        }
        var r = try await page("click", arg)
        let end2 = Date().addingTimeInterval(3)
        while !r.bool("ok") && Date() < end2 {
            try await pause(0.4)
            r = try await page("click", arg)
        }
        guard r.bool("ok") else { throw Stop(message: (r["error"] as? String).map { "Salework: \($0)" } ?? "Không bấm được \"\(text)\".") }
    }

    private func page(_ action: String, _ arg: [String: Any]? = nil) async throws -> [String: Any] {
        func js(_ v: Any) -> String {
            guard let d = try? JSONSerialization.data(withJSONObject: v, options: [.fragmentsAllowed]), let s = String(data: d, encoding: .utf8) else { return "null" }
            return s
        }
        let script = Self.pageScript + "(" + js(action) + "," + (arg.map { js($0) } ?? "null") + ")"
        let out = try await webView.evaluateJavaScript(script)
        return (out as? [String: Any]) ?? [:]
    }

    private func setArmed(_ on: Bool) {
        webView.evaluateJavaScript("window.__qlhhArmed = \(on ? "true" : "false");", completionHandler: nil)
    }

    private func waitDownload(timeout: Double) async throws -> (String, Data) {
        if let p = pendingDownload { pendingDownload = nil; return try p.get() }
        let token = UUID()
        downloadToken = token
        return try await withCheckedThrowingContinuation { (c: CheckedContinuation<(String, Data), Error>) in
            downloadCont?.resume(throwing: Stop(message: "Đã hủy lần tải trước."))
            downloadCont = c
            Task { @MainActor [weak self] in
                try? await Task.sleep(nanoseconds: UInt64(timeout * 1_000_000_000))
                guard let self, self.downloadToken == token else { return }   // chỉ hết giờ cho ĐÚNG lần chờ này
                self.finishDownload(.failure(Stop(message: "Không thấy file tải về sau \(Int(timeout)) giây.")))
            }
        }
    }

    fileprivate func finishDownload(_ r: Result<(String, Data), Error>) {
        guard let c = downloadCont else {
            // file về trước khi kịp chờ: giữ lại
            if expectingDownload, pendingDownload == nil, case .success = r { pendingDownload = r }
            return
        }
        downloadToken = UUID()
        downloadCont = nil
        switch r {
        case .success(let v): c.resume(returning: v)
        case .failure(let e): c.resume(throwing: e)
        }
    }

    // MARK: - mã chạy trong trang (giống hệt SaleworkPageScript của máy tính)

    /// Bắt file tải dạng blob (trang tạo blob rồi bấm thẻ <a download>): đọc blob gửi về app. Chỉ khi app đang chờ file (__qlhhArmed).
    private static let blobHook = """
    (function () {
      if (window.__qlhhHook) return; window.__qlhhHook = true;
      var blobs = new Map();
      var oc = URL.createObjectURL;
      URL.createObjectURL = function (o) { var u = oc.apply(this, arguments); try { if (o instanceof Blob) blobs.set(u, o); } catch (e) {} return u; };
      function send(name, blob) {
        var r = new FileReader();
        r.onload = function () { try { window.webkit.messageHandlers.qlhhDownload.postMessage({ name: name || 'salework.xlsx', data: String(r.result).split(',')[1] || '' }); } catch (e) {} };
        r.readAsDataURL(blob);
      }
      function handle(a) { if (!a || !a.href || a.href.indexOf('blob:') !== 0 || !blobs.has(a.href)) return false; send(a.getAttribute('download'), blobs.get(a.href)); return true; }
      var ac = HTMLAnchorElement.prototype.click;
      HTMLAnchorElement.prototype.click = function () { if (window.__qlhhArmed && handle(this)) return; return ac.apply(this, arguments); };
      document.addEventListener('click', function (e) {
        var a = e.target && e.target.closest ? e.target.closest('a[href^="blob:"]') : null;
        if (window.__qlhhArmed && a && handle(a)) e.preventDefault();
      }, true);
    })();
    """

    private static let pageScript = """
    (function (action, arg) {
      const norm = s => String(s || '').normalize('NFC').replace(/\\s+/g, ' ').trim().toLowerCase();
      const visible = el => !!(el.offsetWidth || el.offsetHeight || (el.getClientRects && el.getClientRects().length));
      const FORBID = /c[aậ]p\\s*nh[aậ]t/i;
      const SEL = 'button,a,[role=tab],[role=button],[role=radio],li,span,label,div,p';
      function find(text, mode, within) {
        const t = norm(text);
        const root = within || document;
        const all = Array.from(root.querySelectorAll(SEL)).filter(el => visible(el));
        const variants = x => [x, x.replace(/^[a-z_]{3,}\\s+(?=\\S)/, ''), x.replace(/(?<=\\S)\\s+[a-z_]{3,}$/, '')];
        const hit = all.filter(el => variants(norm(el.textContent)).some(v => mode === 'prefix' ? v.startsWith(t) : v === t));
        const leaf = hit.filter(el => !hit.some(o => o !== el && el.contains(o)));
        leaf.sort((a, b) => norm(a.textContent).length - norm(b.textContent).length);
        return leaf;
      }
      function dialogRoot() {
        const d = Array.from(document.querySelectorAll('[role=dialog],.ant-modal,.modal,.el-dialog,.v-dialog,.MuiDialog-root,.q-dialog')).filter(visible);
        return d.length ? d[d.length - 1] : null;
      }
      function click(el) {
        let target = el;
        const inp = el.querySelector && el.querySelector('input[type=radio]');
        if (inp) target = inp;
        target.scrollIntoView && target.scrollIntoView({ block: 'center' });
        ['mousedown', 'mouseup'].forEach(n => target.dispatchEvent(new MouseEvent(n, { bubbles: true, cancelable: true, view: window })));
        target.click();
      }
      if (action === 'state') {
        const body = norm(document.body ? document.body.innerText : '');
        return {
          url: location.href,
          hasPassword: !!document.querySelector('input[type=password]'),
          hasExport: find('xuất excel', 'exact').length > 0,
          hasTab: find('đang chờ hoàn', 'prefix').length > 0,
          channelTiktok: /k[eê]nh\\s*tiktok/.test(body) || /tiktok/.test(body.slice(0, 4000)),
          title: document.title
        };
      }
      function locate(a) {
        if (FORBID.test(a.text)) return { error: 'từ chối bấm nút có chữ "cập nhật"' };
        let within = a.inDialog ? dialogRoot() : null;
        let c = find(a.text, a.mode || 'exact', within);
        if (!c.length && within) c = find(a.text, a.mode || 'exact', null);
        if (!c.length) return { error: 'không thấy "' + a.text + '"' };
        if (FORBID.test(c[0].textContent)) return { error: 'phần tử khớp có chữ "cập nhật", bỏ qua' };
        return { el: c[0], count: c.length };
      }
      if (action === 'click') { const L = locate(arg); if (L.error) return { ok: false, error: L.error }; click(L.el); return { ok: true, count: L.count }; }
      if (action === 'hover') {
        const L = locate(arg);
        if (L.error) return { ok: false, error: L.error };
        ['mouseover', 'mouseenter', 'mousemove'].forEach(n => L.el.dispatchEvent(new MouseEvent(n, { bubbles: n !== 'mouseenter', cancelable: true, view: window })));
        return { ok: true };
      }
      if (action === 'exists') { const L = locate(arg); return L.error ? { ok: false, error: L.error } : { ok: true }; }
      return { ok: false, error: 'action lạ' };
    })
    """
}

private extension Dictionary where Key == String, Value == Any {
    func bool(_ k: String) -> Bool { (self[k] as? Bool) ?? ((self[k] as? NSNumber)?.boolValue ?? false) }
}

// MARK: - tải file

extension SaleworkRunner: WKNavigationDelegate, WKUIDelegate, WKDownloadDelegate {
    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        decisionHandler(navigationAction.shouldPerformDownload ? .download : .allow)
    }

    func webView(_ webView: WKWebView, decidePolicyFor navigationResponse: WKNavigationResponse, decisionHandler: @escaping (WKNavigationResponsePolicy) -> Void) {
        let disp = (navigationResponse.response as? HTTPURLResponse)?.value(forHTTPHeaderField: "Content-Disposition") ?? ""
        decisionHandler(!navigationResponse.canShowMIMEType || disp.lowercased().contains("attachment") ? .download : .allow)
    }

    func webView(_ webView: WKWebView, navigationAction: WKNavigationAction, didBecome download: WKDownload) { download.delegate = self }
    func webView(_ webView: WKWebView, navigationResponse: WKNavigationResponse, didBecome download: WKDownload) { download.delegate = self }

    /// Trang mở cửa sổ mới (target=_blank) để tải: mở ngay trong khung này.
    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration, for navigationAction: WKNavigationAction,
                 windowFeatures: WKWindowFeatures) -> WKWebView? {
        if navigationAction.targetFrame == nil { webView.load(navigationAction.request) }
        return nil
    }

    func download(_ download: WKDownload, decideDestinationUsing response: URLResponse, suggestedFilename: String,
                  completionHandler: @escaping (URL?) -> Void) {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("salework-dl", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let name = suggestedFilename.isEmpty ? "salework.xlsx" : suggestedFilename
        let url = dir.appendingPathComponent(UUID().uuidString + "-" + name)
        downloadName = name
        downloadFile = url
        completionHandler(url)
    }

    func downloadDidFinish(_ download: WKDownload) {
        guard let url = downloadFile else { return }
        defer { try? FileManager.default.removeItem(at: url) }
        if let d = try? Data(contentsOf: url), !d.isEmpty { finishDownload(.success((downloadName, d))) }
        else { finishDownload(.failure(Stop(message: "File tải về rỗng."))) }
    }

    func download(_ download: WKDownload, didFailWithError error: Error, resumeData: Data?) {
        finishDownload(.failure(Stop(message: "Tải file lỗi: " + error.localizedDescription)))
    }
}

extension SaleworkRunner: WKScriptMessageHandler {
    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard message.name == "qlhhDownload", let o = message.body as? [String: Any],
              let b64 = o["data"] as? String, let d = Data(base64Encoded: b64), !d.isEmpty else { return }
        finishDownload(.success(((o["name"] as? String) ?? "salework.xlsx", d)))
    }
}

/// Tránh giữ vòng (WKUserContentController giữ chặt bộ nhận tin).
private final class WeakMessageHandler: NSObject, WKScriptMessageHandler {
    weak var target: WKScriptMessageHandler?
    init(_ t: WKScriptMessageHandler) { target = t }
    func userContentController(_ c: WKUserContentController, didReceive m: WKScriptMessage) { target?.userContentController(c, didReceive: m) }
}
