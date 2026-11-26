import SwiftUI
import UserNotifications
import ScannerCore

/// Bảng thông báo của riêng điện thoại (không đồng bộ với máy tính): đơn quá hạn, sắp đến hạn và lịch thông báo sắp tới.
struct NotificationsView: View {
    @EnvironmentObject var store: SnapshotStore
    @EnvironmentObject var router: NotificationRouter
    @EnvironmentObject var engine: SyncEngine
    @State private var overdueRows: [OrderRow] = []
    @State private var soonRows: [OrderRow] = []
    @State private var upcoming: [PlannedNotice] = []
    @State private var listNowMs = Int(Date().timeIntervalSince1970 * 1000)

    private var refreshToken: String {
        "\(store.revision)|\(store.isDemoMode ? 1 : 0)|\(store.dismissed.count)|\(store.rows.count)"
    }

    var body: some View {
        let now = listNowMs
        return List {
            if let d = engine.aiDigest {
                Section {
                    VStack(alignment: .leading, spacing: 8) {
                        HStack(spacing: 6) {
                            Image(systemName: "sparkles").foregroundColor(Theme.accent)
                            Text("AI gợi ý lúc \(d.slot)").font(.subheadline.bold())
                            Spacer()
                            if !d.byAi { Text("tóm tắt tự động").font(.caption2).foregroundStyle(.secondary) }
                        }
                        Text(d.text).font(.footnote)
                    }
                    .padding(.vertical, 4)
                } footer: {
                    Text("Cloud viết gợi ý lúc 8:00, 10:30, 13:30, 16:30, 19:00 và 21:00 từ số liệu tổng hợp (không có thông tin khách).")
                }
            }
            if !store.notificationsAllowed {
                Section {
                    VStack(alignment: .leading, spacing: 10) {
                        HStack(spacing: 8) {
                            Image(systemName: "bell.slash.fill").foregroundColor(Theme.warning)
                            Text("Thông báo đang tắt").font(.subheadline.bold())
                        }
                        Text("Bật thông báo để điện thoại nhắc tự động khi đơn sắp đến hạn 12h/24h.")
                            .font(.footnote).foregroundStyle(.secondary)
                        Button {
                            Haptics.medium()
                            store.requestNotificationPermission()
                        } label: {
                            Text("Bật thông báo đơn sắp đến hạn")
                                .font(.subheadline.bold())
                                .frame(maxWidth: .infinity, minHeight: 44)
                        }
                        .buttonStyle(.borderedProminent)
                        .tint(Theme.accent)
                    }
                    .card(padding: 14)
                    .listRowInsets(EdgeInsets(top: 6, leading: 14, bottom: 6, trailing: 14))
                    .listRowBackground(Color.clear)
                    .listRowSeparator(.hidden)
                }
            }

            if !store.hasData {
                Section {
                    Text("Chưa nhận danh sách đơn từ máy tính nên chưa có thông báo.")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .card(padding: 14)
                        .listRowInsets(EdgeInsets(top: 6, leading: 14, bottom: 6, trailing: 14))
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)
                }
            }

            Section {
                NavigationLink { StatsView() } label: {
                    HStack(spacing: 10) {
                        Image(systemName: "chart.bar.xaxis")
                            .font(.subheadline.bold())
                            .foregroundColor(Theme.accent)
                            .frame(width: 32, height: 32)
                            .background(Theme.accentSoft)
                            .clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
                        Text("Thống kê đơn hoàn / hủy theo ngày, tuần, tháng")
                            .font(.subheadline.bold())
                            .foregroundColor(.primary)
                        Spacer()
                    }
                    .card(padding: 12)
                }
                .listRowInsets(EdgeInsets(top: 4, leading: 14, bottom: 4, trailing: 14))
                .listRowBackground(Color.clear)
                .listRowSeparator(.hidden)

                HStack(spacing: 12) {
                    StatTile(title: "Báo Đỏ · quá hạn", value: overdueRows.count, icon: "exclamationmark.triangle.fill", color: Theme.danger)
                    StatTile(title: "Báo Vàng · sắp hạn", value: soonRows.count, icon: "clock.fill", color: Theme.warning)
                }
                .listRowInsets(EdgeInsets(top: 6, leading: 14, bottom: 6, trailing: 14))
                .listRowBackground(Color.clear)
                .listRowSeparator(.hidden)
            }

            Section {
                if overdueRows.isEmpty {
                    Text("Không có đơn quá hạn")
                        .font(.footnote).foregroundStyle(.secondary)
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)
                }
                ForEach(overdueRows) { alertRow($0, now: now) }
            } header: {
                Text("Quá hạn (\(overdueRows.count))")
                    .font(.subheadline.bold())
                    .foregroundColor(Theme.danger)
            }

