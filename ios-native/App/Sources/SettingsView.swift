import SwiftUI
import ScannerCore

struct SettingsView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @State private var roomText = ""
    @AppStorage("soundOn") private var soundOn = true
    @AppStorage("hapticOn") private var hapticOn = true
    @AppStorage("sendPhoto") private var sendPhoto = true

    /// (nhãn, số PC, số điện thoại): điện thoại đếm lại từ danh sách đã nhận để so với số PC tự tính.
    private var reconcile: [(String, Int, Int)] {
        let r = store.rows, s = store.stats
        func n(_ f: (OrderRow) -> Bool) -> Int { r.filter(f).count }
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
            ("Đơn hủy đã nhận", s["c_recv"] ?? 0, n { $0.k == "c" && $0.rv })
        ]
    }

    var body: some View {
        NavigationStack {
            Form {
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
