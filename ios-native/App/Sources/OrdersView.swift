import SwiftUI
import ScannerCore

private enum OrderFilter: String, CaseIterable {
    case all = "Tất cả", overdue = "Quá hạn", soon = "Sắp đến hạn", main = "Bảng chính", cancelled = "Đơn hủy"
}

struct OrdersView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @State private var query = ""
    @State private var filter: OrderFilter = .all
    @State private var selected: OrderRow?
    @State private var platform = ""

    private var shown: [OrderRow] {
        let now = Int(Date().timeIntervalSince1970 * 1000)
        return store.rows.filter { r in
            if !platform.isEmpty && r.p != platform { return false }
            switch filter {
            case .all: break
            case .overdue: if Deadline.level(due: r.due, now: now) != .overdue { return false }
            case .soon: if Deadline.level(due: r.due, now: now) != .soon { return false }
            case .main: if r.isCancelled { return false }
            case .cancelled: if !r.isCancelled { return false }
            }
            if query.isEmpty { return true }
            return [r.c, r.o, r.n, r.co, r.sz].contains { $0.localizedCaseInsensitiveContains(query) }
        }
    }

    private var platforms: [String] { Array(Set(store.rows.map { $0.p }.filter { !$0.isEmpty })).sorted() }

    var body: some View {
        NavigationStack {
            VStack(spacing: 6) {
                summary
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack {
                        ForEach(OrderFilter.allCases, id: \.self) { f in
                            Button(f.rawValue) { filter = f }
                                .buttonStyle(.borderedProminent).tint(filter == f ? .indigo : .gray).font(.footnote.bold())
                        }
                    }.padding(.horizontal)
                }
                List(shown) { r in
                    Button { selected = r } label: { row(r) }.buttonStyle(.plain)
                }
                .listStyle(.plain)
                .refreshable { engine.requestSnapshot(); try? await Task.sleep(nanoseconds: 2_500_000_000) }
                .overlay {
                    if !store.hasData {
                        VStack(spacing: 6) {
                            Text("Chưa nhận dữ liệu đơn từ máy tính").font(.headline)
                            Text("Mở app trên PC và kéo xuống để cập nhật.").font(.footnote).foregroundStyle(.secondary)
                        }
                    } else if shown.isEmpty { Text("Không có đơn phù hợp").foregroundStyle(.secondary) }
                }
            }
            .searchable(text: $query, prompt: "Tìm mã, sản phẩm, màu, size")
            .navigationTitle("Đơn chờ hoàn")
            .toolbar {
                ToolbarItemGroup(placement: .navigationBarTrailing) {
                    Menu {
                        Button("Tất cả sàn") { platform = "" }
                        ForEach(platforms, id: \.self) { p in Button(p) { platform = p } }
                    } label: {
                        Image(systemName: platform.isEmpty ? "line.3.horizontal.decrease.circle" : "line.3.horizontal.decrease.circle.fill")
                    }
                    Button { engine.requestSnapshot() } label: { Image(systemName: "arrow.clockwise") }
                }
            }
            .sheet(item: $selected) { DetailView(row: $0) }
        }
    }

    private var summary: some View {
        HStack {
            Text("\(store.rows.count) đơn").font(.caption.bold())
            Text("🚨 \(store.overdueCount) quá hạn").font(.caption.bold()).foregroundColor(.red)
            Text("⏰ \(store.soonCount) sắp đến hạn").font(.caption.bold()).foregroundColor(.orange)
            Spacer()
            if let t = store.updatedAt { Text(t.formatted(date: .omitted, time: .shortened)).font(.caption2).foregroundStyle(.secondary) }
        }.padding(.horizontal)
    }

    private func row(_ r: OrderRow) -> some View {
        let scanned = engine.scannedReturnCodes
        let now = Int(Date().timeIntervalSince1970 * 1000)
        let level = Deadline.level(due: r.due, now: now)
        return VStack(alignment: .leading, spacing: 2) {
            HStack {
                Text(r.c).font(.system(.body, design: .monospaced).bold())
                if scanned.contains(r.c.uppercased()) { Text("✅ Đã quét").font(.caption2.bold()).foregroundColor(.green) }
                if r.isCancelled { Text("Đơn hủy").font(.caption2.bold()).padding(.horizontal, 5).background(Color.red.opacity(0.2)).cornerRadius(4) }
            }
            Text(r.n).font(.footnote).lineLimit(2)
            HStack {
                if !r.co.isEmpty || !r.sz.isEmpty { Text([r.co, r.sz].filter { !$0.isEmpty }.joined(separator: " / ")).font(.caption) }
                Text("×\(r.q)").font(.caption)
                Spacer()
                Text(Deadline.text(due: r.due, now: now)).font(.caption.bold()).foregroundColor(color(level))
            }
        }
    }

    private func color(_ l: DeadlineLevel) -> Color {
        switch l { case .overdue: return .red; case .soon: return .orange; case .normal: return .green; case .unknown: return .secondary }
    }
}

struct DetailView: View {
    let row: OrderRow
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            Form {
                Section("Mã") {
                    LabeledContent("Mã vận đơn", value: row.c)
                    if !row.o.isEmpty { LabeledContent("Mã đơn hàng", value: row.o) }
                    Button("Sao chép mã vận đơn") { UIPasteboard.general.string = row.c }
                }
                Section("Đơn") {
                    LabeledContent("Sàn", value: row.p.isEmpty ? "—" : row.p)
                    LabeledContent("Sản phẩm", value: row.n.isEmpty ? "—" : row.n)
                    LabeledContent("Màu", value: row.co.isEmpty ? "—" : row.co)
                    LabeledContent("Size", value: row.sz.isEmpty ? "—" : row.sz)
                    LabeledContent("Số lượng", value: "\(row.q)")
                    LabeledContent("Trạng thái", value: row.st.isEmpty ? "—" : row.st)
                }
                Section("Hạn xử lý") {
                    LabeledContent("Ngày hủy/hoàn", value: row.rd.isEmpty ? "—" : row.rd)
                    LabeledContent("Hạn", value: row.due > 0 ? Date(timeIntervalSince1970: Double(row.due) / 1000).formatted(date: .abbreviated, time: .shortened) : "Chưa có")
                    LabeledContent("Còn lại", value: Deadline.text(due: row.due, now: Int(Date().timeIntervalSince1970 * 1000)))
                }
            }
            .navigationTitle("Chi tiết đơn")
            .toolbar { ToolbarItem(placement: .navigationBarTrailing) { Button("Đóng") { dismiss() } } }
        }
    }
}
