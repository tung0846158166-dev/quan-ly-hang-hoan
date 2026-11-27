import SwiftUI
import UIKit
import ScannerCore

enum OrderFilter: String, CaseIterable {
    case main = "Tất cả"
    case pending = "Chưa nhận"
    case overdue = "Quá hạn"
    case soon = "Sắp hạn"
    case partialReturn = "Hoàn 1 phần"
    case watch = "Theo dõi"
    case complaint = "Khiếu nại"
    case received = "Đã nhận"
    case cancelled = "Đơn hủy"
    case archived = "Lưu trữ"
    case pre = "Đặt trước"
    case all = "Toàn bộ"

    var accentColor: Color {
        switch self {
        case .overdue: return Theme.danger
        case .soon, .watch: return Theme.warning
        case .complaint: return Theme.purple
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
    @State private var platforms: [String] = []
    @State private var returnWatchActive = 0
    @State private var cancelledPending = 0
    @State private var afterPrepPending = 0
    @State private var offPlatformPending = 0
    @State private var productCount = 0
    @State private var listNowMs = Int(Date().timeIntervalSince1970 * 1000)

    private struct FilterOutput {
        var shown: [OrderRow]
        var counts: [OrderFilter: Int]
        var nowMs: Int
    }

    private struct SummaryOutput {
        var platforms: [String]
        var returnWatchActive: Int
        var cancelledPending: Int
        var afterPrepPending: Int
        var offPlatformPending: Int
        var productCount: Int
    }

    private static func matches(_ r: OrderRow, _ q: String, _ qc: String) -> Bool {
        if q.isEmpty { return true }
        for f in [r.c, r.o, r.x, r.n, r.co, r.sz, r.wr, r.nt] {
            if f.localizedCaseInsensitiveContains(q) { return true }
            if !qc.isEmpty && f.localizedCaseInsensitiveContains(qc) { return true }
        }
        return false
    }

    private static func belongs(_ r: OrderRow, _ f: OrderFilter, now: Int) -> Bool {
        switch f {
        case .all: return true
        case .main: return r.k == "m"
        case .pending: return r.pd && r.k == "m"
        case .overdue: return r.pd && r.k == "m" && r.alertLevel(now: now) == .overdue
        case .soon: return r.pd && r.k == "m" && r.alertLevel(now: now) == .soon
        case .partialReturn: return DashboardMetrics.isPartialReturn(r)
        case .watch: return r.wl && r.k == "m"
        case .complaint: return r.isComplaint
        case .received: return r.rv && r.k == "m"
        case .cancelled: return r.isCancelled
        case .pre: return r.pre
        case .archived: return r.isArchived
        }
    }

    private static func computeFilter(rows: [OrderRow], query: String, platform: String, filter: OrderFilter) -> FilterOutput {
        let now = Int(Date().timeIntervalSince1970 * 1000)
        let q = query.trimmingCharacters(in: .whitespacesAndNewlines)
        let qc = q.isEmpty ? "" : TrackingCode.clean(q)
        let targetPlat = platform.isEmpty ? "" : DashboardMetrics.normalizedPlatform(platform)
        var counts: [OrderFilter: Int] = [:]
        var shown: [OrderRow] = []
        shown.reserveCapacity(min(rows.count, 512))
        for r in rows {
            if !targetPlat.isEmpty && DashboardMetrics.normalizedPlatform(r.p) != targetPlat { continue }
            if !matches(r, q, qc) { continue }
            let isMain = r.k == "m"
            let alert: DeadlineLevel = (isMain && r.pd) ? r.alertLevel(now: now) : .unknown
            counts[.all, default: 0] += 1
            if isMain {
                counts[.main, default: 0] += 1
                if r.pd {
                    counts[.pending, default: 0] += 1
                    if alert == .overdue { counts[.overdue, default: 0] += 1 }
                    else if alert == .soon { counts[.soon, default: 0] += 1 }
                }
                if r.wl { counts[.watch, default: 0] += 1 }
                if r.rv { counts[.received, default: 0] += 1 }
            }
            if DashboardMetrics.isPartialReturn(r) { counts[.partialReturn, default: 0] += 1 }
            if r.isCancelled { counts[.cancelled, default: 0] += 1 }
            if r.isArchived { counts[.archived, default: 0] += 1 }
            if r.isComplaint { counts[.complaint, default: 0] += 1 }
            if r.pre { counts[.pre, default: 0] += 1 }
            if belongs(r, filter, now: now) { shown.append(r) }
        }
        return FilterOutput(shown: shown, counts: counts, nowMs: now)
    }

    private static func computeSummaries(rows: [OrderRow]) -> SummaryOutput {
        var platSet = Set<String>()
        var rwActive = 0
        var cxlPending = 0
        var prepPending = 0
        var offPending = 0
        for r in rows {
            if r.k == "m" && !r.rv && OffPlatform.isOffPlatform(r) { offPending += 1 }
            let p = DashboardMetrics.normalizedPlatform(r.p)
            if !p.isEmpty && p != "Khác" { platSet.insert(p) }
            if r.inReturnWatch && r.returnWatchStage < 4 { rwActive += 1 }
            if r.k == "c" && !r.rv { cxlPending += 1 }
            if r.k == "m" && !r.rv && r.isCancelAfterPrep { prepPending += 1 }
        }
        let prodCount = ProductMetrics.compute(rows: rows).products.count
        return SummaryOutput(
            platforms: Array(platSet).sorted(),
            returnWatchActive: rwActive,
            cancelledPending: cxlPending,
            afterPrepPending: prepPending,
            offPlatformPending: offPending,
            productCount: prodCount
        )
    }

    private var dataToken: String {
        "\(store.revision)|\(store.isDemoMode ? 1 : 0)|\(store.updatedAt?.timeIntervalSince1970 ?? 0)|\(store.rows.count)"
    }

    private var filterToken: String {
        "\(dataToken)|\(filter.rawValue)|\(platform)|\(query)|\(engine.items.count)"
    }

    private func applyPendingNavigation() {
        if let f = router.pendingOrdersFilter {
            filter = f
            router.pendingOrdersFilter = nil
        }
        if let p = router.pendingOrdersPlatform {
            platform = p
            router.pendingOrdersPlatform = nil
        }
    }

    var body: some View {
        NavigationStack {
            VStack(spacing: 4) {
                shortcutRow
                if store.hasData {
                    filterBar
                    if hasExtraFilter {
                        activePlatformStrip
                    }
                }

                List(shown) { r in
                    Button {
                        Haptics.tap()
                        selected = r
                    } label: {
                        orderCard(r, now: listNowMs)
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
            .onAppear { applyPendingNavigation() }
            .onChange(of: router.pendingOrdersFilter) { _ in applyPendingNavigation() }
            .onChange(of: router.pendingOrdersPlatform) { _ in applyPendingNavigation() }
            // Chỉ tính lại số liệu chip lối tắt + danh sách sàn + số mặt hàng khi dữ liệu đơn đổi (không tính lại khi gõ tìm kiếm).
            .task(id: dataToken) {
                let rowsSnapshot = store.rows
                let summary = await Task.detached(priority: .userInitiated) {
                    Self.computeSummaries(rows: rowsSnapshot)
                }.value
                guard !Task.isCancelled else { return }
                platforms = summary.platforms
                returnWatchActive = summary.returnWatchActive
                cancelledPending = summary.cancelledPending
                afterPrepPending = summary.afterPrepPending
                offPlatformPending = summary.offPlatformPending
                productCount = summary.productCount
            }
            // Lọc 5.000 đơn trên luồng nền + debounce nhẹ khi gõ tìm kiếm để cuộn/gõ mượt 60fps.
            .task(id: filterToken) {
                let q = query
                if !q.isEmpty {
                    try? await Task.sleep(nanoseconds: 120_000_000)
                    guard !Task.isCancelled else { return }
                }
                let rowsSnapshot = store.rows
                let plat = platform
                let activeFilter = filter
                let scannedNow = engine.scannedReturnCodes
                let result = await Task.detached(priority: .userInitiated) {
                    Self.computeFilter(rows: rowsSnapshot, query: q, platform: plat, filter: activeFilter)
                }.value
                guard !Task.isCancelled else { return }
                shown = result.shown
                counts = result.counts
                listNowMs = result.nowMs
                scanned = scannedNow
            }
            .toolbar {
                ToolbarItemGroup(placement: .navigationBarTrailing) {
                    Button {
                        Haptics.tap()
                        showScanner = true
                    } label: {
                        Image(systemName: "qrcode.viewfinder")
                    }
                    .accessibilityLabel("Quét mã để tìm đơn")

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
                ScanSearchSheet { cleanedCode in
                    query = cleanedCode
                    filter = .all
                    let rowsSnapshot = store.rows
                    let res = Self.computeFilter(rows: rowsSnapshot, query: cleanedCode, platform: platform, filter: .all)
                    shown = res.shown
                    counts = res.counts
                    listNowMs = res.nowMs
                    if res.shown.count == 1, let only = res.shown.first {
                        selected = only
                    }
                }
            }
        }
    }

    // MARK: - Bảng phụ: 5 ô chia đều một hàng, không cuộn (chủ dự án 27/11/2026: gọn lại phần lựa chọn)
    private var shortcutRow: some View {
        HStack(spacing: 6) {
            NavigationLink { ReturnWatchView() } label: {
                tableTile(icon: "arrow.uturn.backward", title: "Hoàn về", count: returnWatchActive, color: Theme.teal)
            }
            NavigationLink { CancelledTableView() } label: {
                tableTile(icon: "xmark.circle.fill", title: "Đơn hủy", count: cancelledPending, color: Theme.danger)
            }
            NavigationLink { CancelAfterPrepView() } label: {
                tableTile(icon: "shippingbox.fill", title: "Sau CB", count: afterPrepPending, color: Theme.purple)
            }
            NavigationLink { OffPlatformView() } label: {
                tableTile(icon: "storefront.fill", title: "Ngoài sàn", count: offPlatformPending, color: Theme.success)
            }
            NavigationLink { ReceivedStockView() } label: {
                tableTile(icon: "archivebox.fill", title: "Sẵn kho", count: nil, color: Theme.teal)
            }
            NavigationLink { ProductStatsView() } label: {
                tableTile(icon: "tshirt.fill", title: "Sản phẩm", count: nil, color: Theme.accent)
            }
        }
        .buttonStyle(.plain)
        .padding(.horizontal, Theme.screenPadding)
        .padding(.top, 2)
    }

    /// Ô bảng: biểu tượng + số cần xử lý (góc), tên bên dưới; ô chia đều chiều ngang.
    private func tableTile(icon: String, title: String, count: Int?, color: Color) -> some View {
        let active = (count ?? 1) > 0
        return VStack(spacing: 3) {
            ZStack(alignment: .topTrailing) {
                Image(systemName: icon)
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundColor(active ? color : .secondary)
                    .frame(width: 30, height: 24)
                if let c = count, c > 0 {
                    Text(c > 999 ? "999+" : "\(c)")
                        .font(.system(size: 9, weight: .bold, design: .rounded))
                        .monospacedDigit()
                        .foregroundColor(.white)
                        .padding(.horizontal, 4)
                        .padding(.vertical, 1)
                        .background(color)
                        .clipShape(Capsule())
                        .offset(x: 10, y: -5)
                }
            }
            Text(title)
                .font(Theme.Typography.microBold)
                .foregroundColor(.primary)
                .lineLimit(1)
                .minimumScaleFactor(0.8)
        }
        .frame(maxWidth: .infinity, minHeight: 52)
        .background(Theme.cardBackground)
        .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).stroke(Color.primary.opacity(0.07), lineWidth: 0.7))
        .contentShape(Rectangle())
        .accessibilityLabel(count.map { "\(title), \($0) cần xử lý" } ?? title)
    }

    // MARK: - Lọc: 4 lựa chọn chính dạng thanh chia đều + nút "Lọc" gom phần còn lại và lọc sàn
    private static let primaryFilters: [OrderFilter] = [.main, .pending, .overdue, .received]
    private static let moreFilters: [OrderFilter] = [.soon, .watch, .complaint, .partialReturn, .pre, .cancelled, .archived, .all]

    private var filterBar: some View {
        HStack(spacing: 6) {
            HStack(spacing: 2) {
                ForEach(Self.primaryFilters, id: \.self) { f in
                    let on = filter == f
                    Button {
                        Haptics.tap()
                        filter = f
                    } label: {
                        VStack(spacing: 0) {
                            Text(f == .main ? "Tất cả" : f.rawValue)
                                .font(on ? Theme.Typography.captionBold : Theme.Typography.caption.weight(.medium))
                                .lineLimit(1)
                                .minimumScaleFactor(0.8)
                            Text("\(counts[f] ?? 0)")
                                .font(.system(.caption2, design: .rounded).bold())
                                .monospacedDigit()
                                .foregroundColor(on ? .white.opacity(0.9) : (f == .overdue && (counts[f] ?? 0) > 0 ? Theme.danger : .secondary))
                        }
                        .frame(maxWidth: .infinity, minHeight: 40)
                        .background(on ? f.accentColor : Color.clear)
                        .foregroundColor(on ? .white : .primary)
                        .clipShape(RoundedRectangle(cornerRadius: 9, style: .continuous))
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                }
            }
            .padding(3)
            .background(Theme.cardBackground)
            .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).stroke(Color.primary.opacity(0.07), lineWidth: 0.7))

            moreFilterMenu
        }
        .padding(.horizontal, Theme.screenPadding)
    }

    private var hasExtraFilter: Bool { Self.moreFilters.contains(filter) || !platform.isEmpty }

    private var moreFilterMenu: some View {
        Menu {
            Section("Lọc thêm") {
                ForEach(Self.moreFilters, id: \.self) { f in
                    Button {
                        filter = f
                    } label: {
                        if filter == f { Label("\(f.rawValue) (\(counts[f] ?? 0))", systemImage: "checkmark") }
                        else { Text("\(f.rawValue) (\(counts[f] ?? 0))") }
                    }
                }
            }
            if !platforms.isEmpty {
                Section("Sàn") {
                    Button { platform = "" } label: {
                        if platform.isEmpty { Label("Tất cả sàn", systemImage: "checkmark") } else { Text("Tất cả sàn") }
                    }
                    ForEach(platforms, id: \.self) { p in
                        Button { platform = p } label: {
                            if platform == p { Label(p, systemImage: "checkmark") } else { Text(p) }
                        }
                    }
                }
            }
        } label: {
            VStack(spacing: 0) {
                Image(systemName: hasExtraFilter ? "line.3.horizontal.decrease.circle.fill" : "line.3.horizontal.decrease.circle")
                    .font(.system(size: 17, weight: .semibold))
                Text("Lọc").font(Theme.Typography.microBold)
            }
            .foregroundColor(hasExtraFilter ? .white : Theme.accent)
            .frame(width: 52, height: 46)
            .background(hasExtraFilter ? Theme.accent : Theme.cardBackground)
            .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).stroke(Color.primary.opacity(0.07), lineWidth: 0.7))
        }
        .accessibilityLabel("Lọc thêm và lọc theo sàn")
    }

    /// Dòng nhỏ báo đang lọc thêm (bấm ✕ để bỏ) — chỉ hiện khi có lọc phụ hoặc lọc sàn.
    private var activePlatformStrip: some View {
        HStack(spacing: 6) {
            Text("Đang lọc:")
                .font(Theme.Typography.caption)
                .foregroundStyle(.secondary)
            if Self.moreFilters.contains(filter) {
                removableChip(text: "\(filter.rawValue) (\(counts[filter] ?? 0))", color: filter.accentColor) { filter = .main }
            }
            if !platform.isEmpty {
                removableChip(text: platform, color: Theme.platformColor(platform)) { platform = "" }
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, Theme.screenPadding)
    }

    private func removableChip(text: String, color: Color, remove: @escaping () -> Void) -> some View {
        Button {
            Haptics.tap()
            remove()
        } label: {
            HStack(spacing: 4) {
                Text(text).font(Theme.Typography.captionBold).lineLimit(1)
                Image(systemName: "xmark").font(.system(size: 9, weight: .bold))
            }
            .padding(.horizontal, 9)
            .padding(.vertical, 4)
            .background(color.opacity(0.14))
            .foregroundColor(color == .secondary ? .primary : color)
            .clipShape(Capsule())
        }
        .buttonStyle(.plain)
    }

    // MARK: - Hàng đơn gọn 3 dòng
    private func orderCard(_ r: OrderRow, now: Int) -> some View {
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
                if r.isComplaint {
                    Text("⚖️ \(r.kn)")
                        .font(Theme.Typography.microBold)
                        .foregroundColor(Theme.purple)
                        .lineLimit(1)
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
                Text(r.returnWatchStageName)
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
            Text("Đang tải dữ liệu từ cloud. Chưa ghép thì vào Cài đặt → Cloud.")
                .font(Theme.Typography.caption)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .padding(.horizontal, 24)
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

/// Quét QR / mã vạch để điền vào ô tìm kiếm (lọc mã rác bằng `pickBest`, làm sạch URL/JSON, dừng khi vào nền).
struct ScanSearchSheet: View {
    let onCode: (String) -> Void
    var title: String = "Quét để tìm đơn"
    var hint: String = "Đưa mã vạch hoặc QR vào khung để tìm đơn"
    @EnvironmentObject var store: SnapshotStore
    @Environment(\.scenePhase) private var scenePhase
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
                if camera.denied {
                    VStack(spacing: 6) {
                        Text("Chưa cho phép camera: vào Cài đặt iPhone để bật.")
                            .font(Theme.Typography.caption)
                            .foregroundColor(Theme.danger)
                            .multilineTextAlignment(.center)
                        Button("Mở Cài đặt iPhone") {
                            if let url = URL(string: UIApplication.openSettingsURLString) {
                                UIApplication.shared.open(url)
                            }
                        }
                        .font(Theme.Typography.captionBold)
                        .buttonStyle(.bordered)
                    }
                }
                Spacer()
            }
            .padding(Theme.screenPadding)
            .navigationTitle(title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .navigationBarTrailing) { Button("Đóng") { dismiss() } } }
            .onAppear {
                camera.onCodes = { raws in
                    Task { @MainActor in
                        guard !done else { return }
                        guard let best = TrackingCode.pickBest(raws, isKnown: { code in store.lookup(TrackingCode.clean(code)) != nil }) else { return }
                        let cleaned = TrackingCode.clean(best)
                        guard !cleaned.isEmpty else { return }
                        done = true
                        Haptics.medium()
                        camera.stop()
                        onCode(cleaned)
                        dismiss()
                    }
                }
                if scenePhase == .active {
                    camera.start()
                }
            }
            .onChange(of: scenePhase) { phase in
                if phase == .active && !done {
                    camera.start()
                } else {
                    camera.stop()
                }
            }
            .onDisappear { camera.stop() }
        }
    }
}
