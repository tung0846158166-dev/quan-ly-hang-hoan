import SwiftUI
import ScannerCore

/// Bảng đơn hủy trên điện thoại = Bảng đơn hủy trên máy tính: tích chọn để đánh dấu đã nhận / chưa nhận.
struct CancelledTableView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    private enum Show: String, CaseIterable { case pending = "Chưa nhận", received = "Đã nhận", all = "Tất cả" }
    @State private var show: Show = .pending
    @State private var picked: Set<String> = []
    @State private var confirmAll = false

    private var all: [OrderRow] { store.rows.filter { $0.k == "c" } }
    private var list: [OrderRow] {
        switch show {
        case .pending: return all.filter { !$0.rv }
        case .received: return all.filter { $0.rv }
        case .all: return all
        }
    }
    /// Chỉ chọn được đơn có id từ máy tính (máy tính bản cũ chưa gửi id).
    private var selectable: [OrderRow] { list.filter { !$0.ci.isEmpty } }
    private var allPicked: Bool { !selectable.isEmpty && selectable.allSatisfy { picked.contains($0.ci) } }
    private var pendingIds: [String] { all.filter { !$0.rv && !$0.ci.isEmpty }.map { $0.ci } }

    var body: some View {
        VStack(spacing: 10) {
            Picker("", selection: $show) {
                ForEach(Show.allCases, id: \.self) { s in
                    Text("\(s.rawValue) (\(count(s)))").tag(s)
                }
            }
            .pickerStyle(.segmented)
            .padding(.horizontal, 14)
            .padding(.top, 6)

            if let b = engine.banner { BannerStrip(banner: b).padding(.horizontal, 14) }

            List {
                if list.isEmpty {
                    Text("Không có đơn hủy")
                        .foregroundStyle(.secondary)
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)
                }
                ForEach(list) { r in
                    Button {
                        Haptics.tap()
                        toggle(r.ci)
                    } label: {
                        line(r).card(padding: 12, radius: 16)
                    }
                    .buttonStyle(.plain)
                    .disabled(r.ci.isEmpty)
                    .listRowSeparator(.hidden)
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets(top: 4, leading: 14, bottom: 4, trailing: 14))
                }
            }
            .listStyle(.plain)
            .scrollContentBackground(.hidden)
        }
        .background(Theme.pageBackground.ignoresSafeArea())
        .navigationTitle("Đơn hủy (\(all.count))")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .navigationBarTrailing) {
                Button(allPicked ? "Bỏ chọn" : "Chọn hết") {
                    Haptics.tap()
                    if allPicked { picked.subtract(selectable.map { $0.ci }) } else { picked.formUnion(selectable.map { $0.ci }) }
                }
                .disabled(selectable.isEmpty)
            }
        }
        .safeAreaInset(edge: .bottom) {
            VStack(spacing: 8) {
                HStack(spacing: 10) {
                    Button { apply(true) } label: {
                        Label("Đã nhận (\(picked.count))", systemImage: "checkmark.circle.fill")
                            .font(.subheadline.bold())
                            .frame(maxWidth: .infinity, minHeight: 44)
                    }
                    .buttonStyle(.borderedProminent).tint(Theme.success)
                    .disabled(picked.isEmpty)

                    Button { apply(false) } label: {
                        Label("Chưa nhận (\(picked.count))", systemImage: "arrow.uturn.backward")
                            .font(.subheadline.bold())
                            .frame(maxWidth: .infinity, minHeight: 44)
                    }
                    .buttonStyle(.bordered)
                    .disabled(picked.isEmpty)
                }
                Button { confirmAll = true } label: {
                    Text("Nhận hết đơn chưa nhận (\(pendingIds.count))")
                        .font(.subheadline.bold())
                        .frame(maxWidth: .infinity, minHeight: 42)
                }
                .buttonStyle(.bordered)
                .tint(Theme.accent)
                .disabled(pendingIds.isEmpty)
            }
            .padding(.horizontal, 14).padding(.vertical, 10)
            .background(.ultraThinMaterial)
        }
        .confirmationDialog("Đánh dấu đã nhận toàn bộ \(pendingIds.count) đơn hủy chưa nhận?", isPresented: $confirmAll, titleVisibility: .visible) {
            Button("Đã nhận hết") {
                Haptics.medium()
                engine.setCancelledReceived(ids: pendingIds, received: true)
                picked = []
            }
        }
        .onChange(of: show) { _ in picked = [] }
        .refreshable { engine.requestSnapshot(); try? await Task.sleep(nanoseconds: 2_000_000_000) }
    }

    private func count(_ s: Show) -> Int {
        switch s {
        case .pending: return all.filter { !$0.rv }.count
        case .received: return all.filter { $0.rv }.count
        case .all: return all.count
        }
    }

    private func toggle(_ id: String) {
        guard !id.isEmpty else { return }
        if picked.contains(id) { picked.remove(id) } else { picked.insert(id) }
    }

    private func apply(_ received: Bool) {
        Haptics.medium()
        let ids = list.map { $0.ci }.filter { picked.contains($0) }
        engine.setCancelledReceived(ids: ids.isEmpty ? Array(picked) : ids, received: received)
        picked = []
    }

    private func line(_ r: OrderRow) -> some View {
        let on = picked.contains(r.ci)
        return HStack(spacing: 10) {
            Image(systemName: on ? "checkmark.circle.fill" : "circle")
                .font(.title2)
                .foregroundColor(r.ci.isEmpty ? Color.secondary.opacity(0.3) : (on ? Theme.accent : Color.secondary))
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 6) {
                    Text(r.c).font(.system(.subheadline, design: .monospaced).bold()).lineLimit(1)
                    if !r.p.isEmpty { PlatformBadge(platform: r.p) }
                }
                let sub = [r.rd, r.n].filter { !$0.isEmpty }.joined(separator: " · ")
                if !sub.isEmpty { Text(sub).font(.caption).foregroundStyle(.secondary).lineLimit(2) }
            }
            Spacer(minLength: 0)
            StatusPill(text: r.rv ? "Đã nhận" : "Chưa nhận", color: r.rv ? Theme.success : Theme.warning)
        }
        .contentShape(Rectangle())
    }
}

