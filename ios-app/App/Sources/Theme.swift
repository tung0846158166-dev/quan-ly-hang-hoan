import SwiftUI
import UIKit
import ScannerCore

/// Bộ giao diện chung v2: màu nhấn cam thương hiệu, thẻ bo góc lớn, chữ số đậm, hỗ trợ sáng/tối & Dynamic Type.
enum Theme {
    /// Cam thương hiệu nổi bật trên cả nền sáng và nền tối.
    static let accent = Color(red: 0.94, green: 0.37, blue: 0.13)
    static let accentSoft = Color(red: 0.94, green: 0.37, blue: 0.13).opacity(0.14)
    static let danger = Color(red: 0.90, green: 0.22, blue: 0.24)
    static let warning = Color(red: 0.94, green: 0.60, blue: 0.06)
    static let success = Color(red: 0.12, green: 0.68, blue: 0.41)
    static let info = Color(red: 0.16, green: 0.49, blue: 0.95)
    static let teal = Color(red: 0.05, green: 0.62, blue: 0.58)

    static let cardRadius: CGFloat = 18
    static let smallRadius: CGFloat = 12
    static let cardBackground = Color(.secondarySystemGroupedBackground)
    static let pageBackground = Color(.systemGroupedBackground)

    static func modeColor(_ k: ScanKind) -> Color {
        switch k {
        case .returnScan: return accent
        case .newOrder: return teal
        default: return danger
        }
    }

    static func platformColor(_ raw: String) -> Color {
        let name = DashboardMetrics.normalizedPlatform(raw)
        switch name {
        case "Shopee": return accent
        case "TikTok": return Color(.label)
        case "Lazada": return info
        default: return .secondary
        }
    }
}

/// Rung phản hồi nhẹ khi chạm thẻ/nút (tôn trọng cài đặt "Rung khi quét").
enum Haptics {
    static func tap() {
        guard SyncEngine.pref("hapticOn") else { return }
        UIImpactFeedbackGenerator(style: .light).impactOccurred()
    }

    static func medium() {
        guard SyncEngine.pref("hapticOn") else { return }
        UIImpactFeedbackGenerator(style: .medium).impactOccurred()
    }
}

struct CardModifier: ViewModifier {
    var padding: CGFloat = 14
    var radius: CGFloat = Theme.cardRadius
    @Environment(\.colorScheme) private var scheme

    func body(content: Content) -> some View {
        content
            .padding(padding)
            .background(Theme.cardBackground)
            .clipShape(RoundedRectangle(cornerRadius: radius, style: .continuous))
            .overlay(
                RoundedRectangle(cornerRadius: radius, style: .continuous)
                    .stroke(Color.primary.opacity(scheme == .dark ? 0.10 : 0.05), lineWidth: 0.8)
            )
            .shadow(color: Color.black.opacity(scheme == .dark ? 0.22 : 0.05), radius: 10, x: 0, y: 3)
    }
}

extension View {
    func card(padding: CGFloat = 14, radius: CGFloat = Theme.cardRadius) -> some View {
        modifier(CardModifier(padding: padding, radius: radius))
    }
}

/// Hiển thị con số mượt mà, tôn trọng tùy chọn Giảm chuyển động (Reduce Motion) và tương thích iOS 16+.
struct AnimatedCounter: View {
    let value: Int
    var font: Font = .system(size: 30, weight: .bold, design: .rounded)
    var color: Color = .primary
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        Group {
            if #available(iOS 17.0, *), !reduceMotion {
                Text("\(value)")
                    .contentTransition(.numericText(value: Double(value)))
                    .animation(.snappy(duration: 0.25), value: value)
            } else {
                Text("\(value)")
            }
        }
        .font(font)
        .foregroundColor(color)
        .monospacedDigit()
        .lineLimit(1)
        .minimumScaleFactor(0.7)
    }
}

/// Huy hiệu sàn (Shopee / TikTok / Lazada) đồng nhất toàn app.
struct PlatformBadge: View {
    let platform: String

