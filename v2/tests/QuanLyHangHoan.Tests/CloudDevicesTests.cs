using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Data.Cloud;

namespace QuanLyHangHoan.Tests;

/// <summary>Chủ cửa hàng xem các máy đã ghép và THU HỒI máy mất (điện thoại giờ tự ghép bằng mật khẩu cửa hàng). Dữ liệu GIẢ.</summary>
public sealed class CloudDevicesTests
{
    private sealed class FakeConfirm(bool answer) : IConfirmationService
    {
        public int Calls;
        public bool Confirm(string title, string message) { Calls++; return answer; }
    }

    private static CloudStatusViewModel Vm(FakeCloudLink link, IConfirmationService confirm) => new(link, new WriteGate(), confirm);

    [Fact]
    public async Task Owner_sees_devices_and_can_revoke_after_confirming()
    {
        var link = new FakeCloudLink();
        link.Set(CloudState.Connected, role: "owner", deviceName: "Máy chủ");
        link.Devices.AddRange(new[]
        {
            new CloudDeviceInfo("d1", "Máy chủ", "pc", "owner", DateTimeOffset.Now.ToUnixTimeMilliseconds(), false),
            new CloudDeviceInfo("d2", "iPhone kho 1", "phone", "staff", 0, false),
            new CloudDeviceInfo("d3", "Máy cũ", "phone", "staff", 0, true),
        });
        var confirm = new FakeConfirm(true);
        var vm = Vm(link, confirm);
        await vm.RefreshDevicesAsync();
        Assert.Equal(3, vm.Devices.Count);
        var phone = vm.Devices.Single(d => d.DeviceId == "d2");
        Assert.True(phone.CanRevoke);
        Assert.False(vm.Devices.Single(d => d.DeviceId == "d1").CanRevoke);   // không thu hồi chính máy chủ
        Assert.False(vm.Devices.Single(d => d.DeviceId == "d3").CanRevoke);   // đã thu hồi
        Assert.Contains("Đã thu hồi", vm.Devices.Single(d => d.DeviceId == "d3").StatusText);

        await vm.RevokeDeviceAsync(phone);
        Assert.Equal(1, confirm.Calls);
        Assert.Equal(new[] { "d2" }, link.Revoked);
        Assert.True(vm.Devices.Single(d => d.DeviceId == "d2").Revoked);
    }

    [Fact]
    public async Task Cancelling_the_confirmation_revokes_nothing_and_staff_sees_no_list()
    {
        var link = new FakeCloudLink();
        link.Set(CloudState.Connected, role: "owner");
        link.Devices.Add(new CloudDeviceInfo("d2", "iPhone kho 1", "phone", "staff", 0, false));
        var vm = Vm(link, new FakeConfirm(false));
        await vm.RefreshDevicesAsync();
        await vm.RevokeDeviceAsync(vm.Devices[0]);
        Assert.Empty(link.Revoked);

        var staff = new FakeCloudLink();
        staff.Set(CloudState.Connected, role: "staff");
        staff.Devices.Add(new CloudDeviceInfo("x", "y", "phone", "staff", 0, false));
        var svm = Vm(staff, new FakeConfirm(true));
        await svm.RefreshDevicesAsync();
        Assert.Empty(svm.Devices);
    }
}
