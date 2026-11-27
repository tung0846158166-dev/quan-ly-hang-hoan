import SwiftUI
import UIKit
import ScannerCore

/// Bộ giao diện v2 (Pass 2): nền tối sâu, nhấn cam thương hiệu, thẻ mờ tinh tế với viền mảnh,
/// số lớn kiểu rounded, biểu tượng SF Symbols trong ô vuông bo màu, hỗ trợ sáng/tối & Dynamic Type.
enum Theme {
    /// Cam thương hiệu nổi bật trên cả nền sáng và nền tối.
    static let accent = Color(red: 0.96, green: 0.40, blue: 0.14)
    static let accentSoft = Color(red: 0.96, green: 0.40, blue: 0.14).opacity(0.16)
    static let danger = Color(red: 0.95, green: 0.26, blue: 0.27)
    static let warning = Color(red: 0.96, green: 0.65, blue: 0.10)
    static let success = Color(red: 0.16, green: 0.74, blue: 0.45)
    static let info = Color(red: 0.22, green: 0.55, blue: 0.98)
    static let teal = Color(red: 0.10, green: 0.68, blue: 0.63)
    static let purple = Color(red: 0.64, green: 0.40, blue: 0.96)

    static let cardRadius: CGFloat = 14
    static let smallRadius: CGFloat = 10
    static let cardPadding: CGFloat = 12
    static let cardSpacing: CGFloat = 8
    static let sectionSpacing: CGFloat = 10
    static let screenPadding: CGFloat = 14
    static let minTapTarget: CGFloat = 44
    static let demoBannerHeight: CGFloat = 24

    /// Thang cỡ chữ chuẩn toàn app (tự co giãn theo Dynamic Type, mặc định 17 / 15 / 13 / 12 / 11pt).
    enum Typography {
        static let navTitle: Font = .headline.bold()              // ~17pt bold
        static let sectionTitle: Font = .subheadline.bold()       // ~15pt bold
        static let primary: Font = .subheadline                   // ~15pt
        static let primaryBold: Font = .subheadline.weight(.semibold)
        static let code: Font = .system(.subheadline, design: .monospaced).bold() // ~15pt mono bold
        static let secondary: Font = .footnote                    // ~13pt
        static let secondaryBold: Font = .footnote.weight(.semibold)
        static let caption: Font = .caption                       // ~12pt
        static let captionBold: Font = .caption.bold()
        static let micro: Font = .caption2                        // ~11pt
        static let microBold: Font = .caption2.bold()
        static let heroNumber: Font = .system(size: 26, weight: .bold, design: .rounded)
        static let metricNumber: Font = .system(size: 18, weight: .bold, design: .rounded)
    }

    /// Nền trang: tối thật sâu ở Dark Mode, sáng gọn ở Light Mode.
    static let pageBackground = Color(UIColor { traits in
        traits.userInterfaceStyle == .dark
            ? UIColor(red: 0.04, green: 0.04, blue: 0.06, alpha: 1.0)
            : UIColor.systemGroupedBackground
    })

    /// Nền thẻ nổi nhẹ thích ứng sáng/tối.
    static let cardBackground = Color(UIColor { traits in
        traits.userInterfaceStyle == .dark
            ? UIColor(red: 0.11, green: 0.11, blue: 0.14, alpha: 0.92)
            : UIColor.secondarySystemGroupedBackground
    })

    /// Nền ô phụ bên trong thẻ.
    static let subtleFill = Color(UIColor { traits in
        traits.userInterfaceStyle == .dark
            ? UIColor(red: 0.16, green: 0.16, blue: 0.20, alpha: 0.85)
            : UIColor.tertiarySystemGroupedBackground
    })

    static func modeColor(_ k: ScanKind) -> Color {
        switch k {
        case .returnScan: return accent
        case .newOrder: return teal
        }
    }

    static func platformColor(_ raw: String) -> Color {
        let name = DashboardMetrics.normalizedPlatform(raw)
        switch name {
        case "Shopee": return accent
        case "TikTok": return Color(.label)
        case "Lazada": return info
        case "Ngoài sàn": return success
        default: return .secondary
        }
    }
}

