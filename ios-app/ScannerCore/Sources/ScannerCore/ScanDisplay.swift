import Foundation

/// Màu của nhãn kết quả quét (giao diện ánh xạ sang màu thật: xanh / đỏ / vàng).
public enum ScanTone: String, Equatable, Sendable {
    case success   // xanh
    case danger    // đỏ
    case warning   // vàng
}

/// Nhãn + màu hiển thị cho một lượt quét. Quy ước của chủ dự án:
/// - Hoàn kho: "Thành công" xanh; "Quét trùng" đỏ; "Đơn hủy" (đơn có trạng thái hủy trên hệ thống) chữ xanh nếu mới quét, chữ đỏ nếu đã quét trước đó; "Không có" vàng.
/// - Đơn mới: "Tạo thành công" xanh; "Đã tạo rồi" đỏ.
public struct ScanDisplay: Equatable, Sendable {
    public let label: String
    public let tone: ScanTone

    /// Nhãn ngắn dùng chung cho danh sách, lịch sử, chi tiết.
    public static func make(for it: ScanItem) -> ScanDisplay {
        if !it.scanConfirmed {
            return ScanDisplay(label: it.status == .error ? "Lỗi, đang thử lại" : "Chờ nạp PC", tone: .warning)
        }
        let returning = it.kind == .returnScan
        switch it.status {
        case .success:
            if returning { return ScanDisplay(label: it.isCancelledOrder ? "Đơn hủy" : "Thành công", tone: .success) }
            return ScanDisplay(label: "Tạo thành công", tone: .success)
        case .duplicate:
            if returning { return ScanDisplay(label: it.isCancelledOrder ? "Đơn hủy" : "Quét trùng", tone: .danger) }
            return ScanDisplay(label: "Đã tạo rồi", tone: .danger)
        case .notFound:
            return ScanDisplay(label: "Không có", tone: .warning)
        case .ambiguous:
            return ScanDisplay(label: "Trùng nhiều đơn", tone: .danger)
        case .error:
            return ScanDisplay(label: "PC từ chối", tone: .danger)
        case .pending:
            return ScanDisplay(label: "Chờ nạp PC", tone: .warning)
        }
    }

    /// Câu thông báo (banner) ngay sau khi PC trả kết quả.
    public static func message(for it: ScanItem) -> ScanDisplay {
        let returning = it.kind == .returnScan
        switch it.status {
        case .success:
            if returning {
                return ScanDisplay(label: it.isCancelledOrder ? "Đơn hủy: đã ghi nhận" : "Thành công: PC đã ghi nhận", tone: .success)
            }
            return ScanDisplay(label: "Tạo thành công: đã tạo đơn mới trên PC", tone: .success)
        case .duplicate:
            if returning {
                return ScanDisplay(label: it.isCancelledOrder ? "Đơn hủy: đã quét trước đó" : "Quét trùng: đơn đã nhận hoàn kho trước đó", tone: .danger)
            }
            return ScanDisplay(label: "Đã tạo rồi: trùng với đơn đã tạo trước đó", tone: .danger)
        case .notFound:
            return ScanDisplay(label: "Không có: không tìm thấy đơn trên hệ thống", tone: .warning)
        case .ambiguous:
            return ScanDisplay(label: "Trùng nhiều đơn, cần kiểm tra trên PC", tone: .danger)
        case .error:
            return ScanDisplay(label: it.scanConfirmed ? "PC từ chối, cần kiểm tra lại" : "PC chưa lưu được, đang tự thử lại", tone: .danger)
        case .pending:
            return ScanDisplay(label: "Đang chờ máy tính xác nhận", tone: .warning)
        }
    }
}