            Section {
                if soonRows.isEmpty {
                    Text("Không có đơn sắp đến hạn")
                        .font(.footnote).foregroundStyle(.secondary)
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)
                }
                ForEach(soonRows) { alertRow($0, now: now) }
            } header: {
                Text("Sắp đến hạn trong 24 giờ (\(soonRows.count))")
                    .font(.subheadline.bold())
                    .foregroundColor(Theme.warning)
            }

            if !upcoming.isEmpty {
                Section {
                    ForEach(upcoming, id: \.id) { n in
                        VStack(alignment: .leading, spacing: 4) {
                            Text(n.title).font(.subheadline.bold())
                            Text(n.body).font(.footnote).foregroundStyle(.secondary)
                            Text(n.fire.formatted(date: .abbreviated, time: .shortened))
                                .font(.caption2.bold())
                                .foregroundColor(Theme.accent)
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .card(padding: 12, radius: 14)
                        .contentShape(Rectangle())
                        .onTapGesture { openPlanned(n) }
                        .listRowInsets(EdgeInsets(top: 4, leading: 14, bottom: 4, trailing: 14))
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)
                    }
                } header: {
                    Text("Thông báo sắp tới trên điện thoại")
                        .font(.subheadline.bold())
                }
            }
        }
        .listStyle(.plain)
        .scrollContentBackground(.hidden)
        .background(Theme.pageBackground.ignoresSafeArea())
        .navigationTitle("Thông báo")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .navigationBarTrailing) {
                Menu {
                    Button("Ẩn tất cả thông báo đang hiện") { store.dismissAlerts(overdueRows + soonRows) }
                    Button("Hiện lại các thông báo đã ẩn") { store.restoreDismissed() }
                } label: { Image(systemName: "ellipsis.circle") }
            }
        }
        .sheet(item: $router.openRow) { DetailView(row: $0) }
        .task(id: refreshToken) {
            let rowsSnapshot = store.rows
            let dismissedSnapshot = store.dismissed
            let nowDate = Date()
            let nowMs = Int(nowDate.timeIntervalSince1970 * 1000)
            let result = await Task.detached(priority: .userInitiated) { () -> ([OrderRow], [OrderRow], [PlannedNotice]) in
                let a = SnapshotStore.computeAlerts(rows: rowsSnapshot, dismissed: dismissedSnapshot, now: nowMs)
                let pending = rowsSnapshot.filter { $0.pd }
                let planned = Array(NotificationPlanner.plan(rows: pending, now: nowDate).prefix(12))
                return (a.overdue, a.soon, planned)
            }.value
            guard !Task.isCancelled else { return }
            overdueRows = result.0
            soonRows = result.1
            upcoming = result.2
            listNowMs = nowMs
        }
    }

    private func alertRow(_ r: OrderRow, now: Int) -> some View {
        let level = r.alertLevel(now: now)
        let hasBoth = !r.o.isEmpty && r.o.uppercased() != r.c.uppercased()
        let tint: Color = level == .overdue ? Theme.danger : Theme.warning
        return VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 8) {
                Circle().fill(tint).frame(width: 9, height: 9)
                Text(hasBoth ? "\(r.o) · \(r.c)" : (r.o.isEmpty ? r.c : r.o))
                    .font(.system(.subheadline, design: .monospaced).bold())
                    .lineLimit(1)
                Spacer()
                if r.wl { Image(systemName: "star.fill").font(.caption).foregroundColor(Theme.warning) }
                if !r.p.isEmpty { PlatformBadge(platform: r.p) }
            }
            if !r.n.isEmpty { Text(r.n).font(.footnote).foregroundStyle(.secondary).lineLimit(1) }
            StatusPill(text: Deadline.text(due: r.due, now: now), color: tint)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(padding: 12, radius: 16)
        .contentShape(Rectangle())
        .onTapGesture {
            Haptics.tap()
            router.openRow = r
        }
        .swipeActions {
            Button("Ẩn") { store.dismissAlerts([r]) }.tint(.gray)
        }
        .listRowInsets(EdgeInsets(top: 4, leading: 14, bottom: 4, trailing: 14))
        .listRowBackground(Color.clear)
        .listRowSeparator(.hidden)
    }

    private func openPlanned(_ n: PlannedNotice) {
        let rid = SnapshotStore.rowId(fromNoticeId: n.id)
        if let rid = rid, let r = store.row(byId: rid) { router.openRow = r }
    }
}
