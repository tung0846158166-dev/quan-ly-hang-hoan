import SwiftUI
import ScannerCore

struct SettingsView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @State private var roomText = ""

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
                    Button("Cập nhật dữ liệu đơn từ PC") { engine.requestSnapshot() }
                    LabeledContent("Thông báo quá hạn", value: store.notificationsAllowed ? "Đã bật" : "Chưa bật")
                    if !store.notificationsAllowed { Button("Bật thông báo đơn sắp đến hạn") { store.requestNotificationPermission() } }
                }
                Section("Giới thiệu") {
                    LabeledContent("Phiên bản", value: "0.3.0 (app gốc)")
                }
            }
            .navigationTitle("Cài đặt")
            .onAppear { roomText = engine.room }
        }
    }
}
