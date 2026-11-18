import SwiftUI
import ScannerCore

/// Bảng đơn mới trên điện thoại = Hộp Đơn Mới trên máy tính: tích chọn rồi đẩy ra bảng chính, hoặc đẩy hết.
struct InboxView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @State private var picked: Set<String> = []
    @State private var confirmAll = false
    @State private var manual = ""
    @State private var showScanner = false

    private var allPicked: Bool { !store.inbox.isEmpty && picked.count == store.inbox.count }

    /// Tạo đơn mới ngay tại đây (nhập mã tay hoặc quét), giống chế độ "Đơn mới" ở màn Quét: đơn vào Hộp Đơn Mới trên máy tính.
    private var addBar: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 8) {
                TextField("Nhập mã tay để tạo đơn mới", text: $manual)
                    .textInputAutocapitalization(.characters).autocorrectionDisabled()
                    .textFieldStyle(.roundedBorder)
                    .submitLabel(.send)
                    .onSubmit(sendManual)
                Button { showScanner = true } label: { Image(systemName: "barcode.viewfinder").font(.title3) }
                    .buttonStyle(.bordered)
                Button("Tạo", action: sendManual)
                    .buttonStyle(.borderedProminent)
                    .disabled(manual.trimmingCharacters(in: .whitespaces).isEmpty)
            }
            Text("Mã nhập hoặc quét ở đây được gửi sang Hộp Đơn Mới trên máy tính (giống chế độ Đơn mới ở màn Quét).")
                .font(.caption2).foregroundStyle(.secondary)
        }
    }

    private func sendManual() {
        let v = manual.trimmingCharacters(in: .whitespaces)
        guard !v.isEmpty else { return }
        if engine.submit(raw: v, kind: .newOrder, photo: nil, useSnapshot: false) { manual = "" }
    }

    var body: some View {
        List {
            Section {
                addBar
                if let b = engine.banner { BannerStrip(banner: b) }
            }
            if store.inbox.isEmpty {
                Text(store.hasData ? "Bảng đơn mới đang trống" : "Chưa nhận dữ liệu từ máy tính")
                    .foregroundStyle(.secondary)
            }
            ForEach(store.inbox) { it in
                Button { toggle(it.id) } label: { line(it) }
                    .buttonStyle(.plain)
            }
        }
        .listStyle(.plain)
        .navigationTitle("Đơn mới (\(store.inbox.count))")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .navigationBarTrailing) {
                Button(allPicked ? "Bỏ chọn" : "Chọn hết") {
                    picked = allPicked ? [] : Set(store.inbox.map { $0.id })
                }
                .disabled(store.inbox.isEmpty)
            }
        }
        .safeAreaInset(edge: .bottom) {
            HStack(spacing: 10) {
                Button {
                    engine.pushInbox(ids: store.inbox.map { $0.id }.filter { picked.contains($0) })
                    picked = []
                } label: { Text("Đẩy ra (\(picked.count))").bold().frame(maxWidth: .infinity) }
                    .buttonStyle(.borderedProminent)
                    .disabled(picked.isEmpty)
                Button { confirmAll = true } label: { Text("Đẩy ra hết (\(store.inbox.count))").bold().frame(maxWidth: .infinity) }
                    .buttonStyle(.bordered)
                    .disabled(store.inbox.isEmpty)
            }
            .padding(.horizontal).padding(.vertical, 8)
            .background(.bar)
        }
        .confirmationDialog("Đẩy toàn bộ \(store.inbox.count) đơn ra bảng chính?", isPresented: $confirmAll, titleVisibility: .visible) {
            Button("Đẩy ra hết") {
                engine.pushInbox(ids: store.inbox.map { $0.id })
                picked = []
            }
        }
        .onChange(of: store.inbox) { list in picked = picked.intersection(Set(list.map { $0.id })) }
        .refreshable { engine.requestSnapshot(); try? await Task.sleep(nanoseconds: 2_000_000_000) }
        .sheet(isPresented: $showScanner) {
            ScanSearchSheet(onCode: { code in _ = engine.submit(raw: code, kind: .newOrder, photo: nil, useSnapshot: false) },
                            title: "Quét để tạo đơn mới", hint: "Đưa mã vận đơn vào khung để tạo đơn mới")
        }
    }

    private func toggle(_ id: String) {
        if picked.contains(id) { picked.remove(id) } else { picked.insert(id) }
    }

    private func line(_ it: InboxRow) -> some View {
        let on = picked.contains(it.id)
        return HStack(spacing: 10) {
            Image(systemName: on ? "checkmark.circle.fill" : "circle")
                .font(.title2)
                .foregroundColor(on ? Color.accentColor : Color.secondary)
            VStack(alignment: .leading, spacing: 3) {
                Text(it.c.isEmpty ? it.o : it.c).font(.system(.body, design: .monospaced).bold())
                let sub = [it.p, it.t, it.n].filter { !$0.isEmpty }.joined(separator: " · ")
                if !sub.isEmpty { Text(sub).font(.caption).foregroundStyle(.secondary).lineLimit(2) }
                if it.dup {
                    Text("Đã có ở bảng khác: đẩy ra chỉ gỡ khỏi hộp, không tạo trùng").font(.caption2.bold()).foregroundColor(.orange)
                } else if it.u {
                    Text("Đã quét ở kho: đẩy ra sẽ ghi Hàng đã trả").font(.caption2.bold()).foregroundColor(.green)
                } else {
                    Text("Đẩy ra sẽ ghi Chưa trả (chờ hàng về)").font(.caption2).foregroundStyle(.secondary)
                }
            }
            Spacer(minLength: 0)
        }
        .padding(.vertical, 4)
        .contentShape(Rectangle())
    }
}

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
        VStack(spacing: 6) {
            Picker("", selection: $show) {
                ForEach(Show.allCases, id: \.self) { s in
                    Text("\(s.rawValue) (\(count(s)))").tag(s)
                }
            }
            .pickerStyle(.segmented)
            .padding(.horizontal)
            if let b = engine.banner { BannerStrip(banner: b).padding(.horizontal) }
            List {
                if list.isEmpty { Text("Không có đơn hủy").foregroundStyle(.secondary) }
                ForEach(list) { r in
                    Button { toggle(r.ci) } label: { line(r) }
                        .buttonStyle(.plain)
                        .disabled(r.ci.isEmpty)
                }
            }
            .listStyle(.plain)
        }
        .navigationTitle("Đơn hủy (\(all.count))")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .navigationBarTrailing) {
                Button(allPicked ? "Bỏ chọn" : "Chọn hết") {
                    if allPicked { picked.subtract(selectable.map { $0.ci }) } else { picked.formUnion(selectable.map { $0.ci }) }
                }
                .disabled(selectable.isEmpty)
            }
        }
        .safeAreaInset(edge: .bottom) {
            VStack(spacing: 8) {
                HStack(spacing: 10) {
                    Button { apply(true) } label: { Text("✅ Đã nhận (\(picked.count))").bold().frame(maxWidth: .infinity) }
                        .buttonStyle(.borderedProminent).tint(.green)
                        .disabled(picked.isEmpty)
                    Button { apply(false) } label: { Text("↩ Chưa nhận (\(picked.count))").bold().frame(maxWidth: .infinity) }
                        .buttonStyle(.bordered)
                        .disabled(picked.isEmpty)
                }
                Button { confirmAll = true } label: { Text("Nhận hết đơn chưa nhận (\(pendingIds.count))").bold().frame(maxWidth: .infinity) }
                    .buttonStyle(.bordered)
                    .disabled(pendingIds.isEmpty)
            }
            .padding(.horizontal).padding(.vertical, 8)
            .background(.bar)
        }
        .confirmationDialog("Đánh dấu đã nhận toàn bộ \(pendingIds.count) đơn hủy chưa nhận?", isPresented: $confirmAll, titleVisibility: .visible) {
            Button("Đã nhận hết") {
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
        let ids = list.map { $0.ci }.filter { picked.contains($0) }
        engine.setCancelledReceived(ids: ids.isEmpty ? Array(picked) : ids, received: received)
        picked = []
    }

    private func line(_ r: OrderRow) -> some View {
        let on = picked.contains(r.ci)
        return HStack(spacing: 10) {
            Image(systemName: on ? "checkmark.circle.fill" : "circle")
                .font(.title2)
                .foregroundColor(r.ci.isEmpty ? Color.secondary.opacity(0.3) : (on ? Color.accentColor : Color.secondary))
            VStack(alignment: .leading, spacing: 3) {
                Text(r.c).font(.system(.body, design: .monospaced).bold())
                let sub = [r.p, r.rd, r.n].filter { !$0.isEmpty }.joined(separator: " · ")
                if !sub.isEmpty { Text(sub).font(.caption).foregroundStyle(.secondary).lineLimit(2) }
            }
            Spacer(minLength: 0)
            Text(r.rv ? "Đã nhận" : "Chưa nhận")
                .font(.caption.bold())
                .padding(.horizontal, 8).padding(.vertical, 3)
                .background((r.rv ? Color.green : Color.orange).opacity(0.18), in: Capsule())
                .foregroundColor(r.rv ? .green : .orange)
        }
        .padding(.vertical, 4)
        .contentShape(Rectangle())
    }
}

