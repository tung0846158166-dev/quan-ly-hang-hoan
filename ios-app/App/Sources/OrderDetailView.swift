import SwiftUI
import UIKit
import PhotosUI
import ScannerCore

/// Màn Chi tiết đơn hàng thiết kế lại (T033):
/// Hero mã vận đơn + trạng thái/cảnh báo, hàng nút hành động nhanh, thẻ Sản phẩm (tô màu hoàn một phần),
/// Hành trình hàng hoàn 4 chặng, Mã vận đơn & liên quan, Timeline Thời gian, Ghi chú.
struct DetailView: View {
    let row: OrderRow
    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme
    @EnvironmentObject var engine: SyncEngine
    @EnvironmentObject var store: SnapshotStore
    @EnvironmentObject var photoManager: OrderPhotoManager

    @State private var copiedCode: String?
    @State private var showNoteSheet = false
    @State private var showStatusSheet = false
    @State private var noteDraft = ""
    @State private var watchDraft = false
    @State private var askDelete = false
    @State private var confirmDelete = false
    @State private var viewerSession: PhotoViewerSession?
    @State private var showCameraPicker = false
    @State private var selectedPickerItem: PhotosPickerItem?
    @State private var photoPendingDelete: OrderPhotoRecord?
    @State private var photoAlertMessage: String?
    @State private var isSavingPhoto = false

    private static let statuses = ["Chưa nhận", "Hàng đã trả", "Không cần nhận", "Bị mất"]

    private var live: OrderRow { store.row(byId: row.id) ?? row }

    private var nowMs: Int { Int(Date().timeIntervalSince1970 * 1000) }

    private var statusChoices: [String] {
        Self.statuses.contains(live.st) || live.st.isEmpty ? Self.statuses : Self.statuses + [live.st]
    }

    private var sourceName: String {
        if live.isArchived { return "Kho lưu trữ" }
        return live.k == "c" ? "Bảng đơn hủy" : "Bảng chính"
    }

    private var navTitle: String {
        live.ty.isEmpty ? "Chi tiết đơn" : live.ty
    }

    private var alertLevel: DeadlineLevel {
        live.pd ? live.alertLevel(now: nowMs) : .unknown
    }

    private var heroTint: Color {
        if live.rv { return Theme.success }
        switch alertLevel {
        case .overdue: return Theme.danger
        case .soon: return Theme.warning
        case .normal: return Theme.accent
        case .unknown: return live.isCancelled ? Theme.danger : Theme.info
        }
    }

    private var statusDisplay: (text: String, color: Color) {
        if live.rv { return ("Đã nhận kho", Theme.success) }
        if live.isCancelled { return ("Đơn hủy", Theme.danger) }
        let st = live.st.trimmingCharacters(in: .whitespacesAndNewlines)
        if st.isEmpty { return (live.pd ? "Chưa nhận" : "Đã xử lý", live.pd ? Theme.info : .secondary) }
        if st == "Bị mất" || st == "Hư hỏng" { return (st, Theme.danger) }
        if st == "Không cần nhận" { return (st, .secondary) }
        if st == "Hàng đã trả" || st == "Đã nhận" { return ("Đã nhận kho", Theme.success) }
        return (st, live.pd ? Theme.info : .secondary)
    }

    private var alertDisplay: (text: String, color: Color, icon: String)? {
        guard live.pd else { return nil }
        let txt = Deadline.text(due: live.due, now: nowMs)
        switch alertLevel {
        case .overdue: return (txt, Theme.danger, "exclamationmark.triangle.fill")
        case .soon: return (txt, Theme.warning, "clock.badge.exclamationmark.fill")
        case .normal: return (live.due > 0 ? txt : "Trong hạn", Theme.success, "checkmark.circle.fill")
        case .unknown: return live.due > 0 ? (txt, .secondary, "clock") : nil
        }
    }