/// Rung phản hồi nhẹ khi chạm thẻ/nút (tôn trọng cài đặt "Rung phản hồi").
@MainActor
enum Haptics {
    private static var enabled: Bool { UserDefaults.standard.object(forKey: "hapticOn") as? Bool ?? true }

    static func tap() {
        guard enabled else { return }
        UIImpactFeedbackGenerator(style: .light).impactOccurred()
    }

    static func medium() {
        guard enabled else { return }
        UIImpactFeedbackGenerator(style: .medium).impactOccurred()
    }
}

/// Thẻ mờ tinh tế (Material nhẹ + nền thẻ + đường viền mảnh).
struct CardModifier: ViewModifier {
    var padding: CGFloat = Theme.cardPadding
    var radius: CGFloat = Theme.cardRadius
    @Environment(\.colorScheme) private var scheme

    func body(content: Content) -> some View {
        content
            .padding(padding)
            .background {
                RoundedRectangle(cornerRadius: radius, style: .continuous)
                    .fill(Theme.cardBackground)
                    .overlay(
                        RoundedRectangle(cornerRadius: radius, style: .continuous)
                            .fill(.ultraThinMaterial.opacity(scheme == .dark ? 0.30 : 0.14))
                    )
            }
            .clipShape(RoundedRectangle(cornerRadius: radius, style: .continuous))
            .overlay(
                RoundedRectangle(cornerRadius: radius, style: .continuous)
                    .stroke(Color.primary.opacity(scheme == .dark ? 0.11 : 0.06), lineWidth: 0.7)
            )
            .shadow(color: Color.black.opacity(scheme == .dark ? 0.22 : 0.03), radius: 6, x: 0, y: 2)
    }
}

extension View {
    func card(padding: CGFloat = Theme.cardPadding, radius: CGFloat = Theme.cardRadius) -> some View {
        modifier(CardModifier(padding: padding, radius: radius))
    }

    /// Snap ngang mượt trên iOS 17+, giữ cuộn thường an toàn trên iOS 16.
    @ViewBuilder
    func snapPagingIfAvailable() -> some View {
        if #available(iOS 17.0, *) {
            self.scrollTargetBehavior(.viewAligned)
        } else {
            self
        }
    }

    @ViewBuilder
    func snapTargetLayoutIfAvailable() -> some View {
        if #available(iOS 17.0, *) {
            self.scrollTargetLayout()
        } else {
            self
        }
    }
}

/// Dải báo chế độ Dữ liệu mẫu mảnh (tối đa 24pt, chữ 11pt) đặt TRÊN thanh điều hướng, không đè nội dung.
struct DemoModeBanner: View {
    let onExit: () -> Void

    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: "sparkles")
                .font(.system(size: 10, weight: .bold))
                .foregroundColor(Theme.accent)
            Text("DỮ LIỆU MẪU · không gửi lên máy tính")
                .font(.system(size: 11, weight: .semibold))
                .foregroundColor(.primary)
                .lineLimit(1)
                .minimumScaleFactor(0.8)
            Spacer(minLength: 4)
            Button {
                Haptics.tap()
                onExit()
            } label: {
                Text("Tắt")
                    .font(.system(size: 11, weight: .bold))
                    .padding(.horizontal, 8)
                    .padding(.vertical, 2)
                    .background(Theme.accent.opacity(0.18))
                    .foregroundColor(Theme.accent)
                    .clipShape(Capsule())
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Tắt chế độ dữ liệu mẫu")
        }
        .padding(.horizontal, Theme.screenPadding)
        .frame(height: Theme.demoBannerHeight)
        .background(.ultraThinMaterial, ignoresSafeAreaEdges: .top)
        .overlay(alignment: .bottom) {
            Rectangle()
                .fill(Theme.accent.opacity(0.30))
                .frame(height: 0.6)
        }
    }
}

/// Biểu tượng SF Symbol trong ô vuông bo màu (phong cách Apple Sức khỏe / Cài đặt).
struct IconTile: View {
    let systemName: String
    let color: Color
    var size: CGFloat = 32
    var cornerRadius: CGFloat = 9

