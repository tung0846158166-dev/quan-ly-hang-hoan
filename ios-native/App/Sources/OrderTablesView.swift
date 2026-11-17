import SwiftUI
import ScannerCore

/// Bảng đơn mới trên điện thoại = Hộp Đơn Mới trên máy tính: tích chọn rồi đẩy ra bảng chính, hoặc đẩy hết.
struct InboxView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @State private var picked: Set<String> = []
    @State private var confirmAll = false

    private var allPicked: Bool { !store.inbox.isEmpty && picked.count == store.inbox.count }

    var body: some View {
        List {
            if store.inbox.isEmpty {
                Text(store.hasData ? "Bảng đơn mới đang trống" : "Chưa nhận dữ liệu từ máy tính")
                    .foregroundStyle(.secondary)
            }
            ForEach(store.inbox) { it in
                Button { toggle(it.id) } label: { line(it) }
                    .buttonStyle(.plain)
            }
        }
        .listStyle(.plain)
        .navigationTitle("Đơn mới (\(store.inbox.count))")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .navigationBarTrailing) {
                Button(allPicked ? "Bỏ chọn" : "Chọn hết") {
                    picked = allPicked ? [] : Set(store.inbox.map { $0.id })
                }
                .disabled(store.inbox.isEmpty)
            }
        }
        .safeAreaInset(edge: .bottom) {
            HStack(spacing: 10) {
                Button {
                    engine.pushInbox(ids: store.inbox.map { $0.id }.filter { picked.contains($0) })
                    picked = []
                } label: { Text("Đẩy ra (\(picked.count))").bold().frame(maxWidth: .infinity) }
                    .buttonStyle(.borderedProminent)
                    .disabled(picked.isEmpty)
                Button { confirmAll = true } label: { Text("Đẩy ra hết (\(store.inbox.count))").bold().frame(maxWidth: .infinity) }
                    .buttonStyle(.bordered)
                    .disabled(store.inbox.isEmpty)
            }
            .padding(.horizontal).padding(.vertical, 8)
            .background(.bar)
        }
        .confirmationDialog("Đẩy toàn bộ \(store.inbox.count) đơn ra bảng chính?", isPresented: $confirmAll, titleVisibility: .visible) {
            Button("Đẩy ra hết") {
                engine.pushInbox(ids: store.inbox.map { $0.id })
                picked = []
            }
        }
        .onChange(of: store.inbox) { list in picked = picked.intersection(Set(list.map { $0.id })) }
        .refreshable { engine.requestSnapshot(); try? await Task.sleep(nanoseconds: 2_000_000_000) }
    }

    private func toggle(_ id: String) {
        if picked.contains(id) { picked.remove(id) } else { picked.insert(id) }
    }

    private func line(_ it: InboxRow) -> some View {
        let on = picked.contains(it.id)
        return HStack(spacing: 10) {
            Image(systemName: on ? "checkmark.circle.fill" : "circle")
                .font(.title2)
                .foregroundColor(on ? Color.accentColor : Color.secondary)
            VStack(alignment: .leading, spacing: 3) {
                Text(it.c.isEmpty ? it.o : it.c).font(.system(.body, design: .monospaced).bold())
                let sub = [it.p, it.t, it.n].filter { !$0.isEmpty }.joined(separator: " · ")
                if !sub.isEmpty { Text(sub).font(.caption).foregroundStyle(.secondary).lineLimit(2) }
                if it.dup {
                    Text("Đã có ở bảng khác: đẩy ra chỉ gỡ khỏi hộp, không tạo trùng").font(.caption2.bold()).foregroundColor(.orange)
                } else if it.u {
                    Text("Đã quét ở kho: đẩy ra sẽ ghi Hàng đã trả").font(.caption2.bold()).foregroundColor(.green)
                } else {
                    Text("Đẩy ra sẽ ghi Chưa trả (chờ hàng về)").font(.caption2).foregroundStyle(.secondary)
                }
            }
            Spacer(minLength: 0)
        }
        .padding(.vertical, 4)
        .contentShape(Rectangle())
    }
}

