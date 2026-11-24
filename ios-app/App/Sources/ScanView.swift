import SwiftUI
import UIKit
import ScannerCore

struct ScanView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var router: NotificationRouter
    @Environment(\.scenePhase) private var scenePhase
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @StateObject private var camera = CameraController()
    @State private var manual = ""
    @State private var zoom: CGFloat = 1
    @State private var detail: ScanItem?
    @State private var showBannerCard = false
    @State private var isVisible = false

    private var connectionStatus: (text: String, color: Color) {
        if engine.isDemoMode {
            return ("Dữ liệu mẫu", Theme.accent)
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

    @MainActor
    private func syncCameraLifecycle() {
        let shouldRun = isVisible && scenePhase == .active && router.tab == NotificationRouter.scanTab
        if shouldRun {
            let eng = engine
            let cam = camera
            camera.onCodes = { [weak eng] codes in
                Task { @MainActor in eng?.handleCameraCodes(codes) }
            }
            engine.snapshotProvider = { [weak cam] in cam?.snapshotJPEG() }
            if !camera.running && camera.restReason == nil {
                camera.start()
            }
            UIApplication.shared.isIdleTimerDisabled = true
        } else {
            camera.stop()
            UIApplication.shared.isIdleTimerDisabled = false
        }
    }

    var body: some View {
        NavigationStack {
            GeometryReader { geo in
                let camHeight = max(180, min(280, geo.size.height * 0.36))
                ScrollView {
                    VStack(spacing: Theme.cardSpacing) {
                        topHeader
                        cameraSection(height: camHeight)
                        controlsRow
                        manualInputCard
                        recentSection
                    }
                    .padding(.horizontal, Theme.screenPadding)
                    .padding(.top, 6)
                    .padding(.bottom, 16)
                }
                .scrollDismissesKeyboard(.interactively)
                .background(Theme.pageBackground.ignoresSafeArea())
            }
            .navigationTitle("Quét")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .navigationBarTrailing) {
                    HStack(spacing: 5) {
                        Circle()
                            .fill(connectionStatus.color)
                            .frame(width: 7, height: 7)
                        Text(connectionStatus.text)
                            .font(Theme.Typography.microBold)
                            .foregroundColor(connectionStatus.color)
                    }
                    .padding(.horizontal, 8)
                    .padding(.vertical, 3)
                    .background(connectionStatus.color.opacity(0.14))
                    .clipShape(Capsule())
                }
            }
        }
        .sheet(item: $detail) { ScanDetailView(requestId: $0.requestId) }
        .onAppear {
            isVisible = true
            syncCameraLifecycle()
        }
        .onDisappear {
            isVisible = false
            camera.stop()
            UIApplication.shared.isIdleTimerDisabled = false
        }
        .onChange(of: scenePhase) { _ in syncCameraLifecycle() }
        .onChange(of: router.tab) { _ in syncCameraLifecycle() }
        .task(id: engine.banner) {
            guard engine.banner != nil else {
                showBannerCard = false
                return
            }
            if reduceMotion {
                showBannerCard = true
            } else {
                withAnimation(.spring(response: 0.30, dampingFraction: 0.85)) {
                    showBannerCard = true
                }
            }
            try? await Task.sleep(nanoseconds: 4_500_000_000)
            guard !Task.isCancelled else { return }
            if reduceMotion {
                showBannerCard = false
            } else {
                withAnimation(.easeOut(duration: 0.22)) {
                    showBannerCard = false
                }
            }
        }
    }

    // MARK: - Thanh chế độ quét + số đã quét hôm nay
    private var topHeader: some View {
        VStack(spacing: 6) {
            ModePicker(mode: $engine.mode)

            HStack(spacing: 8) {
                if engine.pendingCount > 0 && !engine.isDemoMode {
                    StatusPill(text: "Chờ gửi \(engine.pendingCount)", color: Theme.warning)
                }
                Spacer(minLength: 4)
                Text("Hôm nay: \(todayScannedCount) lượt")
                    .font(Theme.Typography.captionBold)
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
            }
            .padding(.horizontal, 2)
        }
    }

    // MARK: - Khung camera gọn (~36% màn để thấy Quét gần đây) + thẻ kết quả trượt từ dưới lên
    private func cameraSection(height: CGFloat) -> some View {
        ZStack(alignment: .bottom) {
            ZStack {
                CameraPreview(controller: camera)

                if camera.denied {
                    VStack(spacing: 8) {
                        Image(systemName: "camera.fill")
                            .font(.title2)
                            .foregroundColor(Theme.accent)
                        Text("Chưa cho phép camera")
                            .font(Theme.Typography.sectionTitle)
                        Text("Vào Cài đặt iPhone > Quét Hàng Hoàn > bật Camera.")
                            .font(Theme.Typography.caption)
                            .foregroundStyle(.secondary)
                            .multilineTextAlignment(.center)
                        Button("Mở Cài đặt iPhone") {
                            Haptics.tap()
                            if let url = URL(string: UIApplication.openSettingsURLString) {
                                UIApplication.shared.open(url)
                            }
                        }
                        .font(Theme.Typography.captionBold)
                        .buttonStyle(.borderedProminent)
                        .tint(Theme.accent)
                    }
                    .padding(14)
                    .background(.ultraThinMaterial)
                    .clipShape(RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous))
                    .padding(12)
                } else if let why = camera.restReason, !camera.running {
                    Button {
                        Haptics.tap()
                        camera.start()
                    } label: {
                        VStack(spacing: 6) {
                            Image(systemName: "pause.circle.fill")
                                .font(.system(size: 36))
                            Text(why)
                                .font(Theme.Typography.captionBold)
                                .multilineTextAlignment(.center)
                            Text("Chạm để quét tiếp")
                                .font(Theme.Typography.microBold)
                                .padding(.horizontal, 12)
                                .padding(.vertical, 5)
                                .background(Color.white.opacity(0.22), in: Capsule())
                        }
                        .foregroundColor(.white)
                        .padding(16)
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                        .background(Color.black.opacity(0.75))
                    }
                    .buttonStyle(.plain)
                } else {
                    ScanGuide()
                        .stroke(Theme.modeColor(engine.mode).opacity(0.95), style: StrokeStyle(lineWidth: 2.8, lineCap: .round))
                        .padding(18)
                }
            }
            .frame(height: height)
            .background(Color.black)
            .clipShape(RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous))
            .overlay(
                RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous)
                    .stroke(Theme.modeColor(engine.mode).opacity(0.65), lineWidth: 1.8)
            )

            if showBannerCard, let b = engine.banner {
                resultBottomCard(b)
                    .padding(8)
                    .transition(.move(edge: .bottom).combined(with: .opacity))
            }
        }
    }

    private func resultBottomCard(_ b: Banner) -> some View {
        HStack(spacing: 10) {
            Image(systemName: bannerIcon(for: b.color))
                .font(.system(size: 24, weight: .semibold))
                .foregroundColor(b.color)
                .frame(width: 34, height: 34)
                .background(b.color.opacity(0.16))
                .clipShape(Circle())

            VStack(alignment: .leading, spacing: 2) {
                if !b.code.isEmpty {
                    Text(b.code)
                        .font(Theme.Typography.code)
                        .foregroundColor(.primary)
                        .lineLimit(1)
                }
                Text(b.text)
                    .font(Theme.Typography.secondaryBold)
                    .foregroundColor(b.color)
                    .lineLimit(2)
                if !b.sub.isEmpty {
                    Text(b.sub)
                        .font(Theme.Typography.micro)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }

            Spacer(minLength: 4)

            Button {
                showBannerCard = false
            } label: {
                Image(systemName: "xmark")
                    .font(.caption2.bold())
                    .foregroundStyle(.secondary)
                    .frame(width: 26, height: 26)
                    .background(Color.primary.opacity(0.08))
                    .clipShape(Circle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Đóng thông báo kết quả")
        }
        .padding(10)
        .background(.regularMaterial)
        .clipShape(RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous)
                .stroke(b.color.opacity(0.45), lineWidth: 1.0)
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

    // MARK: - Hàng nút điều khiển gọn (Đèn, Zoom, Tạm dừng, Dán mã)
    private var controlsRow: some View {
        HStack(spacing: 6) {
            compactControl(
                title: "Đèn",
                icon: camera.torchOn ? "flashlight.on.fill" : "flashlight.off.fill",
                active: camera.torchOn
            ) {
                Haptics.tap()
                camera.setTorch(!camera.torchOn)
            }

            compactControl(
                title: zoom >= 2 ? "2x" : "1x",
                icon: "plus.magnifyingglass",
                active: zoom >= 2
            ) {
                Haptics.tap()
                zoom = zoom >= 2 ? 1 : 2
                camera.setZoom(zoom)
            }

            compactControl(
                title: camera.running ? "Dừng" : "Quét",
                icon: camera.running ? "pause.fill" : "play.fill",
                active: !camera.running
            ) {
                Haptics.tap()
                camera.running ? camera.stop() : camera.start()
            }

            VStack(spacing: 2) {
                PasteButton(payloadType: String.self) { texts in
                    let codes = TrackingCode.extractPasteCodes(texts.joined(separator: "\n"))
                    DispatchQueue.main.async {
                        if codes.isEmpty {
                            engine.banner = Banner(code: "", text: "Không thấy mã vận đơn trong nội dung đã sao chép", color: Theme.warning)
                        } else {
                            codes.forEach { engine.submitManual($0) }
                        }
                    }
                }
                .labelStyle(.iconOnly)
                .buttonBorderShape(.capsule)
                .tint(Theme.accent)
                .frame(height: 36)

                Text("Dán mã")
                    .font(Theme.Typography.micro)
                    .foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity, minHeight: Theme.minTapTarget)
        }
    }

    private func compactControl(title: String, icon: String, active: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            VStack(spacing: 3) {
                Image(systemName: icon)
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundColor(active ? .white : .primary)
                    .frame(width: 38, height: 36)
                    .background(active ? Theme.accent : Theme.cardBackground)
                    .clipShape(RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous))
                    .overlay(
                        RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous)
                            .stroke(active ? Color.clear : Color.primary.opacity(0.08), lineWidth: 0.7)
                    )
                Text(title)
                    .font(Theme.Typography.micro)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            .frame(maxWidth: .infinity, minHeight: Theme.minTapTarget)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    // MARK: - Ô nhập tay một dòng gọn
    private var manualInputCard: some View {
        VStack(spacing: 6) {
            HStack(spacing: 6) {
                TextField("Nhập mã vận đơn thủ công...", text: $manual)
                    .textInputAutocapitalization(.characters)
                    .autocorrectionDisabled()
                    .font(.system(.footnote, design: .monospaced))
                    .padding(.horizontal, 10)
                    .frame(minHeight: 38)
                    .background(Theme.subtleFill)
                    .clipShape(RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous))
                    .submitLabel(.send)
                    .onSubmit(sendManual)

                Button(action: sendManual) {
                    Image(systemName: "arrow.up.circle.fill")
                        .font(.system(size: 28))
                        .foregroundColor(manual.trimmingCharacters(in: .whitespaces).isEmpty ? .secondary : Theme.accent)
                        .frame(width: Theme.minTapTarget, height: Theme.minTapTarget)
                }
                .buttonStyle(.plain)
                .disabled(manual.trimmingCharacters(in: .whitespaces).isEmpty)
                .accessibilityLabel("Gửi mã thủ công")
            }

            if engine.mode != .returnScan {
                TextField("Ghi chú đơn mới (không bắt buộc)", text: $engine.note)
                    .font(Theme.Typography.caption)
                    .padding(.horizontal, 10)
                    .frame(minHeight: 34)
                    .background(Theme.subtleFill)
                    .clipShape(RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous))
            }
        }
        .card(padding: 8, radius: Theme.cardRadius)
    }

    // MARK: - Quét gần đây (gọn, không bị che)
    private var recentSection: some View {
        let list = Array(engine.items.filter { $0.kind == engine.mode }.prefix(5))
        return VStack(alignment: .leading, spacing: 6) {
            if !list.isEmpty {
                Text("Quét gần đây")
                    .font(Theme.Typography.captionBold)
                    .foregroundStyle(.secondary)
                    .padding(.horizontal, 2)

                ForEach(list) { it in
                    Button {
                        Haptics.tap()
                        detail = it
                    } label: {
                        HStack(spacing: 8) {
                            Circle()
                                .fill(scanColor(it))
                                .frame(width: 8, height: 8)
                            VStack(alignment: .leading, spacing: 1) {
                                Text(it.code)
                                    .font(Theme.Typography.code)
                                    .foregroundColor(.primary)
                                    .lineLimit(1)
                                Text(scanLabel(it))
                                    .font(Theme.Typography.microBold)
                                    .foregroundColor(scanColor(it))
                            }
                            Spacer()
                            Text(Date(timeIntervalSince1970: Double(it.time) / 1000).formatted(date: .omitted, time: .shortened))
                                .font(Theme.Typography.micro)
                                .monospacedDigit()
                                .foregroundStyle(.secondary)
                            Image(systemName: "chevron.right")
                                .font(.caption2)
                                .foregroundStyle(.tertiary)
                        }
                        .frame(minHeight: Theme.minTapTarget)
                        .card(padding: 8, radius: Theme.cardRadius)
                    }
                    .buttonStyle(.plain)
                }
            }
        }
    }

    private func sendManual() {
        let v = manual.trimmingCharacters(in: .whitespacesAndNewlines)
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
        let l: CGFloat = 24
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
