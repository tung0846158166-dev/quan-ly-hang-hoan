import SwiftUI
import ScannerCore

/// Màn hình mở đầu "Tổng quan": nắm nhanh tình hình hàng hoàn, cảnh báo, đường ống 4 chặng, sàn và nút Quét ngay.
struct HomeView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @EnvironmentObject var router: NotificationRouter
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    @State private var summary = DashboardSummary()
    @State private var selectedRow: OrderRow?

    private let kpiColumns = [
        GridItem(.flexible(), spacing: 12),
        GridItem(.flexible(), spacing: 12)
    ]

    private func recompute() {
        let next = DashboardMetrics.compute(rows: store.rows, stats: store.stats, now: Date())
        if reduceMotion {
            summary = next
        } else {
            withAnimation(.easeOut(duration: 0.2)) { summary = next }
        }
    }

    private var connectionStatus: (text: String, color: Color) {
        if engine.room.isEmpty || !engine.channelUp {
            return ("Chưa kết nối", .secondary)
        }
        if engine.pcOnline {
            return ("PC đang online", Theme.success)
        }
        return ("PC offline", Theme.warning)
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: 16) {
                    headerCard

                    if engine.room.isEmpty {
                        emptyRoomCard
                    } else if !store.hasData {
                        waitingDataCard
                    } else {
                        if summary.discrepancyCount > 0 {
                            discrepancyCard
                        }
                        kpiGrid
                        returnWatchCard
                        urgentOrdersCard
                        platformsCard
                        partialReturnCard
                        if summary.has7DayData {
                            last7DaysCard
                        }
                        statsShortcutCard
                    }
                }
                .padding(.horizontal, 16)
                .padding(.top, 8)
                .padding(.bottom, 20)
            }
            .background(Theme.pageBackground.ignoresSafeArea())
            .refreshable {
                Haptics.tap()
                engine.requestSnapshot()
                try? await Task.sleep(nanoseconds: 1_500_000_000)
            }
            .safeAreaInset(edge: .bottom) {
                scanNowBar
            }
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .navigationBarLeading) {
                    Text("Tổng quan")
                        .font(.headline.bold())
                }
                ToolbarItemGroup(placement: .navigationBarTrailing) {
                    Button {
                        Haptics.tap()
                        engine.requestSnapshot()
                    } label: {
                        Image(systemName: "arrow.clockwise")
                            .font(.subheadline.bold())
                    }
                    .accessibilityLabel("Cập nhật dữ liệu từ máy tính")

                    Button {
                        Haptics.tap()
                        router.showNotifications = true
                    } label: {
                        ZStack(alignment: .topTrailing) {
                            Image(systemName: store.alertCount > 0 ? "bell.badge.fill" : "bell")
                                .font(.subheadline.bold())
                                .foregroundColor(store.alertCount > 0 ? Theme.accent : .primary)
                        }
                    }
                    .accessibilityLabel("Thông báo (\(store.alertCount))")
                }
            }
            .navigationDestination(isPresented: $router.showNotifications) {
                NotificationsView()
            }
            .sheet(item: $selectedRow) { DetailView(row: $0) }
            .onAppear { recompute() }
            .onChange(of: store.updatedAt) { _ in recompute() }
            .onChange(of: store.revision) { _ in recompute() }
            .onChange(of: store.rows.count) { _ in recompute() }
        }
        .sheet(item: $router.openRow) { DetailView(row: $0) }
    }

    // MARK: - Đầu trang
    private var headerCard: some View {
        let st = connectionStatus
        return VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .top) {
                VStack(alignment: .leading, spacing: 4) {
                    Text(DashboardMetrics.greeting(for: Date()))
                        .font(.title2.bold())
                        .foregroundColor(.primary)
                    if let updated = store.updatedAt {
                        Text("Cập nhật lúc \(updated.formatted(date: .omitted, time: .shortened)) · \(store.rows.count) đơn")
                            .font(.footnote)
                            .foregroundStyle(.secondary)
                    } else {
                        Text("Kéo xuống để xin danh sách từ máy tính")
                            .font(.footnote)
                            .foregroundStyle(.secondary)
                    }
                }
                Spacer(minLength: 8)
                HStack(spacing: 6) {
                    Circle()
                        .fill(st.color)
                        .frame(width: 8, height: 8)
                    Text(st.text)
                        .font(.caption.bold())
                        .foregroundColor(st.color)
                }
                .padding(.horizontal, 10)
                .padding(.vertical, 6)
                .background(st.color.opacity(0.13))
                .clipShape(Capsule())
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(padding: 16)
    }

    // MARK: - Trạng thái rỗng
    private var emptyRoomCard: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 12) {
                Image(systemName: "qrcode.viewfinder")
                    .font(.title)
                    .foregroundColor(Theme.accent)
                    .frame(width: 48, height: 48)
                    .background(Theme.accentSoft)
                    .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
                VStack(alignment: .leading, spacing: 2) {
                    Text("Chưa kết nối với máy tính")
                        .font(.headline.bold())
                    Text("Làm theo 2 bước để đồng bộ đơn hoàn:")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }

            VStack(alignment: .leading, spacing: 10) {
                stepRow(number: "1", text: "Mở app Quản lý hàng hoàn v2 trên máy tính, vào mục Đồng bộ điện thoại và sao chép mã phòng.")
                stepRow(number: "2", text: "Vào Cài đặt trên iPhone, dán mã phòng rồi bấm Lưu mã phòng.")
            }

            Button {
                Haptics.medium()
                router.tab = NotificationRouter.settingsTab
            } label: {
                Label("Đi tới Cài đặt", systemImage: "gearshape.fill")
                    .font(.subheadline.bold())
                    .frame(maxWidth: .infinity, minHeight: 46)
            }
            .buttonStyle(.borderedProminent)
            .tint(Theme.accent)
            .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
        }
        .card(padding: 18)
    }

    private var waitingDataCard: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 12) {
                Image(systemName: "arrow.triangle.2.circlepath.circle.fill")
                    .font(.title)
                    .foregroundColor(Theme.info)
                    .frame(width: 48, height: 48)
                    .background(Theme.info.opacity(0.14))
                    .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
                VStack(alignment: .leading, spacing: 2) {
                    Text("Chưa có dữ liệu đơn")
                        .font(.headline.bold())
                    Text(store.assembleInfo.isEmpty ? "Mở app PC v2 để gửi danh sách đơn sang iPhone." : store.assembleInfo)
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }

            HStack(spacing: 10) {
                Button {
                    Haptics.tap()
                    engine.requestSnapshot()
                } label: {
                    Label("Xin lại danh sách", systemImage: "arrow.clockwise")
                        .font(.subheadline.bold())
                        .frame(maxWidth: .infinity, minHeight: 44)
                }
                .buttonStyle(.borderedProminent)
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
        .card(padding: 18)
    }

    private func stepRow(number: String, text: String) -> some View {
        HStack(alignment: .top, spacing: 10) {
            Text(number)
                .font(.caption.bold())
                .foregroundColor(.white)
                .frame(width: 24, height: 24)
                .background(Theme.accent)
                .clipShape(Circle())
            Text(text)
                .font(.subheadline)
                .foregroundColor(.primary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    // MARK: - Cảnh báo lệch số đơn
    private var discrepancyCard: some View {
        HStack(alignment: .top, spacing: 10) {
            Image(systemName: "exclamationmark.triangle.fill")
                .font(.title3)
                .foregroundColor(Theme.warning)
            VStack(alignment: .leading, spacing: 4) {
                Text("Lệch \(summary.discrepancyCount) đơn so với máy tính")
                    .font(.subheadline.bold())
                    .foregroundColor(.primary)
                Text(discrepancyDetail)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                HStack(spacing: 12) {
                    Button("Tải lại danh sách") {
                        Haptics.tap()
                        engine.requestSnapshot()
                    }
                    .font(.caption.bold())
                    .foregroundColor(Theme.accent)

                    Button("Xem đối chiếu") {
                        Haptics.tap()
                        router.tab = NotificationRouter.settingsTab
                    }
                    .font(.caption.bold())
                    .foregroundStyle(.secondary)
                }
                .padding(.top, 2)
            }
            Spacer(minLength: 0)
        }
        .padding(14)
        .background(Theme.warning.opacity(0.12))
        .clipShape(RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous)
                .stroke(Theme.warning.opacity(0.35), lineWidth: 1)
        )
    }

    private var discrepancyDetail: String {
        var parts: [String] = []
        if summary.droppedCount > 0 {
            parts.append("\(summary.droppedCount) đơn thiếu mã trên PC nên không gửi sang")
        }
        if summary.sentMismatchCount > 0 {
            parts.append("lệch \(summary.sentMismatchCount) đơn giữa gói gửi và danh sách hiện có")
        }
        return parts.joined(separator: " · ")
    }

    // MARK: - 4 thẻ số lớn
    private var kpiGrid: some View {
        LazyVGrid(columns: kpiColumns, spacing: 12) {
            kpiCard(
                title: "Đang chờ về",
                value: summary.pendingCount,
                icon: "shippingbox.fill",
                color: Theme.accent,
                filter: .pending
            )
            kpiCard(
                title: "Đã nhận hôm nay",
                value: summary.receivedTodayCount,
                icon: "checkmark.seal.fill",
                color: Theme.success,
                filter: .received
            )
            kpiCard(
                title: "Quá hạn (đỏ)",
                value: summary.overdueCount,
                icon: "exclamationmark.triangle.fill",
                color: Theme.danger,
                filter: .overdue
            )
            kpiCard(
                title: "Sắp đến hạn (vàng)",
                value: summary.soonCount,
                icon: "clock.fill",
                color: Theme.warning,
                filter: .soon
            )
        }
    }

    private func kpiCard(title: String, value: Int, icon: String, color: Color, filter: OrderFilter) -> some View {
        Button {
            Haptics.tap()
            router.openOrders(filter: filter)
        } label: {
            VStack(alignment: .leading, spacing: 10) {
                HStack(spacing: 6) {
                    Image(systemName: icon)
                        .font(.caption.bold())
                        .foregroundColor(color)
                        .frame(width: 26, height: 26)
                        .background(color.opacity(0.15))
                        .clipShape(Circle())
                    Spacer(minLength: 4)
                    Image(systemName: "chevron.right")
                        .font(.caption2.bold())
                        .foregroundStyle(.tertiary)
                }
                AnimatedCounter(
                    value: value,
                    font: .system(size: 34, weight: .bold, design: .rounded),
                    color: value > 0 ? color : .primary
                )
                Text(title)
                    .font(.caption.bold())
                    .foregroundStyle(.secondary)
                    .lineLimit(2)
                    .minimumScaleFactor(0.85)
            }
            .frame(maxWidth: .infinity, minHeight: 104, alignment: .leading)
            .card(padding: 14)
        }
        .buttonStyle(.plain)
    }

    // MARK: - Hàng hoàn về 4 chặng
    private var returnWatchCard: some View {
        let rw = summary.returnWatch
        return NavigationLink {
            ReturnWatchView()
        } label: {
            VStack(alignment: .leading, spacing: 14) {
                HStack {
                    SectionTitleRow(
                        title: "Hàng hoàn về",
                        icon: "arrow.uturn.backward.circle.fill",
                        iconColor: Theme.teal,
                        subtitle: "\(rw.active) đang về"
                    )
                    Image(systemName: "chevron.right")
                        .font(.caption.bold())
                        .foregroundStyle(.tertiary)
                }

                HStack(spacing: 8) {
                    pipelineStage(step: 1, title: "Đã đồng ý", count: rw.stage1, color: Theme.info)
                    pipelineStage(step: 2, title: "Đang về", count: rw.stage2, color: Theme.teal)
                    pipelineStage(step: 3, title: "Về shop", count: rw.stage3, color: Theme.warning)
                    pipelineStage(step: 4, title: "Nhận kho", count: rw.stage4, color: Theme.success)
                }
            }
            .card(padding: 16)
        }
        .buttonStyle(.plain)
    }

    private func pipelineStage(step: Int, title: String, count: Int, color: Color) -> some View {
        VStack(spacing: 6) {
            Capsule()
                .fill(count > 0 ? color : Color.gray.opacity(0.22))
                .frame(height: 5)
            AnimatedCounter(
                value: count,
                font: .system(size: 22, weight: .bold, design: .rounded),
                color: count > 0 ? color : .secondary
            )
            Text(title)
                .font(.caption2.bold())
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .minimumScaleFactor(0.75)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 8)
        .padding(.horizontal, 4)
        .background(Color(.tertiarySystemGroupedBackground))
        .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
    }

    // MARK: - Cần xử lý ngay (tối đa 5 đơn)
    private var urgentOrdersCard: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                SectionTitleRow(
                    title: "Cần xử lý ngay",
                    icon: "flame.fill",
                    iconColor: Theme.danger,
                    subtitle: summary.urgentOrders.isEmpty ? nil : "Top \(summary.urgentOrders.count)"
                )
                if !summary.urgentOrders.isEmpty {
                    Button("Xem hết") {
                        Haptics.tap()
                        router.openOrders(filter: summary.overdueCount > 0 ? .overdue : .soon)
                    }
                    .font(.caption.bold())
                    .foregroundColor(Theme.accent)
                }
            }

            if summary.urgentOrders.isEmpty {
                HStack(spacing: 10) {
                    Image(systemName: "checkmark.circle.fill")
                        .font(.title3)
                        .foregroundColor(Theme.success)
                    Text("Không có đơn quá hạn hay sắp đến hạn.")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
                .padding(.vertical, 6)
            } else {
                VStack(spacing: 8) {
                    ForEach(summary.urgentOrders) { item in
                        urgentRow(item)
                    }
                }
            }
        }
        .card(padding: 16)
    }

    private func urgentRow(_ item: UrgentOrder) -> some View {
        let r = item.row
        let tint: Color = item.level == .overdue ? Theme.danger : Theme.warning
        return Button {
            Haptics.tap()
            selectedRow = r
        } label: {
            HStack(spacing: 10) {
                Circle()
                    .fill(tint)
                    .frame(width: 10, height: 10)
                VStack(alignment: .leading, spacing: 3) {
                    HStack(spacing: 6) {
                        PlatformBadge(platform: r.p)
                        Text(r.c)
                            .font(.system(.subheadline, design: .monospaced).bold())
                            .foregroundColor(.primary)
                            .lineLimit(1)
                    }
                    if !r.n.isEmpty {
                        Text(r.n)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                }
                Spacer(minLength: 6)
                StatusPill(text: item.deadlineText, color: tint)
            }
            .padding(10)
            .frame(minHeight: 52)
            .background(Color(.tertiarySystemGroupedBackground))
            .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
        }
        .buttonStyle(.plain)
    }

    // MARK: - Theo sàn
    private var platformsCard: some View {
        VStack(alignment: .leading, spacing: 12) {
            SectionTitleRow(
                title: "Theo sàn",
                icon: "storefront.fill",
                iconColor: Theme.accent,
                subtitle: "Đang chờ · Quá hạn"
            )

            VStack(spacing: 10) {
                ForEach(summary.platforms) { plat in
                    platformRow(plat)
                }
            }
        }
        .card(padding: 16)
    }

    private func platformRow(_ plat: PlatformSummary) -> some View {
        let tint = Theme.platformColor(plat.name)
        let ratio = summary.pendingRatio(for: plat)
        return Button {
            Haptics.tap()
            router.openOrders(filter: .pending, platform: plat.name)
        } label: {
            VStack(alignment: .leading, spacing: 6) {
                HStack {
                    Text(plat.name)
                        .font(.subheadline.bold())
                        .foregroundColor(.primary)
                    Spacer()
                    Text("\(plat.pending) chờ")
                        .font(.caption.bold())
                        .foregroundStyle(.secondary)
                    if plat.overdue > 0 {
                        StatusPill(text: "\(plat.overdue) quá hạn", color: Theme.danger)
                    }
                }
                GeometryReader { geo in
                    ZStack(alignment: .leading) {
                        Capsule()
                            .fill(Color.gray.opacity(0.16))
                        Capsule()
                            .fill(tint)
                            .frame(width: max(plat.pending > 0 ? 8 : 0, geo.size.width * CGFloat(ratio)))
                    }
                }
                .frame(height: 8)
            }
            .padding(.vertical, 4)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    // MARK: - Hoàn một phần
    private var partialReturnCard: some View {
        Button {
            Haptics.tap()
            router.openOrders(filter: .partialReturn)
        } label: {
            HStack(spacing: 12) {
                Image(systemName: "square.split.2x1.fill")
                    .font(.title3.bold())
                    .foregroundColor(Theme.accent)
                    .frame(width: 44, height: 44)
                    .background(Theme.accentSoft)
                    .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                VStack(alignment: .leading, spacing: 2) {
                    Text("Hoàn một phần")
                        .font(.subheadline.bold())
                        .foregroundColor(.primary)
                    Text("Đơn chỉ hoàn một phần sản phẩm trong kiện")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                }
                Spacer(minLength: 8)
                AnimatedCounter(
                    value: summary.partialReturnCount,
                    font: .system(size: 26, weight: .bold, design: .rounded),
                    color: summary.partialReturnCount > 0 ? Theme.accent : .secondary
                )
                Image(systemName: "chevron.right")
                    .font(.caption.bold())
                    .foregroundStyle(.tertiary)
            }
            .frame(minHeight: 56)
            .card(padding: 14)
        }
        .buttonStyle(.plain)
    }

    // MARK: - 7 ngày gần nhất (thuần SwiftUI HStack/Capsule)
    private var last7DaysCard: some View {
        VStack(alignment: .leading, spacing: 12) {
            SectionTitleRow(
                title: "7 ngày gần nhất",
                icon: "chart.bar.fill",
                iconColor: Theme.accent,
                subtitle: "Đơn đã nhận"
            )

            HStack(alignment: .bottom, spacing: 8) {
                ForEach(summary.last7Days) { day in
                    let ratio = summary.dailyRatio(for: day)
                    let barHeight = day.count > 0 ? max(10, CGFloat(ratio) * 84) : 4
                    VStack(spacing: 5) {
                        Text(day.count > 0 ? "\(day.count)" : "")
                            .font(.caption2.bold())
                            .foregroundColor(day.isToday ? Theme.accent : .secondary)
                            .monospacedDigit()
                            .frame(height: 14)

                        Capsule()
                            .fill(day.isToday ? Theme.accent : (day.count > 0 ? Theme.accent.opacity(0.42) : Color.gray.opacity(0.18)))
                            .frame(maxWidth: 24, minHeight: barHeight, maxHeight: barHeight)

                        Text(day.label)
                            .font(.caption2.bold())
                            .foregroundColor(day.isToday ? Theme.accent : .secondary)
                    }
                    .frame(maxWidth: .infinity)
                }
            }
            .frame(height: 126, alignment: .bottom)
        }
        .card(padding: 16)
    }

    // MARK: - Lối tắt Thống kê chi tiết
    private var statsShortcutCard: some View {
        NavigationLink {
            StatsView()
        } label: {
            HStack(spacing: 12) {
                Image(systemName: "chart.bar.xaxis")
                    .font(.body.bold())
                    .foregroundColor(Theme.info)
                    .frame(width: 38, height: 38)
                    .background(Theme.info.opacity(0.14))
                    .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
                VStack(alignment: .leading, spacing: 2) {
                    Text("Thống kê chi tiết theo ngày / tuần / tháng")
                        .font(.subheadline.bold())
                        .foregroundColor(.primary)
                    Text("So sánh đơn hoàn, đơn hủy và số về kho")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                Spacer()
                Image(systemName: "chevron.right")
                    .font(.caption.bold())
                    .foregroundStyle(.tertiary)
            }
            .frame(minHeight: 48)
            .card(padding: 14)
        }
        .buttonStyle(.plain)
    }

    // MARK: - Nút "Quét ngay" ở nửa dưới (dùng một tay)
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
            .frame(maxWidth: .infinity, minHeight: 52)
            .background(Theme.accent)
            .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
            .shadow(color: Theme.accent.opacity(0.32), radius: 10, x: 0, y: 4)
        }
        .buttonStyle(.plain)
        .padding(.horizontal, 16)
        .padding(.vertical, 8)
        .background(.ultraThinMaterial)
    }
}
