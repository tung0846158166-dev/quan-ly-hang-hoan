import SwiftUI
import UIKit
import ScannerCore

/// Một mục ảnh hiển thị trong trình xem toàn màn hình `ZoomablePhotoViewer`.
struct PhotoViewerItem: Identifiable, Equatable {
    let id: String
    let url: URL
    let createdAtMs: Int
    let byteSize: Int64
    let canDelete: Bool
}

/// Phiên mở trình xem ảnh toàn màn hình (dùng cho `.fullScreenCover(item:)`).
struct PhotoViewerSession: Identifiable {
    let id = UUID()
    let items: [PhotoViewerItem]
    let initialIndex: Int
}

/// `UIScrollView` tùy chỉnh hỗ trợ phóng to 2 ngón (1x–5x), chạm đúp để zoom tại đúng điểm chạm và kéo di chuyển mượt mà.
final class ZoomContainerScrollView: UIScrollView, UIScrollViewDelegate {
    let imageView = UIImageView()
    var onZoomStateChange: ((Bool) -> Void)?
    private var loadedURL: URL?
    private var lastBoundsSize: CGSize = .zero

    override init(frame: CGRect) {
        super.init(frame: frame)
        delegate = self
        minimumZoomScale = 1.0
        maximumZoomScale = 5.0
        bouncesZoom = true
        showsHorizontalScrollIndicator = false
        showsVerticalScrollIndicator = false
        decelerationRate = .fast
        backgroundColor = .clear

        imageView.contentMode = .scaleAspectFit
        imageView.isUserInteractionEnabled = true
        addSubview(imageView)

        let doubleTap = UITapGestureRecognizer(target: self, action: #selector(handleDoubleTap(_:)))
        doubleTap.numberOfTapsRequired = 2
        addGestureRecognizer(doubleTap)
    }

    required init?(coder: NSCoder) {
        return nil
    }

    func setImageURL(_ url: URL) {
        guard loadedURL != url else { return }
        loadedURL = url
        imageView.image = UIImage(contentsOfFile: url.path)
        setZoomScale(1.0, animated: false)
        layoutImageToFit()
        onZoomStateChange?(false)
    }

    override func layoutSubviews() {
        super.layoutSubviews()
        if bounds.size != lastBoundsSize && bounds.width > 0 && bounds.height > 0 {
            lastBoundsSize = bounds.size
            if zoomScale <= 1.01 {
                layoutImageToFit()
            } else {
                centerImageView()
            }
        }
    }

    private func layoutImageToFit() {
        guard bounds.width > 0, bounds.height > 0 else { return }
        imageView.frame = CGRect(origin: .zero, size: bounds.size)
        contentSize = bounds.size
        centerImageView()
    }

    private func centerImageView() {
        let offsetX = max((bounds.width - contentSize.width) * 0.5, 0)
        let offsetY = max((bounds.height - contentSize.height) * 0.5, 0)
        imageView.center = CGPoint(
            x: contentSize.width * 0.5 + offsetX,
            y: contentSize.height * 0.5 + offsetY
        )
    }

    func viewForZooming(in scrollView: UIScrollView) -> UIView? {
        imageView
    }

    func scrollViewDidZoom(_ scrollView: UIScrollView) {
        centerImageView()
        onZoomStateChange?(scrollView.zoomScale > 1.05)
    }

    @objc private func handleDoubleTap(_ recognizer: UITapGestureRecognizer) {
        if zoomScale > 1.05 {
            setZoomScale(1.0, animated: true)
        } else {
            let point = recognizer.location(in: imageView)
            let targetScale: CGFloat = 2.8
            let w = bounds.width / targetScale
            let h = bounds.height / targetScale
            let rect = CGRect(x: point.x - w * 0.5, y: point.y - h * 0.5, width: w, height: h)
            zoom(to: rect, animated: true)
        }
    }
}

struct ZoomableImageScrollView: UIViewRepresentable {
    let url: URL
    @Binding var isZoomed: Bool

    func makeCoordinator() -> Coordinator {
        Coordinator(isZoomed: $isZoomed)
    }

    func makeUIView(context: Context) -> ZoomContainerScrollView {
        let sv = ZoomContainerScrollView()
        let coord = context.coordinator
        sv.onZoomStateChange = { [weak coord] zoomed in
            DispatchQueue.main.async {
                coord?.updateZoomed(zoomed)
            }
        }
        sv.setImageURL(url)
        return sv
    }

