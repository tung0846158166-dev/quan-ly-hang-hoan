using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Tests.UiTests;

/// <summary>Chủ dự án 26/11/2026: bộ lọc bảng đơn theo cột "Trạng thái kiện" (trạng thái trên sàn). Dữ liệu GIẢ.</summary>
public class PlatformStatusFilterTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);

    private static Order O(string id, string platformStatus) => new()
    {
        OrderId = id, Track = "T" + id, Platform = "Shopee", Status = ReceiveStatus.NotReturned, PlatformStatus = platformStatus,
        Items = [new OrderItem { Product = "Áo", Qty = 1 }],
    };

    private static ReturnOrdersViewModel Make()
    {
        var vm = new ReturnOrdersViewModel(new FakeDataService(() => Now));
        vm.LoadOrders([O("A1", "Đang về"), O("A2", "Đã về shop"), O("A3", "Đang về"), O("A4", "")], Now);
        return vm;
    }

    [Fact]
    public void Options_come_from_the_table_and_filtering_matches_exactly()
    {
        var vm = Make();
        Assert.Equal(ReturnOrdersViewModel.AllPlatformStatuses, vm.PlatformStatusOptions[0]);
        Assert.Contains("Đang về", vm.PlatformStatusOptions);
        Assert.Contains("Đã về shop", vm.PlatformStatusOptions);
        Assert.Contains(ReturnOrdersViewModel.EmptyPlatformStatus, vm.PlatformStatusOptions);

        vm.PlatformStatusFilter = "Đang về";
        Assert.Equal(new[] { "A1", "A3" }, vm.VisibleOrders.Select(o => o.OrderId).OrderBy(x => x));
        Assert.Equal(1, vm.AdvancedFilterCount);

        vm.PlatformStatusFilter = ReturnOrdersViewModel.EmptyPlatformStatus;
        Assert.Equal("A4", Assert.Single(vm.VisibleOrders).OrderId);
    }

    [Fact]
    public void Reset_clears_the_platform_status_filter()
    {
        var vm = Make();
        vm.PlatformStatusFilter = "Đã về shop";
        vm.ResetAllFiltersCommand.Execute(null);
        Assert.Equal(ReturnOrdersViewModel.AllPlatformStatuses, vm.PlatformStatusFilter);
        Assert.Equal(4, vm.VisibleOrders.Count);
    }
}
