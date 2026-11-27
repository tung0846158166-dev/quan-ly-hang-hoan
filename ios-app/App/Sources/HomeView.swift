import SwiftUI
import ScannerCore

/// Màn hình mở đầu "Tổng quan" (T036 - gọn & hiện đại):
/// Thanh trên inline với 1 dòng chào + ngày và nút Quét gọn; Hero vòng 84pt + dải 4 số liệu ngang;
/// Các thẻ xếp dọc gọn (Sản phẩm hoàn nhiều nhất, Hàng hoàn về, Cần xử lý ngay, Theo sàn, 7 ngày).
struct HomeView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @EnvironmentObject var router: NotificationRouter
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.colorScheme) private var scheme

    @State private var summary = DashboardSummary()
    @State private var productStats = ProductStatsSummary()
    @State private var productDateRange: ProductDateRange = .all
    @State private var productFilteredRows: [OrderRow] = []
    @State private var hasProductDates = false
    @State private var selectedRow: OrderRow?
    @State private var showAllProductStats = false
    @State private var selectedProductStat: ProductStatItem?
    @State private var showSelectedProductStat = false

    private static let homeProductRanges: [ProductDateRange] = [.today, .days7, .days30, .thisMonth, .all]

    private static let todayFormatter: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "vi_VN")
        f.dateFormat = "EEE, dd/MM"
        return f
    }()

    private var recomputeToken: String {
        "\(productDateRange.rawValue)|\(store.revision)|\(store.isDemoMode ? 1 : 0)|\(store.updatedAt?.timeIntervalSince1970 ?? 0)|\(store.rows.count)"
    }

    @MainActor
    private func runRecompute() async {
        let rowsSnapshot = store.rows
        let statsSnapshot = store.stats
        let selectedRange = productDateRange
        let now = Date()
        let result = await Task.detached(priority: .userInitiated) { () -> (DashboardSummary, ProductStatsSummary, [OrderRow], Bool) in
            let next = DashboardMetrics.compute(rows: rowsSnapshot, stats: statsSnapshot, now: now, urgentLimit: 3)
            let datesAvailable = ProductMetrics.hasAnyDate(rows: rowsSnapshot)
            let activeRange: ProductDateRange = datesAvailable ? selectedRange : .all
            let filteredProducts = ProductMetrics.filter(
                rows: rowsSnapshot,
                platform: .all,
                dateRange: activeRange,
                dateBasis: .returnDate,
                now: now
            )
            let nextProducts = ProductMetrics.compute(rows: filteredProducts)
            return (next, nextProducts, filteredProducts, datesAvailable)
        }.value
        guard !Task.isCancelled else { return }
        if reduceMotion {
            summary = result.0
            productStats = result.1
            productFilteredRows = result.2
            hasProductDates = result.3
        } else {
            withAnimation(.easeOut(duration: 0.20)) {
                summary = result.0
                productStats = result.1
                productFilteredRows = result.2
                hasProductDates = result.3
            }
        }
    }

    private var connectionStatus: (text: String, color: Color) {
        if store.isDemoMode {
            return ("Mẫu", Theme.accent)
        }
        if engine.cloudActive {
            return (CloudStatusText.label(engine.cloudState), CloudStatusText.tone(engine.cloudState).color)
        }
        return ("Chưa ghép cloud", .secondary)
    }

    private var formattedToday: String {
        let s = Self.todayFormatter.string(from: Date())
        return s.prefix(1).uppercased() + s.dropFirst()
    }

    private var greetingLine: String {
        "\(DashboardMetrics.greeting(for: Date())) · \(formattedToday)"
    }

    var body: some View {
        NavigationStack {
            List {
                if !store.hasData {
                    emptyStateSection
                        .listRowInsets(EdgeInsets(top: 8, leading: Theme.screenPadding, bottom: 6, trailing: Theme.screenPadding))
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)

                    topProductsCard
                        .listRowInsets(EdgeInsets(top: 4, leading: Theme.screenPadding, bottom: 16, trailing: Theme.screenPadding))
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)
                } else {
                    heroCard
                        .listRowInsets(EdgeInsets(top: 6, leading: Theme.screenPadding, bottom: 4, trailing: Theme.screenPadding))
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)

                    metricsStrip
                        .listRowInsets(EdgeInsets(top: 4, leading: Theme.screenPadding, bottom: 4, trailing: Theme.screenPadding))
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)

                    if summary.discrepancyCount > 0 && !engine.cloudActive {
                        discrepancyCard
                            .listRowInsets(EdgeInsets(top: 4, leading: Theme.screenPadding, bottom: 4, trailing: Theme.screenPadding))
                            .listRowBackground(Color.clear)
                            .listRowSeparator(.hidden)
                    }

                    topProductsCard
                        .listRowInsets(EdgeInsets(top: 4, leading: Theme.screenPadding, bottom: 4, trailing: Theme.screenPadding))
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)

                    returnWatchCard
                        .listRowInsets(EdgeInsets(top: 4, leading: Theme.screenPadding, bottom: 4, trailing: Theme.screenPadding))
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)

                    complaintsCard
                        .listRowInsets(EdgeInsets(top: 4, leading: Theme.screenPadding, bottom: 4, trailing: Theme.screenPadding))
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)

                    urgentHeader
                        .listRowInsets(EdgeInsets(top: 6, leading: Theme.screenPadding, bottom: 4, trailing: Theme.screenPadding))
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)

                    if summary.urgentOrders.isEmpty {
                        urgentEmptyCard
                            .listRowInsets(EdgeInsets(top: 2, leading: Theme.screenPadding, bottom: 4, trailing: Theme.screenPadding))
                            .listRowBackground(Color.clear)
                            .listRowSeparator(.hidden)
                    } else {
                        ForEach(summary.urgentOrders) { item in
                            urgentRow(item)
                                .listRowInsets(EdgeInsets(top: 3, leading: Theme.screenPadding, bottom: 3, trailing: Theme.screenPadding))
                                .listRowBackground(Color.clear)
                                .listRowSeparator(.hidden)
                                .swipeActions(edge: .leading, allowsFullSwipe: true) {
                                    Button {
                                        Haptics.tap()
                                        selectedRow = item.row
                                    } label: {
                                        Label("Ghi chú", systemImage: "note.text")
                                    }
                                    .tint(Theme.info)
                                }
                                .swipeActions(edge: .trailing, allowsFullSwipe: true) {
                                    Button {
                                        Haptics.medium()
                                        engine.sendOrderNote(item.row, note: item.row.wr, watch: !item.row.wl)
                                    } label: {
                                        Label(item.row.wl ? "Bỏ theo dõi" : "Theo dõi", systemImage: item.row.wl ? "star.slash" : "star.fill")
                                    }
                                    .tint(Theme.warning)
                                }
                        }
                    }

                    platformsCard
                        .listRowInsets(EdgeInsets(top: 4, leading: Theme.screenPadding, bottom: 4, trailing: Theme.screenPadding))
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)

                    if summary.has7DayData {
                        last7DaysCard
                            .listRowInsets(EdgeInsets(top: 4, leading: Theme.screenPadding, bottom: 4, trailing: Theme.screenPadding))
                            .listRowBackground(Color.clear)
                            .listRowSeparator(.hidden)
                    }

                    statsShortcutCard
                        .listRowInsets(EdgeInsets(top: 4, leading: Theme.screenPadding, bottom: 16, trailing: Theme.screenPadding))
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)
                }
            }
            .listStyle(.plain)
            .scrollContentBackground(.hidden)
            .background(Theme.pageBackground.ignoresSafeArea())
            .refreshable {
                guard !store.isDemoMode else { return }
                Haptics.tap()
                engine.requestSnapshot()
                try? await Task.sleep(nanoseconds: 1_500_000_000)
            }
            .navigationTitle("")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .navigationBarLeading) {
                    Text(greetingLine)
                        .font(Theme.Typography.secondaryBold)
                        .foregroundColor(.primary)
                        .lineLimit(1)
                        .minimumScaleFactor(0.75)
                }

                ToolbarItemGroup(placement: .navigationBarTrailing) {
                    Button {
                        Haptics.medium()
                        router.tab = NotificationRouter.scanTab
                    } label: {
                        HStack(spacing: 4) {
                            Image(systemName: "barcode.viewfinder")
                                .font(.system(size: 13, weight: .bold))
                            Text("Quét")
                                .font(Theme.Typography.captionBold)
                        }
                        .foregroundColor(.white)
                        .padding(.horizontal, 10)
                        .padding(.vertical, 5)
                        .background(Theme.accent)
                        .clipShape(Capsule())
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel("Quét mã vận đơn")

                    if !store.isDemoMode {
                        Button {
                            Haptics.tap()
                            Task { await engine.cloud.syncNow() }
                        } label: {
                            Image(systemName: "arrow.clockwise")
                                .font(.subheadline.weight(.semibold))
                        }
                        .accessibilityLabel("Đồng bộ lại từ cloud")
                    }

                    Button {
                        Haptics.tap()
                        router.showNotifications = true
                    } label: {
                        Image(systemName: store.alertCount > 0 ? "bell.badge.fill" : "bell")
                            .font(.subheadline.weight(.semibold))
                            .foregroundColor(store.alertCount > 0 ? Theme.accent : .primary)
                    }
                    .accessibilityLabel("Thông báo (\(store.alertCount))")
                }
            }
            .navigationDestination(isPresented: $router.showNotifications) {
                NotificationsView()
            }
            .navigationDestination(isPresented: $showAllProductStats) {
                ProductStatsView(initialDateRange: productDateRange)
            }
            .navigationDestination(isPresented: $showSelectedProductStat) {
                if let stat = selectedProductStat {
                    ProductStatDetailView(stat: stat, sourceRows: productFilteredRows)
                }
            }
            .sheet(item: $selectedRow) { DetailView(row: $0) }
            .task(id: recomputeToken) { await runRecompute() }
        }
        .sheet(item: $router.openRow) { DetailView(row: $0) }
    }

    // MARK: - 1. Khối Hero gọn (vòng 84pt + quá hạn / sắp hạn bên phải)
    private var heroCard: some View {
        let st = connectionStatus
        let ringSize: CGFloat = 84
        return HStack(spacing: 14) {
            Button {
                Haptics.tap()
                router.openOrders(filter: .received)
            } label: {
                ZStack {
                    Circle()
                        .stroke(Theme.accent.opacity(scheme == .dark ? 0.22 : 0.16), lineWidth: 8)
                    Circle()
                        .trim(from: 0, to: CGFloat(max(summary.todayProgress, summary.receivedTodayCount > 0 ? 0.05 : 0)))
                        .stroke(
                            LinearGradient(
                                colors: [Theme.accent, Theme.warning],
                                startPoint: .topLeading,
                                endPoint: .bottomTrailing
                            ),
                            style: StrokeStyle(lineWidth: 8, lineCap: .round)
                        )
                        .rotationEffect(.degrees(-90))

                    VStack(spacing: 0) {
                        AnimatedCounter(
                            value: summary.receivedTodayCount,
                            font: Theme.Typography.heroNumber,
                            color: .primary
                        )
                        Text("/ \(summary.todayTargetCount)")
                            .font(.system(.caption2, design: .rounded).bold())
                            .foregroundStyle(.secondary)
                    }
                }
                .frame(width: ringSize, height: ringSize)
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Đã nhận \(summary.receivedTodayCount) trên \(summary.todayTargetCount) đơn hôm nay")

            VStack(alignment: .leading, spacing: 6) {
                HStack(spacing: 6) {
                    Text("Tiến độ hôm nay")
                        .font(Theme.Typography.sectionTitle)
                        .foregroundColor(.primary)
                        .lineLimit(1)
                    Spacer(minLength: 4)
                    HStack(spacing: 4) {
                        Circle().fill(st.color).frame(width: 6, height: 6)
                        Text(st.text)
                            .font(Theme.Typography.microBold)
                            .foregroundColor(st.color)
                    }
                    .padding(.horizontal, 7)
                    .padding(.vertical, 2.5)
                    .background(st.color.opacity(0.14))
                    .clipShape(Capsule())
                }

                heroSubMetric(
                    title: "Quá hạn",
                    value: summary.overdueCount,
                    color: Theme.danger
                ) {
                    router.openOrders(filter: .overdue)
                }

                heroSubMetric(
                    title: "Sắp đến hạn",
                    value: summary.soonCount,
                    color: Theme.warning
                ) {
                    router.openOrders(filter: .soon)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(Theme.cardPadding)
        .background {
            RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous)
                .fill(Theme.cardBackground)
                .overlay(
                    RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous)
                        .fill(
                            LinearGradient(
                                colors: [
                                    Theme.accent.opacity(scheme == .dark ? 0.20 : 0.12),
                                    Theme.accent.opacity(scheme == .dark ? 0.03 : 0.01)
                                ],
                                startPoint: .topLeading,
                                endPoint: .bottomTrailing
                            )
                        )
                )
        }
        .clipShape(RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous)
                .stroke(Theme.accent.opacity(scheme == .dark ? 0.28 : 0.18), lineWidth: 0.8)
        )
    }

    private func heroSubMetric(title: String, value: Int, color: Color, action: @escaping () -> Void) -> some View {
        Button {
            Haptics.tap()
            action()
        } label: {
            HStack(spacing: 6) {
                Circle()
                    .fill(color)
                    .frame(width: 7, height: 7)
                Text(title)
                    .font(Theme.Typography.secondary)
                    .foregroundColor(.primary)
                Spacer(minLength: 4)
                AnimatedCounter(
                    value: value,
                    font: Theme.Typography.metricNumber,
                    color: value > 0 ? color : .secondary
                )
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 5)
            .frame(minHeight: 32)
            .background(Theme.subtleFill)
            .clipShape(RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous))
        }
        .buttonStyle(.plain)
    }

    // MARK: - 2. Dải 4 số liệu ngang gọn (Chờ về / Nhận hôm nay / Quá hạn / Sắp hạn)
    private var metricsStrip: some View {
        HStack(spacing: 0) {
            stripMetricCell("Chờ về", summary.pendingCount, Theme.accent) {
                router.openOrders(filter: .pending)
            }
            Divider().frame(height: 28)
            stripMetricCell("Nhận hôm nay", summary.receivedTodayCount, Theme.success) {
                router.openOrders(filter: .received)
            }
            Divider().frame(height: 28)
            stripMetricCell("Quá hạn", summary.overdueCount, Theme.danger) {
                router.openOrders(filter: .overdue)
            }
            Divider().frame(height: 28)
            stripMetricCell("Sắp hạn", summary.soonCount, Theme.warning) {
                router.openOrders(filter: .soon)
            }
        }
        .card(padding: 8, radius: Theme.cardRadius)
    }

    private func stripMetricCell(_ title: String, _ value: Int, _ color: Color, action: @escaping () -> Void) -> some View {
        Button {
            Haptics.tap()
            action()
        } label: {
            VStack(spacing: 2) {
                AnimatedCounter(
                    value: value,
                    font: Theme.Typography.metricNumber,
                    color: value > 0 ? color : .secondary
                )
                Text(title)
                    .font(Theme.Typography.micro)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                    .lineLimit(2)
                    .minimumScaleFactor(0.75)
            }
            .frame(maxWidth: .infinity, minHeight: Theme.minTapTarget)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    // MARK: - 3. Sản phẩm hoàn nhiều nhất (xếp dọc gọn, luôn hiển thị, không ẩn khi trống)
    private var topProductsCard: some View {
        let top3 = Array(productStats.products.prefix(3))
        return VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                SectionTitleRow(
                    title: "Sản phẩm hoàn nhiều nhất",
                    icon: "tshirt.fill",
                    iconColor: Theme.accent
                )
                Button {
                    Haptics.tap()
                    showAllProductStats = true
                } label: {
                    HStack(spacing: 3) {
                        Text("Xem tất cả")
                            .font(Theme.Typography.microBold)
                        Image(systemName: "chevron.right")
                            .font(.system(size: 9, weight: .bold))
                    }
                    .foregroundColor(Theme.accent)
                    .padding(.horizontal, 8)
                    .padding(.vertical, 4)
                    .background(Theme.accent.opacity(0.14))
                    .clipShape(Capsule())
                }
                .buttonStyle(.plain)
            }

            if store.hasData && hasProductDates {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 5) {
                        ForEach(Self.homeProductRanges) { range in
                            let on = productDateRange == range
                            Button {
                                Haptics.tap()
                                productDateRange = range
                            } label: {
                                Text(range.rawValue)
                                    .font(on ? Theme.Typography.microBold : Theme.Typography.micro.weight(.semibold))
                                    .padding(.horizontal, 9)
                                    .padding(.vertical, 4)
                                    .background(on ? Theme.accent : Theme.subtleFill)
                                    .foregroundColor(on ? .white : .secondary)
                                    .clipShape(Capsule())
                            }
                            .buttonStyle(.plain)
                        }
                    }
                }
            }

            if !store.hasData {
                VStack(alignment: .leading, spacing: 8) {
                    Text("Chưa có dữ liệu sản phẩm")
                        .font(Theme.Typography.caption)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)

                }
            } else if top3.isEmpty {
                HStack(spacing: 8) {
                    Text("Chưa có sản phẩm hoàn trong khoảng này")
                        .font(Theme.Typography.caption)
                        .foregroundStyle(.secondary)
                    Spacer(minLength: 4)
                    if productDateRange != .all {
                        Button("Tất cả") {
                            Haptics.tap()
                            productDateRange = .all
                        }
                        .font(Theme.Typography.microBold)
                        .foregroundColor(Theme.accent)
                        .buttonStyle(.plain)
                    }
                }
            } else {
                Text("\(ProductMetrics.rangeSummaryText(for: productStats)) · Theo ngày hoàn")
                    .font(Theme.Typography.micro)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)

                VStack(spacing: 6) {
                    ForEach(Array(top3.enumerated()), id: \.element.id) { idx, item in
                        let rank = idx + 1
                        let barColor: Color = rank == 1 ? Theme.accent : (rank == 2 ? Theme.warning : Theme.teal)
                        let pct = Int((item.share * 100).rounded())

                        Button {
                            Haptics.tap()
                            selectedProductStat = item
                            showSelectedProductStat = true
                        } label: {
                            HStack(spacing: 8) {
                                Text("\(rank)")
                                    .font(.system(size: 11, weight: .bold, design: .rounded))
                                    .foregroundColor(.white)
                                    .frame(width: 18, height: 18)
                                    .background(barColor)
                                    .clipShape(RoundedRectangle(cornerRadius: 5, style: .continuous))

                                VStack(alignment: .leading, spacing: 1) {
                                    Text(item.name)
                                        .font(Theme.Typography.secondaryBold)
                                        .foregroundColor(.primary)
                                        .lineLimit(1)
                                    if item.premiumUnits > 0 {
                                        Text("Premium \(item.premiumUnits) • Thường \(item.standardUnits)")
                                            .font(Theme.Typography.micro)
                                            .foregroundColor(Theme.warning)
                                            .monospacedDigit()
                                            .lineLimit(1)
                                    }
                                }

                                Spacer(minLength: 4)

                                Text("\(item.units) cái")
                                    .font(.system(.caption, design: .rounded).bold())
                                    .foregroundColor(.primary)
                                    .monospacedDigit()
                                Text("(\(pct)%)")
                                    .font(Theme.Typography.micro)
                                    .foregroundStyle(.secondary)
                                    .monospacedDigit()
                            }
                            .frame(minHeight: 28)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
        }
        .card(padding: Theme.cardPadding, radius: Theme.cardRadius)
    }

    // MARK: - 4. Hàng hoàn về (thẻ dọc gọn <= 120pt)
    private var returnWatchCard: some View {
        let rw = summary.returnWatch
        let total = max(1, rw.total)
        return NavigationLink {
            ReturnWatchView()
        } label: {
            VStack(alignment: .leading, spacing: 8) {
                HStack(spacing: 8) {
                    SectionTitleRow(
                        title: "Hàng hoàn về",
                        icon: "arrow.uturn.backward",
                        iconColor: Theme.teal,
                        subtitle: "\(rw.active) đang về"
                    )
                    Image(systemName: "chevron.right")
                        .font(.caption2.bold())
                        .foregroundStyle(.tertiary)
                }

                GeometryReader { geo in
                    let w = max(0, geo.size.width)
                    HStack(spacing: 2) {
                        if rw.total == 0 || w <= 0 {
                            Capsule().fill(Color.gray.opacity(0.2))
                        } else {
                            if rw.stage1 > 0 {
                                Rectangle().fill(Theme.info).frame(width: min(w, max(6, w * CGFloat(rw.stage1) / CGFloat(total))))
                            }
                            if rw.stage2 > 0 {
                                Rectangle().fill(Theme.teal).frame(width: min(w, max(6, w * CGFloat(rw.stage2) / CGFloat(total))))
                            }
                            if rw.stage3 > 0 {
                                Rectangle().fill(Theme.warning).frame(width: min(w, max(6, w * CGFloat(rw.stage3) / CGFloat(total))))
                            }
                            if rw.stage4 > 0 {
                                Rectangle().fill(Theme.success).frame(width: min(w, max(6, w * CGFloat(rw.stage4) / CGFloat(total))))
                            }
                        }
                    }
                    .clipShape(Capsule())
                }
                .frame(height: 7)

                HStack(spacing: 6) {
                    stageMini("Đồng ý", rw.stage1, Theme.info)
                    stageMini("Đang về", rw.stage2, Theme.teal)
                    stageMini("Về shop", rw.stage3, Theme.warning)
                    stageMini("Nhận kho", rw.stage4, Theme.success)
                }
            }
            .card(padding: Theme.cardPadding, radius: Theme.cardRadius)
        }
        .buttonStyle(.plain)
    }

    // MARK: - Khiếu nại gần nhất (chủ dự án 27/11/2026): 3 đơn mới nhất, bấm để xem chi tiết; nút mở bảng Khiếu nại
    private var complaintsCard: some View {
        let recent = CloudOps.recentComplaints(store.rows, limit: 3)
        let processing = store.rows.filter { $0.kn == "Đang xử lý" }.count
        return VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                SectionTitleRow(
                    title: "Khiếu nại gần nhất",
                    icon: "exclamationmark.bubble.fill",
                    iconColor: Theme.purple,
                    subtitle: processing > 0 ? "\(processing) đang xử lý" : nil
                )
                Button("Mở bảng") {
                    Haptics.tap()
                    router.openOrders(filter: .complaint)
                }
                .font(Theme.Typography.captionBold)
                .foregroundColor(Theme.purple)
                .buttonStyle(.plain)
            }
            if recent.isEmpty {
                Text("Chưa có đơn khiếu nại. Mở một đơn → Khiếu nại (cần ghi lý do).")
                    .font(Theme.Typography.caption)
                    .foregroundStyle(.secondary)
            } else {
                ForEach(recent) { r in
                    Button {
                        Haptics.tap()
                        selectedRow = r
                    } label: {
                        HStack(spacing: 8) {
                            VStack(alignment: .leading, spacing: 2) {
                                HStack(spacing: 5) {
                                    PlatformBadge(platform: r.p)
                                    Text(r.c.isEmpty ? r.o : r.c)
                                        .font(Theme.Typography.code)
                                        .foregroundColor(.primary)
                                        .lineLimit(1)
                                }
                                let last = CloudOps.lastComplaintLine(r.knn)
                                Text(last.isEmpty ? (r.n.isEmpty ? "Chưa có lý do" : r.n) : last)
                                    .font(Theme.Typography.caption)
                                    .foregroundStyle(.secondary)
                                    .lineLimit(1)
                            }
                            Spacer(minLength: 4)
                            StatusPill(text: r.kn, color: r.kn == "Thành công" ? Theme.success : (r.kn == "Thất bại" ? Theme.danger : Theme.purple))
                            Image(systemName: "chevron.right")
                                .font(.caption2.bold())
                                .foregroundStyle(.tertiary)
                        }
                        .frame(minHeight: Theme.minTapTarget)
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                }
            }
        }
        .card(padding: Theme.cardPadding, radius: Theme.cardRadius)
    }

    private func stageMini(_ title: String, _ count: Int, _ color: Color) -> some View {
        HStack(spacing: 4) {
            Text("\(count)")
                .font(.system(size: 14, weight: .bold, design: .rounded))
                .foregroundColor(count > 0 ? color : .secondary)
                .monospacedDigit()
            Text(title)
                .font(Theme.Typography.micro)
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .minimumScaleFactor(0.75)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    // MARK: - 5. Cần xử lý ngay (tối đa 3 đơn gọn, giữ vuốt Ghi chú / Theo dõi)
    private var urgentHeader: some View {
        HStack {
            SectionTitleRow(
                title: "Cần xử lý ngay",
                icon: "flame.fill",
                iconColor: Theme.danger
            )
            if !summary.urgentOrders.isEmpty {
                Button("Xem hết") {
                    Haptics.tap()
                    router.openOrders(filter: summary.overdueCount > 0 ? .overdue : .soon)
                }
                .font(Theme.Typography.captionBold)
                .foregroundColor(Theme.accent)
                .buttonStyle(.plain)
            }
        }
    }

    private var urgentEmptyCard: some View {
        HStack(spacing: 10) {
            IconTile(systemName: "checkmark", color: Theme.success, size: 26, cornerRadius: 8)
            Text("Không có đơn quá hạn hay sắp đến hạn")
                .font(Theme.Typography.secondary)
                .foregroundStyle(.secondary)
            Spacer()
        }
        .card(padding: Theme.cardPadding, radius: Theme.cardRadius)
    }

    private func urgentRow(_ item: UrgentOrder) -> some View {
        let r = item.row
        let tint: Color = item.level == .overdue ? Theme.danger : Theme.warning
        return Button {
            Haptics.tap()
            selectedRow = r
        } label: {
            HStack(spacing: 10) {
                RoundedRectangle(cornerRadius: 2, style: .continuous)
                    .fill(tint)
                    .frame(width: 3.5)
                    .padding(.vertical, 2)

                VStack(alignment: .leading, spacing: 2) {
                    HStack(spacing: 5) {
                        PlatformBadge(platform: r.p)
                        Text(r.c)
                            .font(Theme.Typography.code)
                            .foregroundColor(.primary)
                            .lineLimit(1)
                        if r.wl {
                            Image(systemName: "star.fill")
                                .font(.system(size: 10))
                                .foregroundColor(Theme.warning)
                        }
                    }
                    if !r.n.isEmpty {
                        Text(r.n)
                            .font(Theme.Typography.caption)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                }

                Spacer(minLength: 4)

                Text(item.deadlineText)
                    .font(Theme.Typography.microBold)
                    .foregroundColor(tint)
                    .multilineTextAlignment(.trailing)
                    .lineLimit(2)
            }
            .frame(minHeight: Theme.minTapTarget)
            .card(padding: 10, radius: Theme.cardRadius)
        }
        .buttonStyle(.plain)
    }

    // MARK: - 6. Theo sàn (thẻ dọc gọn <= 120pt + lối tắt Hoàn một phần)
    private var platformsCard: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                SectionTitleRow(
                    title: "Theo sàn",
                    icon: "storefront.fill",
                    iconColor: Theme.accent
                )
                if summary.partialReturnCount > 0 {
                    Button {
                        Haptics.tap()
                        router.openOrders(filter: .partialReturn)
                    } label: {
                        StatusPill(text: "Hoàn 1 phần: \(summary.partialReturnCount)", color: Theme.purple)
                    }
                    .buttonStyle(.plain)
                }
            }

            HStack(spacing: 8) {
                ForEach(summary.platforms.prefix(3)) { plat in
                    let tint = Theme.platformColor(plat.name)
                    Button {
                        Haptics.tap()
                        router.openOrders(filter: .pending, platform: plat.name)
                    } label: {
                        VStack(alignment: .leading, spacing: 3) {
                            HStack(spacing: 4) {
                                Circle().fill(tint).frame(width: 6, height: 6)
                                Text(plat.name)
                                    .font(Theme.Typography.microBold)
                                    .foregroundColor(.primary)
                                    .lineLimit(1)
                            }
                            Text("\(plat.pending)")
                                .font(Theme.Typography.metricNumber)
                                .foregroundColor(.primary)
                                .monospacedDigit()
                            Text(plat.overdue > 0 ? "\(plat.overdue) quá hạn" : "Ổn định")
                                .font(Theme.Typography.micro)
                                .foregroundColor(plat.overdue > 0 ? Theme.danger : .secondary)
                                .lineLimit(1)
                                .minimumScaleFactor(0.8)
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(8)
                        .background(Theme.subtleFill)
                        .clipShape(RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous))
                    }
                    .buttonStyle(.plain)
                }
            }
        }
        .card(padding: Theme.cardPadding, radius: Theme.cardRadius)
    }

    // MARK: - 7. 7 ngày gần nhất (gọn)
    private var last7DaysCard: some View {
        let avgRatio = summary.average7DaysRatio
        return VStack(alignment: .leading, spacing: 8) {
            HStack {
                SectionTitleRow(
                    title: "7 ngày gần nhất",
                    icon: "chart.bar.fill",
                    iconColor: Theme.accent
                )
                StatusPill(text: String(format: "TB %.1f/ngày", summary.average7Days), color: Theme.accent)
            }

            GeometryReader { geo in
                let barAreaHeight: CGFloat = 56
                let avgY = barAreaHeight * (1 - CGFloat(avgRatio))

                ZStack(alignment: .top) {
                    if summary.average7Days > 0 {
                        Path { path in
                            path.move(to: CGPoint(x: 0, y: avgY + 14))
                            path.addLine(to: CGPoint(x: geo.size.width, y: avgY + 14))
                        }
                        .stroke(Theme.accent.opacity(0.55), style: StrokeStyle(lineWidth: 1.0, dash: [4, 3]))
                    }

                    HStack(alignment: .bottom, spacing: 6) {
                        ForEach(summary.last7Days) { day in
                            let ratio = summary.dailyRatio(for: day)
                            let barH = day.count > 0 ? max(8, CGFloat(ratio) * barAreaHeight) : 3
                            VStack(spacing: 3) {
                                Text(day.count > 0 ? "\(day.count)" : "")
                                    .font(.system(size: 10, weight: .bold, design: .rounded))
                                    .foregroundColor(day.isToday ? Theme.accent : .secondary)
                                    .monospacedDigit()
                                    .frame(height: 12)

                                Capsule()
                                    .fill(
                                        day.isToday
                                            ? LinearGradient(colors: [Theme.accent, Theme.warning], startPoint: .top, endPoint: .bottom)
                                            : LinearGradient(
                                                colors: [Theme.accent.opacity(day.count > 0 ? 0.55 : 0.16), Theme.accent.opacity(day.count > 0 ? 0.30 : 0.16)],
                                                startPoint: .top,
                                                endPoint: .bottom
                                            )
                                    )
                                    .frame(maxWidth: 20, minHeight: barH, maxHeight: barH)

                                Text(day.label)
                                    .font(.system(size: 10, weight: .bold))
                                    .foregroundColor(day.isToday ? Theme.accent : .secondary)
                            }
                            .frame(maxWidth: .infinity)
                        }
                    }
                    .frame(height: barAreaHeight + 30, alignment: .bottom)
                }
            }
            .frame(height: 88)
        }
        .card(padding: Theme.cardPadding, radius: Theme.cardRadius)
    }

    // MARK: - Cảnh báo lệch số đơn
    private var discrepancyCard: some View {
        HStack(alignment: .top, spacing: 10) {
            IconTile(systemName: "exclamationmark.triangle.fill", color: Theme.warning, size: 26, cornerRadius: 7)
            VStack(alignment: .leading, spacing: 3) {
                Text("Lệch \(summary.discrepancyCount) đơn so với máy tính")
                    .font(Theme.Typography.secondaryBold)
                Text(discrepancyDetail)
                    .font(Theme.Typography.caption)
                    .foregroundStyle(.secondary)
                HStack(spacing: 12) {
                    Button("Tải lại") {
                        Haptics.tap()
                        engine.requestSnapshot()
                    }
                    .font(Theme.Typography.captionBold)
                    .foregroundColor(Theme.accent)
                    .buttonStyle(.plain)

                    Button("Xem đối chiếu") {
                        Haptics.tap()
                        router.tab = NotificationRouter.settingsTab
                    }
                    .font(Theme.Typography.captionBold)
                    .foregroundStyle(.secondary)
                    .buttonStyle(.plain)
                }
            }
            Spacer(minLength: 0)
        }
        .padding(Theme.cardPadding)
        .background(Theme.warning.opacity(0.12))
        .clipShape(RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous)
                .stroke(Theme.warning.opacity(0.30), lineWidth: 0.8)
        )
    }

    private var discrepancyDetail: String {
        var parts: [String] = []
        if summary.droppedCount > 0 { parts.append("\(summary.droppedCount) đơn thiếu mã trên PC") }
        if summary.sentMismatchCount > 0 { parts.append("lệch \(summary.sentMismatchCount) đơn với gói gửi") }
        return parts.joined(separator: " · ")
    }

    // MARK: - Trạng thái rỗng kèm nút Xem thử dữ liệu mẫu
    private var emptyStateSection: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 12) {
                IconTile(systemName: "shippingbox.fill", color: Theme.accent, size: 40, cornerRadius: 11)
                VStack(alignment: .leading, spacing: 2) {
                    Text(engine.cloudActive ? "Đang tải dữ liệu từ cloud" : "Chưa ghép cloud")
                        .font(Theme.Typography.navTitle)
                    Text(engine.cloudActive ? "Danh sách đơn sẽ hiện sau vài giây." : "Ghép cloud bằng mật khẩu cửa hàng (Cài đặt → Cloud).")
                        .font(Theme.Typography.caption)
                        .foregroundStyle(.secondary)
                }
            }


            if !engine.cloudActive {
                VStack(alignment: .leading, spacing: 6) {
                    stepRow("1", "Vào Cài đặt → Cloud trên iPhone.")
                    stepRow("2", "Nhập mật khẩu cửa hàng và tên máy, bấm Ghép thiết bị.")
                }

                Button {
                    Haptics.tap()
                    router.tab = NotificationRouter.settingsTab
                } label: {
                    Label("Đi tới Cài đặt", systemImage: "gearshape.fill")
                        .font(Theme.Typography.secondaryBold)
                        .frame(maxWidth: .infinity, minHeight: Theme.minTapTarget)
                }
                .buttonStyle(.bordered)
                .tint(Theme.accent)
            } else {
                HStack(spacing: 8) {
                    Button {
                        Haptics.tap()
                        engine.requestSnapshot()
                    } label: {
                        Label("Xin lại danh sách", systemImage: "arrow.clockwise")
                            .font(Theme.Typography.secondaryBold)
                            .frame(maxWidth: .infinity, minHeight: Theme.minTapTarget)
                    }
                    .buttonStyle(.bordered)
                    .tint(Theme.accent)

                    Button {
                        Haptics.tap()
                        router.tab = NotificationRouter.settingsTab
                    } label: {
                        Text("Cài đặt")
                            .font(Theme.Typography.secondaryBold)
                            .frame(minHeight: Theme.minTapTarget)
                            .padding(.horizontal, 12)
                    }
                    .buttonStyle(.bordered)
                }
            }
        }
        .card(padding: Theme.cardPadding, radius: Theme.cardRadius)
    }

    private func stepRow(_ number: String, _ text: String) -> some View {
        HStack(alignment: .top, spacing: 8) {
            Text(number)
                .font(Theme.Typography.microBold)
                .foregroundColor(.white)
                .frame(width: 18, height: 18)
                .background(Theme.accent)
                .clipShape(Circle())
            Text(text)
                .font(Theme.Typography.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    // MARK: - Lối tắt thống kê
    private var statsShortcutCard: some View {
        NavigationLink {
            StatsView()
        } label: {
            HStack(spacing: 10) {
                IconTile(systemName: "chart.bar.xaxis", color: Theme.info, size: 30, cornerRadius: 8)
                VStack(alignment: .leading, spacing: 1) {
                    Text("Thống kê chi tiết")
                        .font(Theme.Typography.sectionTitle)
                        .foregroundColor(.primary)
                    Text("Theo ngày, tuần, tháng và so sánh các sàn")
                        .font(Theme.Typography.caption)
                        .foregroundStyle(.secondary)
                }
                Spacer()
                Image(systemName: "chevron.right")
                    .font(.caption.bold())
                    .foregroundStyle(.tertiary)
            }
            .frame(minHeight: Theme.minTapTarget)
            .card(padding: Theme.cardPadding, radius: Theme.cardRadius)
        }
        .buttonStyle(.plain)
    }
}
