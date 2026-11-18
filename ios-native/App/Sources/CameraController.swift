import AVFoundation
import UIKit
import SwiftUI

/// Camera + bộ quét mã gốc của iPhone (AVCaptureMetadataOutput).
final class CameraController: NSObject, ObservableObject, AVCaptureMetadataOutputObjectsDelegate, AVCaptureVideoDataOutputSampleBufferDelegate {
    let session = AVCaptureSession()
    var onCode: ((String) -> Void)?
    @Published var torchOn = false
    @Published var running = false
    @Published var denied = false
    /// Camera tự nghỉ (lâu không quét được mã, hoặc máy quá nóng); chạm để quét tiếp.
    @Published var restReason: String?

    // ---- Chống nóng máy ----
    // Bình thường 24 hình/giây (đủ nhanh để nhận mã); 30 giây không thấy mã nào thì 15 hình/giây;
    // 3 phút không thấy mã thì tạm dừng camera. Máy nóng (thermalState .serious) thì 12 hình/giây, quá nóng (.critical) thì dừng.
    private let activeFPS: Int32 = 24, idleFPS: Int32 = 15, hotFPS: Int32 = 12
    private var currentFPS: Int32 = 0
    private var lastCodeAt = Date()
    private var idleTimer: Timer?
    private var thermalObserver: NSObjectProtocol?

    override init() {
        super.init()
        thermalObserver = NotificationCenter.default.addObserver(forName: ProcessInfo.thermalStateDidChangeNotification, object: nil, queue: .main) { [weak self] _ in
            self?.applyThermalAndIdle()
        }
    }
    deinit {
        if let o = thermalObserver { NotificationCenter.default.removeObserver(o) }
        idleTimer?.invalidate()
    }

    private func setFPS(_ fps: Int32) {
        queue.async {
            guard let dev = self.device, fps != self.currentFPS else { return }
            let ok = dev.activeFormat.videoSupportedFrameRateRanges.contains { Double(fps) >= $0.minFrameRate && Double(fps) <= $0.maxFrameRate }
            guard ok, (try? dev.lockForConfiguration()) != nil else { return }
            dev.activeVideoMinFrameDuration = CMTime(value: 1, timescale: fps)
            dev.activeVideoMaxFrameDuration = CMTime(value: 1, timescale: fps)
            dev.unlockForConfiguration()
            self.currentFPS = fps
        }
    }

    /// Gọi định kỳ và khi nhiệt độ máy đổi: chọn số hình/giây hoặc cho camera nghỉ.
    private func applyThermalAndIdle() {
        guard running else { return }
        let thermal = ProcessInfo.processInfo.thermalState
        if thermal == .critical {
            restReason = "Máy đang quá nóng nên camera tạm nghỉ. Để máy nguội một chút rồi chạm để quét tiếp."
            stop(); return
        }
        let idle = Date().timeIntervalSince(lastCodeAt)
        let restAfter = UserDefaults.standard.object(forKey: "cameraRestAfterSec") as? Double ?? 180
        if restAfter > 0, idle > restAfter {
            restReason = "Camera tạm nghỉ vì \(Int(restAfter / 60)) phút không quét mã (đỡ nóng máy, đỡ tốn pin). Chạm để quét tiếp."
            stop(); return
        }
        if thermal == .serious { setFPS(hotFPS) }
        else if idle > 30 { setFPS(idleFPS) }
        else { setFPS(activeFPS) }
    }

    private let queue = DispatchQueue(label: "camera.session")
    private let frameLock = NSLock()
    private var latestBuffer: CVPixelBuffer?
    private var configured = false
    private var device: AVCaptureDevice?
    private var metaOutput: AVCaptureMetadataOutput?
    private let ciContext = CIContext()
    private var lastRegion = CGRect.zero
    private let metaQueue = DispatchQueue(label: "camera.meta", qos: .userInteractive)

    func start() {
        restReason = nil
        lastCodeAt = Date()
        idleTimer?.invalidate()
        idleTimer = Timer.scheduledTimer(withTimeInterval: 10, repeats: true) { [weak self] _ in self?.applyThermalAndIdle() }
        switch AVCaptureDevice.authorizationStatus(for: .video) {
        case .authorized: begin()
        case .notDetermined:
            AVCaptureDevice.requestAccess(for: .video) { ok in
                DispatchQueue.main.async { if ok { self.begin() } else { self.denied = true } }
            }
        default:
            denied = true
        }
    }

