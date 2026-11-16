import SwiftUI
import UserNotifications
import ScannerCore

/// Bảng thông báo của riêng điện thoại (không đồng bộ với máy tính): đơn quá hạn, sắp đến hạn và lịch thông báo sắp tới.
struct NotificationsView: View {
    @EnvironmentObject var store: SnapshotStore
    @EnvironmentObject var router: NotificationRouter

    private var upcoming: [PlannedNotice] {
        Array(NotificationPlanner.plan(rows: store.rows.filter { $0.pd }, now: Date()).prefix(12))
    }

    var body: some View {
        let a = store.alerts()
        let now = Int(Date().timeIntervalSince1970 * 1000)
        return NavigationStack {
            List {
                if !store.notificationsAllowed {
                    Section {
                        Text("Thông báo đang tắt nên điện thoại sẽ không nhắc khi đơn sắp đến hạn.").font(.footnote)
                        Button("Bật thông báo đơn sắp đến hạn") { store.requestNotificationPermission() }
                    }
                }
                if !store.hasData {
                    Section { Text("Chưa nhận danh sách đơn từ máy tính nên chưa có thông báo.").foregroundStyle(.secondary) }
                }
                Section {
                    NavigationLink { StatsView() } label: { Label("Thống kê đơn hoàn / hủy theo ngày, tuần, tháng", systemImage: "chart.bar.xaxis") }
                }
                Section("🚨 Quá hạn (\(a.overdue.count))") {
                    if a.overdue.isEmpty { Text("Không có đơn quá hạn").foregroundStyle(.secondary) }
                    ForEach(a.overdue) { alertRow($0, now: now) }
                }
                Section("⏰ Sắp đến hạn trong 24 giờ (\(a.soon.count))") {
                    if a.soon.isEmpty { Text("Không có đơn sắp đến hạn").foregroundStyle(.secondary) }
                    ForEach(a.soon) { alertRow($0, now: now) }
                }
                if !upcoming.isEmpty {
                    Section("Thông báo sắp tới trên điện thoại") {
                        ForEach(upcoming, id: \.id) { n in
                            VStack(alignment: .leading, spacing: 2) {
                                Text(n.title).font(.subheadline.bold())
                                Text(n.body).font(.footnote)
                                Text(n.fire.formatted(date: .abbreviated, time: .shortened)).font(.caption2).foregroundStyle(.secondary)
                            }
                            .contentShape(Rectangle())
                            .onTapGesture { openPlanned(n) }
                        }
                    }
                }
            }
            .navigationTitle("Thông báo")
            .toolbar {
                ToolbarItem(placement: .navigationBarTrailing) {
                    Menu {
                        Button("Ẩn tất cả thông báo đang hiện") { store.dismissAlerts(a.overdue + a.soon) }
                        Button("Hiện lại các thông báo đã ẩn") { store.restoreDismissed() }
                    } label: { Image(systemName: "ellipsis.circle") }
                }
            }
            .sheet(item: $router.openRow) { DetailView(row: $0) }
        }
    }

    private func alertRow(_ r: OrderRow, now: Int) -> some View {
        let level = Deadline.level(due: r.due, now: now)
        let hasBoth = !r.o.isEmpty && r.o.uppercased() != r.c.uppercased()
        return VStack(alignment: .leading, spacing: 2) {
            HStack {
                Text(hasBoth ? "\(r.o) · \(r.c)" : (r.o.isEmpty ? r.c : r.o))
                    .font(.system(.subheadline, design: .monospaced).bold()).lineLimit(1)
                Spacer()
                if r.wl { Text("⭐") }
            }
            if !r.n.isEmpty { Text(r.n).font(.footnote).lineLimit(1) }
            Text(Deadline.text(due: r.due, now: now)).font(.caption.bold())
                .foregroundColor(level == .overdue ? .red : .orange)
        }
        .contentShape(Rectangle())
        .onTapGesture { router.openRow = r }
        .swipeActions {
            Button("Ẩn") { store.dismissAlerts([r]) }.tint(.gray)
        }
    }

    private func openPlanned(_ n: PlannedNotice) {
        let rid = SnapshotStore.rowId(fromNoticeId: n.id)
        if let rid = rid, let r = store.rows.first(where: { $0.id == rid }) { router.openRow = r }
    }
}
