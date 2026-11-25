import SwiftUI
import UIKit
import ScannerCore

// T042: giao diện luồng cloud. Chữ/màu/khóa lấy từ ScannerCore (CloudStatusText, đã có XCTest).

extension CloudTone {
    var color: Color {
        switch self {
        case .success: return Theme.success
        case .info: return Theme.info
        case .warning: return Theme.warning
        case .danger: return Theme.danger
        case .neutral: return .gray
        }
    }
}

extension SyncEngine {
    /// Ghi được lên cloud (đã kết nối). Chỉ có nghĩa khi `cloudActive`.
    var cloudCanWrite: Bool { cloudState == .connected || cloudState == .syncing }
    /// Đang chạy cloud mà bị khóa ghi (mất mạng, đang nối, bị thu hồi).
    var cloudLocked: Bool { cloudActive && !cloudCanWrite }
}

/// Dải trạng thái nhỏ trên cùng mọi tab khi chạy cloud. "MẤT KẾT NỐI" nổi bật. Bấm để xem chi tiết.
struct CloudStatusBar: View {
    @EnvironmentObject var engine: SyncEngine
    @State private var showDetail = false

    var body: some View {
        let s = engine.cloudState
        let tone = CloudStatusText.tone(s)
        let lost = s == .disconnected || s == .revoked
        Button { showDetail = true } label: {
            HStack(spacing: 6) {
                Image(systemName: lost ? "wifi.exclamationmark" : (s == .syncing ? "arrow.triangle.2.circlepath" : "cloud.fill"))
                    .font(.caption.weight(.bold))
                Text(CloudStatusText.label(s))
                    .font(lost ? .subheadline.weight(.heavy) : .caption.weight(.semibold))
                if lost {
                    Text("· chỉ xem").font(.caption)
                }
                Spacer(minLength: 4)
                if let t = engine.cloud.lastSyncAt {
                    Text(t, style: .time).font(.caption2).opacity(0.85)
                }
                Image(systemName: "chevron.right").font(.caption2).opacity(0.7)
            }
            .foregroundColor(.white)
            .padding(.horizontal, 12)
            .padding(.vertical, lost ? 8 : 4)
            .frame(maxWidth: .infinity)
            .background(tone.color)
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Trạng thái cloud: \(CloudStatusText.label(s))")
        .sheet(isPresented: $showDetail) {
            NavigationStack { CloudDetailView() }
                .environmentObject(engine)
                .presentationDetents([.medium, .large])
        }
    }
}

/// Chi tiết kết nối: lần đồng bộ cuối, lỗi gần nhất, hạn mức (chủ), nút Đồng bộ ngay.
struct CloudDetailView: View {
    @EnvironmentObject var engine: SyncEngine
    @State private var syncing = false

