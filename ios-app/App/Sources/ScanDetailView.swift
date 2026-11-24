import SwiftUI
import UIKit
import ScannerCore

func scanLabel(_ it: ScanItem) -> String { ScanDisplay.make(for: it).label }

/// Xanh = thành công / tạo thành công; đỏ = quét trùng / đã tạo rồi / lỗi; vàng = không có / đang chờ.
func toneColor(_ tone: ScanTone) -> Color {
    switch tone {
    case .success: return Theme.success
    case .danger: return Theme.danger
    case .warning: return Theme.warning
    }
}

func scanColor(_ it: ScanItem) -> Color { toneColor(ScanDisplay.make(for: it).tone) }

/// Chi tiết một lần quét (T040): ảnh đã lưu vào đơn (chạm để zoom toàn màn hình) hoặc ảnh tạm đang chờ PC ACK,
/// cảnh báo nếu treo quá 7 ngày cho người dùng quyết định bỏ ảnh tạm (KHÔNG tự xóa lượt quét), gửi lại lệnh quét, sao chép.
struct ScanDetailView: View {
    let requestId: String
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @EnvironmentObject var photoManager: OrderPhotoManager
    @Environment(\.dismiss) private var dismiss

    @State private var viewerSession: PhotoViewerSession?
    @State private var linkedOrderRow: OrderRow?
    @State private var confirmDiscardTempPhoto = false

