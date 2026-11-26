import AVFoundation
import UIKit
import SwiftUI
import ScannerCore

/// Camera + bộ quét mã gốc của iPhone (AVCaptureMetadataOutput).
/// Mọi thao tác cấu hình phần cứng camera (`device.lockForConfiguration`, `session`, `metaOutput`) đều chạy tuần tự trên `queue`.
final class CameraController: NSObject, ObservableObject, AVCaptureMetadataOutputObjectsDelegate, AVCaptureVideoDataOutputSampleBufferDelegate {
    let session = AVCaptureSession()
    var onCode: ((String) -> Void)?
    /// Nếu đặt, nhận TẤT CẢ mã đang thấy trong một lần quét (ưu tiên hơn onCode).
    var onCodes: (([String]) -> Void)?
    @Published var torchOn = false
    @Published var running = false
    @Published var denied = false
    /// Camera tự nghỉ (lâu không quét được mã, máy quá nóng, hoặc có cuộc gọi); chạm để quét tiếp.
    @Published var restReason: String?
    /// Khung hình "đứng" ngay lúc nhận mã (phủ lên khung xem trước ~0,2 giây rồi camera chạy tiếp). nil = đang chạy bình thường.
    @Published var frozenFrame: UIImage?
    /// Thời gian đứng (giây), đổi được qua UserDefaults "scanFreezeSec"; 0 = tắt. CHỈ dùng trên main thread.
    private var freezeGate = ScanFreezeGate(duration: UserDefaults.standard.object(forKey: "scanFreezeSec") as? Double ?? ScanFreezeGate.defaultDuration)
    private var unfreezeWork: DispatchWorkItem?

    // ---- Chống nóng máy ----
    // Bình thường 24 hình/giây (đủ nhanh để nhận mã); 15 giây không thấy mã nào thì 15 hình/giây;
    // 30 giây không thấy mã thì tạm dừng camera. Máy nóng (thermalState .serious) thì 12 hình/giây, quá nóng (.critical) thì dừng.
    private let activeFPS: Int32 = 24, idleFPS: Int32 = 15, hotFPS: Int32 = 12
    private var currentFPS: Int32 = 0           // CHỈ đọc/ghi trên `queue`
    private var desiredZoom: CGFloat = 1        // CHỈ đọc/ghi trên `queue`
    private var lastCodeAt = Date()             // CHỈ đọc/ghi trên main thread
    private var wantsRunning = false            // CHỈ đọc/ghi trên main thread
    private var pausedBySystem = false          // CHỈ đọc/ghi trên main thread
    private var idleTimer: Timer?               // CHỈ đọc/ghi trên main thread
    private weak var attachedPreviewLayer: AVCaptureVideoPreviewLayer? // CHỈ đọc/ghi trên main thread
    private var observers: [NSObjectProtocol] = []

    private let queue = DispatchQueue(label: "camera.session")
    private let frameLock = NSLock()
    private var latestBuffer: CVPixelBuffer?
    private var configured = false              // CHỈ đọc/ghi trên `queue`
    private var device: AVCaptureDevice?        // CHỈ đọc/ghi trên `queue`
    private var metaOutput: AVCaptureMetadataOutput? // CHỈ đọc/ghi trên `queue`
    private let ciContext = CIContext()
    private var lastRegion = CGRect.zero        // CHỈ đọc/ghi trên main thread
    private let metaQueue = DispatchQueue(label: "camera.meta", qos: .userInteractive)

    override init() {
        super.init()
        let nc = NotificationCenter.default
        observers.append(nc.addObserver(forName: ProcessInfo.thermalStateDidChangeNotification, object: nil, queue: .main) { [weak self] _ in
            self?.applyThermalAndIdle()
        })
        observers.append(nc.addObserver(forName: AVCaptureSession.wasInterruptedNotification, object: session, queue: .main) { [weak self] _ in
            guard let self = self else { return }
            self.pausedBySystem = true
            self.idleTimer?.invalidate()
            self.idleTimer = nil
            self.running = false
            self.torchOn = false
            self.restReason = "Camera tạm dừng do có cuộc gọi hoặc ứng dụng khác đang dùng."
        })
        observers.append(nc.addObserver(forName: AVCaptureSession.interruptionEndedNotification, object: session, queue: .main) { [weak self] _ in
            guard let self = self, self.pausedBySystem else { return }
            self.pausedBySystem = false
            if self.wantsRunning {
                self.start()
            }
        })
        observers.append(nc.addObserver(forName: AVCaptureSession.runtimeErrorNotification, object: session, queue: .main) { [weak self] _ in
            guard let self = self else { return }
            self.running = false
            self.torchOn = false
            if self.wantsRunning {
                self.begin()
            }
        })
        observers.append(nc.addObserver(forName: UIApplication.didBecomeActiveNotification, object: nil, queue: .main) { [weak self] _ in
            guard let self = self else { return }
            let status = AVCaptureDevice.authorizationStatus(for: .video)
            if status == .authorized && self.denied {
                self.denied = false
                if self.wantsRunning { self.begin() }
            }
        })
    }