    var body: some View {
        List {
            Section {
                HStack {
                    Circle().fill(CloudStatusText.tone(engine.cloudState).color).frame(width: 10, height: 10)
                    Text(CloudStatusText.label(engine.cloudState)).font(.headline)
                }
                if let why = CloudStatusText.lockReason(engine.cloudState) {
                    Text(why).font(.footnote).foregroundColor(.secondary)
                }
                if let t = engine.cloud.lastSyncAt {
                    LabeledContent("Đồng bộ lần cuối") { Text(t, style: .relative) + Text(" trước") }
                }
                if let e = engine.cloud.lastError, !e.isEmpty {
                    LabeledContent("Lỗi gần nhất") { Text(e).font(.footnote).foregroundColor(Theme.danger) }
                }
                Button {
                    syncing = true
                    Task { await engine.cloud.syncNow(); syncing = false }
                } label: {
                    HStack { Text("Đồng bộ ngay"); if syncing { Spacer(); ProgressView() } }
                }
                .disabled(syncing || !engine.cloudCanWrite)
            }
            if engine.cloud.role == "owner", let q = engine.cloud.quota {
                CloudQuotaSection(quota: q)
            }
        }
        .navigationTitle("Cloud")
        .navigationBarTitleDisplayMode(.inline)
    }
}

/// Hạn mức hôm nay (chỉ chủ cửa hàng).
struct CloudQuotaSection: View {
    let quota: CloudQuota
    var body: some View {
        let color: Color = quota.level == "stop" ? Theme.danger : (quota.level == "warn" ? Theme.warning : Theme.success)
        Section("Hạn mức hôm nay") {
            VStack(alignment: .leading, spacing: 4) {
                Text("Lượt ghi: \(Int(quota.rowsWrittenPercent.rounded()))%").font(.footnote)
                ProgressView(value: min(1, quota.rowsWrittenPercent / 100)).tint(color)
                Text("Yêu cầu: \(Int(quota.requestsPercent.rounded()))%").font(.footnote)
                ProgressView(value: min(1, quota.requestsPercent / 100)).tint(color)
            }
            if let note = CloudStatusText.quotaNote(quota) {
                Text(note).font(.footnote).foregroundColor(color)
            }
        }
    }
}

/// Lớp phủ trên camera khi bị khóa ghi: không quét được, nói rõ lý do.
struct CloudScanLockOverlay: View {
    @EnvironmentObject var engine: SyncEngine
    var body: some View {
        ZStack {
            Color.black.opacity(0.72)
            VStack(spacing: 8) {
                Image(systemName: "wifi.exclamationmark").font(.largeTitle).foregroundColor(Theme.danger)
                Text(engine.cloudState == .connecting ? "Đang kết nối cloud..." : "Mất kết nối cloud — không quét được")
                    .font(.headline).foregroundColor(.white).multilineTextAlignment(.center)
                if let why = CloudStatusText.lockReason(engine.cloudState) {
                    Text(why).font(.footnote).foregroundColor(.white.opacity(0.85)).multilineTextAlignment(.center)
                }
            }
            .padding()
        }
    }
}

/// Banner "chỉ xem" đầu các tab xem dữ liệu khi bị khóa.
struct CloudReadOnlyBanner: View {
    @EnvironmentObject var engine: SyncEngine
    var body: some View {
        if engine.cloudLocked {
            HStack(spacing: 6) {
                Image(systemName: "lock.fill").font(.caption)
                if let t = engine.cloud.lastSyncAt {
                    Text("Dữ liệu cũ lúc \(t.formatted(date: .omitted, time: .shortened)), chỉ xem").font(.caption.weight(.semibold))
                } else {
                    Text("Chưa có dữ liệu cloud, chỉ xem").font(.caption.weight(.semibold))
                }
                Spacer()
            }
            .foregroundColor(Theme.danger)
            .padding(.horizontal, 12).padding(.vertical, 6)
            .background(Theme.danger.opacity(0.12))
        }
    }
}

/// Cài đặt > Cloud: ghép thiết bị (mã 6 số hoặc mã QR `qlhh://pair?u=...&c=...`), thông tin khi đã ghép, quên ghép.
struct CloudSettingsView: View {
    @EnvironmentObject var engine: SyncEngine
    @AppStorage("cloudBaseURL") private var baseURL = "https://qlhh-cloud.qlhh-cloud-worker.workers.dev"
    @AppStorage("cloudDeviceName") private var deviceName = UIDevice.current.name
    @State private var code = ""
    @State private var pairLink = ""
    @State private var busy = false
    @State private var error: String?
    @State private var confirmForget = false

    var body: some View {
        List {
            if engine.cloudActive {
                Section("Thiết bị này") {
                    LabeledContent("Trạng thái") {
                        Text(CloudStatusText.label(engine.cloudState)).foregroundColor(CloudStatusText.tone(engine.cloudState).color)
                    }
                    LabeledContent("Tên") { Text(engine.cloud.deviceName ?? "") }
                    LabeledContent("Vai trò") { Text(engine.cloud.role == "owner" ? "Chủ cửa hàng" : "Nhân viên") }
                    LabeledContent("Cloud") { Text(engine.cloud.baseURL ?? "").font(.caption).lineLimit(1).truncationMode(.middle) }
                    NavigationLink("Chi tiết kết nối") { CloudDetailView() }
                }
                Section {
                    Button("Quên ghép trên máy này", role: .destructive) { confirmForget = true }
                } footer: {
                    Text("Chỉ xóa phiên trên điện thoại này. Muốn chặn hẳn thiết bị, chủ cửa hàng thu hồi trên máy tính.")
                }
            } else {
                Section {
                    TextField("Địa chỉ cloud", text: $baseURL)
                        .textInputAutocapitalization(.never).autocorrectionDisabled().keyboardType(.URL)
                    TextField("Mã ghép 6 số", text: $code)
                        .keyboardType(.numberPad)
                        .font(.system(.title3, design: .monospaced))
                    TextField("Tên thiết bị (vd: iPhone kho 1)", text: $deviceName)
                } header: {
                    Text("Ghép với cloud")
                } footer: {
                    Text("Chủ cửa hàng tạo mã ghép trên máy tính. Mã dùng một lần, hết hạn sau 10 phút. Sau khi ghép, điện thoại lấy dữ liệu từ cloud và KHÔNG dùng mã phòng MQTT nữa.")
                }
                Section {
                    TextField("Hoặc dán nội dung mã QR (qlhh://pair?...)", text: $pairLink)
                        .textInputAutocapitalization(.never).autocorrectionDisabled()
                        .onChange(of: pairLink) { v in
                            if let p = CloudPairLink.parse(v) { baseURL = p.url; code = p.code }
                        }
                }
                Section {
                    Button {
                        Task { await pair() }
                    } label: {
                        HStack { Text("Ghép thiết bị").bold(); if busy { Spacer(); ProgressView() } }
                    }
                    .disabled(busy || code.count != 6)
                    if let error {
                        Text(error).font(.footnote).foregroundColor(Theme.danger)
                    }
                }
            }
        }
        .navigationTitle("Cloud")
        .navigationBarTitleDisplayMode(.inline)
        .confirmationDialog("Quên ghép cloud trên máy này?", isPresented: $confirmForget, titleVisibility: .visible) {
            Button("Quên ghép", role: .destructive) { engine.unpairCloud() }
            Button("Huỷ", role: .cancel) {}
        } message: {
            Text("Điện thoại sẽ không còn dữ liệu cloud cho tới khi ghép lại.")
        }
    }

