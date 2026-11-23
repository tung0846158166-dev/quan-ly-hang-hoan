import SwiftUI
import ScannerCore

struct SettingsView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @State private var roomText = ""
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

    private func statusTile(_ title: String, on: Bool, onText: String, offText: String, icon: String) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 6) {
                Image(systemName: icon).font(.caption.bold()).foregroundColor(on ? .green : .orange)
                Text(title).font(.caption.bold()).foregroundStyle(.secondary)
            }
            HStack(spacing: 6) {
                Circle().fill(on ? Color.green : Color.orange).frame(width: 9, height: 9)
                Text(on ? onText : offText).font(.system(size: 17, weight: .bold, design: .rounded))
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    HStack(spacing: 10) {
                        statusTile("Kênh đồng bộ", on: engine.channelUp, onText: "Đã nối", offText: "Mất kết nối", icon: "antenna.radiowaves.left.and.right")
                        statusTile("Máy tính", on: engine.pcOnline, onText: "Đang online", offText: "Chờ phản hồi", icon: "desktopcomputer")
                    }
                    .listRowInsets(EdgeInsets()).listRowBackground(Color.clear)
                }
                Section("Mã phòng (giống mã trên PC)") {
                    TextField("SHP-2026", text: $roomText)
                        .textInputAutocapitalization(.characters).autocorrectionDisabled()
                        .onSubmit { engine.setRoom(roomText) }
                    Button("Lưu mã phòng") { engine.setRoom(roomText) }
                }
                Section("Trạng thái") {
                    LabeledContent("Kênh ntfy", value: engine.sseConnected ? "Đã nối" : "Mất kết nối")
                    LabeledContent("Kênh MQTT dự phòng", value: engine.brokerUp.isEmpty ? "Mất kết nối" : "Đã nối (\(engine.brokerUp.count)/\(SyncProtocol.mqttBrokers.count))")
                    LabeledContent("PC", value: engine.pcLastSeen.map { "Phản hồi \($0.formatted(date: .omitted, time: .standard))" } ?? "Chưa thấy phản hồi")
                    LabeledContent("Đơn đang chờ PC", value: "\(engine.pendingCount)")
                }
                Section("Dữ liệu đơn & thông báo") {
                    LabeledContent("Danh sách từ PC", value: store.updatedAt.map { "\(store.rows.count) đơn, lúc \($0.formatted(date: .omitted, time: .shortened))" } ?? "Chưa nhận")
                    LabeledContent("Mảnh dữ liệu đã nhận", value: engine.snapshotLastChunkAt.map { "\(engine.snapshotChunksSeen) mảnh, lúc \($0.formatted(date: .omitted, time: .standard))" } ?? "Chưa nhận mảnh nào")
                    if !store.assembleInfo.isEmpty { LabeledContent("Ghép danh sách", value: store.assembleInfo) }
                    Button("Cập nhật dữ liệu đơn từ PC") { engine.requestSnapshot() }
                    LabeledContent("Thông báo quá hạn", value: store.notificationsAllowed ? "Đã bật" : "Chưa bật")
                    if !store.notificationsAllowed { Button("Bật thông báo đơn sắp đến hạn") { store.requestNotificationPermission() } }
                }
                Section("Đối chiếu số đơn với máy tính") {
                    if store.stats.isEmpty {
                        Text("Máy tính chưa gửi bảng đối chiếu (cần bản máy tính 1.9.4 trở lên).").font(.footnote).foregroundStyle(.secondary)
                    } else {
                        ForEach(reconcile, id: \.0) { item in
                            LabeledContent(item.0) {
                                Text("PC \(item.1) · ĐT \(item.2)" + (item.1 == item.2 ? "  ✓" : "  ⚠️"))
                                    .foregroundColor(item.1 == item.2 ? .green : .orange)
                            }
                        }
                        if let d = store.stats["dropped"], d > 0 {
                            Text("\(d) đơn không có mã vận đơn và mã đơn hàng nên không gửi sang được.").font(.footnote).foregroundStyle(.secondary)
                        }
                    }
                }
                Section("Tùy chọn quét") {
                    Toggle("Âm thanh khi có kết quả", isOn: $soundOn)
                    Toggle("Rung khi quét", isOn: $hapticOn)
                    Toggle("Gửi kèm ảnh khi quét hoàn kho", isOn: $sendPhoto)
                    VStack(alignment: .leading, spacing: 4) {
                        Text(minCodeSize <= 0 ? "Giới hạn khoảng cách quét: tắt" : "Giới hạn khoảng cách quét (mã phải đủ to trong khung)")
                        Slider(value: $minCodeSize, in: 0...0.3, step: 0.01)
                        Text("Kéo sang phải: chỉ nhận mã ở gần hơn. Kéo về 0: nhận mọi mã. Mặc định 0,12 ≈ dưới khoảng 30 cm với nhãn thường; mã to nhỏ khác nhau nên chỉ là gần đúng.")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                }
                Section("Giới thiệu") {
                    LabeledContent("Phiên bản", value: (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "?") + " (app gốc)")
                }
            }
            .navigationTitle("Cài đặt")
            .onAppear { roomText = engine.room }
        }
    }
}
