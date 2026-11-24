import SwiftUI
import ScannerCore

private enum HistoryFilter: String, CaseIterable {
    case all = "Tất cả"
    case valid = "Hợp lệ"
    case duplicate = "Quét trùng"
    case missing = "Không có"
    case pending = "Chờ PC"

    var accentColor: Color {
        switch self {
        case .all: return Theme.accent
        case .valid: return Theme.success
        case .duplicate: return Theme.warning
        case .missing: return Theme.danger
        case .pending: return Theme.accent
        }
    }
}

private struct DayGroup: Identifiable {
    let title: String
    let items: [ScanItem]
    var id: String { title }
}

struct HistoryView: View {
    @EnvironmentObject var engine: SyncEngine
    @State private var kind: ScanKind = .returnScan
    @State private var filter: HistoryFilter = .all
    @State private var confirmClear = false
    @State private var query = ""
    @State private var detail: ScanItem?
    @State private var resentNote = ""

    /// Một nguồn dữ liệu duy nhất cho cả hàng chip đếm lẫn danh sách hiển thị.
    private var scopedItems: [ScanItem] {
        engine.items.filter { $0.kind == kind }
    }

    private func matches(_ it: ScanItem, _ f: HistoryFilter) -> Bool {
        switch f {
        case .all: return true
        case .valid: return it.status == .success
        case .duplicate: return it.status == .duplicate
        case .missing: return it.status == .notFound || it.status == .ambiguous
        case .pending: return !it.isSettled
        }
    }

    private var shown: [ScanItem] {
        scopedItems.filter { it in
            if !query.isEmpty && !it.code.localizedCaseInsensitiveContains(query) { return false }
            return matches(it, filter)
        }
    }

    private var grouped: [DayGroup] {
        let now = Date()
        var order: [String] = []
        var map: [String: [ScanItem]] = [:]
        for it in shown {
            let d = Date(timeIntervalSince1970: Double(it.time) / 1000)
            let key = DashboardMetrics.daySectionTitle(for: d, now: now)
            if map[key] == nil { order.append(key) }
            map[key, default: []].append(it)
        }
        return order.map { DayGroup(title: $0, items: map[$0] ?? []) }
    }

    private func filterCount(_ f: HistoryFilter) -> Int {
        scopedItems.filter { matches($0, f) }.count
    }

