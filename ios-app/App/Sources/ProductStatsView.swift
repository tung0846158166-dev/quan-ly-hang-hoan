import SwiftUI
import ScannerCore

/// Màn Thống kê sản phẩm đầy đủ: xếp hạng sản phẩm bị hoàn nhiều nhất,
/// lọc nhanh theo sàn (Tất cả / Shopee / TikTok / Lazada) và khoảng thời gian (7 ngày / 30 ngày / Tất cả).
struct ProductStatsView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore

    @State private var platformFilter: ProductPlatformFilter = .all
    @State private var dateRange: ProductDateRange = .all
    @State private var summary = ProductStatsSummary()
    @State private var filteredRows: [OrderRow] = []
    @State private var hasDates = false

    private func recompute() {
        let all = store.rows
        let datesAvailable = ProductMetrics.hasAnyDate(rows: all)
        hasDates = datesAvailable
        let activeRange: ProductDateRange = datesAvailable ? dateRange : .all
        let rows = ProductMetrics.filter(rows: all, platform: platformFilter, dateRange: activeRange, now: Date())
        filteredRows = rows
        summary = ProductMetrics.compute(rows: rows)
    }

    var body: some View {
        ScrollView {
            VStack(spacing: 14) {
                if !store.hasData {
                    emptyNoDataCard
                } else {
                    filterControls
                    overviewCard

                    if summary.products.isEmpty {
                        emptyFilterCard
                    } else {
                        rankingList
                    }
                }
            }
            .padding(.horizontal, 16)
            .padding(.top, 10)
            .padding(.bottom, 28)
        }
        .background(Theme.pageBackground.ignoresSafeArea())
        .navigationTitle("Thống kê sản phẩm")
        .navigationBarTitleDisplayMode(.large)
        .onAppear { recompute() }
        .onChange(of: platformFilter) { _ in recompute() }
        .onChange(of: dateRange) { _ in recompute() }
        .onChange(of: store.updatedAt) { _ in recompute() }
        .onChange(of: store.revision) { _ in recompute() }
        .onChange(of: store.rows.count) { _ in recompute() }
        .onChange(of: store.isDemoMode) { _ in recompute() }
    }

    // MARK: - Bộ lọc sàn & thời gian
    private var filterControls: some View {
        VStack(spacing: 10) {
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 8) {
                    ForEach(ProductPlatformFilter.allCases) { plat in
                        Chip(title: plat.rawValue, selected: platformFilter == plat) {
                            platformFilter = plat
                        }
                    }
                }
            }

            if hasDates {
                Picker("Khoảng thời gian", selection: $dateRange) {
                    ForEach(ProductDateRange.allCases) { r in
                        Text(r.rawValue).tag(r)
                    }
                }
                .pickerStyle(.segmented)
                .onChange(of: dateRange) { _ in Haptics.tap() }
            }
        }
    }

    // MARK: - Thẻ tóm tắt tổng quan
    private var overviewCard: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(summary.summaryLine)
                .font(.subheadline.weight(.semibold))
                .foregroundColor(.primary)
                .fixedSize(horizontal: false, vertical: true)

            HStack(spacing: 10) {
                summaryMetric(title: "Tổng cái hoàn", value: summary.totalUnits, color: Theme.accent)
                summaryMetric(title: "Số đơn", value: summary.ordersWithItems, color: Theme.info)
                summaryMetric(title: "Mặt hàng", value: summary.products.count, color: Theme.teal)
            }
        }
        .card(padding: 14, radius: 18)
    }

    private func summaryMetric(title: String, value: Int, color: Color) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            AnimatedCounter(
                value: value,
                font: .system(size: 24, weight: .bold, design: .rounded),
                color: value > 0 ? color : .secondary
            )
            Text(title)
                .font(.caption2.weight(.medium))
                .foregroundStyle(.secondary)
                .lineLimit(1)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(10)
        .background(Theme.subtleFill)
        .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
    }

    // MARK: - Danh sách xếp hạng sản phẩm
    private var rankingList: some View {
        LazyVStack(spacing: 10) {
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

        return VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .top, spacing: 10) {
                Text("\(rank)")
                    .font(.system(.caption, design: .rounded).bold())
                    .foregroundColor(rank <= 3 ? .white : .secondary)
                    .frame(width: 26, height: 26)
                    .background(rank <= 3 ? rankColor : Theme.subtleFill)
                    .clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))

                VStack(alignment: .leading, spacing: 3) {
                    Text(item.name)
                        .font(.subheadline.bold())
                        .foregroundColor(.primary)
                        .lineLimit(2)
                        .multilineTextAlignment(.leading)

                    HStack(spacing: 8) {
                        Text("\(item.orders) đơn")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                        Text("·")
                            .foregroundStyle(.tertiary)
                        Text("\(pct)% tỷ trọng")
                            .font(.caption.weight(.semibold))
                            .foregroundColor(Theme.accent)
                    }

                    if item.premiumUnits > 0 {
                        HStack(spacing: 4) {
                            Image(systemName: "sparkles")
                                .font(.caption2.bold())
                                .foregroundColor(Theme.warning)
                            Text("Premium \(item.premiumUnits) • Thường \(item.standardUnits)")
                                .font(.caption2.weight(.semibold))
                                .foregroundColor(Theme.warning)
                                .monospacedDigit()
                        }
                    }
                }

                Spacer(minLength: 4)

                VStack(alignment: .trailing, spacing: 2) {
                    Text("\(item.units)")
                        .font(.system(size: 20, weight: .bold, design: .rounded))
                        .foregroundColor(.primary)
                        .monospacedDigit()
                    Text("cái")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                }

                Image(systemName: "chevron.right")
                    .font(.caption.bold())
                    .foregroundStyle(.tertiary)
                    .padding(.top, 6)
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
            .frame(height: 7)

            if !item.topReason.isEmpty {
                HStack(spacing: 5) {
                    Image(systemName: "text.bubble.fill")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                    Text(item.topReason)
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(padding: 14, radius: 16)
    }

    // MARK: - Trạng thái trống
    private var emptyNoDataCard: some View {
        VStack(spacing: 14) {
            IconTile(systemName: "chart.bar.doc.horizontal.fill", color: Theme.accent, size: 50, cornerRadius: 14)
            Text("Chưa có dữ liệu sản phẩm")
                .font(.headline.bold())
            Text("Kết nối máy tính hoặc bật dữ liệu mẫu để xem bảng xếp hạng sản phẩm bị hoàn.")
                .font(.footnote)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
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
        .frame(maxWidth: .infinity)
        .card(padding: 22, radius: 20)
    }

    private var emptyFilterCard: some View {
        VStack(spacing: 8) {
            Image(systemName: "tray")
                .font(.title2)
                .foregroundStyle(.secondary)
            Text("Không có sản phẩm phù hợp bộ lọc")
                .font(.subheadline.weight(.medium))
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity)
        .card(padding: 24, radius: 18)
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