    private func begin() {
        queue.async {
            if !self.configured { self.configure() }
            if !self.session.isRunning { self.session.startRunning() }
            DispatchQueue.main.async {
                self.running = self.session.isRunning
                self.currentFPS = 0
                self.applyThermalAndIdle()
            }
        }
    }

    func stop() {
        idleTimer?.invalidate(); idleTimer = nil
        queue.async {
            if self.session.isRunning { self.session.stopRunning() }
            DispatchQueue.main.async { self.running = false; self.torchOn = false }
        }
    }

    private func configure() {
        session.beginConfiguration()
        if session.canSetSessionPreset(.hd1920x1080) { session.sessionPreset = .hd1920x1080 }
        guard let dev = AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: .back),
              let input = try? AVCaptureDeviceInput(device: dev), session.canAddInput(input) else {
            session.commitConfiguration(); return
        }
        session.addInput(input)
        device = dev

        let meta = AVCaptureMetadataOutput()
        if session.canAddOutput(meta) {
            session.addOutput(meta)
            metaOutput = meta
            meta.setMetadataObjectsDelegate(self, queue: metaQueue)   // không dùng luồng giao diện: giao diện bận cũng không làm trễ việc dò mã
            let wanted: [AVMetadataObject.ObjectType] = [.code128, .code39, .code93, .ean13, .ean8, .upce, .qr, .itf14, .interleaved2of5, .dataMatrix, .pdf417]
            meta.metadataObjectTypes = wanted.filter { meta.availableMetadataObjectTypes.contains($0) }
        }

        let video = AVCaptureVideoDataOutput()
        video.alwaysDiscardsLateVideoFrames = true
        video.setSampleBufferDelegate(self, queue: DispatchQueue(label: "camera.frames"))
        if session.canAddOutput(video) {
            session.addOutput(video)
            if let c = video.connection(with: .video), c.isVideoOrientationSupported { c.videoOrientation = .portrait }
        }
        session.commitConfiguration()

