import AVFoundation
import AudioToolbox
import UIKit

/// Âm thanh + rung khi quét (chủ dự án 28/11/2026: "âm thanh và rung khi quét vẫn chưa dùng được").
/// Trước đây: chế độ cloud KHÔNG phát âm thanh; âm hệ thống (AudioServicesPlaySystemSound) tắt khi gạt im lặng;
/// rung Taptic bị iOS chặn khi camera đang chạy.
/// Giờ: tiếng bíp do app tự tạo, phát bằng phiên âm thanh "playback" (kêu cả khi gạt im lặng, trộn với nhạc đang mở);
/// rung = Taptic + bộ rung hệ thống (kSystemSoundID_Vibrate, chạy được cả khi tắt "Rung hệ thống"); cho phép rung khi camera chạy.
/// Bật/tắt theo "Âm thanh khi quét" (soundOn) và "Rung phản hồi" (hapticOn) trong Cài đặt.
@MainActor
enum ScanFeedback {
    enum Kind { case success, warning, error }

    private static var players: [Kind: AVAudioPlayer] = [:]
    private static var sessionReady = false

    private static func pref(_ key: String) -> Bool { UserDefaults.standard.object(forKey: key) as? Bool ?? true }

    /// Gọi một lần lúc mở màn quét (tạo sẵn tiếng để phát ngay không trễ).
    static func prepare() {
        setupSession()
        for k in [Kind.success, .warning, .error] where players[k] == nil {
            if let p = try? AVAudioPlayer(data: tone(k)) { p.prepareToPlay(); players[k] = p }
        }
    }

    /// Chạm nhẹ lúc vừa đọc được mã (chưa có kết quả).
    static func tick() {
        guard pref("hapticOn") else { return }
        setupSession()
        UIImpactFeedbackGenerator(style: .heavy).impactOccurred()
    }

    /// Kết quả quét: tiếng + rung theo loại.
    static func play(_ kind: Kind) {
        prepare()
        if pref("soundOn"), let p = players[kind] {
            p.currentTime = 0
            p.play()
        }
        if pref("hapticOn") {
            let g = UINotificationFeedbackGenerator()
            g.notificationOccurred(kind == .success ? .success : kind == .warning ? .warning : .error)
            // rung máy thật (kêu "rừ"): chạy được cả khi Taptic bị chặn / tắt "Rung hệ thống"
            AudioServicesPlaySystemSound(kSystemSoundID_Vibrate)
        }
    }

    private static func setupSession() {
        guard !sessionReady else { return }
        let s = AVAudioSession.sharedInstance()
        // playback: kêu cả khi gạt im lặng; mixWithOthers: không tắt nhạc / video đang mở
        try? s.setCategory(.playback, mode: .default, options: [.mixWithOthers])
        try? s.setAllowHapticsAndSystemSoundsDuringRecording(true)   // cho rung khi camera đang chạy
        try? s.setActive(true)
        sessionReady = true
    }

    /// Tiếng bíp dạng WAV 16-bit mono: thành công = 1 tiếng cao ngắn; trùng / cần kiểm tra = 2 tiếng; lỗi = 1 tiếng trầm dài.
    private static func tone(_ kind: Kind) -> Data {
        let rate = 44_100.0
        let parts: [(freq: Double, ms: Double)]
        switch kind {
        case .success: parts = [(1760, 110)]
        case .warning: parts = [(1175, 90), (0, 70), (1175, 90)]
        case .error: parts = [(330, 380)]
        }
        var samples: [Int16] = []
        for (freq, ms) in parts {
            let n = Int(rate * ms / 1000)
            for i in 0..<n {
                guard freq > 0 else { samples.append(0); continue }
                let t = Double(i) / rate
                let fade = min(1, Double(i) / 200, Double(n - i) / 400)    // tránh tiếng lụp bụp đầu / cuối
                let v = sin(2 * .pi * freq * t) * 0.9 * fade
                samples.append(Int16(max(-1, min(1, v)) * Double(Int16.max)))
            }
        }
        var d = Data()
        func u32(_ v: UInt32) { var x = v.littleEndian; d.append(Data(bytes: &x, count: 4)) }
        func u16(_ v: UInt16) { var x = v.littleEndian; d.append(Data(bytes: &x, count: 2)) }
        let dataBytes = UInt32(samples.count * 2)
        d.append("RIFF".data(using: .ascii)!); u32(36 + dataBytes); d.append("WAVE".data(using: .ascii)!)
        d.append("fmt ".data(using: .ascii)!); u32(16); u16(1); u16(1); u32(UInt32(rate)); u32(UInt32(rate) * 2); u16(2); u16(16)
        d.append("data".data(using: .ascii)!); u32(dataBytes)
        samples.withUnsafeBufferPointer { d.append(Data(buffer: $0)) }
        return d
    }
}