    var body: some View {
        Image(systemName: systemName)
            .font(.system(size: size * 0.48, weight: .semibold))
            .foregroundColor(.white)
            .frame(width: size, height: size)
            .background(
                LinearGradient(
                    colors: [color, color.opacity(0.78)],
                    startPoint: .topLeading,
                    endPoint: .bottomTrailing
                )
            )
            .clipShape(RoundedRectangle(cornerRadius: cornerRadius, style: .continuous))
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

/// Huy hiệu sàn (Shopee / TikTok / Lazada) nhỏ gọn đồng nhất toàn app.
struct PlatformBadge: View {
    let platform: String

    var body: some View {
        let name = DashboardMetrics.normalizedPlatform(platform)
        let tint = Theme.platformColor(platform)
        Text(name)
            .font(Theme.Typography.microBold)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .background(tint.opacity(0.14))
            .foregroundColor(tint)
            .clipShape(Capsule())
    }
}

/// Nhãn trạng thái dạng viên thuốc gọn.
struct StatusPill: View {
    let text: String
    let color: Color

    var body: some View {
        Text(text)
            .font(Theme.Typography.microBold)
            .padding(.horizontal, 7)
            .padding(.vertical, 2)
            .background(color.opacity(0.15))
            .foregroundColor(color)
            .clipShape(Capsule())
    }
}

/// Tiêu đề nhóm thẻ trên màn Tổng quan và các màn con.
struct SectionTitleRow: View {
    let title: String
    var icon: String? = nil
    var iconColor: Color = Theme.accent
    var subtitle: String? = nil

    var body: some View {
        HStack(spacing: 8) {
            if let icon = icon {
                IconTile(systemName: icon, color: iconColor, size: 24, cornerRadius: 7)
            }
            Text(title)
                .font(Theme.Typography.sectionTitle)
                .foregroundColor(.primary)
                .lineLimit(1)
            Spacer(minLength: 4)
            if let subtitle = subtitle, !subtitle.isEmpty {
                Text(subtitle)
                    .font(Theme.Typography.captionBold)
                    .foregroundStyle(.secondary)
            }
        }
    }
}

/// Ô số liệu lớn (dùng ở Thông báo, Thống kê).
struct StatTile: View {
    let title: String
    let value: Int
    let icon: String
    let color: Color
    var subtitle: String? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                IconTile(systemName: icon, color: color, size: 26, cornerRadius: 7)
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

/// Ô lọc nhỏ có số đếm (thay dãy nút bo tròn dài).
struct FilterTile: View {
    let title: String
    let count: Int
    let selected: Bool
    var accent: Color = Theme.accent
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
            HStack(spacing: 5) {
                Text(title)
                    .font(selected ? Theme.Typography.captionBold : Theme.Typography.caption.weight(.semibold))
                Text("\(count)")
                    .font(.system(.caption2, design: .rounded).bold())
                    .monospacedDigit()
                    .padding(.horizontal, 5)
                    .padding(.vertical, 1.5)
                    .background(selected ? Color.white.opacity(0.24) : Color.primary.opacity(0.08))
                    .clipShape(Capsule())
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 6)
            .background(selected ? accent : Theme.cardBackground)
            .foregroundColor(selected ? .white : .primary)
            .clipShape(RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous))
            .overlay(
                RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous)
                    .stroke(selected ? Color.clear : Color.primary.opacity(0.08), lineWidth: 0.7)
            )
            .frame(minHeight: Theme.minTapTarget)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

/// Nút lọc dạng viên thuốc gọn (giữ tương thích cho các màn nhỏ).
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
                .padding(.horizontal, 13)
                .padding(.vertical, 8)
                .frame(minHeight: 38)
                .background(selected ? Theme.accent : Theme.subtleFill)
                .foregroundColor(selected ? .white : .primary)
                .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
        }
        .buttonStyle(.plain)
    }
}

/// Bộ chọn phân đoạn nhỏ gọn cho chế độ quét (Hoàn kho / Đơn mới).
struct ModePicker: View {
    @Binding var mode: ScanKind
    var body: some View {
        Picker("Chế độ quét", selection: $mode) {
            Text("Hoàn kho").tag(ScanKind.returnScan)
            Text("Đơn mới").tag(ScanKind.newOrder)
        }
        .pickerStyle(.segmented)
        .onChange(of: mode) { _ in Haptics.tap() }
    }
}
