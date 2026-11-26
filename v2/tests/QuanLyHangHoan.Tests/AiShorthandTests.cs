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
    [InlineData("chờ spx", "Đếm và liệt kê các kiện của SPX Express đang chờ (chưa về).")]
    [InlineData("bn jt", "Có bao nhiêu kiện của J&T Express đang chờ (chưa về)?")]
    [InlineData("trễ sp", "Liệt kê các kiện Shopee đang chờ bị báo đỏ (chờ quá 24 giờ kể từ lúc hàng về).")]
    [InlineData("cho mình xem đơn đỏ shopee nhé", "Liệt kê các kiện Shopee đang chờ bị báo đỏ (chờ quá 24 giờ kể từ lúc hàng về).")]
    [InlineData("Hôm nay có bao nhiêu đơn về?", "Có bao nhiêu kiện đã nhận về kho hôm nay?")]
    [InlineData("mất", "Đếm và liệt kê các kiện bị mất.")]
    [InlineData("hỏng tuần", "Đếm và liệt kê các kiện bị hư hỏng tuần này.")]
    [InlineData("kcn", "Đếm và liệt kê các kiện không cần nhận.")]
    [InlineData("td", "Đếm và liệt kê các kiện đang theo dõi.")]
    [InlineData("lt tháng", "Đếm và liệt kê các kiện trong Kho lưu trữ tháng này.")]
    [InlineData("gtb tt", "Đếm và liệt kê các kiện TikTok giao không thành công.")]
    [InlineData("mới hn", "Đếm và liệt kê các kiện mới nạp hôm nay.")]
    [InlineData("mới", "Đếm và liệt kê các kiện mới nạp hôm nay.")]
    [InlineData("trùng", "Có đơn nào bị trùng lặp không? Liệt kê nếu có.")]
    [InlineData("nhật ký hq", "Có những hoạt động gì hôm qua (nhật ký)?")]
    [InlineData("tình hình thế nào", "Tổng quan hiện tại: bao nhiêu kiện đang chờ, đã nhận hôm nay, báo đỏ, báo vàng và tổng giá trị đang chờ.")]
    [InlineData("top spx", "Sản phẩm nào hoàn nhiều nhất tuần này qua SPX Express?")]
    [InlineData("sản phẩm hoàn nhiều nhất tháng", "Sản phẩm nào hoàn nhiều nhất tháng này?")]
    [InlineData("bn về 7n tiền", "Có bao nhiêu kiện đã nhận về kho 7 ngày qua? Tổng giá trị bao nhiêu?")]
    public void Short_questions_expand_to_full_questions(string input, string expected) =>
        Assert.Equal(expected, AiShorthand.Expand(input));

    [Theory]
    [InlineData("Đơn nào hoàn vì lý do sai size?")]
    [InlineData("xin chào")]
    [InlineData("đỏ sp nhưng bỏ đơn đã lưu trữ")]
    [InlineData("hãng spx")]
    [InlineData("")]
    public void Full_or_unknown_questions_are_left_alone(string input) => Assert.Null(AiShorthand.Expand(input));

    [Fact]
    public void Every_quick_ask_button_expands()
    {
        foreach (var q in AiShorthand.QuickAsks) Assert.NotNull(AiShorthand.Expand(q.Short));
    }
}
