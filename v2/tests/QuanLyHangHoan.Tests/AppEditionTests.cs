using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.Tests;

/// <summary>
/// Chủ dự án 26/11/2026: hai bộ cài — máy CHÍNH (đầy đủ) và máy PHỤ (bỏ "Tạo mã ghép" + "Thiết bị đã ghép" trong Cài đặt).
/// Dựng bản máy phụ bằng: dotnet publish -p:Edition=Secondary.
/// </summary>
[Collection("AppEdition")]   // đổi cờ dùng chung: không chạy song song với test khác cùng nhóm
public sealed class AppEditionTests
{
    [Fact]
    public void Main_edition_owner_sees_pairing_code_and_devices()
    {
        var old = AppEdition.IsSecondary;
        try
        {
            AppEdition.IsSecondary = false;
            var link = new FakeCloudLink();
            link.Set(CloudState.Connected, role: "owner", deviceName: "Máy chính");
            var vm = new CloudStatusViewModel(link, new WriteGate());
            Assert.True(vm.ShowOwnerTools);
            Assert.DoesNotContain("máy phụ", AppEdition.TitleSuffix);
        }
        finally { AppEdition.IsSecondary = old; }
    }

    [Fact]
    public void Secondary_edition_never_shows_pairing_code_or_devices_even_for_owner()
    {
        var old = AppEdition.IsSecondary;
        try
        {
            AppEdition.IsSecondary = true;
            var link = new FakeCloudLink();
            link.Set(CloudState.Connected, role: "owner", deviceName: "Máy phụ");
            var vm = new CloudStatusViewModel(link, new WriteGate());
            Assert.False(vm.ShowOwnerTools);
            Assert.Contains("máy phụ", AppEdition.TitleSuffix);
        }
        finally { AppEdition.IsSecondary = old; }
    }

    [Fact]
    public void Staff_never_sees_owner_tools()
    {
        var link = new FakeCloudLink();
        link.Set(CloudState.Connected, role: "staff");
        Assert.False(new CloudStatusViewModel(link, new WriteGate()).ShowOwnerTools);
    }
}
