using System.IO;
using System.Windows;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.Tests.UiTests;

/// <summary>
/// Yêu cầu chủ dự án 24/11/2026: chỉ MỘT ô tìm kiếm tổng, chỉ MỘT cửa sổ chi tiết đơn, thanh bên mặc định thu gọn (rê chuột mới mở),
/// có nút "Bỏ chọn" khi chọn nhiều đơn.
/// </summary>
public sealed class T033UiCleanupTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 12, 0, 0);

    private static string ViewsFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var p = Path.Combine(dir.FullName, "src", "QuanLyHangHoan.App", "Views");
            if (Directory.Exists(p)) return p;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Không tìm thấy thư mục Views.");
    }

    // ---------------- một ô tìm kiếm ----------------
    [Theory]
    [InlineData("ReturnOrdersView")]
    [InlineData("ArchivedOrdersView")]
    [InlineData("DeletedOrdersView")]
    [InlineData("NewOrdersView")]
    [InlineData("ReturnWatchView")]
    [InlineData("WatchlistView")]
    public void Table_views_have_no_search_box_of_their_own(string view)
    {
        var xaml = File.ReadAllText(Path.Combine(ViewsFolder(), view + ".xaml"));
        Assert.DoesNotContain("Binding SearchText", xaml);
        Assert.DoesNotContain("SearchBoxBgBrush", xaml);
    }

    [Fact]
    public void The_global_search_box_is_still_there_exactly_once()
    {
        var xaml = File.ReadAllText(Path.Combine(ViewsFolder(), "..", "MainWindow.xaml"));
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(xaml, "x:Name=\"GlobalSearchBox\"").Count);
    }

    // ---------------- thanh bên ----------------
    [Fact]
    public void Sidebar_starts_collapsed_and_expands_only_while_the_mouse_is_over_it()
    {
        var d = new FakeDataService(enableMutableDemoStore: true);
        using var vm = new MainViewModel(d, d, d, d, new FakeLegacyImporter(d.GetCurrentTime), isDemoMode: true,
            uiSettingsStore: new UiSettingsStore(null, true), dispatchToUi: a => a());
        Assert.False(vm.IsSidebarExpanded);

        WpfTestHost.Run(() =>
        {
            var window = new MainWindow
            {
                DataContext = vm, Width = 1200, Height = 760, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false,
            };
            try
            {
                window.Show();
                WpfTestHost.PumpDispatcher();
                var shell = (FrameworkElement)window.FindName("SidebarShell");
                Assert.Equal(60, shell.ActualWidth, 1);                              // thu gọn: chỉ cột biểu tượng

                vm.IsSidebarExpanded = true;                                          // rê chuột vào
                WpfTestHost.PumpDispatcher();
                System.Threading.Thread.Sleep(350);
                WpfTestHost.PumpDispatcher();
                Assert.Equal(244, shell.ActualWidth, 1);

                vm.IsSidebarExpanded = false;                                         // rời chuột
                WpfTestHost.PumpDispatcher();
                System.Threading.Thread.Sleep(350);
                WpfTestHost.PumpDispatcher();
                Assert.Equal(60, shell.ActualWidth, 1);
            }
            finally { window.Close(); WpfTestHost.PumpDispatcher(); }
        });
    }

    // ---------------- bỏ chọn ----------------
    [Fact]
    public void Clear_selection_command_unselects_everything_and_hides_the_bulk_bar_state()
    {
        var data = new FakeDataService(enableMutableDemoStore: true);
        var vm = new ReturnOrdersViewModel(data, ReturnTableScope.All);
        vm.LoadOrders(data.GetReturnOrders(40), Now);
        vm.UpdateSelectedRows(vm.VisibleOrders.Take(3).ToList());
        Assert.True(vm.HasMultipleSelected);
        Assert.Equal(3, vm.SelectedCount);

        vm.ClearSelectionCommand.Execute(null);
        Assert.Equal(0, vm.SelectedCount);
        Assert.False(vm.HasMultipleSelected);
        Assert.False(vm.HasAnySelected);
    }

    [Fact]
    public void Clear_selection_asks_the_view_to_unselect_when_a_view_is_attached_and_the_bar_has_the_button()
    {
        var data = new FakeDataService(enableMutableDemoStore: true);
        var vm = new ReturnOrdersViewModel(data, ReturnTableScope.All);
        vm.LoadOrders(data.GetReturnOrders(40), Now);
        vm.UpdateSelectedRows(vm.VisibleOrders.Take(2).ToList());
        var asked = 0;
        vm.ClearSelectionRequested += () => asked++;
        vm.ClearSelectionCommand.Execute(null);
        Assert.Equal(1, asked);

        var xaml = File.ReadAllText(Path.Combine(ViewsFolder(), "ReturnOrdersView.xaml"));
        Assert.Contains("ClearSelectionCommand", xaml);
        Assert.Contains("Bỏ chọn", xaml);
    }

    // ---------------- Trang chủ "Cần xử lý ngay" phải đánh dấu đúng đơn ----------------
    private static T? FindByName<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (c is T t && t.Name == name) return t;
            var n = FindByName<T>(c, name);
            if (n is not null) return n;
        }
        return null;
    }

    [Fact]
    public void Urgent_order_from_home_opens_the_table_selects_scrolls_to_and_focuses_that_exact_order()
    {
        var d = new FakeDataService(clock: () => Now, enableMutableDemoStore: true);
        using var vm = new MainViewModel(d, d, d, d, new FakeLegacyImporter(d.GetCurrentTime), isDemoMode: true,
            uiSettingsStore: new UiSettingsStore(null, true), dispatchToUi: a => a(), deferInitialLoad: false);
        WpfTestHost.Run(() =>
        {
            var window = new MainWindow
            {
                DataContext = vm, Width = 1360, Height = 820, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false,
            };
            try
            {
                window.Show();
                WpfTestHost.PumpDispatcher();
                vm.ShopeeReturnVm.LoadOrders(d.GetReturnOrders(3000), Now);           // bảng dài: đơn cần tìm nằm sâu, ngoài màn hình
                var target = vm.ShopeeReturnVm.VisibleOrders[300];
                Assert.True(vm.ShopeeReturnVm.VisibleOrders.Count > 301);
                vm.NavigateByKey("home");
                WpfTestHost.PumpDispatcher();

                var item = new UrgentOrderItem(target.OrderId, target.Source.Track, "Shopee", "Trả hàng/Hoàn tiền", "shopee_return",
                    QuanLyHangHoan.Core.Alerts.AlertLevel.Red, "Quá 24h", "100.000 đ", "", 30);
                vm.HomeVm.OpenUrgentOrderCommand.Execute(item);
                for (var i = 0; i < 5; i++) WpfTestHost.PumpDispatcher();

                var grid = FindByName<System.Windows.Controls.DataGrid>(window, "OrdersGrid");
                Assert.NotNull(grid);
                Assert.Same(target, grid!.SelectedItem);                               // đúng đơn được chọn
                Assert.Same(target, vm.ShopeeReturnVm.SelectedRow);
                var container = grid.ItemContainerGenerator.ContainerFromItem(target);
                Assert.NotNull(container);                                             // đã cuộn tới nên dòng được dựng (không còn ngoài màn hình)
                Assert.True(grid.IsKeyboardFocusWithin);                               // bảng có tiêu điểm: dòng chọn hiện màu chọn rõ
            }
            finally { window.Close(); WpfTestHost.PumpDispatcher(); }
        });
    }

    // ---------------- ảnh chụp ----------------
    [Fact]
    public void RenderScreenshots_Sidebar_And_Selection_ToReportsFolder()
    {
        var repo = FindRepoRoot();
        var outDir = Path.Combine(repo, "tasks", "reports", "T033-screens");
        Directory.CreateDirectory(outDir);
        WpfTestHost.Run(() =>
        {
            var d = new FakeDataService(clock: () => Now, enableMutableDemoStore: true);
            using var vm = new MainViewModel(d, d, d, d, new FakeLegacyImporter(d.GetCurrentTime), isDemoMode: true,
                uiSettingsStore: new UiSettingsStore(null, true), dispatchToUi: a => a(), deferInitialLoad: false);
            var window = new MainWindow
            {
                DataContext = vm, Width = 1360, Height = 820, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false,
            };
            try
            {
                window.Show();
                WpfTestHost.PumpDispatcher();
                vm.SetTheme(ThemeService.LightTheme);
                vm.NavigateByKey("shopee_return");
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "01-rail-collapsed-one-search.png"));

                vm.IsSidebarExpanded = true;
                WpfTestHost.PumpDispatcher();
                System.Threading.Thread.Sleep(350);
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "02-sidebar-expanded-on-hover.png"));
                vm.IsSidebarExpanded = false;
                WpfTestHost.PumpDispatcher();
                System.Threading.Thread.Sleep(300);

                foreach (var key in new[] { "archive", "deleted", "new_orders", "watchlist", "return_watch" })
                {
                    vm.NavigateByKey(key);
                    WpfTestHost.PumpDispatcher();
                    WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, $"04-{key}.png"));
                }
                vm.NavigateByKey("shopee_return");
                WpfTestHost.PumpDispatcher();

                if (vm.CurrentPage is ReturnOrdersViewModel table)
                {
                    table.UpdateSelectedRows(table.VisibleOrders.Take(4).ToList());
                    WpfTestHost.PumpDispatcher();
                    WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "03-bulk-bar-with-clear-selection.png"));
                }
            }
            finally
            {
                ThemeService.ApplyTheme(ThemeService.LightTheme);
                window.Close();
                WpfTestHost.PumpDispatcher();
            }
        });
        Assert.True(File.Exists(Path.Combine(outDir, "01-rail-collapsed-one-search.png")));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tasks")) && Directory.Exists(Path.Combine(dir.FullName, "v2"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Không tìm thấy thư mục gốc repo.");
    }
}
