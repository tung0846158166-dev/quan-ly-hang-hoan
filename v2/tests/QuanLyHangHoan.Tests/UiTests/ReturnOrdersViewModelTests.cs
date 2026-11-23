using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Tests.UiTests;

public class ReturnOrdersViewModelTests
{
    private static readonly DateTime FixedNow = new(2026, 11, 23, 12, 0, 0);

    private static ReturnOrdersViewModel CreateViewModelWithSamples(IReadOnlyList<Order>? customOrders = null)
    {
        var provider = new FakeDataService(() => FixedNow);
        var vm = new ReturnOrdersViewModel(provider);
        if (customOrders is not null)
        {
            vm.LoadOrders(customOrders, FixedNow);
        }
        return vm;
    }

    [Theory]
    [InlineData(6, ReceiveStatus.NotReturned, AlertLevel.Safe)]
    [InlineData(12, ReceiveStatus.NotReturned, AlertLevel.Yellow)]
    [InlineData(18, ReceiveStatus.NotReturned, AlertLevel.Yellow)]
    [InlineData(24, ReceiveStatus.NotReturned, AlertLevel.Red)]
    [InlineData(36, ReceiveStatus.NotReturned, AlertLevel.Red)]
    [InlineData(48, ReceiveStatus.Received, AlertLevel.Done)]
    [InlineData(48, ReceiveStatus.NotNeeded, AlertLevel.Done)]
    public void AlertThresholds_ClassifyYellow12hAndRed24hCorrectly(double hoursSinceArrived, string status, AlertLevel expected)
    {
        var order = new Order
        {
            OrderId = "2611TEST0001",
            Track = "SPXTEST0001",
            Platform = "Shopee",
            Status = status,
            ReturnedAt = FixedNow.AddHours(-hoursSinceArrived).ToString("dd/MM/yyyy HH:mm")
        };

        var level = AlertThresholds.EvaluateOrder(order, FixedNow);
        Assert.Equal(expected, level);
    }

    [Fact]
    public void PlatformFilter_FiltersShopeeTikTokAndAllCorrectly()
    {
        var vm = CreateViewModelWithSamples(
        [
            new Order { OrderId = "SHP-1", Track = "SPX1", Platform = "Shopee", Status = ReceiveStatus.NotReturned },
            new Order { OrderId = "SHP-2", Track = "SPX2", Platform = "Shopee", Status = ReceiveStatus.Received },
            new Order { OrderId = "TT-1", Track = "TTVN1", Platform = PlatformNames.TikTok, Status = ReceiveStatus.NotReturned }
        ]);

        Assert.Equal(3, vm.VisibleOrders.Count);

        vm.PlatformFilter = "Shopee";
        Assert.Equal(2, vm.VisibleOrders.Count);
        Assert.All(vm.VisibleOrders, r => Assert.Equal("Shopee", r.Platform));

        vm.PlatformFilter = PlatformNames.TikTok;
        Assert.Single(vm.VisibleOrders);
        Assert.Equal("TT-1", vm.VisibleOrders[0].OrderId);

        vm.PlatformFilter = "all";
        Assert.Equal(3, vm.VisibleOrders.Count);
    }

    [Fact]
    public void StatusAndAlertFilter_SeparatesYellowAndRedAlerts()
    {
        var vm = CreateViewModelWithSamples(
        [
            new Order
            {
                OrderId = "RED-1", Track = "SPXRED1", Platform = "Shopee", Status = ReceiveStatus.NotReturned,
                ReturnedAt = FixedNow.AddHours(-26).ToString("dd/MM/yyyy HH:mm")
            },
            new Order
            {
                OrderId = "YEL-1", Track = "SPXYEL1", Platform = "Shopee", Status = ReceiveStatus.NotReturned,
                ReturnedAt = FixedNow.AddHours(-15).ToString("dd/MM/yyyy HH:mm")
            },
            new Order
            {
                OrderId = "SAFE-1", Track = "SPXSAFE1", Platform = "Shopee", Status = ReceiveStatus.NotReturned,
                ReturnedAt = FixedNow.AddHours(-3).ToString("dd/MM/yyyy HH:mm")
            },
            new Order
            {
                OrderId = "DONE-1", Track = "SPXDONE1", Platform = "Shopee", Status = ReceiveStatus.Received,
                ReturnedAt = FixedNow.AddHours(-40).ToString("dd/MM/yyyy HH:mm")
            }
        ]);

        Assert.Equal(1, vm.RedAlertCount);
        Assert.Equal(1, vm.YellowAlertCount);

        vm.StatusFilter = "red";
        Assert.Single(vm.VisibleOrders);
        Assert.Equal("RED-1", vm.VisibleOrders[0].OrderId);
        Assert.Equal(AlertLevel.Red, vm.VisibleOrders[0].AlertLevel);

        vm.StatusFilter = "yellow";
        Assert.Single(vm.VisibleOrders);
        Assert.Equal("YEL-1", vm.VisibleOrders[0].OrderId);
        Assert.Equal(AlertLevel.Yellow, vm.VisibleOrders[0].AlertLevel);
    }

