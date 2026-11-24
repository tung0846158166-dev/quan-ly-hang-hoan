import SwiftUI
import UIKit
import ScannerCore

struct SettingsView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @State private var roomText = ""
    @State private var brokerText = UserDefaults.standard.string(forKey: "privateBroker") ?? ""
    @State private var savedToast = ""
    @AppStorage("soundOn") private var soundOn = true
    @AppStorage("hapticOn") private var hapticOn = true
    @AppStorage("sendPhoto") private var sendPhoto = true
    @AppStorage("minCodeSize") private var minCodeSize = 0.12

    private var demoBinding: Binding<Bool> {
        Binding(
            get: { engine.isDemoMode },
            set: { newValue in
                Haptics.medium()
                engine.setDemoMode(newValue)
            }
        )
    }

    private var overallConnection: (text: String, color: Color) {
        if engine.isDemoMode { return ("Đang ở chế độ mẫu", Theme.accent) }
        if engine.pcOnline { return ("Đã nối PC", Theme.success) }
        if engine.channelUp { return ("Kênh sẵn sàng, chờ PC", Theme.warning) }
        return ("Mất kết nối", Theme.danger)
    }

    var body: some View {
        NavigationStack {
            List {
                // MARK: - Dữ liệu mẫu
                Section {
                    HStack(spacing: 10) {
                        IconTile(systemName: "sparkles", color: Theme.accent, size: 28, cornerRadius: 7)
                        Toggle("Xem thử với dữ liệu mẫu", isOn: demoBinding)
                            .font(Theme.Typography.primary)
                            .tint(Theme.accent)
                    }
                } header: {
                    Text("Chế độ xem thử")
                } footer: {
                    Text("Hiển thị ~60 đơn mẫu đầy đủ các trường hợp để xem trước giao diện. Không gửi bất kỳ lệnh nào lên máy tính và giữ nguyên dữ liệu thật khi tắt.")
                }

                // MARK: - Mã phòng (thẻ riêng có Dán / Sao chép)
                Section {
                    VStack(alignment: .leading, spacing: Theme.cardSpacing) {
                        TextField("Dán mã phòng từ app PC v2", text: $roomText)
                            .textInputAutocapitalization(.characters)
                            .autocorrectionDisabled()
                            .font(.system(.subheadline, design: .monospaced))
                            .padding(10)
                            .background(Theme.subtleFill)
                            .clipShape(RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous))
                            .onSubmit { saveRoom() }

                        HStack(spacing: Theme.cardSpacing) {
                            Button {
                                Haptics.tap()
                                if let pasted = UIPasteboard.general.string {
                                    roomText = pasted.trimmingCharacters(in: .whitespacesAndNewlines)
                                }
                            } label: {
                                Label("Dán", systemImage: "doc.on.clipboard")
                                    .font(Theme.Typography.secondaryBold)
                                    .frame(maxWidth: .infinity, minHeight: Theme.minTapTarget)
                            }
                            .buttonStyle(.bordered)

                            Button {
                                Haptics.tap()
                                UIPasteboard.general.string = engine.room.isEmpty ? roomText : engine.room
                                savedToast = "Đã sao chép mã phòng"
                            } label: {
                                Label("Sao chép", systemImage: "doc.on.doc")
                                    .font(Theme.Typography.secondaryBold)
                                    .frame(maxWidth: .infinity, minHeight: Theme.minTapTarget)
                            }
                            .buttonStyle(.bordered)
                            .disabled(engine.room.isEmpty && roomText.isEmpty)
                        }

                        Button { saveRoom() } label: {
                            Text("Lưu mã phòng")
                                .font(Theme.Typography.secondaryBold)
                                .frame(maxWidth: .infinity, minHeight: Theme.minTapTarget)
                        }
                        .buttonStyle(.borderedProminent)
                        .tint(Theme.accent)
                        .disabled(!SyncProtocol.isValidRoom(roomText))

                        if !roomText.isEmpty && !SyncProtocol.isValidRoom(roomText) {
                            Text("Mã phòng cần ít nhất 12 ký tự (lấy ở mục Đồng bộ điện thoại trên app PC v2).")
                                .font(Theme.Typography.caption)
                                .foregroundColor(Theme.danger)
                        } else if !savedToast.isEmpty {
                            Text(savedToast)
                                .font(Theme.Typography.captionBold)
                                .foregroundColor(Theme.success)
                        }
                    }
                    .padding(.vertical, 2)
                } header: {
                    Text("Mã phòng đồng bộ")
                }

                // MARK: - Kết nối
                Section("Kết nối") {
                    let conn = overallConnection
                    settingsRow(icon: "desktopcomputer", color: conn.color, title: "Máy tính") {
                        HStack(spacing: 6) {
                            Circle().fill(conn.color).frame(width: 8, height: 8)
                            Text(conn.text)
                                .font(.subheadline.bold())
                                .foregroundColor(conn.color)
                        }
                    }

                    settingsRow(icon: "antenna.radiowaves.left.and.right", color: Theme.info, title: "Kênh ntfy") {
                        Text(engine.sseConnected ? "Đã nối" : "Chưa nối")
                            .font(.subheadline)
                            .foregroundColor(engine.sseConnected ? Theme.success : .secondary)
                    }

                    settingsRow(icon: "network", color: Theme.teal, title: "MQTT dự phòng") {
                        Text(engine.brokerUp.isEmpty ? "Chưa nối" : "\(engine.brokerUp.count)/\(SyncProtocol.mqttBrokers.count) máy chủ")
                            .font(.subheadline)
                            .foregroundColor(engine.brokerUp.isEmpty ? .secondary : Theme.success)
                    }

                    VStack(alignment: .leading, spacing: 6) {
                        Text("Máy chủ MQTT riêng (để trống = công cộng)")
                            .font(.caption)
                            .foregroundColor(.secondary)
                        TextField("wss://tên:mật-khẩu@may-chu/mqtt", text: $brokerText)
                            .textInputAutocapitalization(.never)
                            .autocorrectionDisabled(true)
                            .keyboardType(.URL)
                            .font(.system(.footnote, design: .monospaced))
                            .onSubmit { engine.setPrivateBroker(brokerText) }
                        Button("Lưu máy chủ và nối lại") { engine.setPrivateBroker(brokerText) }
                            .font(.footnote.bold())
                    }

                    settingsRow(icon: "tray.and.arrow.up.fill", color: Theme.warning, title: "Hàng chờ gửi PC") {
                        Text("\(engine.pendingCount)")
                            .font(.subheadline.bold())
                            .monospacedDigit()
                            .foregroundColor(engine.pendingCount > 0 ? Theme.warning : .secondary)
                    }
                }

                // MARK: - Dữ liệu
                Section("Dữ liệu") {
                    settingsRow(icon: "shippingbox.fill", color: Theme.accent, title: "Danh sách đơn") {
                        Text(store.updatedAt.map { "\(store.rows.count) đơn · \($0.formatted(date: .omitted, time: .shortened))" } ?? "Chưa nhận")
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                    }

                    NavigationLink {
                        ReconcileDetailView()
                    } label: {
                        HStack(spacing: 10) {
                            IconTile(systemName: "checkmark.shield.fill", color: Theme.success, size: 28, cornerRadius: 7)
                            Text("Đối chiếu số đơn với PC")
                                .font(Theme.Typography.primary)
                            Spacer()
                            if !store.stats.isEmpty {
                                Text("\(store.rows.count)")
                                    .font(Theme.Typography.captionBold)
                                    .foregroundStyle(.secondary)
                            }
                        }
                    }

                    Button {
                        Haptics.tap()
                        engine.requestSnapshot()
                    } label: {
                        HStack(spacing: 10) {
                            IconTile(systemName: "arrow.clockwise", color: Theme.info, size: 28, cornerRadius: 7)
                            Text("Cập nhật danh sách từ PC")
                                .font(Theme.Typography.primary)
                                .foregroundColor(engine.isDemoMode ? .secondary : Theme.accent)
                        }
                    }
                    .disabled(engine.isDemoMode)
                }

                // MARK: - Thông báo
                Section("Thông báo") {
                    settingsRow(icon: "bell.badge.fill", color: Theme.danger, title: "Nhắc đơn sắp đến hạn") {
                        Text(store.notificationsAllowed ? "Đã bật" : "Chưa bật")
                            .font(Theme.Typography.secondaryBold)
                            .foregroundColor(store.notificationsAllowed ? Theme.success : Theme.warning)
                    }

                    if !store.notificationsAllowed {
                        Button {
                            Haptics.medium()
                            store.requestNotificationPermission()
                        } label: {
                            HStack(spacing: 10) {
                                IconTile(systemName: "bell.fill", color: Theme.accent, size: 28, cornerRadius: 7)
                                Text("Bật quyền thông báo")
                                    .font(Theme.Typography.primary)
                                    .foregroundColor(Theme.accent)
                            }
                        }
                    }
                }

                // MARK: - Quét
                Section("Quét") {
                    HStack(spacing: 10) {
                        IconTile(systemName: "speaker.wave.2.fill", color: Theme.info, size: 28, cornerRadius: 7)
                        Toggle("Âm thanh khi quét", isOn: $soundOn)
                            .font(Theme.Typography.primary)
                            .tint(Theme.accent)
                    }
                    HStack(spacing: 10) {
                        IconTile(systemName: "iphone.radiowaves.left.and.right", color: Theme.purple, size: 28, cornerRadius: 7)
                        Toggle("Rung phản hồi", isOn: $hapticOn)
                            .font(Theme.Typography.primary)
                            .tint(Theme.accent)
                    }
                    HStack(spacing: 10) {
                        IconTile(systemName: "camera.fill", color: Theme.teal, size: 28, cornerRadius: 7)
                        Toggle("Gửi ảnh khi quét hoàn kho", isOn: $sendPhoto)
                            .font(Theme.Typography.primary)
                            .tint(Theme.accent)
                    }
                    VStack(alignment: .leading, spacing: 4) {
                        HStack(spacing: 10) {
                            IconTile(systemName: "viewfinder", color: Theme.accent, size: 28, cornerRadius: 7)
                            Text(minCodeSize <= 0 ? "Khoảng cách quét: không giới hạn" : "Khoảng cách quét (\(String(format: "%.2f", minCodeSize)))")
                                .font(Theme.Typography.primary)
                        }
                        Slider(value: $minCodeSize, in: 0...0.3, step: 0.01)
                            .tint(Theme.accent)
                        Text("Kéo sang phải để chỉ nhận mã ở gần hơn. Mặc định 0,12 (~30 cm).")
                            .font(Theme.Typography.caption)
                            .foregroundStyle(.secondary)
                    }
                    .padding(.vertical, 2)
                }

                // MARK: - Giới thiệu
                Section("Giới thiệu") {
                    settingsRow(icon: "info.circle.fill", color: .secondary, title: "Phiên bản") {
                        Text((Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "2.0.0") + " (SwiftUI v2)")
                            .font(Theme.Typography.secondary)
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .listStyle(.insetGrouped)
            .scrollContentBackground(.hidden)
            .background(Theme.pageBackground.ignoresSafeArea())
            .navigationTitle("Cài đặt")
            .navigationBarTitleDisplayMode(.inline)
            .onAppear { roomText = engine.room }
        }
    }

    private func settingsRow<Trailing: View>(
        icon: String,
        color: Color,
        title: String,
        @ViewBuilder trailing: () -> Trailing
    ) -> some View {
        HStack(spacing: 10) {
            IconTile(systemName: icon, color: color, size: 28, cornerRadius: 7)
            Text(title)
                .font(Theme.Typography.primary)
                .foregroundColor(.primary)
            Spacer(minLength: 8)
            trailing()
        }
        .frame(minHeight: 32)
    }

    private func saveRoom() {
        guard SyncProtocol.isValidRoom(roomText) else { return }
        Haptics.medium()
        engine.setRoom(roomText)
        savedToast = "Đã lưu mã phòng và đang kết nối..."
    }
}

/// Màn chi tiết đối chiếu số đơn PC vs điện thoại (tính một lượt trên luồng nền, tránh duyệt 12 lần trong `body`).
struct ReconcileDetailView: View {
    @EnvironmentObject var store: SnapshotStore
    @State private var items: [(String, Int, Int)] = []

    private var token: String {
        "\(store.revision)|\(store.isDemoMode ? 1 : 0)|\(store.updatedAt?.timeIntervalSince1970 ?? 0)|\(store.rows.count)"
    }

    private static func compute(rows: [OrderRow], stats s: [String: Int]) -> [(String, Int, Int)] {
        let now = Int(Date().timeIntervalSince1970 * 1000)
        var mTotal = 0, mRecv = 0, mPend = 0, mWatch = 0, mPre = 0, mCancelType = 0
        var aTotal = 0, aRecv = 0, cTotal = 0, cRecv = 0, mDanger = 0, mWarn = 0
        for r in rows {
            if r.k == "m" {
                mTotal += 1
                if r.rv { mRecv += 1 }
                if r.pd {
                    mPend += 1
                    switch r.alertLevel(now: now) {
                    case .overdue: mDanger += 1
                    case .soon: mWarn += 1
                    default: break
                    }
                }
                if r.wl { mWatch += 1 }
                if r.pre { mPre += 1 }
                if r.ty == "Đơn hủy" { mCancelType += 1 }
            } else if r.k == "a" {
                aTotal += 1
                if r.rv { aRecv += 1 }
            } else if r.k == "c" {
                cTotal += 1
                if r.rv { cRecv += 1 }
            }
        }
        return [
            ("Bảng chính", s["m_total"] ?? 0, mTotal),
            ("Đã nhận (bảng chính)", s["m_recv"] ?? 0, mRecv),
            ("Đang chờ (bảng chính)", s["m_pend"] ?? 0, mPend),
            ("Đang theo dõi", s["m_watch"] ?? 0, mWatch),
            ("Hàng đặt trước", s["m_pre"] ?? 0, mPre),
            ("Loại đơn là Đơn hủy", s["m_cancelType"] ?? 0, mCancelType),
            ("Kho lưu trữ", s["a_total"] ?? 0, aTotal),
            ("Lưu trữ đã nhận", s["a_recv"] ?? 0, aRecv),
            ("Bảng đơn hủy", s["c_total"] ?? 0, cTotal),
            ("Đơn hủy đã nhận", s["c_recv"] ?? 0, cRecv),
            ("Báo Đỏ (quá hạn)", s["m_danger"] ?? 0, mDanger),
            ("Báo Vàng (sắp đến hạn)", s["m_warn"] ?? 0, mWarn)
        ]
    }

    var body: some View {
        List {
            if store.stats.isEmpty {
                Text("Máy tính chưa gửi bảng đối chiếu.")
                    .foregroundStyle(.secondary)
            } else {
                Section("So sánh số liệu") {
                    ForEach(Array(items.enumerated()), id: \.offset) { _, item in
                        let match = item.1 == item.2
                        HStack {
                            Text(item.0)
                                .font(.subheadline)
                            Spacer()
                            Text("PC \(item.1) · ĐT \(item.2)")
                                .font(.caption.bold())
                                .monospacedDigit()
                                .foregroundColor(match ? Theme.success : Theme.warning)
                            Image(systemName: match ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                                .font(.caption)
                                .foregroundColor(match ? Theme.success : Theme.warning)
                        }
                    }
                }

                if let d = store.stats["dropped"], d > 0 {
                    Section {
                        Text("\(d) đơn không có mã vận đơn và mã đơn hàng nên không gửi sang được.")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
            }
        }
        .listStyle(.insetGrouped)
        .scrollContentBackground(.hidden)
        .background(Theme.pageBackground.ignoresSafeArea())
        .navigationTitle("Đối chiếu với PC")
        .navigationBarTitleDisplayMode(.inline)
        .task(id: token) {
            let rowsSnapshot = store.rows
            let statsSnapshot = store.stats
            let computed = await Task.detached(priority: .userInitiated) {
                Self.compute(rows: rowsSnapshot, stats: statsSnapshot)
            }.value
            guard !Task.isCancelled else { return }
            items = computed
        }
    }
}