        do {
            try dev.lockForConfiguration()
            if dev.isFocusModeSupported(.continuousAutoFocus) { dev.focusMode = .continuousAutoFocus }
            if dev.isAutoFocusRangeRestrictionSupported { dev.autoFocusRangeRestriction = .near }
            if dev.isExposureModeSupported(.continuousAutoExposure) { dev.exposureMode = .continuousAutoExposure }
            dev.unlockForConfiguration()
        } catch {}
        configured = true
    }

    /// Chỉ nhận mã nằm trong khung đang nhìn thấy (khung nhỏ hơn toàn màn hình). Nếu tính ra giá trị lạ thì giữ nguyên toàn khung hình.
    func updateScanRegion(_ layer: AVCaptureVideoPreviewLayer) {
        guard let meta = metaOutput, layer.bounds.width > 20, layer.bounds.height > 20 else { return }
        let r = layer.metadataOutputRectConverted(fromLayerRect: layer.bounds)
        guard r.width > 0.1, r.height > 0.1, r.minX >= -0.01, r.minY >= -0.01, r.maxX <= 1.01, r.maxY <= 1.01 else { return }
        // Đặt lại vùng quét làm bộ dò mã khởi động lại và có thể mất vài giây mới nhận được mã: chỉ đặt khi vùng thật sự đổi
        // (bố cục chạy lại mỗi lần đổi chế độ quét, trước đây cũng đặt lại vùng mỗi lần).
        let l = lastRegion
        if abs(l.minX - r.minX) < 0.004, abs(l.minY - r.minY) < 0.004, abs(l.width - r.width) < 0.004, abs(l.height - r.height) < 0.004 { return }
        lastRegion = r
        queue.async { meta.rectOfInterest = r }
    }

    func setTorch(_ on: Bool) {
        guard let dev = device, dev.hasTorch else { return }
        do {
            try dev.lockForConfiguration()
            dev.torchMode = on ? .on : .off
            dev.unlockForConfiguration()
            torchOn = on
        } catch {}
    }

    func setZoom(_ factor: CGFloat) {
        guard let dev = device else { return }
        do {
            try dev.lockForConfiguration()
            dev.videoZoomFactor = max(1, min(factor, dev.activeFormat.videoMaxZoomFactor, 6))
            dev.unlockForConfiguration()
        } catch {}
    }

    // MARK: delegates
    /// Loại mã không có số kiểm tra / sửa lỗi, rất hay bị đọc nhầm từ chữ và hoa văn: phải thấy cùng một giá trị nhiều lần liên tiếp mới nhận.
    private let weakTypes: Set<AVMetadataObject.ObjectType> = [.code39, .interleaved2of5, .itf14]
    private var weakCandidate: (code: String, hits: Int, last: Date)?

    func metadataOutput(_ output: AVCaptureMetadataOutput, didOutput metadataObjects: [AVMetadataObject], from connection: AVCaptureConnection) {
        for obj in metadataObjects {
            guard let m = obj as? AVMetadataMachineReadableCodeObject, let s = m.stringValue, !s.isEmpty else { continue }
            // Giới hạn khoảng cách (gần đúng): mã ở xa thì chiếm phần nhỏ của khung hình, bỏ qua mã nhỏ hơn ngưỡng người dùng đặt.
            let minSize = UserDefaults.standard.object(forKey: "minCodeSize") as? Double ?? 0.12
            if minSize > 0, Double(max(m.bounds.width, m.bounds.height)) < minSize { continue }
            if weakTypes.contains(m.type) {
                let now = Date()
                if let c = weakCandidate, c.code == s, now.timeIntervalSince(c.last) < 1.0 {
                    weakCandidate = (s, c.hits + 1, now)
                } else {
                    weakCandidate = (s, 1, now)
                }
                guard (weakCandidate?.hits ?? 0) >= 4 else { continue }
                weakCandidate = nil
            }
            DispatchQueue.main.async { [weak self] in
                guard let self = self else { return }
                let wasIdle = Date().timeIntervalSince(self.lastCodeAt) > 30
                self.lastCodeAt = Date()
                if wasIdle && ProcessInfo.processInfo.thermalState != .serious { self.setFPS(self.activeFPS) }
                self.onCode?(s)
            }
            return
        }
    }

    func captureOutput(_ output: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer, from connection: AVCaptureConnection) {
        guard let px = CMSampleBufferGetImageBuffer(sampleBuffer) else { return }
        frameLock.lock(); latestBuffer = px; frameLock.unlock()
    }

    /// Ảnh khung hình hiện tại, thu nhỏ để gửi nhanh qua 4G.
    func snapshotJPEG(maxDim: CGFloat = 560, quality: CGFloat = 0.55) -> Data? {
        frameLock.lock(); let px = latestBuffer; frameLock.unlock()
        guard let px = px else { return nil }
        let ci = CIImage(cvPixelBuffer: px)
        guard let cg = ciContext.createCGImage(ci, from: ci.extent) else { return nil }
        let w = CGFloat(cg.width), h = CGFloat(cg.height)
        let scale = min(1, maxDim / max(w, h))
        let size = CGSize(width: (w * scale).rounded(), height: (h * scale).rounded())
        let fmt = UIGraphicsImageRendererFormat.default(); fmt.scale = 1
        let img = UIGraphicsImageRenderer(size: size, format: fmt).image { _ in
            UIImage(cgImage: cg).draw(in: CGRect(origin: .zero, size: size))
        }
        return img.jpegData(compressionQuality: quality)
    }
}

final class PreviewUIView: UIView {
    override class var layerClass: AnyClass { AVCaptureVideoPreviewLayer.self }
    var previewLayer: AVCaptureVideoPreviewLayer { layer as! AVCaptureVideoPreviewLayer }
    var onLayout: ((AVCaptureVideoPreviewLayer) -> Void)?
    override func layoutSubviews() {
        super.layoutSubviews()
        onLayout?(previewLayer)
        // Phiên camera có thể chạy sau lần bố cục đầu; tính lại một lần nữa.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) { [weak self] in
            if let self = self { self.onLayout?(self.previewLayer) }
        }
    }
}

struct CameraPreview: UIViewRepresentable {
    let controller: CameraController
    func makeUIView(context: Context) -> PreviewUIView {
        let v = PreviewUIView()
        v.previewLayer.session = controller.session
        v.previewLayer.videoGravity = .resizeAspectFill
        v.onLayout = { [weak controller] layer in controller?.updateScanRegion(layer) }
        return v
    }
    func updateUIView(_ uiView: PreviewUIView, context: Context) {}
}
