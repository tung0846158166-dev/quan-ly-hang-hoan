import SwiftUI
import ScannerCore

private enum HistoryFilter: String, CaseIterable {
    case valid = "Hợp lệ", duplicate = "Quét trùng", missing = "Không có", pending = "Chờ nạp PC"
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
            case .pending: return !it.isSettled   // gồm cả lượt quét đã xác nhận nhưng ảnh chưa gửi xong
            }
        }
    }

    var body: some View {
        NavigationStack {
            VStack(spacing: 10) {
                Picker("Loại", selection: $kind) {
                    Text("Quét hoàn kho").tag(ScanKind.returnScan)
                    Text("Đơn mới").tag(ScanKind.newOrder)
                }
                .pickerStyle(.segmented)
                .padding(.horizontal, 14)

                statsLine

                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 8) {
                        ForEach(HistoryFilter.allCases, id: \.self) { f in
                            Chip(title: f.rawValue, selected: filter == f) { filter = (filter == f ? nil : f) }
                        }
                    }
                    .padding(.horizontal, 14)
                }

                let waiting = engine.items.filter { $0.kind == kind && !$0.isSettled }.count
                if waiting > 0 {
                    VStack(spacing: 6) {
                        Button {
                            let n = engine.resendAllPending(kind: kind)
                            resentNote = "Đã đưa \(n) đơn vào hàng gửi lại, app gửi dần vài đơn mỗi nhịp."
                            if SyncEngine.pref("hapticOn") { UINotificationFeedbackGenerator().notificationOccurred(.success) }
                        } label: {
                            Label("Gửi lại tất cả lên máy tính (\(waiting))", systemImage: "arrow.clockwise")
                                .font(.subheadline.bold())
                                .frame(maxWidth: .infinity, minHeight: 44)
                        }
                        .buttonStyle(.borderedProminent)
                        .tint(Theme.accent)
                        if !resentNote.isEmpty { Text(resentNote).font(.caption2).foregroundStyle(.secondary) }
                    }
                    .padding(.horizontal, 14)
                }

                List {
                    ForEach(shown) { it in
                        row(it)
                            .card(padding: 12, radius: 16)
                            .contentShape(Rectangle())
                            .onTapGesture {
                                Haptics.tap()
                                detail = it
                            }
                            .listRowSeparator(.hidden)
                            .listRowBackground(Color.clear)
                            .listRowInsets(EdgeInsets(top: 4, leading: 14, bottom: 4, trailing: 14))
                    }
                    .onDelete { idx in idx.map { shown[$0] }.forEach(engine.delete) }
                }
                .listStyle(.plain)
                .scrollContentBackground(.hidden)
                .overlay {
                    if shown.isEmpty {
                        VStack(spacing: 6) {
                            Image(systemName: "clock.arrow.circlepath")
                                .font(.title2)
                                .foregroundStyle(.secondary)
                            Text("Chưa có lịch sử quét").foregroundStyle(.secondary)
                        }
                    }
                }
            }
            .background(Theme.pageBackground.ignoresSafeArea())
            .searchable(text: $query, prompt: "Tìm mã")
            .navigationTitle("Lịch sử")
            .toolbar {
                ToolbarItem(placement: .navigationBarLeading) {
                    ShareLink(item: engine.csv(kind: kind), subject: Text("Lịch sử quét")) { Image(systemName: "square.and.arrow.up") }
                }
                ToolbarItem(placement: .navigationBarTrailing) {
                    Button("Xóa hết") { confirmClear = true }.disabled(shown.isEmpty)
                }
            }
            .confirmationDialog("Xóa toàn bộ lịch sử mục này?", isPresented: $confirmClear) {
                Button("Xóa", role: .destructive) { engine.clear(kind: kind) }
            }
            .onChange(of: kind) { _ in filter = nil }
            .sheet(item: $detail) { ScanDetailView(requestId: $0.requestId) }
        }
    }

    private var statsLine: some View {
        let s = engine.todayStats(kind: kind)
        return VStack(alignment: .leading, spacing: 8) {
            Text("Hôm nay: \(s.total) mã")
                .font(.caption.bold())
                .foregroundStyle(.secondary)
            HStack(spacing: 8) {
                miniStat("Hợp lệ", s.valid, Theme.success)
                miniStat("Trùng", s.dup, Theme.warning)
                miniStat("Không có", s.missing, Theme.danger)
                miniStat("Chờ PC", s.pending, Theme.accent)
            }
        }
        .padding(.horizontal, 14)
    }

    private func miniStat(_ title: String, _ value: Int, _ color: Color) -> some View {
        VStack(spacing: 3) {
            AnimatedCounter(
                value: value,
                font: .system(size: 22, weight: .bold, design: .rounded),
                color: value > 0 ? color : .primary
            )
            Text(title)
                .font(.caption2.bold())
                .foregroundStyle(.secondary)
                .lineLimit(1)
        }
        .frame(maxWidth: .infinity)
        .card(padding: 10, radius: 14)
    }

    private func row(_ it: ScanItem) -> some View {
        HStack(spacing: 12) {
            if let u = engine.photoURL(it), let img = UIImage(contentsOfFile: u.path) {
                Image(uiImage: img)
                    .resizable()
                    .scaledToFill()
                    .frame(width: 56, height: 56)
                    .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
            }
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 8) {
                    Text(it.code)
                        .font(.system(.subheadline, design: .monospaced).bold())
                        .lineLimit(1)
                    Spacer(minLength: 4)
                    StatusPill(text: scanLabel(it), color: scanColor(it))
                }
                if !it.detail.isEmpty {
                    Text(it.detail).font(.caption).foregroundStyle(.secondary).lineLimit(2)
                }
                if it.scanConfirmed && it.photoFile != nil && !it.photoConfirmed && !it.reviewRequired {
                    Text("Đang chờ: ảnh chưa gửi xong (đã thử \(it.photoAttempts) lần)")
                        .font(.caption2.bold()).foregroundColor(Theme.warning)
                } else if !it.scanConfirmed {
                    Text("Đang chờ: máy tính chưa xác nhận (đã gửi \(it.attempts) lần)")
                        .font(.caption2.bold()).foregroundColor(Theme.warning)
                }
                HStack {
                    Text(Date(timeIntervalSince1970: Double(it.time) / 1000).formatted(date: .abbreviated, time: .standard))
                        .font(.caption2).foregroundStyle(.secondary)
                    Spacer()
                    if it.kind == .newOrder && it.scanConfirmed && it.status == .success {
                        Text("Đã tạo ở Bảng chính").font(.caption2.bold()).foregroundColor(Theme.success)
                    }
                }
            }
        }
    }
}
