import SwiftUI
import ScannerCore

/// Bộ giao diện chung: màu, thẻ, ô số liệu, chip lọc.
enum Theme {
    static let accent = Color(red: 0.30, green: 0.31, blue: 0.93)
    static func modeColor(_ k: ScanKind) -> Color {
        switch k {
        case .returnScan: return Color(red: 0.30, green: 0.31, blue: 0.93)
        case .newOrder: return Color(red: 0.05, green: 0.62, blue: 0.55)
        case .cancelOrder: return Color(red: 0.86, green: 0.22, blue: 0.30)
        }
    }
}

struct CardModifier: ViewModifier {
    var padding: CGFloat = 12
    func body(content: Content) -> some View {
        content
            .padding(padding)
            .background(Color(.secondarySystemGroupedBackground))
            .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
            .shadow(color: Color.black.opacity(0.06), radius: 8, x: 0, y: 2)
    }
}

extension View {
    func card(padding: CGFloat = 12) -> some View { modifier(CardModifier(padding: padding)) }
}

/// Ô số liệu lớn (quá hạn, sắp đến hạn, đã về kho…).
struct StatTile: View {
    let title: String
    let value: Int
    let icon: String
    let color: Color
    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 6) {
                Image(systemName: icon).font(.caption.bold()).foregroundColor(color)
                Text(title).font(.caption.bold()).foregroundStyle(.secondary).lineLimit(1)
            }
            Text("\(value)").font(.system(size: 30, weight: .bold, design: .rounded)).foregroundColor(value > 0 ? color : .primary)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
    }
}

/// Nút lọc dạng viên thuốc.
struct Chip: View {
    let title: String
    let selected: Bool
    let action: () -> Void
    var body: some View {
        Button(action: action) {
            Text(title)
                .font(.footnote.bold())
                .padding(.horizontal, 12).padding(.vertical, 7)
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
    ]   // v2: BỎ tạo đơn hủy từ điện thoại (PC v2 từ chối); giá trị enum .cancelOrder vẫn giữ để đọc được lịch sử quét cũ
    var body: some View {
        HStack(spacing: 6) {
            ForEach(items, id: \.0) { item in
                let on = mode == item.0
                Button { mode = item.0 } label: {
                    HStack(spacing: 5) {
                        Image(systemName: item.2).font(.footnote)
                        Text(item.1).font(.footnote.bold())
                    }
                    .frame(maxWidth: .infinity).padding(.vertical, 9)
                    .background(on ? Theme.modeColor(item.0) : Color(.tertiarySystemFill))
                    .foregroundColor(on ? .white : .primary)
                    .clipShape(Capsule())
                }
                .buttonStyle(.plain)
            }
        }
    }
}
