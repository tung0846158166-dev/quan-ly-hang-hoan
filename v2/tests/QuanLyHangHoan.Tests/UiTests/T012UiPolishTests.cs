using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.Tests.UiTests;

public sealed class T012UiPolishTests : IDisposable
{
    private readonly string _tempRoot;

    public T012UiPolishTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "QuanLyHangHoan_T012_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* ignore temp cleanup */ }
        }
    }

    private string CreateSettingsFile(
        string? room = null,
        bool syncEnabled = false,
        bool ntfyEnabled = true,
        string theme = "light",
        List<string>? collapsedGroups = null,
        string lastPage = "",
        WindowPlacement? window = null)
    {
        var path = Path.Combine(_tempRoot, "settings_" + Guid.NewGuid().ToString("N")[..8] + ".json");
        var settings = new AppSettings
        {
            Room = room ?? "ROOM-TEST-1234567890",
            SyncEnabled = syncEnabled,
            NtfyEnabled = ntfyEnabled,
            Brokers = new List<string> { "wss://broker-a.example/mqtt", "wss://broker-b.example/mqtt" },
            Theme = theme,
            CollapsedNavGroups = collapsedGroups ?? new List<string>(),
            LastPage = lastPage,
            Window = window
        };
        settings.Save(path);
        return path;
    }

    private static MainViewModel CreateMainVm(string? settingsPath, bool isDemoMode = true)
    {
        var fakeData = new FakeDataService();
        return new MainViewModel(
            fakeData,
            fakeData,
            fakeData,
            fakeData,
            new FakeLegacyImporter(fakeData.GetCurrentTime),
            isDemoMode: isDemoMode,
            syncController: new FakeSyncController("ROOM-DEMO-123456"),
            uiSettingsStore: new UiSettingsStore(settingsPath, isDemoMode: isDemoMode));
    }

    [Fact]
    public void SidebarGroups_CollapseAndExpand_PersistAndReload()
    {
        var settingsPath = CreateSettingsFile();

        using (var vm1 = CreateMainVm(settingsPath, isDemoMode: false))
        {
            Assert.False(vm1.IsGroupCollapsed("SHOPEE"));
            Assert.True(vm1.NavigationItems.Single(n => n.Key == "shopee_return").IsVisible);
            Assert.True(vm1.NavigationItems.Single(n => n.Key == "shopee_cancelled").IsVisible);

            vm1.ToggleNavGroupCommand.Execute("SHOPEE");

            Assert.True(vm1.IsGroupCollapsed("SHOPEE"));
            Assert.False(vm1.NavigationItems.Single(n => n.Key == "shopee_return").IsVisible);
            Assert.False(vm1.NavigationItems.Single(n => n.Key == "shopee_cancelled").IsVisible);
            Assert.True(vm1.NavigationItems.Single(n => n.Key == "shopee_return").ShowCollapsedGroupBadge);
            Assert.False(string.IsNullOrWhiteSpace(vm1.NavigationItems.Single(n => n.Key == "shopee_return").GroupBadge));
        }

        // Nạp lại từ tệp cài đặt
        using (var vm2 = CreateMainVm(settingsPath, isDemoMode: false))
        {
            Assert.True(vm2.IsGroupCollapsed("SHOPEE"));
            Assert.False(vm2.NavigationItems.Single(n => n.Key == "shopee_return").IsVisible);
            Assert.False(vm2.NavigationItems.Single(n => n.Key == "shopee_cancelled").IsVisible);

            // Mở lại nhóm SHOPEE
            vm2.ToggleNavGroupCommand.Execute("SHOPEE");
            Assert.False(vm2.IsGroupCollapsed("SHOPEE"));
            Assert.True(vm2.NavigationItems.Single(n => n.Key == "shopee_return").IsVisible);
        }

        var reloaded = AppSettings.LoadOrCreate(settingsPath);
        Assert.DoesNotContain("SHOPEE", reloaded.CollapsedNavGroups);
    }

    [Fact]
    public void Sidebar_SelectedItem_RemainsVisibleEvenWhenItsGroupIsCollapsed()
    {
        var settingsPath = CreateSettingsFile(
            collapsedGroups: new List<string> { "CHUNG" },
            lastPage: "scan_receive");

        using var vm = CreateMainVm(settingsPath, isDemoMode: false);

        Assert.Equal("scan_receive", vm.SelectedNavigationKey);
        Assert.True(vm.IsGroupCollapsed("CHUNG"));

        // Mục đang chọn ("scan_receive") phải luôn hiển thị dù nhóm CHUNG đang thu gọn
        Assert.True(vm.NavigationItems.Single(n => n.Key == "scan_receive").IsVisible);
        // Các mục khác trong nhóm CHUNG vẫn ẩn
        Assert.False(vm.NavigationItems.Single(n => n.Key == "phone_sync").IsVisible);
        Assert.False(vm.NavigationItems.Single(n => n.Key == "archive").IsVisible);

        // Nếu đang đứng ở Trang chủ và thu gọn nhóm SHOPEE, rồi chọn shopee_cancelled => shopee_cancelled phải hiện
        vm.NavigateByKey("home");
        vm.ToggleNavGroupCommand.Execute("SHOPEE");
        Assert.False(vm.NavigationItems.Single(n => n.Key == "shopee_cancelled").IsVisible);

        vm.NavigateByKey("shopee_cancelled");
        Assert.True(vm.NavigationItems.Single(n => n.Key == "shopee_cancelled").IsVisible);
        Assert.False(vm.NavigationItems.Single(n => n.Key == "shopee_return").IsVisible);
    }

    [Fact]
    public void Theme_TogglePersists_AndInvalidThemeFallsBackToLight()
    {
        var settingsPath = CreateSettingsFile(theme: "neon-invalid-theme");

        using (var vm = CreateMainVm(settingsPath, isDemoMode: false))
        {
            Assert.Equal("light", vm.CurrentTheme);
            Assert.False(vm.IsDarkTheme);

            vm.ToggleThemeCommand.Execute(null);
            Assert.Equal("dark", vm.CurrentTheme);
            Assert.True(vm.IsDarkTheme);
        }

        var saved = AppSettings.LoadOrCreate(settingsPath);
        Assert.Equal("dark", saved.Theme);

        using (var vm2 = CreateMainVm(settingsPath, isDemoMode: false))
        {
            Assert.Equal("dark", vm2.CurrentTheme);
            Assert.True(vm2.IsDarkTheme);
        }
    }

    [Fact]
    public void LastPage_InvalidFallsBackToHome_AndValidRestores()
    {
        var settingsInvalid = CreateSettingsFile(lastPage: "non_existent_page_xyz");
        using (var vm1 = CreateMainVm(settingsInvalid, isDemoMode: false))
        {
            Assert.Equal("home", vm1.SelectedNavigationKey);
            Assert.Same(vm1.HomeVm, vm1.CurrentPage);
        }

        var settingsValid = CreateSettingsFile();
        using (var vm2 = CreateMainVm(settingsValid, isDemoMode: false))
        {
            vm2.NavigateByKey("tiktok_return");
            Assert.Equal("tiktok_return", vm2.SelectedNavigationKey);
        }

        using (var vm3 = CreateMainVm(settingsValid, isDemoMode: false))
        {
            Assert.Equal("tiktok_return", vm3.SelectedNavigationKey);
            Assert.Same(vm3.TikTokReturnVm, vm3.CurrentPage);
        }
    }

    [Fact]
    public void WindowPlacement_ValidatesScreenBounds_AndIgnoresMinimized()
    {
        var screens = new[]
        {
            new ScreenWorkArea(0, 0, 1920, 1040)
        };

        // Hợp lệ trong màn hình
        var valid = new WindowPlacement { Left = 100, Top = 80, Width = 1280, Height = 760, Maximized = false };
        Assert.True(WindowPlacementService.IsPlacementVisibleOnScreens(valid, screens));

        // Nằm ngoài màn hình (ví dụ màn hình phụ đã rút)
        var offScreen = new WindowPlacement { Left = 2500, Top = 100, Width = 1280, Height = 760, Maximized = false };
        Assert.False(WindowPlacementService.IsPlacementVisibleOnScreens(offScreen, screens));

        // Trôi quá cao (mất thanh tiêu đề)
        var tooHigh = new WindowPlacement { Left = 100, Top = -900, Width = 1280, Height = 760, Maximized = false };
        Assert.False(WindowPlacementService.IsPlacementVisibleOnScreens(tooHigh, screens));

        // Quá nhỏ
        var tooSmall = new WindowPlacement { Left = 100, Top = 100, Width = 200, Height = 150, Maximized = false };
        Assert.False(WindowPlacementService.IsPlacementVisibleOnScreens(tooSmall, screens));

        // Minimized không được lưu làm trạng thái mặc định
        var capturedMinimized = WindowPlacementService.CapturePlacement(
            WindowState.Minimized, 100, 100, 1280, 760, new Rect(100, 100, 1280, 760));
        Assert.Null(capturedMinimized);

        // Maximized dùng RestoreBounds
        var capturedMax = WindowPlacementService.CapturePlacement(
            WindowState.Maximized, -8, -8, 1936, 1056, new Rect(120, 90, 1300, 800));
        Assert.NotNull(capturedMax);
        Assert.True(capturedMax!.Maximized);
        Assert.Equal(120, capturedMax.Left);
        Assert.Equal(90, capturedMax.Top);
        Assert.Equal(1300, capturedMax.Width);
        Assert.Equal(800, capturedMax.Height);
    }

    [Fact]
    public void SavingUiSettings_DoesNotOverwriteSyncFields()
    {
        var expectedRoom = "ROOM-KEEP-9999999999";
        var expectedBrokers = new List<string> { "wss://custom-1.example/mqtt", "wss://custom-2.example/mqtt" };
        var settingsPath = CreateSettingsFile(
            room: expectedRoom,
            syncEnabled: true,
            ntfyEnabled: false,
            theme: "light");
        // Ghi đè Brokers tùy chỉnh
        var s = AppSettings.LoadOrCreate(settingsPath);
        s.Brokers = new List<string>(expectedBrokers);
        s.Save(settingsPath);

        using (var vm = CreateMainVm(settingsPath, isDemoMode: false))
        {
            vm.ToggleThemeCommand.Execute(null);
            vm.ToggleNavGroupCommand.Execute("TIKTOK");
            vm.NavigateByKey("stats");
            vm.SaveWindowPlacement(new WindowPlacement { Left = 50, Top = 50, Width = 1200, Height = 750 });
        }

        var after = AppSettings.LoadOrCreate(settingsPath);
        Assert.Equal(expectedRoom, after.Room);
        Assert.True(after.SyncEnabled);
        Assert.False(after.NtfyEnabled);
        Assert.Equal(expectedBrokers, after.Brokers);
        Assert.Equal("dark", after.Theme);
        Assert.Contains("TIKTOK", after.CollapsedNavGroups);
        Assert.Equal("stats", after.LastPage);
        Assert.NotNull(after.Window);
        Assert.Equal(1200, after.Window!.Width);
    }

    [Fact]
    public void DemoMode_NeverReadsOrWritesRealSettingsFile()
    {
        var realSettingsPath = Path.Combine(_tempRoot, "real_settings_should_not_exist.json");
        var demoStore = new UiSettingsStore(realSettingsPath, isDemoMode: true);
        var fakeData = new FakeDataService();

        using var vm = new MainViewModel(
            fakeData,
            fakeData,
            fakeData,
            fakeData,
            new FakeLegacyImporter(fakeData.GetCurrentTime),
            isDemoMode: true,
            syncController: new FakeSyncController("ROOM-DEMO"),
            uiSettingsStore: demoStore);

        vm.ToggleThemeCommand.Execute(null);
        vm.ToggleNavGroupCommand.Execute("SHOPEE");
        vm.NavigateByKey("shopee_return");
        vm.SaveWindowPlacement(new WindowPlacement { Left = 10, Top = 10, Width = 1200, Height = 700 });

        Assert.False(File.Exists(realSettingsPath));
    }

    [Fact]
    public void RenderScreenshots_LightAndDarkAndCollapsedSidebar_ToReportsFolder()
    {
        WpfTestHost.Run(() =>
        {
            var repoRoot = FindRepoRoot();
            var outDir = Path.Combine(repoRoot, "tasks", "reports", "T012-screens");
            Directory.CreateDirectory(outDir);

            using var vm = CreateMainVm(settingsPath: null, isDemoMode: true);
            var window = new MainWindow
            {
                DataContext = vm,
                Width = 1360,
                Height = 820,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowInTaskbar = false,
                ShowActivated = false
            };

            try
            {
                window.Show();
                WpfTestHost.PumpDispatcher();

                // 1. Trang chủ sáng
                vm.SetTheme(ThemeService.LightTheme);
                vm.NavigateByKey("home");
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "01-home-light.png"));

                // 2. Trang chủ tối + sidebar thu gọn nhóm TIKTOK & CHUNG
                vm.SetTheme(ThemeService.DarkTheme);
                if (!vm.IsGroupCollapsed("TIKTOK")) vm.ToggleNavGroupCommand.Execute("TIKTOK");
                if (!vm.IsGroupCollapsed("CHUNG")) vm.ToggleNavGroupCommand.Execute("CHUNG");
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "02-home-dark-collapsed-sidebar.png"));

                // 3. Bảng sàn Shopee Hoàn hàng ở chế độ tối
                vm.NavigateByKey("shopee_return");
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "03-shopee-return-dark.png"));

                // 4. Màn Quét nhận hàng ở chế độ tối (có kết quả quét mẫu để thấy độ tương phản)
                vm.NavigateByKey("scan_receive");
                vm.ScanReceiveVm.ScanInput = "SPX-DEMO-001";
                vm.ScanReceiveVm.SubmitScanCommand.Execute(null);
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "04-scan-receive-dark.png"));
            }
            finally
            {
                ThemeService.ApplyTheme(ThemeService.LightTheme);
                window.Close();
                WpfTestHost.PumpDispatcher();
            }
        });

        var screensDir = Path.Combine(FindRepoRoot(), "tasks", "reports", "T012-screens");
        Assert.True(File.Exists(Path.Combine(screensDir, "01-home-light.png")));
        Assert.True(File.Exists(Path.Combine(screensDir, "02-home-dark-collapsed-sidebar.png")));
        Assert.True(File.Exists(Path.Combine(screensDir, "03-shopee-return-dark.png")));
        Assert.True(File.Exists(Path.Combine(screensDir, "04-scan-receive-dark.png")));
    }

    private static void EnsureWpfApplication()
    {
        if (Application.Current is null)
        {
            var app = new QuanLyHangHoan.App.App();
            app.InitializeComponent();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new DispatcherOperationCallback(_ => { frame.Continue = false; return null; }),
            null);
        Dispatcher.PushFrame(frame);
    }

    private static void SaveWindowPng(Window window, string path)
    {
        window.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "v2")) && Directory.Exists(Path.Combine(dir.FullName, "tasks")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Không tìm thấy thư mục gốc repo.");
    }
}
