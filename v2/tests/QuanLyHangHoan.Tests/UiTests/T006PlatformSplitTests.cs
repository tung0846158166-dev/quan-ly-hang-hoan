using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Tests.UiTests;

public class T006PlatformSplitTests
{
    private static readonly DateTime FixedNow = new(2026, 11, 23, 12, 0, 0);

    [Fact]
    public void PlatformSplit_SeparatesShopeeReturn_ShopeeCancelled_AndTikTok_WithoutOverlapOrMissingOrders()
    {
        var provider = new FakeDataService(() => FixedNow);
        var allOrders = provider.GetReturnOrders(200);

        var shopeeReturnVm = new ReturnOrdersViewModel(provider, ReturnTableScope.ShopeeReturn);
        var shopeeCancelledVm = new ReturnOrdersViewModel(provider, ReturnTableScope.ShopeeCancelled);
        var tiktokReturnVm = new ReturnOrdersViewModel(provider, ReturnTableScope.TikTokReturn);

        shopeeReturnVm.LoadOrders(allOrders, FixedNow);
        shopeeCancelledVm.LoadOrders(allOrders, FixedNow);
        tiktokReturnVm.LoadOrders(allOrders, FixedNow);

        // No platform filter inside split tables
        Assert.False(shopeeReturnVm.ShowPlatformFilter);
        Assert.False(shopeeCancelledVm.ShowPlatformFilter);
        Assert.False(tiktokReturnVm.ShowPlatformFilter);

        // Shopee Hoàn hàng = Shopee && OrderType != "Đơn hủy" (gồm Trả hàng/Hoàn tiền & Giao không thành công)
        Assert.All(shopeeReturnVm.VisibleOrders, r =>
        {
            Assert.Equal(PlatformNames.Shopee, r.Platform);
            Assert.NotEqual(OrderTypeNames.Cancelled, r.OrderType);
        });
        Assert.Contains(shopeeReturnVm.VisibleOrders, r => r.OrderType == OrderTypeNames.ReturnRefund);
        Assert.Contains(shopeeReturnVm.VisibleOrders, r => r.OrderType == OrderTypeNames.FailedDelivery);

        // Shopee Đơn hủy = Shopee && OrderType == "Đơn hủy"
        Assert.All(shopeeCancelledVm.VisibleOrders, r =>
        {
            Assert.Equal(PlatformNames.Shopee, r.Platform);
            Assert.Equal(OrderTypeNames.Cancelled, r.OrderType);
        });

        // TikTok Hoàn hàng = toàn bộ TikTok, đủ 4 loại đơn
        Assert.All(tiktokReturnVm.VisibleOrders, r => Assert.Equal(PlatformNames.TikTok, r.Platform));
        Assert.Contains(tiktokReturnVm.VisibleOrders, r => r.OrderType == OrderTypeNames.Lost);
        Assert.Contains(tiktokReturnVm.VisibleOrders, r => r.OrderType == OrderTypeNames.ReturnRefund);
        Assert.Contains(tiktokReturnVm.VisibleOrders, r => r.OrderType == OrderTypeNames.FailedDelivery);
        Assert.Contains(tiktokReturnVm.VisibleOrders, r => r.OrderType == OrderTypeNames.RefundOnly);

        // Tổng 3 bảng = tổng dữ liệu giả, không có đơn nào nằm ở 2 bảng
        var ids1 = shopeeReturnVm.VisibleOrders.Select(r => r.Source.Id).ToHashSet();
        var ids2 = shopeeCancelledVm.VisibleOrders.Select(r => r.Source.Id).ToHashSet();
        var ids3 = tiktokReturnVm.VisibleOrders.Select(r => r.Source.Id).ToHashSet();

        Assert.Empty(ids1.Intersect(ids2));
        Assert.Empty(ids1.Intersect(ids3));
        Assert.Empty(ids2.Intersect(ids3));
        Assert.Equal(allOrders.Count, ids1.Count + ids2.Count + ids3.Count);
    }

    [Fact]
    public void TikTokTable_OrderTypeChips_FilterEachTypeAccurately()
    {
        var provider = new FakeDataService(() => FixedNow);
        var vm = new ReturnOrdersViewModel(provider, ReturnTableScope.TikTokReturn);
        vm.LoadOrders(provider.GetReturnOrders(200), FixedNow);

        Assert.True(vm.ShowOrderTypeFilter);

        foreach (var orderType in OrderTypeNames.TikTokTypes)
        {
            vm.OrderTypeFilter = orderType;
            Assert.NotEmpty(vm.VisibleOrders);
            Assert.All(vm.VisibleOrders, r => Assert.Equal(orderType, r.OrderType));
        }

        vm.OrderTypeFilter = "all";
        Assert.Equal(vm.TotalCount, vm.VisibleOrders.Count);
    }

