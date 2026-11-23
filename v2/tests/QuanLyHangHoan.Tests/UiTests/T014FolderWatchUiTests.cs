using System.IO;
using System.Text;
using System.Windows;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.FolderWatch;
using QuanLyHangHoan.Data.Import;
using Xunit;

namespace QuanLyHangHoan.Tests.UiTests;

public sealed class T014FolderWatchUiTests : IDisposable
{
    private static readonly DateTime FixedNow = new(2026, 11, 23, 14, 30, 0);
    private readonly string _tempRoot;

    public T014FolderWatchUiTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "qlhh-t014-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Bỏ qua khi xóa thư mục tạm
        }
    }

    [Fact]
    public void ConfigureAndEnable_SavesRealFolderWatchConfig()
    {
        var watchDir = Path.Combine(_tempRoot, "watch");
        Directory.CreateDirectory(watchDir);
        var dbPath = CreateInitializedTempDb();
        var statePath = Path.Combine(_tempRoot, "folder_watch.json");

        using var controller = new RealFolderWatchController(dbPath, statePath, () => FixedNow);
        using var vm = new FolderWatchViewModel(
            controller,
            folderPickerService: new StubFolderPicker(watchDir),
            confirmationService: new FixedConfirm(true));

        vm.BrowseFolderCommand.Execute(null);
        vm.IsEnabled = true;

        Assert.Equal(watchDir, vm.Dir);
        Assert.True(vm.IsEnabled);
        Assert.True(File.Exists(statePath));

        using var reloaded = new RealFolderWatchController(dbPath, statePath, () => FixedNow);
        Assert.Equal(watchDir, reloaded.Dir);
        Assert.True(reloaded.Enabled);
    }

    [Fact]
    public async Task RunNow_ImportsFakeTikTokCsv_AndReloadsMainViewModel()
    {
        var watchDir = Path.Combine(_tempRoot, "watch-run");
        Directory.CreateDirectory(watchDir);
        var dbPath = CreateInitializedTempDb();
        var statePath = Path.Combine(_tempRoot, "folder_watch.json");
        var settingsPath = Path.Combine(_tempRoot, "settings.json");

        var now = FixedNow;
        using var controller = new RealFolderWatchController(dbPath, statePath, () => now);
        controller.Configure(watchDir, enabled: true);

        // Ghi file TikTok giả SAU thời điểm bật và đã cũ hơn 3 giây so với đồng hồ test
        var csvPath = Path.Combine(watchDir, "tiktok_returns_fake.csv");
        WriteFakeTikTokCsv(csvPath, "TT-FAKE-ORD-001", "TT-FAKE-TRK-001");
        File.SetLastWriteTime(csvPath, now.AddSeconds(5));

        var provider = new SqliteOrderProvider(dbPath, () => now);
        var importer = new SqliteLegacyImporter(Path.Combine(_tempRoot, "copies"), dbPath, () => now);
        using var mainVm = new MainViewModel(
            provider,
            provider,
            provider,
            provider,
            importer,
            isDemoMode: false,
            deferInitialLoad: true,
            dispatchToUi: a => a(),
            uiSettingsStore: new UiSettingsStore(settingsPath, isDemoMode: false),
            folderWatchController: controller,
            toastDuration: TimeSpan.Zero);

        // Lần 1 ghi nhận kích thước file; lần 2 (sau >3s) nạp file thật vào DB tạm
        now = now.AddSeconds(10);
        await mainVm.FolderWatchVm.RunNowCommand.ExecuteAsync(null);
        now = now.AddSeconds(5);
        await mainVm.FolderWatchVm.RunNowCommand.ExecuteAsync(null);
        await mainVm.ReloadAllDataAsync();

        Assert.True(mainVm.FolderWatchVm.LastOk);
        Assert.Contains("tiktok_returns_fake.csv", mainVm.FolderWatchVm.LastNote);
        Assert.NotEmpty(mainVm.FolderWatchVm.Logs);
        Assert.Equal("Thành công", mainVm.FolderWatchVm.Logs[0].StatusText);
        Assert.Contains(mainVm.TikTokReturnVm.VisibleOrders, o => o.Source.Track == "TT-FAKE-TRK-001");
    }

    [Fact]
    public async Task RunExisting_RequiresConfirmation_AndImportsPreExistingCsv()
    {
        var watchDir = Path.Combine(_tempRoot, "watch-existing");
        Directory.CreateDirectory(watchDir);
        var dbPath = CreateInitializedTempDb();
        var statePath = Path.Combine(_tempRoot, "folder_watch.json");

        var csvPath = Path.Combine(watchDir, "pre_existing_tiktok.csv");
        WriteFakeTikTokCsv(csvPath, "TT-PRE-001", "TT-PRE-TRK-001");
        File.SetLastWriteTime(csvPath, FixedNow.AddMinutes(-10));

        var confirm = new SpyConfirm(false);
        using var controller = new RealFolderWatchController(dbPath, statePath, () => FixedNow);
        controller.Configure(watchDir, enabled: true);
        using var vm = new FolderWatchViewModel(controller, confirmationService: confirm);

        await vm.RunExistingCommand.ExecuteAsync(null);
        Assert.Equal("Sẽ nạp cả các file TikTok đã có sẵn trước khi bật theo dõi", confirm.LastMessage);
        Assert.Empty(vm.Logs);

        confirm.NextResult = true;
        await vm.RunExistingCommand.ExecuteAsync(null);
        Assert.Single(vm.Logs);
        Assert.Equal("Thành công", vm.Logs[0].StatusText);
        Assert.Contains("pre_existing_tiktok.csv", vm.Logs[0].FileNamesText);
    }

    [Fact]
    public void CompletedEvent_FromBackgroundThread_MarshalsToUiThread_AndShowsToast()
    {
        WpfTestHost.Run(() =>
        {
            var fakeData = new FakeDataService(() => FixedNow);
            var fakeWatch = new FakeFolderWatchController();
            fakeWatch.Configure(@"C:\Temp\FakeWatch", enabled: true);

            var window = new MainWindow();
            var dispatcher = window.Dispatcher;
            Action<Action> dispatchToUi = a =>
            {
                if (dispatcher.CheckAccess()) a();
                else _ = dispatcher.BeginInvoke(a);
            };

            using var mainVm = new MainViewModel(
                fakeData,
                fakeData,
                fakeData,
                fakeData,
                new FakeLegacyImporter(() => FixedNow),
                isDemoMode: true,
                dispatchToUi: dispatchToUi,
                toastDuration: TimeSpan.Zero,
                folderWatchController: fakeWatch);

            window.DataContext = mainVm;
            window.Show();
            WpfTestHost.PumpDispatcher(window);

            RunOnBackgroundThread(() =>
            {
                var res = new FolderWatchRunResult
                {
                    Ran = true,
                    Ok = true,
                    Note = "14:30 23/11: Đã nạp 1 file TikTok (bg_tiktok.csv): thêm 3, cập nhật 1",
                    FilesImported = 1,
                    Import = new TikTokImportResult { Ok = true, Added = 3, Updated = 1 }
                };
                res.Names.Add("bg_tiktok.csv");
                fakeWatch.EmitCompleted(res);
            });

            WpfTestHost.PumpDispatcher(window);

            Assert.True(mainVm.IsToastVisible);
            Assert.Contains("Đã tự nạp 1 file TikTok: bg_tiktok.csv", mainVm.ToastMessage);
            Assert.Single(mainVm.FolderWatchVm.Logs);
            window.Close();
        });
    }

    [Fact]
    public void ErrorAndAmbiguousResults_ShowRedAndYellowLogs()
    {
        var fakeWatch = new FakeFolderWatchController();
        fakeWatch.Configure(@"C:\Temp\FakeWatch", enabled: true);
        using var vm = new FolderWatchViewModel(fakeWatch, clock: () => FixedNow);

        var errRes = new FolderWatchRunResult
        {
            Ran = true,
            Ok = false,
            Note = "14:30 23/11: Không tìm thấy thư mục: C:\\Temp\\Missing"
        };
        errRes.Failed.Add("bad_file.xls");
        fakeWatch.EmitCompleted(errRes);

        Assert.True(vm.IsStatusError);
        Assert.False(vm.LastOk);
        Assert.Single(vm.Logs);
        Assert.True(vm.Logs[0].IsError);

        var ambRes = new FolderWatchRunResult
        {
            Ran = true,
            Ok = true,
            Note = "14:31 23/11: Đã nạp 1 file TikTok (amb.csv): thêm 1, cập nhật 0, 2 dòng nhập nhằng cần kiểm tra",
            FilesImported = 1,
            Import = new TikTokImportResult
            {
                Ok = true,
                Added = 1,
                Updated = 0,
                Ambiguous =
                {
                    new AmbiguousParcel("TikTok", "ORD-1", "TRK-1", "RET-1", 2),
                    new AmbiguousParcel("TikTok", "ORD-2", "TRK-2", "RET-2", 2)
                }
            }
        };
        ambRes.Names.Add("amb.csv");
        fakeWatch.EmitCompleted(ambRes);

        Assert.Contains(vm.Logs, l => l.IsWarning && l.SummaryText.Contains("2 dòng khớp nhiều kiện nên chưa nạp, cần kiểm tra thủ công"));
    }

    [Fact]
    public void Options_SaveToConfig_AndPersistAcrossReload()
    {
        var dbPath = CreateInitializedTempDb();
        var statePath = Path.Combine(_tempRoot, "folder_watch_opts.json");

        using (var controller = new RealFolderWatchController(dbPath, statePath, () => FixedNow))
        using (var vm = new FolderWatchViewModel(controller))
        {
            vm.SelectedPriceMode = vm.PriceModeOptions.Single(o => o.Value == "sau_chiet_khau");
            vm.SelectedProductMode = vm.ProductModeOptions.Single(o => o.Value == "replace");
            vm.OnlyReturns = false;
            vm.SkipUnshippedCancel = true;
        }

        using var reloaded = new RealFolderWatchController(dbPath, statePath, () => FixedNow);
        Assert.Equal("sau_chiet_khau", reloaded.PriceMode);
        Assert.Equal("replace", reloaded.ProductMode);
        Assert.False(reloaded.OnlyReturns);
        Assert.True(reloaded.SkipUnshippedCancel);
    }

    [Fact]
    public void SidebarGroup_NapDonTuDong_CollapsesExpandsAndPersists()
    {
        var settingsPath = Path.Combine(_tempRoot, "ui-settings.json");
        var fakeData = new FakeDataService(() => FixedNow);

        using (var vm1 = new MainViewModel(
            fakeData,
            fakeData,
            fakeData,
            fakeData,
            new FakeLegacyImporter(() => FixedNow),
            isDemoMode: false,
            uiSettingsStore: new UiSettingsStore(settingsPath, isDemoMode: false),
            folderWatchController: new FakeFolderWatchController()))
        {
            vm1.NavigateByKey("home");
            var fwNav = vm1.NavigationItems.Single(n => n.Key == "folder_watch");
            Assert.Equal("NẠP ĐƠN TỰ ĐỘNG", fwNav.Section);
            Assert.True(fwNav.IsVisible);

            vm1.ToggleNavGroupCommand.Execute("NẠP ĐƠN TỰ ĐỘNG");
            Assert.True(vm1.IsGroupCollapsed("NẠP ĐƠN TỰ ĐỘNG"));
            Assert.False(fwNav.IsVisible);
        }

        using var vm2 = new MainViewModel(
            fakeData,
            fakeData,
            fakeData,
            fakeData,
            new FakeLegacyImporter(() => FixedNow),
            isDemoMode: false,
            uiSettingsStore: new UiSettingsStore(settingsPath, isDemoMode: false),
            folderWatchController: new FakeFolderWatchController());

        Assert.True(vm2.IsGroupCollapsed("NẠP ĐƠN TỰ ĐỘNG"));
        Assert.False(vm2.NavigationItems.Single(n => n.Key == "folder_watch").IsVisible);

        vm2.ToggleNavGroupCommand.Execute("NẠP ĐƠN TỰ ĐỘNG");
        Assert.False(vm2.IsGroupCollapsed("NẠP ĐƠN TỰ ĐỘNG"));
        Assert.True(vm2.NavigationItems.Single(n => n.Key == "folder_watch").IsVisible);
    }

    [Fact]
    public void DemoMode_UsesFakeController_DoesNotTouchRealLocalAppData()
    {
        var realStatePath = AppPaths.FolderWatchStatePath;
        var existedBefore = File.Exists(realStatePath);
        var beforeWriteTime = existedBefore ? File.GetLastWriteTimeUtc(realStatePath) : DateTime.MinValue;

        var fakeData = new FakeDataService(() => FixedNow);
        var fakeWatch = new FakeFolderWatchController();
        using var mainVm = new MainViewModel(
            fakeData,
            fakeData,
            fakeData,
            fakeData,
            new FakeLegacyImporter(() => FixedNow),
            isDemoMode: true,
            uiSettingsStore: new UiSettingsStore(null, isDemoMode: true),
            folderWatchController: fakeWatch);

        Assert.True(mainVm.FolderWatchVm.IsDemoMode);
        fakeWatch.Configure(@"C:\Temp\DemoOnly", enabled: true);
        fakeWatch.RunOnce(false);

        Assert.Equal(existedBefore, File.Exists(realStatePath));
        if (existedBefore)
        {
            Assert.Equal(beforeWriteTime, File.GetLastWriteTimeUtc(realStatePath));
        }
    }

    [Fact]
    public void RenderScreenshots_ForT014()
    {
        var outDir = Path.Combine(FindRepoRoot(), "tasks", "reports", "T014-screens");
        Directory.CreateDirectory(outDir);

        WpfTestHost.Run(() =>
        {
            var fakeData = new FakeDataService(() => FixedNow);
            var fakeWatch = new FakeFolderWatchController();
            using var mainVm = new MainViewModel(
                fakeData,
                fakeData,
                fakeData,
                fakeData,
                new FakeLegacyImporter(() => FixedNow),
                isDemoMode: true,
                uiSettingsStore: new UiSettingsStore(null, isDemoMode: true),
                toastDuration: TimeSpan.Zero,
                folderWatchController: fakeWatch);

            var window = new MainWindow
            {
                DataContext = mainVm,
                Width = 1360,
                Height = 820
            };

            try
            {
                mainVm.NavigateByKey("folder_watch");
                window.Show();
                WpfTestHost.PumpDispatcher(window);

                // 1. Màn hình trống chưa chọn thư mục + sidebar nhóm mới
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "01-folder-watch-empty.png"));

                // 2. Đã bật + lượt nạp thành công
                fakeWatch.Configure(@"C:\Users\Shop\Downloads\TikTok-Returns", enabled: true);
                var okRes = new FolderWatchRunResult
                {
                    Ran = true,
                    Ok = true,
                    Note = "14:30 23/11: Đã nạp 1 file TikTok (tiktok_hoan_2311.csv): thêm 5, cập nhật 2",
                    FilesImported = 1,
                    Import = new TikTokImportResult { Ok = true, Added = 5, Updated = 2 }
                };
                okRes.Names.Add("tiktok_hoan_2311.csv");
                fakeWatch.EmitCompleted(okRes);
                WpfTestHost.PumpDispatcher(window);
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "02-folder-watch-success.png"));

                // 3. Lượt lỗi (hiện đỏ)
                var errRes = new FolderWatchRunResult
                {
                    Ran = true,
                    Ok = false,
                    Note = "14:31 23/11: Không đọc được: don_cu.xls (File .xls cũ chưa hỗ trợ, hãy lưu lại thành .xlsx)"
                };
                errRes.Failed.Add("don_cu.xls");
                fakeWatch.EmitCompleted(errRes);
                WpfTestHost.PumpDispatcher(window);
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "03-folder-watch-error.png"));

                // 4. Cảnh báo nhập nhằng (dòng vàng)
                var ambRes = new FolderWatchRunResult
                {
                    Ran = true,
                    Ok = true,
                    Note = "14:32 23/11: Đã nạp 1 file TikTok (tiktok_amb.csv): thêm 1, cập nhật 0, 2 dòng nhập nhằng cần kiểm tra",
                    FilesImported = 1,
                    Import = new TikTokImportResult
                    {
                        Ok = true,
                        Added = 1,
                        Updated = 0,
                        Ambiguous =
                        {
                            new AmbiguousParcel("TikTok", "TT-AMB-1", "FWD-1", "RET-1", 2),
                            new AmbiguousParcel("TikTok", "TT-AMB-2", "FWD-2", "RET-2", 2)
                        }
                    }
                };
                ambRes.Names.Add("tiktok_amb.csv");
                fakeWatch.EmitCompleted(ambRes);
                WpfTestHost.PumpDispatcher(window);
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "04-folder-watch-ambiguous.png"));
            }
            finally
            {
                window.Close();
                WpfTestHost.PumpDispatcher();
            }
        });

        Assert.True(File.Exists(Path.Combine(outDir, "01-folder-watch-empty.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "02-folder-watch-success.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "03-folder-watch-error.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "04-folder-watch-ambiguous.png")));
    }

    private string CreateInitializedTempDb()
    {
        var dbPath = Path.Combine(_tempRoot, $"test-{Guid.NewGuid():N}.db");
        AppDatabase.Initialize(dbPath);
        return dbPath;
    }

    private static void WriteFakeTikTokCsv(string path, string orderId, string tracking)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Return Order ID,Order ID,Return Logistics Tracking ID,Tracking ID,Order Status,Return Reason,SKU Quantity of Return,Order Amount,Created Time");
        sb.AppendLine($"RET-{orderId},{orderId},{tracking},FWD-{tracking},Return/Refund,Khách đổi ý,1,150000,2026-11-23 10:00:00");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static void RunOnBackgroundThread(Action action)
    {
        Exception? bgEx = null;
        var bg = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { bgEx = ex; }
        });
        bg.IsBackground = true;
        bg.Start();
        bg.Join();
        if (bgEx is not null) throw new AggregateException(bgEx);
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
        throw new DirectoryNotFoundException("Không tìm thấy thư mục gốc repository.");
    }

    private sealed class StubFolderPicker : IFolderPickerService
    {
        private readonly string? _result;
        public StubFolderPicker(string? result) => _result = result;
        public string? PickFolder(string title, string? initialDirectory = null) => _result;
    }

    private sealed class FixedConfirm : IConfirmationService
    {
        private readonly bool _result;
        public FixedConfirm(bool result) => _result = result;
        public bool Confirm(string title, string message) => _result;
    }

    private sealed class SpyConfirm : IConfirmationService
    {
        public bool NextResult { get; set; }
        public string LastMessage { get; private set; } = string.Empty;
        public SpyConfirm(bool nextResult) => NextResult = nextResult;
        public bool Confirm(string title, string message)
        {
            LastMessage = message;
            return NextResult;
        }
    }
}