    deinit {
        let nc = NotificationCenter.default
        for o in observers { nc.removeObserver(o) }
        idleTimer?.invalidate()
        let s = session
        queue.async {
            if s.isRunning { s.stopRunning() }
        }
    }

    private func setFPS(_ fps: Int32) {
        queue.async { [weak self] in
            guard let self = self, let dev = self.device, fps != self.currentFPS else { return }
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
            stop()
            return
        }
        let idle = Date().timeIntervalSince(lastCodeAt)
        let restAfter = UserDefaults.standard.object(forKey: "cameraRestAfterSec") as? Double ?? 30
        if restAfter > 0, idle > restAfter {
            restReason = "Camera tạm nghỉ vì \(Int(restAfter)) giây không quét mã (đỡ nóng máy, đỡ tốn pin). Chạm để quét tiếp."
            stop()
            return
        }
        if thermal == .serious { setFPS(hotFPS) }
        else if idle > 15 { setFPS(idleFPS) }
        else { setFPS(activeFPS) }
    }

    func start() {
        wantsRunning = true
        pausedBySystem = false
        restReason = nil
        lastCodeAt = Date()
        switch AVCaptureDevice.authorizationStatus(for: .video) {
        case .authorized:
            denied = false
            begin()
        case .notDetermined:
            denied = false
            AVCaptureDevice.requestAccess(for: .video) { [weak self] ok in
                DispatchQueue.main.async {
                    guard let self = self else { return }
                    if ok {
                        self.denied = false
                        if self.wantsRunning { self.begin() }
                    } else {
                        self.denied = true
                        self.wantsRunning = false
                    }
                }
            }
        default:
            denied = true
            idleTimer?.invalidate()
            idleTimer = nil
        }
    }

    private func begin() {
        idleTimer?.invalidate()
        idleTimer = Timer.scheduledTimer(withTimeInterval: 3, repeats: true) { [weak self] _ in
            self?.applyThermalAndIdle()
        }
        queue.async { [weak self] in
            guard let self = self else { return }
            if !self.configured { self.configure() }
            if !self.session.isRunning { self.session.startRunning() }
            self.currentFPS = 0
            let isRunning = self.session.isRunning
            DispatchQueue.main.async { [weak self] in
                guard let self = self else { return }
                if !self.wantsRunning {
                    // Đã có lệnh stop() trong lúc chờ startRunning() trên `queue`: dừng ngay để không rò camera.
                    self.stop()
                    return
                }
                self.running = isRunning
                self.lastRegion = .zero
                if let layer = self.attachedPreviewLayer {
                    self.updateScanRegion(layer)
                }
                self.applyThermalAndIdle()
            }
        }
    }

    func stop() {
        wantsRunning = false
        pausedBySystem = false
        idleTimer?.invalidate()
        idleTimer = nil
        running = false
        torchOn = false
        lastRegion = .zero
        unfreezeWork?.cancel()
        frozenFrame = nil
        frameLock.lock()
        latestBuffer = nil
        frameLock.unlock()
        queue.async { [weak self] in
            guard let self = self else { return }
            if let dev = self.device, dev.hasTorch, dev.torchMode != .off {
                if (try? dev.lockForConfiguration()) != nil {
                    dev.torchMode = .off
                    dev.unlockForConfiguration()
                }
            }
            if self.session.isRunning { self.session.stopRunning() }
            self.currentFPS = 0
        }
    }

