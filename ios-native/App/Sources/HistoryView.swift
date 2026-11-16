import SwiftUI
import ScannerCore

private enum HistoryFilter: String, CaseIterable {
    case valid = "Hợp lệ", duplicate = "Quét trùng", missing = "Không có", pending = "Chờ nạp PC"
}

struct HistoryView: View {
    @EnvironmentObject var engine: SyncEngine
    @State private var kind: ScanKind = .returnScan
    @State private var filter: HistoryFilter?
    @State private var confirmClear = false
    @State private var query = ""
    @State private var detail: ScanItem?

    private var filters: [HistoryFilter] { kind == .cancelOrder ? [.valid, .duplicate, .pending] : HistoryFilter.allCases }

    private var shown: [ScanItem] {
        engine.items.filter { $0.kind == kind }.filter { it in
            if !query.isEmpty && !it.code.localizedCaseInsensitiveContains(query) { return false }
            guard let f = filter else { return true }
            switch f {
            case .valid: return it.status == .success
            case .duplicate: return it.status == .duplicate
            case .missing: return it.status == .notFound || it.status == .ambiguous
            case .pending: return !it.isSettled   // gồm cả lượt quét đã xác nhận nhưng ảnh chưa gửi xong (cùng cách đếm với chip "Chờ")
            }
        }
    }

    var body: some View {
        NavigationStack {
            VStack(spacing: 8) {
                Picker("Loại", selection: $kind) {
                    Text("Quét hoàn kho").tag(ScanKind.returnScan)
                    Text("Đơn mới").tag(ScanKind.newOrder)
                    Text("Đơn hủy").tag(ScanKind.cancelOrder)
                }
                .pickerStyle(.segmented).padding(.horizontal)
                statsLine
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack {
                        ForEach(filters, id: \.self) { f in
                            Button(f.rawValue) { filter = (filter == f ? nil : f) }
                                .buttonStyle(.borderedProminent)
                                .tint(filter == f ? .indigo : .gray)
                                .font(.footnote.bold())
                        }
                    }.padding(.horizontal)
                }
                List {
                    ForEach(shown) { it in row(it).contentShape(Rectangle()).onTapGesture { detail = it } }
                        .onDelete { idx in idx.map { shown[$0] }.forEach(engine.delete) }
                }
                .listStyle(.plain)
                .overlay { if shown.isEmpty { Text("Chưa có lịch sử").foregroundStyle(.secondary) } }
            }
            .searchable(text: $query, prompt: "Tìm mã")
            .navigationTitle("Lịch sử")
            .toolbar {
                ToolbarItem(placement: .navigationBarLeading) {
                    ShareLink(item: engine.csv(kind: kind), subject: Text("Lịch sử quét")) { Image(systemName: "square.and.arrow.up") }
                }
                ToolbarItem(placement: .navigationBarTrailing) {
                    Button("Xóa hết") { confirmClear = true }.disabled(shown.isEmpty)
                }
            }
            .confirmationDialog("Xóa toàn bộ lịch sử mục này?", isPresented: $confirmClear) {
                Button("Xóa", role: .destructive) { engine.clear(kind: kind) }
            }
            .onChange(of: kind) { _ in filter = nil }
            .sheet(item: $detail) { ScanDetailView(requestId: $0.requestId) }
        }
    }

    private var statsLine: some View {
        let s = engine.todayStats(kind: kind)
        return Text("Hôm nay: \(s.total) mã · \(s.valid) hợp lệ · \(s.dup) trùng · \(s.missing) không có · \(s.pending) chờ PC")
            .font(.caption.bold()).foregroundStyle(.secondary).padding(.horizontal)
    }

    private func row(_ it: ScanItem) -> some View {
        HStack(spacing: 10) {
            if let u = engine.photoURL(it), let img = UIImage(contentsOfFile: u.path) {
                Image(uiImage: img).resizable().scaledToFill().frame(width: 52, height: 52).clipped().cornerRadius(6)
            }
            VStack(alignment: .leading, spacing: 2) {
                Text(it.code).font(.system(.body, design: .monospaced).bold())
                Text(scanLabel(it)).font(.caption.bold()).foregroundColor(scanColor(it))
                Text(it.detail).font(.caption2).foregroundStyle(.secondary)
                if it.scanConfirmed && it.photoFile != nil && !it.photoConfirmed && !it.reviewRequired {
                    Text("Đang chờ: ảnh chưa gửi xong (đã thử \(it.photoAttempts) lần)").font(.caption2.bold()).foregroundColor(.orange)
                } else if !it.scanConfirmed {
                    Text("Đang chờ: máy tính chưa xác nhận (đã gửi \(it.attempts) lần)").font(.caption2.bold()).foregroundColor(.orange)
                }
                Text(Date(timeIntervalSince1970: Double(it.time) / 1000).formatted(date: .abbreviated, time: .standard))
                    .font(.caption2).foregroundStyle(.secondary)
                if it.kind == .newOrder && it.scanConfirmed {
                    if it.pushed { Text("✅ Đã đẩy ra bảng chính").font(.caption2.bold()).foregroundColor(.green) }
                    else { Button("⬆️ Đẩy ra bảng chính") { engine.pushToMain(it) }.font(.caption.bold()).buttonStyle(.bordered) }
                }
            }
        }
    }
}
