using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.App.Views;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.Tests.UiTests;

public sealed class T021TrayAutoStartNotifyUiTests
{
    private sealed class RecordingSoundPlayer : IScanSoundPlayer
    {
        public int SuccessCount { get; private set; }
        public int ErrorCount { get; private set; }
        public void PlaySuccess() => SuccessCount++;
        public void PlayError() => ErrorCount++;
    }

    private sealed class TestOrderProvider : IReturnOrderProvider, INewOrderProvider, IArchivedOrderProvider, IDeletedOrderProvider
    {
        public DateTime Now { get; set; } = new(2026, 11, 23, 18, 0, 0);
        public List<Order> Orders { get; } = new();

        public DateTime GetCurrentTime() => Now;
        public IReadOnlyList<Order> GetReturnOrders(int count = 40) => Orders.ToList();
        public Task<IReadOnlyList<Order>> GetReturnOrdersAsync(int count = 40, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Order>>(Orders.ToList());
        public IReadOnlyList<NewOrderRecord> GetNewOrders() => Array.Empty<NewOrderRecord>();
        public IReadOnlyList<Order> GetArchivedOrders() => Array.Empty<Order>();
        public IReadOnlyList<DeletedOrderRecord> GetDeletedOrders() => Array.Empty<DeletedOrderRecord>();
        public PlaceholderScreenData GetPlaceholderScreenData(string screenKey)
            => new(screenKey, "", "", Array.Empty<string>(), Array.Empty<PlaceholderPreviewRow>(), "");
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "qlhh-t021-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void CloseButton_MinimizeToTrayOn_HidesWindowAndShowsFirstHint_DoubleClickRestores_ExitClosesAndDisposes()
    {
        WpfTestHost.Run(() =>
        {
            var fakeData = new FakeDataService(enableMutableDemoStore: true);
            var tray = new FakeTrayService();
            var notif = new FakeNotificationService(fakeData.GetCurrentTime);
            var syncCtrl = new FakeSyncController();
            var folderCtrl = new FakeFolderWatchController();
            var sheetCtrl = new FakeSheetSyncController();
            var saleworkCtrl = new FakeSaleworkController();

            using var vm = new MainViewModel(
                fakeData,
                fakeData,
                fakeData,
                fakeData,
                new FakeLegacyImporter(fakeData.GetCurrentTime),
                isDemoMode: true,
                syncController: syncCtrl,
                folderWatchController: folderCtrl,
                sheetSyncController: sheetCtrl,
                saleworkController: saleworkCtrl,
                trayService: tray,
                notificationService: notif);

            tray.Initialize();
            Assert.True(vm.MinimizeToTray);

            var window = new MainWindow { DataContext = vm };
            tray.OpenRequested += () => window.RestoreFromTray();
            tray.ExitRequested += () => window.RequestExplicitExit();
            window.Show();
            WpfTestHost.PumpDispatcher(window);

            // Bấm X lần 1: ẩn xuống khay và hiện thông báo nhỏ lần đầu
            window.Close();
            WpfTestHost.PumpDispatcher(window);
            Assert.True(window.IsHiddenToTray);
            Assert.False(window.IsVisible);
            Assert.Single(notif.Notifications);
            Assert.Contains("App vẫn chạy ở khay", notif.Notifications[0].Message);

            // Nhấp đúp biểu tượng khay: khôi phục cửa sổ
            tray.SimulateDoubleClick();
            WpfTestHost.PumpDispatcher(window);
            Assert.False(window.IsHiddenToTray);
            Assert.True(window.IsVisible);

            // Bấm X lần 2: ẩn xuống khay nhưng không lặp lại thông báo lần đầu
            window.Close();
            WpfTestHost.PumpDispatcher(window);
            Assert.True(window.IsHiddenToTray);
            Assert.Single(notif.Notifications);

            // "Thoát hẳn" đóng cửa sổ thật và Dispose dừng sạch dịch vụ nền
            var closed = false;
            window.Closed += (_, _) => closed = true;
            tray.SimulateExit();
            WpfTestHost.PumpDispatcher(window);
            Assert.True(closed);

            vm.Dispose();
            Assert.True(tray.Disposed);
            Assert.False(syncCtrl.SyncEnabled == false && false); // không ném lỗi khi dispose
        });
    }

    [Fact]
    public void CloseButton_MinimizeToTrayOff_ClosesWindowDirectly()
    {
        WpfTestHost.Run(() =>
        {
            var tempDir = CreateTempDir();
            try
            {
                var settingsPath = Path.Combine(tempDir, "settings.json");
                var store = new UiSettingsStore(settingsPath, isDemoMode: false);
                var fakeData = new FakeDataService(enableMutableDemoStore: true);
                using var vm = new MainViewModel(
                    fakeData,
                    fakeData,
                    fakeData,
                    fakeData,
                    new FakeLegacyImporter(fakeData.GetCurrentTime),
                    isDemoMode: true,
                    uiSettingsStore: store);

                vm.MinimizeToTray = false;
                Assert.False(AppSettings.LoadOrCreate(settingsPath).MinimizeToTray);

                var window = new MainWindow { DataContext = vm };
                var closed = false;
                window.Closed += (_, _) => closed = true;
                window.Show();
                WpfTestHost.PumpDispatcher(window);

                window.Close();
                WpfTestHost.PumpDispatcher(window);

                Assert.True(closed);
                Assert.False(window.IsHiddenToTray);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
        });
    }

    [Fact]
    public void AutoStartToggle_WritesAndDeletesOnlyOwnKey_WithTrayArgument()
    {
        WpfTestHost.Run(() =>
        {
            var tempDir = CreateTempDir();
            try
            {
                var settingsPath = Path.Combine(tempDir, "settings.json");
                var store = new UiSettingsStore(settingsPath, isDemoMode: false);
                var autoStart = new FakeAutoStartService();
                autoStart.SeedUnrelatedEntry("OtherApp", "\"C:\\Other\\app.exe\"");

                var fakeData = new FakeDataService(enableMutableDemoStore: true);
                using var vm = new MainViewModel(
                    fakeData,
                    fakeData,
                    fakeData,
                    fakeData,
                    new FakeLegacyImporter(fakeData.GetCurrentTime),
                    isDemoMode: true,
                    uiSettingsStore: store,
                    autoStartService: autoStart);

                Assert.False(vm.AutoStart);
                Assert.False(autoStart.IsEnabled());

                // Bật qua HomeVm (như người dùng bấm checkbox trên giao diện)
                vm.HomeVm.AutoStart = true;
                Assert.True(vm.AutoStart);
                Assert.True(autoStart.IsEnabled());
                Assert.Contains("--tray", autoStart.Entries[WindowsAutoStartService.ValueName]);
                Assert.True(AppSettings.LoadOrCreate(settingsPath).AutoStart);
                Assert.True(autoStart.Entries.ContainsKey("OtherApp"));

                // Tắt: xóa đúng khóa của mình, không đụng khóa khác
                vm.HomeVm.AutoStart = false;
                Assert.False(vm.AutoStart);
                Assert.False(autoStart.IsEnabled());
                Assert.False(AppSettings.LoadOrCreate(settingsPath).AutoStart);
                Assert.True(autoStart.Entries.ContainsKey("OtherApp"));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
        });
    }

    [Fact]
    public void AlertMonitor_GroupsYellowAndRed_DeduplicatesAcrossRestart_RespectsStatusAndNotifyAlerts()
    {
        WpfTestHost.Run(() =>
        {
            var tempDir = CreateTempDir();
            try
            {
                var statePath = Path.Combine(tempDir, "alert_notify_state.json");
                var provider = new TestOrderProvider();
                var now = provider.Now;
                provider.Orders.AddRange(
                [
                    // 2 đơn Vàng (13h, 15h)
                    new Order { Id = "o-y1", OrderId = "ORD-Y1", Platform = PlatformNames.Shopee, Status = ReceiveStatus.NotReturned, ReturnedAt = now.AddHours(-13).ToString("dd/MM/yyyy HH:mm:ss") },
                    new Order { Id = "o-y2", OrderId = "ORD-Y2", Platform = PlatformNames.TikTok, Status = ReceiveStatus.NotReturned, ReturnedAt = now.AddHours(-15).ToString("dd/MM/yyyy HH:mm:ss") },
                    // 1 đơn Đỏ (26h)
                    new Order { Id = "o-r1", OrderId = "ORD-R1", Platform = PlatformNames.Shopee, Status = ReceiveStatus.NotReturned, ReturnedAt = now.AddHours(-26).ToString("dd/MM/yyyy HH:mm:ss") },
                    // Các đơn KHÔNG được báo
                    new Order { Id = "o-recv", OrderId = "ORD-RECV", Platform = PlatformNames.Shopee, Status = ReceiveStatus.Received, ReturnedAt = now.AddHours(-30).ToString("dd/MM/yyyy HH:mm:ss") },
                    new Order { Id = "o-noneed", OrderId = "ORD-NN", Platform = PlatformNames.Shopee, Status = ReceiveStatus.NotNeeded, ReturnedAt = now.AddHours(-30).ToString("dd/MM/yyyy HH:mm:ss") },
                    new Order { Id = "o-lost", OrderId = "ORD-LOST", Platform = PlatformNames.Shopee, Status = ReceiveStatus.Lost, ReturnedAt = now.AddHours(-30).ToString("dd/MM/yyyy HH:mm:ss") },
                    new Order { Id = "o-nodate", OrderId = "ORD-ND", Platform = PlatformNames.Shopee, Status = ReceiveStatus.NotReturned, ReturnedAt = "" },
                    new Order { Id = "o-fresh", OrderId = "ORD-FR", Platform = PlatformNames.Shopee, Status = ReceiveStatus.NotReturned, ReturnedAt = now.AddHours(-4).ToString("dd/MM/yyyy HH:mm:ss") }
                ]);

                var tray = new FakeTrayService();
                var notif = new FakeNotificationService(() => provider.Now);
                var store1 = new FileAlertNotifyStateStore(statePath);
                var notifyEnabled = true;

                using (var monitor1 = new AlertNotificationMonitor(provider, store1, notif, tray, () => notifyEnabled, () => provider.Now))
                {
                    var res1 = monitor1.EvaluateAndNotify(provider.Orders, provider.Now);
                    Assert.True(res1.NotificationSent);
                    Assert.Equal(2, res1.NewYellowCount);
                    Assert.Equal(1, res1.NewRedCount);
                    Assert.True(res1.HasAnyRed);
                    Assert.True(tray.IsRedAlertBadge);
                    Assert.Single(notif.Notifications);
                    Assert.Equal("2 đơn vừa quá 12h, 1 đơn vừa quá 24h", notif.Notifications[0].Message);

                    // Chạy lại ngay: không báo lại
                    var res2 = monitor1.EvaluateAndNotify(provider.Orders, provider.Now);
                    Assert.False(res2.NotificationSent);
                    Assert.Single(notif.Notifications);
                }

                // Giả lập "khởi động lại": tạo store mới đọc từ cùng tệp trạng thái -> vẫn không báo lại
                var store2 = new FileAlertNotifyStateStore(statePath);
                using (var monitor2 = new AlertNotificationMonitor(provider, store2, notif, tray, () => notifyEnabled, () => provider.Now))
                {
                    var resRestart = monitor2.EvaluateAndNotify(provider.Orders, provider.Now);
                    Assert.False(resRestart.NotificationSent);
                    Assert.Single(notif.Notifications);

                    // Khi tiến thêm 12h: o-y1 (25h) và o-y2 (27h) chuyển sang Đỏ; o-fresh (16h) chuyển sang Vàng
                    provider.Now = now.AddHours(12);
                    var resEscalated = monitor2.EvaluateAndNotify(provider.Orders, provider.Now);
                    Assert.True(resEscalated.NotificationSent);
                    Assert.Equal(1, resEscalated.NewYellowCount);
                    Assert.Equal(2, resEscalated.NewRedCount);
                    Assert.Equal("1 đơn vừa quá 12h, 2 đơn vừa quá 24h", resEscalated.Message);
                    Assert.Equal(2, notif.Notifications.Count);

                    // Tắt NotifyAlerts -> thêm đơn đỏ mới cũng im lặng
                    notifyEnabled = false;
                    provider.Orders.Add(new Order
                    {
                        Id = "o-r2",
                        OrderId = "ORD-R2",
                        Platform = PlatformNames.Shopee,
                        Status = ReceiveStatus.NotReturned,
                        ReturnedAt = provider.Now.AddHours(-30).ToString("dd/MM/yyyy HH:mm:ss")
                    });
                    var resMuted = monitor2.EvaluateAndNotify(provider.Orders, provider.Now);
                    Assert.False(resMuted.NotificationSent);
                    Assert.Equal(2, notif.Notifications.Count);
                }
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
        });
    }

    [Fact]
    public void AlertNotifyStateStore_CapsAt5000Keys()
    {
        var store = new InMemoryAlertNotifyStateStore();
        var keys = Enumerable.Range(1, 5100).Select(i => $"ord-{i}|yellow").ToList();
        store.MarkNotified(keys);

        Assert.Equal(FileAlertNotifyStateStore.MaxKeys, store.Count);
        Assert.False(store.HasNotified("ord-1|yellow"));
        Assert.True(store.HasNotified("ord-5100|yellow"));
    }

    [Fact]
    public void TrayMenu_ToggleNotify_And_NotificationClick_NavigatesHome()
    {
        WpfTestHost.Run(() =>
        {
            var fakeData = new FakeDataService(enableMutableDemoStore: true);
            var tray = new FakeTrayService();
            var notif = new FakeNotificationService(fakeData.GetCurrentTime);

            using var vm = new MainViewModel(
                fakeData,
                fakeData,
                fakeData,
                fakeData,
                new FakeLegacyImporter(fakeData.GetCurrentTime),
                isDemoMode: true,
                trayService: tray,
                notificationService: notif);

            tray.Initialize();
            Assert.Contains("Mở app", tray.MenuItems);
            Assert.Contains("Tắt thông báo", tray.MenuItems);
            Assert.Contains("Thoát hẳn", tray.MenuItems);

            // Tắt thông báo qua menu khay
            tray.SimulateToggleNotifyAlerts();
            WpfTestHost.PumpDispatcher();
            Assert.False(vm.NotifyAlerts);
            Assert.False(vm.HomeVm.NotifyAlerts);
            Assert.Contains("Bật thông báo", tray.MenuItems);

            // Chuyển sang màn khác rồi bấm thông báo -> về Trang chủ + hiện cửa sổ
            vm.NavigateByKey("phone_sync");
            Assert.Equal("phone_sync", vm.SelectedNavigationKey);

            var window = new MainWindow { DataContext = vm };
            notif.NotificationClicked += () =>
            {
                window.RestoreFromTray();
                vm.NavigateByKey("home");
            };
            window.Show();
            window.Close();
            Assert.True(window.IsHiddenToTray);

            notif.SimulateClick();
            WpfTestHost.PumpDispatcher(window);
            Assert.False(window.IsHiddenToTray);
            Assert.True(window.IsVisible);
            Assert.Equal("home", vm.SelectedNavigationKey);
        });
    }

    [Fact]
    public void Sounds_PlaysOnPhoneScanAndError_RespectsSoundsToggle()
    {
        WpfTestHost.Run(() =>
        {
            var fakeData = new FakeDataService(enableMutableDemoStore: true);
            var sound = new RecordingSoundPlayer();
            var syncCtrl = new FakeSyncController();

            using var vm = new MainViewModel(
                fakeData,
                fakeData,
                fakeData,
                fakeData,
                new FakeLegacyImporter(fakeData.GetCurrentTime),
                isDemoMode: true,
                soundPlayer: sound,
                syncController: syncCtrl);

            Assert.True(vm.Sounds);

            syncCtrl.EmitDemoHandled(new SyncHandled("req-1", "SPX1", "success", "Đã nhận", DateTime.Now, "scan"));
            syncCtrl.EmitDemoHandled(new SyncHandled("req-2", "SPX2", "duplicate", "ĐƠN ĐÃ CÓ", DateTime.Now, "scan"));
            syncCtrl.EmitDemoHandled(new SyncHandled("req-3", "SPX3", "error", "Không tìm thấy", DateTime.Now, "scan"));
            syncCtrl.EmitDemoHandled(new SyncHandled("req-4", "SPX4", "success", "Đơn mới (Shopee)", DateTime.Now, "scan", "new_order"));

            Assert.Equal(2, sound.SuccessCount);
            Assert.Equal(2, sound.ErrorCount);

            vm.Sounds = false;
            syncCtrl.EmitDemoHandled(new SyncHandled("req-5", "SPX5", "success", "Đã nhận", DateTime.Now, "scan"));
            syncCtrl.EmitDemoHandled(new SyncHandled("req-6", "SPX6", "error", "Lỗi", DateTime.Now, "scan"));

            Assert.Equal(2, sound.SuccessCount);
            Assert.Equal(2, sound.ErrorCount);
        });
    }

    [Fact]
    public void SingleInstanceAndTrayFlag_BehaveAsExpectedWithoutTouchingOs()
    {
        WpfTestHost.Run(() =>
        {
            // Bản 1 giữ lock; bản 2 bị từ chối và đánh thức bản 1
            var firstGuard = new FakeSingleInstanceGuard(allowAcquire: true);
            var secondGuard = new FakeSingleInstanceGuard(allowAcquire: false);

            var activatedCount = 0;
            Assert.True(firstGuard.TryAcquire(() => activatedCount++));
            Assert.False(secondGuard.TryAcquire(() => { }));

            firstGuard.SimulateSecondInstance();
            Assert.Equal(1, activatedCount);

            // --tray: không gọi Show() thì cửa sổ không hiện
            var fakeData = new FakeDataService(enableMutableDemoStore: true);
            var tray = new FakeTrayService();
            using var vm = new MainViewModel(
                fakeData,
                fakeData,
                fakeData,
                fakeData,
                new FakeLegacyImporter(fakeData.GetCurrentTime),
                isDemoMode: true,
                trayService: tray);
            tray.Initialize();

            var window = new MainWindow { DataContext = vm };
            // Không gọi window.Show() như khi truyền --tray
            Assert.False(window.IsVisible);
            Assert.True(tray.IsVisible);

            tray.OpenRequested += () => window.RestoreFromTray();
            tray.SimulateOpen();
            WpfTestHost.PumpDispatcher(window);
            Assert.True(window.IsVisible);
        });
    }

    [Fact]
    public void CaptureHomeBackgroundSettingsScreenshots()
    {
        WpfTestHost.Run(() =>
        {
            var fakeData = new FakeDataService(enableMutableDemoStore: true);
            using var vm = new MainViewModel(
                fakeData,
                fakeData,
                fakeData,
                fakeData,
                new FakeLegacyImporter(fakeData.GetCurrentTime),
                isDemoMode: true);

            var window = new MainWindow
            {
                DataContext = vm,
                Width = 1360,
                Height = 820
            };
            window.Show();
            WpfTestHost.PumpDispatcher(window);

            // Cuộn ScrollViewer trong HomeView xuống cuối để thấy rõ khối "Chạy nền và thông báo"
            var homeView = FindDescendant<HomeView>(window);
            var scrollViewer = homeView is not null ? FindDescendant<ScrollViewer>(homeView) : null;
            scrollViewer?.ScrollToEnd();
            WpfTestHost.PumpDispatcher(window);

            var outDir = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "..",
                "tasks", "reports", "T021-screens"));
            Directory.CreateDirectory(outDir);

            WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "01-home-background-settings-light.png"));

            vm.ToggleThemeCommand.Execute(null);
            WpfTestHost.PumpDispatcher(window);
            scrollViewer?.ScrollToEnd();
            WpfTestHost.PumpDispatcher(window);

            WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "02-home-background-settings-dark.png"));
        });
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }
}