    private func configure() {
        session.beginConfiguration()
        if session.canSetSessionPreset(.hd1920x1080) { session.sessionPreset = .hd1920x1080 }
        guard let dev = AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: .back),
              let input = try? AVCaptureDeviceInput(device: dev), session.canAddInput(input) else {
            session.commitConfiguration()
            return
        }
        session.addInput(input)
        device = dev

        let meta = AVCaptureMetadataOutput()
        if session.canAddOutput(meta) {
            session.addOutput(meta)
            metaOutput = meta
            meta.setMetadataObjectsDelegate(self, queue: metaQueue)
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
            if desiredZoom > 1 {
                dev.videoZoomFactor = max(1, min(desiredZoom, dev.activeFormat.videoMaxZoomFactor, 6))
            }
            dev.unlockForConfiguration()
        } catch {}
        configured = true
    }

    /// Chỉ nhận mã nằm trong khung đang nhìn thấy (khung nhỏ hơn toàn màn hình). Nếu tính ra giá trị lạ thì giữ nguyên toàn khung hình.
    func updateScanRegion(_ layer: AVCaptureVideoPreviewLayer) {
        attachedPreviewLayer = layer
        guard layer.bounds.width > 20, layer.bounds.height > 20 else { return }
        let r = layer.metadataOutputRectConverted(fromLayerRect: layer.bounds)
        guard r.minX.isFinite, r.minY.isFinite, r.width.isFinite, r.height.isFinite,
              r.width > 0.1, r.height > 0.1,
              r.minX >= -0.01, r.minY >= -0.01, r.maxX <= 1.01, r.maxY <= 1.01 else { return }
        let clamped = CGRect(
            x: max(0, min(1, r.minX)),
            y: max(0, min(1, r.minY)),
            width: max(0.1, min(1, r.width)),
            height: max(0.1, min(1, r.height))
        )
        let l = lastRegion
        if abs(l.minX - clamped.minX) < 0.004,
           abs(l.minY - clamped.minY) < 0.004,
           abs(l.width - clamped.width) < 0.004,
           abs(l.height - clamped.height) < 0.004 { return }
        lastRegion = clamped
        queue.async { [weak self] in
            guard let meta = self?.metaOutput else { return }
            meta.rectOfInterest = clamped
        }
    }

    func setTorch(_ on: Bool) {
        queue.async { [weak self] in
            guard let self = self, let dev = self.device, dev.hasTorch else {
                DispatchQueue.main.async { self?.torchOn = false }
                return
            }
            var applied = false
            do {
                try dev.lockForConfiguration()
                dev.torchMode = on ? .on : .off
                dev.unlockForConfiguration()
                applied = (dev.torchMode == .on)
            } catch {
                applied = (dev.torchMode == .on)
            }
            DispatchQueue.main.async { [weak self] in
                self?.torchOn = applied
            }
        }
    }

    func setZoom(_ factor: CGFloat) {
        queue.async { [weak self] in
            guard let self = self else { return }
            self.desiredZoom = max(1, factor)
            guard let dev = self.device else { return }
            do {
                try dev.lockForConfiguration()
                dev.videoZoomFactor = max(1, min(self.desiredZoom, dev.activeFormat.videoMaxZoomFactor, 6))
                dev.unlockForConfiguration()
            } catch {}
        }
    }

    // MARK: delegates
    /// Loại mã không có số kiểm tra / sửa lỗi, rất hay bị đọc nhầm từ chữ và hoa văn: phải thấy cùng một giá trị nhiều lần liên tiếp mới nhận.
    private let weakTypes: Set<AVMetadataObject.ObjectType> = [.code39, .interleaved2of5, .itf14]
    private var weakCandidate: (code: String, hits: Int, last: Date)?

    func metadataOutput(_ output: AVCaptureMetadataOutput, didOutput metadataObjects: [AVMetadataObject], from connection: AVCaptureConnection) {
        let minSize = UserDefaults.standard.object(forKey: "minCodeSize") as? Double ?? 0.12
        var found: [String] = []
        for obj in metadataObjects {
            guard let m = obj as? AVMetadataMachineReadableCodeObject, let s = m.stringValue, !s.isEmpty else { continue }
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
            if !found.contains(s) { found.append(s) }
        }
        guard !found.isEmpty else { return }
        let candidates = found
        DispatchQueue.main.async { [weak self] in
            guard let self = self, self.running, self.wantsRunning else { return }
            // khung đang đứng sau mã trước: bỏ (không kéo dài thời gian đứng)
            guard self.freezeGate.accept() else { return }
            self.freezeFrame()
            let wasIdle = Date().timeIntervalSince(self.lastCodeAt) > 15
            self.lastCodeAt = Date()
            if wasIdle && ProcessInfo.processInfo.thermalState != .serious { self.setFPS(self.activeFPS) }
            if let many = self.onCodes {
                many(candidates)
            } else if let first = candidates.first {
                self.onCode?(first)
            }
        }
    }

    /// Phủ khung hình hiện tại lên khung xem trước trong `freezeGate.duration` giây (camera vẫn chạy bên dưới).
    private func freezeFrame() {
        let d = freezeGate.duration
        guard d > 0 else { return }
        frameLock.lock()
        let px = latestBuffer
        frameLock.unlock()
        if let px = px, let cg = ciContext.createCGImage(CIImage(cvPixelBuffer: px), from: CIImage(cvPixelBuffer: px).extent) {
            frozenFrame = UIImage(cgImage: cg)
        }
        unfreezeWork?.cancel()
        let work = DispatchWorkItem { [weak self] in self?.frozenFrame = nil }
        unfreezeWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + d, execute: work)
    }

    func captureOutput(_ output: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer, from connection: AVCaptureConnection) {
        guard let px = CMSampleBufferGetImageBuffer(sampleBuffer) else { return }
        frameLock.lock()
        latestBuffer = px
        frameLock.unlock()
    }

    /// Chụp khung hình hiện tại và nén theo cấu hình LƯU TRỮ cục bộ trên iPhone (T039: cạnh dài ~1280 px, mục tiêu ~60-120 KB, không dưới 960 px).
    func snapshotJPEG(config: PhotoCompressionConfig = .storageDefault) -> Data? {
        frameLock.lock()
        let px = latestBuffer
        frameLock.unlock()
        guard let px = px else { return nil }
        let ci = CIImage(cvPixelBuffer: px)
        guard let cg = ciContext.createCGImage(ci, from: ci.extent) else { return nil }
        return PhotoCompressor.compressCGImage(cg, config: config)
    }
}

