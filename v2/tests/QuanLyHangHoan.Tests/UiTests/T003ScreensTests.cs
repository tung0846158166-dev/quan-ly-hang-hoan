using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Tests.UiTests;

public class T003ScreensTests
{
    private static readonly DateTime FixedNow = new(2026, 11, 23, 12, 0, 0);

    private sealed class StubFolderPicker(string? folderToReturn) : IFolderPickerService
    {
        public string? PickFolder(string title, string? initialDirectory = null) => folderToReturn;
    }

    [Fact]
    public void NewOrdersViewModel_FiltersSearchesSortsAndShowsEmptyState()
    {
        var provider = new FakeDataService(() => FixedNow);
        var vm = new NewOrdersViewModel(provider);

        vm.LoadOrders(
        [
            new NewOrderRecord
            {
                ScannedAt = "23/11/2026 08:00:00",
                Platform = PlatformNames.Shopee,
                Track = "SPXNEW100",
                Carrier = "SPX Express",
                Note = "Vỏ hộp móp nhẹ",
                IsSelectedForImport = true
            },
            new NewOrderRecord
            {
                ScannedAt = "23/11/2026 10:30:00",
                Platform = PlatformNames.TikTok,
                Track = "TTVNNEW200",
                Carrier = "J&T Express",
                Note = "Áo khoác gió màu đen",
                IsSelectedForImport = false
            }
        ]);

        Assert.Equal(2, vm.TotalCount);
        Assert.Equal(1, vm.SelectedForImportCount);

        vm.SelectedSortOption = vm.SortOptions.First(o => o.Key == "time_desc");
        Assert.Equal(["TTVNNEW200", "SPXNEW100"], vm.VisibleOrders.Select(r => r.Track).ToArray());

        vm.PlatformFilter = PlatformNames.Shopee;
        Assert.Single(vm.VisibleOrders);
        Assert.Equal("SPXNEW100", vm.VisibleOrders[0].Track);

        vm.PlatformFilter = "all";
        vm.SearchText = "ao khoac gio";
        Assert.Single(vm.VisibleOrders);
        Assert.Equal("TTVNNEW200", vm.VisibleOrders[0].Track);
        Assert.False(vm.IsEmpty);

        vm.SearchText = "KHONG-TON-TAI-XYZ";
        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.VisibleOrders);

