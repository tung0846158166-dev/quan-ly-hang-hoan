import SwiftUI
import UIKit
import ScannerCore

struct SettingsView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @State private var roomText = ""
    @State private var savedToast = ""
    @AppStorage("soundOn") private var soundOn = true
    @AppStorage("hapticOn") private var hapticOn = true
    @AppStorage("sendPhoto") private var sendPhoto = true
    @AppStorage("minCodeSize") private var minCodeSize = 0.12

    /// (nhãn, số PC, số điện thoại): điện thoại đếm lại từ danh sách đã nhận để so với số PC tự tính.
    private var reconcile: [(String, Int, Int)] {
        let r = store.rows, s = store.stats
        func n(_ f: (OrderRow) -> Bool) -> Int { r.filter(f).count }
        let now = Int(Date().timeIntervalSince1970 * 1000)
        return [
            ("Bảng chính", s["m_total"] ?? 0, n { $0.k == "m" }),
            ("Đã nhận (bảng chính)", s["m_recv"] ?? 0, n { $0.k == "m" && $0.rv }),
            ("Đang chờ (bảng chính)", s["m_pend"] ?? 0, n { $0.k == "m" && $0.pd }),
            ("Đang theo dõi", s["m_watch"] ?? 0, n { $0.k == "m" && $0.wl }),
            ("Hàng đặt trước", s["m_pre"] ?? 0, n { $0.k == "m" && $0.pre }),
            ("Loại đơn là Đơn hủy", s["m_cancelType"] ?? 0, n { $0.k == "m" && $0.ty == "Đơn hủy" }),
            ("Kho lưu trữ", s["a_total"] ?? 0, n { $0.k == "a" }),
            ("Lưu trữ đã nhận", s["a_recv"] ?? 0, n { $0.k == "a" && $0.rv }),
            ("Bảng đơn hủy", s["c_total"] ?? 0, n { $0.k == "c" }),
            ("Đơn hủy đã nhận", s["c_recv"] ?? 0, n { $0.k == "c" && $0.rv }),
            ("Báo Đỏ (quá hạn)", s["m_danger"] ?? 0, n { $0.k == "m" && $0.pd && $0.alertLevel(now: now) == .overdue }),
            ("Báo Vàng (sắp đến hạn)", s["m_warn"] ?? 0, n { $0.k == "m" && $0.pd && $0.alertLevel(now: now) == .soon })
        ]
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: 16) {
                    statusTiles
                    roomCard
                    connectionCard
                    dataAndNotifyCard
                    reconcileCard
                    scanPrefsCard
                    aboutCard
                }
                .padding(.horizontal, 16)
                .padding(.top, 8)
                .padding(.bottom, 24)
            }
            .background(Theme.pageBackground.ignoresSafeArea())
            .navigationTitle("Cài đặt")
            .onAppear { roomText = engine.room }
        }
    }

    // MARK: - 2 thẻ trạng thái đầu trang
    private var statusTiles: some View {
        HStack(spacing: 12) {
            statusTile("Kênh đồng bộ", on: engine.channelUp, onText: "Đã nối", offText: "Mất kết nối", icon: "antenna.radiowaves.left.and.right")
            statusTile("Máy tính", on: engine.pcOnline, onText: "Đang online", offText: "Chờ phản hồi", icon: "desktopcomputer")
        }
    }

    private func statusTile(_ title: String, on: Bool, onText: String, offText: String, icon: String) -> some View {
        let color: Color = on ? Theme.success : Theme.warning
        return VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 6) {
                Image(systemName: icon)
                    .font(.caption.bold())
                    .foregroundColor(color)
                    .frame(width: 24, height: 24)
                    .background(color.opacity(0.14))
                    .clipShape(Circle())
                Text(title).font(.caption.bold()).foregroundStyle(.secondary)
            }
            HStack(spacing: 6) {
                Circle().fill(color).frame(width: 9, height: 9)
                Text(on ? onText : offText)
                    .font(.system(size: 17, weight: .bold, design: .rounded))
                    .lineLimit(1)
                    .minimumScaleFactor(0.8)
            }
        }
        .frame(maxWidth: .infinity, minHeight: 78, alignment: .leading)
        .card(padding: 14)
    }

    // MARK: - Mã phòng
    private var roomCard: some View {
        VStack(alignment: .leading, spacing: 12) {
            SectionTitleRow(title: "Mã phòng đồng bộ", icon: "key.fill", iconColor: Theme.accent)

            TextField("Dán mã phòng từ app PC v2", text: $roomText)
                .textInputAutocapitalization(.characters)
                .autocorrectionDisabled()
                .font(.system(.body, design: .monospaced))
                .padding(12)
                .background(Color(.tertiarySystemGroupedBackground))
                .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                .onSubmit { saveRoom() }

            HStack(spacing: 8) {
                Button {
                    Haptics.tap()
                    if let pasted = UIPasteboard.general.string {
                        roomText = pasted.trimmingCharacters(in: .whitespacesAndNewlines)
                    }
                } label: {
                    Label("Dán", systemImage: "doc.on.clipboard")
                        .font(.footnote.bold())
                        .frame(maxWidth: .infinity, minHeight: 44)
                }
                .buttonStyle(.bordered)

                Button {
                    Haptics.tap()
                    UIPasteboard.general.string = engine.room.isEmpty ? roomText : engine.room
                    savedToast = "Đã sao chép mã phòng"
                } label: {
                    Label("Sao chép", systemImage: "doc.on.doc")
                        .font(.footnote.bold())
                        .frame(maxWidth: .infinity, minHeight: 44)
                }
                .buttonStyle(.bordered)
                .disabled(engine.room.isEmpty && roomText.isEmpty)
            }

            Button { saveRoom() } label: {
                Text("Lưu mã phòng")
                    .font(.subheadline.bold())
                    .frame(maxWidth: .infinity, minHeight: 46)
            }
            .buttonStyle(.borderedProminent)
            .tint(Theme.accent)
            .disabled(!SyncProtocol.isValidRoom(roomText))

            if !roomText.isEmpty && !SyncProtocol.isValidRoom(roomText) {
                Text("Mã phòng quá ngắn (cần ít nhất 12 ký tự). Lấy mã ở màn \"Đồng bộ điện thoại\" của app PC v2.")
                    .font(.caption)
                    .foregroundColor(Theme.danger)
            } else if !savedToast.isEmpty {
                Text(savedToast)
                    .font(.caption.bold())
                    .foregroundColor(Theme.success)
            } else {
                Text("Mã phòng của app PC v2 là chuỗi ngẫu nhiên bảo mật. Giữ bí mật mã này và không dùng mã phòng cũ.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .card(padding: 16)
    }

    private func saveRoom() {
        guard SyncProtocol.isValidRoom(roomText) else { return }
        Haptics.medium()
        engine.setRoom(roomText)
        savedToast = "Đã lưu mã phòng và đang kết nối..."
    }

    // MARK: - Trạng thái kênh
    private var connectionCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            SectionTitleRow(title: "Trạng thái kết nối", icon: "network", iconColor: Theme.info)
            infoRow("Kênh ntfy", value: engine.sseConnected ? "Đã nối" : "Mất kết nối", valueColor: engine.sseConnected ? Theme.success : .secondary)
            Divider()
            infoRow("Kênh MQTT dự phòng", value: engine.brokerUp.isEmpty ? "Mất kết nối" : "Đã nối (\(engine.brokerUp.count)/\(SyncProtocol.mqttBrokers.count))", valueColor: engine.brokerUp.isEmpty ? .secondary : Theme.success)
            Divider()
            infoRow("Phản hồi PC", value: engine.pcLastSeen.map { $0.formatted(date: .omitted, time: .standard) } ?? "Chưa thấy phản hồi")
            Divider()
            infoRow("Đơn đang chờ PC", value: "\(engine.pendingCount)", valueColor: engine.pendingCount > 0 ? Theme.warning : .primary)
        }
        .card(padding: 16)
    }

    // MARK: - Dữ liệu & thông báo
    private var dataAndNotifyCard: some View {
        VStack(alignment: .leading, spacing: 12) {
            SectionTitleRow(title: "Dữ liệu đơn & thông báo", icon: "tray.full.fill", iconColor: Theme.teal)

            infoRow("Danh sách từ PC", value: store.updatedAt.map { "\(store.rows.count) đơn · \($0.formatted(date: .omitted, time: .shortened))" } ?? "Chưa nhận")
            Divider()
            infoRow("Mảnh dữ liệu đã nhận", value: engine.snapshotLastChunkAt.map { "\(engine.snapshotChunksSeen) mảnh · \($0.formatted(date: .omitted, time: .standard))" } ?? "Chưa nhận")
            if !store.assembleInfo.isEmpty {
                Divider()
                infoRow("Ghép danh sách", value: store.assembleInfo)
            }
            Divider()
            infoRow("Thông báo quá hạn", value: store.notificationsAllowed ? "Đã bật" : "Chưa bật", valueColor: store.notificationsAllowed ? Theme.success : Theme.warning)

            Button {
                Haptics.tap()
                engine.requestSnapshot()
            } label: {
                Label("Cập nhật dữ liệu đơn từ PC", systemImage: "arrow.clockwise")
                    .font(.subheadline.bold())
                    .frame(maxWidth: .infinity, minHeight: 44)
            }
            .buttonStyle(.borderedProminent)
            .tint(Theme.accent)

            if !store.notificationsAllowed {
                Button {
                    Haptics.tap()
                    store.requestNotificationPermission()
                } label: {
                    Label("Bật thông báo đơn sắp đến hạn", systemImage: "bell.badge.fill")
                        .font(.subheadline.bold())
                        .frame(maxWidth: .infinity, minHeight: 44)
                }
                .buttonStyle(.bordered)
                .tint(Theme.accent)
            }
        }
        .card(padding: 16)
    }

    // MARK: - Đối chiếu số đơn
    private var reconcileCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            SectionTitleRow(title: "Đối chiếu với máy tính", icon: "checkmark.shield.fill", iconColor: Theme.success)

            if store.stats.isEmpty {
                Text("Máy tính chưa gửi bảng đối chiếu.")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            } else {
                ForEach(Array(reconcile.enumerated()), id: \.offset) { idx, item in
                    if idx > 0 { Divider() }
                    let match = item.1 == item.2
                    HStack {
                        Text(item.0).font(.subheadline)
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
                if let d = store.stats["dropped"], d > 0 {
                    Text("\(d) đơn không có mã vận đơn và mã đơn hàng nên không gửi sang được.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .padding(.top, 4)
                }
            }
        }
        .card(padding: 16)
    }

    // MARK: - Tùy chọn quét
    private var scanPrefsCard: some View {
        VStack(alignment: .leading, spacing: 12) {
            SectionTitleRow(title: "Tùy chọn quét", icon: "barcode.viewfinder", iconColor: Theme.accent)

            Toggle("Âm thanh khi có kết quả", isOn: $soundOn)
                .tint(Theme.accent)
            Divider()
            Toggle("Rung phản hồi", isOn: $hapticOn)
                .tint(Theme.accent)
            Divider()
            Toggle("Gửi kèm ảnh khi quét hoàn kho", isOn: $sendPhoto)
                .tint(Theme.accent)
            Divider()
            VStack(alignment: .leading, spacing: 6) {
                Text(minCodeSize <= 0 ? "Giới hạn khoảng cách quét: tắt" : "Giới hạn khoảng cách quét (\(String(format: "%.2f", minCodeSize)))")
                    .font(.subheadline.bold())
                Slider(value: $minCodeSize, in: 0...0.3, step: 0.01)
                    .tint(Theme.accent)
                Text("Kéo sang phải: chỉ nhận mã ở gần hơn. Kéo về 0: nhận mọi mã. Mặc định 0,12 ≈ dưới 30 cm với nhãn thường.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .card(padding: 16)
    }

    // MARK: - Giới thiệu
    private var aboutCard: some View {
        VStack(alignment: .leading, spacing: 8) {
            SectionTitleRow(title: "Giới thiệu", icon: "info.circle.fill", iconColor: .secondary)
            infoRow("Phiên bản", value: (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "2.0.0") + " (SwiftUI v2)")
        }
        .card(padding: 16)
    }

    private func infoRow(_ title: String, value: String, valueColor: Color = .primary) -> some View {
        HStack(alignment: .firstTextBaseline) {
            Text(title)
                .font(.subheadline)
                .foregroundStyle(.secondary)
            Spacer(minLength: 8)
            Text(value)
                .font(.subheadline.bold())
                .foregroundColor(valueColor)
                .multilineTextAlignment(.trailing)
        }
    }
}
