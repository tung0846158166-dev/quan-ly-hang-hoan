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
    @State private var all: [OrderRow] = []

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
    private var dataToken: String { "\(store.revision)|\(store.isDemoMode ? 1 : 0)|\(store.rows.count)" }

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
        .task(id: dataToken) {
            let rowsSnapshot = store.rows
            let filtered = await Task.detached(priority: .userInitiated) {
                rowsSnapshot.filter { $0.k == "c" }
            }.value
            guard !Task.isCancelled else { return }
            all = filtered
        }
        .refreshable {
            guard !store.isDemoMode else { return }
            engine.requestSnapshot()
            try? await Task.sleep(nanoseconds: 2_000_000_000)
        }
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
    @State private var all: [OrderRow] = []

    private var list: [OrderRow] {
        switch show {
        case .pending: return all.filter { !$0.rv }
        case .received: return all.filter { $0.rv }
        case .all: return all
        }
    }
    private var allPicked: Bool { !list.isEmpty && list.allSatisfy { picked.contains($0.id) } }
    private var dataToken: String { "\(store.revision)|\(store.isDemoMode ? 1 : 0)|\(store.rows.count)" }

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
        .task(id: dataToken) {
            let rowsSnapshot = store.rows
            let filtered = await Task.detached(priority: .userInitiated) {
                rowsSnapshot.filter { $0.k == "m" && $0.isCancelAfterPrep }
            }.value
            guard !Task.isCancelled else { return }
            all = filtered
        }
        .refreshable {
            guard !store.isDemoMode else { return }
            engine.requestSnapshot()
            try? await Task.sleep(nanoseconds: 2_000_000_000)
        }
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

/// Bảng "Hàng hoàn về": đường ống 4 chặng lớn ở đầu (chạm chặng để lọc), mỗi đơn có thanh tiến trình 4 điểm.
struct ReturnWatchView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private enum StageFilter: Int, CaseIterable {
        case active = 0
        case stage1 = 1
        case stage2 = 2
        case stage3 = 3
        case stage4 = 4
        case all = 99

