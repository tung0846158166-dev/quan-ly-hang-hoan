using System.IO;
using System.Text;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Import;
using QuanLyHangHoan.Data.Sheet;
using Xunit;

namespace QuanLyHangHoan.Tests.UiTests;

public sealed class T015GoogleSheetUiTests : IDisposable
{
    private static readonly DateTime FixedNow = new(2026, 11, 23, 15, 0, 0);
    private const string FakeAppsScriptUrl = "https://script.google.com/macros/s/FAKE/exec";
    private readonly string _tempRoot;

    public T015GoogleSheetUiTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "qlhh-t015-" + Guid.NewGuid().ToString("N"));
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
    public void SaveUrl_ValidAndInvalid_ProtectsSecretAndHidesSavedUrl()
    {
        var dbPath = CreateInitializedTempDb();
        var statePath = Path.Combine(_tempRoot, "sheet_sync.json");
        var protector = new FakeSecretProtector();

        using var controller = new RealSheetSyncController(dbPath, statePath, protector, () => FixedNow, (_, _) => Task.FromResult(""));
        using var vm = new GoogleSheetViewModel(controller, new StubClipboardService());

        Assert.False(vm.HasUrl);
        Assert.True(vm.ShowUrlEditor);
        Assert.False(vm.ShowSavedUrlState);

        // 1. http:// bị từ chối, báo đỏ, không lưu
        vm.UrlInput = "http://script.google.com/macros/s/FAKE/exec";
        vm.SaveUrlCommand.Execute(null);
        Assert.False(vm.HasUrl);
        Assert.True(vm.HasUrlError);
        Assert.Contains("https", vm.UrlError);
        Assert.False(File.Exists(statePath));

        // 2. Host lạ bị từ chối, báo đỏ, không lưu
        vm.UrlInput = "https://evil.example.com/exec";
        vm.SaveUrlCommand.Execute(null);
        Assert.False(vm.HasUrl);
        Assert.True(vm.HasUrlError);
        Assert.Contains("Google", vm.UrlError);

        // 3. Địa chỉ Google hợp lệ được lưu mã hóa và không hiện lại trên màn hình
        vm.UrlInput = FakeAppsScriptUrl;
        vm.SaveUrlCommand.Execute(null);

        Assert.True(vm.HasUrl);
        Assert.False(vm.HasUrlError);
        Assert.Equal(string.Empty, vm.UrlInput);
        Assert.True(vm.ShowSavedUrlState);
        Assert.False(vm.ShowUrlEditor);
        Assert.True(vm.HasToken);
        Assert.DoesNotContain(FakeAppsScriptUrl, vm.Token);

        var rawState = File.ReadAllText(statePath);
        Assert.DoesNotContain(FakeAppsScriptUrl, rawState);
        Assert.DoesNotContain(vm.Token, rawState);
    }

    [Fact]
    public void ConfigOptions_SaveAndReloadCorrectly()
    {
        var dbPath = CreateInitializedTempDb();
        var statePath = Path.Combine(_tempRoot, "sheet_sync_cfg.json");
        var protector = new FakeSecretProtector();

        using (var controller = new RealSheetSyncController(dbPath, statePath, protector, () => FixedNow, (_, _) => Task.FromResult("")))
        using (var vm = new GoogleSheetViewModel(controller))
        {
            vm.UrlInput = FakeAppsScriptUrl;
            vm.SaveUrlCommand.Execute(null);

            vm.IsEnabled = true;
            vm.Tabs = "Hàng hoàn; Trả hàng; Huỷ";
            vm.MaxDays = "30";
            vm.SkipTypes = "";
            vm.OnlyReturns = false;
        }

        using var reloaded = new RealSheetSyncController(dbPath, statePath, protector, () => FixedNow, (_, _) => Task.FromResult(""));
        Assert.True(reloaded.HasUrl);
        Assert.True(reloaded.Enabled);
        Assert.Equal("Hàng hoàn; Trả hàng; Huỷ", reloaded.Tabs);
        Assert.Equal("30", reloaded.MaxDays);
        Assert.Null(reloaded.SkipTypes);
        Assert.False(reloaded.OnlyReturns);
    }

    [Fact]
    public async Task RunNow_UsesFakeFetch_ImportsIntoTempDb_AndReloadsMainViewModel()
    {
        var dbPath = CreateInitializedTempDb();
        var statePath = Path.Combine(_tempRoot, "sheet_sync_run.json");
        var settingsPath = Path.Combine(_tempRoot, "settings.json");
        var protector = new FakeSecretProtector();

        var csv = BuildFakeShopeeSheetCsv("SP-SHEET-ORD-001", "SP-SHEET-TRK-001");
        using var controller = new RealSheetSyncController(
            dbPath,
            statePath,
            protector,
            () => FixedNow,
            (requestedUrl, _) =>
            {
                Assert.Contains("token=", requestedUrl);
                return Task.FromResult(csv);
            });

        Assert.True(controller.SetUrl(FakeAppsScriptUrl, out _));

        var provider = new SqliteOrderProvider(dbPath, () => FixedNow);
        var importer = new SqliteLegacyImporter(Path.Combine(_tempRoot, "copies"), dbPath, () => FixedNow);
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
            sheetSyncController: controller,
            toastDuration: TimeSpan.Zero);

        await mainVm.GoogleSheetVm.RunNowCommand.ExecuteAsync(null);
        await mainVm.ReloadAllDataAsync();

        Assert.True(mainVm.GoogleSheetVm.LastOk);
        Assert.NotEmpty(mainVm.GoogleSheetVm.Logs);
        Assert.Equal("Thành công", mainVm.GoogleSheetVm.Logs[0].StatusText);
        Assert.Contains(mainVm.ShopeeReturnVm.VisibleOrders, o => o.Source.Track == "SP-SHEET-TRK-001");
        Assert.DoesNotContain(FakeAppsScriptUrl, mainVm.GoogleSheetVm.LastNote);
        Assert.DoesNotContain(FakeAppsScriptUrl, mainVm.GoogleSheetVm.Logs[0].SummaryText);
    }

    [Fact]
    public void CompletedEvent_FromBackgroundThread_MarshalsToUiThread_AndShowsToast()
    {
        WpfTestHost.Run(() =>
        {
            var fakeData = new FakeDataService(() => FixedNow);
            var fakeSheet = new FakeSheetSyncController(clock: () => FixedNow);
            Assert.True(fakeSheet.SetUrl(FakeAppsScriptUrl, out _));

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
                sheetSyncController: fakeSheet);

            window.DataContext = mainVm;
            window.Show();
            WpfTestHost.PumpDispatcher(window);

            RunOnBackgroundThread(() =>
            {
                var res = new SheetRunResult
                {
                    Ran = true,
                    Ok = true,
                    Note = "10 đơn ghép từ 3 tab. Thêm 4, cập nhật 2",
                    Import = new TikTokImportResult { Ok = true, Added = 4, Updated = 2 }
                };
                fakeSheet.EmitCompleted(res);
            });

            WpfTestHost.PumpDispatcher(window);

            Assert.True(mainVm.IsToastVisible);
            Assert.Contains("Đã cập nhật từ Google Sheet: thêm 4, cập nhật 2", mainVm.ToastMessage);
            Assert.Single(mainVm.GoogleSheetVm.Logs);
            window.Close();
        });
    }

    [Fact]
    public void ErrorAndAmbiguousResults_ShowRedAndYellow_WithoutBlockingToast()
    {
        var fakeData = new FakeDataService(() => FixedNow);
        var fakeSheet = new FakeSheetSyncController(clock: () => FixedNow);
        Assert.True(fakeSheet.SetUrl(FakeAppsScriptUrl, out _));

        using var mainVm = new MainViewModel(
            fakeData,
            fakeData,
            fakeData,
            fakeData,
            new FakeLegacyImporter(() => FixedNow),
            isDemoMode: true,
            dispatchToUi: a => a(),
            toastDuration: TimeSpan.Zero,
            sheetSyncController: fakeSheet);

        var errRes = new SheetRunResult
        {
            Ran = true,
            Ok = false,
            Note = "Google trả về trang web thay vì dữ liệu — kiểm tra lại bước \"Triển khai\"."
        };
        fakeSheet.EmitCompleted(errRes);

        Assert.True(mainVm.GoogleSheetVm.IsStatusError);
        Assert.False(mainVm.GoogleSheetVm.LastOk);
        Assert.False(mainVm.IsToastVisible);
        Assert.Single(mainVm.GoogleSheetVm.Logs);
        Assert.True(mainVm.GoogleSheetVm.Logs[0].IsError);

        var ambRes = new SheetRunResult
        {
            Ran = true,
            Ok = true,
            Note = "5 đơn ghép từ 3 tab. Thêm 1, cập nhật 0, 2 dòng nhập nhằng chưa nạp",
            Import = new TikTokImportResult
            {
                Ok = true,
                Added = 1,
                Updated = 0,
                Ambiguous =
                {
                    new AmbiguousParcel("Shopee", "SP-AMB-1", "FWD-1", "RET-1", 2),
                    new AmbiguousParcel("Shopee", "SP-AMB-2", "FWD-2", "RET-2", 2)
                }
            }
        };
        fakeSheet.EmitCompleted(ambRes);

        Assert.Contains(mainVm.GoogleSheetVm.Logs, l => l.IsWarning && l.SummaryText.Contains("2 dòng khớp nhiều kiện nên chưa nạp, cần kiểm tra thủ công"));
    }

    [Fact]
    public void Sidebar_ContainsGoogleSheetUnderAutoImport_AndNavigates()
    {
        var fakeData = new FakeDataService(() => FixedNow);
        var fakeSheet = new FakeSheetSyncController(clock: () => FixedNow);
        using var mainVm = new MainViewModel(
            fakeData,
            fakeData,
            fakeData,
            fakeData,
            new FakeLegacyImporter(() => FixedNow),
            isDemoMode: true,
            sheetSyncController: fakeSheet);

        var autoItems = mainVm.NavigationItems.Where(i => i.Section == "NẠP ĐƠN TỰ ĐỘNG").ToList();
        Assert.Equal(4, autoItems.Count);
        Assert.Equal("file_import", autoItems[0].Key);
        Assert.Equal("folder_watch", autoItems[1].Key);
        Assert.Equal("google_sheet", autoItems[2].Key);
        Assert.Equal("Google Sheet", autoItems[2].Title);
        Assert.Equal("salework", autoItems[3].Key);

        mainVm.NavigateByKey("google_sheet");
        Assert.IsType<GoogleSheetViewModel>(mainVm.CurrentPage);
    }

    [Fact]
    public async Task DemoMode_FakeController_DoesNotTouchNetworkOrDisk()
    {
        var fakeSheet = new FakeSheetSyncController(clock: () => FixedNow);
        Assert.True(fakeSheet.IsDemoMode);
        Assert.True(fakeSheet.SetUrl(FakeAppsScriptUrl, out _));
        fakeSheet.Start();

        var res = await fakeSheet.RunOnceAsync(manual: true);
        Assert.True(res.Ran);
        Assert.True(res.Ok);
        Assert.NotNull(res.Import);
    }

    [Fact]
    public void CaptureScreenshots_ForT015Review()
    {
        var repoRoot = FindRepoRoot();
        var outDir = Path.Combine(repoRoot, "tasks", "reports", "T015-screens");
        Directory.CreateDirectory(outDir);

        WpfTestHost.Run(() =>
        {
            var fakeData = new FakeDataService(() => FixedNow);
            var fakeSheet = new FakeSheetSyncController(clock: () => FixedNow);
            using var mainVm = new MainViewModel(
                fakeData,
                fakeData,
                fakeData,
                fakeData,
                new FakeLegacyImporter(() => FixedNow),
                isDemoMode: true,
                dispatchToUi: a => a(),
                toastDuration: TimeSpan.Zero,
                sheetSyncController: fakeSheet);

            mainVm.NavigateByKey("google_sheet");
            var window = new MainWindow
            {
                DataContext = mainVm,
                Width = 1360,
                Height = 820
            };
            window.Show();
            WpfTestHost.PumpDispatcher(window);

            try
            {
                // 1. Chưa có địa chỉ
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "01-google-sheet-empty.png"));

                // 2. Đã lưu địa chỉ giả + thành công
                Assert.True(fakeSheet.SetUrl(FakeAppsScriptUrl, out _));
                fakeSheet.UpdateConfig(true, "Hàng hoàn; Trả hàng; Huỷ", "30", null, true);
                fakeSheet.EmitCompleted(new SheetRunResult
                {
                    Ran = true,
                    Ok = true,
                    Note = "18 đơn ghép từ 3 tab (12 đơn mới/đổi được nạp, 6 đơn không đổi). Thêm 5, cập nhật 7",
                    Import = new TikTokImportResult { Ok = true, Added = 5, Updated = 7 }
                });
                WpfTestHost.PumpDispatcher(window);
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "02-google-sheet-success.png"));

                // 3. Lỗi (hiện đỏ)
                fakeSheet.EmitCompleted(new SheetRunResult
                {
                    Ran = true,
                    Ok = false,
                    Note = "Google trả về trang web thay vì dữ liệu — kiểm tra lại bước \"Triển khai\" (Ai có quyền truy cập: Bất kỳ ai)."
                });
                WpfTestHost.PumpDispatcher(window);
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "03-google-sheet-error.png"));

                // 4. Cảnh báo nhập nhằng (dòng vàng)
                fakeSheet.EmitCompleted(new SheetRunResult
                {
                    Ran = true,
                    Ok = true,
                    Note = "9 đơn ghép từ 3 tab. Thêm 3, cập nhật 1, 2 dòng nhập nhằng chưa nạp",
                    Import = new TikTokImportResult
                    {
                        Ok = true,
                        Added = 3,
                        Updated = 1,
                        Ambiguous =
                        {
                            new AmbiguousParcel("Shopee", "SP-AMB-01", "FWD-1", "RET-1", 2),
                            new AmbiguousParcel("Shopee", "SP-AMB-02", "FWD-2", "RET-2", 2)
                        }
                    }
                });
                WpfTestHost.PumpDispatcher(window);
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "04-google-sheet-ambiguous.png"));
            }
            finally
            {
                window.Close();
                WpfTestHost.PumpDispatcher();
            }
        });

        Assert.True(File.Exists(Path.Combine(outDir, "01-google-sheet-empty.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "02-google-sheet-success.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "03-google-sheet-error.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "04-google-sheet-ambiguous.png")));
    }

    private string CreateInitializedTempDb()
    {
        var dbPath = Path.Combine(_tempRoot, $"test-{Guid.NewGuid():N}.db");
        AppDatabase.Initialize(dbPath);
        return dbPath;
    }

    private static string BuildFakeShopeeSheetCsv(string orderId, string tracking)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Mã đơn hàng,Mã vận đơn,Loại,Trạng thái về,Sản phẩm,Số lượng,Hoàn tiền,Ngày yêu cầu");
        sb.AppendLine($"{orderId},{tracking},Trả hàng/Hoàn tiền,Đang trả hàng,Áo thun trắng · M × 1,1,150000,23/11/2026 10:00");
        return sb.ToString();
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

    private sealed class FakeSecretProtector : ISecretProtector
    {
        private const string Prefix = "ENC:";
        public string Protect(string plain) => Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));

        public string? Unprotect(string protectedText)
        {
            if (!protectedText.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            return Encoding.UTF8.GetString(Convert.FromBase64String(protectedText[Prefix.Length..]));
        }
    }

    private sealed class StubClipboardService : IClipboardService
    {
        public string? LastText { get; private set; }
        public void SetText(string text) => LastText = text;
    }
}
