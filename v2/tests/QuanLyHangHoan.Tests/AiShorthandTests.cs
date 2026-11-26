using QuanLyHangHoan.Core.Ai;

namespace QuanLyHangHoan.Tests;

/// <summary>Chủ dự án 26/11/2026: hỏi AI bằng câu tắt ("đỏ sp", "về hn", "top tuần"...) thì máy tự hiểu thành câu đầy đủ.</summary>
public sealed class AiShorthandTests
{
    [Theory]
    [InlineData("đỏ", "Liệt kê các kiện đang chờ bị báo đỏ (chờ quá 24 giờ kể từ lúc hàng về).")]
    [InlineData("do sp", "Liệt kê các kiện Shopee đang chờ bị báo đỏ (chờ quá 24 giờ kể từ lúc hàng về).")]
    [InlineData("Vàng TT", "Liệt kê các kiện TikTok đang chờ bị báo vàng (chờ quá 12 giờ kể từ lúc hàng về).")]
    [InlineData("quá hạn", "Liệt kê các kiện đang chờ bị báo đỏ (chờ quá 24 giờ kể từ lúc hàng về).")]
    [InlineData("về hn", "Đếm và liệt kê các kiện đã nhận về kho hôm nay.")]
    [InlineData("bn về hn", "Có bao nhiêu kiện đã nhận về kho hôm nay?")]
    [InlineData("đơn về hôm qua", "Đếm và liệt kê các kiện đã nhận về kho hôm qua.")]
    [InlineData("chờ sp", "Đếm và liệt kê các kiện Shopee đang chờ (chưa về).")]
    [InlineData("bn chờ tiền", "Có bao nhiêu kiện đang chờ (chưa về)? Tổng giá trị bao nhiêu?")]
    [InlineData("ds chờ lz", "Liệt kê các kiện Lazada đang chờ (chưa về).")]
    [InlineData("hủy sp", "Đếm và liệt kê các đơn hủy Shopee.")]
    [InlineData("top tuần", "Sản phẩm nào hoàn nhiều nhất tuần này?")]
    [InlineData("top sp tháng", "Sản phẩm nào hoàn nhiều nhất tháng này trên Shopee?")]
    [InlineData("top", "Sản phẩm nào hoàn nhiều nhất tuần này?")]
    [InlineData("so tuần", "So sánh tuần này với tuần trước: số kiện hoàn và giá trị.")]
    [InlineData("so tháng tt", "So sánh tháng này với tháng trước trên TikTok: số kiện hoàn và giá trị.")]
    [InlineData("hãng tuần", "Đơn vị vận chuyển nào có nhiều kiện hoàn nhất tuần này?")]
    [InlineData("sàn", "Sàn nào có nhiều kiện hoàn nhất?")]
    [InlineData("tổng", "Tổng quan hiện tại: bao nhiêu kiện đang chờ, đã nhận hôm nay, báo đỏ, báo vàng và tổng giá trị đang chờ.")]
    [InlineData("tq", "Tổng quan hiện tại: bao nhiêu kiện đang chờ, đã nhận hôm nay, báo đỏ, báo vàng và tổng giá trị đang chờ.")]
    [InlineData("tìm 5678", "Tìm đơn có mã đơn hoặc mã vận đơn chứa 5678.")]
    [InlineData("SPXVN06446806070B", "Tìm đơn có mã đơn hoặc mã vận đơn chứa SPXVN06446806070B.")]
    public void Short_questions_expand_to_full_questions(string input, string expected) =>
        Assert.Equal(expected, AiShorthand.Expand(input));

    [Theory]
    [InlineData("Hôm nay có bao nhiêu đơn về?")]   // câu đầy đủ: để nguyên cho AI
    [InlineData("xin chào")]
    [InlineData("đỏ sp nhưng bỏ đơn đã lưu trữ")]   // có chữ lạ: không đoán
    [InlineData("")]
    public void Full_or_unknown_questions_are_left_alone(string input) => Assert.Null(AiShorthand.Expand(input));

    [Fact]
    public void Glossary_for_the_ai_mentions_the_main_shortcuts()
    {
        var g = AiShorthand.Glossary;
        foreach (var k in new[] { "sp", "tt", "lz", "hn", "hq", "đỏ", "vàng", "bn", "ds", "top", "so", "tq" }) Assert.Contains(k, g);
    }
}
