import SwiftUI
import ScannerCore

struct ScanView: View {
    @EnvironmentObject var engine: SyncEngine
    @StateObject private var camera = CameraController()
    @State private var manual = ""
    @State private var zoom: CGFloat = 1

    @State private var detail: ScanItem?

    var body: some View {
        ScrollView {
        VStack(spacing: 8) {
            statusBar
            cameraFrame
            if let b = engine.banner { bannerView(b) }
            controls
            recentList
        }
        .padding(.horizontal, 12).padding(.top, 4)
        }
        .scrollDismissesKeyboard(.interactively)
        .background(Color(.systemGroupedBackground).ignoresSafeArea())
        .sheet(item: $detail) { ScanDetailView(requestId: $0.requestId) }
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

    /// Khung camera nhỏ như bản web (chỉ nhận mã trong khung).
    private var cameraFrame: some View {
        ZStack {
            CameraPreview(controller: camera)
            if camera.denied {
                VStack(spacing: 6) {
                    Image(systemName: "camera.fill").font(.title)
                    Text("Chưa cho phép camera").font(.headline)
                    Text("Vào Cài đặt iPhone > Quét Hàng Hoàn > bật Camera.").font(.footnote).multilineTextAlignment(.center)
                }
                .padding().background(.ultraThinMaterial).cornerRadius(12)
            } else {
                ScanGuide().stroke(Color.green.opacity(0.9), style: StrokeStyle(lineWidth: 3, lineCap: .round)).padding(14)
            }
        }
        .frame(height: 345)
        .background(Color.black)
        .clipShape(RoundedRectangle(cornerRadius: 22, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 22, style: .continuous).stroke(Theme.modeColor(engine.mode), lineWidth: 3))
        .shadow(color: Theme.modeColor(engine.mode).opacity(0.35), radius: 10, x: 0, y: 3)
    }

    /// Vài lần quét gần nhất của chế độ đang chọn; chạm vào để xem chi tiết và ảnh.
    private var recentList: some View {
        let list = Array(engine.items.filter { $0.kind == engine.mode }.prefix(8))
        return VStack(spacing: 0) {
            VStack(spacing: 6) {
                if list.isEmpty {
                    Text("Chưa quét mã nào ở chế độ này").font(.footnote).foregroundStyle(.secondary).padding(.top, 12)
                }
                ForEach(list) { it in
                    HStack(spacing: 8) {
                        Circle().fill(scanColor(it)).frame(width: 10, height: 10)
                        VStack(alignment: .leading, spacing: 1) {
                            Text(it.code).font(.system(.subheadline, design: .monospaced).bold())
                            Text(scanLabel(it)).font(.caption2.bold()).foregroundColor(scanColor(it))
                        }
                        Spacer()
                        Text(Date(timeIntervalSince1970: Double(it.time) / 1000).formatted(date: .omitted, time: .standard))
                            .font(.caption2).foregroundStyle(.secondary)
                        Image(systemName: "chevron.right").font(.caption2).foregroundStyle(.tertiary)
                    }
                    .padding(10).background(Color(.secondarySystemGroupedBackground)).clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                    .contentShape(Rectangle())
                    .onTapGesture { detail = it }
                }
            }
        }
    }

    private var statusBar: some View {
        VStack(spacing: 6) {
            ModePicker(mode: $engine.mode)
            HStack {
                pill(engine.channelUp ? "Đã nối kênh" : "Mất kết nối", engine.channelUp ? .green : .red)
                pill(engine.pcOnline ? "PC đang online" : "Chờ PC phản hồi", engine.pcOnline ? .green : .orange)
                if engine.pendingCount > 0 { pill("Chờ \(engine.pendingCount)", .orange) }
                Spacer()
            }
        }
        .card(padding: 10)
    }

    private func pill(_ t: String, _ c: Color) -> some View {
        Text(t).font(.caption.bold()).padding(.horizontal, 8).padding(.vertical, 3)
            .background(c.opacity(0.85)).foregroundColor(.white).clipShape(Capsule())
    }

    private func bannerView(_ b: Banner) -> some View {
        VStack(spacing: 2) {
            Text(b.text).font(.headline.bold())
            Text(b.code).font(.system(.title3, design: .monospaced).bold())
            if !b.sub.isEmpty { Text(b.sub).font(.footnote).multilineTextAlignment(.center) }
        }
        .frame(maxWidth: .infinity).padding(14)
        .background(b.color.opacity(0.92)).foregroundColor(.white)
        .clipShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
        .shadow(color: b.color.opacity(0.35), radius: 8, x: 0, y: 3)
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
            .buttonStyle(.borderedProminent).buttonBorderShape(.capsule).tint(Theme.accent).font(.footnote.bold())
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
        .card(padding: 10)
    }

    private func sendManual() {
        let v = manual.trimmingCharacters(in: .whitespaces)
        guard !v.isEmpty else { return }
        engine.submitManual(v)
        manual = ""
    }
}

/// Bốn góc ngắm quét.
struct ScanGuide: Shape {
    func path(in r: CGRect) -> Path {
        var p = Path()
        let l: CGFloat = 26
        let corners: [(CGPoint, CGFloat, CGFloat)] = [
            (CGPoint(x: r.minX, y: r.minY), 1, 1),
            (CGPoint(x: r.maxX, y: r.minY), -1, 1),
            (CGPoint(x: r.minX, y: r.maxY), 1, -1),
            (CGPoint(x: r.maxX, y: r.maxY), -1, -1)
        ]
        for (pt, dx, dy) in corners {
            p.move(to: CGPoint(x: pt.x + dx * l, y: pt.y))
            p.addLine(to: pt)
            p.addLine(to: CGPoint(x: pt.x, y: pt.y + dy * l))
        }
        return p
    }
}
