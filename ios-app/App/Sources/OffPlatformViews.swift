import SwiftUI
import ScannerCore

/// Bảng ĐƠN NGOÀI SÀN trên điện thoại (chủ dự án 27/11/2026): các đơn bán qua Facebook, Zalo, tại shop... + nút tạo nhanh.
struct OffPlatformView: View {
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    private enum Show: String, CaseIterable { case pending = "Chưa nhận", received = "Đã nhận", all = "Tất cả" }
    @State private var show: Show = .pending
    @State private var all: [OrderRow] = []
    @State private var selected: OrderRow?
    @State private var showCreate = false

    private var list: [OrderRow] {
        switch show {
        case .pending: return all.filter { !$0.rv }
        case .received: return all.filter { $0.rv }
        case .all: return all
        }
    }
    private func count(_ s: Show) -> Int {
        switch s {
        case .pending: return all.filter { !$0.rv }.count
        case .received: return all.filter { $0.rv }.count
        case .all: return all.count
        }
    }
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
                    VStack(alignment: .leading, spacing: 6) {
                        Text("Chưa có đơn ngoài sàn").font(Theme.Typography.primaryBold)
                        Text("Bấm \"Tạo đơn\" để thêm đơn bán qua Facebook, Zalo, tại shop... Không có mã vận đơn thì cloud tự sinh mã NS... để ghi lên kiện và quét nhận.")
                            .font(Theme.Typography.caption).foregroundStyle(.secondary)
                    }
                    .listRowBackground(Color.clear)
                    .listRowSeparator(.hidden)
                }
                ForEach(list) { r in
                    Button {
                        Haptics.tap()
                        selected = r
                    } label: {
                        line(r).card(padding: 12, radius: 16)
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
        .navigationTitle("Đơn ngoài sàn")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .navigationBarTrailing) {
                Button {
                    Haptics.tap()
                    showCreate = true
                } label: {
                    Label("Tạo đơn", systemImage: "plus.circle.fill")
                }
                .disabled(!engine.cloudActive || store.isDemoMode)
            }
        }
        .sheet(item: $selected) { DetailView(row: $0) }
        .sheet(isPresented: $showCreate) {
            OffPlatformCreateView(catalog: ProductCatalog.build(rows: store.rows))
        }
        .task(id: dataToken) {
            let rows = store.rows
            all = await Task.detached(priority: .userInitiated) {
                rows.filter { OffPlatform.isOffPlatform($0) && !$0.isArchived && !$0.isCancelled }
            }.value
        }
    }

    private func line(_ r: OrderRow) -> some View {
        HStack(spacing: 10) {
            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 6) {
                    Text(r.c).font(Theme.Typography.code).lineLimit(1)
                    let ch = OffPlatform.channel(of: r)
                    if !ch.isEmpty { StatusPill(text: ch, color: Theme.success) }
                    if !r.ty.isEmpty && r.ty != OffPlatform.orderTypes[0] { StatusPill(text: r.ty, color: Theme.info) }
                }
                Text(r.n.isEmpty ? "Chưa có sản phẩm" : r.n)
                    .font(Theme.Typography.caption).foregroundStyle(.secondary).lineLimit(2)
                if r.pr > 0 {
                    Text("\(r.pr.formatted(.number.locale(Locale(identifier: "vi_VN")))) đ · \(r.q) cái")
                        .font(Theme.Typography.micro).foregroundStyle(.secondary)
                }
            }
            Spacer(minLength: 4)
            StatusPill(text: r.rv ? "Đã nhận" : "Chưa nhận", color: r.rv ? Theme.success : Theme.warning)
            Image(systemName: "chevron.right").font(.caption2.bold()).foregroundStyle(.tertiary)
        }
        .contentShape(Rectangle())
    }
}

/// Form tạo nhanh đơn ngoài sàn: tìm sản phẩm đã có (gõ không dấu cũng được), bấm là thêm; màu / size gợi ý theo sản phẩm;
/// nhiều sản phẩm một đơn; kênh bán, loại đơn, giá trị; để trống mã thì cloud tự sinh mã "NS...". Không ghi thông tin người mua.
struct OffPlatformCreateView: View {
    let catalog: [CatalogProduct]
    @EnvironmentObject var engine: SyncEngine
    @Environment(\.dismiss) private var dismiss

    @State private var code = ""
    @State private var orderId = ""
    @State private var showScan = false
    @State private var channel = OffPlatform.channels[0]
    @State private var orderType = OffPlatform.orderTypes[0]
    @State private var search = ""
    @State private var lines: [NewOrderLine] = [NewOrderLine()]
    @State private var priceText = ""
    @State private var note = ""
    @State private var error = ""
    @State private var lastCreated = ""
    @State private var createdCount = 0
    @State private var busy = false

