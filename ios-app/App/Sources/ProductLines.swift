import SwiftUI

/// Danh sách sản phẩm của một đơn. Đơn hoàn MỘT PHẦN (PC gửi `rn` = tên các sản phẩm đã hoàn): giữ ĐỦ mọi sản phẩm,
/// sản phẩm đã hoàn đậm màu cam kèm nhãn "Hoàn", sản phẩm không hoàn xám kèm nhãn "Không hoàn" (luôn có chữ, không chỉ màu).
/// Không có `rn` (hoàn đủ hoặc chưa biết) thì hiển thị như thường.
struct ProductLines: View {
    let n: String
    let rn: String

    private static func split(_ s: String) -> [String] {
        s.components(separatedBy: " | ").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
    }
    private var names: [String] { Self.split(n) }
    private var returned: Set<String> { Set(Self.split(rn)) }

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            if !returned.isEmpty {
                Text("Hoàn \(returned.count)/\(names.count) sản phẩm")
                    .font(.caption.bold()).foregroundColor(.orange)
            }
            ForEach(Array(names.enumerated()), id: \.offset) { _, name in
                row(name)
            }
        }
    }

    @ViewBuilder
    private func row(_ name: String) -> some View {
        let mixed = !returned.isEmpty
        let isReturned = returned.contains(name)
        HStack(alignment: .firstTextBaseline, spacing: 6) {
            Text(name)
                .font(isReturned ? .subheadline.bold() : .subheadline)
                .foregroundColor(!mixed ? .primary : (isReturned ? .orange : .secondary))
            if mixed {
                Text(isReturned ? "Hoàn" : "Không hoàn")
                    .font(.caption2.bold())
                    .foregroundColor(isReturned ? .orange : .secondary)
            }
        }
    }
}
