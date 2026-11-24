import SwiftUI
import ScannerCore

/// Màn Thống kê sản phẩm đầy đủ: xếp hạng sản phẩm bị hoàn nhiều nhất,
/// lọc nhanh theo sàn (Tất cả / Shopee / TikTok / Lazada), khoảng thời gian dạng chip gọn
/// (Hôm nay / 7 ngày / 30 ngày / Tháng này / Tất cả / Chọn ngày từ–đến), căn cứ ngày (Ngày hoàn / Ngày nhận kho),
/// và chế độ xem "Theo ngày" bấm vào từng ngày để lọc sản phẩm của ngày đó.
struct ProductStatsView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private enum ViewMode: String, CaseIterable, Identifiable {
        case ranking = "Xếp hạng"
        case byDay = "Theo ngày"
        var id: String { rawValue }
    }

    @State private var viewMode: ViewMode = .ranking
    @State private var platformFilter: ProductPlatformFilter = .all
    @State private var dateRange: ProductDateRange
    @State private var dateBasis: ProductDateBasis
    @State private var customFrom: Date
    @State private var customTo: Date
    @State private var selectedDay: Date? = nil
    @State private var summary = ProductStatsSummary()
    @State private var filteredRows: [OrderRow] = []
    @State private var dailyBuckets: [DailyProductBucket] = []
    @State private var hasDates = false

    init(
        initialDateRange: ProductDateRange = .all,
        initialDateBasis: ProductDateBasis = .returnDate
    ) {
        let now = Date()
        let weekAgo = Calendar.current.date(byAdding: .day, value: -6, to: now) ?? now
        _dateRange = State(initialValue: initialDateRange)
        _dateBasis = State(initialValue: initialDateBasis)
        _customFrom = State(initialValue: weekAgo)
        _customTo = State(initialValue: now)
    }

    private func recompute() {
        let all = store.rows
        let datesAvailable = ProductMetrics.hasAnyDate(rows: all)
        hasDates = datesAvailable
        let activeRange: ProductDateRange = datesAvailable ? dateRange : .all

        let rangeRows = ProductMetrics.filter(
            rows: all,
            platform: platformFilter,
            dateRange: activeRange,
            customFrom: customFrom,
            customTo: customTo,
            dateBasis: dateBasis,
            now: Date()
        )
        dailyBuckets = ProductMetrics.groupByDay(rows: rangeRows, dateBasis: dateBasis)

        let effectiveRows: [OrderRow]
        if let day = selectedDay {
            effectiveRows = ProductMetrics.filter(
                rows: all,
                onDay: day,
                platform: platformFilter,
                dateBasis: dateBasis
            )
        } else {
            effectiveRows = rangeRows
        }
        filteredRows = effectiveRows
        summary = ProductMetrics.compute(rows: effectiveRows)
    }

    private var activeRangeDescription: String {
        if let day = selectedDay {
            return "Ngày \(OrderRow.formatDisplayDate(day, includeTime: false))"
        }
        if dateRange == .custom {
            let a = min(customFrom, customTo)
            let b = max(customFrom, customTo)
            let fa = OrderRow.formatDisplayDate(a, includeTime: false)
            let fb = OrderRow.formatDisplayDate(b, includeTime: false)
            return fa == fb ? "Ngày \(fa)" : "\(fa) – \(fb)"
        }
        return dateRange.rawValue
    }

    private var activeSummaryLine: String {
        if let day = selectedDay {
            let label = "trong ngày \(OrderRow.formatDisplayDate(day, includeTime: false))"
            return ProductMetrics.rangeSummaryText(for: summary, scopeLabel: label)
        }
        return ProductMetrics.rangeSummaryText(for: summary)
    }

    var body: some View {
        ScrollView {
            VStack(spacing: Theme.sectionSpacing) {
                if !store.hasData {
                    emptyNoDataCard
                } else {
                    filterControls

                    if let day = selectedDay {
                        selectedDayBanner(day)
                    }

                    overviewCard

                    if viewMode == .byDay {
                        if dailyBuckets.isEmpty {
                            emptyDailyCard
                        } else {
                            dailyList
                        }
                    } else if summary.products.isEmpty {
                        emptyFilterCard
                    } else {
                        rankingList
                    }
                }
            }
            .padding(.horizontal, Theme.screenPadding)
            .padding(.top, 8)
            .padding(.bottom, 24)
        }
        .background(Theme.pageBackground.ignoresSafeArea())
        .navigationTitle("Thống kê sản phẩm")
        .navigationBarTitleDisplayMode(.inline)
        .onAppear { recompute() }
        .onChange(of: platformFilter) { _ in recompute() }
        .onChange(of: dateRange) { _ in recompute() }
        .onChange(of: dateBasis) { _ in recompute() }
        .onChange(of: customFrom) { _ in recompute() }
        .onChange(of: customTo) { _ in recompute() }
        .onChange(of: selectedDay) { _ in recompute() }
        .onChange(of: store.updatedAt) { _ in recompute() }
        .onChange(of: store.revision) { _ in recompute() }
        .onChange(of: store.rows.count) { _ in recompute() }
        .onChange(of: store.isDemoMode) { _ in recompute() }
    }

    // MARK: - Bộ lọc chế độ xem, khoảng thời gian, căn cứ ngày & sàn
    private var filterControls: some View {
        VStack(spacing: 8) {
            if hasDates {
                Picker("Chế độ xem", selection: $viewMode) {
                    ForEach(ViewMode.allCases) { m in
                        Text(m.rawValue).tag(m)
                    }
                }
                .pickerStyle(.segmented)
                .onChange(of: viewMode) { newMode in
                    Haptics.tap()
                    if newMode == .byDay {
                        selectedDay = nil
                    }
                }

                // Hàng chip khoảng thời gian gọn: Hôm nay / 7 ngày / 30 ngày / Tháng này / Tất cả / Chọn ngày
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 6) {
                        ForEach(ProductDateRange.allCases) { range in
                            let isSelected = selectedDay == nil && dateRange == range
                            compactFilterChip(
                                title: range.rawValue,
                                icon: range == .custom ? "calendar" : nil,
                                selected: isSelected,
                                accent: Theme.accent
                            ) {
                                selectedDay = nil
                                dateRange = range
                            }
                        }
                    }
                }

                // Bộ chọn ngày từ – đến khi chọn "Chọn ngày"
                if dateRange == .custom && selectedDay == nil {
                    customDatePickerCard
                }

                // Hàng chọn căn cứ ngày (mặc định Ngày hoàn, có thể đổi sang Ngày nhận kho)
                HStack(spacing: 6) {
                    Image(systemName: "calendar.badge.clock")
                        .font(.caption2.bold())
                        .foregroundStyle(.secondary)
                    Text("Tính theo:")
                        .font(Theme.Typography.caption)
                        .foregroundStyle(.secondary)

                    ForEach(ProductDateBasis.allCases) { basis in
                        let on = dateBasis == basis
                        Button {
                            Haptics.tap()
                            if reduceMotion {
                                dateBasis = basis
                            } else {
                                withAnimation(.easeInOut(duration: 0.16)) { dateBasis = basis }
                            }
                        } label: {
                            Text(basis.rawValue)
                                .font(Theme.Typography.microBold)
                                .padding(.horizontal, 9)
                                .padding(.vertical, 4)
                                .background(on ? Theme.teal : Theme.subtleFill)
                                .foregroundColor(on ? .white : .secondary)
                                .clipShape(Capsule())
                        }
                        .buttonStyle(.plain)
                    }

                    Spacer(minLength: 0)
                }
            }

            // Hàng lọc theo sàn
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 6) {
                    ForEach(ProductPlatformFilter.allCases) { plat in
                        compactFilterChip(
                            title: plat.rawValue,
                            icon: nil,
                            selected: platformFilter == plat,
                            accent: Theme.info
                        ) {
                            platformFilter = plat
                        }
                    }
                }
            }
        }
    }

    private func compactFilterChip(
        title: String,
        icon: String?,
        selected: Bool,
        accent: Color,
        action: @escaping () -> Void
    ) -> some View {
        Button {
            Haptics.tap()
            if reduceMotion {
                action()
            } else {
                withAnimation(.easeInOut(duration: 0.16)) { action() }
            }
        } label: {
            HStack(spacing: 4) {
                if let icon = icon {
                    Image(systemName: icon)
                        .font(.system(size: 11, weight: .semibold))
                }
                Text(title)
                    .font(selected ? Theme.Typography.captionBold : Theme.Typography.caption.weight(.semibold))
                    .lineLimit(1)
            }
            .padding(.horizontal, 11)
            .padding(.vertical, 7)
            .background(selected ? accent : Theme.cardBackground)
            .foregroundColor(selected ? .white : .primary)
            .clipShape(Capsule())
            .overlay(
                Capsule()
                    .stroke(selected ? Color.clear : Color.primary.opacity(0.08), lineWidth: 0.7)
            )
            .frame(minHeight: Theme.minTapTarget)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    private var customDatePickerCard: some View {
        HStack(spacing: 10) {
            VStack(alignment: .leading, spacing: 2) {
                Text("Từ ngày")
                    .font(Theme.Typography.microBold)
                    .foregroundStyle(.secondary)
                DatePicker("Từ ngày", selection: $customFrom, displayedComponents: .date)
                    .labelsHidden()
                    .datePickerStyle(.compact)
                    .environment(\.locale, Locale(identifier: "vi_VN"))
            }
            .frame(maxWidth: .infinity, alignment: .leading)

            Image(systemName: "arrow.right")
                .font(.caption.bold())
                .foregroundStyle(.tertiary)

            VStack(alignment: .leading, spacing: 2) {
                Text("Đến ngày")
                    .font(Theme.Typography.microBold)
                    .foregroundStyle(.secondary)
                DatePicker("Đến ngày", selection: $customTo, displayedComponents: .date)
                    .labelsHidden()
                    .datePickerStyle(.compact)
                    .environment(\.locale, Locale(identifier: "vi_VN"))
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .card(padding: 10, radius: Theme.cardRadius)
    }

    private func selectedDayBanner(_ day: Date) -> some View {
        HStack(spacing: 8) {
            Image(systemName: "calendar")
                .font(.caption.bold())
                .foregroundColor(Theme.accent)
            Text("Đang lọc: \(formatDayTitle(day))")
                .font(Theme.Typography.captionBold)
                .foregroundColor(.primary)
                .lineLimit(1)
            Spacer(minLength: 4)
            Button {
                Haptics.tap()
                selectedDay = nil
                viewMode = .byDay
            } label: {
                Text("Theo ngày")
                    .font(Theme.Typography.microBold)
                    .padding(.horizontal, 8)
                    .padding(.vertical, 4)
                    .background(Theme.subtleFill)
                    .foregroundColor(.primary)
                    .clipShape(Capsule())
            }
            .buttonStyle(.plain)

            Button {
                Haptics.tap()
                selectedDay = nil
            } label: {
                Image(systemName: "xmark.circle.fill")
                    .font(.subheadline)
                    .foregroundColor(Theme.accent)
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Bỏ lọc ngày")
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(Theme.accent.opacity(0.12))
        .clipShape(RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous)
                .stroke(Theme.accent.opacity(0.28), lineWidth: 0.7)
        )
    }

    // MARK: - Thẻ tóm tắt tổng quan
    private var overviewCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            VStack(alignment: .leading, spacing: 3) {
                Text(activeSummaryLine)
                    .font(Theme.Typography.primaryBold)
                    .foregroundColor(.primary)
                    .fixedSize(horizontal: false, vertical: true)

                if hasDates {
                    Text("Theo \(dateBasis.rawValue.lowercased()) · \(activeRangeDescription)")
                        .font(Theme.Typography.micro)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }

            HStack(spacing: 8) {
                summaryMetric(title: "Tổng cái hoàn", value: summary.totalUnits, color: Theme.accent)
                summaryMetric(title: "Số đơn", value: summary.ordersWithItems, color: Theme.info)
                summaryMetric(title: "Mặt hàng", value: summary.products.count, color: Theme.teal)
            }
        }
        .card(padding: Theme.cardPadding, radius: Theme.cardRadius)
    }

    private func summaryMetric(title: String, value: Int, color: Color) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            AnimatedCounter(
                value: value,
                font: .system(size: 20, weight: .bold, design: .rounded),
                color: value > 0 ? color : .secondary
            )
            Text(title)
                .font(Theme.Typography.micro)
                .foregroundStyle(.secondary)
                .lineLimit(1)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(8)
        .background(Theme.subtleFill)
        .clipShape(RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous))
    }

    // MARK: - Chế độ xem "Theo ngày" (mới nhất trước, mỗi ngày 1 dòng gọn)
    private var dailyList: some View {
        LazyVStack(spacing: Theme.cardSpacing) {
            ForEach(dailyBuckets) { bucket in
                Button {
                    Haptics.tap()
                    selectedDay = bucket.day
                    viewMode = .ranking
                } label: {
                    dailyRowCard(bucket)
                }
                .buttonStyle(.plain)
            }
        }
    }

    private func dailyRowCard(_ bucket: DailyProductBucket) -> some View {
        HStack(spacing: 10) {
            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 6) {
                    Text(formatDayTitle(bucket.day))
                        .font(Theme.Typography.primaryBold)
                        .foregroundColor(.primary)
                        .lineLimit(1)
                    Text("· \(bucket.productCount) SP")
                        .font(Theme.Typography.micro)
                        .foregroundStyle(.secondary)
                        .monospacedDigit()
                }

                if !bucket.topProductName.isEmpty {
                    HStack(spacing: 4) {
                        Image(systemName: "crown.fill")
                            .font(.system(size: 9))
                            .foregroundColor(Theme.warning)
                        Text("\(bucket.topProductName) (\(bucket.topProductUnits))")
                            .font(Theme.Typography.caption)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                            .truncationMode(.tail)
                    }
                }
            }

            Spacer(minLength: 6)

            HStack(spacing: 3) {
                Text("\(bucket.totalUnits)")
                    .font(.system(size: 18, weight: .bold, design: .rounded))
                    .foregroundColor(Theme.accent)
                    .monospacedDigit()
                Text("cái")
                    .font(Theme.Typography.micro)
                    .foregroundStyle(.secondary)
            }

            Image(systemName: "chevron.right")
                .font(.caption2.bold())
                .foregroundStyle(.tertiary)
        }
        .frame(maxWidth: .infinity, minHeight: Theme.minTapTarget, alignment: .leading)
        .card(padding: Theme.cardPadding, radius: Theme.cardRadius)
    }

    private func formatDayTitle(_ day: Date) -> String {
        let cal = Calendar.current
        let base = DashboardMetrics.daySectionTitle(for: day, now: Date(), calendar: cal)
        let shortDate = OrderRow.formatDisplayDate(day, includeTime: false, calendar: cal)
        if base == "Hôm nay" || base == "Hôm qua" {
            let comps = cal.dateComponents([.day, .month], from: day)
            return "\(base) · \(String(format: "%02d/%02d", comps.day ?? 1, comps.month ?? 1))"
        }
        let wd = DashboardMetrics.shortWeekdayLabel(for: day, calendar: cal)
        return wd.isEmpty ? shortDate : "\(wd), \(shortDate)"
    }

    // MARK: - Danh sách xếp hạng sản phẩm
    private var rankingList: some View {
        LazyVStack(spacing: Theme.cardSpacing) {
            ForEach(Array(summary.products.enumerated()), id: \.element.id) { idx, item in
                NavigationLink {
                    ProductStatDetailView(stat: item, sourceRows: filteredRows)
                } label: {
                    productRankCard(rank: idx + 1, item: item)
                }
                .buttonStyle(.plain)
            }
        }
    }

    private func productRankCard(rank: Int, item: ProductStatItem) -> some View {
        let rankColor: Color = rank == 1 ? Theme.accent : (rank == 2 ? Theme.warning : (rank == 3 ? Theme.teal : .secondary))
        let pct = Int((item.share * 100).rounded())

        return VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .top, spacing: 10) {
                Text("\(rank)")
                    .font(.system(.caption, design: .rounded).bold())
                    .foregroundColor(rank <= 3 ? .white : .secondary)
                    .frame(width: 24, height: 24)
                    .background(rank <= 3 ? rankColor : Theme.subtleFill)
                    .clipShape(RoundedRectangle(cornerRadius: 7, style: .continuous))

                VStack(alignment: .leading, spacing: 2) {
                    Text(item.name)
                        .font(Theme.Typography.primaryBold)
                        .foregroundColor(.primary)
                        .lineLimit(2)
                        .multilineTextAlignment(.leading)

                    HStack(spacing: 6) {
                        Text("\(item.orders) đơn")
                            .font(Theme.Typography.caption)
                            .foregroundStyle(.secondary)
                        Text("·")
                            .foregroundStyle(.tertiary)
                        Text("\(pct)% tỷ trọng")
                            .font(Theme.Typography.captionBold)
                            .foregroundColor(Theme.accent)
                    }

                    if item.premiumUnits > 0 {
                        HStack(spacing: 4) {
                            Image(systemName: "sparkles")
                                .font(.caption2.bold())
                                .foregroundColor(Theme.warning)
                            Text("Premium \(item.premiumUnits) • Thường \(item.standardUnits)")
                                .font(Theme.Typography.microBold)
                                .foregroundColor(Theme.warning)
                                .monospacedDigit()
                        }
                    }
                }

                Spacer(minLength: 4)

                VStack(alignment: .trailing, spacing: 1) {
                    Text("\(item.units)")
                        .font(.system(size: 18, weight: .bold, design: .rounded))
                        .foregroundColor(.primary)
                        .monospacedDigit()
                    Text("cái")
                        .font(Theme.Typography.micro)
                        .foregroundStyle(.secondary)
                }

                Image(systemName: "chevron.right")
                    .font(.caption.bold())
                    .foregroundStyle(.tertiary)
                    .padding(.top, 5)
            }

            // Thanh tỷ trọng ngang
            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    Capsule()
                        .fill(Theme.subtleFill)
                    Capsule()
                        .fill(
                            LinearGradient(
                                colors: [rankColor, rankColor.opacity(0.70)],
                                startPoint: .leading,
                                endPoint: .trailing
                            )
                        )
                        .frame(width: max(8, geo.size.width * CGFloat(min(1.0, item.share))))
                }
            }
            .frame(height: 6)

            if !item.topReason.isEmpty {
                HStack(spacing: 5) {
                    Image(systemName: "text.bubble.fill")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                    Text(item.topReason)
                        .font(Theme.Typography.micro)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(padding: Theme.cardPadding, radius: Theme.cardRadius)
    }

    // MARK: - Trạng thái trống
    private var emptyNoDataCard: some View {
        VStack(spacing: 12) {
            IconTile(systemName: "chart.bar.doc.horizontal.fill", color: Theme.accent, size: 46, cornerRadius: 13)
            Text("Chưa có dữ liệu sản phẩm")
                .font(Theme.Typography.navTitle)
            Text("Kết nối máy tính hoặc bật dữ liệu mẫu để xem bảng xếp hạng sản phẩm bị hoàn.")
                .font(Theme.Typography.secondary)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
            Button {
                Haptics.medium()
                engine.setDemoMode(true)
            } label: {
                Label("Xem thử với dữ liệu mẫu", systemImage: "sparkles")
                    .font(Theme.Typography.primaryBold)
                    .padding(.horizontal, 16)
                    .frame(minHeight: Theme.minTapTarget)
            }
            .buttonStyle(.borderedProminent)
            .tint(Theme.accent)
        }
        .frame(maxWidth: .infinity)
        .card(padding: 20, radius: Theme.cardRadius)
    }

    private var emptyFilterCard: some View {
        VStack(spacing: 8) {
            Image(systemName: "tray")
                .font(.title2)
                .foregroundStyle(.secondary)
            Text("Không có sản phẩm phù hợp bộ lọc")
                .font(Theme.Typography.secondaryBold)
                .foregroundStyle(.secondary)
            if selectedDay != nil || dateRange != .all {
                Button("Xem tất cả thời gian") {
                    Haptics.tap()
                    selectedDay = nil
                    dateRange = .all
                }
                .font(Theme.Typography.captionBold)
                .foregroundColor(Theme.accent)
                .padding(.top, 2)
            }
        }
        .frame(maxWidth: .infinity)
        .card(padding: 20, radius: Theme.cardRadius)
    }

    private var emptyDailyCard: some View {
        VStack(spacing: 8) {
            Image(systemName: "calendar.badge.exclamationmark")
                .font(.title2)
                .foregroundStyle(.secondary)
            Text("Chưa có đơn có \(dateBasis.rawValue.lowercased()) trong khoảng này")
                .font(Theme.Typography.secondaryBold)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
            Text("Đơn không có ngày chỉ được tính khi xem ở mục \"Tất cả\".")
                .font(Theme.Typography.caption)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
        }
        .frame(maxWidth: .infinity)
        .card(padding: 20, radius: Theme.cardRadius)
    }
}

