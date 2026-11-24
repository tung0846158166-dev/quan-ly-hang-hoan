import SwiftUI
import ScannerCore

enum OrderFilter: String, CaseIterable {
    case main = "Tất cả"
    case pending = "Chưa nhận"
    case overdue = "Quá hạn"
    case soon = "Sắp hạn"
    case partialReturn = "Hoàn 1 phần"
    case watch = "Theo dõi"
    case received = "Đã nhận"
    case cancelled = "Đơn hủy"
    case archived = "Lưu trữ"
    case pre = "Đặt trước"
    case all = "Toàn bộ"

    var accentColor: Color {
        switch self {
        case .overdue: return Theme.danger
        case .soon, .watch: return Theme.warning
        case .received: return Theme.success
        case .partialReturn: return Theme.purple
        default: return Theme.accent
        }
    }
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
    @State private var shown: [OrderRow] = []
    @State private var counts: [OrderFilter: Int] = [:]
    @State private var scanned: Set<String> = []
    @State private var productCount = 0

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
        case .overdue: return r.pd && r.k == "m" && r.alertLevel(now: now) == .overdue
        case .soon: return r.pd && r.k == "m" && r.alertLevel(now: now) == .soon
        case .partialReturn: return DashboardMetrics.isPartialReturn(r)
        case .watch: return r.wl && r.k == "m"
        case .received: return r.rv && r.k == "m"
        case .cancelled: return r.isCancelled
        case .pre: return r.pre
        case .archived: return r.isArchived
        }
    }

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
        productCount = ProductMetrics.compute(rows: store.rows).products.count
    }

    var body: some View {
        NavigationStack {
            VStack(spacing: 10) {
                if store.hasData {
                    shortcutRow
                    filterBar
                    if !platform.isEmpty {
                        activePlatformStrip
                    }
                }

                List(shown) { r in
                    Button {
                        Haptics.tap()
                        selected = r
                    } label: {
                        orderCard(r)
                    }
                    .buttonStyle(.plain)
                    .listRowSeparator(.hidden)
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets(top: 5, leading: 16, bottom: 5, trailing: 16))
                }
                .listStyle(.plain)
                .scrollContentBackground(.hidden)
                .refreshable {
                    guard !store.isDemoMode else { return }
                    Haptics.tap()
                    engine.requestSnapshot()
                    try? await Task.sleep(nanoseconds: 1_500_000_000)
                }
                .overlay {
                    if !store.hasData {
                        emptyOrdersView
                    } else if shown.isEmpty {
                        VStack(spacing: 8) {
                            Image(systemName: "magnifyingglass")
                                .font(.title2)
                                .foregroundStyle(.secondary)
                            Text("Không có đơn phù hợp")
                                .font(.subheadline.weight(.medium))
                                .foregroundStyle(.secondary)
                        }
                    }
                }
            }
            .background(Theme.pageBackground.ignoresSafeArea())
            .searchable(text: $query, prompt: "Tìm mã vận đơn, mã đơn, sản phẩm...")
            .navigationTitle("Đơn hàng")
            .navigationBarTitleDisplayMode(.large)
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
            .onChange(of: store.isDemoMode) { _ in refresh() }
            .onChange(of: engine.items.count) { _ in refresh() }
            .toolbar {
                ToolbarItemGroup(placement: .navigationBarTrailing) {
                    Button {
                        Haptics.tap()
                        showScanner = true
                    } label: {
                        Image(systemName: "qrcode.viewfinder")
                    }
                    .accessibilityLabel("Quét mã để tìm đơn")

                    Menu {
                        Button("Tất cả sàn") { platform = "" }
                        ForEach(platforms, id: \.self) { p in Button(p) { platform = p } }
                    } label: {
                        Image(systemName: platform.isEmpty ? "line.3.horizontal.decrease.circle" : "line.3.horizontal.decrease.circle.fill")
                            .foregroundColor(platform.isEmpty ? .primary : Theme.accent)
                    }
                    .accessibilityLabel("Lọc theo sàn")

                    if !store.isDemoMode {
                        Button {
                            Haptics.tap()
                            engine.requestSnapshot()
                        } label: {
                            Image(systemName: "arrow.clockwise")
                        }
                        .accessibilityLabel("Cập nhật danh sách đơn")
                    }
                }
            }
            .sheet(item: $selected) { DetailView(row: $0) }
            .sheet(isPresented: $showScanner) {
                ScanSearchSheet { raw in
                    query = raw.trimmingCharacters(in: .whitespacesAndNewlines)
                    filter = .all
                    refresh()
                    if shown.count == 1 { selected = shown[0] }
                }
            }
        }
    }

    // MARK: - Lối tắt bảng phụ & Thống kê sản phẩm (biểu tượng + số, gọn)
    private var shortcutRow: some View {
        let returnWatchActive = store.rows.filter { $0.inReturnWatch && $0.returnWatchStage < 4 }.count
        let cancelledPending = store.rows.filter { $0.k == "c" && !$0.rv }.count
        let afterPrepPending = store.rows.filter { $0.k == "m" && !$0.rv && $0.isCancelAfterPrep }.count

        return ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 10) {
                NavigationLink { ReturnWatchView() } label: {
                    shortcutPill(icon: "arrow.uturn.backward", title: "Hoàn về", count: returnWatchActive, color: Theme.teal)
                }
                .buttonStyle(.plain)

                NavigationLink { ProductStatsView() } label: {
                    shortcutPill(icon: "tshirt.fill", title: "Sản phẩm", count: productCount, color: Theme.accent)
                }
                .buttonStyle(.plain)

                NavigationLink { CancelledTableView() } label: {
                    shortcutPill(icon: "xmark", title: "Đơn hủy", count: cancelledPending, color: Theme.danger)
                }
                .buttonStyle(.plain)

                NavigationLink { CancelAfterPrepView() } label: {
                    shortcutPill(icon: "shippingbox.fill", title: "Sau CB", count: afterPrepPending, color: Theme.purple)
                }
                .buttonStyle(.plain)
            }
            .padding(.horizontal, 16)
        }
    }

    private func shortcutPill(icon: String, title: String, count: Int, color: Color) -> some View {
        HStack(spacing: 8) {
            IconTile(systemName: icon, color: count > 0 ? color : .secondary, size: 28, cornerRadius: 8)
            VStack(alignment: .leading, spacing: 0) {
                Text("\(count)")
                    .font(.system(size: 16, weight: .bold, design: .rounded))
                    .foregroundColor(count > 0 ? .primary : .secondary)
                    .monospacedDigit()
                Text(title)
                    .font(.caption2.weight(.medium))
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
        }
        .frame(minWidth: 102, minHeight: 46, alignment: .leading)
        .card(padding: 8, radius: 14)
    }

    // MARK: - Hàng ô lọc cuộn ngang có số đếm
    private var filterBar: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 8) {
                ForEach(OrderFilter.allCases, id: \.self) { f in
                    FilterTile(
                        title: f.rawValue,
                        count: counts[f] ?? 0,
                        selected: filter == f,
                        accent: f.accentColor
                    ) {
                        filter = f
                    }
                }
            }
            .padding(.horizontal, 16)
        }
    }

    private var activePlatformStrip: some View {
        HStack(spacing: 8) {
            Text("Đang lọc sàn:")
                .font(.caption)
                .foregroundStyle(.secondary)
            PlatformBadge(platform: platform)
            Spacer()
            Button("Bỏ lọc") {
                Haptics.tap()
                platform = ""
            }
            .font(.caption.bold())
            .foregroundColor(Theme.accent)
        }
        .padding(.horizontal, 18)
    }

    // MARK: - Thẻ đơn hàng rộng rãi
    private func orderCard(_ r: OrderRow) -> some View {
        let now = Int(Date().timeIntervalSince1970 * 1000)
        let level = r.pd ? r.alertLevel(now: now) : .unknown
        let hasBoth = !r.o.isEmpty && r.o.uppercased() != r.c.uppercased()
        let isPartial = DashboardMetrics.isPartialReturn(r)

        return VStack(alignment: .leading, spacing: 10) {
            // Dòng trên: huy hiệu sàn + hạn tô màu
            HStack(spacing: 8) {
                if !r.p.isEmpty { PlatformBadge(platform: r.p) }
                if r.wl {
                    Image(systemName: "star.fill")
                        .font(.caption)
                        .foregroundColor(Theme.warning)
                }
                Spacer(minLength: 4)
                if r.pd && r.due > 0 {
                    Text(Deadline.text(due: r.due, now: now))
                        .font(.caption.bold())
                        .foregroundColor(deadlineColor(level))
                } else if r.rv {
                    Text("Đã nhận")
                        .font(.caption.bold())
                        .foregroundColor(Theme.success)
                }
            }

            // Mã vận đơn đậm
            VStack(alignment: .leading, spacing: 2) {
                Text(r.c.isEmpty ? r.o : r.c)
                    .font(.system(.headline, design: .monospaced).bold())
                    .foregroundColor(.primary)
                    .lineLimit(1)
                if hasBoth {
                    Text("Mã đơn: \(r.o)")
                        .font(.system(.caption, design: .monospaced))
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }

            // Sản phẩm (hoàn một phần giữ tô màu + nhãn)
            if !r.n.isEmpty {
                if isPartial {
                    ProductLines(n: r.n, rn: r.rn)
                } else {
                    Text(r.n)
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                }
            }

            if r.wl && !r.wr.isEmpty {
                HStack(spacing: 5) {
                    Image(systemName: "pin.fill").font(.caption2)
                    Text(r.wr).font(.caption).lineLimit(1)
                }
                .foregroundColor(Theme.warning)
            }

            // Chân thẻ: chặng hàng hoàn về hoặc trạng thái
            HStack(spacing: 8) {
                if r.inReturnWatch {
                    let st = r.returnWatchStage
                    HStack(spacing: 4) {
                        ForEach(1...4, id: \.self) { k in
                            Circle()
                                .fill(k <= st ? stageColor(st) : Color.gray.opacity(0.25))
                                .frame(width: 6, height: 6)
                        }
                        Text(OrderRow.returnWatchStageNames[st])
                            .font(.caption2.bold())
                            .foregroundColor(stageColor(st))
                    }
                } else if scanned.contains(r.c.uppercased()) {
                    StatusPill(text: "Đã quét", color: Theme.success)
                } else if r.rv {
                    StatusPill(text: "Đã nhận kho", color: Theme.success)
                } else if r.isCancelled {
                    StatusPill(text: "Đơn hủy", color: Theme.danger)
                } else if !r.st.isEmpty {
                    StatusPill(text: r.st, color: r.pd ? Theme.info : .secondary)
                }

                if isPartial {
                    StatusPill(text: "Hoàn 1 phần", color: Theme.purple)
                }

                Spacer(minLength: 0)

                if !r.co.isEmpty || !r.sz.isEmpty {
                    Text([r.co, r.sz].filter { !$0.isEmpty }.joined(separator: " · "))
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(padding: 15, radius: 18)
    }

    private var emptyOrdersView: some View {
        VStack(spacing: 14) {
            IconTile(systemName: "shippingbox.fill", color: Theme.accent, size: 52, cornerRadius: 15)
            Text("Chưa có dữ liệu đơn")
                .font(.headline.bold())
            Text("Kết nối máy tính ở Cài đặt hoặc bật chế độ xem thử để khám phá giao diện.")
                .font(.footnote)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .padding(.horizontal, 24)
            Button {
                Haptics.medium()
                engine.setDemoMode(true)
            } label: {
                Label("Xem thử với dữ liệu mẫu", systemImage: "sparkles")
                    .font(.subheadline.bold())
                    .padding(.horizontal, 18)
                    .frame(minHeight: 44)
            }
            .buttonStyle(.borderedProminent)
            .tint(Theme.accent)
        }
        .padding(24)
    }

    private func deadlineColor(_ l: DeadlineLevel) -> Color {
        switch l {
        case .overdue: return Theme.danger
        case .soon: return Theme.warning
        case .normal: return Theme.success
        case .unknown: return .secondary
        }
    }

    private func stageColor(_ stage: Int) -> Color {
        switch stage {
        case 1: return Theme.info
        case 2: return Theme.teal
        case 3: return Theme.warning
        default: return Theme.success
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
