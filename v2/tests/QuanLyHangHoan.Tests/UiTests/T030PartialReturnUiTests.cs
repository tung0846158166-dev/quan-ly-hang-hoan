using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Controls;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.App.Views;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.Tests.UiTests;

/// <summary>
/// Đơn hoàn MỘT PHẦN: giữ đủ mọi sản phẩm, sản phẩm đã hoàn khác màu + nhãn chữ; hoàn đủ/chưa biết thì như thường. Dữ liệu GIẢ.
/// </summary>
public sealed class T030PartialReturnUiTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);

    private static Order Make(params (string Name, int Qty, int Returned)[] items) => new()
    {
        Id = "o1", OrderId = "A", Track = "C", ReturnTrack = "C", Platform = "Shopee", Status = ReceiveStatus.NotReturned,
        Items = items.Select(i => new OrderItem { Product = i.Name, Color = "Đen", Size = "M", Qty = i.Qty, ReturnedQty = i.Returned }).ToList(),
    };

    // ---------------- quy tắc ----------------
    [Fact]
    public void Partial_return_keeps_all_three_lines_and_marks_only_the_returned_one()
    {
        var o = Make(("Áo", 1, 1), ("Quần", 1, 0), ("Nón", 1, 0));
        var lines = ItemDisplay.Build(o.Items);
        Assert.Equal(3, lines.Count);                                              // giữ ĐỦ sản phẩm
        Assert.Equal(ItemLineKind.Returned, lines[0].Kind);
        Assert.Equal("Hoàn 1", lines[0].Tag);
        Assert.All(lines.Skip(1), l => { Assert.Equal(ItemLineKind.NotReturned, l.Kind); Assert.Equal("Không hoàn", l.Tag); });
        Assert.Equal("Hoàn 1/3 SP", ItemDisplay.Chip(o.Items, isPartialFlag: true));
        Assert.True(ItemDisplay.IsMixed(o.Items));
    }

    [Fact]
    public void Full_return_and_unknown_return_do_not_distinguish_items()
    {
        var full = Make(("Áo", 1, 1), ("Quần", 2, 2));
        Assert.All(ItemDisplay.Build(full.Items), l => { Assert.Equal(ItemLineKind.Neutral, l.Kind); Assert.Equal("", l.Tag); });
        Assert.Equal("", ItemDisplay.Chip(full.Items, isPartialFlag: false));

        var unknown = Make(("Áo", 1, 0), ("Quần", 1, 0));
        Assert.All(ItemDisplay.Build(unknown.Items), l => Assert.Equal(ItemLineKind.Neutral, l.Kind));
        Assert.False(ItemDisplay.IsMixed(unknown.Items));
    }

    [Fact]
    public void Platform_partial_flag_without_known_items_shows_generic_chip_only()
    {
        var o = Make(("Áo", 1, 0), ("Quần", 1, 0));
        Assert.Equal("Hoàn một phần", ItemDisplay.Chip(o.Items, isPartialFlag: true));
        Assert.All(ItemDisplay.Build(o.Items), l => Assert.Equal(ItemLineKind.Neutral, l.Kind));
        Assert.Equal("", ItemDisplay.Chip(o.Items, isPartialFlag: false));
    }

    [Fact]
    public void Single_item_and_empty_orders_are_safe()
    {
        Assert.False(ItemDisplay.IsMixed(Make(("Áo", 1, 1)).Items));
        var empty = ItemDisplay.Build(new List<OrderItem>());
        Assert.Equal("Chưa có sản phẩm", Assert.Single(empty).Text);
    }

    [Fact]
    public void Tooltip_and_flat_text_list_every_product_with_text_tags_not_only_colors()
    {
        var o = Make(("Áo", 1, 1), ("Quần", 1, 0));
        var tip = ItemDisplay.Tooltip(o.Items, true);
        Assert.Contains("Hoàn 1/2 SP", tip);
        Assert.Contains("Áo", tip); Assert.Contains("Quần", tip);
        Assert.Contains("Hoàn 1", tip); Assert.Contains("Không hoàn", tip);
        var flat = ItemDisplay.Flat(o.Items, true);
        Assert.Contains("Áo (Đen/M x1) · Hoàn 1", flat);
        Assert.Contains("Quần (Đen/M x1) · Không hoàn", flat);
    }

    // ---------------- ô hiển thị ----------------
    [Fact]
    public void Item_lines_control_colors_returned_and_not_returned_differently_and_keeps_text_tags()
    {
        WpfTestHost.Run(() =>
        {
            var o = Make(("Áo", 1, 1), ("Quần", 1, 0), ("Nón", 1, 0));
            var tb = new ItemLinesTextBlock { Lines = ItemDisplay.Build(o.Items), Chip = ItemDisplay.Chip(o.Items, true) };
            var win = new Window { Content = tb, Width = 700, Height = 80, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
            win.Show();
            WpfTestHost.PumpDispatcher(win);

            var runs = tb.Inlines.OfType<Run>().ToList();
            var text = string.Concat(runs.Select(r => r.Text));
            Assert.Contains("Hoàn 1/3 SP", text);
            Assert.Contains("[Hoàn 1]", text);
            Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(text, @"\[Không hoàn\]").Count);   // chữ, không chỉ màu
            var returnedRun = runs.First(r => r.Text.StartsWith("Áo"));
            var notReturnedRun = runs.First(r => r.Text.StartsWith("Quần"));
            Assert.Equal(FontWeights.Bold, returnedRun.FontWeight);
            Assert.NotEqual(((SolidColorBrush)returnedRun.Foreground).Color, ((SolidColorBrush)notReturnedRun.Foreground).Color);
            Assert.Contains("Nón", text);                                                                     // giữ đủ 3 sản phẩm
            win.Close();
        });
    }

    // ---------------- chi tiết đơn ----------------
    private sealed class NoPhoto : IPhotoProvider
    {
        public Task<PhotoData?> GetPhotoAsync(string photoId, CancellationToken cancellationToken = default) => Task.FromResult<PhotoData?>(null);
        public Task<IReadOnlyList<string>> GetMissingPhotoIdsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private sealed class NoViewer : IPhotoViewerService { public void ShowPhoto(string photoId, string? orderId = null, string? tracking = null) { } }

    private sealed class NoHistory : IOrderEditService
    {
        public Task<EditResult> SetFieldAsync(string orderId, EditField field, string newValue, string? expectedOld = null, CancellationToken ct = default) => Task.FromResult(new EditResult(EditKind.NoChange, ""));
        public Task<EditResult> DeleteAsync(string orderId, string from, CancellationToken ct = default) => Task.FromResult(new EditResult(EditKind.NoChange, ""));
        public Task<EditResult> RestoreAsync(string orderId, CancellationToken ct = default) => Task.FromResult(new EditResult(EditKind.NoChange, ""));
        public Task<EditResult> ArchiveAsync(string orderId, CancellationToken ct = default) => Task.FromResult(new EditResult(EditKind.NoChange, ""));
        public Task<EditResult> UnarchiveAsync(string orderId, CancellationToken ct = default) => Task.FromResult(new EditResult(EditKind.NoChange, ""));
        public Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(string orderId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HistoryEntry>>(Array.Empty<HistoryEntry>());
    }

    private sealed class NoClip : IClipboardService { public void SetText(string text) { } }

    private static OrderDetailViewModel Detail(Order o)
    {
        var vm = new OrderDetailViewModel(o, Now, new NoPhoto(), new NoViewer(), new NoHistory(), new NoClip());
        WpfTestHost.Run(() => WpfTestHost.RunTask(() => vm.LoadAsync()));
        return vm;
    }

    [Fact]
    public void Order_detail_shows_all_products_with_a_returned_column_and_a_summary_line()
    {
        var vm = Detail(Make(("Áo", 1, 1), ("Quần", 1, 0), ("Nón", 1, 0)));
        Assert.Equal(3, vm.Items.Count);
        Assert.Equal(ItemLineKind.Returned, vm.Items[0].Kind);
        Assert.Equal("Hoàn 1", vm.Items[0].ReturnTag);
        Assert.Equal("Không hoàn", vm.Items[1].ReturnTag);
        Assert.True(vm.HasReturnSummary);
        Assert.Equal("Hoàn 1/3 sản phẩm: Áo x1", vm.ReturnSummary);
    }

    [Fact]
    public void Order_detail_for_full_or_unknown_return_has_no_distinction_and_no_summary()
    {
        var full = Detail(Make(("Áo", 1, 1), ("Quần", 1, 1)));
        Assert.All(full.Items, i => { Assert.Equal(ItemLineKind.Neutral, i.Kind); Assert.Equal("—", i.ReturnTag); });
        Assert.False(full.HasReturnSummary);
        var unknown = Detail(Make(("Áo", 1, 0), ("Quần", 1, 0)));
        Assert.False(unknown.HasReturnSummary);
    }

    [Fact]
    public void Row_view_models_expose_lines_chip_and_tooltip_for_a_partial_order()
    {
        var o = Make(("Áo", 1, 1), ("Quần", 1, 0));
        o.IsPartialReturn = true;
        var row = new ReturnOrderRowViewModel(o, 1, Now);
        Assert.Equal(2, row.ItemLines.Count);
        Assert.Equal("Hoàn 1/2 SP", row.PartialChip);
        Assert.Contains("Không hoàn", row.ItemsTooltip);
    }

    [Fact]
    public void Two_parcels_of_one_order_are_displayed_independently()
    {
        var a = Make(("Áo", 1, 1), ("Quần", 1, 0));
        var b = Make(("Váy", 1, 0), ("Mũ", 1, 0));            // kiện B: chưa biết gì
        Assert.True(ItemDisplay.IsMixed(a.Items));
        Assert.False(ItemDisplay.IsMixed(b.Items));
    }

    // ---------------- ảnh chụp ----------------
    [Fact]
    public void RenderScreenshots_PartialReturn_ToReportsFolder()
    {
        var repo = FindRepoRoot();
        var outDir = Path.Combine(repo, "tasks", "reports", "T030-screens");
        Directory.CreateDirectory(outDir);

        WpfTestHost.Run(() =>
        {
            var settingsPath = Path.Combine(Path.GetTempPath(), "qlhh-t030-" + Guid.NewGuid().ToString("N") + ".json");
            var fakeData = new FakeDataService();
            using var vm = new MainViewModel(
                fakeData, fakeData, fakeData, fakeData,
                new FakeLegacyImporter(fakeData.GetCurrentTime),
                isDemoMode: true,
                syncController: new FakeSyncController("ROOM-DEMO"),
                uiSettingsStore: new UiSettingsStore(settingsPath, isDemoMode: true));
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
                vm.NavigateByKey("tiktok_return");
                WpfTestHost.PumpDispatcher();
                ScrollToProductColumn(window);
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "01-table-partial-light.png"));

                vm.SetTheme(ThemeService.DarkTheme);
                vm.NavigateByKey("tiktok_return");
                WpfTestHost.PumpDispatcher();
                ScrollToProductColumn(window);
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "02-table-partial-dark.png"));

                var partial = fakeData.GetReturnOrders(200).First(o => ItemDisplay.IsMixed(o.Items) && o.Items.Count == 3);
                var detailVms = new[] { Detail(partial), Detail(partial) };   // tao truoc: Detail() goi WpfTestHost.Run long nhau se dat lai theme
                var idx = 0;
                foreach (var (theme, file) in new[] { (ThemeService.LightTheme, "03-detail-partial-light.png"), (ThemeService.DarkTheme, "04-detail-partial-dark.png") })
                {
                    vm.SetTheme(theme);
                    var dvm = detailVms[idx++];
                    var win = new OrderDetailWindow { DataContext = dvm, Width = 920, Height = 820 };
                    win.Show();
                    WpfTestHost.PumpDispatcher(win);
                    (win.FindName("DetailScrollViewer") as System.Windows.Controls.ScrollViewer)?.ScrollToVerticalOffset(380);
                    WpfTestHost.PumpDispatcher(win);
                    WpfTestHost.SaveWindowPng(win, Path.Combine(outDir, file));
                    win.Close();
                }
            }
            finally
            {
                ThemeService.ApplyTheme(ThemeService.LightTheme);
                window.Close();
                WpfTestHost.PumpDispatcher();
            }
        });

        foreach (var f in new[] { "01-table-partial-light.png", "02-table-partial-dark.png", "03-detail-partial-light.png", "04-detail-partial-dark.png" })
            Assert.True(File.Exists(Path.Combine(outDir, f)), f);
    }

    private static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (c is T hit) return hit;
            var deeper = FindVisual<T>(c);
            if (deeper is not null) return deeper;
        }
        return null;
    }

    private static void ScrollToProductColumn(Window window)
    {
        var grid = FindVisual<System.Windows.Controls.DataGrid>(window);
        if (grid is null || grid.Items.Count == 0) return;
        var col = grid.Columns.FirstOrDefault(c => (c.Header as string ?? "").Contains("Sản phẩm"));
        if (col is null) return;
        grid.ScrollIntoView(grid.Items[0], col);
        WpfTestHost.PumpDispatcher();
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
