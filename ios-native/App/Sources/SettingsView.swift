import SwiftUI

struct SettingsView: View {
    @EnvironmentObject var engine: SyncEngine
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
                    LabeledContent("PC", value: engine.pcLastSeen.map { "Phản hồi \($0.formatted(date: .omitted, time: .standard))" } ?? "Chưa thấy phản hồi")
                    LabeledContent("Đơn đang chờ PC", value: "\(engine.pendingCount)")
                }
                Section("Giới thiệu") {
                    LabeledContent("Phiên bản", value: "0.2.0 (app gốc)")
                }
            }
            .navigationTitle("Cài đặt")
            .onAppear { roomText = engine.room }
        }
    }
}