    [Fact]
    public void MainViewModel_StartsOnHome_AndSidebarMatchesT006Structure()
    {
        var mainVm = new MainViewModel(new FakeDataService(() => FixedNow));

        Assert.Equal("home", mainVm.SelectedNavigationKey);
        Assert.IsType<HomeViewModel>(mainVm.CurrentPage);

        var keys = mainVm.NavigationItems.Select(n => n.Key).ToArray();
        Assert.Equal(
        [
            "home",
            "shopee_return",
            "shopee_cancelled",
            "tiktok_return",
            "file_import",
            "folder_watch",
            "google_sheet",
            "salework",
            "scan_receive",
            "phone_sync",
            "return_watch",
            "new_orders",
            "watchlist",
            "archive",
            "deleted",
            "stats",
            "legacy_import",
            "activity",
            "settings",
            "help"
        ], keys);
    }

    [Fact]
    public void HomeViewModel_ComputesKpis_YellowRedThresholds_CsvAndEmptyState()
    {
        var provider = new FakeDataService(() => FixedNow);
        var homeVm = new HomeViewModel(provider);

        var orders = new List<Order>
        {
            new()
            {
                Id = "1", OrderId = "SHP-RED", Track = "SPX1", Platform = PlatformNames.Shopee,
                OrderType = OrderTypeNames.ReturnRefund, Status = ReceiveStatus.NotReturned,
                ReturnedAt = FixedNow.AddHours(-25).ToString("dd/MM/yyyy HH:mm"),
                ReturnDate = FixedNow.AddDays(-1).ToString("dd/MM/yyyy HH:mm"),
                Price = 300_000m, Note = "Khách đổi ý"
            },
            new()
            {
                Id = "2", OrderId = "SHP-YEL", Track = "SPX2", Platform = PlatformNames.Shopee,
                OrderType = OrderTypeNames.Cancelled, Status = ReceiveStatus.NotReturned,
                ReturnedAt = FixedNow.AddHours(-13).ToString("dd/MM/yyyy HH:mm"),
                ReturnDate = FixedNow.ToString("dd/MM/yyyy HH:mm"),
                Price = 200_000m, Note = "Hủy sau chuẩn bị"
            },
            new()
            {
                Id = "3", OrderId = "TT-TODAY", Track = "TT1", Platform = PlatformNames.TikTok,
                OrderType = OrderTypeNames.Lost, Status = ReceiveStatus.Received,
                ReturnedAt = FixedNow.AddHours(-8).ToString("dd/MM/yyyy HH:mm"),
                RecvDate = FixedNow.AddHours(-2).ToString("dd/MM/yyyy HH:mm"),
                ReturnDate = FixedNow.ToString("dd/MM/yyyy HH:mm"),
                Price = 500_000m, Note = "Giao thất bại"
            }
        };

        homeVm.LoadData(orders, FixedNow);

        Assert.False(homeVm.IsEmpty);
        Assert.Equal(2, homeVm.OverallKpi.PendingCount);
        Assert.Equal(1, homeVm.OverallKpi.ReceivedTodayCount);
        Assert.Equal(1, homeVm.OverallKpi.YellowAlertCount);
        Assert.Equal(1, homeVm.OverallKpi.RedAlertCount);
        Assert.Equal(500_000m, homeVm.OverallKpi.PendingTotalValue);

        Assert.Equal(2, homeVm.UrgentItems.Count);
        Assert.Equal("SHP-RED", homeVm.UrgentItems[0].OrderId);
        Assert.Equal(AlertLevel.Red, homeVm.UrgentItems[0].AlertLevel);
        Assert.Equal("SHP-YEL", homeVm.UrgentItems[1].OrderId);
        Assert.Equal(AlertLevel.Yellow, homeVm.UrgentItems[1].AlertLevel);

        var csv = homeVm.BuildCsvSummary();
        Assert.Contains("SHP-RED", csv);
        Assert.Contains("TT-TODAY", csv);

        homeVm.LoadData(Array.Empty<Order>(), FixedNow);
        Assert.True(homeVm.IsEmpty);
        Assert.Empty(homeVm.UrgentItems);
    }

    [Fact]
    public void HomeViewModel_OpenUrgentOrder_NavigatesToTargetPlatformTableAndSelectsRow()
    {
        var mainVm = new MainViewModel(new FakeDataService(() => FixedNow));
        var urgent = mainVm.HomeVm.UrgentItems.First(u => u.TargetNavKey == "tiktok_return");

        mainVm.HomeVm.OpenUrgentOrderCommand.Execute(urgent);

        Assert.Equal("tiktok_return", mainVm.SelectedNavigationKey);
        Assert.Same(mainVm.TikTokReturnVm, mainVm.CurrentPage);
        Assert.NotNull(mainVm.TikTokReturnVm.SelectedRow);
        Assert.Equal(urgent.OrderId, mainVm.TikTokReturnVm.SelectedRow!.OrderId);
    }
}