/// Màn chi tiết của một sản phẩm bị hoàn:
/// Biến thể màu/size bị hoàn nhiều (thanh ngang), tách theo sàn, và danh sách đơn có sản phẩm đó (bấm mở Chi tiết đơn).
struct ProductStatDetailView: View {
    let stat: ProductStatItem
    let sourceRows: [OrderRow]

    @State private var selectedRow: OrderRow?

    private var matchingOrders: [OrderRow] {
        ProductMetrics.orders(forProductKey: stat.key, in: sourceRows)
    }

    var body: some View {
        ScrollView {
            VStack(spacing: 14) {
                headerCard
                if stat.premiumUnits > 0 {
                    premiumBreakdownCard
                }
                platformBreakdownCard
                if !stat.variants.isEmpty {
                    variantsCard
                }
                ordersSection
            }
            .padding(.horizontal, 16)
            .padding(.top, 10)
            .padding(.bottom, 28)
        }
        .background(Theme.pageBackground.ignoresSafeArea())
        .navigationTitle("Chi tiết sản phẩm")
        .navigationBarTitleDisplayMode(.inline)
        .sheet(item: $selectedRow) { DetailView(row: $0) }
    }

    private var headerCard: some View {
        let pct = Int((stat.share * 100).rounded())
        return VStack(alignment: .leading, spacing: 12) {
            Text(stat.name)
                .font(.title3.bold())
                .foregroundColor(.primary)
                .fixedSize(horizontal: false, vertical: true)

            HStack(spacing: 10) {
                metricBox(value: "\(stat.units)", label: "Số cái hoàn", color: Theme.accent)
                metricBox(value: "\(stat.orders)", label: "Số đơn", color: Theme.info)
                metricBox(value: "\(pct)%", label: "Tỷ trọng", color: Theme.teal)
            }

            if !stat.topReason.isEmpty {
                HStack(spacing: 8) {
                    Image(systemName: "text.bubble.fill")
                        .font(.caption.bold())
                        .foregroundColor(Theme.warning)
                    Text("Hay gặp: \(stat.topReason)")
                        .font(.caption.weight(.medium))
                        .foregroundColor(.primary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .padding(.horizontal, 10)
                .padding(.vertical, 8)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Theme.warning.opacity(0.12))
                .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(padding: 16, radius: 18)
    }

    private func metricBox(value: String, label: String, color: Color) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(value)
                .font(.system(size: 22, weight: .bold, design: .rounded))
                .foregroundColor(color)
                .monospacedDigit()
            Text(label)
                .font(.caption2.weight(.medium))
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(10)
        .background(Theme.subtleFill)
        .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
    }

    // MARK: - Tách Premium / Thường
    private var premiumBreakdownCard: some View {
        let total = max(1, stat.premiumUnits + stat.standardUnits)
        let premRatio = Double(stat.premiumUnits) / Double(total)
        let premPct = Int((premRatio * 100.0).rounded())
        let stdPct = max(0, 100 - premPct)

        return VStack(alignment: .leading, spacing: 12) {
            SectionTitleRow(
                title: "Premium / Thường",
                icon: "sparkles",
                iconColor: Theme.warning
            )

            // Thanh ngang 2 phần (Premium vs Thường)
            GeometryReader { geo in
                let w = geo.size.width
                HStack(spacing: 2) {
                    if stat.standardUnits <= 0 {
                        Rectangle()
                            .fill(
                                LinearGradient(
                                    colors: [Theme.warning, Theme.accent],
                                    startPoint: .leading,
                                    endPoint: .trailing
                                )
                            )
                            .frame(maxWidth: .infinity)
                    } else {
                        let premWidth = max(8, min(w - 10, w * CGFloat(premRatio)))
                        Rectangle()
                            .fill(
                                LinearGradient(
                                    colors: [Theme.warning, Theme.accent],
                                    startPoint: .leading,
                                    endPoint: .trailing
                                )
                            )
                            .frame(width: premWidth)
                        Rectangle()
                            .fill(Theme.info.opacity(0.75))
                            .frame(maxWidth: .infinity)
                    }
                }
                .clipShape(Capsule())
            }
            .frame(height: 10)

            HStack(spacing: 12) {
                HStack(spacing: 6) {
                    Circle()
                        .fill(Theme.warning)
                        .frame(width: 8, height: 8)
                    Text("Premium \(stat.premiumUnits)")
                        .font(.caption.bold())
                        .foregroundColor(.primary)
                        .monospacedDigit()
                    Text("(\(premPct)%)")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                        .monospacedDigit()
                }

                Spacer(minLength: 8)

                HStack(spacing: 6) {
                    Circle()
                        .fill(Theme.info.opacity(0.85))
                        .frame(width: 8, height: 8)
                    Text("Thường \(stat.standardUnits)")
                        .font(.caption.bold())
                        .foregroundColor(.primary)
                        .monospacedDigit()
                    Text("(\(stdPct)%)")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                        .monospacedDigit()
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(padding: 16, radius: 18)
    }

    // MARK: - Tách theo sàn
    private struct PlatformEntry: Identifiable {
        let name: String
        let units: Int
        let color: Color
        var id: String { name }
    }

    private var platformBreakdownCard: some View {
        let entries: [PlatformEntry] = [
            PlatformEntry(name: "Shopee", units: stat.shopeeUnits, color: Theme.accent),
            PlatformEntry(name: "TikTok", units: stat.tiktokUnits, color: Color(.label)),
            PlatformEntry(name: "Lazada", units: stat.lazadaUnits, color: Theme.info),
            PlatformEntry(name: "Khác", units: stat.otherUnits, color: .secondary)
        ].filter { $0.units > 0 }

        let maxUnits = max(1, entries.map { $0.units }.max() ?? 1)

        return VStack(alignment: .leading, spacing: 12) {
            SectionTitleRow(
                title: "Theo sàn",
                icon: "storefront.fill",
                iconColor: Theme.accent
            )

            ForEach(entries) { entry in
                barRow(
                    label: entry.name,
                    units: entry.units,
                    ratio: Double(entry.units) / Double(maxUnits),
                    color: entry.color
                )
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(padding: 16, radius: 18)
    }

    // MARK: - Màu & Size bị hoàn nhiều
    private var variantsCard: some View {
        let maxUnits = max(1, stat.variants.map { $0.units }.max() ?? 1)

        return VStack(alignment: .leading, spacing: 12) {
            SectionTitleRow(
                title: "Màu & Size bị hoàn nhiều",
                icon: "paintpalette.fill",
                iconColor: Theme.purple
            )

            ForEach(stat.variants) { v in
                barRow(
                    label: v.label,
                    units: v.units,
                    ratio: Double(v.units) / Double(maxUnits),
                    color: Theme.purple
                )
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(padding: 16, radius: 18)
    }

    private func barRow(label: String, units: Int, ratio: Double, color: Color) -> some View {
        VStack(alignment: .leading, spacing: 5) {
            HStack {
                Text(label)
                    .font(.subheadline.weight(.medium))
                    .foregroundColor(.primary)
                Spacer()
                Text("\(units) cái")
                    .font(.system(.subheadline, design: .rounded).bold())
                    .foregroundColor(.primary)
                    .monospacedDigit()
            }
            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    Capsule().fill(Theme.subtleFill)
                    Capsule()
                        .fill(color.opacity(0.85))
                        .frame(width: max(8, geo.size.width * CGFloat(min(1.0, ratio))))
                }
            }
            .frame(height: 8)
        }
    }

    // MARK: - Danh sách đơn có sản phẩm này
    private var ordersSection: some View {
        let orders = matchingOrders
        return VStack(alignment: .leading, spacing: 10) {
            SectionTitleRow(
                title: "Đơn có sản phẩm này",
                icon: "shippingbox.fill",
                iconColor: Theme.info,
                subtitle: "\(orders.count) đơn"
            )

            if orders.isEmpty {
                Text("Không tìm thấy đơn tương ứng.")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .card(padding: 14, radius: 16)
            } else {
                LazyVStack(spacing: 8) {
                    ForEach(orders) { r in
                        Button {
                            Haptics.tap()
                            selectedRow = r
                        } label: {
                            orderRowCard(r)
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
        }
    }

    private func orderRowCard(_ r: OrderRow) -> some View {
        let now = Int(Date().timeIntervalSince1970 * 1000)
        let level = r.pd ? r.alertLevel(now: now) : .unknown
        let levelColor: Color = level == .overdue ? Theme.danger : (level == .soon ? Theme.warning : Theme.success)

        return HStack(spacing: 10) {
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 6) {
                    if !r.p.isEmpty { PlatformBadge(platform: r.p) }
                    Text(r.c.isEmpty ? r.o : r.c)
                        .font(.system(.subheadline, design: .monospaced).bold())
                        .foregroundColor(.primary)
                        .lineLimit(1)
                }

                HStack(spacing: 6) {
                    if !r.co.isEmpty || !r.sz.isEmpty {
                        Text([r.co, r.sz].filter { !$0.isEmpty }.joined(separator: " · "))
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                    if !r.rd.isEmpty {
                        Text(r.rd)
                            .font(.caption2)
                            .foregroundStyle(.tertiary)
                    }
                }
            }

            Spacer(minLength: 4)

            VStack(alignment: .trailing, spacing: 4) {
                if r.rv {
                    StatusPill(text: "Đã nhận", color: Theme.success)
                } else if r.pd && r.due > 0 {
                    StatusPill(text: Deadline.text(due: r.due, now: now), color: levelColor)
                } else if !r.st.isEmpty {
                    StatusPill(text: r.st, color: .secondary)
                }
            }

            Image(systemName: "chevron.right")
                .font(.caption2.bold())
                .foregroundStyle(.tertiary)
        }
        .frame(minHeight: 50)
        .card(padding: 12, radius: 14)
    }
}