        vm.ResetAllFiltersCommand.Execute(null);
        Assert.False(vm.IsEmpty);
        Assert.Equal(2, vm.VisibleOrders.Count);
    }

    [Fact]
    public void ArchivedOrdersViewModel_FiltersByPlatformSearchesAndSortsByDateAndPrice()
    {
        var provider = new FakeDataService(() => FixedNow);
        var vm = new ArchivedOrdersViewModel(provider);

        vm.LoadOrders(
        [
            new Order
            {
                OrderId = "ARC-1",
                Track = "SPXARC1",
                Platform = PlatformNames.Shopee,
                RecvDate = "20/11/2026 10:00",
                Price = 180000m,
                Status = ReceiveStatus.Received,
                Items = [new OrderItem { Product = "Sơ Mi Trắng", Color = "Trắng", Size = "M", Qty = 1 }]
            },
            new Order
            {
                OrderId = "ARC-2",
                Track = "TTVNARC2",
                Platform = PlatformNames.TikTok,
                RecvDate = "21/11/2026 15:00",
                Price = 490000m,
                Status = ReceiveStatus.Received,
                Items = [new OrderItem { Product = "Áo Len Cổ Lọ", Color = "Be", Size = "XL", Qty = 2 }]
            }
        ]);

        vm.SelectedSortOption = vm.SortOptions.First(o => o.Key == "recv_desc");
        Assert.Equal(["ARC-2", "ARC-1"], vm.VisibleOrders.Select(r => r.OrderId).ToArray());

        vm.PlatformFilter = PlatformNames.Shopee;
        Assert.Single(vm.VisibleOrders);
        Assert.Equal("ARC-1", vm.VisibleOrders[0].OrderId);

        vm.PlatformFilter = "all";
        vm.SearchText = "ao len co lo";
        Assert.Single(vm.VisibleOrders);
        Assert.Equal("ARC-2", vm.VisibleOrders[0].OrderId);
    }

    [Fact]
    public void DeletedOrdersViewModel_FiltersBySourceTablePlatformAndSearchesCleanly()
    {
        var provider = new FakeDataService(() => FixedNow);
        var vm = new DeletedOrdersViewModel(provider);

        vm.LoadOrders(
        [
            new DeletedOrderRecord
            {
                OrderId = "DEL-MAIN",
                Track = "SPXD1",
                Platform = PlatformNames.Shopee,
                DeletedFrom = "Bảng chính",
                DeletedAt = "22/11/2026 09:00",
                DeleteReason = "Tạo trùng khi nhập tay"
            },
            new DeletedOrderRecord
            {
                OrderId = "DEL-ARC",
                Track = "TTVND2",
                Platform = PlatformNames.TikTok,
                DeletedFrom = "Kho lưu trữ",
                DeletedAt = "23/11/2026 08:30",
                DeleteReason = "Đã chốt ngoài hệ thống"
            }
        ]);

        Assert.Equal(2, vm.TotalCount);
        Assert.Equal(1, vm.MainTableCount);
        Assert.Equal(1, vm.ArchiveTableCount);
        Assert.Equal(1, vm.ShopeeCount);
        Assert.Equal(1, vm.TikTokCount);

        vm.SourceFilter = "Kho lưu trữ";
        Assert.Single(vm.VisibleOrders);
        Assert.Equal("DEL-ARC", vm.VisibleOrders[0].OrderId);

        vm.SourceFilter = "all";
        vm.PlatformFilter = PlatformNames.Shopee;
        Assert.Single(vm.VisibleOrders);
        Assert.Equal("DEL-MAIN", vm.VisibleOrders[0].OrderId);

        vm.PlatformFilter = "all";
        vm.SelectedSortOption = vm.SortOptions.First(o => o.Key == "deleted_desc");
        Assert.Equal(["DEL-ARC", "DEL-MAIN"], vm.VisibleOrders.Select(r => r.OrderId).ToArray());

        vm.SearchText = "tao trung";
        Assert.Single(vm.VisibleOrders);
        Assert.Equal("DEL-MAIN", vm.VisibleOrders[0].OrderId);
    }

    [Fact]
    public void StatisticsViewModel_ComputesTotalsByPlatformStatusAndAlertsAccurately()
    {
        var fakeData = new FakeDataService(() => FixedNow);
        var vm = new StatisticsViewModel(fakeData, fakeData);

        var mainOrders = new List<Order>
        {
            new()
            {
                OrderId = "S-RED", Platform = PlatformNames.Shopee, OrderType = OrderTypeNames.ReturnRefund,
                Status = ReceiveStatus.NotReturned,
                ReturnedAt = FixedNow.AddHours(-30).ToString("dd/MM/yyyy HH:mm"), Price = 300000m
            },
            new()
            {
                OrderId = "S-YEL", Platform = PlatformNames.Shopee, OrderType = OrderTypeNames.Cancelled,
                Status = ReceiveStatus.NotReturned,
                ReturnedAt = FixedNow.AddHours(-15).ToString("dd/MM/yyyy HH:mm"), Price = 200000m
            },
            new()
            {
                OrderId = "S-REC", Platform = PlatformNames.Shopee, OrderType = OrderTypeNames.ReturnRefund,
                Status = ReceiveStatus.Received,
                ReturnedAt = FixedNow.AddHours(-20).ToString("dd/MM/yyyy HH:mm"), Price = 100000m
            },
            new()
            {
                OrderId = "T-LOST", Platform = PlatformNames.TikTok, OrderType = OrderTypeNames.Lost,
                Status = ReceiveStatus.Lost,
                ReturnedAt = FixedNow.AddDays(-3).ToString("dd/MM/yyyy HH:mm"), Price = 400000m
            },
            new()
            {
                OrderId = "T-SAFE", Platform = PlatformNames.TikTok, OrderType = OrderTypeNames.ReturnRefund,
                Status = ReceiveStatus.NotReturned,
                ReturnedAt = FixedNow.AddHours(-4).ToString("dd/MM/yyyy HH:mm"), Price = 150000m
            }
        };

        var archivedOrders = new List<Order>
        {
            new()
            {
                OrderId = "A-REC", Platform = PlatformNames.TikTok, OrderType = OrderTypeNames.ReturnRefund,
                Status = ReceiveStatus.Received,
                RecvDate = FixedNow.AddDays(-2).ToString("dd/MM/yyyy HH:mm"), Price = 250000m
            }
        };

        vm.LoadData(mainOrders, archivedOrders, FixedNow);

        // Default scope: include_archive (5 main + 1 archive = 6)
        Assert.Equal(6, vm.TotalOrders);
        Assert.Equal(2, vm.ReceivedCount);
        Assert.Equal(3, vm.PendingCount);
        Assert.Equal(1, vm.YellowAlertCount);
        Assert.Equal(1, vm.RedAlertCount);
        Assert.Equal(1, vm.LostCount);
        Assert.Equal(1_400_000m, vm.TotalValue);

        // Platform breakdown: Shopee, TikTok, TỔNG CỘNG
        Assert.Equal(3, vm.PlatformBreakdown.Count);
        var shopeeRow = vm.PlatformBreakdown.Single(r => r.Platform == PlatformNames.Shopee);
        Assert.Equal(3, shopeeRow.Total);
        Assert.Equal(1, shopeeRow.Received);
        Assert.Equal(2, shopeeRow.Pending);
        Assert.Equal(1, shopeeRow.Yellow);
        Assert.Equal(1, shopeeRow.Red);

        // Switch scope to main_only (excludes archive order)
        vm.ScopeFilter = "main_only";
        Assert.Equal(5, vm.TotalOrders);
        Assert.Equal(1, vm.ReceivedCount);
        Assert.Equal(1_150_000m, vm.TotalValue);

        // Filter platform to Shopee
        vm.PlatformFilter = PlatformNames.Shopee;
        Assert.Equal(3, vm.TotalOrders);
        Assert.Equal(1, vm.YellowAlertCount);
        Assert.Equal(1, vm.RedAlertCount);
        Assert.Single(vm.PlatformBreakdown);
    }

    [Fact]
    public async Task LegacyImportViewModel_EnablesRealImportOnlyAfterDryRunAndConfirmation()
    {
        var importer = new FakeLegacyImporter(() => FixedNow, simulatedDelayMs: 0);
        var picker = new StubFolderPicker(@"C:\FakeDemo\QuanLyHangHoan");
        var vm = new LegacyImportViewModel(importer, picker);

        Assert.False(vm.RunDryRunCommand.CanExecute(null));
        Assert.False(vm.ExecuteImportCommand.CanExecute(null));
        Assert.False(vm.HasCompletedDryRun);

        vm.BrowseFolderCommand.Execute(null);
        Assert.Equal(@"C:\FakeDemo\QuanLyHangHoan", vm.SourceFolderPath);
        Assert.True(vm.RunDryRunCommand.CanExecute(null));
        Assert.False(vm.ExecuteImportCommand.CanExecute(null));

        vm.IsConfirmed = true;
        Assert.False(vm.ExecuteImportCommand.CanExecute(null));

        await vm.RunDryRunAsync();
        Assert.True(vm.HasCompletedDryRun);
        Assert.NotNull(vm.DryRunReport);
        Assert.Equal(2_307, vm.DryRunReport!.MainOrdersCount);
        Assert.True(vm.DryRunReport.SkippedByDecision.Count > 0);
        Assert.False(vm.IsConfirmed);
        Assert.False(vm.ExecuteImportCommand.CanExecute(null));

        vm.IsConfirmed = true;
        Assert.True(vm.ExecuteImportCommand.CanExecute(null));

        await vm.ExecuteImportAsync();
        Assert.NotNull(vm.ImportResult);
        Assert.True(vm.ImportResult!.Success);
        Assert.Equal(vm.DryRunReport.MainOrdersCount, vm.ImportResult.ImportedMainOrders);

        vm.SourceFolderPath = @"C:\FakeDemo\AnotherFolder";
        Assert.False(vm.HasCompletedDryRun);
        Assert.False(vm.IsConfirmed);
        Assert.Null(vm.DryRunReport);
        Assert.Null(vm.ImportResult);
        Assert.False(vm.ExecuteImportCommand.CanExecute(null));
    }

    [Fact]
    public void MainViewModel_RemembersSelectedPageAndRowStateAcrossNavigation()
    {
        var mainVm = new MainViewModel(new FakeDataService(() => FixedNow));

        // Starts on Home
        Assert.Equal("home", mainVm.SelectedNavigationKey);
        Assert.IsType<HomeViewModel>(mainVm.CurrentPage);

        // Navigate to Shopee Return, select a row
        mainVm.NavigateByKey("shopee_return");
        var chosenShopeeRow = mainVm.ShopeeReturnVm.VisibleOrders[0];
        mainVm.ShopeeReturnVm.SelectedRow = chosenShopeeRow;

        // Navigate to Shopee Cancelled, select a row
        mainVm.NavigateByKey("shopee_cancelled");
        Assert.Equal("shopee_cancelled", mainVm.SelectedNavigationKey);
        Assert.True(mainVm.NavigationItems.Single(n => n.Key == "shopee_cancelled").IsSelected);
        Assert.False(mainVm.NavigationItems.Single(n => n.Key == "shopee_return").IsSelected);

        var chosenCancelledRow = mainVm.ShopeeCancelledVm.VisibleOrders[0];
        mainVm.ShopeeCancelledVm.SelectedRow = chosenCancelledRow;

        // Navigate to LegacyImport and set a path
        mainVm.NavigateByKey("legacy_import");
        Assert.Equal("legacy_import", mainVm.SelectedNavigationKey);
        Assert.IsType<LegacyImportViewModel>(mainVm.CurrentPage);
        mainVm.LegacyImportVm.SourceFolderPath = @"C:\Fake\Path";

        // Navigate back to Shopee Return -> selected row is still remembered
        mainVm.NavigateByKey("shopee_return");
        Assert.Same(mainVm.ShopeeReturnVm, mainVm.CurrentPage);
        Assert.Same(chosenShopeeRow, mainVm.ShopeeReturnVm.SelectedRow);

        // Navigate back to Shopee Cancelled -> selected row is still remembered
        mainVm.NavigateByKey("shopee_cancelled");
        Assert.Same(mainVm.ShopeeCancelledVm, mainVm.CurrentPage);
        Assert.Same(chosenCancelledRow, mainVm.ShopeeCancelledVm.SelectedRow);

        // Navigate back to LegacyImport -> entered path is still remembered
        mainVm.NavigateByKey("legacy_import");
        Assert.Equal(@"C:\Fake\Path", mainVm.LegacyImportVm.SourceFolderPath);
    }
}
