import SwiftUI
import ScannerCore

struct ScanView: View {
    @EnvironmentObject var engine: SyncEngine
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @StateObject private var camera = CameraController()
    @State private var manual = ""
    @State private var zoom: CGFloat = 1
    @State private var detail: ScanItem?
    @State private var showBannerCard = false

    private var connectionStatus: (text: String, color: Color) {
        if engine.isDemoMode {
            return ("Chế độ dữ liệu mẫu", Theme.accent)
        }
        if engine.pcOnline {
            return ("Đã nối PC", Theme.success)
        }
        if engine.channelUp {
            return ("Đang chờ PC", Theme.warning)
        }
        return ("Mất kết nối PC", Theme.danger)
    }

    private var todayScannedCount: Int {
        engine.todayStats(kind: engine.mode).total
    }

    var body: some View {
        GeometryReader { geo in
            let camHeight = max(230, min(420, geo.size.height * 0.54))
            ScrollView {
                VStack(spacing: 12) {
                    topHeader
                    cameraSection(height: camHeight)
                    controlsRow
                    manualInputCard
                    recentSection
                }
                .padding(.horizontal, 14)
                .padding(.top, 8)
                .padding(.bottom, 18)
            }
            .scrollDismissesKeyboard(.interactively)
            .background(Theme.pageBackground.ignoresSafeArea())
        }
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
        .task(id: engine.banner) {
            guard engine.banner != nil else {
                showBannerCard = false
                return
            }
            if reduceMotion {
                showBannerCard = true
            } else {
                withAnimation(.spring(response: 0.32, dampingFraction: 0.84)) {
                    showBannerCard = true
                }
            }
            try? await Task.sleep(nanoseconds: 4_500_000_000)
            guard !Task.isCancelled else { return }
            if reduceMotion {
                showBannerCard = false
            } else {
                withAnimation(.easeOut(duration: 0.25)) {
                    showBannerCard = false
                }
            }
        }
    }

    // MARK: - Thanh trên: chế độ quét + 1 chấm trạng thái + số đã quét hôm nay
    private var topHeader: some View {
        let st = connectionStatus
        return VStack(spacing: 10) {
            ModePicker(mode: $engine.mode)

            HStack(spacing: 10) {
                HStack(spacing: 6) {
                    Circle()
                        .fill(st.color)
                        .frame(width: 9, height: 9)
                    Text(st.text)
                        .font(.footnote.bold())
                        .foregroundColor(.primary)
                }
                .accessibilityElement(children: .combine)

                if engine.pendingCount > 0 && !engine.isDemoMode {
                    StatusPill(text: "Chờ gửi \(engine.pendingCount)", color: Theme.warning)
                }

                Spacer(minLength: 4)

                Text("Đã quét hôm nay: \(todayScannedCount)")
                    .font(.caption.bold())
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
            }
            .padding(.horizontal, 4)
        }
    }

    // MARK: - Khung camera (~55% màn) + thẻ kết quả trượt từ dưới lên
    private func cameraSection(height: CGFloat) -> some View {
        ZStack(alignment: .bottom) {
            ZStack {
                CameraPreview(controller: camera)

                if camera.denied {
                    VStack(spacing: 8) {
                        Image(systemName: "camera.fill")
                            .font(.title)
                            .foregroundColor(Theme.accent)
                        Text("Chưa cho phép camera")
                            .font(.headline)
                        Text("Vào Cài đặt iPhone > Quét Hàng Hoàn > bật Camera.")
                            .font(.footnote)
                            .foregroundStyle(.secondary)
                            .multilineTextAlignment(.center)
                    }
                    .padding(18)
                    .background(.ultraThinMaterial)
                    .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
                    .padding(16)
                } else if let why = camera.restReason, !camera.running {
                    Button {
                        Haptics.tap()
                        camera.start()
                    } label: {
                        VStack(spacing: 8) {
                            Image(systemName: "pause.circle.fill")
                                .font(.system(size: 44))
                            Text(why)
                                .font(.footnote.bold())
                                .multilineTextAlignment(.center)
                            Text("Chạm để quét tiếp")
                                .font(.caption.bold())
                                .padding(.horizontal, 14)
                                .padding(.vertical, 6)
                                .background(Color.white.opacity(0.22), in: Capsule())
                        }
                        .foregroundColor(.white)
                        .padding(20)
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                        .background(Color.black.opacity(0.75))
                    }
                    .buttonStyle(.plain)
                } else {
                    ScanGuide()
                        .stroke(Theme.modeColor(engine.mode).opacity(0.95), style: StrokeStyle(lineWidth: 3.2, lineCap: .round))
                        .padding(22)
                }
            }
            .frame(height: height)
            .background(Color.black)
            .clipShape(RoundedRectangle(cornerRadius: 24, style: .continuous))
            .overlay(
                RoundedRectangle(cornerRadius: 24, style: .continuous)
                    .stroke(Theme.modeColor(engine.mode).opacity(0.65), lineWidth: 2)
            )

            // Thẻ kết quả trượt từ dưới lên
            if showBannerCard, let b = engine.banner {
                resultBottomCard(b)
                    .padding(10)
                    .transition(.move(edge: .bottom).combined(with: .opacity))
            }
        }
    }

