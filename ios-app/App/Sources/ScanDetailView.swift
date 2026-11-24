import SwiftUI
import ScannerCore

func scanLabel(_ it: ScanItem) -> String {
    if !it.scanConfirmed { return it.status == .error ? "Lỗi, đang thử lại" : "Chờ nạp PC" }
    switch it.status {
    case .success: return it.kind == .returnScan ? "Hợp lệ" : "Đã tạo"
    case .duplicate: return "Quét trùng"
    case .notFound: return "Không có"
    case .ambiguous: return "Trùng nhiều đơn"
    case .error: return "PC từ chối"
    case .pending: return "Chờ nạp PC"
    }
}

func scanColor(_ it: ScanItem) -> Color {
    if !it.scanConfirmed { return Theme.warning }
    switch it.status {
    case .success: return Theme.success
    case .duplicate: return Theme.warning
    default: return Theme.danger
    }
}

/// Chi tiết một lần quét: ảnh lớn, trạng thái, thông tin đơn (từ danh sách PC gửi sang), gửi lại, sao chép.
struct ScanDetailView: View {
    let requestId: String
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            if let it = engine.items.first(where: { $0.requestId == requestId }) {
                Form {
                    if let u = engine.photoURL(it), let img = UIImage(contentsOfFile: u.path) {
                        Section("Ảnh lúc quét") {
                            Image(uiImage: img).resizable().scaledToFit().frame(maxWidth: .infinity).cornerRadius(8)
                        }
                    }
                    Section("Lần quét") {
                        LabeledContent("Mã", value: it.code)
                        LabeledContent("Kết quả") { Text(scanLabel(it)).foregroundColor(scanColor(it)).bold() }
                        if !it.detail.isEmpty { Text(it.detail).font(.footnote).foregroundStyle(.secondary) }
                        LabeledContent("Thời gian", value: Date(timeIntervalSince1970: Double(it.time) / 1000).formatted(date: .abbreviated, time: .standard))
                        if let n = it.orderNum, !n.isEmpty { LabeledContent("Đơn số", value: n) }
                        if it.photoFile != nil { LabeledContent("Ảnh", value: it.photoConfirmed ? "PC đã lưu" : "Đang chờ PC lưu") }
                        if !it.scanConfirmed { LabeledContent("Số lần gửi", value: "\(it.attempts)") }
                    }
                    if let r = store.lookup(it.code) {
                        Section("Thông tin đơn (từ máy tính)") {
                            LabeledContent("Sản phẩm", value: r.n.isEmpty ? "—" : r.n)
                            LabeledContent("Màu / Size", value: [r.co, r.sz].filter { !$0.isEmpty }.joined(separator: " / ").isEmpty ? "—" : [r.co, r.sz].filter { !$0.isEmpty }.joined(separator: " / "))
                            LabeledContent("Số lượng", value: "\(r.q)")
                            LabeledContent("Còn lại", value: Deadline.text(due: r.due, now: Int(Date().timeIntervalSince1970 * 1000)))
                        }
                    }
                    Section {
                        Button("Sao chép mã") { UIPasteboard.general.string = it.code }
                        if !it.isSettled || it.status == .error {
                            Button("Gửi lại cho máy tính") { engine.resend(it.requestId) }
                        }
                        Button("Xóa khỏi lịch sử", role: .destructive) { engine.delete(it); dismiss() }
                    }
                }
                .navigationTitle("Chi tiết lần quét")
                .toolbar { ToolbarItem(placement: .navigationBarTrailing) { Button("Đóng") { dismiss() } } }
            } else {
                Text("Mục này đã bị xóa").foregroundStyle(.secondary)
            }
        }
    }
}
