using System.Text;
using System.Text.RegularExpressions;

namespace QuanLyHangHoan.Data.Ai;

/// <summary>Một đoạn chữ trong dòng, có thể đậm / nghiêng / mã.</summary>
public sealed record MdInline(string Text, bool Bold = false, bool Italic = false, bool Code = false);

public abstract record MdBlock;
public sealed record MdParagraph(IReadOnlyList<MdInline> Inlines) : MdBlock;
public sealed record MdHeading(IReadOnlyList<MdInline> Inlines) : MdBlock;
/// <summary>Dòng danh sách; Marker là "•" hoặc "1." (số thứ tự giữ nguyên).</summary>
public sealed record MdListItem(IReadOnlyList<MdInline> Inlines, string Marker) : MdBlock;
/// <summary>Bảng: Rows[hàng][ô] là các đoạn chữ; hàng đầu là tiêu đề nếu HasHeader.</summary>
public sealed record MdTable(IReadOnlyList<IReadOnlyList<IReadOnlyList<MdInline>>> Rows, bool HasHeader) : MdBlock;

/// <summary>
/// Bộ đọc markdown rút gọn cho câu trả lời của AI: **đậm**, *nghiêng*, `mã`, tiêu đề #, danh sách - / * / 1., bảng | a | b |.
/// Chỉ phân tích chữ (không chạm giao diện) nên kiểm thử được; giao diện PC/iPhone vẽ từ các khối này.
/// Không bao giờ ném lỗi: gặp cú pháp lạ thì giữ nguyên chữ.
/// </summary>
public static class ChatMarkdown
{
    private static readonly Regex Bullet = new(@"^\s*([-*•])\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Numbered = new(@"^\s*(\d{1,3})[.)]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^\s*#{1,6}\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex TableSeparator = new(@"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$", RegexOptions.Compiled);
    private static readonly Regex InlineToken = new(@"(\*\*(?<b>.+?)\*\*)|(__(?<b2>.+?)__)|(`(?<c>[^`]+)`)|(\*(?<i>[^*\s][^*]*?)\*)", RegexOptions.Compiled);

    public static IReadOnlyList<MdBlock> Parse(string? text)
    {
        var blocks = new List<MdBlock>();
        if (string.IsNullOrWhiteSpace(text)) return blocks;
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (LooksLikeTableRow(line))
            {
                var rows = new List<string>();
                while (i < lines.Length && LooksLikeTableRow(lines[i])) { rows.Add(lines[i]); i++; }
                i--;
                blocks.Add(ParseTable(rows));
                continue;
            }
            var h = Heading.Match(line);
            if (h.Success) { blocks.Add(new MdHeading(ParseInlines(h.Groups[1].Value))); continue; }
            var b = Bullet.Match(line);
            if (b.Success && !line.TrimStart().StartsWith("**", StringComparison.Ordinal))
            { blocks.Add(new MdListItem(ParseInlines(b.Groups[2].Value), "•")); continue; }
            var n = Numbered.Match(line);
            if (n.Success) { blocks.Add(new MdListItem(ParseInlines(n.Groups[2].Value), n.Groups[1].Value + ".")); continue; }
            blocks.Add(new MdParagraph(ParseInlines(line.Trim())));
        }
        return blocks;
    }

    private static bool LooksLikeTableRow(string line)
    {
        var t = line.Trim();
        return t.Length > 1 && t[0] == '|' && t.IndexOf('|', 1) > 0;
    }

    private static MdTable ParseTable(List<string> rawRows)
    {
        var rows = new List<IReadOnlyList<IReadOnlyList<MdInline>>>();
        var hasHeader = false;
        for (var r = 0; r < rawRows.Count; r++)
        {
            if (TableSeparator.IsMatch(rawRows[r])) { if (r == 1) hasHeader = true; continue; }
            var cells = SplitCells(rawRows[r]).Select(c => (IReadOnlyList<MdInline>)ParseInlines(c)).ToList();
            rows.Add(cells);
        }
        return new MdTable(rows, hasHeader && rows.Count > 0);
    }

    private static List<string> SplitCells(string row)
    {
        var t = row.Trim();
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|')) t = t[..^1];
        return t.Split('|').Select(c => c.Trim()).ToList();
    }

    public static IReadOnlyList<MdInline> ParseInlines(string text)
    {
        var list = new List<MdInline>();
        var pos = 0;
        foreach (Match m in InlineToken.Matches(text))
        {
            if (m.Index > pos) list.Add(new MdInline(text[pos..m.Index]));
            if (m.Groups["b"].Success) list.Add(new MdInline(m.Groups["b"].Value, Bold: true));
            else if (m.Groups["b2"].Success) list.Add(new MdInline(m.Groups["b2"].Value, Bold: true));
            else if (m.Groups["c"].Success) list.Add(new MdInline(m.Groups["c"].Value, Code: true));
            else if (m.Groups["i"].Success) list.Add(new MdInline(m.Groups["i"].Value, Italic: true));
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) list.Add(new MdInline(text[pos..]));
        if (list.Count == 0) list.Add(new MdInline(text));
        return list;
    }

    /// <summary>Chữ thuần (bỏ ký hiệu markdown), dùng khi sao chép hoặc xem trước.</summary>
    public static string ToPlainText(string? text)
    {
        var sb = new StringBuilder();
        foreach (var b in Parse(text))
        {
            switch (b)
            {
                case MdParagraph p: sb.AppendLine(Join(p.Inlines)); break;
                case MdHeading h: sb.AppendLine(Join(h.Inlines)); break;
                case MdListItem l: sb.AppendLine(l.Marker + " " + Join(l.Inlines)); break;
                case MdTable t:
                    foreach (var row in t.Rows) sb.AppendLine(string.Join("  |  ", row.Select(Join)));
                    break;
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static string Join(IEnumerable<MdInline> inl) => string.Concat(inl.Select(i => i.Text));
}