    private func resultBottomCard(_ b: Banner) -> some View {
        HStack(spacing: 12) {
            Image(systemName: bannerIcon(for: b.color))
                .font(.system(size: 30, weight: .semibold))
                .foregroundColor(b.color)
                .frame(width: 42, height: 42)
                .background(b.color.opacity(0.16))
                .clipShape(Circle())

            VStack(alignment: .leading, spacing: 3) {
                if !b.code.isEmpty {
                    Text(b.code)
                        .font(.system(.headline, design: .monospaced).bold())
                        .foregroundColor(.primary)
                        .lineLimit(1)
                }
                Text(b.text)
                    .font(.subheadline.weight(.semibold))
                    .foregroundColor(b.color)
                    .lineLimit(2)
                if !b.sub.isEmpty {
                    Text(b.sub)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                }
            }

            Spacer(minLength: 4)

            Button {
                showBannerCard = false
            } label: {
                Image(systemName: "xmark")
                    .font(.caption.bold())
                    .foregroundStyle(.secondary)
                    .frame(width: 28, height: 28)
                    .background(Color.primary.opacity(0.08))
                    .clipShape(Circle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Đóng thông báo kết quả")
        }
        .padding(14)
        .background(.regularMaterial)
        .clipShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: 18, style: .continuous)
                .stroke(b.color.opacity(0.45), lineWidth: 1.2)
        )
    }

    private func bannerIcon(for color: Color) -> String {
        if color == Theme.success || color == .green || color == Theme.teal {
            return "checkmark.circle.fill"
        }
        if color == Theme.danger || color == .red {
            return "xmark.octagon.fill"
        }
        return "clock.badge.exclamationmark.fill"
    }

    // MARK: - Hàng nút tròn có nhãn nhỏ (Đèn, Zoom, Dừng, Dán)
    private var controlsRow: some View {
        HStack(spacing: 0) {
            roundControl(
                title: "Đèn",
                icon: camera.torchOn ? "flashlight.on.fill" : "flashlight.off.fill",
                active: camera.torchOn
            ) {
                Haptics.tap()
                camera.setTorch(!camera.torchOn)
            }

            roundControl(
                title: zoom >= 2 ? "Zoom 2x" : "Zoom 1x",
                icon: "plus.magnifyingglass",
                active: zoom >= 2
            ) {
                Haptics.tap()
                zoom = zoom >= 2 ? 1 : 2
                camera.setZoom(zoom)
            }

            roundControl(
                title: camera.running ? "Tạm dừng" : "Bật quét",
                icon: camera.running ? "pause.fill" : "play.fill",
                active: !camera.running
            ) {
                Haptics.tap()
                camera.running ? camera.stop() : camera.start()
            }

            VStack(spacing: 6) {
                PasteButton(payloadType: String.self) { texts in
                    let codes = PasteCodes.split(texts.joined(separator: "\n"))
                    DispatchQueue.main.async {
                        if codes.isEmpty {
                            engine.banner = Banner(code: "", text: "Không thấy mã vận đơn trong nội dung đã sao chép", color: Theme.warning)
                        } else {
                            codes.forEach { engine.submitManual($0) }
                        }
                    }
                }
                .labelStyle(.iconOnly)
                .buttonBorderShape(.capsule)   // .circle chỉ có từ iOS 17; khung 48x48 + capsule vẫn ra hình tròn
                .tint(Theme.accent)
                .frame(width: 48, height: 48)

                Text("Dán mã")
                    .font(.caption2.weight(.medium))
                    .foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity)
        }
        .padding(.vertical, 4)
    }