    var body: some View {
        let name = DashboardMetrics.normalizedPlatform(platform)
        let tint = Theme.platformColor(platform)
        Text(name)
            .font(.caption2.bold())
            .padding(.horizontal, 8)
            .padding(.vertical, 3)
            .background(tint.opacity(0.14))
            .foregroundColor(tint)
            .clipShape(Capsule())
    }
}

/// Nhãn trạng thái dạng viên thuốc.
struct StatusPill: View {
    let text: String
    let color: Color

    var body: some View {
        Text(text)
            .font(.caption2.bold())
            .padding(.horizontal, 8)
            .padding(.vertical, 3)
            .background(color.opacity(0.15))
            .foregroundColor(color)
            .clipShape(Capsule())
    }
}

/// Tiêu đề nhóm thẻ trên màn Tổng quan và Cài đặt.
struct SectionTitleRow: View {
    let title: String
    var icon: String? = nil
    var iconColor: Color = Theme.accent
    var subtitle: String? = nil

    var body: some View {
        HStack(spacing: 8) {
            if let icon = icon {
                Image(systemName: icon)
                    .font(.subheadline.bold())
                    .foregroundColor(iconColor)
            }
            Text(title)
                .font(.headline.bold())
                .foregroundColor(.primary)
            Spacer(minLength: 4)
            if let subtitle = subtitle, !subtitle.isEmpty {
                Text(subtitle)
                    .font(.caption.bold())
                    .foregroundStyle(.secondary)
            }
        }
    }
}

/// Ô số liệu lớn (dùng ở Tổng quan, Thông báo, Thống kê).
struct StatTile: View {
    let title: String
    let value: Int
    let icon: String
    let color: Color
    var subtitle: String? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 6) {
                Image(systemName: icon)
                    .font(.caption.bold())
                    .foregroundColor(color)
                    .frame(width: 24, height: 24)
                    .background(color.opacity(0.14))
                    .clipShape(Circle())
                Text(title)
                    .font(.caption.bold())
                    .foregroundStyle(.secondary)
                    .lineLimit(2)
                    .minimumScaleFactor(0.85)
                Spacer(minLength: 0)
            }
            AnimatedCounter(
                value: value,
                font: .system(size: 32, weight: .bold, design: .rounded),
                color: value > 0 ? color : .primary
            )
            if let subtitle = subtitle, !subtitle.isEmpty {
                Text(subtitle)
                    .font(.caption2)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
        }
        .frame(maxWidth: .infinity, minHeight: 88, alignment: .leading)
        .card(padding: 14)
    }
}

/// Nút lọc dạng viên thuốc, vùng chạm thoải mái >= 40pt.
struct Chip: View {
    let title: String
    let selected: Bool
    let action: () -> Void
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        Button {
            Haptics.tap()
            if reduceMotion {
                action()
            } else {
                withAnimation(.easeInOut(duration: 0.16)) { action() }
            }
        } label: {
            Text(title)
                .font(.footnote.bold())
                .padding(.horizontal, 14)
                .padding(.vertical, 9)
                .frame(minHeight: 38)
                .background(selected ? Theme.accent : Color(.tertiarySystemFill))
                .foregroundColor(selected ? .white : .primary)
                .clipShape(Capsule())
        }
        .buttonStyle(.plain)
    }
}

/// Chọn chế độ quét dạng viên thuốc có màu theo chế độ.
struct ModePicker: View {
    @Binding var mode: ScanKind
    private let items: [(ScanKind, String, String)] = [
        (.returnScan, "Hoàn kho", "shippingbox.fill"),
        (.newOrder, "Đơn mới", "plus.circle.fill")
    ]
    var body: some View {
        HStack(spacing: 8) {
            ForEach(items, id: \.0) { item in
                let on = mode == item.0
                Button {
                    Haptics.tap()
                    mode = item.0
                } label: {
                    HStack(spacing: 6) {
                        Image(systemName: item.2).font(.footnote.bold())
                        Text(item.1).font(.footnote.bold())
                    }
                    .frame(maxWidth: .infinity, minHeight: 42)
                    .background(on ? Theme.modeColor(item.0) : Color(.tertiarySystemFill))
                    .foregroundColor(on ? .white : .primary)
                    .clipShape(Capsule())
                }
                .buttonStyle(.plain)
            }
        }
    }
}