/// Bảng "Hủy sau chuẩn bị": đơn ở bảng chính có cột Trạng thái sàn ghi "Huỷ sau chuẩn bị".
struct CancelAfterPrepView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    private enum Show: String, CaseIterable { case pending = "Chưa nhận", received = "Đã nhận", all = "Tất cả" }
    @State private var show: Show = .pending
    @State private var picked: Set<String> = []
    @State private var detail: OrderRow?

    private var all: [OrderRow] { store.rows.filter { $0.k == "m" && $0.isCancelAfterPrep } }
    private var list: [OrderRow] {
        switch show {
        case .pending: return all.filter { !$0.rv }
        case .received: return all.filter { $0.rv }
        case .all: return all
        }
    }
    private var allPicked: Bool { !list.isEmpty && list.allSatisfy { picked.contains($0.id) } }

    var body: some View {
        VStack(spacing: 10) {
            Picker("", selection: $show) {
                ForEach(Show.allCases, id: \.self) { s in Text("\(s.rawValue) (\(count(s)))").tag(s) }
            }
            .pickerStyle(.segmented)
            .padding(.horizontal, 14)
            .padding(.top, 6)

            if let b = engine.banner { BannerStrip(banner: b).padding(.horizontal, 14) }

            List {
                if list.isEmpty {
                    Text(store.rows.contains { !$0.ps.isEmpty } ? "Không có đơn Hủy sau chuẩn bị" : "Máy tính chưa gửi cột Trạng thái sàn — cập nhật máy tính lên bản mới rồi kéo xuống để tải lại")
                        .foregroundStyle(.secondary)
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)
                }
                ForEach(list) { r in
                    Button {
                        Haptics.tap()
                        toggle(r.id)
                    } label: {
                        line(r).card(padding: 12, radius: 16)
                    }
                    .buttonStyle(.plain)
                    .contextMenu { Button("Xem chi tiết") { detail = r } }
                    .listRowSeparator(.hidden)
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets(top: 4, leading: 14, bottom: 4, trailing: 14))
                }
            }
            .listStyle(.plain)
            .scrollContentBackground(.hidden)
        }
        .background(Theme.pageBackground.ignoresSafeArea())
        .navigationTitle("Hủy sau chuẩn bị (\(all.count))")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .navigationBarTrailing) {
                Button(allPicked ? "Bỏ chọn" : "Chọn hết") {
                    Haptics.tap()
                    if allPicked { picked.subtract(list.map { $0.id }) } else { picked.formUnion(list.map { $0.id }) }
                }
                .disabled(list.isEmpty)
            }
        }
        .safeAreaInset(edge: .bottom) {
            HStack(spacing: 10) {
                Button { apply("Hàng đã trả") } label: {
                    Label("Đã nhận (\(picked.count))", systemImage: "checkmark.circle.fill")
                        .font(.subheadline.bold())
                        .frame(maxWidth: .infinity, minHeight: 44)
                }
                .buttonStyle(.borderedProminent).tint(Theme.success)
                .disabled(picked.isEmpty)

                Button { apply("Không cần nhận") } label: {
                    Text("Không cần nhận")
                        .font(.subheadline.bold())
                        .frame(maxWidth: .infinity, minHeight: 44)
                }
                .buttonStyle(.bordered)
                .disabled(picked.isEmpty)
            }
            .padding(.horizontal, 14).padding(.vertical, 10)
            .background(.ultraThinMaterial)
        }
        .sheet(item: $detail) { DetailView(row: $0) }
        .onChange(of: show) { _ in picked = [] }
        .refreshable { engine.requestSnapshot(); try? await Task.sleep(nanoseconds: 2_000_000_000) }
    }

    private func count(_ s: Show) -> Int {
        switch s {
        case .pending: return all.filter { !$0.rv }.count
        case .received: return all.filter { $0.rv }.count
        case .all: return all.count
        }
    }

    private func toggle(_ id: String) {
        if picked.contains(id) { picked.remove(id) } else { picked.insert(id) }
    }

    private func apply(_ status: String) {
        Haptics.medium()
        let rows = all.filter { picked.contains($0.id) }
        engine.sendOrderEdits(rows, fields: ["st": status])
        picked = []
    }

    private func line(_ r: OrderRow) -> some View {
        let on = picked.contains(r.id)
        let now = Int(Date().timeIntervalSince1970 * 1000)
        let level = r.pd ? r.alertLevel(now: now) : .unknown
        return HStack(spacing: 10) {
            Image(systemName: on ? "checkmark.circle.fill" : "circle")
                .font(.title2)
                .foregroundColor(on ? Theme.accent : Color.secondary)
            VStack(alignment: .leading, spacing: 3) {
                Text(r.o.isEmpty ? r.c : r.o).font(.system(.subheadline, design: .monospaced).bold())
                if !r.o.isEmpty && r.c.uppercased() != r.o.uppercased() {
                    Text("Vận đơn: \(r.c)").font(.caption.monospaced()).foregroundStyle(.secondary)
                }
                Text(r.ps).font(.caption.bold()).foregroundColor(.purple)
                let sub = [r.rd, r.n].filter { !$0.isEmpty }.joined(separator: " · ")
                if !sub.isEmpty { Text(sub).font(.caption).foregroundStyle(.secondary).lineLimit(2) }
            }
            Spacer(minLength: 0)
            VStack(alignment: .trailing, spacing: 4) {
                StatusPill(
                    text: r.rv ? "Đã nhận" : (r.pd ? "Chưa nhận" : (r.st.isEmpty ? "Chưa nhận" : r.st)),
                    color: r.rv ? Theme.success : (r.pd ? Theme.warning : .secondary)
                )
                if level == .overdue { Text("Quá hạn").font(.caption2.bold()).foregroundColor(Theme.danger) }
                else if level == .soon { Text("Sắp hạn").font(.caption2.bold()).foregroundColor(Theme.warning) }
            }
        }
        .contentShape(Rectangle())
    }
}

