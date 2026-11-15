import SwiftUI
import ScannerCore

struct ScanView: View {
    @EnvironmentObject var engine: SyncEngine
    @StateObject private var camera = CameraController()
    @State private var manual = ""
    @State private var zoom: CGFloat = 1

    var body: some View {
        ZStack(alignment: .bottom) {
            CameraPreview(session: camera.session).ignoresSafeArea()

            if camera.denied {
                VStack(spacing: 8) {
                    Image(systemName: "camera.fill").font(.largeTitle)
                    Text("Chưa cho phép camera").font(.headline)
                    Text("Vào Cài đặt iPhone > Quét Hàng Hoàn > bật Camera.").font(.footnote).multilineTextAlignment(.center)
                }
                .padding().background(.ultraThinMaterial).cornerRadius(12).frame(maxHeight: .infinity)
            }

            VStack(spacing: 8) {
                statusBar
                Spacer()
                if let b = engine.banner { bannerView(b) }
                controls
            }
            .padding(.horizontal, 12).padding(.bottom, 8)
        }
        .onAppear {
            camera.onCode = { engine.handleCameraCode($0) }
            engine.snapshotProvider = { camera.snapshotJPEG() }
            camera.start()
            UIApplication.shared.isIdleTimerDisabled = true
        }
        .onDisappear {
            camera.stop()
            UIApplication.shared.isIdleTimerDisabled = false
        }
    }

    private var statusBar: some View {
        VStack(spacing: 6) {
            Picker("Chế độ", selection: $engine.mode) {
                Text("Quét hoàn kho").tag(ScanKind.returnScan)
                Text("Tạo đơn mới").tag(ScanKind.newOrder)
                Text("Tạo đơn hủy").tag(ScanKind.cancelOrder)
            }
            .pickerStyle(.segmented)
            HStack {
                pill(engine.sseConnected ? "Đã nối kênh" : "Mất kết nối", engine.sseConnected ? .green : .red)
                pill(engine.pcOnline ? "PC đang online" : "Chờ PC phản hồi", engine.pcOnline ? .green : .orange)
                if engine.pendingCount > 0 { pill("Chờ \(engine.pendingCount)", .orange) }
                Spacer()
            }
        }
        .padding(8).background(.ultraThinMaterial).cornerRadius(12)
    }

    private func pill(_ t: String, _ c: Color) -> some View {
        Text(t).font(.caption.bold()).padding(.horizontal, 8).padding(.vertical, 3)
            .background(c.opacity(0.85)).foregroundColor(.white).clipShape(Capsule())
    }

    private func bannerView(_ b: Banner) -> some View {
        VStack(spacing: 2) {
            Text(b.text).font(.headline.bold())
            Text(b.code).font(.system(.title3, design: .monospaced).bold())
        }
        .frame(maxWidth: .infinity).padding(10)
        .background(b.color.opacity(0.9)).foregroundColor(.white).cornerRadius(12)
    }

    private var controls: some View {
        VStack(spacing: 8) {
            HStack(spacing: 10) {
                Button { camera.running ? camera.stop() : camera.start() } label: {
                    Label(camera.running ? "Dừng camera" : "Bật camera", systemImage: camera.running ? "pause.fill" : "play.fill")
                }
                Button { camera.setTorch(!camera.torchOn) } label: {
                    Label("Đèn", systemImage: camera.torchOn ? "flashlight.on.fill" : "flashlight.off.fill")
                }
                Button { zoom = zoom >= 2 ? 1 : 2; camera.setZoom(zoom) } label: {
                    Label(zoom >= 2 ? "2x" : "1x", systemImage: "plus.magnifyingglass")
                }
            }
            .buttonStyle(.borderedProminent).tint(.indigo).font(.footnote.bold())
            HStack {
                TextField("Nhập mã tay", text: $manual)
                    .textInputAutocapitalization(.characters).autocorrectionDisabled()
                    .textFieldStyle(.roundedBorder)
                    .submitLabel(.send)
                    .onSubmit(sendManual)
                Button("Gửi", action: sendManual).buttonStyle(.borderedProminent)
            }
            if engine.mode != .returnScan {
                TextField("Ghi chú đơn (không bắt buộc)", text: $engine.note).textFieldStyle(.roundedBorder).font(.footnote)
            }
        }
        .padding(8).background(.ultraThinMaterial).cornerRadius(12)
    }

    private func sendManual() {
        let v = manual.trimmingCharacters(in: .whitespaces)
        guard !v.isEmpty else { return }
        engine.submitManual(v)
        manual = ""
    }
}