    func updateUIView(_ uiView: ZoomContainerScrollView, context: Context) {
        context.coordinator.isZoomed = $isZoomed
        uiView.setImageURL(url)
    }

    final class Coordinator {
        var isZoomed: Binding<Bool>

        init(isZoomed: Binding<Bool>) {
            self.isZoomed = isZoomed
        }

        func updateZoomed(_ zoomed: Bool) {
            if isZoomed.wrappedValue != zoomed {
                isZoomed.wrappedValue = zoomed
            }
        }
    }
}

/// Trình xem ảnh toàn màn hình: phóng to 2 ngón + chạm đúp, kéo di chuyển, vuốt xuống đóng, có nút xóa có xác nhận.
struct ZoomablePhotoViewer: View {
    @Environment(\.dismiss) private var dismiss
    @State private var items: [PhotoViewerItem]
    @State private var currentIndex: Int
    @State private var isZoomed = false
    @State private var dragOffsetY: CGFloat = 0
    @State private var confirmDeleteItem: PhotoViewerItem?
    let onDelete: ((String) -> Void)?

    init(items: [PhotoViewerItem], initialIndex: Int = 0, onDelete: ((String) -> Void)? = nil) {
        let safeIdx = items.isEmpty ? 0 : max(0, min(initialIndex, items.count - 1))
        _items = State(initialValue: items)
        _currentIndex = State(initialValue: safeIdx)
        self.onDelete = onDelete
    }

    private var currentItem: PhotoViewerItem? {
        guard currentIndex >= 0, currentIndex < items.count else { return nil }
        return items[currentIndex]
    }

    private var backdropOpacity: Double {
        let progress = min(1.0, max(0.0, Double(dragOffsetY) / 260.0))
        return max(0.25, 1.0 - progress * 0.75)
    }