        var title: String {
            switch self {
            case .active: return "Đang về (1–3)"
            case .stage1: return "Đã đồng ý"
            case .stage2: return "Đang về"
            case .stage3: return "Về shop"
            case .stage4: return "Nhận kho"
            case .all: return "Tất cả"
            }
        }
    }

    @State private var filter: StageFilter = .active
    @State private var detail: OrderRow?
    @State private var all: [OrderRow] = []

    private var dataToken: String { "\(store.revision)|\(store.isDemoMode ? 1 : 0)|\(store.rows.count)" }

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
        VStack(spacing: 12) {
            pipelineHeader
                .padding(.horizontal, 16)
                .padding(.top, 6)

            if let b = engine.banner { BannerStrip(banner: b).padding(.horizontal, 16) }

            List {
                if list.isEmpty {
                    Text(store.rows.contains { $0.rw > 0 } ? "Không có đơn trong chặng này" : "Máy tính chưa gửi Bảng Hàng Hoàn Về — kéo xuống để tải lại")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)
                }
                ForEach(list) { r in
                    Button {
                        Haptics.tap()
                        detail = r
                    } label: {
                        line(r).card(padding: 15, radius: 18)
                    }
                    .buttonStyle(.plain)
                    .listRowSeparator(.hidden)
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets(top: 5, leading: 16, bottom: 5, trailing: 16))
                }
            }
            .listStyle(.plain)
            .scrollContentBackground(.hidden)
        }
        .background(Theme.pageBackground.ignoresSafeArea())
        .navigationTitle("Hàng hoàn về")
        .navigationBarTitleDisplayMode(.inline)
        .sheet(item: $detail) { DetailView(row: $0) }
        .task(id: dataToken) {
            let rowsSnapshot = store.rows
            let filtered = await Task.detached(priority: .userInitiated) {
                rowsSnapshot.filter { $0.inReturnWatch }
            }.value
            guard !Task.isCancelled else { return }
            all = filtered
        }
        .refreshable {
            guard !store.isDemoMode else { return }
            engine.requestSnapshot()
            try? await Task.sleep(nanoseconds: 1_500_000_000)
        }
    }

    // MARK: - Đường ống 4 chặng lớn ở đầu (bấm chặng để lọc)
    private var pipelineHeader: some View {
        let stages: [(StageFilter, Int, String, Color)] = [
            (.stage1, 1, "Đồng ý", Theme.info),
            (.stage2, 2, "Đang về", Theme.teal),
            (.stage3, 3, "Về shop", Theme.warning),
            (.stage4, 4, "Nhận kho", Theme.success)
        ]

        return VStack(spacing: 12) {
            HStack(spacing: 6) {
                ForEach(stages, id: \.1) { item in
                    let selected = filter == item.0
                    let cnt = count(item.0)
                    Button {
                        Haptics.tap()
                        let next: StageFilter = selected ? .active : item.0
                        if reduceMotion { filter = next } else { withAnimation(.easeInOut(duration: 0.18)) { filter = next } }
                    } label: {
                        VStack(spacing: 6) {
                            ZStack {
                                Circle()
                                    .fill(selected ? item.3 : item.3.opacity(0.16))
                                    .frame(width: 38, height: 38)
                                Text("\(cnt)")
                                    .font(.system(size: 16, weight: .bold, design: .rounded))
                                    .foregroundColor(selected ? .white : item.3)
                                    .monospacedDigit()
                            }
                            Text(item.2)
                                .font(.caption2.bold())
                                .foregroundColor(selected ? item.3 : .secondary)
                                .lineLimit(1)
                                .minimumScaleFactor(0.8)
                        }
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 8)
                        .background(selected ? item.3.opacity(0.12) : Theme.subtleFill)
                        .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
                        .overlay(
                            RoundedRectangle(cornerRadius: 14, style: .continuous)
                                .stroke(selected ? item.3.opacity(0.6) : Color.clear, lineWidth: 1.2)
                        )
                    }
                    .buttonStyle(.plain)
                }
            }

            HStack(spacing: 8) {
                quickScopePill("Đang về (\(count(.active)))", selected: filter == .active) { filter = .active }
                quickScopePill("Tất cả (\(count(.all)))", selected: filter == .all) { filter = .all }
                Spacer()
            }
        }
        .card(padding: 14, radius: 20)
    }

    private func quickScopePill(_ title: String, selected: Bool, action: @escaping () -> Void) -> some View {
        Button {
            Haptics.tap()
            action()
        } label: {
            Text(title)
                .font(.caption.bold())
                .padding(.horizontal, 12)
                .padding(.vertical, 6)
                .background(selected ? Theme.accent : Theme.subtleFill)
                .foregroundColor(selected ? .white : .secondary)
                .clipShape(Capsule())
        }
        .buttonStyle(.plain)
    }

    private func count(_ f: StageFilter) -> Int { all.filter { matches($0, f) }.count }

    private func date(_ ms: Int) -> String {
        Date(timeIntervalSince1970: Double(ms) / 1000).formatted(date: .abbreviated, time: .shortened)
    }

    private func info(_ r: OrderRow) -> String {
        var parts: [String] = ["Vào bảng: " + date(r.rw)]
        if r.ar > 0 { parts.append("Về shop: " + date(r.ar)) }
        if !r.ps.isEmpty { parts.append(r.ps) }
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
        return VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 8) {
                if !r.p.isEmpty { PlatformBadge(platform: r.p) }
                Text(r.c.isEmpty ? r.o : r.c)
                    .font(.system(.subheadline, design: .monospaced).bold())
                    .foregroundColor(.primary)
                    .lineLimit(1)
                Spacer(minLength: 4)
                StatusPill(text: r.returnWatchStageName, color: color)
            }

            // Thanh tiến trình 4 điểm nối liền
            HStack(spacing: 0) {
                ForEach(1...4, id: \.self) { k in
                    Circle()
                        .fill(k <= stage ? color : Color.gray.opacity(0.28))
                        .frame(width: k == stage ? 11 : 8, height: k == stage ? 11 : 8)
                    if k < 4 {
                        Rectangle()
                            .fill(k < stage ? color : Color.gray.opacity(0.22))
                            .frame(height: 2.5)
                            .frame(maxWidth: .infinity)
                    }
                }
            }
            .padding(.vertical, 2)

            if !r.n.isEmpty {
                Text(r.n)
                    .font(.footnote)
                    .foregroundStyle(.secondary)
                    .lineLimit(2)
            }
            Text(info(r))
                .font(.caption2)
                .foregroundStyle(.secondary)
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