    private var suggestions: [CatalogProduct] {
        search.trimmingCharacters(in: .whitespaces).isEmpty ? [] : ProductCatalog.search(catalog, query: search, limit: 8)
    }
    private var summary: String {
        let filled = lines.filter { !$0.isBlank }
        return filled.isEmpty ? "Chưa có sản phẩm" : "\(filled.count) sản phẩm • \(filled.reduce(0) { $0 + max(1, $1.qty) }) cái"
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    HStack {
                        TextField("Mã vận đơn (để trống: tự sinh mã NS...)", text: $code)
                            .textInputAutocapitalization(.characters)
                            .autocorrectionDisabled()
                            .font(.system(.body, design: .monospaced))
                        Button {
                            Haptics.tap()
                            showScan = true
                        } label: {
                            Label("Quét", systemImage: "barcode.viewfinder")
                                .font(Theme.Typography.captionBold)
                                .padding(.horizontal, 10).padding(.vertical, 6)
                                .background(Theme.accent)
                                .foregroundColor(.white)
                                .clipShape(Capsule())
                        }
                        .buttonStyle(.plain)
                        .accessibilityLabel("Quét mã vận đơn")
                    }
                    TextField("Mã đơn hàng (không bắt buộc)", text: $orderId)
                        .textInputAutocapitalization(.characters)
                        .autocorrectionDisabled()
                        .font(.system(.body, design: .monospaced))
                    ScrollView(.horizontal, showsIndicators: false) {
                        HStack(spacing: 6) {
                            ForEach(OffPlatform.channels, id: \.self) { ch in
                                Button {
                                    Haptics.tap()
                                    channel = ch
                                } label: {
                                    Text(ch)
                                        .font(Theme.Typography.captionBold)
                                        .padding(.horizontal, 10).padding(.vertical, 6)
                                        .background(channel == ch ? Theme.success : Theme.subtleFill)
                                        .foregroundColor(channel == ch ? .white : .primary)
                                        .clipShape(Capsule())
                                }
                                .buttonStyle(.plain)
                            }
                        }
                    }
                    Picker("Loại đơn", selection: $orderType) {
                        ForEach(OffPlatform.orderTypes, id: \.self) { Text($0).tag($0) }
                    }
                } header: { Text("Đơn & kênh bán") }

                Section {
                    TextField("Tìm sản phẩm đã có (không dấu cũng được)", text: $search)
                        .autocorrectionDisabled()
                        .onSubmit(addFromSearch)
                    ForEach(suggestions) { p in
                        Button { add(p) } label: {
                            HStack {
                                Text(p.name).foregroundColor(.primary).lineLimit(2)
                                Spacer()
                                Text("\(p.units) cái").font(Theme.Typography.micro).foregroundStyle(.secondary)
                            }
                        }
                    }
                    if !search.trimmingCharacters(in: .whitespaces).isEmpty && suggestions.isEmpty {
                        Button("Thêm sản phẩm mới \"\(search.trimmingCharacters(in: .whitespaces))\"", action: addFromSearch)
                    }
                    if search.isEmpty && !catalog.isEmpty {
                        ScrollView(.horizontal, showsIndicators: false) {
                            HStack(spacing: 6) {
                                ForEach(catalog.prefix(10)) { p in
                                    Button { add(p) } label: {
                                        Text(p.name)
                                            .font(Theme.Typography.caption)
                                            .lineLimit(1)
                                            .padding(.horizontal, 10).padding(.vertical, 6)
                                            .background(Theme.subtleFill)
                                            .clipShape(Capsule())
                                    }
                                    .buttonStyle(.plain)
                                }
                            }
                        }
                    }
                } header: { Text("Thêm sản phẩm") } footer: {
                    Text(catalog.isEmpty ? "Chưa có dữ liệu sản phẩm: gõ tên rồi bấm Thêm." : "Gợi ý từ \(catalog.count) sản phẩm đã có. Hàng chip là sản phẩm hay dùng.")
                }

                Section {
                    ForEach($lines) { $line in
                        lineEditor($line)
                    }
                    .onDelete { idx in
                        lines.remove(atOffsets: idx)
                        if lines.isEmpty { lines = [NewOrderLine()] }
                    }
                    Button {
                        lines.append(NewOrderLine())
                    } label: { Label("Thêm dòng trống", systemImage: "plus") }
                } header: { Text("Sản phẩm trong đơn — \(summary)") }