/// Bảng "Hàng hoàn về": theo dõi 4 chặng Đã đồng ý → Đang về → Đã về shop → Đã nhận kho.
struct ReturnWatchView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    private enum StageFilter: Int, CaseIterable {
        case active = 0
        case stage1 = 1
        case stage2 = 2
        case stage3 = 3
        case stage4 = 4
        case all = 99

        var title: String {
            switch self {
            case .active: return "Đang về"
            case .stage1: return "Đã đồng ý"
            case .stage2: return "Đang vận chuyển"
            case .stage3: return "Về shop"
            case .stage4: return "Đã nhận"
            case .all: return "Tất cả"
            }
        }
    }

    @State private var filter: StageFilter = .active
    @State private var detail: OrderRow?

    private var all: [OrderRow] { store.rows.filter { $0.inReturnWatch } }

    private func matches(_ r: OrderRow, _ f: StageFilter) -> Bool {
        switch f {
        case .active: return r.returnWatchStage < 4
        case .stage1: return r.returnWatchStage == 1
        case .stage2: return r.returnWatchStage == 2
        case .stage3: return r.returnWatchStage == 3
        case .stage4: return r.returnWatchStage >= 4
        case .all: return true
        }
    }

    private var list: [OrderRow] {
        all.filter { matches($0, filter) }.sorted { a, b in
            if a.returnWatchStage != b.returnWatchStage { return a.returnWatchStage > b.returnWatchStage }
            return a.rw < b.rw
        }
    }

    var body: some View {
        VStack(spacing: 10) {
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 8) {
                    ForEach(StageFilter.allCases, id: \.self) { f in
                        Chip(title: "\(f.title) (\(count(f)))", selected: filter == f) { filter = f }
                    }
                }
                .padding(.horizontal, 14)
                .padding(.top, 6)
            }

            if let b = engine.banner { BannerStrip(banner: b).padding(.horizontal, 14) }

            List {
                if list.isEmpty {
                    Text(store.rows.contains { $0.rw > 0 } ? "Không có đơn trong chặng này" : "Máy tính chưa gửi Bảng Hàng Hoàn Về — kéo xuống để tải lại")
                        .foregroundStyle(.secondary)
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)
                }
                ForEach(list) { r in
                    Button {
                        Haptics.tap()
                        detail = r
                    } label: {
                        line(r).card(padding: 14, radius: 16)
                    }
                    .buttonStyle(.plain)
                    .listRowSeparator(.hidden)
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets(top: 4, leading: 14, bottom: 4, trailing: 14))
                }
            }
            .listStyle(.plain)
            .scrollContentBackground(.hidden)
        }
        .background(Theme.pageBackground.ignoresSafeArea())
        .navigationTitle("Hàng hoàn về (\(count(.active)))")
        .navigationBarTitleDisplayMode(.inline)
        .sheet(item: $detail) { DetailView(row: $0) }
        .refreshable { engine.requestSnapshot(); try? await Task.sleep(nanoseconds: 2_000_000_000) }
    }

    private func count(_ f: StageFilter) -> Int { all.filter { matches($0, f) }.count }

    private func date(_ ms: Int) -> String {
        Date(timeIntervalSince1970: Double(ms) / 1000).formatted(date: .abbreviated, time: .shortened)
    }

    private func info(_ r: OrderRow) -> String {
        var parts: [String] = ["Vào theo dõi: " + date(r.rw)]
        if r.ar > 0 { parts.append("Về shop: " + date(r.ar)) }
        if !r.ps.isEmpty { parts.append("Sàn: " + r.ps) }
        return parts.joined(separator: " · ")
    }

    private func stageColor(_ stage: Int) -> Color {
        switch stage {
        case 1: return Theme.info
        case 2: return Theme.teal
        case 3: return Theme.warning
        default: return Theme.success
        }
    }

    private func line(_ r: OrderRow) -> some View {
        let stage = r.returnWatchStage
        let color = stageColor(stage)
        return VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                Text(r.o.isEmpty ? r.c : r.o)
                    .font(.system(.subheadline, design: .monospaced).bold())
                    .lineLimit(1)
                if !r.p.isEmpty { PlatformBadge(platform: r.p) }
                Spacer(minLength: 0)
                StatusPill(text: OrderRow.returnWatchStageNames[stage], color: color)
            }
            if !r.o.isEmpty && r.c.uppercased() != r.o.uppercased() {
                Text("Vận đơn: \(r.c)").font(.caption.monospaced()).foregroundStyle(.secondary)
            }
            HStack(spacing: 5) {
                ForEach(1...4, id: \.self) { k in
                    Capsule()
                        .fill(k <= stage ? color : Color.gray.opacity(0.22))
                        .frame(height: 6)
                }
            }
            if !r.n.isEmpty { Text(r.n).font(.caption).foregroundStyle(.secondary).lineLimit(2) }
            Text(info(r)).font(.caption2).foregroundStyle(.secondary)
        }
        .contentShape(Rectangle())
    }
}

/// Dòng báo kết quả gửi lệnh (đã gửi, máy tính đã làm xong, lỗi…) cho các bảng ngoài màn Quét.
struct BannerStrip: View {
    let banner: Banner
    @State private var visible = false
    @State private var shown: Banner?
    var body: some View {
        Group { if visible { strip } }
            .onAppear { shown = banner }
            .onChange(of: banner) { b in
                guard b != shown else { return }
                shown = b; visible = true
                Task { try? await Task.sleep(nanoseconds: 6_000_000_000); if shown == b { visible = false } }
            }
    }
    private var strip: some View {
        HStack(spacing: 6) {
            Text(banner.text).font(.caption.bold())
            if !banner.code.isEmpty { Text(banner.code).font(.caption.monospaced().bold()) }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 12).padding(.vertical, 8)
        .background(banner.color.opacity(0.92)).foregroundColor(.white)
        .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
    }
}
