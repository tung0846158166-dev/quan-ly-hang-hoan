using System.IO;
using System.Windows;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Tests.UiTests;

/// <summary>Thống kê sản phẩm trên màn "Thống kê &amp; báo cáo": dùng cùng bộ lọc, chọn sản phẩm thì hiện màu/size. Dữ liệu GIẢ.</summary>
public sealed class T034ProductStatsUiTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 12, 0, 0);

    private static StatisticsViewModel NewVm()
    {
        var d = new FakeDataService(clock: () => Now, enableMutableDemoStore: true);
        return new StatisticsViewModel(d, d);
    }

    [Fact]
    public void Product_rows_are_built_from_the_filtered_orders_ranked_by_units_with_shares_adding_up()
    {
        var vm = NewVm();
        Assert.True(vm.HasProducts);
        Assert.Equal(Enumerable.Range(1, vm.ProductRows.Count), vm.ProductRows.Select(r => r.Rank));
        Assert.Equal(vm.ProductRows.OrderByDescending(r => r.Units).Select(r => r.Units), vm.ProductRows.Select(r => r.Units));
        Assert.InRange(vm.ProductRows.Sum(r => r.SharePercent), 99.0, 100.01);               // ít hơn 100 chỉ khi bị cắt ở 100 dòng
        Assert.Contains("sản phẩm", vm.ProductSummaryText);
        Assert.Contains("cái bị hoàn", vm.ProductSummaryText);
    }

    [Fact]
    public void Selecting_a_product_shows_its_color_size_breakdown_and_clearing_hides_it()
    {
        var vm = NewVm();
        Assert.False(vm.HasSelectedProduct);
        Assert.Empty(vm.VariantRows);
        vm.SelectedProduct = vm.ProductRows[0];
        Assert.True(vm.HasSelectedProduct);
        Assert.NotEmpty(vm.VariantRows);
        Assert.Equal(vm.ProductRows[0].Units, vm.VariantRows.Sum(v => v.Units));              // màu/size cộng lại đúng tổng số cái
        Assert.Contains(vm.ProductRows[0].Name, vm.VariantTitle);
        vm.SelectedProduct = null;
        Assert.Empty(vm.VariantRows);
    }

    [Fact]
    public void Product_list_follows_the_same_filters_as_the_rest_of_the_screen()
    {
        var vm = NewVm();
        var all = vm.ProductRows.Sum(r => r.Units);
        vm.PlatformFilter = PlatformNames.TikTok;
        vm.Recalculate();
        var tiktokOnly = vm.ProductRows.Sum(r => r.Units);
        Assert.True(tiktokOnly < all);
        Assert.All(vm.ProductRows, r => Assert.DoesNotContain("Shopee", r.PlatformText));
        vm.PlatformFilter = "all";
        vm.Recalculate();
        Assert.Equal(all, vm.ProductRows.Sum(r => r.Units));
    }

    [Fact]
    public void Selection_is_kept_across_a_recalculation_when_the_product_is_still_listed()
    {
        var vm = NewVm();
        var name = vm.ProductRows[1].Name;
        vm.SelectedProduct = vm.ProductRows[1];
        vm.Recalculate();
        Assert.Equal(name, vm.SelectedProduct?.Name);
    }

    [Fact]
    public void RenderScreenshots_ProductStats_ToReportsFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(Directory.Exists(Path.Combine(dir.FullName, "tasks")) && Directory.Exists(Path.Combine(dir.FullName, "v2")))) dir = dir.Parent;
        var outDir = Path.Combine(dir!.FullName, "tasks", "reports", "T034-screens");
        Directory.CreateDirectory(outDir);
        WpfTestHost.Run(() =>
        {
            var d = new FakeDataService(clock: () => Now, enableMutableDemoStore: true);
            using var vm = new MainViewModel(d, d, d, d, new FakeLegacyImporter(d.GetCurrentTime), isDemoMode: true,
                uiSettingsStore: new UiSettingsStore(null, true), dispatchToUi: a => a(), deferInitialLoad: false);
            var window = new MainWindow
            {
                DataContext = vm, Width = 1360, Height = 1000, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false,
            };
            try
            {
                window.Show();
                WpfTestHost.PumpDispatcher();
                vm.SetTheme(ThemeService.LightTheme);
                vm.NavigateByKey("stats");
                WpfTestHost.PumpDispatcher();
                var sv = FindScrollViewer(window);
                sv?.ScrollToVerticalOffset(0);
                vm.StatisticsVm.SelectedProduct = vm.StatisticsVm.ProductRows.FirstOrDefault();
                WpfTestHost.PumpDispatcher();
                sv?.ScrollToVerticalOffset(0);
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "01-product-stats-light.png"));
                vm.SetTheme(ThemeService.DarkTheme);
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "02-product-stats-dark.png"));
            }
            finally { ThemeService.ApplyTheme(ThemeService.LightTheme); window.Close(); WpfTestHost.PumpDispatcher(); }
        });
        Assert.True(File.Exists(Path.Combine(outDir, "01-product-stats-light.png")));
    }

    private static System.Windows.Controls.ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (c is System.Windows.Controls.ScrollViewer sv && sv.ScrollableHeight > 0 && sv.Padding.Left == 18) return sv;
            var n = FindScrollViewer(c);
            if (n is not null) return n;
        }
        return null;
    }
}
