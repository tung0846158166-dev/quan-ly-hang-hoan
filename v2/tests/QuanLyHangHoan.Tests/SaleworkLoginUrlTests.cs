using QuanLyHangHoan.App.Services;

namespace QuanLyHangHoan.Tests;

/// <summary>Chủ dự án 26/11/2026: nút đăng nhập Salework mở thẳng trang đăng nhập của cửa hàng.</summary>
public sealed class SaleworkLoginUrlTests
{
    [Fact]
    public void Default_login_url_is_the_shop_login_page_and_is_accepted()
    {
        Assert.Equal("https://salework.net/login/lavistudio", RealSaleworkController.DefaultLoginUrl);
        Assert.True(SaleworkPageScript.ValidateOpenUrl(RealSaleworkController.DefaultLoginUrl, out _));
    }
}
