using QuanLyHangHoan.Data.Ai;

namespace QuanLyHangHoan.Tests;

/// <summary>Đọc markdown trong câu trả lời của AI: đậm, nghiêng, mã, danh sách, bảng, tiêu đề.</summary>
public sealed class ChatMarkdownTests
{
    [Fact]
    public void Bold_italic_and_code_are_split_into_inlines()
    {
        var p = Assert.IsType<MdParagraph>(Assert.Single(ChatMarkdown.Parse("Hôm nay **có 1 kiện** về, *chưa* kiểm `VN001` xong")));
        Assert.Equal(new[] { "Hôm nay ", "có 1 kiện", " về, ", "chưa", " kiểm ", "VN001", " xong" }, p.Inlines.Select(i => i.Text).ToArray());
        Assert.True(p.Inlines[1].Bold);
        Assert.True(p.Inlines[3].Italic);
        Assert.True(p.Inlines[5].Code);
    }

    [Fact]
    public void Table_from_the_real_answer_becomes_a_header_row_and_data_row()
    {
        var md = "**Hôm nay (25/11/2026) có 1 kiện hàng đã về kho**:\n\n| Mã đơn | Mã vận đơn |\n|--------|------------|\n| 261109RYQX93M3 | 01M6ZHRV754AD0DQNEGV3TXGS8 |\n\nBạn có thể bấm vào mã đơn.";
        var blocks = ChatMarkdown.Parse(md);
        Assert.Equal(3, blocks.Count);
        Assert.IsType<MdParagraph>(blocks[0]);
        var t = Assert.IsType<MdTable>(blocks[1]);
        Assert.True(t.HasHeader);
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal("Mã đơn", t.Rows[0][0][0].Text);
        Assert.Equal("261109RYQX93M3", t.Rows[1][0][0].Text);
        Assert.Equal("01M6ZHRV754AD0DQNEGV3TXGS8", t.Rows[1][1][0].Text);
        Assert.IsType<MdParagraph>(blocks[2]);
    }

    [Fact]
    public void Lists_headings_and_plain_lines()
    {
        var blocks = ChatMarkdown.Parse("## Kết quả\n- Áo thun: 3\n* Quần jean: 1\n1. Một\n2) Hai\nDòng thường");
        Assert.IsType<MdHeading>(blocks[0]);
        Assert.Equal("•", Assert.IsType<MdListItem>(blocks[1]).Marker);
        Assert.Equal("•", Assert.IsType<MdListItem>(blocks[2]).Marker);
        Assert.Equal("1.", Assert.IsType<MdListItem>(blocks[3]).Marker);
        Assert.Equal("2.", Assert.IsType<MdListItem>(blocks[4]).Marker);
        Assert.IsType<MdParagraph>(blocks[5]);
    }

    [Fact]
    public void Bold_line_starting_with_stars_is_not_mistaken_for_a_bullet()
    {
        var blocks = ChatMarkdown.Parse("**Tổng cộng**: 5 kiện");
        var p = Assert.IsType<MdParagraph>(Assert.Single(blocks));
        Assert.True(p.Inlines[0].Bold);
    }

    [Fact]
    public void Odd_or_empty_input_never_throws_and_plain_text_drops_the_marks()
    {
        Assert.Empty(ChatMarkdown.Parse(null));
        Assert.Empty(ChatMarkdown.Parse("   \n  "));
        Assert.Single(ChatMarkdown.Parse("**chưa đóng và | lạ"));
        Assert.Equal("Hôm nay có 1 kiện\n• một\nMã đơn  |  Mã vận đơn\nA  |  B".Replace("\n", Environment.NewLine),
            ChatMarkdown.ToPlainText("Hôm nay **có 1 kiện**\n- một\n| Mã đơn | Mã vận đơn |\n|---|---|\n| A | B |"));
    }
}