    [Fact]
    public void Search_MatchesTrackingAndDiacriticInsensitiveVietnamese_AndSetsEmptyState()
    {
        var vm = CreateViewModelWithSamples(
        [
            new Order
            {
                OrderId = "2611AAA111", ReturnTrack = "SPX99887766", ForwardTrack = "FWD111", Platform = "Shopee",
                Status = ReceiveStatus.NotReturned,
                Items = [new OrderItem { Product = "Áo Khoác Gió 2 Lớp", Color = "Đen", Size = "XL", Qty = 1 }]
            },
            new Order
            {
                OrderId = "5789BBB222", ReturnTrack = "TTVN554433", Platform = PlatformNames.TikTok,
                Status = ReceiveStatus.NotReturned,
                Items = [new OrderItem { Product = "Quần Jeans Ống Suông", Color = "Xanh Đen", Size = "31", Qty = 1 }]
            }
        ]);

        vm.SearchText = "ao khoac gio";
        Assert.Single(vm.VisibleOrders);
        Assert.Equal("2611AAA111", vm.VisibleOrders[0].OrderId);
        Assert.False(vm.IsEmpty);

        vm.SearchText = "ttvn554433";
        Assert.Single(vm.VisibleOrders);
        Assert.Equal("5789BBB222", vm.VisibleOrders[0].OrderId);

        vm.SearchText = "MA-KHONG-TON-TAI-999999";
        Assert.Empty(vm.VisibleOrders);
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public void Sorting_ByAlertDesc_PlacesRedBeforeYellowBeforeSafe()
    {
        var vm = CreateViewModelWithSamples(
        [
            new Order
            {
                OrderId = "SAFE", Track = "T1", Platform = "Shopee", Status = ReceiveStatus.NotReturned,
                ReturnedAt = FixedNow.AddHours(-2).ToString("dd/MM/yyyy HH:mm")
            },
            new Order
            {
                OrderId = "YELLOW", Track = "T2", Platform = "Shopee", Status = ReceiveStatus.NotReturned,
                ReturnedAt = FixedNow.AddHours(-14).ToString("dd/MM/yyyy HH:mm")
            },
            new Order
            {
                OrderId = "RED", Track = "T3", Platform = "Shopee", Status = ReceiveStatus.NotReturned,
                ReturnedAt = FixedNow.AddHours(-30).ToString("dd/MM/yyyy HH:mm")
            }
        ]);

        vm.SelectedSortOption = vm.SortOptions.First(o => o.Key == "alert_desc");
        Assert.Equal(["RED", "YELLOW", "SAFE"], vm.VisibleOrders.Select(r => r.OrderId).ToArray());
    }

    [Fact]
    public async Task LoadStress50k_LoadsAndFilters50000FakeRowsCleanly()
    {
        var vm = CreateViewModelWithSamples();
        await vm.LoadFakeBatchAsync(50_000);

        Assert.Equal(50_000, vm.TotalCount);
        Assert.Equal(50_000, vm.VisibleOrders.Count);
        Assert.True(vm.RedAlertCount > 0);
        Assert.True(vm.YellowAlertCount > 0);

        vm.PlatformFilter = "Shopee";
        Assert.All(vm.VisibleOrders, r => Assert.Equal("Shopee", r.Platform));
        Assert.True(vm.VisibleOrders.Count > 20_000);
    }

    [Fact]
    public void MainViewModel_SwitchesAcrossAll11SidebarPages()
    {
        var mainVm = new MainViewModel(new FakeDataService(() => FixedNow));

        Assert.Equal(20, mainVm.NavigationItems.Count);   // + "Nhật ký hoạt động" + "Cài đặt" + "Trợ giúp"
        Assert.IsType<HomeViewModel>(mainVm.CurrentPage);

        mainVm.NavigateByKey("return_watch");
        Assert.IsType<ReturnWatchViewModel>(mainVm.CurrentPage);

        mainVm.NavigateByKey("file_import");
        Assert.IsType<FileImportViewModel>(mainVm.CurrentPage);

        mainVm.NavigateByKey("folder_watch");
        Assert.IsType<FolderWatchViewModel>(mainVm.CurrentPage);

        mainVm.NavigateByKey("google_sheet");
        Assert.IsType<GoogleSheetViewModel>(mainVm.CurrentPage);

        mainVm.NavigateByKey("salework");
        Assert.IsType<SaleworkViewModel>(mainVm.CurrentPage);

        mainVm.NavigateByKey("scan_receive");
        Assert.IsType<ScanReceiveViewModel>(mainVm.CurrentPage);

        mainVm.NavigateByKey("phone_sync");
        Assert.IsType<PhoneSyncViewModel>(mainVm.CurrentPage);

        mainVm.NavigateByKey("shopee_return");
        Assert.IsType<ReturnOrdersViewModel>(mainVm.CurrentPage);

        mainVm.NavigateByKey("shopee_cancelled");
        Assert.IsType<ReturnOrdersViewModel>(mainVm.CurrentPage);

        mainVm.NavigateByKey("tiktok_return");
        Assert.IsType<ReturnOrdersViewModel>(mainVm.CurrentPage);

        mainVm.NavigateByKey("archive");
        Assert.IsType<ArchivedOrdersViewModel>(mainVm.CurrentPage);

        mainVm.NavigateByKey("new_orders");
        Assert.IsType<NewOrdersViewModel>(mainVm.CurrentPage);

        mainVm.NavigateByKey("stats");
        Assert.IsType<StatisticsViewModel>(mainVm.CurrentPage);

        mainVm.NavigateByKey("deleted");
        Assert.IsType<DeletedOrdersViewModel>(mainVm.CurrentPage);

        mainVm.NavigateByKey("legacy_import");
        Assert.IsType<LegacyImportViewModel>(mainVm.CurrentPage);

        mainVm.NavigateByKey("home");
        Assert.IsType<HomeViewModel>(mainVm.CurrentPage);
    }
}