final class PreviewUIView: UIView {
    override class var layerClass: AnyClass { AVCaptureVideoPreviewLayer.self }
    var previewLayer: AVCaptureVideoPreviewLayer? { layer as? AVCaptureVideoPreviewLayer }
    var onLayout: ((AVCaptureVideoPreviewLayer) -> Void)?
    private var delayedRelayout: DispatchWorkItem?

    deinit {
        delayedRelayout?.cancel()
    }

    override func layoutSubviews() {
        super.layoutSubviews()
        guard let pl = previewLayer else { return }
        updateOrientation(for: pl)
        onLayout?(pl)
        delayedRelayout?.cancel()
        let work = DispatchWorkItem { [weak self] in
            guard let self = self, let layer = self.previewLayer else { return }
            self.updateOrientation(for: layer)
            self.onLayout?(layer)
        }
        delayedRelayout = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.6, execute: work)
    }

    private func updateOrientation(for preview: AVCaptureVideoPreviewLayer) {
        guard let conn = preview.connection, conn.isVideoOrientationSupported else { return }
        let interfaceOrientation = window?.windowScene?.interfaceOrientation ?? .portrait
        let mapped: AVCaptureVideoOrientation
        switch interfaceOrientation {
        case .portraitUpsideDown: mapped = .portraitUpsideDown
        case .landscapeLeft: mapped = .landscapeLeft
        case .landscapeRight: mapped = .landscapeRight
        default: mapped = .portrait
        }
        if conn.videoOrientation != mapped {
            conn.videoOrientation = mapped
        }
    }
}

struct CameraPreview: UIViewRepresentable {
    let controller: CameraController
    func makeUIView(context: Context) -> PreviewUIView {
        let v = PreviewUIView()
        v.previewLayer?.session = controller.session
        v.previewLayer?.videoGravity = .resizeAspectFill
        v.onLayout = { [weak controller] layer in controller?.updateScanRegion(layer) }
        return v
    }
    func updateUIView(_ uiView: PreviewUIView, context: Context) {}
}
