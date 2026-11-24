import SwiftUI
import UniformTypeIdentifiers
import UIKit
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

/// Bộ đệm ảnh thu nhỏ gọn nhẹ tránh giải mã JPEG lặp lại trên luồng chính khi cuộn lịch sử.
private enum ScanThumbCache {
    static let cache: NSCache<NSString, UIImage> = {
        let c = NSCache<NSString, UIImage>()
        c.countLimit = 120
        return c
    }()

    static func image(at url: URL) -> UIImage? {
        let key = url.path as NSString
        if let hit = cache.object(forKey: key) { return hit }
        guard let img = UIImage(contentsOfFile: url.path) else { return nil }
        cache.setObject(img, forKey: key)
        return img
    }
}

/// Xuất CSV theo yêu cầu: chỉ tạo chuỗi CSV khi người dùng thực sự bấm Chia sẻ (không chạy trong `body`).
private struct HistoryCSVFile: Transferable {
    let items: [ScanItem]
    let kind: ScanKind

    static var transferRepresentation: some TransferRepresentation {
        DataRepresentation(exportedContentType: .commaSeparatedText) { file in
            Data(SyncEngine.buildCSV(items: file.items, kind: file.kind).utf8)
        }
    }
}

struct HistoryView: View {
    @EnvironmentObject var engine: SyncEngine
    @State private var kind: ScanKind = .returnScan
    @State private var filter: HistoryFilter = .all
    @State private var confirmClear = false
    @State private var query = ""
    @State private var detail: ScanItem?
    @State private var resentNote = ""

    private struct ComputedHistory {
        var counts: [HistoryFilter: Int]
        var shownCount: Int
        var grouped: [DayGroup]
    }

    private func computeHistory() -> ComputedHistory {
        var counts: [HistoryFilter: Int] = [:]
        var filtered: [ScanItem] = []
        let q = query.trimmingCharacters(in: .whitespacesAndNewlines)
        for it in engine.items where it.kind == kind {
            counts[.all, default: 0] += 1
            if it.status == .success { counts[.valid, default: 0] += 1 }
            if it.status == .duplicate { counts[.duplicate, default: 0] += 1 }
            if it.status == .notFound || it.status == .ambiguous { counts[.missing, default: 0] += 1 }
            if !it.isSettled { counts[.pending, default: 0] += 1 }

            if !q.isEmpty && !it.code.localizedCaseInsensitiveContains(q) { continue }
            let keep: Bool
            switch filter {
            case .all: keep = true
            case .valid: keep = it.status == .success
            case .duplicate: keep = it.status == .duplicate
            case .missing: keep = it.status == .notFound || it.status == .ambiguous
            case .pending: keep = !it.isSettled
            }
            if keep { filtered.append(it) }
        }
        let now = Date()
        var order: [String] = []
        var map: [String: [ScanItem]] = [:]
        for it in filtered {
            let d = Date(timeIntervalSince1970: Double(it.time) / 1000)
            let key = DashboardMetrics.daySectionTitle(for: d, now: now)
            if map[key] == nil { order.append(key) }
            map[key, default: []].append(it)
        }
        let groups = order.map { DayGroup(title: $0, items: map[$0] ?? []) }
        return ComputedHistory(counts: counts, shownCount: filtered.count, grouped: groups)
    }

    var body: some View {
        let state = computeHistory()
        let waiting = state.counts[.pending] ?? 0
        return NavigationStack {
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
                                count: state.counts[f] ?? 0,
                                selected: filter == f,
                                accent: f.accentColor
                            ) {
                                filter = f
                            }
                        }
                    }
                    .padding(.horizontal, Theme.screenPadding)
                }

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
                    ForEach(state.grouped) { group in
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
                                let targets = offsets.compactMap { $0 < group.items.count ? group.items[$0] : nil }
                                for item in targets {
                                    engine.delete(item)
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
                    if state.shownCount == 0 {
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
                    ShareLink(
                        item: HistoryCSVFile(items: engine.items, kind: kind),
                        preview: SharePreview("Lịch sử quét.csv")
                    ) {
                        Image(systemName: "square.and.arrow.up")
                    }
                    .accessibilityLabel("Xuất CSV lịch sử quét")
                }
                ToolbarItem(placement: .navigationBarTrailing) {
                    Button("Xóa hết") { confirmClear = true }
                        .disabled(state.shownCount == 0)
                }
            }
            .confirmationDialog("Xóa toàn bộ lịch sử mục này?", isPresented: $confirmClear) {
                Button("Xóa", role: .destructive) { engine.clear(kind: kind) }
            }
            .onChange(of: kind) { _ in filter = .all; resentNote = "" }
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
            if let u = engine.photoURL(it), let img = ScanThumbCache.image(at: u) {
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
