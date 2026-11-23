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
        return List {
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
                    StatTile(title: "Báo Đỏ · quá hạn", value: a.overdue.count, icon: "exclamationmark.triangle.fill", color: Theme.danger)
                    StatTile(title: "Báo Vàng · sắp hạn", value: a.soon.count, icon: "clock.fill", color: Theme.warning)
                }
                .listRowInsets(EdgeInsets(top: 6, leading: 14, bottom: 6, trailing: 14))
                .listRowBackground(Color.clear)
                .listRowSeparator(.hidden)
            }

            Section {
                if a.overdue.isEmpty {
                    Text("Không có đơn quá hạn")
                        .font(.footnote).foregroundStyle(.secondary)
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)
                }
                ForEach(a.overdue) { alertRow($0, now: now) }
            } header: {
                Text("Quá hạn (\(a.overdue.count))")
                    .font(.subheadline.bold())
                    .foregroundColor(Theme.danger)
            }

            Section {
                if a.soon.isEmpty {
                    Text("Không có đơn sắp đến hạn")
                        .font(.footnote).foregroundStyle(.secondary)
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)
                }
                ForEach(a.soon) { alertRow($0, now: now) }
            } header: {
                Text("Sắp đến hạn trong 24 giờ (\(a.soon.count))")
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
                    Button("Ẩn tất cả thông báo đang hiện") { store.dismissAlerts(a.overdue + a.soon) }
                    Button("Hiện lại các thông báo đã ẩn") { store.restoreDismissed() }
                } label: { Image(systemName: "ellipsis.circle") }
            }
        }
        .sheet(item: $router.openRow) { DetailView(row: $0) }
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
        if let rid = rid, let r = store.rows.first(where: { $0.id == rid }) { router.openRow = r }
    }
}