    var body: some View {
        NavigationStack {
            VStack(spacing: 6) {
                Picker("Loại", selection: $kind) {
                    Text("Quét hoàn kho").tag(ScanKind.returnScan)
                    Text("Đơn mới").tag(ScanKind.newOrder)
                }
                .pickerStyle(.segmented)
                .padding(.horizontal, Theme.screenPadding)
                .padding(.top, 4)

                // Duy nhất một hàng chip có số đếm (đồng nhất một nguồn số).
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 6) {
                        ForEach(HistoryFilter.allCases, id: \.self) { f in
                            FilterTile(
                                title: f.rawValue,
                                count: filterCount(f),
                                selected: filter == f,
                                accent: f.accentColor
                            ) {
                                filter = f
                            }
                        }
                    }
                    .padding(.horizontal, Theme.screenPadding)
                }

                let waiting = filterCount(.pending)
                if waiting > 0 && !engine.isDemoMode {
                    VStack(spacing: 4) {
                        Button {
                            let n = engine.resendAllPending(kind: kind)
                            resentNote = "Đã đưa \(n) đơn vào hàng gửi lại, app gửi dần vài đơn mỗi nhịp."
                            Haptics.medium()
                        } label: {
                            Label("Gửi lại tất cả lên máy tính (\(waiting))", systemImage: "arrow.clockwise")
                                .font(Theme.Typography.secondaryBold)
                                .frame(maxWidth: .infinity, minHeight: Theme.minTapTarget)
                        }
                        .buttonStyle(.borderedProminent)
                        .tint(Theme.accent)
                        if !resentNote.isEmpty {
                            Text(resentNote)
                                .font(Theme.Typography.micro)
                                .foregroundStyle(.secondary)
                        }
                    }
                    .padding(.horizontal, Theme.screenPadding)
                }

                List {
                    ForEach(grouped) { group in
                        Section {
                            ForEach(group.items) { it in
                                row(it)
                                    .card(padding: 10, radius: Theme.cardRadius)
                                    .contentShape(Rectangle())
                                    .onTapGesture {
                                        Haptics.tap()
                                        detail = it
                                    }
                                    .listRowSeparator(.hidden)
                                    .listRowBackground(Color.clear)
                                    .listRowInsets(EdgeInsets(top: 3, leading: Theme.screenPadding, bottom: 3, trailing: Theme.screenPadding))
                            }
                            .onDelete { offsets in
                                for idx in offsets {
                                    engine.delete(group.items[idx])
                                }
                            }
                        } header: {
                            Text(group.title)
                                .font(Theme.Typography.captionBold)
                                .foregroundStyle(.secondary)
                                .textCase(nil)
                        }
                    }
                }
                .listStyle(.plain)
                .scrollContentBackground(.hidden)
                .overlay {
                    if shown.isEmpty {
                        VStack(spacing: 8) {
                            IconTile(systemName: "clock.arrow.circlepath", color: .secondary, size: 38, cornerRadius: 10)
                            Text("Chưa có lịch sử quét")
                                .font(Theme.Typography.secondary)
                                .foregroundStyle(.secondary)
                        }
                    }
                }
            }
            .background(Theme.pageBackground.ignoresSafeArea())
            .searchable(text: $query, prompt: "Tìm mã vận đơn...")
            .navigationTitle("Lịch sử")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .navigationBarLeading) {
                    ShareLink(item: engine.csv(kind: kind), subject: Text("Lịch sử quét")) {
                        Image(systemName: "square.and.arrow.up")
                    }
                    .accessibilityLabel("Xuất CSV lịch sử quét")
                }
                ToolbarItem(placement: .navigationBarTrailing) {
                    Button("Xóa hết") { confirmClear = true }
                        .disabled(shown.isEmpty)
                }
            }
            .confirmationDialog("Xóa toàn bộ lịch sử mục này?", isPresented: $confirmClear) {
                Button("Xóa", role: .destructive) { engine.clear(kind: kind) }
            }
            .onChange(of: kind) { _ in filter = .all }
            .sheet(item: $detail) { ScanDetailView(requestId: $0.requestId) }
        }
    }

    // MARK: - Dòng lịch sử gọn (~52pt)
    private func scanIcon(_ it: ScanItem) -> String {
        if !it.scanConfirmed { return "clock.fill" }
        switch it.status {
        case .success: return "checkmark"
        case .duplicate: return "arrow.2.squarepath"
        case .notFound, .ambiguous: return "questionmark"
        default: return "exclamationmark"
        }
    }

    private func row(_ it: ScanItem) -> some View {
        let color = scanColor(it)
        return HStack(spacing: 10) {
            if let u = engine.photoURL(it), let img = UIImage(contentsOfFile: u.path) {
                Image(uiImage: img)
                    .resizable()
                    .scaledToFill()
                    .frame(width: 34, height: 34)
                    .clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
            } else {
                IconTile(systemName: scanIcon(it), color: color, size: 32, cornerRadius: 8)
            }

            VStack(alignment: .leading, spacing: 2) {
                HStack(spacing: 6) {
                    Text(it.code)
                        .font(Theme.Typography.code)
                        .foregroundColor(.primary)
                        .lineLimit(1)
                    Spacer(minLength: 4)
                    Text(Date(timeIntervalSince1970: Double(it.time) / 1000).formatted(date: .omitted, time: .shortened))
                        .font(Theme.Typography.micro)
                        .monospacedDigit()
                        .foregroundStyle(.secondary)
                }

                HStack(spacing: 5) {
                    Text(scanLabel(it))
                        .font(Theme.Typography.microBold)
                        .foregroundColor(color)
                    if it.scanConfirmed && it.photoFile != nil && !it.photoConfirmed && !it.reviewRequired {
                        Text("· Ảnh gửi ngầm (\(it.photoAttempts))")
                            .font(Theme.Typography.microBold)
                            .foregroundColor(Theme.warning)
                            .lineLimit(1)
                    } else if !it.scanConfirmed {
                        Text("· Chưa xác nhận (\(it.attempts) lần)")
                            .font(Theme.Typography.microBold)
                            .foregroundColor(Theme.warning)
                            .lineLimit(1)
                    } else if !it.detail.isEmpty {
                        Text("· \(it.detail)")
                            .font(Theme.Typography.micro)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                }
            }
        }
        .frame(minHeight: 34)
    }
}