/// Bảng "Hủy sau chuẩn bị": đơn ở bảng chính có cột Trạng thái sàn ghi "Huỷ sau chuẩn bị" (shop đã đóng gói rồi đơn mới bị huỷ,
/// hàng đang quay về hoặc vẫn nằm ở kho). Tích chọn để đánh dấu Hàng đã trả / Không cần nhận; chạm giữ để xem chi tiết.
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
        VStack(spacing: 6) {
            Picker("", selection: $show) {
                ForEach(Show.allCases, id: \.self) { s in Text("\(s.rawValue) (\(count(s)))").tag(s) }
            }
            .pickerStyle(.segmented)
            .padding(.horizontal)
            if let b = engine.banner { BannerStrip(banner: b).padding(.horizontal) }
            List {
                if list.isEmpty {
                    Text(store.rows.contains { !$0.ps.isEmpty } ? "Không có đơn Hủy sau chuẩn bị" : "Máy tính chưa gửi cột Trạng thái sàn — cập nhật máy tính lên bản mới rồi kéo xuống để tải lại")
                        .foregroundStyle(.secondary)
                }
                ForEach(list) { r in
                    Button { toggle(r.id) } label: { line(r) }
                        .buttonStyle(.plain)
                        .contextMenu { Button("Xem chi tiết") { detail = r } }
                }
            }
            .listStyle(.plain)
        }
        .navigationTitle("Hủy sau chuẩn bị (\(all.count))")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .navigationBarTrailing) {
                Button(allPicked ? "Bỏ chọn" : "Chọn hết") {
                    if allPicked { picked.subtract(list.map { $0.id }) } else { picked.formUnion(list.map { $0.id }) }
                }
                .disabled(list.isEmpty)
            }
        }
        .safeAreaInset(edge: .bottom) {
            HStack(spacing: 10) {
                Button { apply("Hàng đã trả") } label: { Text("✅ Đã nhận (\(picked.count))").bold().frame(maxWidth: .infinity) }
                    .buttonStyle(.borderedProminent).tint(.green)
                    .disabled(picked.isEmpty)
                Button { apply("Không cần nhận") } label: { Text("Không cần nhận").bold().frame(maxWidth: .infinity) }
                    .buttonStyle(.bordered)
                    .disabled(picked.isEmpty)
            }
            .padding(.horizontal).padding(.vertical, 8)
            .background(.bar)
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
                .foregroundColor(on ? Color.accentColor : Color.secondary)
            VStack(alignment: .leading, spacing: 3) {
                Text(r.o.isEmpty ? r.c : r.o).font(.system(.body, design: .monospaced).bold())
                if !r.o.isEmpty && r.c.uppercased() != r.o.uppercased() {
                    Text("Vận đơn: \(r.c)").font(.caption.monospaced()).foregroundStyle(.secondary)
                }
                Text(r.ps).font(.caption.bold()).foregroundColor(.purple)
                let sub = [r.rd, r.n].filter { !$0.isEmpty }.joined(separator: " · ")
                if !sub.isEmpty { Text(sub).font(.caption).foregroundStyle(.secondary).lineLimit(2) }
            }
            Spacer(minLength: 0)
            VStack(alignment: .trailing, spacing: 3) {
                Text(r.rv ? "Đã nhận" : (r.pd ? "Chưa nhận" : (r.st.isEmpty ? "Chưa nhận" : r.st)))
                    .font(.caption.bold())
                    .padding(.horizontal, 8).padding(.vertical, 3)
                    .background((r.rv ? Color.green : (r.pd ? Color.orange : Color.gray)).opacity(0.18), in: Capsule())
                    .foregroundColor(r.rv ? .green : (r.pd ? .orange : .gray))
                if level == .overdue { Text("Quá hạn").font(.caption2.bold()).foregroundColor(.red) }
                else if level == .soon { Text("Sắp hạn").font(.caption2.bold()).foregroundColor(.orange) }
            }
        }
        .padding(.vertical, 4)
        .contentShape(Rectangle())
    }
}


/// Dòng báo kết quả gửi lệnh (đã gửi, máy tính đã làm xong, lỗi…) cho các bảng ngoài màn Quét.
/// Chỉ hiện các thông báo mới (trong 6 giây sau khi đổi), không hiện lại thông báo cũ từ màn Quét.
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
        .padding(.horizontal, 10).padding(.vertical, 6)
        .background(banner.color.opacity(0.9)).foregroundColor(.white)
        .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
    }
}
