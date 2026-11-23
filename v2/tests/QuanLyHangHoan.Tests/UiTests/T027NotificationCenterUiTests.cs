using System.IO;
using System.Windows;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.FolderWatch;
using QuanLyHangHoan.Data.Sheet;
using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.Tests.UiTests;

/// <summary>Chuông thông báo: đếm chưa đọc, mỗi đơn mỗi mức một lần, sự kiện điện thoại/nạp đơn/lỗi, gộp trùng, lưu, giới hạn 300, không lộ địa chỉ.</summary>
public sealed class T027NotificationCenterUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-t027-" + Guid.NewGuid().ToString("N"));
    public T027NotificationCenterUiTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class Clock
    {
        public DateTime Now = new(2026, 11, 24, 9, 0, 0);
        public DateTime Get() => Now;
    }

    private static NotificationCenter NewCenter(Clock c, INotificationStore? store = null)
        => new(store ?? new InMemoryNotificationStore(), c.Get);

    private sealed record Rig(MainViewModel Vm, NotificationCenter Center, FakeSyncController Sync, FakeSheetSyncController Sheet,
        FakeFolderWatchController Folder, FakeFileImportController File, InMemoryAlertNotifyStateStore AlertState);

    private static Rig NewRig(Clock clock, NotificationCenter? center = null, InMemoryAlertNotifyStateStore? alertState = null,
        FakeFileImportController? file = null, IUiSettingsStore? settings = null)
    {
        var d = new FakeDataService(clock.Get, enableMutableDemoStore: true);   // đồng hồ cố định: đơn không tự vượt mốc giữa hai lần kiểm tra
        var sync = new FakeSyncController("ROOM-ABCDEFGH-123456");
        var sheet = new FakeSheetSyncController();
        var folder = new FakeFolderWatchController();
        var fileCtl = file ?? new FakeFileImportController();
        var c = center ?? NewCenter(clock);
        var st = alertState ?? new InMemoryAlertNotifyStateStore();
        var vm = new MainViewModel(d, d, d, d, new FakeLegacyImporter(d.GetCurrentTime), isDemoMode: true,
            uiSettingsStore: settings ?? new UiSettingsStore(null, true), syncController: sync, sheetSyncController: sheet,
            folderWatchController: folder, fileImportController: fileCtl, alertNotifyStateStore: st, notificationCenter: c,
            dispatchToUi: a => a(), syncReloadDebounce: TimeSpan.Zero);
        return new Rig(vm, c, sync, sheet, folder, fileCtl, st);
    }

    // ---------------- lõi: đếm, đọc, xóa ----------------
    [Fact]
    public void Unread_count_drops_when_marked_read_and_all_read_and_clear_work()
    {
        var clock = new Clock(); var c = NewCenter(clock);
        c.Add(NotificationLevel.Info, "a", "Một", "x"); clock.Now = clock.Now.AddMinutes(5);
        c.Add(NotificationLevel.Red, "b", "Hai", "y"); clock.Now = clock.Now.AddMinutes(5);
        c.Add(NotificationLevel.Success, "c", "Ba", "z");
        Assert.Equal(3, c.UnreadCount);
        Assert.Equal("Ba", c.Snapshot()[0].Title);                      // mới nhất trước
        c.MarkRead(c.Snapshot()[0].Id);
        Assert.Equal(2, c.UnreadCount);
        c.MarkAllRead();
        Assert.Equal(0, c.UnreadCount);
        c.ClearAll();
        Assert.Empty(c.Snapshot());
    }

    [Fact]
    public void Bell_text_hides_at_zero_and_shows_99_plus_when_many()
    {
        var clock = new Clock(); var c = NewCenter(clock);
        var vm = new NotificationCenterViewModel(c, clock.Get, _ => { }, a => a());
        Assert.False(vm.HasUnread);
        Assert.True(vm.IsEmpty);
        for (var i = 0; i < 120; i++) { clock.Now = clock.Now.AddMinutes(2); c.Add(NotificationLevel.Info, "s" + i, "T" + i, "d"); }
        Assert.Equal("99+", vm.UnreadText);
        Assert.True(vm.HasUnread);
        c.MarkAllRead();
        Assert.False(vm.HasUnread);
        Assert.Equal(120, vm.Items.Count);
    }

    [Fact]
    public void List_keeps_at_most_300_newest_entries()
    {
        var clock = new Clock(); var c = NewCenter(clock);
        for (var i = 0; i < 350; i++) { clock.Now = clock.Now.AddMinutes(2); c.Add(NotificationLevel.Info, "s" + i, "T" + i, "d"); }
        var snap = c.Snapshot();
        Assert.Equal(300, snap.Count);
        Assert.Equal("T349", snap[0].Title);
        Assert.Equal("T50", snap[^1].Title);
    }

    [Fact]
    public void Identical_consecutive_entries_within_60_seconds_merge_into_x2_but_not_after_60_seconds()
    {
        var clock = new Clock(); var c = NewCenter(clock);
        c.Add(NotificationLevel.Info, "phone", "Điện thoại cập nhật ghi chú", "SPX1");
        clock.Now = clock.Now.AddSeconds(30);
        c.Add(NotificationLevel.Info, "phone", "Điện thoại cập nhật ghi chú", "SPX1");
        Assert.Single(c.Snapshot());
        Assert.Equal(2, c.Snapshot()[0].Count);
        Assert.Contains("×2", new NotificationItemViewModel(c.Snapshot()[0], clock.Now).Title);
        clock.Now = clock.Now.AddSeconds(61);
        c.Add(NotificationLevel.Info, "phone", "Điện thoại cập nhật ghi chú", "SPX1");
        Assert.Equal(2, c.Snapshot().Count);
        c.Add(NotificationLevel.Info, "phone", "Điện thoại cập nhật ghi chú", "SPX2");   // khác chi tiết: không gộp
        Assert.Equal(3, c.Snapshot().Count);
    }

    // ---------------- lưu giữa các lần mở ----------------
    [Fact]
    public void Saved_entries_and_read_state_survive_a_restart_and_a_broken_file_is_ignored()
    {
        var path = Path.Combine(_dir, "notifications.json");
        var clock = new Clock();
        var c1 = new NotificationCenter(new FileNotificationStore(path), clock.Get);
        c1.Add(NotificationLevel.Red, "a", "Đỏ", "chi tiết", "search:X1"); clock.Now = clock.Now.AddMinutes(3);
        c1.Add(NotificationLevel.Info, "b", "Tin", "chi tiết 2");
        c1.MarkRead(c1.Snapshot()[0].Id);
        var c2 = new NotificationCenter(new FileNotificationStore(path), clock.Get);
        Assert.Equal(2, c2.Snapshot().Count);
        Assert.Equal(1, c2.UnreadCount);
        Assert.Equal("search:X1", c2.Snapshot().Single(e => e.Title == "Đỏ").Target);
        Assert.False(File.Exists(path + ".tmp"));

        File.WriteAllText(path, "{ không phải json [[[");
        var c3 = new NotificationCenter(new FileNotificationStore(path), clock.Get);   // không sập
        Assert.Empty(c3.Snapshot());
        c3.Add(NotificationLevel.Info, "x", "Mới", "d");
        Assert.Single(new NotificationCenter(new FileNotificationStore(path), clock.Get).Snapshot());
    }

    // ---------------- mất kết nối > 2 phút ----------------
    [Fact]
    public void Disconnect_over_two_minutes_raises_one_red_entry_and_reconnect_raises_one_green()
    {
        var clock = new Clock(); var c = NewCenter(clock);
        c.ReportConnection(syncEnabled: true, connected: false);
        clock.Now = clock.Now.AddSeconds(90); c.ReportConnection(true, false);
        Assert.Empty(c.Snapshot());                                    // chưa đủ 2 phút
        clock.Now = clock.Now.AddSeconds(40); c.ReportConnection(true, false);
        Assert.Single(c.Snapshot());
        Assert.Equal(NotificationLevel.Red, c.Snapshot()[0].Level);
        clock.Now = clock.Now.AddMinutes(10); c.ReportConnection(true, false);
        Assert.Single(c.Snapshot());                                   // không báo lặp
        c.ReportConnection(true, true);
        Assert.Equal(2, c.Snapshot().Count);
        Assert.Equal(NotificationLevel.Success, c.Snapshot()[0].Level);
        c.ReportConnection(syncEnabled: false, connected: false);      // tắt đồng bộ: không báo mất kết nối
        clock.Now = clock.Now.AddMinutes(10); c.ReportConnection(false, false);
        Assert.Equal(2, c.Snapshot().Count);
    }

    [Fact]
    public void Main_view_model_reports_connection_through_the_sync_controller()
    {
        var clock = new Clock(); var rig = NewRig(clock);
        rig.Sync.SetDemoStatus(0, false, syncEnabled: true);
        rig.Vm.CheckSyncHealthOnce();
        clock.Now = clock.Now.AddMinutes(3);
        rig.Vm.CheckSyncHealthOnce();
        Assert.Contains(rig.Center.Snapshot(), e => e.Title.Contains("Mất kết nối") && e.Target == "phone_sync");
    }

    // ---------------- vàng/đỏ, mỗi đơn mỗi mức một lần ----------------
    [Fact]
    public async Task Yellow_and_red_alerts_create_one_entry_per_order_and_level_and_not_again_even_after_restart()
    {
        var clock = new Clock(); var rig = NewRig(clock);
        var r = await rig.Vm.CheckAlertNotificationsAsync();
        var n = r.NewYellowCount + r.NewRedCount;
        Assert.True(n > 0);
        var alerts = rig.Center.Snapshot().Where(e => e.Source.StartsWith("alert")).ToList();
        Assert.Equal(n > 10 ? 1 : n, alerts.Count);
        Assert.All(alerts, e => Assert.True(e.Level is NotificationLevel.Red or NotificationLevel.Yellow));
        var before = rig.Center.Snapshot().Count;
        await rig.Vm.CheckAlertNotificationsAsync();                    // lần 2: không có đơn mới đổi mức
        Assert.Equal(before, rig.Center.Snapshot().Count);
        var restarted = NewRig(clock, center: rig.Center, alertState: rig.AlertState);   // "khởi động lại": cùng bộ đã báo
        await restarted.Vm.CheckAlertNotificationsAsync();
        Assert.Equal(before, rig.Center.Snapshot().Count);
    }

    [Fact]
    public async Task Alerts_turned_off_create_no_yellow_or_red_entries()
    {
        var clock = new Clock();
        var store = new UiSettingsStore(null, true);
        var rig = NewRig(clock, settings: store);
        rig.Vm.NotifyAlerts = false;
        await rig.Vm.CheckAlertNotificationsAsync();
        Assert.DoesNotContain(rig.Center.Snapshot(), e => e.Source.StartsWith("alert"));
    }

    // ---------------- sự kiện điện thoại ----------------
    [Fact]
    public void Phone_events_become_entries_with_the_right_text_and_navigation_target()
    {
        var clock = new Clock(); var rig = NewRig(clock);
        var t = clock.Now;
        rig.Sync.EmitDemoHandled(new SyncHandled("r1", "SPXNEW1", "success", "ĐÃ TẠO ĐƠN MỚI Ở BẢNG CHÍNH (Shopee)", t, "scan", "new_order"));
        rig.Sync.EmitDemoHandled(new SyncHandled("r2", "SPXA", "success", "Đã nhận kiện", t, "scan"));
        rig.Sync.EmitDemoHandled(new SyncHandled("r3", "SPXB", "success", "ok", t, "delete"));
        rig.Sync.EmitDemoHandled(new SyncHandled("r4", "SPXC", "success", "ok", t, "note"));
        rig.Sync.EmitDemoHandled(new SyncHandled("r5", "SPXD", "success", "ok", t, "edit"));
        rig.Sync.EmitDemoHandled(new SyncHandled("r6", "SPXE", "success", "Đã lưu ảnh", t, "photo"));   // ảnh: không thông báo
        var titles = rig.Center.Snapshot().Select(e => e.Title).ToList();
        Assert.Equal(5, titles.Count);
        Assert.Contains("Điện thoại tạo đơn mới", titles);
        Assert.Contains("Điện thoại quét nhận kiện", titles);
        Assert.Contains("Điện thoại xóa đơn", titles);
        Assert.Contains("Điện thoại cập nhật ghi chú", titles);
        Assert.Contains("Điện thoại cập nhật trạng thái đơn", titles);
        Assert.Equal("search:SPXNEW1", rig.Center.Snapshot().Single(e => e.Title == "Điện thoại tạo đơn mới").Target);
        Assert.Equal("deleted", rig.Center.Snapshot().Single(e => e.Title == "Điện thoại xóa đơn").Target);
    }

    [Fact]
    public void The_same_phone_request_delivered_twice_does_not_notify_twice()
    {
        var clock = new Clock(); var rig = NewRig(clock);
        var h = new SyncHandled("dup-1", "SPXN", "success", "ok", clock.Now, "note");
        rig.Sync.EmitDemoHandled(h); rig.Sync.EmitDemoHandled(h);
        Assert.Single(rig.Center.Snapshot());
    }

    // ---------------- nạp đơn xong ----------------
    [Fact]
    public async Task Imports_from_every_route_create_entries_and_failures_are_red()
    {
        var clock = new Clock();
        var file = new FakeFileImportController((_, _, _) =>
        {
            var r = new FileImportResult { Ok = true, TikTok = new TikTokImportResult { Added = 4, Updated = 2 } };
            r.TikTok!.Ambiguous.Add(new AmbiguousParcel("TikTok Shop", "O1", "T1", "R1", 2));
            return r;
        });
        var rig = NewRig(clock, file: file);
        await file.ImportAsync(new[] { "a.xlsx" }, new TikTokMapOptions(), ImportProductMode.Replace);
        rig.Sheet.EmitCompleted(new SheetRunResult { Ran = true, Ok = true, Import = new TikTokImportResult { Added = 1, Updated = 5 } });
        rig.Folder.EmitCompleted(new FolderWatchRunResult { Ran = true, Ok = true, FilesImported = 1, Import = new TikTokImportResult { Added = 3 } });
        rig.Sheet.EmitCompleted(new SheetRunResult { Ran = true, Ok = false, Note = "Lỗi mạng khi gọi https://script.google.com/macros/s/SECRET_ID_999/exec" });
        var all = rig.Center.Snapshot();
        Assert.Contains(all, e => e.Title == "Đã nạp (file Excel)" && e.Detail == "Thêm 4, cập nhật 2" && e.Target == "file_import");
        Assert.Contains(all, e => e.Title.Contains("nhập nhằng") && e.Level == NotificationLevel.Yellow);
        Assert.Contains(all, e => e.Title == "Đã nạp (Google Sheet)" && e.Detail == "Thêm 1, cập nhật 5");
        Assert.Contains(all, e => e.Title == "Đã nạp (theo dõi thư mục)" && e.Target == "folder_watch");
        var fail = all.Single(e => e.Level == NotificationLevel.Red);
        Assert.Equal("google_sheet", fail.Target);
        Assert.DoesNotContain("script.google.com", fail.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET_ID_999", fail.Detail);
        Assert.All(all, e => Assert.DoesNotContain("script.google.com", e.Detail + e.Title, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_import_with_nothing_changed_does_not_notify()
    {
        var clock = new Clock(); var rig = NewRig(clock);
        rig.Sheet.EmitCompleted(new SheetRunResult { Ran = true, Ok = true, Import = new TikTokImportResult() });
        rig.Folder.EmitCompleted(new FolderWatchRunResult { Ran = false });
        Assert.Empty(rig.Center.Snapshot());
    }

    // ---------------- bấm thông báo điều hướng ----------------
    [Fact]
    public void Clicking_an_entry_marks_it_read_closes_the_list_and_navigates()
    {
        var clock = new Clock(); var rig = NewRig(clock);
        rig.Center.Add(NotificationLevel.Success, "file", "Đã nạp (file Excel)", "Thêm 1", "file_import");
        rig.Center.Add(NotificationLevel.Red, "alert:X:r", "Đơn quá 24h chưa nhận", "SPXTHU", "search:SPXTHU");
        rig.Vm.NotificationsVm.ToggleCommand.Execute(null);
        Assert.True(rig.Vm.NotificationsVm.IsOpen);
        Assert.Equal(2, rig.Vm.NotificationsVm.UnreadCount);
        var excel = rig.Vm.NotificationsVm.Items.Single(i => i.Target == "file_import");
        rig.Vm.NotificationsVm.OpenItemCommand.Execute(excel);
        Assert.False(rig.Vm.NotificationsVm.IsOpen);
        Assert.Equal(1, rig.Vm.NotificationsVm.UnreadCount);
        Assert.IsType<FileImportViewModel>(rig.Vm.CurrentPage);
        rig.Vm.NotificationsVm.ToggleCommand.Execute(null);
        var order = rig.Vm.NotificationsVm.Items.Single(i => i.Target == "search:SPXTHU");
        rig.Vm.NotificationsVm.OpenItemCommand.Execute(order);
        Assert.Equal("SPXTHU", rig.Vm.GlobalSearchText);
        Assert.Equal(0, rig.Vm.NotificationsVm.UnreadCount);
    }

    [Fact]
    public void Clear_all_only_clears_notifications_and_leaves_order_data_alone()
    {
        var clock = new Clock(); var rig = NewRig(clock);
        var before = rig.Vm.ShopeeReturnVm.TotalCount + rig.Vm.TikTokReturnVm.TotalCount + rig.Vm.ArchivedOrdersVm.TotalCount;
        rig.Center.Add(NotificationLevel.Info, "a", "T", "d");
        rig.Vm.NotificationsVm.ClearAllCommand.Execute(null);
        Assert.True(rig.Vm.NotificationsVm.IsEmpty);
        Assert.Equal(before, rig.Vm.ShopeeReturnVm.TotalCount + rig.Vm.TikTokReturnVm.TotalCount + rig.Vm.ArchivedOrdersVm.TotalCount);
    }

    // ---------------- demo không chạm đĩa ----------------
    [Fact]
    public void Demo_mode_seeds_sample_entries_in_memory_only()
    {
        var d = new FakeDataService(enableMutableDemoStore: true);
        using var vm = new MainViewModel(d, d, d, d, new FakeLegacyImporter(d.GetCurrentTime), isDemoMode: true,
            uiSettingsStore: new UiSettingsStore(null, true), dispatchToUi: a => a());
        Assert.True(vm.NotificationsVm.UnreadCount >= 5);
        var kinds = vm.NotificationsVm.Items.Select(i => i.LevelKey).Distinct().ToList();
        Assert.Contains("Red", kinds); Assert.Contains("Yellow", kinds); Assert.Contains("Success", kinds); Assert.Contains("Info", kinds);
        Assert.False(File.Exists(AppPaths.NotificationsPath) && File.GetLastWriteTime(AppPaths.NotificationsPath) > DateTime.Now.AddMinutes(-1));
    }

    [Fact]
    public void Sanitize_hides_addresses_and_tokens()
    {
        var s = NotificationCenter.Sanitize("Lỗi https://script.google.com/macros/s/ABC/exec và token=xyz đã gặp");
        Assert.DoesNotContain("script.google.com", s); Assert.DoesNotContain("xyz", s); Assert.DoesNotContain("ABC", s);
        Assert.Contains("Lỗi", s);
        Assert.Equal("", NotificationCenter.Sanitize(null));
    }

    [Theory]
    [InlineData(10, "vừa xong")]
    [InlineData(150, "2 phút trước")]
    [InlineData(7300, "2 giờ trước")]
    [InlineData(200000, "2 ngày trước")]
    public void Relative_time_text(int seconds, string expected)
    {
        var now = new DateTime(2026, 11, 24, 12, 0, 0);
        Assert.Equal(expected, NotificationCenter.RelativeTime(now.AddSeconds(-seconds), now));
    }

    // ---------------- ảnh chụp ----------------
    [Fact]
    public void RenderScreenshots_Bell_ToReportsFolder()
    {
        var repo = FindRepoRoot();
        var outDir = Path.Combine(repo, "tasks", "reports", "T027-screens");
        Directory.CreateDirectory(outDir);
        WpfTestHost.Run(() =>
        {
            var d = new FakeDataService(enableMutableDemoStore: true);
            using var vm = new MainViewModel(d, d, d, d, new FakeLegacyImporter(d.GetCurrentTime), isDemoMode: true,
                uiSettingsStore: new UiSettingsStore(null, true), dispatchToUi: a => a());
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
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "01-bell-unread.png"));
                vm.NotificationsVm.ToggleCommand.Execute(null);
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "02-list-light.png"));
                vm.SetTheme(ThemeService.DarkTheme);
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "03-list-dark.png"));
                vm.SetTheme(ThemeService.LightTheme);
                vm.NotificationsVm.ClearAllCommand.Execute(null);
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "04-empty.png"));
            }
            finally
            {
                ThemeService.ApplyTheme(ThemeService.LightTheme);
                window.Close();
                WpfTestHost.PumpDispatcher();
            }
        });
        Assert.True(File.Exists(Path.Combine(outDir, "04-empty.png")));
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
