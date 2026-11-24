import SwiftUI
import ScannerCore

/// Thống kê giống bảng thống kê trên máy tính (bản 1.9.21):
/// - Bên "Về kho": đơn về kho trong ngày, theo ngày nhận.
/// - Bên "Phát sinh": đơn khách hoàn/hủy trong ngày, theo ngày hoàn/hủy; Số đơn = Đã về + Còn chờ + Khác.
/// Phạm vi "Bảng chính" khớp số trên các tab của bảng chính (khi máy tính không bật bộ lọc); "Toàn bộ" tính cả kho lưu trữ.
struct StatsView: View {
    @EnvironmentObject var store: SnapshotStore
    @State private var period: StatsPeriod = .day
    @State private var scope = "main"
    @State private var platform = "all"
    @State private var data: StatsData?

    struct Section2 {
        var flow = StatsResult()
        var recv = StatsResult()
        var flowRows: [OrderRow] = []
        var recvRows: [OrderRow] = []
    }
    struct PlatRow: Identifiable {
        let name: String
        let ret: StatsResult
        let can: StatsResult
        var id: String { name }
    }
    struct StatsData {
        var ret = Section2()
        var can = Section2()
        var plats: [PlatRow] = []
    }

    var body: some View {
        List {
            Section {
                Picker("Phạm vi", selection: $scope) {
                    Text("Bảng chính").tag("main")
                    Text("Toàn bộ (+ lưu trữ)").tag("all")
                }.pickerStyle(.segmented)
                Picker("Thống kê theo", selection: $period) {
                    ForEach(StatsPeriod.allCases) { Text($0.title).tag($0) }
                }.pickerStyle(.segmented)
                Picker("Sàn", selection: $platform) {
                    Text("Tất cả sàn").tag("all")
                    Text("Shopee").tag("Shopee")
                    Text("TikTok").tag("TikTok Shop")
                    Text("Lazada").tag("Lazada")
                }.pickerStyle(.segmented)
                Text(scope == "main"
                     ? "Bảng chính: khớp số trên các tab của máy tính (Tất cả, Đã nhận, Chưa nhận…) khi máy tính không bật bộ lọc. Đơn đã nhận quá 24 giờ đã sang kho lưu trữ nên không còn ở đây."
                     : "Toàn bộ: bảng chính + kho lưu trữ. Đơn hủy luôn lấy từ bảng đơn hủy.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            if !store.hasData {
                Section { Text("Chưa nhận danh sách đơn từ máy tính.").foregroundStyle(.secondary) }
            } else if let d = data {
                platSection(d.plats)
                totals("↩️ Đơn hoàn", d.ret)
                buckets("Đơn hoàn theo \(period.title.lowercased())", d.ret, cancel: false)
                totals("🚫 Đơn hủy", d.can)
                buckets("Đơn hủy theo \(period.title.lowercased())", d.can, cancel: true)
            } else {
                Section { ProgressView("Đang tính…") }
            }
        }
        .navigationTitle("Thống kê đơn")
        .navigationBarTitleDisplayMode(.inline)
        .task(id: "\(period.rawValue)|\(scope)|\(platform)|\(store.updatedAt?.timeIntervalSince1970 ?? 0)|\(store.revision)|\(store.rows.count)") { await reload() }
    }

    private func reload() async {
        let p = period, pf = platform, sc = scope
        let all = store.rows
        let now = Int(Date().timeIntervalSince1970 * 1000)
        let d = await Task.detached(priority: .userInitiated) { () -> StatsData in
            let byPlat = pf == "all" ? all : all.filter { $0.p == pf || (pf == "TikTok Shop" && $0.p == "TikTok") }
            let retRows = byPlat.filter { sc == "main" ? $0.k == "m" : ($0.k == "m" || $0.k == "a") }
            let canRows = byPlat.filter { $0.k == "c" }
            let asRecv: (OrderRow) -> OrderRow = { var x = $0; x.rd = x.rcd; return x }
            func make(_ rows: [OrderRow]) -> Section2 {
                let recvRows = rows.filter { $0.rv }.map(asRecv)
                return Section2(flow: OrderStats.compute(rows: rows, period: p, now: now),
                                recv: OrderStats.compute(rows: recvRows, period: p, now: now),
                                flowRows: rows, recvRows: recvRows)
            }
            // Theo sàn: từng sàn riêng (không theo ô chọn sàn), cùng phạm vi Bảng chính / Toàn bộ
            let scoped = all.filter { $0.k == "c" || (sc == "main" ? $0.k == "m" : ($0.k == "m" || $0.k == "a")) }
            func norm(_ p: String) -> String { p == "TikTok" ? "TikTok Shop" : (p.isEmpty ? "Không rõ sàn" : p) }
            var names = ["Shopee", "TikTok Shop"]
            for n in Set(scoped.map { norm($0.p) }) where !names.contains(n) { names.append(n) }
            var plats: [PlatRow] = names.map { n in
                let rs = scoped.filter { norm($0.p) == n }
                return PlatRow(name: n, ret: OrderStats.compute(rows: rs.filter { $0.k != "c" }, period: p, now: now),
                               can: OrderStats.compute(rows: rs.filter { $0.k == "c" }, period: p, now: now))
            }
            plats.append(PlatRow(name: "Tổng", ret: OrderStats.compute(rows: scoped.filter { $0.k != "c" }, period: p, now: now),
                                 can: OrderStats.compute(rows: scoped.filter { $0.k == "c" }, period: p, now: now)))
            return StatsData(ret: make(retRows), can: make(canRows), plats: plats)
        }.value
        data = d
    }

    @ViewBuilder
    private func platSection(_ rows: [PlatRow]) -> some View {
        Section("🏬 Theo sàn (\(scope == "main" ? "bảng chính" : "bảng chính + lưu trữ"))") {
            ForEach(rows) { r in
                VStack(alignment: .leading, spacing: 4) {
                    Text(r.name == "Shopee" ? "🟠 Shopee" : (r.name == "TikTok Shop" ? "⚫ TikTok Shop" : r.name))
                        .font(.subheadline.bold())
                    Text("Hoàn \(r.ret.total) = về \(r.ret.received) + chờ \(r.ret.pending) + khác \(r.ret.other) · quá hạn \(r.ret.overdue) · sắp hạn \(r.ret.soon)")
                        .font(.caption)
                    Text("Hủy \(r.can.total) = về \(r.can.received) + chờ \(r.can.pending) · quá hạn \(r.can.overdue)")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
        }
    }

    @ViewBuilder
    private func totals(_ title: String, _ s: Section2) -> some View {
        Section(title) {
            VStack(spacing: 10) {
                HStack(spacing: 10) {
                    StatTile(title: "Số đơn", value: s.flow.total, icon: "shippingbox.fill", color: Theme.accent)
                    StatTile(title: "Đã về", value: s.flow.received, icon: "checkmark.seal.fill", color: .green)
                }
                HStack(spacing: 10) {
                    StatTile(title: "Còn chờ", value: s.flow.pending, icon: "hourglass", color: .orange)
                    StatTile(title: "Khác", value: s.flow.other, icon: "minus.circle.fill", color: .gray)
                }
                HStack(spacing: 10) {
                    StatTile(title: "Báo Đỏ · quá hạn", value: s.flow.overdue, icon: "exclamationmark.triangle.fill", color: .red)
                    StatTile(title: "Báo Vàng · sắp hạn", value: s.flow.soon, icon: "clock.fill", color: .orange)
                }
                Text("Số đơn = Đã về + Còn chờ + Khác (không cần nhận / bị mất / hư hỏng)")
                    .font(.caption2).foregroundStyle(.secondary).frame(maxWidth: .infinity, alignment: .leading)
            }
            .listRowInsets(EdgeInsets()).listRowBackground(Color.clear)
        }
    }

    @ViewBuilder
    private func buckets(_ title: String, _ s: Section2, cancel: Bool) -> some View {
        let keys = Array(Set(s.flow.buckets.map { $0.start } + s.recv.buckets.map { $0.start })).sorted(by: >).prefix(period == .day ? 31 : 24)
        let fMap = Dictionary(uniqueKeysWithValues: s.flow.buckets.map { ($0.start, $0) })
        let rMap = Dictionary(uniqueKeysWithValues: s.recv.buckets.map { ($0.start, $0) })
        Section(title) {
            if keys.isEmpty { Text("Không có dữ liệu").foregroundStyle(.secondary) }
            ForEach(Array(keys), id: \.self) { k in
                let f = fMap[k], r = rMap[k]
                NavigationLink {
                    StatsOrderList(title: label(k), flow: rowsIn(s.flowRows, k), recv: rowsIn(s.recvRows, k), cancel: cancel)
                } label: {
                    VStack(alignment: .leading, spacing: 4) {
                        HStack {
                            Text(label(k)).font(.subheadline.bold())
                            Spacer()
                            Text("Về kho \(r?.total ?? 0)").font(.subheadline.bold()).foregroundColor(.green)
                        }
                        if !cancel && scope == "all", let r = r, r.total > 0 {
                            Text("bảng chính \(r.main) · lưu trữ \(r.arch)").font(.caption2).foregroundStyle(.secondary)
                        }
                        Text(flowText(f, cancel: cancel)).font(.caption).foregroundStyle(.secondary)
                    }
                }
            }
            let nf = s.flow.unknownDate, nr = s.recv.unknownDate
            if nf > 0 || nr > 0 {
                NavigationLink {
                    StatsOrderList(title: "Chưa có ngày", flow: s.flowRows.filter { OrderStats.parseDate($0.rd) == nil },
                                   recv: s.recvRows.filter { OrderStats.parseDate($0.rd) == nil }, cancel: cancel)
                } label: {
                    Text("Chưa có ngày: \(nf) đơn chưa có ngày \(cancel ? "hủy" : "hoàn") · \(nr) đơn đã nhận chưa có ngày nhận")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
        }
    }

    private func flowText(_ b: StatsBucket?, cancel: Bool) -> String {
        guard let b = b else { return "\(cancel ? "Hủy" : "Hoàn") trong \(period.title.lowercased()): 0" }
        var t = "\(cancel ? "Hủy" : "Hoàn") \(b.total) = về \(b.received) + chờ \(b.pending) + khác \(b.other)"
        if b.overdue > 0 { t += " · quá hạn \(b.overdue)" }
        if b.soon > 0 { t += " · sắp hạn \(b.soon)" }
        return t
    }

    private func rowsIn(_ rows: [OrderRow], _ key: Date) -> [OrderRow] {
        rows.filter { r in
            guard let d = OrderStats.parseDate(r.rd) else { return false }
            return OrderStats.bucketStart(d, period: period) == key
        }
    }

    private static let dayFmt: DateFormatter = {
        let f = DateFormatter(); f.locale = Locale(identifier: "vi_VN"); f.dateFormat = "EEE dd/MM/yyyy"; return f
    }()
    private static let monthFmt: DateFormatter = {
        let f = DateFormatter(); f.locale = Locale(identifier: "vi_VN"); f.dateFormat = "MM/yyyy"; return f
    }()
    private static let weekFmt: DateFormatter = {
        let f = DateFormatter(); f.locale = Locale(identifier: "vi_VN"); f.dateFormat = "dd/MM"; return f
    }()

    private func label(_ d: Date) -> String {
        switch period {
        case .day: return Self.dayFmt.string(from: d)
        case .month: return "Tháng " + Self.monthFmt.string(from: d)
        case .week:
            let end = Calendar.current.date(byAdding: .day, value: 6, to: d) ?? d
            return "Tuần \(Self.weekFmt.string(from: d)) – \(Self.weekFmt.string(from: end))"
        }
    }
}

/// Danh sách đúng các đơn tạo nên con số của một ngày/tuần/tháng.
struct StatsOrderList: View {
    let title: String
    let flow: [OrderRow]
    let recv: [OrderRow]
    let cancel: Bool
    @State private var tab = 0
    @State private var selected: OrderRow?

    var body: some View {
        List {
            Picker("", selection: $tab) {
                Text("Về kho (\(recv.count))").tag(0)
                Text("\(cancel ? "Hủy" : "Hoàn") (\(flow.count))").tag(1)
            }.pickerStyle(.segmented)
            ForEach(tab == 0 ? recv : flow) { r in
                Button { selected = r } label: {
                    HStack {
                        VStack(alignment: .leading, spacing: 2) {
                            Text(r.c.isEmpty ? r.o : r.c).font(.system(.footnote, design: .monospaced).bold())
                            Text(r.n).font(.caption).foregroundStyle(.secondary).lineLimit(1)
                        }
                        Spacer()
                        Text(r.rv ? "Đã về" : (r.pd ? "Chờ" : "Khác")).font(.caption.bold())
                            .foregroundColor(r.rv ? .green : (r.pd ? .orange : .gray))
                        Text(r.k == "a" ? "Lưu trữ" : (r.k == "c" ? "Đơn hủy" : "Bảng chính")).font(.caption2).foregroundStyle(.secondary)
                    }
                }.buttonStyle(.plain)
            }
        }
        .navigationTitle(title)
        .navigationBarTitleDisplayMode(.inline)
        .sheet(item: $selected) { DetailView(row: $0) }
    }
}
