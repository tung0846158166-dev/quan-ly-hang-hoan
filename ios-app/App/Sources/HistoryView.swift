import SwiftUI
import ScannerCore

private enum HistoryFilter: String, CaseIterable {
    case valid = "Hợp lệ"
    case duplicate = "Quét trùng"
    case missing = "Không có"
    case pending = "Chờ PC"
}

private struct DayGroup: Identifiable {
    let title: String
    let items: [ScanItem]
    var id: String { title }
}

struct HistoryView: View {
    @EnvironmentObject var engine: SyncEngine
    @State private var kind: ScanKind = .returnScan
    @State private var filter: HistoryFilter?
    @State private var confirmClear = false
    @State private var query = ""
    @State private var detail: ScanItem?
    @State private var resentNote = ""

    private var shown: [ScanItem] {
        engine.items.filter { $0.kind == kind }.filter { it in
            if !query.isEmpty && !it.code.localizedCaseInsensitiveContains(query) { return false }
            guard let f = filter else { return true }
            switch f {
            case .valid: return it.status == .success
            case .duplicate: return it.status == .duplicate
            case .missing: return it.status == .notFound || it.status == .ambiguous
            case .pending: return !it.isSettled
            }
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
        engine.items.filter { $0.kind == kind }.filter { it in
            switch f {
            case .valid: return it.status == .success
            case .duplicate: return it.status == .duplicate
            case .missing: return it.status == .notFound || it.status == .ambiguous
            case .pending: return !it.isSettled
            }
        }.count
    }

    var body: some View {
        NavigationStack {
            VStack(spacing: 12) {
                Picker("Loại", selection: $kind) {
                    Text("Quét hoàn kho").tag(ScanKind.returnScan)
                    Text("Đơn mới").tag(ScanKind.newOrder)
                }
                .pickerStyle(.segmented)
                .padding(.horizontal, 16)

                todaySummaryCard
                    .padding(.horizontal, 16)

                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 8) {
                        ForEach(HistoryFilter.allCases, id: \.self) { f in
                            FilterTile(
                                title: f.rawValue,
                                count: filterCount(f),
                                selected: filter == f
                            ) {
                                filter = (filter == f ? nil : f)
                            }
                        }
                    }
                    .padding(.horizontal, 16)
                }

                let waiting = engine.items.filter { $0.kind == kind && !$0.isSettled }.count
                if waiting > 0 && !engine.isDemoMode {
                    VStack(spacing: 6) {
                        Button {
                            let n = engine.resendAllPending(kind: kind)
                            resentNote = "Đã đưa \(n) đơn vào hàng gửi lại, app gửi dần vài đơn mỗi nhịp."
                            Haptics.medium()
                        } label: {
                            Label("Gửi lại tất cả lên máy tính (\(waiting))", systemImage: "arrow.clockwise")
                                .font(.subheadline.bold())
                                .frame(maxWidth: .infinity, minHeight: 44)
                        }
                        .buttonStyle(.borderedProminent)
                        .tint(Theme.accent)
                        if !resentNote.isEmpty {
                            Text(resentNote)
                                .font(.caption2)
                                .foregroundStyle(.secondary)
                        }
                    }
                    .padding(.horizontal, 16)
                }

                List {
                    ForEach(grouped) { group in
                        Section {
                            ForEach(group.items) { it in
                                row(it)
                                    .card(padding: 12, radius: 16)
                                    .contentShape(Rectangle())
                                    .onTapGesture {
                                        Haptics.tap()
                                        detail = it
                                    }
                                    .listRowSeparator(.hidden)
                                    .listRowBackground(Color.clear)
                                    .listRowInsets(EdgeInsets(top: 4, leading: 16, bottom: 4, trailing: 16))
                            }
                            .onDelete { offsets in
                                for idx in offsets {
                                    engine.delete(group.items[idx])
                                }
                            }
                        } header: {
                            Text(group.title)
                                .font(.footnote.bold())
                                .foregroundStyle(.secondary)
                                .textCase(nil)
                        }
                    }
                }
                .listStyle(.plain)
                .scrollContentBackground(.hidden)
                .overlay {
                    if shown.isEmpty {
                        VStack(spacing: 10) {
                            IconTile(systemName: "clock.arrow.circlepath", color: .secondary, size: 44, cornerRadius: 12)
                            Text("Chưa có lịch sử quét")
                                .font(.subheadline.weight(.medium))
                                .foregroundStyle(.secondary)
                        }
                    }
                }
            }
            .background(Theme.pageBackground.ignoresSafeArea())
            .searchable(text: $query, prompt: "Tìm mã vận đơn...")
            .navigationTitle("Lịch sử")
            .navigationBarTitleDisplayMode(.large)
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
            .onChange(of: kind) { _ in filter = nil }
            .sheet(item: $detail) { ScanDetailView(requestId: $0.requestId) }
        }
    }

    // MARK: - 4 số liệu hôm nay thu gọn vào 1 thẻ duy nhất
    private var todaySummaryCard: some View {
        let s = engine.todayStats(kind: kind)
        return VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text("Hôm nay")
                    .font(.caption.bold())
                    .foregroundStyle(.secondary)
                Spacer()
                Text("Tổng \(s.total) lượt")
                    .font(.caption.bold())
                    .monospacedDigit()
                    .foregroundColor(Theme.accent)
            }

            HStack(spacing: 0) {
                statCell("Hợp lệ", s.valid, Theme.success)
                Divider().frame(height: 30)
                statCell("Trùng", s.dup, Theme.warning)
                Divider().frame(height: 30)
                statCell("Không có", s.missing, Theme.danger)
                Divider().frame(height: 30)
                statCell("Chờ PC", s.pending, Theme.accent)
            }
        }
        .card(padding: 14, radius: 18)
    }

    private func statCell(_ title: String, _ value: Int, _ color: Color) -> some View {
        VStack(spacing: 2) {
            AnimatedCounter(
                value: value,
                font: .system(size: 20, weight: .bold, design: .rounded),
                color: value > 0 ? color : .secondary
            )
            Text(title)
                .font(.caption2.weight(.medium))
                .foregroundStyle(.secondary)
                .lineLimit(1)
        }
        .frame(maxWidth: .infinity)
    }

    // MARK: - Dòng lịch sử gọn có biểu tượng kết quả
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
        return HStack(spacing: 12) {
            if let u = engine.photoURL(it), let img = UIImage(contentsOfFile: u.path) {
                Image(uiImage: img)
                    .resizable()
                    .scaledToFill()
                    .frame(width: 44, height: 44)
                    .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
            } else {
                IconTile(systemName: scanIcon(it), color: color, size: 36, cornerRadius: 10)
            }

            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 6) {
                    Text(it.code)
                        .font(.system(.subheadline, design: .monospaced).bold())
                        .foregroundColor(.primary)
                        .lineLimit(1)
                    Spacer(minLength: 4)
                    Text(Date(timeIntervalSince1970: Double(it.time) / 1000).formatted(date: .omitted, time: .shortened))
                        .font(.caption2)
                        .monospacedDigit()
                        .foregroundStyle(.secondary)
                }

                HStack(spacing: 6) {
                    Text(scanLabel(it))
                        .font(.caption.bold())
                        .foregroundColor(color)
                    if !it.detail.isEmpty {
                        Text("· \(it.detail)")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                }

                if it.scanConfirmed && it.photoFile != nil && !it.photoConfirmed && !it.reviewRequired {
                    Text("Ảnh đang gửi ngầm (lần \(it.photoAttempts))")
                        .font(.caption2.bold())
                        .foregroundColor(Theme.warning)
                } else if !it.scanConfirmed {
                    Text("Chưa xác nhận (đã gửi \(it.attempts) lần)")
                        .font(.caption2.bold())
                        .foregroundColor(Theme.warning)
                }
            }
        }
        .frame(minHeight: 44)
    }
}