    private func pair() async {
        busy = true; error = nil
        let err = await engine.pairCloud(baseURL: baseURL.trimmingCharacters(in: .whitespacesAndNewlines),
                                         code: code.trimmingCharacters(in: .whitespacesAndNewlines), deviceName: deviceName)
        busy = false
        if let err { error = err } else { code = ""; pairLink = "" }
    }
}

/// Chi tiết đơn (chế độ cloud): ảnh của kiện trên cloud, kể cả ảnh máy khác chụp. Chạm để xem to.
struct CloudPhotosCard: View {
    @EnvironmentObject var engine: SyncEngine
    let parcelId: String
    @State private var photos: [CloudPhotoInfo] = []
    @State private var thumbs: [String: URL] = [:]
    @State private var loading = false
    @State private var error: String?
    @State private var viewer: PhotoViewerSession?

    private static var cacheDir: URL {
        (FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask).first ?? FileManager.default.temporaryDirectory)
            .appendingPathComponent("cloud-photos", isDirectory: true)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            SectionTitleRow(title: "Ảnh trên cloud", icon: "icloud.and.arrow.down", subtitle: photos.isEmpty ? nil : "\(photos.count) ảnh")
            if loading && photos.isEmpty {
                ProgressView().frame(maxWidth: .infinity)
            } else if let error {
                Text(error).font(.footnote).foregroundColor(.secondary)
            } else if photos.isEmpty {
                Text("Chưa có ảnh trên cloud cho kiện này.").font(.footnote).foregroundColor(.secondary)
            } else {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 8) {
                        ForEach(Array(photos.enumerated()), id: \.element.photoId) { idx, p in
                            Button { Task { await open(at: idx) } } label: {
                                ZStack(alignment: .bottomLeading) {
                                    if let u = thumbs[p.photoId], let img = UIImage(contentsOfFile: u.path) {
                                        Image(uiImage: img).resizable().scaledToFill()
                                    } else {
                                        Color.gray.opacity(0.15)
                                        Image(systemName: p.expired ? "clock.badge.xmark" : "photo").foregroundColor(.secondary)
                                    }
                                    if !p.deviceName.isEmpty {
                                        Text(p.deviceName).font(.caption2).foregroundColor(.white)
                                            .padding(.horizontal, 4).background(Color.black.opacity(0.5))
                                    }
                                }
                                .frame(width: 84, height: 84)
                                .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
                            }
                            .buttonStyle(.plain)
                            .disabled(p.expired)
                        }
                    }
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(padding: Theme.cardPadding, radius: Theme.cardRadius)
        .task(id: parcelId) { await load() }
        .fullScreenCover(item: $viewer) { s in
            ZoomablePhotoViewer(items: s.items, initialIndex: s.initialIndex)
        }
    }

    private func load() async {
        guard let api = engine.cloud.currentAPI else { error = "Chưa kết nối cloud: chưa tải được ảnh."; return }
        loading = true
        defer { loading = false }
        do {
            photos = try await api.parcelPhotos(parcelId: parcelId)
            error = nil
            CloudPhotoFiles.prune(dir: Self.cacheDir, olderThanDays: 90)
            for p in photos where !p.expired {
                if let u = try? await CloudPhotoFiles.fetch(api: api, dir: Self.cacheDir, photoId: p.photoId, thumb: true) { thumbs[p.photoId] = u }
            }
        } catch let e as CloudAPIError {
            error = e.isNetwork ? "Mất kết nối cloud: chưa tải được ảnh." : e.message
        } catch {
            self.error = "Không tải được ảnh."
        }
    }

    private func open(at index: Int) async {
        guard let api = engine.cloud.currentAPI else { return }
        var items: [PhotoViewerItem] = []
        for p in photos where !p.expired {
            if let u = try? await CloudPhotoFiles.fetch(api: api, dir: Self.cacheDir, photoId: p.photoId, thumb: false) {
                let size = (try? FileManager.default.attributesOfItem(atPath: u.path)[.size] as? Int64) ?? 0
                items.append(PhotoViewerItem(id: p.photoId, url: u, createdAtMs: p.takenAt, byteSize: size, canDelete: false))
            }
        }
        guard !items.isEmpty else { error = "Không tải được ảnh."; return }
        let wanted = photos.indices.contains(index) ? photos[index].photoId : ""
        viewer = PhotoViewerSession(items: items, initialIndex: items.firstIndex(where: { $0.id == wanted }) ?? 0)
    }
}
