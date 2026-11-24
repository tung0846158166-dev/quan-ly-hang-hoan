import Foundation

/// Một đoạn chữ trong dòng, có thể đậm / nghiêng / mã.
public struct MDInline: Equatable {
    public var text: String
    public var bold: Bool
    public var italic: Bool
    public var code: Bool
    public init(_ text: String, bold: Bool = false, italic: Bool = false, code: Bool = false) {
        self.text = text; self.bold = bold; self.italic = italic; self.code = code
    }
}

public enum MDBlock: Equatable {
    case paragraph([MDInline])
    case heading([MDInline])
    /// marker là "•" hoặc "1." (số thứ tự giữ nguyên)
    case listItem([MDInline], marker: String)
    /// rows[hàng][ô] là các đoạn chữ; hàng đầu là tiêu đề nếu hasHeader
    case table(rows: [[[MDInline]]], hasHeader: Bool)
}

/// Bộ đọc markdown rút gọn cho câu trả lời của AI: **đậm**, *nghiêng*, `mã`, tiêu đề #, danh sách - / * / 1., bảng | a | b |.
/// Chỉ phân tích chữ (không chạm giao diện) nên kiểm thử được. Không bao giờ lỗi: cú pháp lạ thì giữ nguyên chữ.
public enum ChatMarkdown {
    public static func parse(_ text: String?) -> [MDBlock] {
        guard let text = text, !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return [] }
        let lines = text.replacingOccurrences(of: "\r\n", with: "\n").replacingOccurrences(of: "\r", with: "\n")
            .components(separatedBy: "\n")
        var blocks: [MDBlock] = []
        var i = 0
        while i < lines.count {
            let line = lines[i]
            if line.trimmingCharacters(in: .whitespaces).isEmpty { i += 1; continue }

            if looksLikeTableRow(line) {
                var rows: [String] = []
                while i < lines.count && looksLikeTableRow(lines[i]) { rows.append(lines[i]); i += 1 }
                blocks.append(parseTable(rows))
                continue
            }
            let trimmed = line.trimmingCharacters(in: .whitespaces)
            if let h = headingText(trimmed) {
                blocks.append(.heading(parseInlines(h)))
            } else if let b = bulletText(trimmed) {
                blocks.append(.listItem(parseInlines(b), marker: "•"))
            } else if let (num, rest) = numbered(trimmed) {
                blocks.append(.listItem(parseInlines(rest), marker: num + "."))
            } else {
                blocks.append(.paragraph(parseInlines(trimmed)))
            }
            i += 1
        }
        return blocks
    }

    private static func looksLikeTableRow(_ line: String) -> Bool {
        let t = line.trimmingCharacters(in: .whitespaces)
        guard t.count > 1, t.first == "|" else { return false }
        return t.dropFirst().contains("|")
    }

    private static func headingText(_ t: String) -> String? {
        var n = 0
        for ch in t { if ch == "#" { n += 1 } else { break } }
        guard n >= 1, n <= 6, t.count > n else { return nil }
        let rest = t.dropFirst(n)
        guard rest.first == " " else { return nil }
        return rest.trimmingCharacters(in: .whitespaces)
    }

    private static func bulletText(_ t: String) -> String? {
        guard let f = t.first, f == "-" || f == "*" || f == "•" else { return nil }
        if t.hasPrefix("**") { return nil }   // dòng đậm, không phải gạch đầu dòng
        let rest = t.dropFirst()
        guard rest.first == " " else { return nil }
        return rest.trimmingCharacters(in: .whitespaces)
    }

    private static func numbered(_ t: String) -> (String, String)? {
        var digits = ""
        var idx = t.startIndex
        while idx < t.endIndex, t[idx].isNumber, digits.count < 3 { digits.append(t[idx]); idx = t.index(after: idx) }
        guard !digits.isEmpty, idx < t.endIndex, t[idx] == "." || t[idx] == ")" else { return nil }
        let after = t.index(after: idx)
        guard after < t.endIndex, t[after] == " " else { return nil }
        return (digits, String(t[after...]).trimmingCharacters(in: .whitespaces))
    }

    private static func isSeparatorRow(_ row: String) -> Bool {
        let cells = splitCells(row)
        guard !cells.isEmpty else { return false }
        for c in cells {
            let core = c.trimmingCharacters(in: CharacterSet(charactersIn: ": "))
            if core.count < 2 || core.contains(where: { $0 != "-" }) { return false }
        }
        return true
    }

    private static func splitCells(_ row: String) -> [String] {
        var t = row.trimmingCharacters(in: .whitespaces)
        if t.hasPrefix("|") { t.removeFirst() }
        if t.hasSuffix("|") { t.removeLast() }
        return t.components(separatedBy: "|").map { $0.trimmingCharacters(in: .whitespaces) }
    }

    private static func parseTable(_ raw: [String]) -> MDBlock {
        var rows: [[[MDInline]]] = []
        var hasHeader = false
        for (r, line) in raw.enumerated() {
            if isSeparatorRow(line) { if r == 1 { hasHeader = true }; continue }
            rows.append(splitCells(line).map { parseInlines($0) })
        }
        return .table(rows: rows, hasHeader: hasHeader && !rows.isEmpty)
    }

    public static func parseInlines(_ s: String) -> [MDInline] {
        let chars = Array(s)
        var out: [MDInline] = []
        var buf = ""
        var i = 0
        func flush() { if !buf.isEmpty { out.append(MDInline(buf)); buf = "" } }
        func findDouble(_ c: Character, from: Int) -> Int? {
            var j = from
            while j + 1 < chars.count { if chars[j] == c && chars[j + 1] == c { return j }; j += 1 }
            return nil
        }
        func findSingle(_ c: Character, from: Int) -> Int? {
            var j = from
            while j < chars.count { if chars[j] == c { return j }; j += 1 }
            return nil
        }
        while i < chars.count {
            let c = chars[i]
            if (c == "*" || c == "_"), i + 1 < chars.count, chars[i + 1] == c, let end = findDouble(c, from: i + 2), end > i + 2 {
                flush()
                out.append(MDInline(String(chars[(i + 2)..<end]), bold: true))
                i = end + 2
                continue
            }
            if c == "`", let end = findSingle("`", from: i + 1), end > i + 1 {
                flush()
                out.append(MDInline(String(chars[(i + 1)..<end]), code: true))
                i = end + 1
                continue
            }
            if c == "*", i + 1 < chars.count, !chars[i + 1].isWhitespace, chars[i + 1] != "*",
               let end = findSingle("*", from: i + 1), end > i + 1 {
                flush()
                out.append(MDInline(String(chars[(i + 1)..<end]), italic: true))
                i = end + 1
                continue
            }
            buf.append(c)
            i += 1
        }
        flush()
        if out.isEmpty { out.append(MDInline(s)) }
        return out
    }

    /// Chữ thuần (bỏ ký hiệu markdown), dùng khi sao chép.
    public static func toPlainText(_ text: String?) -> String {
        func join(_ inl: [MDInline]) -> String { inl.map { $0.text }.joined() }
        var lines: [String] = []
        for b in parse(text) {
            switch b {
            case .paragraph(let p): lines.append(join(p))
            case .heading(let h): lines.append(join(h))
            case .listItem(let l, let marker): lines.append(marker + " " + join(l))
            case .table(let rows, _): for row in rows { lines.append(row.map { join($0) }.joined(separator: "  |  ")) }
            }
        }
        return lines.joined(separator: "\n")
    }
}
