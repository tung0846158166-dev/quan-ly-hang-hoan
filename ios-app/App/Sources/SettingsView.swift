import SwiftUI
import UIKit
import ScannerCore

struct SettingsView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @EnvironmentObject var photoManager: OrderPhotoManager
    @State private var roomText = ""
    @State private var brokerText = UserDefaults.standard.string(forKey: "privateBroker") ?? ""
    @State private var savedToast = ""
    @State private var cleanupPreview: PhotoCleanupPreview?
    @State private var cleanupNotice: String?
    @State private var confirmDiscardStaleTemp = false
    @AppStorage("soundOn") private var soundOn = true
    @AppStorage("hapticOn") private var hapticOn = true
    @AppStorage("sendPhoto") private var sendPhoto = true
    @AppStorage("minCodeSize") private var minCodeSize = 0.12
    @AppStorage("photoCleanupDays") private var cleanupDays = 30

    private static let cleanupDayChoices = [7, 14, 30, 60, 90]

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
                // MARK: - Cloud (T042)
                Section {
                    NavigationLink {
                        CloudSettingsView()
                    } label: {
                        HStack {
                            Image(systemName: "cloud.fill").foregroundColor(engine.cloudActive ? CloudStatusText.tone(engine.cloudState).color : .gray)
                            Text("Cloud")
                            Spacer()
                            Text(engine.cloudActive ? CloudStatusText.label(engine.cloudState) : "Chưa ghép").font(.footnote).foregroundColor(.secondary)
                        }
                    }
                } footer: {
                    Text(engine.cloudActive ? "Điện thoại đồng bộ thẳng với cloud, không cần máy tính bật."
                                            : "Ghép cloud bằng mật khẩu cửa hàng để nhận danh sách đơn và quét nhận.")
                }

                // MARK: - Dữ liệu
                Section("Dữ liệu") {
                    settingsRow(icon: "shippingbox.fill", color: Theme.accent, title: "Danh sách đơn") {
                        Text(store.updatedAt.map { "\(store.rows.count) đơn · \($0.formatted(date: .omitted, time: .shortened))" } ?? "Chưa nhận")
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                    }

                    Button {
                        Haptics.tap()
                        Task { await engine.cloud.syncNow() }
                    } label: {
                        HStack(spacing: 10) {
                            IconTile(systemName: "arrow.clockwise", color: Theme.info, size: 28, cornerRadius: 7)
                            Text("Đồng bộ lại từ cloud")
                                .font(Theme.Typography.primary)
                                .foregroundColor(engine.cloudActive ? Theme.accent : .secondary)
                        }
                    }
                    .disabled(!engine.cloudActive || engine.isDemoMode)
                }

                // MARK: - Ảnh cục bộ (T039/T040: chỉ lưu ảnh khi cloud xác nhận đúng 1 kiện, không gửi sang PC, không tự xóa ảnh đã gắn)
                Section {
                    let tempSummary = engine.tempPhotoSummary
                    let staleSummary = engine.stalePendingTempPhotoSummary

                    settingsRow(icon: "photo.stack.fill", color: Theme.teal, title: "Ảnh đã lưu") {
                        Text("\(photoManager.storageSummary.photoCount) ảnh, \(photoManager.storageSummary.formattedMB)")
                            .font(.subheadline.bold())
                            .monospacedDigit()
                            .foregroundStyle(.secondary)
                    }

                    if tempSummary.photoCount > 0 {
                        settingsRow(icon: "clock.fill", color: Theme.info, title: "Ảnh tạm chờ cloud xác nhận") {
                            Text("\(tempSummary.photoCount) ảnh, \(tempSummary.formattedMB)")
                                .font(.subheadline.bold())
                                .monospacedDigit()
                                .foregroundColor(Theme.info)
                        }
                    }

                    if staleSummary.photoCount > 0 && !engine.isDemoMode {
                        Button(role: .destructive) {
                            Haptics.medium()
                            confirmDiscardStaleTemp = true
                        } label: {
                            HStack(spacing: 10) {
                                IconTile(systemName: "exclamationmark.triangle.fill", color: Theme.warning, size: 28, cornerRadius: 7)
                                Text("Bỏ \(staleSummary.photoCount) ảnh tạm treo quá 7 ngày (\(staleSummary.formattedMB))")
                                    .font(Theme.Typography.primary)
                                    .foregroundColor(Theme.danger)
                            }
                        }
                    }

                    Picker(selection: $cleanupDays) {
                        ForEach(Self.cleanupDayChoices, id: \.self) { d in
                            Text("\(d) ngày").tag(d)
                        }
                    } label: {
                        HStack(spacing: 10) {
                            IconTile(systemName: "calendar.badge.clock", color: Theme.warning, size: 28, cornerRadius: 7)
                            Text("Đơn đã hoàn tất cũ hơn")
                                .font(Theme.Typography.primary)
                        }
                    }

                    Button(role: .destructive) {
                        Haptics.medium()
                        let preview = photoManager.previewCleanupCompletedOrders(snapshot: store, olderThanDays: cleanupDays)
                        if preview.photoCount == 0 {
                            cleanupNotice = "Không có ảnh nào của các đơn đã hoàn tất cũ hơn \(cleanupDays) ngày."
                        } else {
                            cleanupPreview = preview
                        }
                    } label: {
                        HStack(spacing: 10) {
                            IconTile(systemName: "trash.fill", color: Theme.danger, size: 28, cornerRadius: 7)
                            Text("Xóa ảnh đơn đã hoàn tất cũ hơn \(cleanupDays) ngày")
                                .font(Theme.Typography.primary)
                                .foregroundColor(engine.isDemoMode ? .secondary : Theme.danger)
                        }
                    }
                    .disabled(engine.isDemoMode)
                } header: {
                    Text("Ảnh cục bộ trên iPhone")
                } footer: {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("Ảnh chụp lúc quét chỉ được lưu vào Chi tiết đơn khi cloud xác nhận khớp đúng 1 kiện; lượt quét không tìm thấy, mơ hồ, lỗi hoặc trùng sẽ tự xóa ảnh ngay. Ảnh đã lưu vào đơn chỉ bị xóa khi bạn chủ động xóa.")
                        if let log = photoManager.lastOrphanCleanupLog, !log.isEmpty {
                            Text(log)
                        }
                    }
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
                    VStack(alignment: .leading, spacing: 4) {
                        HStack(spacing: 10) {
                            IconTile(systemName: "camera.fill", color: Theme.teal, size: 28, cornerRadius: 7)
                            Toggle("Chụp ảnh lưu vào đơn khi quét hoàn kho", isOn: $sendPhoto)
                                .font(Theme.Typography.primary)
                                .tint(Theme.accent)
                        }
                        Text("Ảnh chụp lúc quét được nén gọn; chỉ lưu vào Chi tiết đơn trên iPhone khi cloud xác nhận đúng kiện (quét sai/mơ hồ/trùng tự bỏ ảnh), ảnh cũng được lưu lên cloud cho máy khác xem.")
                            .font(Theme.Typography.caption)
                            .foregroundStyle(.secondary)
                    }
                    .padding(.vertical, 2)
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

                // MARK: - Trợ lý AI
                Section("Trợ lý AI") {
                    NavigationLink {
                        AIKeySettingsView()
                    } label: {
                        HStack(spacing: 10) {
                            IconTile(systemName: "sparkles", color: Theme.purple, size: 28, cornerRadius: 7)
                            Text("Mô hình AI")
                                .font(Theme.Typography.primary)
                            Spacer()
                        }
                    }
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
            .onAppear {
                roomText = engine.room
                photoManager.refreshStorage()
            }
            .alert(
                "Xóa \(cleanupPreview?.photoCount ?? 0) ảnh (\(cleanupPreview?.formattedMB ?? "0,0 MB"))?",
                isPresented: Binding(
                    get: { cleanupPreview != nil },
                    set: { if !$0 { cleanupPreview = nil } }
                )
            ) {
                Button("Hủy", role: .cancel) { cleanupPreview = nil }
                Button("Xóa \(cleanupPreview?.photoCount ?? 0) ảnh", role: .destructive) {
                    if let p = cleanupPreview {
                        let res = photoManager.executeCleanup(recordIds: p.recordIds)
                        cleanupNotice = "Đã xóa \(res.deletedCount) ảnh, giải phóng \(PhotoStorageSummary.formatMB(res.freedBytes))."
                    }
                    cleanupPreview = nil
                }
            } message: {
                Text("Chỉ xóa ảnh của các đơn đã hoàn tất (đã nhận kho / lưu trữ / không cần nhận) và cũ hơn \(cleanupDays) ngày. Đơn đang chờ hoàn được giữ nguyên. Không thể hoàn tác.")
            }
            .alert("Dọn ảnh cục bộ", isPresented: Binding(
                get: { cleanupNotice != nil },
                set: { if !$0 { cleanupNotice = nil } }
            )) {
                Button("Đã hiểu", role: .cancel) { cleanupNotice = nil }
            } message: {
                Text(cleanupNotice ?? "")
            }
            .alert(
                "Bỏ ảnh tạm của các lượt quét treo quá 7 ngày?",
                isPresented: $confirmDiscardStaleTemp
            ) {
                Button("Hủy", role: .cancel) {}
                Button("Bỏ ảnh tạm", role: .destructive) {
                    let res = engine.discardStalePendingTempPhotos()
                    cleanupNotice = "Đã bỏ \(res.deletedCount) ảnh tạm (\(PhotoStorageSummary.formatMB(res.freedBytes))). Các lượt quét vẫn được giữ nguyên."
                }
            } message: {
                Text("Chỉ xóa tệp ảnh tạm của các lượt quét chưa có kết quả quá 7 ngày. KHÔNG xóa lượt quét khỏi Lịch sử và KHÔNG xóa ảnh đã lưu vào đơn.")
            }
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
