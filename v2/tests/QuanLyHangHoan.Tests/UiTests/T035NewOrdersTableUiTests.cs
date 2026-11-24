using System.IO;
using System.Windows;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.App.Views;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.Tests.UiTests;

/// <summary>Bảng đơn mới: hiện các đơn tạo tay / từ điện thoại, có đủ thao tác và nút "Tạo đơn mới". Dữ liệu GIẢ, không chạm đĩa thật.</summary>
public sealed class T035NewOrdersTableUiTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 12, 0, 0);

    private sealed record Rig(MainViewModel Vm, FakeDataService Data, FakeNewOrderDialogService Dialog, List<(string, bool)> Toasts);

    private static Rig NewRig(FakeNewOrderDialogService? dialog = null)
    {
        var d = new FakeDataService(clock: () => Now, enableMutableDemoStore: true);
        var dlg = dialog ?? new FakeNewOrderDialogService();
        var vm = new MainViewModel(d, d, d, d, new FakeLegacyImporter(d.GetCurrentTime), isDemoMode: true,
            uiSettingsStore: new UiSettingsStore(null, true), dispatchToUi: a => a(), deferInitialLoad: false,
            newOrderDialogService: dlg);
        var toasts = new List<(string, bool)>();
        return new Rig(vm, d, dlg, toasts);
    }

    [Fact]
    public void The_table_lists_only_newly_created_orders_and_has_the_create_button()
    {
        var r = NewRig();
        var t = r.Vm.NewCreatedVm;
        Assert.True(t.ShowCreateButton);
        Assert.True(t.TotalCount > 0);                                                   // demo có sẵn vài đơn mới
        Assert.All(t.VisibleOrders, row => Assert.True(NewOrderMarker.IsNewlyCreated(row.Source)));
        Assert.True(t.TotalCount < r.Vm.ShopeeReturnVm.TotalCount + r.Vm.TikTokReturnVm.TotalCount + r.Vm.ShopeeCancelledVm.TotalCount);
        Assert.Equal("📥 Bảng đơn mới", t.ScreenTitle);
        Assert.False(r.Vm.ShopeeReturnVm.ShowCreateButton);                              // chỉ Bảng đơn mới có nút tạo
        Assert.Equal("Bảng đơn mới", t.SourceTableName);
    }

    [Fact]
    public async Task Creating_an_order_adds_it_to_the_table_selects_it_and_shows_a_toast()
    {
        var dlg = new FakeNewOrderDialogService { Next = new NewOrderInput("TTVN9990000001", "OD-NEW-1", "TikTok", "", "Áo Polo Test", "Nâu", "L", 2, "khách báo hoàn") };
        var r = NewRig(dlg);
        var t = r.Vm.NewCreatedVm;
        var before = t.TotalCount;
        await t.CreateNewOrderCommand.ExecuteAsync(null);
        Assert.Equal(1, dlg.AskCount);
        Assert.Equal(before + 1, t.TotalCount);
        Assert.Equal("TTVN9990000001", t.SelectedRow?.Source.Track);
        var item = Assert.Single(t.SelectedRow!.Source.Items);
        Assert.Equal(("Áo Polo Test", "Nâu", "L", 2), (item.Product, item.Color, item.Size, item.Qty));
        Assert.Contains("Đã tạo đơn", r.Vm.ToastMessage);
        // đơn mới cũng nằm ở bảng chính (TikTok), không bị "tách" ra một kho riêng
        Assert.Contains(r.Vm.TikTokReturnVm.VisibleOrders, row => row.Source.Track == "TTVN9990000001");
    }

    [Fact]
    public async Task Cancelling_the_dialog_creates_nothing()
    {
        var dlg = new FakeNewOrderDialogService { Next = null };
        var r = NewRig(dlg);
        var before = r.Vm.NewCreatedVm.TotalCount;
        await r.Vm.NewCreatedVm.CreateNewOrderCommand.ExecuteAsync(null);
        Assert.Equal(before, r.Vm.NewCreatedVm.TotalCount);
        Assert.Equal(1, dlg.AskCount);
    }

    [Fact]
    public async Task An_existing_code_is_refused_with_a_clear_warning_and_nothing_is_added()
    {
        var r = NewRig();
        var existing = r.Vm.NewCreatedVm.VisibleOrders[0].Source.Track;
        r.Dialog.Next = new NewOrderInput(existing);
        var before = r.Vm.NewCreatedVm.TotalCount;
        await r.Vm.NewCreatedVm.CreateNewOrderCommand.ExecuteAsync(null);
        Assert.Equal(before, r.Vm.NewCreatedVm.TotalCount);
        Assert.Contains("KHÔNG TẠO LẠI", r.Vm.ToastMessage);
    }

    [Fact]
    public async Task Rows_of_the_new_orders_table_can_be_marked_received_and_deleted_like_other_tables()
    {
        var dlg = new FakeNewOrderDialogService { Next = new NewOrderInput("SPXVN9990000002", Product: "Quần Test") };
        var r = NewRig(dlg);
        var t = r.Vm.NewCreatedVm;
        await t.CreateNewOrderCommand.ExecuteAsync(null);
        var row = t.SelectedRow!;
        Assert.NotEqual(ReceiveStatus.Received, row.Source.Status);

        t.UpdateSelectedRows(new[] { row });
        Assert.True(t.HasAnySelected);
        // chọn nhiều dòng + Bỏ chọn dùng được ở bảng này
        t.UpdateSelectedRows(t.VisibleOrders.Take(2).ToList());
        Assert.True(t.HasMultipleSelected);
        t.ClearSelectionCommand.Execute(null);
        Assert.False(t.HasAnySelected);
        Assert.NotNull(t.OpenDetailCommand);
        Assert.NotNull(t.BulkMarkReceivedCommand);
        Assert.NotNull(t.BulkDeleteCommand);
    }

    [Fact]
    public void The_dialog_validation_requires_a_code_and_a_sane_quantity()
    {
        Assert.Null(NewOrderWindow.Build("  ", "", "", "", "", "", "1", "", "", out var e1));
        Assert.Contains("mã vận đơn", e1);
        Assert.Null(NewOrderWindow.Build("SPX1", "", "", "", "", "", "abc", "", "", out var e2));
        Assert.Contains("Số lượng", e2);
        Assert.Null(NewOrderWindow.Build("SPX1", "", "", "", "", "", "0", "", "", out _));
        Assert.Null(NewOrderWindow.Build("SPX1", "", "", "", "", "", "10000", "", "", out _));
        var ok = NewOrderWindow.Build(" spx1 ", " O1 ", "Shopee", " Áo ", " Đen ", " M ", " 3 ", "", " ghi chú ", out var e3);
        Assert.NotNull(ok); Assert.Equal("", e3);
        Assert.Equal(("spx1", "O1", "Shopee", "Áo", "Đen", "M", 3, "ghi chú", "manual"), (ok!.Track, ok.OrderId, ok.Platform, ok.Product, ok.Color, ok.Size, ok.Qty, ok.Note, ok.Origin));
        Assert.Equal(1, NewOrderWindow.Build("SPX1", "", "", "", "", "", "", "", "", out _)!.Qty);   // để trống số lượng: 1
    }

    // ---------------- các đường nạp đơn nằm trong Cài đặt ----------------
    [Fact]
    public void The_four_import_routes_are_no_longer_in_the_sidebar_but_still_reachable_as_settings_tabs()
    {
        var r = NewRig();
        var keys = r.Vm.NavigationItems.Select(n => n.Key).ToList();
        Assert.DoesNotContain("file_import", keys);
        Assert.DoesNotContain("folder_watch", keys);
        Assert.DoesNotContain("google_sheet", keys);
        Assert.DoesNotContain("salework", keys);
        Assert.DoesNotContain(r.Vm.NavigationItems, n => n.Section == "NẠP ĐƠN TỰ ĐỘNG");
        Assert.Contains("settings", keys);

        foreach (var (key, expected) in new (string, object)[]
        {
            ("file_import", r.Vm.FileImportVm), ("folder_watch", r.Vm.FolderWatchVm),
            ("google_sheet", r.Vm.GoogleSheetVm), ("salework", r.Vm.SaleworkVm),
        })
        {
            r.Vm.NavigateByKey(key);                                   // khóa cũ (Trợ giúp, thông báo, nút "Đi tới") vẫn dùng được
            Assert.Same(r.Vm.SettingsVm, r.Vm.CurrentPage);
            Assert.Equal(key, r.Vm.SettingsVm.SelectedTabKey);
            Assert.Same(expected, r.Vm.SettingsVm.ImportPage);
            Assert.True(r.Vm.SettingsVm.IsImportTab);
            Assert.Equal(1, r.Vm.SettingsVm.Tabs.Count(t => t.IsSelected));
        }
    }

    [Fact]
    public void Settings_general_tab_hides_the_import_page_and_switching_tabs_updates_the_selection()
    {
        var r = NewRig();
        var s = r.Vm.SettingsVm;
        Assert.True(s.IsGeneralTab); Assert.Null(s.ImportPage);
        s.SelectTabCommand.Execute("google_sheet");
        Assert.Same(r.Vm.GoogleSheetVm, s.ImportPage);
        s.SelectTabCommand.Execute("general");
        Assert.True(s.IsGeneralTab); Assert.Null(s.ImportPage);
        s.SelectTabCommand.Execute("khong-co-the-nay");               // khóa lạ: về thẻ Chung, không sập
        Assert.True(s.IsGeneralTab);
        Assert.Equal(5, s.Tabs.Count);
        Assert.Contains("TikTok", s.Tabs.Single(t => t.Key == "file_import").Title);   // Nạp file Excel chỉ dành cho TikTok
    }

    [Fact]
    public void RenderScreenshots_SettingsTabs_ToReportsFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(Directory.Exists(Path.Combine(dir.FullName, "tasks")) && Directory.Exists(Path.Combine(dir.FullName, "v2")))) dir = dir.Parent;
        var outDir = Path.Combine(dir!.FullName, "tasks", "reports", "T035-screens");
        Directory.CreateDirectory(outDir);
        WpfTestHost.Run(() =>
        {
            var r = NewRig();
            var window = new MainWindow
            {
                DataContext = r.Vm, Width = 1360, Height = 820, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false,
            };
            try
            {
                window.Show();
                WpfTestHost.PumpDispatcher();
                r.Vm.SetTheme(ThemeService.LightTheme);
                r.Vm.NavigateByKey("settings");
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "03-settings-general-tab.png"));
                r.Vm.NavigateByKey("google_sheet");
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "04-settings-google-sheet-tab.png"));
                r.Vm.NavigateByKey("file_import");
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "05-settings-file-import-tab.png"));
            }
            finally { window.Close(); WpfTestHost.PumpDispatcher(); }
        });
        Assert.True(File.Exists(Path.Combine(outDir, "05-settings-file-import-tab.png")));
    }

    [Fact]
    public void RenderScreenshots_NewOrdersTable_ToReportsFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(Directory.Exists(Path.Combine(dir.FullName, "tasks")) && Directory.Exists(Path.Combine(dir.FullName, "v2")))) dir = dir.Parent;
        var outDir = Path.Combine(dir!.FullName, "tasks", "reports", "T035-screens");
        Directory.CreateDirectory(outDir);
        WpfTestHost.Run(() =>
        {
            var r = NewRig();
            var window = new MainWindow
            {
                DataContext = r.Vm, Width = 1360, Height = 820, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false,
            };
            try
            {
                window.Show();
                WpfTestHost.PumpDispatcher();
                r.Vm.SetTheme(ThemeService.LightTheme);
                r.Vm.NavigateByKey("new_orders");
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "01-new-orders-table.png"));
                var dlgWindow = new NewOrderWindow { Left = -10000, Top = -10000, ShowInTaskbar = false };
                dlgWindow.Show();
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(dlgWindow, Path.Combine(outDir, "02-create-dialog.png"));
                dlgWindow.Close();
            }
            finally { window.Close(); WpfTestHost.PumpDispatcher(); }
        });
        Assert.True(File.Exists(Path.Combine(outDir, "01-new-orders-table.png")));
    }
}
