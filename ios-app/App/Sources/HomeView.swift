import SwiftUI
import ScannerCore

/// Màn hình mở đầu "Tổng quan" (Pass 2):
/// Hero tiến độ hôm nay, dải thẻ cuộn ngang (Hàng hoàn về / Theo sàn / Hoàn một phần),
/// Cần xử lý ngay (có vuốt Ghi chú / Theo dõi), biểu đồ 7 ngày có đường trung bình.
struct HomeView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @EnvironmentObject var router: NotificationRouter
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.colorScheme) private var scheme

    @State private var summary = DashboardSummary()
    @State private var productStats = ProductStatsSummary()
    @State private var selectedRow: OrderRow?
    @State private var showAllProductStats = false
    @State private var selectedProductStat: ProductStatItem?
    @State private var showSelectedProductStat = false

    private func recompute() {
        let next = DashboardMetrics.compute(rows: store.rows, stats: store.stats, now: Date())
        let nextProducts = ProductMetrics.compute(rows: store.rows)
        if reduceMotion {
            summary = next
            productStats = nextProducts
        } else {
            withAnimation(.easeOut(duration: 0.22)) {
                summary = next
                productStats = nextProducts
            }
        }
    }

    private var connectionStatus: (text: String, color: Color) {
        if store.isDemoMode {
            return ("Dữ liệu mẫu", Theme.accent)
        }
        if engine.room.isEmpty || !engine.channelUp {
            return ("Chưa kết nối", .secondary)
        }
        if engine.pcOnline {
            return ("PC online", Theme.success)
        }
        return ("PC offline", Theme.warning)
    }

    private var formattedToday: String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "vi_VN")
        f.dateFormat = "EEEE, dd/MM"
        let s = f.string(from: Date())
        return s.prefix(1).uppercased() + s.dropFirst()
    }

    var body: some View {
        NavigationStack {
            List {
                if !store.hasData {
                    emptyStateSection
                        .listRowInsets(EdgeInsets(top: 12, leading: 16, bottom: 12, trailing: 16))
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)
                } else {
                    heroCard
                        .listRowInsets(EdgeInsets(top: 8, leading: 16, bottom: 8, trailing: 16))
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)

                    if summary.discrepancyCount > 0 {
                        discrepancyCard
                            .listRowInsets(EdgeInsets(top: 4, leading: 16, bottom: 8, trailing: 16))
                            .listRowBackground(Color.clear)
                            .listRowSeparator(.hidden)
                    }

                    horizontalDeck
                        .listRowInsets(EdgeInsets(top: 6, leading: 0, bottom: 10, trailing: 0))
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)

                    urgentHeader
                        .listRowInsets(EdgeInsets(top: 8, leading: 16, bottom: 6, trailing: 16))
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)

                    if summary.urgentOrders.isEmpty {
                        urgentEmptyCard
                            .listRowInsets(EdgeInsets(top: 2, leading: 16, bottom: 10, trailing: 16))
                            .listRowBackground(Color.clear)
                            .listRowSeparator(.hidden)
                    } else {
                        ForEach(summary.urgentOrders) { item in
                            urgentRow(item)
                                .listRowInsets(EdgeInsets(top: 4, leading: 16, bottom: 4, trailing: 16))
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

                    if summary.has7DayData {
                        last7DaysCard
                            .listRowInsets(EdgeInsets(top: 12, leading: 16, bottom: 10, trailing: 16))
                            .listRowBackground(Color.clear)
                            .listRowSeparator(.hidden)
                    }

                    if !productStats.products.isEmpty {
                        topProductsCard
                            .listRowInsets(EdgeInsets(top: 6, leading: 16, bottom: 10, trailing: 16))
                            .listRowBackground(Color.clear)
                            .listRowSeparator(.hidden)
                    }

                    statsShortcutCard
                        .listRowInsets(EdgeInsets(top: 4, leading: 16, bottom: 20, trailing: 16))
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
            .safeAreaInset(edge: .bottom) {
                scanNowBar
            }
            .navigationTitle("Tổng quan")
            .navigationBarTitleDisplayMode(.large)
            .toolbar {
                ToolbarItemGroup(placement: .navigationBarTrailing) {
                    if !store.isDemoMode {
                        Button {
                            Haptics.tap()
                            engine.requestSnapshot()
                        } label: {
                            Image(systemName: "arrow.clockwise")
                                .font(.body.weight(.semibold))
                        }
                        .accessibilityLabel("Cập nhật dữ liệu từ máy tính")
                    }

                    Button {
                        Haptics.tap()
                        router.showNotifications = true
                    } label: {
                        Image(systemName: store.alertCount > 0 ? "bell.badge.fill" : "bell")
                            .font(.body.weight(.semibold))
                            .foregroundColor(store.alertCount > 0 ? Theme.accent : .primary)
                    }
                    .accessibilityLabel("Thông báo (\(store.alertCount))")
                }
            }
            .navigationDestination(isPresented: $router.showNotifications) {
                NotificationsView()
            }
            .navigationDestination(isPresented: $showAllProductStats) {
                ProductStatsView()
            }
            .navigationDestination(isPresented: $showSelectedProductStat) {
                if let stat = selectedProductStat {
                    ProductStatDetailView(stat: stat, sourceRows: store.rows)
                }
            }
            .sheet(item: $selectedRow) { DetailView(row: $0) }
            .onAppear { recompute() }
            .onChange(of: store.updatedAt) { _ in recompute() }
            .onChange(of: store.revision) { _ in recompute() }
            .onChange(of: store.rows.count) { _ in recompute() }
            .onChange(of: store.isDemoMode) { _ in recompute() }
        }
        .sheet(item: $router.openRow) { DetailView(row: $0) }
    }

    // MARK: - Khối Hero trên cùng
    private var heroCard: some View {
        let st = connectionStatus
        let ringSize: CGFloat = 112
        return VStack(alignment: .leading, spacing: 18) {
            HStack(alignment: .center) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Hôm nay")
                        .font(.title2.bold())
                        .foregroundColor(.primary)
                    Text(formattedToday)
                        .font(.footnote.weight(.medium))
                        .foregroundStyle(.secondary)
                }
                Spacer(minLength: 8)
                HStack(spacing: 6) {
                    Circle().fill(st.color).frame(width: 8, height: 8)
                    Text(st.text)
                        .font(.caption.bold())
                        .foregroundColor(st.color)
                }
                .padding(.horizontal, 10)
                .padding(.vertical, 6)
                .background(st.color.opacity(0.14))
                .clipShape(Capsule())
            }

            HStack(spacing: 18) {
                // Vòng tiến độ hôm nay
                Button {
                    Haptics.tap()
                    router.openOrders(filter: .received)
                } label: {
                    ZStack {
                        Circle()
                            .stroke(Theme.accent.opacity(scheme == .dark ? 0.22 : 0.16), lineWidth: 11)
                        Circle()
                            .trim(from: 0, to: CGFloat(max(summary.todayProgress, summary.receivedTodayCount > 0 ? 0.05 : 0)))
                            .stroke(
                                LinearGradient(
                                    colors: [Theme.accent, Theme.warning],
                                    startPoint: .topLeading,
                                    endPoint: .bottomTrailing
                                ),
                                style: StrokeStyle(lineWidth: 11, lineCap: .round)
                            )
                            .rotationEffect(.degrees(-90))

                        VStack(spacing: 1) {
                            AnimatedCounter(
                                value: summary.receivedTodayCount,
                                font: .system(size: 30, weight: .bold, design: .rounded),
                                color: .primary
                            )
                            Text("/ \(summary.todayTargetCount)")
                                .font(.system(.caption, design: .rounded).bold())
                                .foregroundStyle(.secondary)
                        }
                    }
                    .frame(width: ringSize, height: ringSize)
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Đã nhận \(summary.receivedTodayCount) trên \(summary.todayTargetCount) đơn hôm nay")

                // 2 con số phụ bên phải (quá hạn, sắp hạn) + còn chờ
                VStack(alignment: .leading, spacing: 10) {
                    Text("Đã nhận \(summary.receivedTodayCount) / Cần nhận \(summary.todayTargetCount)")
                        .font(.caption.bold())
                        .foregroundStyle(.secondary)
                        .lineLimit(2)

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

                    Button {
                        Haptics.tap()
                        router.openOrders(filter: .pending)
                    } label: {
                        HStack(spacing: 6) {
                            Circle().fill(Theme.accent).frame(width: 7, height: 7)
                            Text("Còn chờ \(summary.pendingCount) đơn")
                                .font(.caption.bold())
                                .foregroundStyle(.secondary)
                            Image(systemName: "chevron.right")
                                .font(.caption2.bold())
                                .foregroundStyle(.tertiary)
                        }
                    }
                    .buttonStyle(.plain)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .padding(18)
        .background {
            RoundedRectangle(cornerRadius: 24, style: .continuous)
                .fill(Theme.cardBackground)
                .overlay(
                    RoundedRectangle(cornerRadius: 24, style: .continuous)
                        .fill(
                            LinearGradient(
                                colors: [
                                    Theme.accent.opacity(scheme == .dark ? 0.22 : 0.14),
                                    Theme.accent.opacity(scheme == .dark ? 0.04 : 0.02)
                                ],
                                startPoint: .topLeading,
                                endPoint: .bottomTrailing
                            )
                        )
                )
        }
        .clipShape(RoundedRectangle(cornerRadius: 24, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: 24, style: .continuous)
                .stroke(Theme.accent.opacity(scheme == .dark ? 0.30 : 0.20), lineWidth: 0.9)
        )
    }

    private func heroSubMetric(title: String, value: Int, color: Color, action: @escaping () -> Void) -> some View {
        Button {
            Haptics.tap()
            action()
        } label: {
            HStack(spacing: 8) {
                Circle()
                    .fill(color)
                    .frame(width: 9, height: 9)
                Text(title)
                    .font(.subheadline.weight(.medium))
                    .foregroundColor(.primary)
                Spacer(minLength: 4)
                AnimatedCounter(
                    value: value,
                    font: .system(size: 20, weight: .bold, design: .rounded),
                    color: value > 0 ? color : .secondary
                )
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 8)
            .frame(minHeight: 40)
            .background(Theme.subtleFill)
            .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
        }
        .buttonStyle(.plain)
    }

    // MARK: - Dải thẻ cuộn ngang (Hàng hoàn về / Theo sàn / Hoàn một phần)
    private var horizontalDeck: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 14) {
                returnWatchDeckCard
                    .frame(width: 300)
                platformsDeckCard
                    .frame(width: 300)
                partialReturnDeckCard
                    .frame(width: 224)
            }
            .snapTargetLayoutIfAvailable()
            .padding(.horizontal, 16)
            .padding(.vertical, 4)
        }
        .snapPagingIfAvailable()
    }

    private var returnWatchDeckCard: some View {
        let rw = summary.returnWatch
        let total = max(1, rw.total)
        return NavigationLink {
            ReturnWatchView()
        } label: {
            VStack(alignment: .leading, spacing: 14) {
                HStack {
                    IconTile(systemName: "arrow.uturn.backward", color: Theme.teal, size: 30, cornerRadius: 8)
                    VStack(alignment: .leading, spacing: 1) {
                        Text("Hàng hoàn về")
                            .font(.subheadline.bold())
                            .foregroundColor(.primary)
                        Text("\(rw.active) đang về kho")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                    Spacer()
                    Image(systemName: "chevron.right")
                        .font(.caption.bold())
                        .foregroundStyle(.tertiary)
                }

                // Thanh liền 4 chặng
                GeometryReader { geo in
                    let w = geo.size.width
                    HStack(spacing: 2) {
                        if rw.total == 0 {
                            Capsule().fill(Color.gray.opacity(0.2))
                        } else {
                            if rw.stage1 > 0 {
                                Rectangle().fill(Theme.info).frame(width: max(8, w * CGFloat(rw.stage1) / CGFloat(total)))
                            }
                            if rw.stage2 > 0 {
                                Rectangle().fill(Theme.teal).frame(width: max(8, w * CGFloat(rw.stage2) / CGFloat(total)))
                            }
                            if rw.stage3 > 0 {
                                Rectangle().fill(Theme.warning).frame(width: max(8, w * CGFloat(rw.stage3) / CGFloat(total)))
                            }
                            if rw.stage4 > 0 {
                                Rectangle().fill(Theme.success).frame(width: max(8, w * CGFloat(rw.stage4) / CGFloat(total)))
                            }
                        }
                    }
                    .clipShape(Capsule())
                }
                .frame(height: 10)

                HStack(spacing: 6) {
                    stageMini("Đồng ý", rw.stage1, Theme.info)
                    stageMini("Đang về", rw.stage2, Theme.teal)
                    stageMini("Về shop", rw.stage3, Theme.warning)
                    stageMini("Nhận kho", rw.stage4, Theme.success)
                }
            }
            .frame(maxWidth: .infinity, minHeight: 138, alignment: .leading)
            .card(padding: 16)
        }
        .buttonStyle(.plain)
    }

    private func stageMini(_ title: String, _ count: Int, _ color: Color) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text("\(count)")
                .font(.system(size: 18, weight: .bold, design: .rounded))
                .foregroundColor(count > 0 ? color : .secondary)
                .monospacedDigit()
            Text(title)
                .font(.caption2)
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .minimumScaleFactor(0.8)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var platformsDeckCard: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                IconTile(systemName: "storefront.fill", color: Theme.accent, size: 30, cornerRadius: 8)
                VStack(alignment: .leading, spacing: 1) {
                    Text("Theo sàn")
                        .font(.subheadline.bold())
                        .foregroundColor(.primary)
                    Text("Chạm sàn để lọc đơn")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                Spacer()
            }

            HStack(spacing: 8) {
                ForEach(summary.platforms.prefix(3)) { plat in
                    let tint = Theme.platformColor(plat.name)
                    Button {
                        Haptics.tap()
                        router.openOrders(filter: .pending, platform: plat.name)
                    } label: {
                        VStack(alignment: .leading, spacing: 5) {
                            HStack(spacing: 4) {
                                Circle().fill(tint).frame(width: 7, height: 7)
                                Text(plat.name)
                                    .font(.caption2.bold())
                                    .foregroundColor(.primary)
                                    .lineLimit(1)
                            }
                            Text("\(plat.pending)")
                                .font(.system(size: 22, weight: .bold, design: .rounded))
                                .foregroundColor(.primary)
                                .monospacedDigit()
                            Text(plat.overdue > 0 ? "\(plat.overdue) quá hạn" : "Ổn định")
                                .font(.caption2.bold())
                                .foregroundColor(plat.overdue > 0 ? Theme.danger : .secondary)
                                .lineLimit(1)
                                .minimumScaleFactor(0.8)
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(10)
                        .background(Theme.subtleFill)
                        .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                    }
                    .buttonStyle(.plain)
                }
            }
        }
        .frame(maxWidth: .infinity, minHeight: 138, alignment: .leading)
        .card(padding: 16)
    }

    private var partialReturnDeckCard: some View {
        Button {
            Haptics.tap()
            router.openOrders(filter: .partialReturn)
        } label: {
            VStack(alignment: .leading, spacing: 12) {
                HStack {
                    IconTile(systemName: "square.split.2x1.fill", color: Theme.purple, size: 30, cornerRadius: 8)
                    Spacer()
                    Image(systemName: "chevron.right")
                        .font(.caption.bold())
                        .foregroundStyle(.tertiary)
                }

                AnimatedCounter(
                    value: summary.partialReturnCount,
                    font: .system(size: 34, weight: .bold, design: .rounded),
                    color: summary.partialReturnCount > 0 ? Theme.accent : .secondary
                )

                VStack(alignment: .leading, spacing: 2) {
                    Text("Hoàn một phần")
                        .font(.subheadline.bold())
                        .foregroundColor(.primary)
                    Text("Giữ đủ SP, tô màu món hoàn")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }
            .frame(maxWidth: .infinity, minHeight: 138, alignment: .leading)
            .card(padding: 16)
        }
        .buttonStyle(.plain)
    }

    // MARK: - Cần xử lý ngay
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
                .font(.caption.bold())
                .foregroundColor(Theme.accent)
                .buttonStyle(.plain)
            }
        }
    }

    private var urgentEmptyCard: some View {
        HStack(spacing: 12) {
            IconTile(systemName: "checkmark", color: Theme.success, size: 32, cornerRadius: 10)
            VStack(alignment: .leading, spacing: 2) {
                Text("Mọi thứ đang ổn")
                    .font(.subheadline.bold())
                Text("Không có đơn quá hạn hay sắp đến hạn.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer()
        }
        .card(padding: 14)
    }

    private func urgentRow(_ item: UrgentOrder) -> some View {
        let r = item.row
        let tint: Color = item.level == .overdue ? Theme.danger : Theme.warning
        return Button {
            Haptics.tap()
            selectedRow = r
        } label: {
            HStack(spacing: 12) {
                RoundedRectangle(cornerRadius: 3, style: .continuous)
                    .fill(tint)
                    .frame(width: 4)
                    .padding(.vertical, 2)

                VStack(alignment: .leading, spacing: 4) {
                    HStack(spacing: 6) {
                        PlatformBadge(platform: r.p)
                        Text(r.c)
                            .font(.system(.subheadline, design: .monospaced).bold())
                            .foregroundColor(.primary)
                            .lineLimit(1)
                        if r.wl {
                            Image(systemName: "star.fill")
                                .font(.caption2)
                                .foregroundColor(Theme.warning)
                        }
                    }
                    if !r.n.isEmpty {
                        Text(r.n)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                }

                Spacer(minLength: 6)

                Text(item.deadlineText)
                    .font(.caption.bold())
                    .foregroundColor(tint)
                    .multilineTextAlignment(.trailing)
                    .lineLimit(2)
            }
            .frame(minHeight: 54)
            .card(padding: 12, radius: 16)
        }
        .buttonStyle(.plain)
    }

    // MARK: - 7 ngày gần nhất (cột + đường trung bình)
    private var last7DaysCard: some View {
        let avgRatio = summary.average7DaysRatio
        return VStack(alignment: .leading, spacing: 14) {
            HStack {
                SectionTitleRow(
                    title: "7 ngày gần nhất",
                    icon: "chart.bar.fill",
                    iconColor: Theme.accent
                )
                StatusPill(text: String(format: "TB %.1f/ngày", summary.average7Days), color: Theme.accent)
            }

            GeometryReader { geo in
                let barAreaHeight: CGFloat = 88
                let avgY = barAreaHeight * (1 - CGFloat(avgRatio))

                ZStack(alignment: .top) {
                    // Đường trung bình nét đứt
                    if summary.average7Days > 0 {
                        Path { path in
                            path.move(to: CGPoint(x: 0, y: avgY + 18))
                            path.addLine(to: CGPoint(x: geo.size.width, y: avgY + 18))
                        }
                        .stroke(Theme.accent.opacity(0.55), style: StrokeStyle(lineWidth: 1.2, dash: [5, 4]))
                    }

                    HStack(alignment: .bottom, spacing: 8) {
                        ForEach(summary.last7Days) { day in
                            let ratio = summary.dailyRatio(for: day)
                            let barH = day.count > 0 ? max(10, CGFloat(ratio) * barAreaHeight) : 4
                            VStack(spacing: 5) {
                                Text(day.count > 0 ? "\(day.count)" : "")
                                    .font(.system(.caption2, design: .rounded).bold())
                                    .foregroundColor(day.isToday ? Theme.accent : .secondary)
                                    .monospacedDigit()
                                    .frame(height: 14)

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
                                    .frame(maxWidth: 24, minHeight: barH, maxHeight: barH)

                                Text(day.label)
                                    .font(.caption2.bold())
                                    .foregroundColor(day.isToday ? Theme.accent : .secondary)
                            }
                            .frame(maxWidth: .infinity)
                        }
                    }
                    .frame(height: barAreaHeight + 38, alignment: .bottom)
                }
            }
            .frame(height: 128)
        }
        .card(padding: 16)
    }

    // MARK: - Cảnh báo lệch số đơn
    private var discrepancyCard: some View {
        HStack(alignment: .top, spacing: 10) {
            IconTile(systemName: "exclamationmark.triangle.fill", color: Theme.warning, size: 30, cornerRadius: 8)
            VStack(alignment: .leading, spacing: 4) {
                Text("Lệch \(summary.discrepancyCount) đơn so với máy tính")
                    .font(.subheadline.bold())
                Text(discrepancyDetail)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                HStack(spacing: 14) {
                    Button("Tải lại") {
                        Haptics.tap()
                        engine.requestSnapshot()
                    }
                    .font(.caption.bold())
                    .foregroundColor(Theme.accent)
                    .buttonStyle(.plain)

                    Button("Xem đối chiếu") {
                        Haptics.tap()
                        router.tab = NotificationRouter.settingsTab
                    }
                    .font(.caption.bold())
                    .foregroundStyle(.secondary)
                    .buttonStyle(.plain)
                }
                .padding(.top, 2)
            }
            Spacer(minLength: 0)
        }
        .padding(14)
        .background(Theme.warning.opacity(0.12))
        .clipShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: 18, style: .continuous)
                .stroke(Theme.warning.opacity(0.32), lineWidth: 0.8)
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
        VStack(alignment: .leading, spacing: 18) {
            HStack(spacing: 14) {
                IconTile(systemName: "shippingbox.fill", color: Theme.accent, size: 48, cornerRadius: 14)
                VStack(alignment: .leading, spacing: 3) {
                    Text(engine.room.isEmpty ? "Chưa nối với máy tính" : "Đang chờ dữ liệu đơn")
                        .font(.title3.bold())
                    Text(engine.room.isEmpty
                         ? "Nhập mã phòng từ PC v2 hoặc xem thử ngay với dữ liệu mẫu."
                         : (store.assembleInfo.isEmpty ? "Mở app PC v2 để gửi danh sách đơn sang iPhone." : store.assembleInfo))
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }

            Button {
                Haptics.medium()
                engine.setDemoMode(true)
            } label: {
                Label("Xem thử với dữ liệu mẫu", systemImage: "sparkles")
                    .font(.subheadline.bold())
                    .frame(maxWidth: .infinity, minHeight: 48)
            }
            .buttonStyle(.borderedProminent)
            .tint(Theme.accent)
            .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))

            if engine.room.isEmpty {
                VStack(alignment: .leading, spacing: 10) {
                    stepRow("1", "Mở app Quản lý hàng hoàn v2 trên máy tính → Đồng bộ điện thoại → sao chép mã phòng.")
                    stepRow("2", "Vào Cài đặt trên iPhone, dán mã phòng và bấm Lưu.")
                }
                .padding(.top, 2)

                Button {
                    Haptics.tap()
                    router.tab = NotificationRouter.settingsTab
                } label: {
                    Label("Đi tới Cài đặt", systemImage: "gearshape.fill")
                        .font(.subheadline.bold())
                        .frame(maxWidth: .infinity, minHeight: 44)
                }
                .buttonStyle(.bordered)
                .tint(Theme.accent)
            } else {
                HStack(spacing: 10) {
                    Button {
                        Haptics.tap()
                        engine.requestSnapshot()
                    } label: {
                        Label("Xin lại danh sách", systemImage: "arrow.clockwise")
                            .font(.subheadline.bold())
                            .frame(maxWidth: .infinity, minHeight: 44)
                    }
                    .buttonStyle(.bordered)
                    .tint(Theme.accent)

                    Button {
                        Haptics.tap()
                        router.tab = NotificationRouter.settingsTab
                    } label: {
                        Text("Cài đặt")
                            .font(.subheadline.bold())
                            .frame(minHeight: 44)
                            .padding(.horizontal, 12)
                    }
                    .buttonStyle(.bordered)
                }
            }
        }
        .card(padding: 20, radius: 22)
    }

    private func stepRow(_ number: String, _ text: String) -> some View {
        HStack(alignment: .top, spacing: 10) {
            Text(number)
                .font(.caption.bold())
                .foregroundColor(.white)
                .frame(width: 22, height: 22)
                .background(Theme.accent)
                .clipShape(Circle())
            Text(text)
                .font(.footnote)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    // MARK: - Top 5 Sản phẩm hoàn nhiều nhất
    private var topProductsCard: some View {
        let top5 = Array(productStats.products.prefix(5))
        return VStack(alignment: .leading, spacing: 12) {
            HStack {
                SectionTitleRow(
                    title: "Sản phẩm hoàn nhiều nhất",
                    icon: "tshirt.fill",
                    iconColor: Theme.accent
                )
                Button {
                    Haptics.tap()
                    showAllProductStats = true
                } label: {
                    Text("Xem tất cả")
                        .font(.caption.bold())
                        .foregroundColor(Theme.accent)
                }
                .buttonStyle(.plain)
            }

            VStack(spacing: 10) {
                ForEach(Array(top5.enumerated()), id: \.element.id) { idx, item in
                    let rank = idx + 1
                    let barColor: Color = rank == 1 ? Theme.accent : (rank == 2 ? Theme.warning : (rank == 3 ? Theme.teal : Theme.info))
                    let pct = Int((item.share * 100).rounded())

                    Button {
                        Haptics.tap()
                        selectedProductStat = item
                        showSelectedProductStat = true
                    } label: {
                        VStack(alignment: .leading, spacing: 5) {
                            HStack(spacing: 8) {
                                Text("\(rank)")
                                    .font(.system(.caption2, design: .rounded).bold())
                                    .foregroundColor(rank <= 3 ? .white : .secondary)
                                    .frame(width: 20, height: 20)
                                    .background(rank <= 3 ? barColor : Theme.subtleFill)
                                    .clipShape(RoundedRectangle(cornerRadius: 6, style: .continuous))

                                Text(item.name)
                                    .font(.subheadline.weight(.medium))
                                    .foregroundColor(.primary)
                                    .lineLimit(1)

                                Spacer(minLength: 4)

                                Text("\(item.units) cái")
                                    .font(.system(.caption, design: .rounded).bold())
                                    .foregroundColor(.primary)
                                    .monospacedDigit()

                                Text("(\(pct)%)")
                                    .font(.caption2)
                                    .foregroundStyle(.secondary)
                                    .monospacedDigit()
                            }

                            GeometryReader { geo in
                                ZStack(alignment: .leading) {
                                    Capsule().fill(Theme.subtleFill)
                                    Capsule()
                                        .fill(barColor.opacity(0.85))
                                        .frame(width: max(8, geo.size.width * CGFloat(min(1.0, item.share))))
                                }
                            }
                            .frame(height: 6)
                        }
                        .frame(minHeight: 44)
                    }
                    .buttonStyle(.plain)
                }
            }
        }
        .card(padding: 16)
    }

    // MARK: - Lối tắt thống kê
    private var statsShortcutCard: some View {
        NavigationLink {
            StatsView()
        } label: {
            HStack(spacing: 12) {
                IconTile(systemName: "chart.bar.xaxis", color: Theme.info, size: 36, cornerRadius: 10)
                VStack(alignment: .leading, spacing: 2) {
                    Text("Thống kê chi tiết")
                        .font(.subheadline.bold())
                        .foregroundColor(.primary)
                    Text("Xem theo ngày, tuần, tháng và so sánh các sàn")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                Spacer()
                Image(systemName: "chevron.right")
                    .font(.caption.bold())
                    .foregroundStyle(.tertiary)
            }
            .frame(minHeight: 48)
            .card(padding: 14, radius: 18)
        }
        .buttonStyle(.plain)
    }

    // MARK: - Nút "Quét ngay" cạnh dưới
    private var scanNowBar: some View {
        Button {
            Haptics.medium()
            router.tab = NotificationRouter.scanTab
        } label: {
            HStack(spacing: 10) {
                Image(systemName: "barcode.viewfinder")
                    .font(.title3.bold())
                Text("Quét ngay")
                    .font(.headline.bold())
            }
            .foregroundColor(.white)
            .frame(maxWidth: .infinity, minHeight: 50)
            .background(
                LinearGradient(
                    colors: [Theme.accent, Theme.accent.opacity(0.86)],
                    startPoint: .leading,
                    endPoint: .trailing
                )
            )
            .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
            .shadow(color: Theme.accent.opacity(0.30), radius: 10, x: 0, y: 4)
        }
        .buttonStyle(.plain)
        .padding(.horizontal, 16)
        .padding(.vertical, 8)
        .background(.ultraThinMaterial)
    }
}
