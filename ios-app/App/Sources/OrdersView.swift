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
            VStack(spacing: 4) {
                shortcutRow
                if store.hasData {
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
                    .listRowInsets(EdgeInsets(top: 4, leading: Theme.screenPadding, bottom: 4, trailing: Theme.screenPadding))
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
                        VStack(spacing: 6) {
                            Image(systemName: "magnifyingglass")
                                .font(.title3)
                                .foregroundStyle(.secondary)
                            Text("Không có đơn phù hợp")
                                .font(Theme.Typography.secondary)
                                .foregroundStyle(.secondary)
                        }
                    }
                }
            }
            .background(Theme.pageBackground.ignoresSafeArea())
            .searchable(text: $query, prompt: "Tìm mã vận đơn, mã đơn, sản phẩm...")
            .navigationTitle("Đơn hàng")
            .navigationBarTitleDisplayMode(.inline)
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

    // MARK: - Hàng chip lối tắt nhỏ gọn (Hoàn về / Sản phẩm / Đơn hủy / Sau CB)
    private var shortcutRow: some View {
        let returnWatchActive = store.rows.filter { $0.inReturnWatch && $0.returnWatchStage < 4 }.count
        let cancelledPending = store.rows.filter { $0.k == "c" && !$0.rv }.count
        let afterPrepPending = store.rows.filter { $0.k == "m" && !$0.rv && $0.isCancelAfterPrep }.count

        return ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 6) {
                NavigationLink { ReturnWatchView() } label: {
                    shortcutChip(icon: "arrow.uturn.backward", title: "Hoàn về", count: returnWatchActive, color: Theme.teal)
                }
                .buttonStyle(.plain)

                NavigationLink { ProductStatsView() } label: {
                    shortcutChip(icon: "tshirt.fill", title: "Sản phẩm", count: productCount, color: Theme.accent, alwaysTint: true)
                }
                .buttonStyle(.plain)

                NavigationLink { CancelledTableView() } label: {
                    shortcutChip(icon: "xmark", title: "Đơn hủy", count: cancelledPending, color: Theme.danger)
                }
                .buttonStyle(.plain)

                NavigationLink { CancelAfterPrepView() } label: {
                    shortcutChip(icon: "shippingbox.fill", title: "Sau CB", count: afterPrepPending, color: Theme.purple)
                }
                .buttonStyle(.plain)
            }
            .padding(.horizontal, Theme.screenPadding)
        }
    }

    private func shortcutChip(icon: String, title: String, count: Int, color: Color, alwaysTint: Bool = false) -> some View {
        let active = count > 0 || alwaysTint
        return HStack(spacing: 5) {
            Image(systemName: icon)
                .font(.system(size: 11, weight: .bold))
                .foregroundColor(active ? color : .secondary)
            Text(title)
                .font(Theme.Typography.captionBold)
                .foregroundColor(.primary)
                .lineLimit(1)
            Text("\(count)")
                .font(.system(.caption2, design: .rounded).bold())
                .monospacedDigit()
                .foregroundColor(active ? color : .secondary)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .background(active ? color.opacity(0.11) : Theme.cardBackground)
        .clipShape(Capsule())
        .overlay(
            Capsule()
                .stroke(active ? color.opacity(0.26) : Color.primary.opacity(0.08), lineWidth: 0.7)
        )
        .frame(minHeight: Theme.minTapTarget)
        .contentShape(Rectangle())
    }

    // MARK: - Hàng ô lọc cuộn ngang có số đếm
    private var filterBar: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 6) {
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
            .padding(.horizontal, Theme.screenPadding)
        }
    }

    private var activePlatformStrip: some View {
        HStack(spacing: 8) {
            Text("Đang lọc sàn:")
                .font(Theme.Typography.caption)
                .foregroundStyle(.secondary)
            PlatformBadge(platform: platform)
            Spacer()
            Button("Bỏ lọc") {
                Haptics.tap()
                platform = ""
            }
            .font(Theme.Typography.captionBold)
            .foregroundColor(Theme.accent)
        }
        .padding(.horizontal, Theme.screenPadding)
    }

    // MARK: - Hàng đơn gọn 3 dòng
    private func orderCard(_ r: OrderRow) -> some View {
        let now = Int(Date().timeIntervalSince1970 * 1000)
        let level = r.pd ? r.alertLevel(now: now) : .unknown
        let isPartial = DashboardMetrics.isPartialReturn(r)
        let variantText = [r.co, r.sz].filter { !$0.isEmpty }.joined(separator: " · ")

        return VStack(alignment: .leading, spacing: 5) {
            // Dòng 1: huy hiệu sàn nhỏ + mã vận đơn đậm + hạn màu bên phải
            HStack(spacing: 6) {
                if !r.p.isEmpty { PlatformBadge(platform: r.p) }
                Text(r.c.isEmpty ? r.o : r.c)
                    .font(Theme.Typography.code)
                    .foregroundColor(.primary)
                    .lineLimit(1)
                if r.wl {
                    Image(systemName: "star.fill")
                        .font(.system(size: 10))
                        .foregroundColor(Theme.warning)
                }
                Spacer(minLength: 4)
                if r.pd && r.due > 0 {
                    Text(Deadline.text(due: r.due, now: now))
                        .font(Theme.Typography.microBold)
                        .foregroundColor(deadlineColor(level))
                        .lineLimit(1)
                } else if r.rv {
                    Text("Đã nhận")
                        .font(Theme.Typography.microBold)
                        .foregroundColor(Theme.success)
                }
            }

            // Dòng 2: sản phẩm 1 dòng (hoàn một phần chỉ hiện chip "Hoàn K/N" + tên món đã hoàn tô màu)
            if !r.n.isEmpty {
                if isPartial {
                    let lines = ProductMetrics.productLines(for: r)
                    let returnedNames = lines.filter { $0.isReturned }.map { $0.name }
                    let returnedText = returnedNames.isEmpty ? r.rn : returnedNames.joined(separator: ", ")
                    HStack(spacing: 6) {
                        StatusPill(
                            text: "Hoàn \(max(1, returnedNames.count))/\(max(1, lines.count))",
                            color: Theme.accent
                        )
                        Text(returnedText)
                            .font(Theme.Typography.secondaryBold)
                            .foregroundColor(Theme.accent)
                            .lineLimit(1)
                            .truncationMode(.tail)
                    }
                } else {
                    HStack(spacing: 6) {
                        Text(r.n)
                            .font(Theme.Typography.secondary)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                            .truncationMode(.tail)
                        if !variantText.isEmpty {
                            Text("· \(variantText)")
                                .font(Theme.Typography.caption)
                                .foregroundStyle(.tertiary)
                                .lineLimit(1)
                        }
                    }
                }
            }

            // Dòng 3: chấm tiến trình / trạng thái + ghi chú rút gọn 1 dòng có biểu tượng ghim
            HStack(spacing: 8) {
                statusProgressIndicator(r)

                if !r.wr.isEmpty {
                    HStack(spacing: 3) {
                        Image(systemName: "pin.fill")
                            .font(.system(size: 9))
                        Text(r.wr)
                            .font(Theme.Typography.micro)
                            .lineLimit(1)
                            .truncationMode(.tail)
                    }
                    .foregroundColor(Theme.warning)
                }

                Spacer(minLength: 0)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(padding: Theme.cardPadding, radius: Theme.cardRadius)
    }

    @ViewBuilder
    private func statusProgressIndicator(_ r: OrderRow) -> some View {
        if r.inReturnWatch {
            let st = r.returnWatchStage
            let color = stageColor(st)
            HStack(spacing: 3) {
                ForEach(1...4, id: \.self) { k in
                    Circle()
                        .fill(k <= st ? color : Color.gray.opacity(0.25))
                        .frame(width: 5, height: 5)
                }
                Text(OrderRow.returnWatchStageNames[st])
                    .font(Theme.Typography.microBold)
                    .foregroundColor(color)
                    .lineLimit(1)
            }
        } else if scanned.contains(r.c.uppercased()) {
            dotStatus("Đã quét", Theme.success)
        } else if r.rv {
            dotStatus("Đã nhận kho", Theme.success)
        } else if r.isCancelled {
            dotStatus("Đơn hủy", Theme.danger)
        } else if !r.st.isEmpty {
            dotStatus(r.st, r.pd ? Theme.info : .secondary)
        } else {
            dotStatus(r.pd ? "Chưa nhận" : "Đã xử lý", r.pd ? Theme.info : .secondary)
        }
    }

    private func dotStatus(_ text: String, _ color: Color) -> some View {
        HStack(spacing: 4) {
            Circle()
                .fill(color)
                .frame(width: 6, height: 6)
            Text(text)
                .font(Theme.Typography.microBold)
                .foregroundColor(color)
                .lineLimit(1)
        }
    }

    private var emptyOrdersView: some View {
        VStack(spacing: 12) {
            IconTile(systemName: "shippingbox.fill", color: Theme.accent, size: 46, cornerRadius: 13)
            Text("Chưa có dữ liệu đơn")
                .font(Theme.Typography.navTitle)
            Text("Kết nối máy tính ở Cài đặt hoặc bật chế độ xem thử để khám phá giao diện.")
                .font(Theme.Typography.caption)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .padding(.horizontal, 24)
            Button {
                Haptics.medium()
                engine.setDemoMode(true)
            } label: {
                Label("Xem thử với dữ liệu mẫu", systemImage: "sparkles")
                    .font(Theme.Typography.secondaryBold)
                    .padding(.horizontal, 16)
                    .frame(minHeight: Theme.minTapTarget)
            }
            .buttonStyle(.borderedProminent)
            .tint(Theme.accent)
        }
        .padding(20)
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
            VStack(spacing: 10) {
                ZStack {
                    CameraPreview(controller: camera)
                    ScanGuide().stroke(Color.green.opacity(0.9), style: StrokeStyle(lineWidth: 3, lineCap: .round)).padding(14)
                }
                .frame(height: 300).background(Color.black)
                .clipShape(RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous).stroke(Theme.accent, lineWidth: 2))
                Text(hint).font(Theme.Typography.caption).foregroundStyle(.secondary)
                Button { camera.setTorch(!camera.torchOn) } label: {
                    Label("Đèn", systemImage: camera.torchOn ? "flashlight.on.fill" : "flashlight.off.fill")
                        .frame(minHeight: Theme.minTapTarget)
                        .padding(.horizontal, 14)
                }
                .buttonStyle(.borderedProminent)
                .tint(Theme.accent)
                if camera.denied { Text("Chưa cho phép camera: vào Cài đặt iPhone để bật.").font(Theme.Typography.caption).foregroundColor(Theme.danger) }
                Spacer()
            }
            .padding(Theme.screenPadding)
            .navigationTitle(title)
            .navigationBarTitleDisplayMode(.inline)
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