                Section {
                    HStack {
                        TextField("Giá trị đơn (350000, 350k, 1,2tr)", text: $priceText)
                            .keyboardType(.numbersAndPunctuation)
                        if let p = OffPlatform.parsePrice(priceText), p > 0 {
                            Text("\(p.formatted(.number.locale(Locale(identifier: "vi_VN")))) đ")
                                .font(Theme.Typography.captionBold).foregroundColor(Theme.success)
                        }
                    }
                    TextField("Ghi chú / lý do (không ghi tên, SĐT khách)", text: $note, axis: .vertical)
                        .lineLimit(1...3)
                } header: { Text("Giá trị & ghi chú") }

                if !error.isEmpty || !lastCreated.isEmpty {
                    Section {
                        if !error.isEmpty { Text(error).foregroundColor(Theme.danger).font(Theme.Typography.secondaryBold) }
                        if !lastCreated.isEmpty { Text(lastCreated).foregroundColor(Theme.success).font(Theme.Typography.secondaryBold) }
                    }
                }

                Section {
                    Button {
                        submit(another: false)
                    } label: {
                        HStack { Spacer(); Text(busy ? "Đang tạo..." : "Tạo đơn").bold(); Spacer() }
                    }
                    .disabled(busy)
                    Button {
                        submit(another: true)
                    } label: {
                        HStack { Spacer(); Text("Tạo & nhập đơn tiếp"); Spacer() }
                    }
                    .disabled(busy)
                }
            }
            .sheet(isPresented: $showScan) {
                ScanSearchSheet(onCode: { scanned in
                    code = scanned
                    error = ""
                }, title: "Quét mã vận đơn", hint: "Đưa mã vạch hoặc QR trên kiện vào khung")
            }
            .navigationTitle("Tạo đơn ngoài sàn")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button(createdCount > 0 ? "Xong" : "Đóng") { dismiss() }
                }
            }
        }
    }

    @ViewBuilder
    private func lineEditor(_ line: Binding<NewOrderLine>) -> some View {
        let known = ProductCatalog.find(catalog, name: line.wrappedValue.product)
        VStack(alignment: .leading, spacing: 6) {
            TextField("Tên sản phẩm", text: line.product)
                .font(Theme.Typography.primaryBold)
            HStack(spacing: 8) {
                optionField("Màu", text: line.color, options: known?.colors ?? [])
                optionField("Size", text: line.size, options: known?.sizes ?? [])
                Stepper(value: line.qty, in: 1...9999) {
                    Text("SL \(line.wrappedValue.qty)").font(Theme.Typography.captionBold).monospacedDigit()
                }
                .fixedSize()
            }
        }
        .padding(.vertical, 2)
    }

    private func optionField(_ title: String, text: Binding<String>, options: [String]) -> some View {
        HStack(spacing: 2) {
            TextField(title, text: text)
                .font(Theme.Typography.secondary)
            if !options.isEmpty {
                Menu {
                    ForEach(options, id: \.self) { o in Button(o) { text.wrappedValue = o } }
                } label: {
                    Image(systemName: "chevron.down.circle").foregroundColor(Theme.accent)
                }
            }
        }
        .frame(maxWidth: .infinity)
    }

    private func add(_ p: CatalogProduct) {
        Haptics.tap()
        if let i = lines.indices.last, lines[i].isBlank {
            lines[i].product = p.name
            if p.colors.count == 1 { lines[i].color = p.colors[0] }
            if p.sizes.count == 1 { lines[i].size = p.sizes[0] }
        } else {
            lines.append(NewOrderLine(product: p.name, color: p.colors.count == 1 ? p.colors[0] : "", size: p.sizes.count == 1 ? p.sizes[0] : ""))
        }
        search = ""
        error = ""
    }

    private func addFromSearch() {
        let t = search.trimmingCharacters(in: .whitespaces)
        if t.isEmpty { return }
        if let first = suggestions.first { add(first); return }
        if let i = lines.indices.last, lines[i].isBlank { lines[i].product = t } else { lines.append(NewOrderLine(product: t)) }
        search = ""
        error = ""
    }

    private func submit(another: Bool) {
        if let e = CloudOps.offPlatformError(lines: lines, priceText: priceText) { error = e; return }
        guard engine.cloudActive else { error = "Cần kết nối cloud để tạo đơn."; return }
        let op = CloudOps.offPlatformOrder(code: code, orderId: orderId, lines: lines, price: OffPlatform.parsePrice(priceText) ?? 0,
                                           orderType: orderType, channel: channel, note: note)
        busy = true
        error = ""
        Task { @MainActor in
            let r = await engine.cloud.createOrder(op)
            busy = false
            guard r.outcome == "success" else {
                error = r.outcome == "duplicate" ? "⚠️ \(r.message)" : (r.message.isEmpty ? "Không tạo được đơn." : r.message)
                UINotificationFeedbackGenerator().notificationOccurred(.error)
                return
            }
            UINotificationFeedbackGenerator().notificationOccurred(.success)
            createdCount += 1
            let shown = r.code.isEmpty ? r.orderId : r.code
            lastCreated = "✅ Đã tạo đơn \(shown) (\(createdCount) đơn lượt này)"
            engine.banner = Banner(code: shown, text: "Đã tạo đơn ngoài sàn", color: Theme.success)
            if another {
                code = ""; orderId = ""; priceText = ""; note = ""; search = ""
                lines = [NewOrderLine()]
            } else {
                dismiss()
            }
        }
    }
}

