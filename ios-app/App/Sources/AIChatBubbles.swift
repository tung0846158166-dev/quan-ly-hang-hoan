import SwiftUI
import UIKit
import ScannerCore

/// Hiển thị câu trả lời của AI có định dạng (đậm, nghiêng, mã, danh sách, bảng) thay vì chữ thô có dấu **.
struct ChatMarkdownView: View {
    let markdown: String
    var textColor: Color = .primary

    var body: some View {
        let blocks = ChatMarkdown.parse(markdown)
        VStack(alignment: .leading, spacing: 6) {
            ForEach(Array(blocks.enumerated()), id: \.offset) { _, block in
                blockView(block)
            }
        }
    }

    @ViewBuilder
    private func blockView(_ block: MDBlock) -> some View {
        switch block {
        case .paragraph(let inl):
            inlineText(inl, size: 15)
        case .heading(let inl):
            inlineText(inl, size: 16, forceBold: true)
        case .listItem(let inl, let marker):
            HStack(alignment: .top, spacing: 6) {
                Text(marker).font(.system(size: 15, weight: .semibold)).foregroundColor(textColor)
                inlineText(inl, size: 15)
            }
        case .table(let rows, let hasHeader):
            tableView(rows: rows, hasHeader: hasHeader)
        }
    }

    private func inlineText(_ inl: [MDInline], size: CGFloat, forceBold: Bool = false) -> some View {
        var result = Text("")
        for piece in inl {
            result = result + styled(piece, size: size, forceBold: forceBold)
        }
        return result.foregroundColor(textColor).fixedSize(horizontal: false, vertical: true)
    }

    private func styled(_ piece: MDInline, size: CGFloat, forceBold: Bool) -> Text {
        var t = Text(piece.text)
        if piece.code {
            t = t.font(.system(size: size - 1, design: .monospaced))
        } else {
            t = t.font(.system(size: size))
        }
        if piece.bold || forceBold { t = t.bold() }
        if piece.italic { t = t.italic() }
        return t
    }

    private func tableView(rows: [[[MDInline]]], hasHeader: Bool) -> some View {
        ScrollView(.horizontal, showsIndicators: true) {
            Grid(alignment: .leading, horizontalSpacing: 0, verticalSpacing: 0) {
                ForEach(rows.indices, id: \.self) { r in
                    tableRow(cells: rows[r], isHeader: hasHeader && r == 0)
                }
            }
            .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.secondary.opacity(0.35), lineWidth: 1))
            .clipShape(RoundedRectangle(cornerRadius: 6))
        }
    }

    private func tableRow(cells: [[MDInline]], isHeader: Bool) -> some View {
        GridRow {
            ForEach(cells.indices, id: \.self) { c in
                inlineText(cells[c], size: 13, forceBold: isHeader)
                    .lineLimit(1)
                    .fixedSize()
                    .padding(.horizontal, 9)
                    .padding(.vertical, 6)
                    .background(isHeader ? Color.indigo.opacity(0.14) : Color.clear)
                    .overlay(Rectangle().stroke(Color.secondary.opacity(0.2), lineWidth: 0.5))
            }
        }
    }
}

/// Hình đại diện AI: vòng tròn chuyển màu có ✨.
struct AIAvatar: View {
    var body: some View {
        Image(systemName: "sparkles")
            .font(.system(size: 14, weight: .semibold))
            .foregroundColor(.white)
            .frame(width: 30, height: 30)
            .background(
                Circle().fill(LinearGradient(colors: [Color(red: 0.39, green: 0.40, blue: 0.95), Color(red: 0.93, green: 0.28, blue: 0.60)],
                                             startPoint: .topLeading, endPoint: .bottomTrailing))
            )
    }
}

/// Một tin nhắn: bong bóng (người dùng bên phải màu tím, AI bên trái), thẻ đơn liên quan, giờ gửi/nhận.
struct AIMessageRow: View {
    let message: AIMessage
    let onOpen: (AIOrderRef) -> Void

    var body: some View {
        HStack(alignment: .top, spacing: 8) {
            if message.isUser { Spacer(minLength: 36) } else { AIAvatar() }
            VStack(alignment: message.isUser ? .trailing : .leading, spacing: 6) {
                bubble
                if !message.refs.isEmpty { refsList }
                Text(message.caption)
                    .font(.caption2)
                    .foregroundColor(.secondary)
                    .padding(.horizontal, 4)
            }
            if message.isUser { EmptyView() } else { Spacer(minLength: 20) }
        }
        .frame(maxWidth: .infinity, alignment: message.isUser ? .trailing : .leading)
    }

    private var bubbleColor: Color {
        if message.isUser { return Color(red: 0.31, green: 0.27, blue: 0.90) }
        if message.isError { return Color.red.opacity(0.14) }
        return Color(.secondarySystemBackground)
    }

    private var textColor: Color {
        if message.isUser { return .white }
        if message.isError { return Color(red: 0.6, green: 0.1, blue: 0.1) }
        return .primary
    }

    private var bubble: some View {
        ChatMarkdownView(markdown: message.text, textColor: textColor)
            .padding(.horizontal, 12)
            .padding(.vertical, 9)
            .background(bubbleColor)
            .clipShape(RoundedRectangle(cornerRadius: 14))
            .contextMenu {
                Button {
                    UIPasteboard.general.string = ChatMarkdown.toPlainText(message.text)
                } label: {
                    Label("Sao chép", systemImage: "doc.on.doc")
                }
            }
    }

    private var refsList: some View {
        VStack(alignment: .leading, spacing: 5) {
            Text("Đơn liên quan (\(message.refs.count)) — chạm để xem chi tiết")
                .font(.caption.bold())
                .foregroundColor(.secondary)
            ForEach(message.refs) { r in
                refCard(r)
            }
        }
    }

    private func refCard(_ r: AIOrderRef) -> some View {
        Button {
            onOpen(r)
        } label: {
            HStack(alignment: .center, spacing: 8) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(r.orderCode.isEmpty ? r.track : r.orderCode)
                        .font(.system(size: 14, weight: .semibold))
                        .foregroundColor(.primary)
                    Text(r.track)
                        .font(.system(size: 11, design: .monospaced))
                        .foregroundColor(.secondary)
                }
                Spacer(minLength: 6)
                Text(r.status)
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundColor(Color(red: 0.09, green: 0.4, blue: 0.2))
                    .padding(.horizontal, 7)
                    .padding(.vertical, 3)
                    .background(Color.green.opacity(0.18))
                    .clipShape(RoundedRectangle(cornerRadius: 6))
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 8)
            .background(Color(.systemBackground))
            .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color.secondary.opacity(0.3), lineWidth: 1))
            .clipShape(RoundedRectangle(cornerRadius: 10))
        }
        .buttonStyle(.plain)
    }
}

/// Bong bóng "Đang trả lời…" khi chờ AI.
struct AITypingBubble: View {
    var body: some View {
        HStack(alignment: .top, spacing: 8) {
            AIAvatar()
            VStack(alignment: .leading, spacing: 6) {
                Text("Đang trả lời…")
                    .font(.subheadline)
                    .foregroundColor(.secondary)
                ProgressView()
                    .progressViewStyle(.linear)
                    .tint(Color.purple)
                    .frame(width: 120)
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 10)
            .background(Color(.secondarySystemBackground))
            .clipShape(RoundedRectangle(cornerRadius: 14))
            Spacer(minLength: 20)
        }
    }
}
