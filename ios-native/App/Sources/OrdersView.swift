import SwiftUI
import ScannerCore

// Các mục đầu tiên khớp đúng các tab trên máy tính (chỉ tính BẢNG CHÍNH): Bảng chính = "Tất cả các tuần",
// Chưa nhận, Đã nhận, Quá hạn = Báo Đỏ, Sắp đến hạn = Báo Vàng. Lưu trữ / Đơn hủy là các bảng riêng trên máy tính;
// "Toàn bộ" gồm cả 3 bảng (dùng khi tìm / quét mã).
private enum OrderFilter: String, CaseIterable {
    case main = "Bảng chính", pending = "Chưa nhận", received = "Đã nhận", overdue = "Quá hạn", soon = "Sắp đến hạn", watch = "Đang theo dõi"
    case cancelled = "Đơn hủy", archived = "Lưu trữ", pre = "Đặt trước", all = "Toàn bộ"
}

struct OrdersView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @State private var query = ""
    @State private var filter: OrderFilter = .main
    @State private var selected: OrderRow?
    @State private var platform = ""
    @State private var showScanner = false
    // Kết quả lọc được tính sẵn (không tính lại mỗi lần giao diện vẽ lại) để vuốt danh sách mượt.
    @State private var shown: [OrderRow] = []
    @State private var counts: [OrderFilter: Int] = [:]
    @State private var scanned: Set<String> = []

    private func matches(_ r: OrderRow, _ q: String, _ qc: String) -> Bool {
        if q.isEmpty { return true }
        for f in [r.c, r.o, r.n, r.co, r.sz, r.wr, r.nt] {
            if f.localizedCaseInsensitiveContains(q) { return true }
            if !qc.isEmpty && f.localizedCaseInsensitiveContains(qc) { return true }
        }
        return false
    }

    private func belongs(_ r: OrderRow, _ f: OrderFilter, now: Int) -> Bool {
        switch f {
        case .all: return true
        case .main: return r.k == "m"
        case .pending: return r.pd && r.k == "m"
        case .overdue: return r.pd && r.k == "m" && r.alertLevel(now: now) == .overdue   // = tab Báo Đỏ trên PC
        case .soon: return r.pd && r.k == "m" && r.alertLevel(now: now) == .soon          // = tab Báo Vàng trên PC
        case .watch: return r.wl && r.k == "m"   // giống bảng Theo Dõi trên PC: chỉ tính bảng chính
        case .received: return r.rv && r.k == "m"   // = tab Đã nhận trên PC; đơn đã sang kho lưu trữ xem ở mục Lưu trữ
        case .cancelled: return r.isCancelled
        case .pre: return r.pre
        case .archived: return r.isArchived
        }
    }

    /// Một lần duyệt: danh sách đang hiển thị và số đơn của từng mục (theo ô tìm kiếm và sàn đang chọn).
    private func compute() -> (shown: [OrderRow], counts: [OrderFilter: Int]) {
        let now = Int(Date().timeIntervalSince1970 * 1000)
        let q = query.trimmingCharacters(in: .whitespaces)
        let qc = TrackingCode.clean(q)
        var counts: [OrderFilter: Int] = [:]
        var shown: [OrderRow] = []
        for r in store.rows {
            if !platform.isEmpty && r.p != platform { continue }
            if !matches(r, q, qc) { continue }
            for f in OrderFilter.allCases where belongs(r, f, now: now) { counts[f, default: 0] += 1 }
            if belongs(r, filter, now: now) { shown.append(r) }
        }
        return (shown, counts)
    }

    private var platforms: [String] { Array(Set(store.rows.map { $0.p }.filter { !$0.isEmpty })).sorted() }

    private func refresh() {
        let d = compute()
        shown = d.shown
        counts = d.counts
        scanned = engine.scannedReturnCodes
    }

    var body: some View {
        NavigationStack {
            VStack(spacing: 6) {
                summary(shown: shown.count)
                tables
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack {
                        ForEach(OrderFilter.allCases, id: \.self) { f in
                            Chip(title: "\(f.rawValue) (\(counts[f] ?? 0))", selected: filter == f) { filter = f }
                        }
                    }.padding(.horizontal)
                }
                List(shown) { r in
                    Button { selected = r } label: { row(r).card(padding: 10) }.buttonStyle(.plain)
                        .listRowSeparator(.hidden).listRowBackground(Color.clear)
                        .listRowInsets(EdgeInsets(top: 4, leading: 12, bottom: 4, trailing: 12))
                }
                .listStyle(.plain)
                .scrollContentBackground(.hidden)
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
            .background(Color(.systemGroupedBackground).ignoresSafeArea())
            .searchable(text: $query, prompt: "Tìm mã, sản phẩm, màu, size, ghi chú")
            .navigationTitle("Đơn hàng")
            .onAppear { refresh() }
            .onChange(of: query) { _ in refresh() }
            .onChange(of: filter) { _ in refresh() }
            .onChange(of: platform) { _ in refresh() }
            .onChange(of: store.updatedAt) { _ in refresh() }
            .onChange(of: store.rows.count) { _ in refresh() }
            .onChange(of: store.revision) { _ in refresh() }
            .onChange(of: engine.items.count) { _ in refresh() }
            .toolbar {
                ToolbarItemGroup(placement: .navigationBarTrailing) {
                    Button { showScanner = true } label: { Image(systemName: "qrcode.viewfinder") }
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
            .sheet(isPresented: $showScanner) {
                ScanSearchSheet { raw in
                    query = raw.trimmingCharacters(in: .whitespacesAndNewlines)
                    filter = .all
                    // Quét ra đúng một đơn thì mở luôn chi tiết.
                    refresh()
                    let again = shown
                    if again.count == 1 { selected = again[0] }
                }
            }
        }
    }

    /// Lối vào Bảng đơn mới (Hộp Đơn Mới trên PC) và Bảng đơn hủy: tích chọn để đẩy ra / đánh dấu đã nhận.
    private var tables: some View {
        let cancelledPending = store.rows.filter { $0.k == "c" && !$0.rv }.count
        let afterPrepPending = store.rows.filter { $0.k == "m" && !$0.rv && $0.isCancelAfterPrep }.count
        return HStack(spacing: 6) {
            NavigationLink { InboxView() } label: {
                VStack(spacing: 1) { Text("📥 Đơn mới").font(.caption.bold()); Text("\(store.inbox.count)").font(.subheadline.bold()) }
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.bordered).tint(store.inbox.isEmpty ? .gray : .blue)
            NavigationLink { CancelledTableView() } label: {
                VStack(spacing: 1) { Text("🚫 Đơn hủy").font(.caption.bold()); Text("chưa nhận \(cancelledPending)").font(.caption2.bold()) }
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.bordered).tint(cancelledPending == 0 ? .gray : .red)
            NavigationLink { CancelAfterPrepView() } label: {
                VStack(spacing: 1) { Text("📦 Hủy sau CB").font(.caption.bold()); Text("chưa nhận \(afterPrepPending)").font(.caption2.bold()) }
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.bordered).tint(afterPrepPending == 0 ? .gray : .purple)
        }
        .padding(.horizontal)
    }

    private func summary(shown: Int) -> some View {
        HStack {
            Text("Hiển thị \(shown) đơn · toàn bộ \(store.rows.count)").font(.caption.bold())
            Spacer()
            // Cùng cách tính với tab Báo Đỏ / Báo Vàng của bảng chính trên PC.
            Text("🚨 \(counts[.overdue] ?? 0)").font(.caption.bold()).foregroundColor(.red)
            Text("⏰ \(counts[.soon] ?? 0)").font(.caption.bold()).foregroundColor(.orange)
            if let t = store.updatedAt { Text(t.formatted(date: .omitted, time: .shortened)).font(.caption2).foregroundStyle(.secondary) }
        }.padding(.horizontal)
    }

    /// Danh sách chỉ hiện mã đơn hàng và/hoặc mã vận đơn; chạm vào mới xem đủ thông tin.
    private func row(_ r: OrderRow) -> some View {
        let now = Int(Date().timeIntervalSince1970 * 1000)
        let level = r.pd ? r.alertLevel(now: now) : .unknown
        let hasBoth = !r.o.isEmpty && r.o.uppercased() != r.c.uppercased()
        return HStack(spacing: 8) {
            Circle().fill(r.pd ? color(level) : Color.gray.opacity(0.4)).frame(width: 8, height: 8)
            VStack(alignment: .leading, spacing: 2) {
                if !r.o.isEmpty {
                    Text(hasBoth ? "Mã đơn: \(r.o)" : r.o).font(.system(.subheadline, design: .monospaced).bold())
                }
                if hasBoth || r.o.isEmpty {
                    Text(hasBoth ? "Vận đơn: \(r.c)" : r.c).font(.system(.subheadline, design: .monospaced)).foregroundColor(hasBoth ? .secondary : .primary)
                }
                // Đơn đang theo dõi: hiện nội dung theo dõi 1 dòng, dài quá thì cắt và thêm "…" ở cuối.
                if r.wl && !r.wr.isEmpty {
                    Text(r.wr).font(.footnote).foregroundColor(.orange).lineLimit(1).truncationMode(.tail)
                }
            }
            Spacer()
            if r.wl { Text("⭐").font(.caption) }
            if scanned.contains(r.c.uppercased()) { Text("✅ Đã quét").font(.caption2.bold()).foregroundColor(.green) }
            else if r.rv { Text("Đã nhận").font(.caption2.bold()).foregroundColor(.green) }
            if r.isCancelled { Text("Đơn hủy").font(.caption2.bold()).padding(.horizontal, 7).padding(.vertical, 2).background(Color.red.opacity(0.15)).foregroundColor(.red).clipShape(Capsule()) }
            if !r.p.isEmpty { Text(r.p == "Shopee" ? "Shopee" : (r.p.hasPrefix("TikTok") ? "TikTok" : r.p)).font(.caption2.bold()).padding(.horizontal, 7).padding(.vertical, 2).background((r.p == "Shopee" ? Color.orange : Color.primary).opacity(0.14)).foregroundColor(r.p == "Shopee" ? .orange : .primary).clipShape(Capsule()) }
        }
    }

    private func color(_ l: DeadlineLevel) -> Color {
        switch l { case .overdue: return .red; case .soon: return .orange; case .normal: return .green; case .unknown: return .secondary }
    }
}

/// Quét QR / mã vạch để điền vào ô tìm kiếm.
struct ScanSearchSheet: View {
    let onCode: (String) -> Void
    var title: String = "Quét để tìm đơn"
    var hint: String = "Đưa mã vạch hoặc QR vào khung để tìm đơn"
    @StateObject private var camera = CameraController()
    @Environment(\.dismiss) private var dismiss
    @State private var done = false

    var body: some View {
        NavigationStack {
            VStack(spacing: 12) {
                ZStack {
                    CameraPreview(controller: camera)
                    ScanGuide().stroke(Color.green.opacity(0.9), style: StrokeStyle(lineWidth: 3, lineCap: .round)).padding(14)
                }
                .frame(height: 345).background(Color.black)
                .clipShape(RoundedRectangle(cornerRadius: 14))
                .overlay(RoundedRectangle(cornerRadius: 14).stroke(Color.indigo, lineWidth: 2))
                Text(hint).font(.footnote).foregroundStyle(.secondary)
                Button { camera.setTorch(!camera.torchOn) } label: {
                    Label("Đèn", systemImage: camera.torchOn ? "flashlight.on.fill" : "flashlight.off.fill")
                }.buttonStyle(.borderedProminent).tint(.indigo)
                if camera.denied { Text("Chưa cho phép camera: vào Cài đặt iPhone để bật.").font(.footnote).foregroundColor(.red) }
                Spacer()
            }
            .padding()
            .navigationTitle(title)
            .toolbar { ToolbarItem(placement: .navigationBarTrailing) { Button("Đóng") { dismiss() } } }
            .onAppear {
                camera.onCode = { raw in
                    guard !done else { return }
                    done = true
                    camera.stop()
                    onCode(raw)
                    dismiss()
                }
                camera.start()
            }
            .onDisappear { camera.stop() }
        }
    }
}

struct DetailView: View {
    let row: OrderRow
    @Environment(\.dismiss) private var dismiss
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @State private var noteText = ""
    @State private var watchOn = false
    @State private var sentNote = false
    @State private var editN = ""
    @State private var editCo = ""
    @State private var editSz = ""
    @State private var editQ = 1
    @State private var editSt = ""
    @State private var editTy = ""
    @State private var sentEdit = false
    @State private var askDelete = false
    @State private var confirmDelete = false

    private static let statuses = ["Chưa nhận", "Hàng đã trả", "Không cần nhận", "Bị mất", "Hư hỏng"]
    private static let types = ["Giao không thành công", "Trả hàng/Hoàn tiền", "Đơn hủy", "THẤT THU"]
    private var statusChoices: [String] { Self.statuses.contains(editSt) || editSt.isEmpty ? Self.statuses : Self.statuses + [editSt] }
    private var typeChoices: [String] { Self.types.contains(editTy) || editTy.isEmpty ? Self.types : Self.types + [editTy] }

    private func fillEdit() {
        editN = live.n; editCo = live.co; editSz = live.sz; editQ = live.q; editSt = live.st; editTy = live.ty
    }

    /// Bản mới nhất của đơn (đổi ngay khi máy tính sửa theo dõi/ghi chú).
    private var live: OrderRow { store.rows.first(where: { $0.id == row.id }) ?? row }

    private var source: String {
        if live.isArchived { return "Kho lưu trữ" }
        return live.k == "c" ? "Bảng đơn hủy" : "Bảng chính"
    }

    var body: some View {
        NavigationStack {
            Form {
                Section("Mã") {
                    LabeledContent("Mã vận đơn", value: live.c)
                    if !live.o.isEmpty { LabeledContent("Mã đơn hàng", value: live.o) }
                    if !live.ps.isEmpty { LabeledContent("Trạng thái sàn", value: live.ps) }
                    Button("Sao chép mã vận đơn") { UIPasteboard.general.string = live.c }
                    if !live.o.isEmpty { Button("Sao chép mã đơn hàng") { UIPasteboard.general.string = live.o } }
                }
                Section("Theo dõi & ghi chú (đồng bộ 2 chiều với máy tính)") {
                    Toggle("Đang theo dõi", isOn: $watchOn)
                    TextField("Ghi chú / lý do theo dõi", text: $noteText, axis: .vertical).lineLimit(1...5)
                    Button(sentNote ? "✓ Đã gửi, đang đồng bộ với máy tính" : "Lưu & đồng bộ sang máy tính") {
                        engine.sendOrderNote(live, note: noteText, watch: watchOn)
                        sentNote = true
                    }
                    if live.k != "m" { Text("Chỉ đơn ở bảng chính mới đồng bộ theo dõi/ghi chú với máy tính.").font(.caption).foregroundStyle(.secondary) }
                    if !live.nt.isEmpty { VStack(alignment: .leading, spacing: 2) { Text("Ghi chú đơn trên máy tính").font(.caption).foregroundStyle(.secondary); Text(live.nt) } }
                }
                if live.k == "m" {
                    Section("Sửa thông tin đơn (đồng bộ 2 chiều với máy tính)") {
                        TextField("Sản phẩm", text: $editN, axis: .vertical).lineLimit(1...3)
                        TextField("Màu", text: $editCo)
                        TextField("Size", text: $editSz)
                        Stepper("Số lượng: \(editQ)", value: $editQ, in: 1...999)
                        Picker("Trạng thái", selection: $editSt) { ForEach(statusChoices, id: \.self) { Text($0).tag($0) } }
                        Picker("Loại đơn", selection: $editTy) { ForEach(typeChoices, id: \.self) { Text($0).tag($0) } }
                        Button(sentEdit ? "✓ Đã gửi, đang đồng bộ với máy tính" : "Lưu thay đổi sang máy tính") {
                            engine.sendOrderEdit(live, fields: ["n": editN, "co": editCo, "sz": editSz, "q": editQ, "st": editSt, "ty": editTy])
                            sentEdit = true
                        }
                        Button("Xóa đơn này", role: .destructive) { askDelete = true }
                    }
                }
                Section("Đơn") {
                    LabeledContent("Nằm ở", value: source)
                    LabeledContent("Sàn", value: live.p.isEmpty ? "—" : live.p)
                    if !live.ty.isEmpty { LabeledContent("Loại đơn", value: live.ty) }
                    if live.pre { LabeledContent("Hàng đặt trước", value: "Có") }
                    LabeledContent("Sản phẩm", value: live.n.isEmpty ? "—" : live.n)
                    LabeledContent("Màu", value: live.co.isEmpty ? "—" : live.co)
                    LabeledContent("Size", value: live.sz.isEmpty ? "—" : live.sz)
                    LabeledContent("Số lượng", value: "\(live.q)")
                    LabeledContent("Trạng thái", value: live.st.isEmpty ? "—" : live.st)
                    LabeledContent("Đã nhận hoàn kho", value: live.rv ? "Rồi" : "Chưa")
                }
                if live.pd {
                    Section("Hạn xử lý") {
                        LabeledContent("Ngày hủy/hoàn", value: live.rd.isEmpty ? "—" : live.rd)
                        LabeledContent("Hạn", value: live.due > 0 ? Date(timeIntervalSince1970: Double(live.due) / 1000).formatted(date: .abbreviated, time: .shortened) : "Chưa có")
                        LabeledContent("Còn lại", value: Deadline.text(due: live.due, now: Int(Date().timeIntervalSince1970 * 1000)))
                    }
                }
            }
            .onAppear { noteText = live.wr; watchOn = live.wl; fillEdit() }
            .onChange(of: live) { _ in fillEdit(); sentEdit = false }
            .confirmationDialog("Xóa đơn này?", isPresented: $askDelete, titleVisibility: .visible) {
                Button("Tiếp tục xóa", role: .destructive) { confirmDelete = true }
            } message: { Text("Đơn sẽ bị xóa khỏi bảng chính trên cả máy tính và điện thoại.") }
            .alert("Chắc chắn xóa đơn \(live.o.isEmpty ? live.c : live.o)?", isPresented: $confirmDelete) {
                Button("Hủy", role: .cancel) {}
                Button("Xóa", role: .destructive) { engine.sendOrderDelete(live); dismiss() }
            } message: { Text("Không thể hoàn tác trên điện thoại.") }
            .onChange(of: live.wr) { v in noteText = v; sentNote = false }
            .onChange(of: live.wl) { v in watchOn = v }
            .navigationTitle("Chi tiết đơn")
            .toolbar { ToolbarItem(placement: .navigationBarTrailing) { Button("Đóng") { dismiss() } } }
        }
    }
}
