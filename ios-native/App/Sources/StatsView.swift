import SwiftUI
import ScannerCore

/// Thống kê đơn hoàn (bảng chính + kho lưu trữ) và đơn hủy (bảng đơn hủy) theo ngày / tuần / tháng, kèm số đơn sắp đến hạn và quá hạn.
struct StatsView: View {
    @EnvironmentObject var store: SnapshotStore
    @State private var period: StatsPeriod = .day
    @State private var returns = StatsResult()
    @State private var cancels = StatsResult()
    @State private var loading = true

    var body: some View {
        List {
            Section {
                Picker("Thống kê theo", selection: $period) {
                    ForEach(StatsPeriod.allCases) { Text($0.title).tag($0) }
                }
                .pickerStyle(.segmented)
                Text("Tính theo ngày hoàn/hủy do khách bấm. Đơn hoàn gồm bảng chính và kho lưu trữ; đơn hủy lấy từ bảng đơn hủy.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            if !store.hasData {
                Section { Text("Chưa nhận danh sách đơn từ máy tính.").foregroundStyle(.secondary) }
            } else if loading {
                Section { ProgressView("Đang tính…") }
            } else {
                Section("Hạn xử lý (đơn còn chờ nhận)") {
                    deadlineRow("Đơn hoàn", returns)
                    deadlineRow("Đơn hủy", cancels)
                }
                group("Đơn hoàn", returns)
                group("Đơn hủy", cancels)
            }
        }
        .navigationTitle("Thống kê đơn")
        .navigationBarTitleDisplayMode(.inline)
        .task(id: "\(period.rawValue)|\(store.updatedAt?.timeIntervalSince1970 ?? 0)|\(store.revision)|\(store.rows.count)") { await reload() }
    }

    private func reload() async {
        let rows = store.rows, p = period
        let now = Int(Date().timeIntervalSince1970 * 1000)
        let (r, c) = await Task.detached(priority: .userInitiated) { () -> (StatsResult, StatsResult) in
            let ret = OrderStats.compute(rows: rows.filter { OrderStats.isReturn($0) }, period: p, now: now)
            let can = OrderStats.compute(rows: rows.filter { OrderStats.isCancel($0) }, period: p, now: now)
            return (ret, can)
        }.value
        returns = r; cancels = c; loading = false
    }

    private func deadlineRow(_ title: String, _ s: StatsResult) -> some View {
        HStack {
            Text(title).bold()
            Spacer()
            Text("🚨 Quá hạn \(s.overdue)").foregroundStyle(s.overdue > 0 ? .red : .secondary)
            Text("⏰ <24h \(s.soon)").foregroundStyle(s.soon > 0 ? .orange : .secondary)
        }.font(.subheadline)
    }

    @ViewBuilder
    private func group(_ title: String, _ s: StatsResult) -> some View {
        Section("\(title) — tổng \(s.total) (đã nhận \(s.received), đang chờ \(s.pending))") {
            if s.buckets.isEmpty { Text("Không có dữ liệu").foregroundStyle(.secondary) }
            ForEach(s.buckets.prefix(period == .day ? 31 : 24)) { b in
                HStack {
                    Text(label(b.start)).font(.subheadline.bold())
                    Spacer()
                    VStack(alignment: .trailing, spacing: 2) {
                        Text("\(b.total) đơn").font(.subheadline)
                        Text(detail(b)).font(.caption).foregroundStyle(.secondary)
                    }
                }
            }
            if s.unknownDate > 0 {
                Text("\(s.unknownDate) đơn không có ngày hoàn/hủy nên không xếp vào ngày nào").font(.caption).foregroundStyle(.secondary)
            }
        }
    }

    private func detail(_ b: StatsBucket) -> String {
        var parts = ["nhận \(b.received)", "chờ \(b.pending)"]
        if b.overdue > 0 { parts.append("quá hạn \(b.overdue)") }
        if b.soon > 0 { parts.append("<24h \(b.soon)") }
        return parts.joined(separator: " · ")
    }

    private func label(_ d: Date) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "vi_VN")
        switch period {
        case .day: f.dateFormat = "EEE dd/MM/yyyy"; return f.string(from: d)
        case .month: f.dateFormat = "MM/yyyy"; return "Tháng " + f.string(from: d)
        case .week:
            f.dateFormat = "dd/MM"
            let end = Calendar.current.date(byAdding: .day, value: 6, to: d) ?? d
            return "Tuần \(f.string(from: d)) – \(f.string(from: end))"
        }
    }
}
