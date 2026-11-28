import SwiftUI
import WebKit
import ScannerCore

/// Nạp đơn từ Salework NGAY TRÊN ĐIỆN THOẠI (26/11/2026): không cần máy tính bật.
/// Điện thoại tự vào Salework xuất file "Đang chờ hoàn" -> đọc file + bỏ cột cá nhân + chuyển thành kiện (cùng bộ chuyển với máy tính)
/// -> gửi lên cloud (cloud gộp, giữ trạng thái kho, thay dòng chờ cũ). Lần đầu đăng nhập Salework trong khung bên dưới.
struct SaleworkImportView: View {
    @EnvironmentObject var engine: SyncEngine
    @StateObject private var runner = SaleworkRunner()
    @State private var running = false
    @State private var note: String?

    var body: some View {
        VStack(spacing: 0) {
            SaleworkWebView(webView: runner.webView)
                .frame(maxWidth: .infinity)
                .frame(height: 300)
                .overlay(alignment: .topTrailing) {
                    if running {
                        Text("App đang thao tác")
                            .font(.caption2.bold()).foregroundColor(.white)
                            .padding(.horizontal, 8).padding(.vertical, 4)
                            .background(Color.red.opacity(0.85)).clipShape(Capsule()).padding(6)
                    }
                }
            Divider()
            List {
                Section {
                    Button {
                        Task { await runImport() }
                    } label: {
                        HStack {
                            if running { ProgressView().padding(.trailing, 4) }
                            Text(running ? "Đang nạp…" : "Bắt đầu nạp từ Salework").bold()
                        }
                    }
                    .disabled(running || !engine.cloud.canWrite)
                    HStack {
                        Button("Mở trang đã lưu") { runner.open() }.disabled(running)
                        Spacer()
                        Button("Lưu trang này") {
                            note = runner.saveCurrentPage() ?? "Đã lưu trang này làm trang Đơn hoàn."
                        }.disabled(running)
                    }
                    .buttonStyle(.borderless)
                    if let note { Text(note).font(.footnote).foregroundColor(.secondary) }
                } footer: {
                    Text(engine.cloud.canWrite
                         ? "Lần đầu: đăng nhập Salework trong khung trên, mở trang Đơn hoàn (kênh Tiktok) rồi bấm \"Lưu trang này\". Mật khẩu chỉ nằm trên điện thoại, không gửi lên cloud."
                         : "Chưa kết nối cloud: có mạng lại mới nạp được.")
                }
                if !runner.lines.isEmpty {
                    Section("Tiến trình") {
                        ForEach(Array(runner.lines.enumerated()), id: \.offset) { _, l in
                            Text(l).font(.footnote)
                        }
                    }
                }
            }
            .listStyle(.insetGrouped)
        }
        .navigationTitle("Nạp từ Salework")
        .navigationBarTitleDisplayMode(.inline)
        .onAppear { if runner.webView.url == nil { runner.open() } }
    }

    private func runImport() async {
        guard engine.cloud.canWrite, let api = engine.cloud.currentAPI else {
            runner.say("Chưa kết nối cloud: chưa nạp được.")
            return
        }
        running = true
        defer { running = false }
        runner.clearLog()
        UIApplication.shared.isIdleTimerDisabled = true
        defer { UIApplication.shared.isIdleTimerDisabled = false }
        do {
            let (name, data) = try await runner.exportFile()
            runner.say("Đọc file…")
            let rows = try SaleworkImport.readRows(fileName: name, data: data)
            let prep = try SaleworkImport.prepare(rows: rows, today: Date())
            // ảnh 31: kiểm kênh bằng mã đơn trong file trước khi nạp (lỡ trang đang ở kênh Shopee thì KHÔNG nạp nhầm)
            if SaleworkImport.looksLikeTikTok(orderIds: prep.orderIds) == false {
                let c = SaleworkImport.channelCounts(orderIds: prep.orderIds)
                runner.say("⚠ File vừa xuất là đơn Shopee (\(c.shopee) mã Shopee, \(c.tiktok) mã TikTok) chứ không phải kênh Tiktok: CHƯA nạp. Chọn kênh Tiktok trong khung trên rồi thử lại.")
                return
            }
            let parcels = prep.incomingJSON
            let skipped = prep.groups.reduce(0) { $0 + $1.skipped }
            runner.say("\(rows.count) dòng → \(parcels.count) kiện\(skipped > 0 ? " (bỏ qua \(skipped) dòng không phải đơn hoàn)" : ""). Gửi lên cloud…")
            let r = runner
            let t = try await CloudImportUpload.salework(api: api, parcels: parcels, orderIds: prep.orderIds) { done, total in
                Task { @MainActor in r.say("Đã gửi \(done)/\(total) kiện…") }
            }
            var msg = "XONG: mới \(t.added), cập nhật \(t.updated)"
            if t.superseded > 0 { msg += ", thay \(t.superseded) dòng chờ cũ" }
            if t.ambiguous > 0 { msg += ", \(t.ambiguous) kiện cần kiểm tra (không tự gộp)" }
            runner.say(msg + ". Các máy khác thấy ngay.")
            Task { await engine.cloud.syncNow() }
        } catch let e as SaleworkRunner.Stop {
            runner.say("⚠ " + e.message)
        } catch let e as CloudImportUpload.Failure {
            runner.say("⚠ Gửi được \(e.sentParcels) kiện thì lỗi: \(e.message). Bấm nạp lại (an toàn: cloud cập nhật, không nhân đôi).")
        } catch {
            runner.say("⚠ " + ((error as? LocalizedError)?.errorDescription ?? error.localizedDescription))
        }
    }
}

private struct SaleworkWebView: UIViewRepresentable {
    let webView: WKWebView
    func makeUIView(context: Context) -> WKWebView { webView }
    func updateUIView(_ uiView: WKWebView, context: Context) {}
}
