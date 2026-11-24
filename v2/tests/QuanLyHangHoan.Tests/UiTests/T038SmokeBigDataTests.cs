using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Tests.UiTests;

/// <summary>Chạy thử "như người dùng" trên dữ liệu lớn: mở lần lượt mọi trang, cuộn, chuyển trang, sắp xếp, lọc; không được có lỗi bắt không kịp.</summary>
public class T038SmokeBigDataTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 12, 0, 0);

    private static void Visit(DependencyObject root, Action<DataGrid> action)
    {
        var q = new Queue<DependencyObject>(); q.Enqueue(root);
        while (q.Count > 0)
        {
            var d = q.Dequeue();
            if (d is DataGrid g && g.IsVisible) action(g);
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) q.Enqueue(VisualTreeHelper.GetChild(d, i));
        }
    }

    [Fact]
    public void Every_page_survives_big_data_scrolling_paging_sorting_and_filtering()
    {
        WpfTestHost.Run(() =>
        {
            var d = new FakeDataService(clock: () => Now, enableMutableDemoStore: true);
            var vm = new MainViewModel(d, d, d, d, new FakeLegacyImporter(d.GetCurrentTime), isDemoMode: true,
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
                var big = d.GetReturnOrders(4_700);       // cỡ dữ liệu thật của chủ dự án (≈4.700 đơn)
                vm.ShopeeReturnVm.LoadOrders(big, Now);
                vm.ShopeeCancelledVm.LoadOrders(big, Now);
                vm.TikTokReturnVm.LoadOrders(big, Now);
                vm.ArchivedOrdersVm.LoadOrders(big.Take(1500).ToList());
                vm.DeletedOrdersVm.LoadOrders(big.Take(400).Select((o, i) => new DeletedOrderRecord
                    { OrderId = o.OrderId, Track = o.Track, Platform = o.Platform, DeletedFrom = i % 2 == 0 ? "Bảng chính" : "Kho lưu trữ", DeletedAt = "20/11/2026 09:00" }).ToList());
                vm.WatchlistVm.LoadOrders(big.Select(o => { o.IsWatchlist = true; return o; }).Take(350).ToList(), [], Now);

                foreach (var item in vm.NavigationItems.ToList())
                {
                    vm.NavigateByKey(item.Key);
                    for (var i = 0; i < 3; i++) WpfTestHost.PumpDispatcher();
                    Visit(window, g =>
                    {
                        var n = g.Items.Count;
                        if (n == 0) return;
                        foreach (var idx in new[] { n - 1, n / 2, 0, n - 1, 0 })
                        {
                            g.ScrollIntoView(g.Items[idx]); g.UpdateLayout();
                            WpfTestHost.PumpDispatcher();
                        }
                        g.SelectedItem = g.Items[n / 2]; g.UpdateLayout();
                        if (g.SelectionMode == DataGridSelectionMode.Extended) { g.SelectAll(); WpfTestHost.PumpDispatcher(); g.UnselectAll(); }   // chọn hết rồi bỏ chọn
                    });
                    // Chuyển trang (nếu bảng có chia trang): trang cuối, giữa, đầu.
                    var page = vm.CurrentPage;
                    switch (page)
                    {
                        case ReturnOrdersViewModel r:
                            foreach (var p in new[] { r.TotalPages, Math.Max(1, r.TotalPages / 2), 1 }) { r.GoToPageNumber(p); WpfTestHost.PumpDispatcher(); }
                            foreach (var col in new[] { "Price", "OrderId", "Platform", "ReturnDateParsed", "Stt" }) { r.SortByColumn(col, out _); WpfTestHost.PumpDispatcher(); }
                            r.SearchText = "demo"; WpfTestHost.PumpDispatcher(); r.SearchText = "";
                            r.ProductFilter = "ao"; WpfTestHost.PumpDispatcher(); r.ProductFilter = "";
                            break;
                        case ArchivedOrdersViewModel a:
                            foreach (var p in new[] { a.Pager.TotalPages, 1 }) { a.Pager.GoToPage(p); WpfTestHost.PumpDispatcher(); }
                            break;
                        case DeletedOrdersViewModel de:
                            foreach (var p in new[] { de.Pager.TotalPages, 1 }) { de.Pager.GoToPage(p); WpfTestHost.PumpDispatcher(); }
                            break;
                        case WatchlistViewModel w:
                            foreach (var p in new[] { w.Pager.TotalPages, 1 }) { w.Pager.GoToPage(p); WpfTestHost.PumpDispatcher(); }
                            break;
                    }
                    WpfTestHost.PumpDispatcher();
                }
            }
            finally { window.Close(); WpfTestHost.PumpDispatcher(); }
        });
    }
}