    private func roundControl(title: String, icon: String, active: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            VStack(spacing: 6) {
                Image(systemName: icon)
                    .font(.system(size: 18, weight: .semibold))
                    .foregroundColor(active ? .white : .primary)
                    .frame(width: 48, height: 48)
                    .background(active ? Theme.accent : Theme.cardBackground)
                    .clipShape(Circle())
                    .overlay(
                        Circle()
                            .stroke(active ? Color.clear : Color.primary.opacity(0.10), lineWidth: 0.8)
                    )
                Text(title)
                    .font(.caption2.weight(.medium))
                    .foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity)
        }
        .buttonStyle(.plain)
    }

    // MARK: - Nhập mã tay
    private var manualInputCard: some View {
        VStack(spacing: 8) {
            HStack(spacing: 8) {
                TextField("Nhập mã vận đơn thủ công...", text: $manual)
                    .textInputAutocapitalization(.characters)
                    .autocorrectionDisabled()
                    .font(.system(.subheadline, design: .monospaced))
                    .padding(.horizontal, 12)
                    .frame(minHeight: 44)
                    .background(Theme.subtleFill)
                    .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                    .submitLabel(.send)
                    .onSubmit(sendManual)

                Button(action: sendManual) {
                    Image(systemName: "arrow.up.circle.fill")
                        .font(.system(size: 32))
                        .foregroundColor(manual.trimmingCharacters(in: .whitespaces).isEmpty ? .secondary : Theme.accent)
                        .frame(width: 44, height: 44)
                }
                .buttonStyle(.plain)
                .disabled(manual.trimmingCharacters(in: .whitespaces).isEmpty)
                .accessibilityLabel("Gửi mã thủ công")
            }

            if engine.mode != .returnScan {
                TextField("Ghi chú đơn mới (không bắt buộc)", text: $engine.note)
                    .font(.footnote)
                    .padding(.horizontal, 12)
                    .frame(minHeight: 38)
                    .background(Theme.subtleFill)
                    .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
            }
        }
        .card(padding: 12, radius: 18)
    }

    // MARK: - Gần đây
    private var recentSection: some View {
        let list = Array(engine.items.filter { $0.kind == engine.mode }.prefix(5))
        return VStack(alignment: .leading, spacing: 8) {
            if !list.isEmpty {
                Text("Quét gần đây")
                    .font(.caption.bold())
                    .foregroundStyle(.secondary)
                    .padding(.horizontal, 4)

                ForEach(list) { it in
                    Button {
                        Haptics.tap()
                        detail = it
                    } label: {
                        HStack(spacing: 10) {
                            Circle()
                                .fill(scanColor(it))
                                .frame(width: 9, height: 9)
                            VStack(alignment: .leading, spacing: 2) {
                                Text(it.code)
                                    .font(.system(.subheadline, design: .monospaced).bold())
                                    .foregroundColor(.primary)
                                Text(scanLabel(it))
                                    .font(.caption2.bold())
                                    .foregroundColor(scanColor(it))
                            }
                            Spacer()
                            Text(Date(timeIntervalSince1970: Double(it.time) / 1000).formatted(date: .omitted, time: .shortened))
                                .font(.caption2)
                                .foregroundStyle(.secondary)
                            Image(systemName: "chevron.right")
                                .font(.caption2)
                                .foregroundStyle(.tertiary)
                        }
                        .frame(minHeight: 44)
                        .card(padding: 10, radius: 14)
                    }
                    .buttonStyle(.plain)
                }
            }
        }
    }

    private func sendManual() {
        let v = manual.trimmingCharacters(in: .whitespaces)
        guard !v.isEmpty else { return }
        Haptics.tap()
        engine.submitManual(v)
        manual = ""
    }
}

/// Bốn góc ngắm quét.
struct ScanGuide: Shape {
    func path(in r: CGRect) -> Path {
        var p = Path()
        let l: CGFloat = 28
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

/// Tách các mã từ nội dung đã chép (bỏ chữ thừa, mã ngắn hơn 6 ký tự, mã trùng).
enum PasteCodes {
    static func split(_ text: String) -> [String] {
        var seen = Set<String>(), out: [String] = []
        for tok in text.components(separatedBy: CharacterSet(charactersIn: " \t\r\n,;|")) {
            let c = TrackingCode.clean(tok)
            guard c.count >= 6, c.contains(where: { $0.isNumber }), !seen.contains(c) else { continue }
            seen.insert(c); out.append(c)
        }
        return out
    }
}
