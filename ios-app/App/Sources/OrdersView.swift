import SwiftUI
import ScannerCore

// Các mục đầu tiên khớp đúng các tab trên máy tính (chỉ tính BẢNG CHÍNH): Bảng chính = "Tất cả các tuần",
// Chưa nhận, Đã nhận, Quá hạn = Báo Đỏ, Sắp đến hạn = Báo Vàng, Hoàn một phần. Lưu trữ / Đơn hủy là các bảng riêng trên máy tính;
// "Toàn bộ" gồm cả 3 bảng (dùng khi tìm / quét mã).
enum OrderFilter: String, CaseIterable {
    case main = "Bảng chính"
    case pending = "Chưa nhận"
    case received = "Đã nhận"
    case overdue = "Quá hạn"
    case soon = "Sắp đến hạn"
    case partialReturn = "Hoàn một phần"
    case watch = "Đang theo dõi"
    case cancelled = "Đơn hủy"
    case archived = "Lưu trữ"
    case pre = "Đặt trước"
    case all = "Toàn bộ"
}

struct OrdersView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @EnvironmentObject var router: NotificationRouter
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
        for f in [r.c, r.o, r.x, r.n, r.co, r.sz, r.wr, r.nt] {
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
        case .partialReturn: return DashboardMetrics.isPartialReturn(r)
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
        let targetPlat = platform.isEmpty ? "" : DashboardMetrics.normalizedPlatform(platform)
        var counts: [OrderFilter: Int] = [:]
        var shown: [OrderRow] = []
        for r in store.rows {
            if !targetPlat.isEmpty && DashboardMetrics.normalizedPlatform(r.p) != targetPlat { continue }
            if !matches(r, q, qc) { continue }
            for f in OrderFilter.allCases where belongs(r, f, now: now) { counts[f, default: 0] += 1 }
            if belongs(r, filter, now: now) { shown.append(r) }
        }
        return (shown, counts)
    }

    private var platforms: [String] {
        let set = Set(store.rows.map { DashboardMetrics.normalizedPlatform($0.p) }.filter { !$0.isEmpty && $0 != "Khác" })
        return Array(set).sorted()
    }

    private func applyPendingNavigation() {
        var changed = false
        if let f = router.pendingOrdersFilter {
            filter = f
            router.pendingOrdersFilter = nil
            changed = true
        }
        if let p = router.pendingOrdersPlatform {
            platform = p
            router.pendingOrdersPlatform = nil
            changed = true
        }
        if changed { refresh() }
    }

    private func refresh() {
        let d = compute()
        shown = d.shown
        counts = d.counts
        scanned = engine.scannedReturnCodes
    }

    var body: some View {
        NavigationStack {
            VStack(spacing: 8) {
                summary(shown: shown.count)
                tables
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 8) {
                        ForEach(OrderFilter.allCases, id: \.self) { f in
                            Chip(title: "\(f.rawValue) (\(counts[f] ?? 0))", selected: filter == f) { filter = f }
                        }
                    }
                    .padding(.horizontal, 14)
                }
                if !platform.isEmpty {
                    HStack(spacing: 8) {
                        Text("Đang lọc sàn:")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                        PlatformBadge(platform: platform)
                        Spacer()
                        Button("Bỏ lọc sàn") {
                            Haptics.tap()
                            platform = ""
                        }
                        .font(.caption.bold())
                        .foregroundColor(Theme.accent)
                    }
                    .padding(.horizontal, 16)
                }
                List(shown) { r in
                    Button {
                        Haptics.tap()
                        selected = r
                    } label: {
                        row(r).card(padding: 12, radius: 16)
                    }
                    .buttonStyle(.plain)
                    .listRowSeparator(.hidden)
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets(top: 4, leading: 14, bottom: 4, trailing: 14))
                }
                .listStyle(.plain)
                .scrollContentBackground(.hidden)
                .refreshable {
                    Haptics.tap()
                    engine.requestSnapshot()
                    try? await Task.sleep(nanoseconds: 2_000_000_000)
                }
                .overlay {
                    if !store.hasData {
                        VStack(spacing: 8) {
                            Image(systemName: "shippingbox")
                                .font(.largeTitle)
                                .foregroundStyle(.secondary)
                            Text("Chưa nhận dữ liệu đơn từ máy tính").font(.headline)
                            Text("Mở app trên PC và kéo xuống để cập nhật.").font(.footnote).foregroundStyle(.secondary)
                        }
                        .padding()
                    } else if shown.isEmpty {
                        VStack(spacing: 6) {
                            Image(systemName: "magnifyingglass")
                                .font(.title2)
                                .foregroundStyle(.secondary)
                            Text("Không có đơn phù hợp").font(.subheadline).foregroundStyle(.secondary)
                        }
                    }
                }
            }
            .background(Theme.pageBackground.ignoresSafeArea())
            .searchable(text: $query, prompt: "Tìm mã, sản phẩm, màu, size, ghi chú")
            .navigationTitle("Đơn hàng")
            .onAppear {
                applyPendingNavigation()
                refresh()
            }
            .onChange(of: router.pendingOrdersFilter) { _ in applyPendingNavigation() }
            .onChange(of: router.pendingOrdersPlatform) { _ in applyPendingNavigation() }
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
                    Button {
                        Haptics.tap()
                        engine.requestSnapshot()
                    } label: { Image(systemName: "arrow.clockwise") }
                }
            }
            .sheet(item: $selected) { DetailView(row: $0) }
            .sheet(isPresented: $showScanner) {
                ScanSearchSheet { raw in
                    query = raw.trimmingCharacters(in: .whitespacesAndNewlines)
                    filter = .all
                    refresh()
                    let again = shown
                    if again.count == 1 { selected = again[0] }
                }
            }
        }
    }

    /// Lối vào nhanh các bảng phụ: Đơn hủy, Hủy sau chuẩn bị, Hàng hoàn về.
    private var tables: some View {
        let cancelledPending = store.rows.filter { $0.k == "c" && !$0.rv }.count
        let afterPrepPending = store.rows.filter { $0.k == "m" && !$0.rv && $0.isCancelAfterPrep }.count
        let returnWatchActive = store.rows.filter { $0.inReturnWatch && $0.returnWatchStage < 4 }.count
        return HStack(spacing: 8) {
            NavigationLink { CancelledTableView() } label: {
                quickTableCard(icon: "xmark.octagon.fill", title: "Đơn hủy", count: cancelledPending, sub: "chưa nhận", color: Theme.danger)
            }
            .buttonStyle(.plain)

            NavigationLink { CancelAfterPrepView() } label: {
                quickTableCard(icon: "shippingbox.and.arrow.backward.fill", title: "Hủy sau CB", count: afterPrepPending, sub: "chưa nhận", color: .purple)
            }
            .buttonStyle(.plain)

            NavigationLink { ReturnWatchView() } label: {
                quickTableCard(icon: "arrow.uturn.backward.circle.fill", title: "Hoàn về", count: returnWatchActive, sub: "đang về", color: Theme.teal)
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 14)
    }

    private func quickTableCard(icon: String, title: String, count: Int, sub: String, color: Color) -> some View {
        let activeColor = count > 0 ? color : .secondary
        return VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 4) {
                Image(systemName: icon)
                    .font(.caption2.bold())
                    .foregroundColor(activeColor)
                Text(title)
                    .font(.caption.bold())
                    .foregroundColor(.primary)
                    .lineLimit(1)
            }
            HStack(alignment: .firstTextBaseline, spacing: 3) {
                Text("\(count)")
                    .font(.system(size: 18, weight: .bold, design: .rounded))
                    .foregroundColor(activeColor)
                Text(sub)
                    .font(.caption2)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
        }
        .frame(maxWidth: .infinity, minHeight: 54, alignment: .leading)
        .card(padding: 10, radius: 14)
    }

    private func summary(shown: Int) -> some View {
        HStack(spacing: 10) {
            Text("Hiển thị \(shown) · tổng \(store.rows.count)")
                .font(.caption.bold())
                .foregroundStyle(.secondary)
            Spacer()
            HStack(spacing: 4) {
                Circle().fill(Theme.danger).frame(width: 7, height: 7)
                Text("\(counts[.overdue] ?? 0)").font(.caption.bold()).foregroundColor(Theme.danger)
            }
            HStack(spacing: 4) {
                Circle().fill(Theme.warning).frame(width: 7, height: 7)
                Text("\(counts[.soon] ?? 0)").font(.caption.bold()).foregroundColor(Theme.warning)
            }
            if let t = store.updatedAt {
                Text(t.formatted(date: .omitted, time: .shortened))
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            }
        }
        .padding(.horizontal, 16)
    }

    /// Hàng đơn dạng thẻ: chấm cảnh báo, mã vận đơn/đơn hàng, huy hiệu sàn, sản phẩm (tô màu nếu hoàn một phần), hạn.
    private func row(_ r: OrderRow) -> some View {
        let now = Int(Date().timeIntervalSince1970 * 1000)
        let level = r.pd ? r.alertLevel(now: now) : .unknown
        let hasBoth = !r.o.isEmpty && r.o.uppercased() != r.c.uppercased()
        let isPartial = DashboardMetrics.isPartialReturn(r)

        return VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .center, spacing: 8) {
                Circle()
                    .fill(r.pd ? color(level) : (r.rv ? Theme.success : Color.gray.opacity(0.4)))
                    .frame(width: 10, height: 10)

                VStack(alignment: .leading, spacing: 2) {
                    Text(r.o.isEmpty ? r.c : r.o)
                        .font(.system(.subheadline, design: .monospaced).bold())
                        .foregroundColor(.primary)
                        .lineLimit(1)
                    if hasBoth {
                        Text("Vận đơn: \(r.c)")
                            .font(.system(.caption, design: .monospaced))
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                }

                Spacer(minLength: 4)

                if r.wl {
                    Image(systemName: "star.fill")
                        .font(.caption)
                        .foregroundColor(Theme.warning)
                }
                if !r.p.isEmpty {
                    PlatformBadge(platform: r.p)
                }
            }

            if !r.n.isEmpty {
                if isPartial {
                    ProductLines(n: r.n, rn: r.rn)
                } else {
                    Text(r.n)
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                }
            }

            if r.wl && !r.wr.isEmpty {
                HStack(spacing: 4) {
                    Image(systemName: "pin.fill").font(.caption2)
                    Text(r.wr).font(.caption).lineLimit(1).truncationMode(.tail)
                }
                .foregroundColor(Theme.warning)
            }

            HStack(spacing: 6) {
                if scanned.contains(r.c.uppercased()) {
                    StatusPill(text: "Đã quét", color: Theme.success)
                } else if r.rv {
                    StatusPill(text: "Đã nhận", color: Theme.success)
                } else if r.isCancelled {
                    StatusPill(text: "Đơn hủy", color: Theme.danger)
                } else if !r.st.isEmpty {
                    StatusPill(text: r.st, color: r.pd ? Theme.info : .secondary)
                }

                if isPartial {
                    StatusPill(text: "Hoàn 1 phần", color: Theme.accent)
                }

                Spacer(minLength: 4)

                if r.pd && (level == .overdue || level == .soon) {
                    StatusPill(text: Deadline.text(due: r.due, now: now), color: color(level))
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func color(_ l: DeadlineLevel) -> Color {
        switch l {
        case .overdue: return Theme.danger
        case .soon: return Theme.warning
        case .normal: return Theme.success
        case .unknown: return .secondary
        }
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
                .clipShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 18, style: .continuous).stroke(Theme.accent, lineWidth: 2.5))
                Text(hint).font(.footnote).foregroundStyle(.secondary)
                Button { camera.setTorch(!camera.torchOn) } label: {
                    Label("Đèn", systemImage: camera.torchOn ? "flashlight.on.fill" : "flashlight.off.fill")
                        .frame(minHeight: 44)
                        .padding(.horizontal, 14)
                }
                .buttonStyle(.borderedProminent)
                .tint(Theme.accent)
                if camera.denied { Text("Chưa cho phép camera: vào Cài đặt iPhone để bật.").font(.footnote).foregroundColor(Theme.danger) }
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

    // v2: PC chỉ nhận 4 trạng thái này ("Hư hỏng" không có ở v2); "Chưa nhận" PC hiểu là "Chưa trả".
    private static let statuses = ["Chưa nhận", "Hàng đã trả", "Không cần nhận", "Bị mất"]
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
                    Button("Sao chép mã vận đơn") { Haptics.tap(); UIPasteboard.general.string = live.c }
                    if !live.o.isEmpty { Button("Sao chép mã đơn hàng") { Haptics.tap(); UIPasteboard.general.string = live.o } }
                }
                Section("Theo dõi & ghi chú (đồng bộ 2 chiều với máy tính)") {
                    Toggle("Đang theo dõi", isOn: $watchOn)
                    TextField("Ghi chú / lý do theo dõi", text: $noteText, axis: .vertical).lineLimit(1...5)
                    Button(sentNote ? "✓ Đã gửi, đang đồng bộ với máy tính" : "Lưu & đồng bộ sang máy tính") {
                        Haptics.medium()
                        engine.sendOrderNote(live, note: noteText, watch: watchOn)
                        sentNote = true
                    }
                    if live.k != "m" { Text("Chỉ đơn ở bảng chính mới đồng bộ theo dõi/ghi chú với máy tính.").font(.caption).foregroundStyle(.secondary) }
                    if !live.nt.isEmpty { VStack(alignment: .leading, spacing: 2) { Text("Ghi chú đơn trên máy tính").font(.caption).foregroundStyle(.secondary); Text(live.nt) } }
                }
                if live.k == "m" {
                    Section("Đổi trạng thái nhận kho (đồng bộ với máy tính)") {
                        // v2: PC chỉ cho đổi TRẠNG THÁI từ điện thoại; sửa sản phẩm/màu/size/số lượng/loại đơn làm trên máy tính.
                        Picker("Trạng thái", selection: $editSt) { ForEach(statusChoices, id: \.self) { Text($0).tag($0) } }
                        Button(sentEdit ? "✓ Đã gửi, đang đồng bộ với máy tính" : "Lưu trạng thái sang máy tính") {
                            Haptics.medium()
                            engine.sendOrderEdit(live, fields: ["st": editSt])
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
                    VStack(alignment: .leading, spacing: 4) {
                        Text("Sản phẩm").font(.caption).foregroundStyle(.secondary)
                        if live.n.isEmpty { Text("—") } else { ProductLines(n: live.n, rn: live.rn) }
                    }
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
            .tint(Theme.accent)
            .onAppear { noteText = live.wr; watchOn = live.wl; fillEdit() }
            .onChange(of: live) { _ in fillEdit(); sentEdit = false }
            .confirmationDialog("Xóa đơn này?", isPresented: $askDelete, titleVisibility: .visible) {
                Button("Tiếp tục xóa", role: .destructive) { confirmDelete = true }
            } message: { Text("Đơn vẫn khôi phục được trên máy tính.") }
            .alert("Chắc chắn xóa đơn \(live.o.isEmpty ? live.c : live.o)?", isPresented: $confirmDelete) {
                Button("Hủy", role: .cancel) {}
                Button("Xóa", role: .destructive) { engine.sendOrderDelete(live); dismiss() }
            } message: { Text("Đơn sẽ bị gỡ khỏi bảng chính; có thể khôi phục trên máy tính.") }
            .onChange(of: live.wr) { v in noteText = v; sentNote = false }
            .onChange(of: live.wl) { v in watchOn = v }
            .navigationTitle("Chi tiết đơn")
            .toolbar { ToolbarItem(placement: .navigationBarTrailing) { Button("Đóng") { dismiss() } } }
        }
    }
}