    private var primaryCode: String {
        live.c.isEmpty ? live.o : live.c
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: Theme.sectionSpacing) {
                    if store.isDemoMode {
                        demoNoticeBar
                    }

                    if let fb = engine.actionFeedback(for: live) {
                        syncFeedbackBar(fb)
                    }

                    heroCard
                    quickActionsBar
                    productsCard
                    photosCard
                    if engine.cloudActive {
                        CloudPhotosCard(parcelId: live.id)   // T042: ảnh trên cloud (cả ảnh máy khác chụp)
                    }

                    if live.inReturnWatch {
                        returnJourneyCard
                    }

                    relatedCodesCard
                    timelineCard
                    notesCard
                }
                .padding(.horizontal, Theme.screenPadding)
                .padding(.top, 8)
                .padding(.bottom, 20)
            }
            .background(Theme.pageBackground.ignoresSafeArea())
            .navigationTitle(navTitle)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .navigationBarTrailing) {
                    Button("Đóng") {
                        Haptics.tap()
                        dismiss()
                    }
                    .font(.subheadline.bold())
                }
            }
            .sheet(isPresented: $showNoteSheet) {
                noteEditorSheet
            }
            .sheet(isPresented: $showStatusSheet) {
                statusPickerSheet
            }
            .sheet(isPresented: $showCameraPicker) {
                CameraPhotoPicker { img in
                    Task {
                        await handleAddedCameraImage(img)
                    }
                }
            }
            .fullScreenCover(item: $viewerSession) { session in
                ZoomablePhotoViewer(
                    items: session.items,
                    initialIndex: session.initialIndex
                ) { deletedId in
                    _ = photoManager.deletePhoto(id: deletedId)
                }
            }
            .onChange(of: selectedPickerItem) { newItem in
                guard let pickerItem = newItem else { return }
                selectedPickerItem = nil
                Task {
                    await handleSelectedLibraryItem(pickerItem)
                }
            }
            .alert("Xóa ảnh này?", isPresented: Binding(
                get: { photoPendingDelete != nil },
                set: { if !$0 { photoPendingDelete = nil } }
            )) {
                Button("Hủy", role: .cancel) { photoPendingDelete = nil }
                Button("Xóa ảnh", role: .destructive) {
                    if let target = photoPendingDelete {
                        Haptics.medium()
                        _ = photoManager.deletePhoto(id: target.id)
                    }
                    photoPendingDelete = nil
                }
            } message: {
                Text("Ảnh sẽ bị xóa khỏi điện thoại và không thể khôi phục.")
            }
            .alert("Thông báo ảnh", isPresented: Binding(
                get: { photoAlertMessage != nil },
                set: { if !$0 { photoAlertMessage = nil } }
            )) {
                Button("Đã hiểu", role: .cancel) { photoAlertMessage = nil }
            } message: {
                Text(photoAlertMessage ?? "")
            }
            .confirmationDialog("Xóa đơn này?", isPresented: $askDelete, titleVisibility: .visible) {
                Button("Tiếp tục xóa", role: .destructive) { confirmDelete = true }
            } message: {
                Text(store.isDemoMode ? "Chỉ xóa trên dữ liệu mẫu." : "Đơn vẫn khôi phục được trên máy tính.")
            }
            .alert("Chắc chắn xóa đơn \(live.o.isEmpty ? live.c : live.o)?", isPresented: $confirmDelete) {
                Button("Hủy", role: .cancel) {}
                Button("Xóa", role: .destructive) {
                    Haptics.medium()
                    engine.sendOrderDelete(live)
                    dismiss()
                }
            } message: {
                Text(store.isDemoMode
                     ? "Tắt rồi bật lại dữ liệu mẫu sẽ khôi phục đơn này."
                     : "Đơn sẽ bị gỡ khỏi bảng chính; có thể khôi phục trên máy tính.")
            }
        }
    }

    // MARK: - Dải thông báo chế độ mẫu & đồng bộ PC
    private var demoNoticeBar: some View {
        HStack(spacing: 8) {
            Image(systemName: "sparkles")
                .font(.caption.bold())
                .foregroundColor(Theme.accent)
            Text("Dữ liệu mẫu: thay đổi chỉ áp dụng cục bộ, không gửi lên máy tính.")
                .font(.caption.weight(.medium))
                .foregroundColor(.primary)
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 9)
        .background(Theme.accent.opacity(0.12))
        .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .stroke(Theme.accent.opacity(0.28), lineWidth: 0.7)
        )
    }

    private func syncFeedbackBar(_ fb: OrderActionFeedback) -> some View {
        let color: Color = fb.state == .confirmed ? Theme.success : (fb.state == .failed ? Theme.danger : Theme.warning)
        return HStack(spacing: 10) {
            if fb.state == .pending {
                ProgressView()
                    .scaleEffect(0.85)
                    .tint(color)
            } else {
                Image(systemName: fb.state == .confirmed ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                    .font(.subheadline.bold())
                    .foregroundColor(color)
            }
            Text(fb.message)
                .font(.caption.bold())
                .foregroundColor(fb.state == .failed ? Theme.danger : .primary)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 10)
        .background(color.opacity(0.13))
        .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .stroke(color.opacity(0.32), lineWidth: 0.8)
        )
    }

    // MARK: - 1. Hero trên cùng
    private var heroCard: some View {
        let st = statusDisplay
        let hasOrderId = !live.o.isEmpty && live.o.uppercased() != primaryCode.uppercased()
        let isCopied = copiedCode == primaryCode

        return VStack(alignment: .leading, spacing: 14) {
            // Huy hiệu sàn + nguồn bảng + đặt trước
            HStack(spacing: 8) {
                heroPlatformBadge(live.p)
                StatusPill(text: sourceName, color: .secondary)
                if live.pre {
                    StatusPill(text: "Đặt trước", color: Theme.purple)
                }
                if live.wl {
                    Image(systemName: "star.fill")
                        .font(.caption)
                        .foregroundColor(Theme.warning)
                }
                Spacer(minLength: 0)
            }

            // Mã vận đơn RẤT to + nút Sao chép tròn bên cạnh
            HStack(alignment: .center, spacing: 12) {
                VStack(alignment: .leading, spacing: 4) {
                    Text(primaryCode.isEmpty ? "Chưa có mã" : primaryCode)
                        .font(.system(.title2, design: .monospaced).bold())
                        .foregroundColor(.primary)
                        .minimumScaleFactor(0.72)
                        .lineLimit(2)
                        .textSelection(.enabled)

                    if hasOrderId {
                        Text("Mã đơn: \(live.o)")
                            .font(.system(.footnote, design: .monospaced).weight(.medium))
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                            .minimumScaleFactor(0.8)
                            .textSelection(.enabled)
                    }
                }

                Spacer(minLength: 4)

                if !primaryCode.isEmpty {
                    Button {
                        copyText(primaryCode)
                    } label: {
                        Image(systemName: isCopied ? "checkmark" : "doc.on.doc")
                            .font(.body.weight(.semibold))
                            .foregroundColor(isCopied ? .white : Theme.accent)
                            .frame(width: 44, height: 44)
                            .background(isCopied ? Theme.success : Theme.accent.opacity(0.16))
                            .clipShape(Circle())
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel("Sao chép mã vận đơn")
                }
            }

            // Viên thuốc trạng thái lớn + viên thuốc cảnh báo (tự xếp dọc ở Dynamic Type lớn)
            ViewThatFits(in: .horizontal) {
                HStack(spacing: 8) {
                    statusPillAction(st: st)
                    if let alert = alertDisplay { alertPillView(alert) }
                    Spacer(minLength: 0)
                }
                VStack(alignment: .leading, spacing: 6) {
                    statusPillAction(st: st)
                    if let alert = alertDisplay { alertPillView(alert) }
                }
            }
        }
        .padding(Theme.cardPadding)
        .background {
            RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous)
                .fill(Theme.cardBackground)
                .overlay(
                    RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous)
                        .fill(
                            LinearGradient(
                                colors: [
                                    heroTint.opacity(scheme == .dark ? 0.22 : 0.13),
                                    heroTint.opacity(scheme == .dark ? 0.04 : 0.02)
                                ],
                                startPoint: .topLeading,
                                endPoint: .bottomTrailing
                            )
                        )
                )
        }
        .clipShape(RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous)
                .stroke(heroTint.opacity(scheme == .dark ? 0.32 : 0.20), lineWidth: 0.8)
        )
    }

    private func heroPlatformBadge(_ raw: String) -> some View {
        let name = DashboardMetrics.normalizedPlatform(raw)
        let bg: Color
        let fg: Color
        switch name {
        case "Shopee":
            bg = Theme.accent
            fg = .white
        case "TikTok":
            bg = scheme == .dark ? Color.white.opacity(0.18) : Color.black
            fg = .white
        case "Lazada":
            bg = Theme.info
            fg = .white
        default:
            bg = Color.secondary.opacity(0.22)
            fg = .primary
        }
        return Text(name)
            .font(.caption.bold())
            .padding(.horizontal, 10)
            .padding(.vertical, 4)
            .background(bg)
            .foregroundColor(fg)
            .clipShape(Capsule())
    }

    @ViewBuilder
    private func statusPillAction(st: (text: String, color: Color)) -> some View {
        if live.k == "m" {
            Button {
                Haptics.tap()
                showStatusSheet = true
            } label: {
                largeStatusPill(text: st.text, color: st.color, showChevron: true)
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Đổi trạng thái đơn: \(st.text)")
        } else {
            largeStatusPill(text: st.text, color: st.color, showChevron: false)
        }
    }

    private func alertPillView(_ alert: (text: String, color: Color, icon: String)) -> some View {
        HStack(spacing: 5) {
            Image(systemName: alert.icon)
                .font(.caption.bold())
            Text(alert.text)
                .font(.caption.bold())
                .lineLimit(1)
                .minimumScaleFactor(0.8)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 7)
        .background(alert.color.opacity(0.16))
        .foregroundColor(alert.color)
        .clipShape(Capsule())
    }

    private func largeStatusPill(text: String, color: Color, showChevron: Bool) -> some View {
        HStack(spacing: 5) {
            Circle().fill(color).frame(width: 7, height: 7)
            Text(text)
                .font(.caption.bold())
                .lineLimit(1)
            if showChevron {
                Image(systemName: "chevron.down")
                    .font(.caption2.bold())
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 7)
        .background(color.opacity(0.16))
        .foregroundColor(color)
        .clipShape(Capsule())
    }

    // MARK: - 2. Hàng nút hành động nhanh (biểu tượng tròn có nhãn, >= 44pt)
    private var quickActionsBar: some View {
        let canEditMain = live.k == "m"
        return HStack(spacing: 8) {
            quickActionButton(
                icon: live.rv ? "checkmark.circle.fill" : "shippingbox.and.arrow.backward.fill",
                label: live.rv ? "Trạng thái" : "Đã nhận",
                color: live.rv ? Theme.success : Theme.accent,
                enabled: canEditMain
            ) {
                Haptics.medium()
                if live.rv {
                    showStatusSheet = true
                } else {
                    engine.sendOrderEdit(live, fields: ["st": "Hàng đã trả"])
                }
            }

            quickActionButton(
                icon: "note.text",
                label: "Ghi chú",
                color: Theme.info,
                enabled: canEditMain
            ) {
                Haptics.tap()
                noteDraft = live.wr
                watchDraft = live.wl
                showNoteSheet = true
            }

            quickActionButton(
                icon: live.wl ? "star.fill" : "star",
                label: "Theo dõi",
                color: live.wl ? Theme.warning : .secondary,
                enabled: canEditMain
            ) {
                Haptics.medium()
                engine.sendOrderNote(live, note: live.wr, watch: !live.wl)
            }

            quickActionButton(
                icon: copiedCode == primaryCode ? "checkmark" : "doc.on.doc",
                label: copiedCode == primaryCode ? "Đã chép" : "Sao chép",
                color: copiedCode == primaryCode ? Theme.success : Theme.teal,
                enabled: !primaryCode.isEmpty
            ) {
                copyText(primaryCode)
            }

            if canEditMain {
                quickActionButton(
                    icon: "trash",
                    label: "Xóa",
                    color: Theme.danger,
                    enabled: true
                ) {
                    Haptics.medium()
                    askDelete = true
                }
            }
        }
        .card(padding: 10, radius: Theme.cardRadius)
    }

    private func quickActionButton(
        icon: String,
        label: String,
        color: Color,
        enabled: Bool,
        action: @escaping () -> Void
    ) -> some View {
        Button(action: action) {
            VStack(spacing: 6) {
                Image(systemName: icon)
                    .font(.system(size: 18, weight: .semibold))
                    .foregroundColor(enabled ? color : .secondary.opacity(0.4))
                    .frame(width: 44, height: 44)
                    .background((enabled ? color : Color.secondary).opacity(enabled ? 0.14 : 0.08))
                    .clipShape(Circle())
                Text(label)
                    .font(.caption2.weight(.semibold))
                    .foregroundColor(enabled ? .primary : .secondary)
                    .lineLimit(1)
                    .minimumScaleFactor(0.8)
            }
            .frame(maxWidth: .infinity, minHeight: 62)
        }
        .buttonStyle(.plain)
        .disabled(!enabled)
    }

    // MARK: - 3. Thẻ "Sản phẩm"
    private var productsCard: some View {
        let lines = ProductMetrics.productLines(for: live)
        let isPartial = !live.rn.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        let returnedCount = lines.filter { $0.isReturned }.count
        let qty = max(1, live.q)
        let hasUnalignedMeta = lines.count > 1 && (
            (!live.co.isEmpty && ProductMetrics.splitPipe(live.co).count != lines.count) ||
            (!live.sz.isEmpty && ProductMetrics.splitPipe(live.sz).count != lines.count)
        )

        return VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 8) {
                SectionTitleRow(
                    title: "Sản phẩm",
                    icon: "tshirt.fill",
                    iconColor: Theme.accent
                )
                StatusPill(text: lines.count <= 1 ? "×\(qty)" : "Tổng \(qty) cái", color: Theme.accent)
            }

            if isPartial && !lines.isEmpty {
                HStack(spacing: 6) {
                    Image(systemName: "square.split.2x1.fill")
                        .font(.caption.bold())
                    Text("Hoàn một phần: \(returnedCount)/\(lines.count) sản phẩm")
                        .font(.caption.bold())
                }
                .foregroundColor(Theme.accent)
                .padding(.horizontal, 10)
                .padding(.vertical, 6)
                .background(Theme.accent.opacity(0.12))
                .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
            }

            if lines.isEmpty {
                Text("Chưa có thông tin sản phẩm")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            } else {
                VStack(spacing: 8) {
                    ForEach(lines) { line in
                        productLineRow(line, showSingleQty: lines.count == 1 ? qty : nil)
                    }
                }
            }

            if hasUnalignedMeta {
                HStack(spacing: 8) {
                    if !live.co.isEmpty {
                        chipView(text: "Màu: \(live.co)", color: Theme.info)
                    }
                    if !live.sz.isEmpty {
                        chipView(text: "Size: \(live.sz)", color: Theme.purple)
                    }
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(padding: Theme.cardPadding, radius: Theme.cardRadius)
    }

    private func productLineRow(_ line: OrderProductLine, showSingleQty: Int?) -> some View {
        let highlighted = line.isPartialContext && line.isReturned
        let muted = line.isPartialContext && !line.isReturned

        return VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .top, spacing: 8) {
                Text(line.name)
                    .font(highlighted ? .subheadline.bold() : .subheadline.weight(.medium))
                    .foregroundColor(highlighted ? Theme.accent : (muted ? .secondary : .primary))
                    .fixedSize(horizontal: false, vertical: true)

                Spacer(minLength: 4)

                if line.isPartialContext {
                    StatusPill(
                        text: line.isReturned ? "Hoàn" : "Không hoàn",
                        color: line.isReturned ? Theme.accent : .secondary
                    )
                } else if let q = showSingleQty {
                    Text("×\(q)")
                        .font(.system(.subheadline, design: .rounded).bold())
                        .foregroundColor(Theme.accent)
                        .monospacedDigit()
                }
            }

            if !line.color.isEmpty || !line.size.isEmpty || (line.isPartialContext && showSingleQty != nil) {
                HStack(spacing: 6) {
                    if !line.color.isEmpty {
                        chipView(text: line.color, color: muted ? .secondary : Theme.info)
                    }
                    if !line.size.isEmpty {
                        chipView(text: "Size \(line.size)", color: muted ? .secondary : Theme.purple)
                    }
                    if line.isPartialContext, let q = showSingleQty {
                        chipView(text: "×\(q)", color: Theme.accent)
                    }
                }
            }
        }
        .padding(11)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(highlighted ? Theme.accent.opacity(0.10) : Theme.subtleFill)
        .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .stroke(highlighted ? Theme.accent.opacity(0.32) : Color.clear, lineWidth: 0.8)
        )
    }

    private func chipView(text: String, color: Color) -> some View {
        Text(text)
            .font(.caption2.bold())
            .padding(.horizontal, 8)
            .padding(.vertical, 3)
            .background(color.opacity(0.14))
            .foregroundColor(color)
            .clipShape(Capsule())
    }

    // MARK: - 3b. Thẻ "Ảnh" cục bộ trên iPhone (T039: tối đa 5 ảnh/kiện, zoom 2 ngón/chạm đúp, thêm/xóa có xác nhận)
    private var photosCard: some View {
        let _ = photoManager.revision
        let parcelPhotos = photoManager.photos(for: live, snapshot: store)
        let maxCount = OrderPhotoDiskStore.maxPhotosPerParcel
        let isFull = parcelPhotos.count >= maxCount

        return VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 8) {
                SectionTitleRow(
                    title: "Ảnh",
                    icon: "photo.on.rectangle.angled",
                    iconColor: Theme.teal
                )
                StatusPill(
                    text: "\(parcelPhotos.count)/\(maxCount)",
                    color: isFull ? Theme.warning : Theme.teal
                )
                if isSavingPhoto {
                    ProgressView()
                        .scaleEffect(0.8)
                }
            }

            if store.isDemoMode {
                Text("Chế độ dữ liệu mẫu: ảnh thật được lưu riêng trên máy khi tắt chế độ mẫu.")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            } else {
                if parcelPhotos.isEmpty {
                    Text("Chưa có ảnh cho kiện này. Ảnh chụp lúc quét (khi cloud xác nhận khớp đúng kiện) hoặc thêm thủ công sẽ được lưu.")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                } else {
                    ScrollView(.horizontal, showsIndicators: false) {
                        HStack(spacing: 10) {
                            ForEach(Array(parcelPhotos.enumerated()), id: \.element.id) { idx, rec in
                                ZStack(alignment: .topTrailing) {
                                    Button {
                                        Haptics.tap()
                                        openPhotoViewer(records: parcelPhotos, initialIndex: idx)
                                    } label: {
                                        Group {
                                            if let img = photoManager.thumbnail(for: rec) {
                                                Image(uiImage: img)
                                                    .resizable()
                                                    .scaledToFill()
                                            } else {
                                                ZStack {
                                                    Theme.subtleFill
                                                    Image(systemName: "photo")
                                                        .foregroundStyle(.secondary)
                                                }
                                            }
                                        }
                                        .frame(width: 90, height: 90)
                                        .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                                        .overlay(
                                            RoundedRectangle(cornerRadius: 12, style: .continuous)
                                                .stroke(Color.primary.opacity(0.10), lineWidth: 0.8)
                                        )
                                    }
                                    .buttonStyle(.plain)
                                    .accessibilityLabel("Xem ảnh \(idx + 1) của kiện")

                                    Button {
                                        Haptics.medium()
                                        photoPendingDelete = rec
                                    } label: {
                                        Image(systemName: "xmark.circle.fill")
                                            .font(.system(size: 20))
                                            .symbolRenderingMode(.palette)
                                            .foregroundStyle(.white, Color.black.opacity(0.65))
                                            .padding(4)
                                    }
                                    .buttonStyle(.plain)
                                    .accessibilityLabel("Xóa ảnh \(idx + 1)")
                                }
                            }
                        }
                        .padding(.vertical, 2)
                    }
                }

                if isFull {
                    HStack(spacing: 6) {
                        Image(systemName: "exclamationmark.circle.fill")
                            .font(.caption)
                            .foregroundColor(Theme.warning)
                        Text("Đã đủ tối đa \(maxCount) ảnh cho kiện này. Xóa bớt ảnh cũ nếu muốn thêm mới.")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    .padding(.horizontal, 10)
                    .padding(.vertical, 7)
                    .background(Theme.warning.opacity(0.12))
                    .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
                }

                HStack(spacing: 8) {
                    Button {
                        Haptics.tap()
                        if isFull {
                            photoAlertMessage = "Kiện này đã đủ tối đa \(maxCount) ảnh. Hãy xóa bớt ảnh cũ trước khi chụp thêm (app không tự xóa ảnh cũ)."
                        } else if UIImagePickerController.isSourceTypeAvailable(.camera) {
                            showCameraPicker = true
                        } else {
                            photoAlertMessage = "Thiết bị này không có camera khả dụng."
                        }
                    } label: {
                        Label("Chụp ảnh", systemImage: "camera.fill")
                            .font(Theme.Typography.captionBold)
                            .frame(maxWidth: .infinity, minHeight: 40)
                    }
                    .buttonStyle(.bordered)
                    .tint(isFull ? .secondary : Theme.teal)
                    .disabled(isSavingPhoto)

                    if isFull {
                        Button {
                            Haptics.tap()
                            photoAlertMessage = "Kiện này đã đủ tối đa \(maxCount) ảnh. Hãy xóa bớt ảnh cũ trước khi chọn thêm (app không tự xóa ảnh cũ)."
                        } label: {
                            Label("Chọn ảnh", systemImage: "photo.on.rectangle")
                                .font(Theme.Typography.captionBold)
                                .frame(maxWidth: .infinity, minHeight: 40)
                        }
                        .buttonStyle(.bordered)
                        .tint(.secondary)
                    } else {
                        PhotosPicker(selection: $selectedPickerItem, matching: .images) {
                            Label("Chọn ảnh", systemImage: "photo.on.rectangle")
                                .font(Theme.Typography.captionBold)
                                .frame(maxWidth: .infinity, minHeight: 40)
                        }
                        .buttonStyle(.bordered)
                        .tint(Theme.accent)
                        .disabled(isSavingPhoto)
                    }
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(padding: Theme.cardPadding, radius: Theme.cardRadius)
    }

    private func openPhotoViewer(records: [OrderPhotoRecord], initialIndex: Int) {
        let items = records.map { rec in
            PhotoViewerItem(
                id: rec.id,
                url: photoManager.photoURL(for: rec),
                createdAtMs: rec.createdAtMs,
                byteSize: rec.byteSize,
                canDelete: true
            )
        }
        viewerSession = PhotoViewerSession(items: items, initialIndex: initialIndex)
    }

    private func handleAddedCameraImage(_ image: UIImage) async {
        isSavingPhoto = true
        let res = await photoManager.addManualPhoto(image: image, for: live, source: .manualCamera, snapshot: store)
        isSavingPhoto = false
        handlePhotoSaveResult(res)
    }

    private func handleSelectedLibraryItem(_ pickerItem: PhotosPickerItem) async {
        isSavingPhoto = true
        defer { isSavingPhoto = false }
        guard let data = try? await pickerItem.loadTransferable(type: Data.self), !data.isEmpty else {
            photoAlertMessage = "Không đọc được ảnh từ thư viện."
            return
        }
        let res = await photoManager.addManualPhotoData(data, for: live, source: .manualLibrary, snapshot: store)
        handlePhotoSaveResult(res)
    }

    private func handlePhotoSaveResult(_ res: Result<OrderPhotoRecord, OrderPhotoError>) {
        switch res {
        case .success:
            Haptics.medium()
        case .failure(.limitReached(let maxCount)):
            photoAlertMessage = "Kiện này đã đủ tối đa \(maxCount) ảnh. Hãy xóa bớt ảnh cũ nếu muốn thêm mới."
        case .failure(.emptyData):
            photoAlertMessage = "Dữ liệu ảnh trống, không thể lưu."
        case .failure(.ioFailure(let msg)):
            photoAlertMessage = msg
        }
    }

    // MARK: - 4. Thẻ "Hành trình hàng hoàn" (4 chặng)
    private func stageColor(for stage: Int) -> Color {
        switch stage {
        case 1: return Theme.info
        case 2: return Theme.teal
        case 3: return Theme.warning
        default: return Theme.success
        }
    }

    private func stageTitle(for stage: Int) -> String {
        switch stage {
        case 1: return "Đã đồng ý"
        case 2: return "Đang về"
        case 3: return "Đã về shop"
        default: return "Đã nhận kho"
        }
    }

    private var returnJourneyCard: some View {
        let stage = live.returnWatchStage
        let currentColor = stageColor(for: stage)

        return VStack(alignment: .leading, spacing: 14) {
            HStack {
                SectionTitleRow(
                    title: "Hành trình hàng hoàn",
                    icon: "arrow.uturn.backward",
                    iconColor: currentColor
                )
                StatusPill(
                    text: live.returnWatchStageName,
                    color: currentColor
                )
            }

            HStack(spacing: 0) {
                ForEach(1...4, id: \.self) { idx in
                    let reached = idx <= stage
                    let isCurrent = idx == stage
                    let c = stageColor(for: idx)

                    VStack(spacing: 6) {
                        ZStack {
                            Circle()
                                .fill(reached ? c : Color.gray.opacity(0.22))
                                .frame(width: isCurrent ? 26 : 20, height: isCurrent ? 26 : 20)
                            if idx < stage || (idx == 4 && stage == 4) {
                                Image(systemName: "checkmark")
                                    .font(.system(size: 10, weight: .bold))
                                    .foregroundColor(.white)
                            } else if isCurrent {
                                Circle()
                                    .fill(Color.white)
                                    .frame(width: 8, height: 8)
                            }
                        }
                        .frame(height: 28)

                        Text(stageTitle(for: idx))
                            .font(.caption2.weight(isCurrent ? .bold : .medium))
                            .foregroundColor(isCurrent ? c : (reached ? .primary : .secondary))
                            .multilineTextAlignment(.center)
                            .lineLimit(2)
                            .minimumScaleFactor(0.75)
                    }
                    .frame(maxWidth: .infinity)

                    if idx < 4 {
                        Rectangle()
                            .fill(idx < stage ? stageColor(for: idx + 1) : Color.gray.opacity(0.22))
                            .frame(height: 2.5)
                            .padding(.bottom, 22)
                    }
                }
            }

            if live.ar > 0 || receivedTimeText != nil {
                VStack(alignment: .leading, spacing: 4) {
                    if live.ar > 0 {
                        HStack(spacing: 6) {
                            Image(systemName: "mappin.and.ellipse")
                                .font(.caption2)
                                .foregroundColor(Theme.warning)
                            Text("Về shop lúc \(formatEpochMs(live.ar))")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                    }
                    if let receivedText = receivedTimeText {
                        HStack(spacing: 6) {
                            Image(systemName: "checkmark.seal.fill")
                                .font(.caption2)
                                .foregroundColor(Theme.success)
                            Text("Nhận kho lúc \(receivedText)")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                    }
                }
                .padding(.top, 2)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(padding: Theme.cardPadding, radius: Theme.cardRadius)
    }

    // MARK: - 5. Thẻ "Mã vận đơn & liên quan"
    private var relatedCodesCard: some View {
        let entries = buildCodeEntries()
        return Group {
            if !entries.isEmpty || !live.ps.isEmpty {
                VStack(alignment: .leading, spacing: 10) {
                    SectionTitleRow(
                        title: "Mã vận đơn & liên quan",
                        icon: "barcode",
                        iconColor: Theme.info
                    )

                    VStack(spacing: 8) {
                        ForEach(entries, id: \.id) { entry in
                            let isCopied = copiedCode == entry.value
                            HStack(spacing: 10) {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(entry.label)
                                        .font(.caption2.weight(.semibold))
                                        .foregroundStyle(.secondary)
                                    Text(entry.value)
                                        .font(.system(.subheadline, design: .monospaced).weight(.semibold))
                                        .foregroundColor(.primary)
                                        .lineLimit(2)
                                        .textSelection(.enabled)
                                }
                                Spacer(minLength: 6)
                                Button {
                                    copyText(entry.value)
                                } label: {
                                    Image(systemName: isCopied ? "checkmark" : "doc.on.doc")
                                        .font(.caption.bold())
                                        .foregroundColor(isCopied ? .white : Theme.accent)
                                        .frame(width: 34, height: 34)
                                        .background(isCopied ? Theme.success : Theme.accent.opacity(0.14))
                                        .clipShape(Circle())
                                }
                                .buttonStyle(.plain)
                                .accessibilityLabel("Sao chép \(entry.label)")
                            }
                            .padding(.horizontal, 12)
                            .padding(.vertical, 9)
                            .background(Theme.subtleFill)
                            .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                        }

                        if !live.ps.isEmpty {
                            HStack(spacing: 10) {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text("Trạng thái sàn")
                                        .font(.caption2.weight(.semibold))
                                        .foregroundStyle(.secondary)
                                    Text(live.ps)
                                        .font(.subheadline.weight(.medium))
                                        .foregroundColor(.primary)
                                }
                                Spacer(minLength: 0)
                            }
                            .padding(.horizontal, 12)
                            .padding(.vertical, 9)
                            .background(Theme.subtleFill)
                            .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                        }
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .card(padding: Theme.cardPadding, radius: Theme.cardRadius)
            }
        }
    }

    private struct CodeEntry: Identifiable {
        let id: String
        let label: String
        let value: String
    }

    private func buildCodeEntries() -> [CodeEntry] {
        var result: [CodeEntry] = []
        var seen = Set<String>()

        func add(_ label: String, _ raw: String) {
            let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !trimmed.isEmpty else { return }
            let key = trimmed.uppercased()
            guard seen.insert(key).inserted else { return }
            result.append(CodeEntry(id: "\(label)|\(key)", label: label, value: trimmed))
        }

        add("Mã vận đơn", live.c)
        add("Mã đơn hàng", live.o)

        let extras = live.x.split(whereSeparator: { $0.isWhitespace }).map(String.init)
        for (idx, code) in extras.enumerated() {
            let label = extras.count == 1 ? "Mã liên quan" : "Mã liên quan #\(idx + 1)"
            add(label, code)
        }
        return result
    }

    // MARK: - 6. Thẻ "Thời gian" (Timeline dọc có chấm)
    private struct TimelineEntry: Identifiable {
        let id: String
        let title: String
        let value: String
        let detail: String?
        let color: Color
        let isMuted: Bool
    }

    private var resolvedReceivedDisplay: ReceivedTimeDisplay {
        let localScanMs = engine.localReturnScanTimeMs(for: live)
        return live.resolvedReceivedTime(localScanTimeMs: localScanMs)
    }

    private var receivedTimeText: String? {
        if case .received(let text, _) = resolvedReceivedDisplay {
            return text
        }
        return nil
    }

    private var timelineEntries: [TimelineEntry] {
        var list: [TimelineEntry] = []
        if !live.rd.isEmpty {
            list.append(TimelineEntry(
                id: "rd",
                title: live.isCancelled ? "Ngày hủy đơn" : "Ngày hoàn / hủy",
                value: formatRawDate(live.rd),
                detail: nil,
                color: Theme.info,
                isMuted: false
            ))
        }
        if live.rw > 0 {
            list.append(TimelineEntry(
                id: "rw",
                title: "Bắt đầu theo dõi hoàn về",
                value: formatEpochMs(live.rw),
                detail: nil,
                color: Theme.teal,
                isMuted: false
            ))
        }
        if live.ar > 0 {
            list.append(TimelineEntry(
                id: "ar",
                title: "Hàng về shop",
                value: formatEpochMs(live.ar),
                detail: nil,
                color: Theme.warning,
                isMuted: false
            ))
        }
        // Luôn hiển thị dòng "Đã nhận tại kho" trong mục Thời gian (chưa nhận hiện "Chưa nhận" màu phụ).
        switch resolvedReceivedDisplay {
        case .received(let text, let fromLocalScan):
            list.append(TimelineEntry(
                id: "rcd",
                title: "Đã nhận tại kho",
                value: text,
                detail: fromLocalScan ? "Quét trên máy này" : nil,
                color: Theme.success,
                isMuted: false
            ))
        case .receivedUnknownTime:
            list.append(TimelineEntry(
                id: "rcd",
                title: "Đã nhận tại kho",
                value: "Đã nhận (chưa rõ giờ)",
                detail: nil,
                color: Theme.success.opacity(0.65),
                isMuted: true
            ))
        case .notReceived:
            list.append(TimelineEntry(
                id: "rcd",
                title: "Đã nhận tại kho",
                value: "Chưa nhận",
                detail: nil,
                color: Color.secondary.opacity(0.45),
                isMuted: true
            ))
        }
        if live.due > 0 {
            let c: Color = alertLevel == .overdue ? Theme.danger : (alertLevel == .soon ? Theme.warning : Theme.accent)
            list.append(TimelineEntry(
                id: "due",
                title: "Hạn xử lý",
                value: formatEpochMs(live.due),
                detail: live.pd ? Deadline.text(due: live.due, now: nowMs) : nil,
                color: c,
                isMuted: false
            ))
        }
        return list
    }

    private var timelineCard: some View {
        let entries = timelineEntries
        return Group {
            if !entries.isEmpty {
                VStack(alignment: .leading, spacing: 12) {
                    SectionTitleRow(
                        title: "Thời gian",
                        icon: "clock.fill",
                        iconColor: Theme.teal
                    )

                    VStack(alignment: .leading, spacing: 0) {
                        ForEach(Array(entries.enumerated()), id: \.element.id) { idx, item in
                            HStack(alignment: .top, spacing: 12) {
                                VStack(spacing: 0) {
                                    Circle()
                                        .fill(item.color)
                                        .frame(width: 10, height: 10)
                                        .padding(.top, 4)
                                    if idx < entries.count - 1 {
                                        Rectangle()
                                            .fill(Color.primary.opacity(0.14))
                                            .frame(width: 1.5, height: 32)
                                            .padding(.vertical, 3)
                                    }
                                }
                                .frame(width: 14)

                                VStack(alignment: .leading, spacing: 2) {
                                    Text(item.title)
                                        .font(.caption.weight(.semibold))
                                        .foregroundStyle(.secondary)
                                    ViewThatFits(in: .horizontal) {
                                        HStack(spacing: 8) {
                                            Text(item.value)
                                                .font(item.isMuted ? Theme.Typography.primary : Theme.Typography.primaryBold)
                                                .foregroundColor(item.isMuted ? .secondary : .primary)
                                            if let detail = item.detail {
                                                StatusPill(text: detail, color: item.color)
                                            }
                                        }
                                        VStack(alignment: .leading, spacing: 4) {
                                            Text(item.value)
                                                .font(item.isMuted ? Theme.Typography.primary : Theme.Typography.primaryBold)
                                                .foregroundColor(item.isMuted ? .secondary : .primary)
                                            if let detail = item.detail {
                                                StatusPill(text: detail, color: item.color)
                                            }
                                        }
                                    }
                                }
                                .padding(.bottom, idx < entries.count - 1 ? 12 : 0)

                                Spacer(minLength: 0)
                            }
                        }
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .card(padding: Theme.cardPadding, radius: Theme.cardRadius)
            }
        }
    }

    // MARK: - 7. Thẻ "Ghi chú"
    private var notesCard: some View {
        let hasShopNote = !live.wr.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        let hasOrderNote = !live.nt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty

        return VStack(alignment: .leading, spacing: 12) {
            HStack {
                SectionTitleRow(
                    title: "Ghi chú",
                    icon: "note.text",
                    iconColor: Theme.warning
                )
                if live.k == "m" {
                    Button {
                        Haptics.tap()
                        noteDraft = live.wr
                        watchDraft = live.wl
                        showNoteSheet = true
                    } label: {
                        Text(hasShopNote ? "Sửa" : "Thêm")
                            .font(.caption.bold())
                            .padding(.horizontal, 12)
                            .padding(.vertical, 6)
                            .background(Theme.accent.opacity(0.15))
                            .foregroundColor(Theme.accent)
                            .clipShape(Capsule())
                    }
                    .buttonStyle(.plain)
                }
            }

            if hasShopNote || live.wl {
                VStack(alignment: .leading, spacing: 4) {
                    HStack(spacing: 6) {
                        Text("Ghi chú shop / theo dõi")
                            .font(.caption2.bold())
                            .foregroundStyle(.secondary)
                        if live.wl {
                            StatusPill(text: "Đang theo dõi", color: Theme.warning)
                        }
                    }
                    Text(hasShopNote ? live.wr : "Đã gắn sao theo dõi (chưa nhập lý do)")
                        .font(.subheadline)
                        .foregroundColor(hasShopNote ? .primary : .secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .padding(12)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Theme.subtleFill)
                .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
            }

            if hasOrderNote {
                VStack(alignment: .leading, spacing: 4) {
                    Text("Ghi chú khách / đơn")
                        .font(.caption2.bold())
                        .foregroundStyle(.secondary)
                    Text(live.nt)
                        .font(.subheadline)
                        .foregroundColor(.primary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .padding(12)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Theme.subtleFill)
                .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
            }

            if !hasShopNote && !hasOrderNote && !live.wl {
                Text("Chưa có ghi chú. Chạm \"Thêm\" để ghi lý do theo dõi hoặc lưu ý cho kho.")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }

            if live.k != "m" {
                Text("Chỉ đơn ở bảng chính mới sửa được theo dõi/ghi chú.")
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(padding: Theme.cardPadding, radius: Theme.cardRadius)
    }

    // MARK: - Sheet sửa Ghi chú & Theo dõi
    private var noteEditorSheet: some View {
        NavigationStack {
            VStack(alignment: .leading, spacing: 16) {
                Toggle(isOn: $watchDraft) {
                    HStack(spacing: 10) {
                        IconTile(systemName: "star.fill", color: Theme.warning, size: 30, cornerRadius: 8)
                        VStack(alignment: .leading, spacing: 1) {
                            Text("Đang theo dõi")
                                .font(.subheadline.bold())
                            Text("Ghim đơn lên danh sách Theo dõi")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                    }
                }
                .tint(Theme.accent)
                .card(padding: Theme.cardPadding, radius: Theme.cardRadius)

                VStack(alignment: .leading, spacing: 8) {
                    Text("Ghi chú / lý do theo dõi")
                        .font(.caption.bold())
                        .foregroundStyle(.secondary)
                    TextField("Nhập ghi chú cho kho...", text: $noteDraft, axis: .vertical)
                        .lineLimit(3...6)
                        .padding(12)
                        .background(Theme.subtleFill)
                        .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                }
                .card(padding: Theme.cardPadding, radius: Theme.cardRadius)

                Button {
                    Haptics.medium()
                    let trimmed = noteDraft.trimmingCharacters(in: .whitespacesAndNewlines)
                    let effectiveWatch = watchDraft || !trimmed.isEmpty
                    engine.sendOrderNote(live, note: trimmed, watch: effectiveWatch)
                    showNoteSheet = false
                } label: {
                    Text(store.isDemoMode ? "Lưu cục bộ (dữ liệu mẫu)" : "Lưu lên cloud")
                        .font(.subheadline.bold())
                        .frame(maxWidth: .infinity, minHeight: 48)
                }
                .buttonStyle(.borderedProminent)
                .tint(Theme.accent)

                Spacer()
            }
            .padding(16)
            .background(Theme.pageBackground.ignoresSafeArea())
            .navigationTitle("Ghi chú đơn")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .navigationBarLeading) {
                    Button("Hủy") { showNoteSheet = false }
                }
            }
        }
        .presentationDetents([.medium, .large])
    }

    // MARK: - Sheet đổi Trạng thái đơn
    private var statusPickerSheet: some View {
        NavigationStack {
            List {
                Section {
                    ForEach(statusChoices, id: \.self) { choice in
                        let isCurrent = choice == live.st || (live.rv && choice == "Hàng đã trả")
                        Button {
                            Haptics.medium()
                            engine.sendOrderEdit(live, fields: ["st": choice])
                            showStatusSheet = false
                        } label: {
                            HStack {
                                Text(choice)
                                    .font(.body.weight(isCurrent ? .bold : .regular))
                                    .foregroundColor(.primary)
                                Spacer()
                                if isCurrent {
                                    Image(systemName: "checkmark.circle.fill")
                                        .foregroundColor(Theme.accent)
                                }
                            }
                            .frame(minHeight: 44)
                        }
                    }
                } header: {
                    Text(store.isDemoMode ? "Chọn trạng thái (dữ liệu mẫu)" : "Chọn trạng thái (đồng bộ sang máy tính)")
                }
            }
            .listStyle(.insetGrouped)
            .navigationTitle("Đổi trạng thái")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .navigationBarTrailing) {
                    Button("Đóng") { showStatusSheet = false }
                }
            }
        }
        .presentationDetents([.medium, .large])
    }

    // MARK: - Helpers
    private func copyText(_ text: String) {
        Haptics.tap()
        UIPasteboard.general.string = text
        copiedCode = text
        Task {
            try? await Task.sleep(nanoseconds: 1_500_000_000)
            if copiedCode == text { copiedCode = nil }
        }
    }

    private func formatEpochMs(_ ms: Int) -> String {
        let d = Date(timeIntervalSince1970: Double(ms) / 1000)
        return OrderRow.formatDisplayDate(d, includeTime: true)
    }

    private func formatRawDate(_ raw: String) -> String {
        let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let parsed = OrderStats.parseDate(trimmed) else { return trimmed }
        return OrderRow.formatDisplayDate(parsed, includeTime: trimmed.contains(":"))
    }
}