    var body: some View {
        ZStack {
            Color.black.opacity(backdropOpacity)
                .ignoresSafeArea()

            if items.isEmpty {
                VStack(spacing: 10) {
                    Image(systemName: "photo")
                        .font(.largeTitle)
                        .foregroundColor(.white.opacity(0.6))
                    Text("Không có ảnh để hiển thị")
                        .font(.subheadline)
                        .foregroundColor(.white.opacity(0.8))
                }
            } else {
                TabView(selection: $currentIndex) {
                    ForEach(Array(items.enumerated()), id: \.element.id) { idx, item in
                        ZoomableImageScrollView(
                            url: item.url,
                            isZoomed: $isZoomed
                        )
                        .tag(idx)
                        .ignoresSafeArea()
                    }
                }
                .tabViewStyle(.page(indexDisplayMode: .never))
                .offset(y: dragOffsetY)
                .simultaneousGesture(
                    DragGesture(minimumDistance: 18)
                        .onChanged { value in
                            guard !isZoomed else { return }
                            let dy = value.translation.height
                            let dx = abs(value.translation.width)
                            if dy > 0 && dy > dx * 1.2 {
                                dragOffsetY = dy
                            }
                        }
                        .onEnded { value in
                            guard !isZoomed else {
                                dragOffsetY = 0
                                return
                            }
                            if dragOffsetY > 110 || value.predictedEndTranslation.height > 180 {
                                dismiss()
                            } else {
                                withAnimation(.spring(response: 0.25, dampingFraction: 0.85)) {
                                    dragOffsetY = 0
                                }
                            }
                        }
                )
            }

            // Thanh điều khiển trên & thông tin ảnh dưới
            VStack {
                HStack(spacing: 12) {
                    Button {
                        Haptics.tap()
                        dismiss()
                    } label: {
                        Image(systemName: "xmark")
                            .font(.body.bold())
                            .foregroundColor(.white)
                            .frame(width: 40, height: 40)
                            .background(Color.black.opacity(0.55))
                            .clipShape(Circle())
                    }
                    .accessibilityLabel("Đóng xem ảnh")

                    Spacer()

                    if items.count > 1 {
                        Text("\(currentIndex + 1) / \(items.count)")
                            .font(.caption.bold())
                            .monospacedDigit()
                            .foregroundColor(.white)
                            .padding(.horizontal, 12)
                            .padding(.vertical, 6)
                            .background(Color.black.opacity(0.55))
                            .clipShape(Capsule())
                    }

                    Spacer()

                    if let cur = currentItem, cur.canDelete, onDelete != nil {
                        Button {
                            Haptics.medium()
                            confirmDeleteItem = cur
                        } label: {
                            Image(systemName: "trash")
                                .font(.body.bold())
                                .foregroundColor(.white)
                                .frame(width: 40, height: 40)
                                .background(Theme.danger.opacity(0.85))
                                .clipShape(Circle())
                        }
                        .accessibilityLabel("Xóa ảnh này")
                    } else {
                        Color.clear.frame(width: 40, height: 40)
                    }
                }
                .padding(.horizontal, 16)
                .padding(.top, 12)

                Spacer()

                if let cur = currentItem {
                    VStack(spacing: 4) {
                        HStack(spacing: 8) {
                            if cur.createdAtMs > 0 {
                                Text(OrderRow.formatDisplayDate(Date(timeIntervalSince1970: Double(cur.createdAtMs) / 1000), includeTime: true))
                                    .font(.caption.weight(.semibold))
                                    .foregroundColor(.white)
                            }
                            if cur.byteSize > 0 {
                                Text("· \(max(1, cur.byteSize / 1024)) KB")
                                    .font(.caption)
                                    .monospacedDigit()
                                    .foregroundColor(.white.opacity(0.8))
                            }
                        }
                        Text("Chụm hai ngón hoặc chạm đúp để phóng to · Vuốt xuống để đóng")
                            .font(.caption2)
                            .foregroundColor(.white.opacity(0.68))
                            .multilineTextAlignment(.center)
                    }
                    .padding(.horizontal, 14)
                    .padding(.vertical, 8)
                    .background(Color.black.opacity(0.55))
                    .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                    .padding(.horizontal, 16)
                    .padding(.bottom, 18)
                }
            }
            .opacity(dragOffsetY > 40 ? 0.3 : 1.0)
        }
        .onChange(of: currentIndex) { _ in
            isZoomed = false
            dragOffsetY = 0
        }
        .alert("Xóa ảnh này?", isPresented: Binding(
            get: { confirmDeleteItem != nil },
            set: { if !$0 { confirmDeleteItem = nil } }
        )) {
            Button("Hủy", role: .cancel) { confirmDeleteItem = nil }
            Button("Xóa ảnh", role: .destructive) {
                guard let target = confirmDeleteItem else { return }
                confirmDeleteItem = nil
                onDelete?(target.id)
                items.removeAll { $0.id == target.id }
                if items.isEmpty {
                    dismiss()
                } else {
                    currentIndex = max(0, min(currentIndex, items.count - 1))
                }
            }
        } message: {
            Text("Ảnh sẽ bị xóa khỏi điện thoại và không thể khôi phục.")
        }
    }
}

/// Chụp ảnh thủ công bằng camera hệ thống cho chi tiết kiện hàng.
struct CameraPhotoPicker: UIViewControllerRepresentable {
    let onImage: (UIImage) -> Void
    @Environment(\.dismiss) private var dismiss

    func makeCoordinator() -> Coordinator {
        Coordinator(onImage: onImage, dismiss: dismiss)
    }

    func makeUIViewController(context: Context) -> UIImagePickerController {
        let picker = UIImagePickerController()
        picker.sourceType = .camera
        picker.allowsEditing = false
        picker.delegate = context.coordinator
        return picker
    }

    func updateUIViewController(_ uiViewController: UIImagePickerController, context: Context) {}

    final class Coordinator: NSObject, UINavigationControllerDelegate, UIImagePickerControllerDelegate {
        let onImage: (UIImage) -> Void
        let dismiss: DismissAction

        init(onImage: @escaping (UIImage) -> Void, dismiss: DismissAction) {
            self.onImage = onImage
            self.dismiss = dismiss
        }

        func imagePickerController(_ picker: UIImagePickerController, didFinishPickingMediaWithInfo info: [UIImagePickerController.InfoKey: Any]) {
            if let img = info[.originalImage] as? UIImage {
                onImage(img)
            }
            dismiss()
        }

        func imagePickerControllerDidCancel(_ picker: UIImagePickerController) {
            dismiss()
        }
    }
}
