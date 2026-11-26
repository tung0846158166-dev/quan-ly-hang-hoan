import SwiftUI
import UniformTypeIdentifiers
import UIKit
import ScannerCore

private enum HistoryFilter: String, CaseIterable {
    case all = "Tất cả"
    case valid = "Thành công"
    case duplicate = "Quét trùng"
    case missing = "Không có"
    case pending = "Đang gửi"

    var accentColor: Color {
        switch self {
        case .all: return Theme.accent
        case .valid: return Theme.success
        case .duplicate: return Theme.danger
        case .missing: return Theme.warning
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
    @EnvironmentObject var photoManager: OrderPhotoManager
    @State private var kind: ScanKind = .returnScan
    @State private var filter: HistoryFilter = .all
    @State private var confirmClear = false
    @State private var query = ""
    @State private var detail: ScanItem?
    @State private var resentNote = ""
    @State private var confirmDiscardStale = false
    @State private var discardStaleNotice: String?

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
        let staleItems = engine.stalePendingTempPhotoItems()
        let staleIds = Set(staleItems.map { $0.requestId })
        let staleSummary = engine.stalePendingTempPhotoSummary
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

                if !engine.isDemoMode && !staleItems.isEmpty {
                    VStack(alignment: .leading, spacing: 6) {
                        HStack(alignment: .top, spacing: 8) {
                            Image(systemName: "exclamationmark.triangle.fill")
                                .font(.subheadline)
                                .foregroundColor(Theme.warning)
                            VStack(alignment: .leading, spacing: 2) {
                                Text("\(staleItems.count) lượt quét treo quá 7 ngày còn ảnh tạm (\(staleSummary.formattedMB))")
                                    .font(Theme.Typography.captionBold)
                                    .foregroundColor(.primary)
                                Text("Lượt quét vẫn được giữ nguyên. Bạn có thể bỏ ảnh tạm để giải phóng dung lượng.")
                                    .font(Theme.Typography.micro)
                                    .foregroundStyle(.secondary)
                            }
                            Spacer(minLength: 4)
                        }
                        Button(role: .destructive) {
                            Haptics.medium()
                            confirmDiscardStale = true
                        } label: {
                            Label("Bỏ \(staleItems.count) ảnh tạm quá 7 ngày", systemImage: "trash")
                                .font(Theme.Typography.captionBold)
                                .frame(maxWidth: .infinity, minHeight: 34)
                        }
                        .buttonStyle(.bordered)
                        .tint(Theme.danger)
                    }
                    .padding(10)
                    .background(Theme.warning.opacity(0.12))
                    .clipShape(RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous))
                    .overlay(
                        RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous)
                            .stroke(Theme.warning.opacity(0.35), lineWidth: 0.8)
                    )
                    .padding(.horizontal, Theme.screenPadding)
                }

                if waiting > 0 && !engine.isDemoMode && !engine.cloudActive {
                    VStack(spacing: 4) {
                        Button {
                            let n = engine.resendAllPending(kind: kind)
                            resentNote = "Đã đưa \(n) lượt quét vào hàng gửi lại, app gửi dần vài lượt mỗi nhịp."
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
                                row(it, isStaleTemp: staleIds.contains(it.requestId))
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
            } message: {
                Text("Ảnh tạm của các lượt chưa xác nhận cũng bị xóa. Ảnh đã gắn vào Chi tiết đơn được giữ nguyên.")
            }
            .alert(
                "Bỏ \(staleSummary.photoCount) ảnh tạm treo quá 7 ngày (\(staleSummary.formattedMB))?",
                isPresented: $confirmDiscardStale
            ) {
                Button("Hủy", role: .cancel) {}
                Button("Bỏ ảnh tạm", role: .destructive) {
                    let res = engine.discardStalePendingTempPhotos()
                    discardStaleNotice = "Đã bỏ \(res.deletedCount) ảnh tạm (\(PhotoStorageSummary.formatMB(res.freedBytes))). Toàn bộ lượt quét vẫn được giữ nguyên."
                }
            } message: {
                Text("Chỉ xóa tệp ảnh tạm của các lượt quét chưa có kết quả quá 7 ngày. KHÔNG xóa lượt quét khỏi lịch sử và KHÔNG xóa ảnh đã gắn vào đơn.")
            }
            .alert("Ảnh tạm", isPresented: Binding(
                get: { discardStaleNotice != nil },
                set: { if !$0 { discardStaleNotice = nil } }
            )) {
                Button("Đã hiểu", role: .cancel) { discardStaleNotice = nil }
            } message: {
                Text(discardStaleNotice ?? "")
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

    private func row(_ it: ScanItem, isStaleTemp: Bool) -> some View {
        let _ = photoManager.revision
        let color = scanColor(it)
        return HStack(spacing: 10) {
            if let u = engine.scanDisplayPhotoURL(it), let img = ScanThumbCache.image(at: u) {
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
                    if !it.scanConfirmed {
                        if isStaleTemp {
                            Text("· Treo > 7 ngày (còn ảnh tạm)")
                                .font(Theme.Typography.microBold)
                                .foregroundColor(Theme.danger)
                                .lineLimit(1)
                        } else if it.hasTemporaryPhoto {
                            Text("· Chưa xác nhận · có ảnh tạm")
                                .font(Theme.Typography.microBold)
                                .foregroundColor(Theme.warning)
                                .lineLimit(1)
                        } else {
                            Text("· Chưa xác nhận (\(it.attempts) lần)")
                                .font(Theme.Typography.microBold)
                                .foregroundColor(Theme.warning)
                                .lineLimit(1)
                        }
                    } else if it.attachedPhotoId != nil {
                        Text("· Đã lưu ảnh vào đơn")
                            .font(Theme.Typography.micro)
                            .foregroundColor(Theme.teal)
                            .lineLimit(1)
                    } else if it.hasTemporaryPhoto {
                        Text("· Ảnh tạm chờ đồng bộ đơn")
                            .font(Theme.Typography.microBold)
                            .foregroundColor(Theme.info)
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
