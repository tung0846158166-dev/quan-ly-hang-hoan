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

    private let queue = DispatchQueue(label: "camera.session")
    private let frameLock = NSLock()
    private var latestBuffer: CVPixelBuffer?
    private var configured = false
    private var device: AVCaptureDevice?
    private let ciContext = CIContext()

    func start() {
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
            DispatchQueue.main.async { self.running = self.session.isRunning }
        }
    }

    func stop() {
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
            meta.setMetadataObjectsDelegate(self, queue: .main)
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
    func metadataOutput(_ output: AVCaptureMetadataOutput, didOutput metadataObjects: [AVMetadataObject], from connection: AVCaptureConnection) {
        for obj in metadataObjects {
            if let m = obj as? AVMetadataMachineReadableCodeObject, let s = m.stringValue, !s.isEmpty {
                onCode?(s)
                return
            }
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
}

struct CameraPreview: UIViewRepresentable {
    let session: AVCaptureSession
    func makeUIView(context: Context) -> PreviewUIView {
        let v = PreviewUIView()
        v.previewLayer.session = session
        v.previewLayer.videoGravity = .resizeAspectFill
        return v
    }
    func updateUIView(_ uiView: PreviewUIView, context: Context) {}
}