    var body: some View {
        NavigationStack {
            if let it = engine.items.first(where: { $0.requestId == requestId }) {
                let _ = photoManager.revision
                let displayURL = engine.scanDisplayPhotoURL(it)
                let match = store.hasData ? store.matchParcel(for: it.code) : .notFound
                let attachedRow: OrderRow? = it.attachedOrderId.flatMap { store.row(byId: $0) }
                let hasTempPhoto = !engine.isDemoMode && it.hasTemporaryPhoto && displayURL != nil
                let isStaleTemp = hasTempPhoto && engine.isStalePendingTempPhoto(it)

                Form {
                    if let u = displayURL, let img = UIImage(contentsOfFile: u.path) {
                        Section(it.attachedPhotoId != nil ? "Ảnh đã lưu vào đơn (trên iPhone)" : "Ảnh tạm (đang chờ PC xác nhận)") {
                            Button {
                                Haptics.tap()
                                let size = Int64((try? FileManager.default.attributesOfItem(atPath: u.path)[.size] as? NSNumber)?.int64Value ?? 0)
                                let item = PhotoViewerItem(
                                    id: it.attachedPhotoId ?? it.requestId,
                                    url: u,
                                    createdAtMs: it.time,
                                    byteSize: size,
                                    canDelete: false
                                )
                                viewerSession = PhotoViewerSession(items: [item], initialIndex: 0)
                            } label: {
                                VStack(spacing: 6) {
                                    Image(uiImage: img)
                                        .resizable()
                                        .scaledToFit()
                                        .frame(maxWidth: .infinity, maxHeight: 240)
                                        .cornerRadius(8)
                                    Text("Chạm vào ảnh để xem toàn màn hình & phóng to")
                                        .font(.caption2)
                                        .foregroundStyle(.secondary)
                                }
                            }
                            .buttonStyle(.plain)
                        }
                    }

                    if isStaleTemp {
                        Section("Cảnh báo ảnh tạm quá 7 ngày") {
                            Text("Lượt quét này đã treo hơn 7 ngày chưa có kết quả từ máy tính. Lượt quét vẫn được giữ nguyên; bạn có thể bỏ ảnh tạm để giải phóng dung lượng.")
                                .font(.caption)
                                .foregroundColor(Theme.warning)
                            Button("Bỏ ảnh tạm (giữ nguyên lượt quét)", role: .destructive) {
                                Haptics.medium()
                                confirmDiscardTempPhoto = true
                            }
                        }
                    }

                    Section("Lần quét") {
                        LabeledContent("Mã", value: it.code)
                        LabeledContent("Kết quả") { Text(scanLabel(it)).foregroundColor(scanColor(it)).bold() }
                        if !it.detail.isEmpty { Text(it.detail).font(.footnote).foregroundStyle(.secondary) }
                        LabeledContent("Thời gian", value: Date(timeIntervalSince1970: Double(it.time) / 1000).formatted(date: .abbreviated, time: .standard))
                        if let n = it.orderNum, !n.isEmpty { LabeledContent("Đơn số", value: n) }
                        if it.attachedPhotoId != nil {
                            LabeledContent("Ảnh", value: "Đã lưu trong chi tiết đơn trên iPhone")
                        } else if isStaleTemp {
                            LabeledContent("Ảnh", value: "Ảnh tạm treo quá 7 ngày")
                        } else if hasTempPhoto {
                            LabeledContent("Ảnh", value: it.scanConfirmed ? "Ảnh tạm chờ đồng bộ danh sách đơn" : "Ảnh tạm chờ PC xác nhận")
                        } else if it.kind == .returnScan && (it.status == .notFound || it.status == .ambiguous || it.status == .duplicate || it.status == .error) {
                            LabeledContent("Ảnh", value: "Không lưu (đã tự xóa ảnh tạm)")
                        }
                        if !it.scanConfirmed { LabeledContent("Số lần gửi", value: "\(it.attempts)") }
                    }

                    if let row = attachedRow {
                        Section("Đơn đã gắn ảnh") {
                            Button {
                                Haptics.tap()
                                linkedOrderRow = row
                            } label: {
                                HStack {
                                    VStack(alignment: .leading, spacing: 2) {
                                        Text(row.c.isEmpty ? row.o : row.c)
                                            .font(Theme.Typography.code)
                                            .foregroundColor(.primary)
                                        if !row.n.isEmpty {
                                            Text(row.n)
                                                .font(.caption)
                                                .foregroundStyle(.secondary)
                                                .lineLimit(2)
                                        }
                                    }
                                    Spacer()
                                    Text("Mở chi tiết")
                                        .font(.caption.bold())
                                        .foregroundColor(Theme.accent)
                                }
                            }
                        }
                    }

                    if attachedRow == nil, case .unique(let r) = match {
                        Section("Thông tin đơn (từ máy tính)") {
                            LabeledContent("Sản phẩm", value: r.n.isEmpty ? "—" : r.n)
                            let cs = [r.co, r.sz].filter { !$0.isEmpty }.joined(separator: " / ")
                            LabeledContent("Màu / Size", value: cs.isEmpty ? "—" : cs)
                            LabeledContent("Số lượng", value: "\(r.q)")
                            LabeledContent("Còn lại", value: Deadline.text(due: r.due, now: Int(Date().timeIntervalSince1970 * 1000)))
                            Button("Mở chi tiết đơn") {
                                Haptics.tap()
                                linkedOrderRow = r
                            }
                        }
                    }

                    Section {
                        Button("Sao chép mã") { UIPasteboard.general.string = it.code }
                        if !it.isSettled || it.status == .error {
                            Button("Gửi lại lệnh quét lên máy tính") { engine.resend(it.requestId) }
                        }
                        Button("Xóa khỏi lịch sử", role: .destructive) { engine.delete(it); dismiss() }
                    } footer: {
                        if hasTempPhoto {
                            Text("Xóa lượt quét khỏi lịch sử sẽ xóa luôn ảnh tạm của lượt này.")
                        } else if it.attachedPhotoId != nil {
                            Text("Xóa dòng lịch sử không làm mất ảnh đã lưu trong Chi tiết đơn.")
                        }
                    }
                }
                .navigationTitle("Chi tiết lần quét")
                .toolbar { ToolbarItem(placement: .navigationBarTrailing) { Button("Đóng") { dismiss() } } }
                .fullScreenCover(item: $viewerSession) { session in
                    ZoomablePhotoViewer(items: session.items, initialIndex: session.initialIndex)
                }
                .sheet(item: $linkedOrderRow) { r in
                    DetailView(row: r)
                }
                .alert("Bỏ ảnh tạm của lượt quét này?", isPresented: $confirmDiscardTempPhoto) {
                    Button("Hủy", role: .cancel) {}
                    Button("Bỏ ảnh tạm", role: .destructive) {
                        Haptics.medium()
                        _ = engine.discardTempPhoto(requestId: it.requestId)
                    }
                } message: {
                    Text("Chỉ xóa tệp ảnh tạm để giải phóng dung lượng; lượt quét vẫn được giữ nguyên trong hàng đợi chờ máy tính xác nhận.")
                }
            } else {
                Text("Mục này đã bị xóa").foregroundStyle(.secondary)
            }
        }
    }
}