/// Bảng đơn hủy trên điện thoại = Bảng đơn hủy trên máy tính: tích chọn để đánh dấu đã nhận / chưa nhận.
struct CancelledTableView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    private enum Show: String, CaseIterable { case pending = "Chưa nhận", received = "Đã nhận", all = "Tất cả" }
    @State private var show: Show = .pending
    @State private var picked: Set<String> = []
    @State private var confirmAll = false

    private var all: [OrderRow] { store.rows.filter { $0.k == "c" } }
    private var list: [OrderRow] {
        switch show {
        case .pending: return all.filter { !$0.rv }
        case .received: return all.filter { $0.rv }
        case .all: return all
        }
    }
    /// Chỉ chọn được đơn có id từ máy tính (máy tính bản cũ chưa gửi id).
    private var selectable: [OrderRow] { list.filter { !$0.ci.isEmpty } }
    private var allPicked: Bool { !selectable.isEmpty && selectable.allSatisfy { picked.contains($0.ci) } }
    private var pendingIds: [String] { all.filter { !$0.rv && !$0.ci.isEmpty }.map { $0.ci } }

    var body: some View {
        VStack(spacing: 6) {
            Picker("", selection: $show) {
                ForEach(Show.allCases, id: \.self) { s in
                    Text("\(s.rawValue) (\(count(s)))").tag(s)
                }
            }
            .pickerStyle(.segmented)
            .padding(.horizontal)
            List {
                if list.isEmpty { Text("Không có đơn hủy").foregroundStyle(.secondary) }
                ForEach(list) { r in
                    Button { toggle(r.ci) } label: { line(r) }
                        .buttonStyle(.plain)
                        .disabled(r.ci.isEmpty)
                }
            }
            .listStyle(.plain)
        }
        .navigationTitle("Đơn hủy (\(all.count))")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .navigationBarTrailing) {
                Button(allPicked ? "Bỏ chọn" : "Chọn hết") {
                    if allPicked { picked.subtract(selectable.map { $0.ci }) } else { picked.formUnion(selectable.map { $0.ci }) }
                }
                .disabled(selectable.isEmpty)
            }
        }
        .safeAreaInset(edge: .bottom) {
            VStack(spacing: 8) {
                HStack(spacing: 10) {
                    Button { apply(true) } label: { Text("✅ Đã nhận (\(picked.count))").bold().frame(maxWidth: .infinity) }
                        .buttonStyle(.borderedProminent).tint(.green)
                        .disabled(picked.isEmpty)
                    Button { apply(false) } label: { Text("↩ Chưa nhận (\(picked.count))").bold().frame(maxWidth: .infinity) }
                        .buttonStyle(.bordered)
                        .disabled(picked.isEmpty)
                }
                Button { confirmAll = true } label: { Text("Nhận hết đơn chưa nhận (\(pendingIds.count))").bold().frame(maxWidth: .infinity) }
                    .buttonStyle(.bordered)
                    .disabled(pendingIds.isEmpty)
            }
            .padding(.horizontal).padding(.vertical, 8)
            .background(.bar)
        }
        .confirmationDialog("Đánh dấu đã nhận toàn bộ \(pendingIds.count) đơn hủy chưa nhận?", isPresented: $confirmAll, titleVisibility: .visible) {
            Button("Đã nhận hết") {
                engine.setCancelledReceived(ids: pendingIds, received: true)
                picked = []
            }
        }
        .onChange(of: show) { _ in picked = [] }
        .refreshable { engine.requestSnapshot(); try? await Task.sleep(nanoseconds: 2_000_000_000) }
    }

    private func count(_ s: Show) -> Int {
        switch s {
        case .pending: return all.filter { !$0.rv }.count
        case .received: return all.filter { $0.rv }.count
        case .all: return all.count
        }
    }

    private func toggle(_ id: String) {
        guard !id.isEmpty else { return }
        if picked.contains(id) { picked.remove(id) } else { picked.insert(id) }
    }

    private func apply(_ received: Bool) {
        let ids = list.map { $0.ci }.filter { picked.contains($0) }
        engine.setCancelledReceived(ids: ids.isEmpty ? Array(picked) : ids, received: received)
        picked = []
    }

    private func line(_ r: OrderRow) -> some View {
        let on = picked.contains(r.ci)
        return HStack(spacing: 10) {
            Image(systemName: on ? "checkmark.circle.fill" : "circle")
                .font(.title2)
                .foregroundColor(r.ci.isEmpty ? Color.secondary.opacity(0.3) : (on ? Color.accentColor : Color.secondary))
            VStack(alignment: .leading, spacing: 3) {
                Text(r.c).font(.system(.body, design: .monospaced).bold())
                let sub = [r.p, r.rd, r.n].filter { !$0.isEmpty }.joined(separator: " · ")
                if !sub.isEmpty { Text(sub).font(.caption).foregroundStyle(.secondary).lineLimit(2) }
            }
            Spacer(minLength: 0)
            Text(r.rv ? "Đã nhận" : "Chưa nhận")
                .font(.caption.bold())
                .padding(.horizontal, 8).padding(.vertical, 3)
                .background((r.rv ? Color.green : Color.orange).opacity(0.18), in: Capsule())
                .foregroundColor(r.rv ? .green : .orange)
        }
        .padding(.vertical, 4)
        .contentShape(Rectangle())
    }
}