/// Tra nhanh sản phẩm (chủ dự án 27/11/2026): gõ tên là thấy bao nhiêu cái về kho / bị hoàn hôm nay, tuần này, tháng này.
/// Tính trên mọi đơn của điện thoại (bảng chính + lưu trữ, mọi sàn), không theo bộ lọc bên dưới.
struct ProductLookupCard: View {
    @EnvironmentObject var store: SnapshotStore
    @State private var query = ""
    @State private var results: [ProductLookupItem] = []

    private var token: String { "\(query)|\(store.revision)|\(store.rows.count)" }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            SectionTitleRow(title: "Tra nhanh sản phẩm", icon: "magnifyingglass", iconColor: Theme.teal)
            HStack {
                Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
                TextField("Gõ tên sản phẩm (không dấu cũng được)", text: $query)
                    .autocorrectionDisabled()
                if !query.isEmpty {
                    Button { query = "" } label: { Image(systemName: "xmark.circle.fill").foregroundStyle(.secondary) }
                        .buttonStyle(.plain)
                }
            }
            .padding(10)
            .background(Theme.subtleFill)
            .clipShape(RoundedRectangle(cornerRadius: 10))

            if !query.trimmingCharacters(in: .whitespaces).isEmpty {
                if results.isEmpty {
                    Text("Không có sản phẩm nào khớp.").font(Theme.Typography.caption).foregroundStyle(.secondary)
                } else {
                    ForEach(results) { item in resultRow(item) }
                    Text("✅ Về kho = đã nhận (theo ngày nhận kho) • ↩️ Hoàn = theo ngày hoàn • Tuần tính từ thứ 2")
                        .font(Theme.Typography.micro).foregroundStyle(.secondary)
                }
            }
        }
        .card(padding: Theme.cardPadding, radius: Theme.cardRadius)
        .task(id: token) {
            let q = query
            if q.trimmingCharacters(in: .whitespaces).isEmpty { results = []; return }
            try? await Task.sleep(nanoseconds: 150_000_000)
            guard !Task.isCancelled else { return }
            let rows = store.rows
            let found = await Task.detached(priority: .userInitiated) { ProductMetrics.lookup(rows: rows, query: q, now: Date()) }.value
            guard !Task.isCancelled else { return }
            results = found
        }
    }

    private func resultRow(_ item: ProductLookupItem) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(item.name).font(Theme.Typography.primaryBold).lineLimit(2)
                Spacer()
                Text("\(item.orders) đơn").font(Theme.Typography.micro).foregroundStyle(.secondary)
            }
            Grid(alignment: .trailing, horizontalSpacing: 10, verticalSpacing: 4) {
                GridRow {
                    Text("").gridColumnAlignment(.leading)
                    header("Hôm nay"); header("Tuần này"); header("Tháng này"); header("Tất cả")
                }
                GridRow {
                    Text("✅ Về kho").font(Theme.Typography.captionBold).foregroundColor(Theme.success).gridColumnAlignment(.leading)
                    num(item.received.today, strong: true); num(item.received.week, strong: true); num(item.received.month, strong: true); num(item.received.total, strong: false)
                }
                GridRow {
                    Text("↩️ Hoàn").font(Theme.Typography.caption).foregroundStyle(.secondary).gridColumnAlignment(.leading)
                    num(item.returned.today, strong: false); num(item.returned.week, strong: false); num(item.returned.month, strong: false); num(item.returned.total, strong: false)
                }
            }
        }
        .padding(10)
        .background(Theme.subtleFill.opacity(0.6))
        .clipShape(RoundedRectangle(cornerRadius: 12))
    }

    private func header(_ t: String) -> some View { Text(t).font(Theme.Typography.micro).foregroundStyle(.secondary) }
    private func num(_ v: Int, strong: Bool) -> some View {
        Text("\(v)").font(strong ? Theme.Typography.primaryBold : Theme.Typography.secondary).monospacedDigit()
            .foregroundColor(strong ? .primary : .secondary)
    }
}
