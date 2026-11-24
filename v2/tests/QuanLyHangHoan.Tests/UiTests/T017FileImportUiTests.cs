using System.IO;
using System.Text;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Import;
using Xunit;

namespace QuanLyHangHoan.Tests.UiTests;

public sealed class T017FileImportUiTests : IDisposable
{
    private static readonly DateTime FixedNow = new(2026, 11, 23, 16, 0, 0);
    private readonly string _tempRoot;

    public T017FileImportUiTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "qlhh-t017-" + Guid.NewGuid().ToString("N"));
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
            // Bỏ qua khi dọn thư mục tạm
        }
    }

    [Fact]
    public void PickAndRemoveFiles_UpdatesSelectedFilesAndCanImport()
    {
        var f1 = Path.Combine(_tempRoot, "tiktok-1.csv");
        var f2 = Path.Combine(_tempRoot, "shopee-1.csv");
        File.WriteAllText(f1, "a,b\n1,2", Encoding.UTF8);
        File.WriteAllText(f2, "a,b\n3,4", Encoding.UTF8);

        var picker = new StubFilePicker([f1, f2, f1]); // có trùng f1
        var controller = new FakeFileImportController();
        var vm = new FileImportViewModel(controller, picker, new StubConfirmationService(true));

        Assert.False(vm.HasSelectedFiles);
        Assert.False(vm.CanImport);

        vm.BrowseFilesCommand.Execute(null);

        Assert.True(vm.HasSelectedFiles);
        Assert.True(vm.CanImport);
        Assert.Equal(2, vm.SelectedFiles.Count);
        Assert.Equal("tiktok-1.csv", vm.SelectedFiles[0].FileName);
        Assert.Equal("shopee-1.csv", vm.SelectedFiles[1].FileName);

        vm.RemoveFileCommand.Execute(vm.SelectedFiles[0]);
        Assert.Single(vm.SelectedFiles);
        Assert.Equal("shopee-1.csv", vm.SelectedFiles[0].FileName);

        vm.ClearFilesCommand.Execute(null);
        Assert.Empty(vm.SelectedFiles);
        Assert.False(vm.CanImport);
    }

    [Fact]
    public async Task Import_TikTokAndShopeeCsv_IntoTempDb_ReloadsScreensAndPreservesFiles()
    {
        var dbPath = CreateInitializedTempDb();
        var settingsPath = Path.Combine(_tempRoot, "settings.json");

        var tiktokCsvPath = Path.Combine(_tempRoot, "tiktok-returns.csv");
        var tiktokContent = BuildTikTokReturnCsv("TT-ORD-101", "TT-TRK-101");
        File.WriteAllText(tiktokCsvPath, tiktokContent, Encoding.UTF8);

        var shopeeCsvPath = Path.Combine(_tempRoot, "shopee-returns.csv");
        var shopeeContent = BuildShopeeReturnCsv("SP-ORD-202", "SP-TRK-202");
        File.WriteAllText(shopeeCsvPath, shopeeContent, Encoding.UTF8);

        var controller = new RealFileImportController(dbPath, () => FixedNow);
        var picker = new StubFilePicker([tiktokCsvPath, shopeeCsvPath]);
        var confirm = new StubConfirmationService(true);

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
            confirmationService: confirm,
            fileImportController: controller,
            importFilePickerService: picker);

        mainVm.FileImportVm.BrowseFilesCommand.Execute(null);
        Assert.Equal(2, mainVm.FileImportVm.SelectedFiles.Count);

        await mainVm.FileImportVm.ImportFilesCommand.ExecuteAsync(null);
        await Task.Delay(120);

        Assert.True(confirm.Called);
        Assert.Contains("2 file", confirm.LastMessage);
        Assert.False(mainVm.FileImportVm.HasError);
        Assert.True(mainVm.FileImportVm.HasSummary);
        Assert.Contains("thêm 2, cập nhật 0", mainVm.FileImportVm.SummaryText);
        Assert.Equal(2, mainVm.FileImportVm.ResultFiles.Count);
        Assert.Contains(mainVm.FileImportVm.ResultFiles, r => r.KindLabel == "TikTok / Salework" && !r.IsUnknown);
        Assert.Contains(mainVm.FileImportVm.ResultFiles, r => r.KindLabel == "Shopee" && !r.IsUnknown);

        // Các màn hình được tải lại từ DB tạm
        Assert.Single(mainVm.TikTokReturnVm.VisibleOrders);
        Assert.Equal("TT-TRK-101", mainVm.TikTokReturnVm.VisibleOrders[0].Source.Track);
        Assert.Single(mainVm.ShopeeReturnVm.VisibleOrders);
        Assert.Equal("SP-TRK-202", mainVm.ShopeeReturnVm.VisibleOrders[0].Source.Track);
        Assert.True(mainVm.IsToastVisible);
        Assert.Contains("thêm 2, cập nhật 0", mainVm.ToastMessage);

        // File gốc không bị xóa hay sửa
        Assert.True(File.Exists(tiktokCsvPath));
        Assert.True(File.Exists(shopeeCsvPath));
        Assert.Equal(tiktokContent, File.ReadAllText(tiktokCsvPath, Encoding.UTF8));
        Assert.Equal(shopeeContent, File.ReadAllText(shopeeCsvPath, Encoding.UTF8));
    }

    [Fact]
    public async Task Import_WithUnknownFile_MarksUnknownRed_AndStillImportsValidFile()
    {
        var dbPath = CreateInitializedTempDb();
        var tiktokCsvPath = Path.Combine(_tempRoot, "tiktok-ok.csv");
        File.WriteAllText(tiktokCsvPath, BuildTikTokReturnCsv("TT-ORD-303", "TT-TRK-303"), Encoding.UTF8);

        var unknownCsvPath = Path.Combine(_tempRoot, "la-khong-phai-don.csv");
        File.WriteAllText(unknownCsvPath, "CotA,CotB\n1,2\n", Encoding.UTF8);

        var controller = new RealFileImportController(dbPath, () => FixedNow);
        var vm = new FileImportViewModel(
            controller,
            new StubFilePicker([tiktokCsvPath, unknownCsvPath]),
            new StubConfirmationService(true));

        vm.BrowseFilesCommand.Execute(null);
        await vm.ImportFilesCommand.ExecuteAsync(null);

        Assert.False(vm.HasError);
        Assert.Contains("thêm 1, cập nhật 0", vm.SummaryText);
        Assert.Equal(2, vm.ResultFiles.Count);

        var validRow = Assert.Single(vm.ResultFiles, r => r.Name == "tiktok-ok.csv");
        Assert.False(validRow.IsUnknown);
        Assert.False(validRow.HasError);

        var unknownRow = Assert.Single(vm.ResultFiles, r => r.Name == "la-khong-phai-don.csv");
        Assert.True(unknownRow.IsUnknown);
        Assert.Equal("Không nhận ra", unknownRow.KindLabel);
        Assert.True(unknownRow.HasError);
    }

    [Fact]
    public async Task Import_WhenConfirmationCancelled_DoesNotImport()
    {
        var called = false;
        var controller = new FakeFileImportController((_, _, _) =>
        {
            called = true;
            return new FileImportResult { Ok = true };
        });

        var vm = new FileImportViewModel(
            controller,
            new StubFilePicker([@"C:\fake\tiktok.csv"]),
            new StubConfirmationService(false));

        vm.BrowseFilesCommand.Execute(null);
        await vm.ImportFilesCommand.ExecuteAsync(null);

        Assert.False(called);
        Assert.False(vm.HasResults);
    }

    [Fact]
    public async Task Import_PassesSelectedOptionsToController()
    {
        var controller = new FakeFileImportController();
        var vm = new FileImportViewModel(
            controller,
            new StubFilePicker([@"C:\fake\tiktok.xlsx"]),
            new StubConfirmationService(true));

        vm.SelectedProductMode = vm.ProductModeOptions.First(o => o.Mode == ImportProductMode.Replace);
        vm.SelectedPriceMode = vm.PriceModeOptions.First(o => o.Value == "sau_chiet_khau");
        vm.OnlyReturns = false;
        vm.SkipUnshippedCancel = true;

        vm.BrowseFilesCommand.Execute(null);
        await vm.ImportFilesCommand.ExecuteAsync(null);

        Assert.Equal(ImportProductMode.Replace, controller.LastMode);
        Assert.NotNull(controller.LastOptions);
        Assert.Equal("sau_chiet_khau", controller.LastOptions!.PriceMode);
        Assert.False(controller.LastOptions.OnlyReturns);
        Assert.True(controller.LastOptions.SkipUnshippedCancel);
    }

    [Fact]
    public async Task Import_WhenResultNotOk_ShowsErrorWithoutBlockingDialog()
    {
        var controller = new FakeFileImportController((_, _, _) => new FileImportResult
        {
            Ok = false,
            Message = "Lỗi ghi cơ sở dữ liệu."
        });

        var vm = new FileImportViewModel(
            controller,
            new StubFilePicker([@"C:\fake\tiktok.csv"]),
            new StubConfirmationService(true));

        vm.BrowseFilesCommand.Execute(null);
        await vm.ImportFilesCommand.ExecuteAsync(null);

        Assert.True(vm.HasError);
        Assert.Equal("Lỗi ghi cơ sở dữ liệu.", vm.ErrorMessage);
        Assert.False(vm.HasSummary);
    }

    [Fact]
    public void Sidebar_HasFileImportFirstInAutoImportGroup()
    {
        var fake = new FakeDataService();
        using var mainVm = new MainViewModel(
            fake, fake, fake, fake,
            new FakeLegacyImporter(),
            isDemoMode: true,
            deferInitialLoad: true,
            dispatchToUi: a => a(),
            uiSettingsStore: new UiSettingsStore(null, isDemoMode: true));

        // Nạp đơn nay nằm trong Cài đặt: thanh bên không còn nhóm "NẠP ĐƠN TỰ ĐỘNG"
        Assert.DoesNotContain(mainVm.NavigationItems, i => i.Section == "NẠP ĐƠN TỰ ĐỘNG");
        var tabs = mainVm.SettingsVm.Tabs;
        Assert.Equal(new[] { "general", "file_import", "folder_watch", "google_sheet", "salework" }, tabs.Select(t => t.Key).ToArray());
        Assert.Contains("TikTok", tabs.Single(t => t.Key == "file_import").Title);   // Nạp file Excel chỉ dành cho TikTok
    }

    [Fact]
    public async Task DemoMode_DoesNotTouchDisk()
    {
        var fake = new FakeDataService();
        var controller = new FakeFileImportController();
        var nonExistent = new[]
        {
            @"Z:\khong-ton-tai\tiktok-demo.xlsx",
            @"Z:\khong-ton-tai\shopee-demo.csv"
        };

        using var mainVm = new MainViewModel(
            fake, fake, fake, fake,
            new FakeLegacyImporter(),
            isDemoMode: true,
            deferInitialLoad: true,
            dispatchToUi: a => a(),
            uiSettingsStore: new UiSettingsStore(null, isDemoMode: true),
            confirmationService: new StubConfirmationService(true),
            fileImportController: controller,
            importFilePickerService: new StubFilePicker(nonExistent));

        Assert.True(mainVm.FileImportVm.IsDemoMode);
        mainVm.FileImportVm.BrowseFilesCommand.Execute(null);
        Assert.Equal(2, mainVm.FileImportVm.SelectedFiles.Count);
        Assert.Equal("—", mainVm.FileImportVm.SelectedFiles[0].SizeText);

        await mainVm.FileImportVm.ImportFilesCommand.ExecuteAsync(null);

        Assert.False(mainVm.FileImportVm.HasError);
        Assert.Equal(2, mainVm.FileImportVm.ResultFiles.Count);
        Assert.Equal("TikTok / Salework", mainVm.FileImportVm.ResultFiles[0].KindLabel);
        Assert.Equal("Shopee", mainVm.FileImportVm.ResultFiles[1].KindLabel);
    }

    [Fact]
    public void RenderFileImportScreens_ForVisualVerification()
    {
        var outDir = Path.Combine(FindRepoRoot(), "tasks", "reports", "T017-screens");
        Directory.CreateDirectory(outDir);

        WpfTestHost.Run(() =>
        {
            var fake = new FakeDataService();
            FileImportResult nextResult = new() { Ok = true };
            var fakeController = new FakeFileImportController((_, _, _) => nextResult);
            var fakePicker = new StubFilePicker([@"C:\demo\tiktok-hoan-23-11.xlsx", @"C:\demo\shopee-tra-hang.csv"]);

            using var mainVm = new MainViewModel(
                fake, fake, fake, fake,
                new FakeLegacyImporter(),
                isDemoMode: true,
                deferInitialLoad: false,
                uiSettingsStore: new UiSettingsStore(null, isDemoMode: true),
                confirmationService: new StubConfirmationService(true),
                fileImportController: fakeController,
                importFilePickerService: fakePicker);

            var window = new MainWindow
            {
                DataContext = mainVm,
                Width = 1360,
                Height = 820
            };

            try
            {
                window.Show();
                mainVm.NavigateByKey("file_import");
                WpfTestHost.PumpDispatcher(window);

                // 1. Chưa chọn file
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "01-file-import-empty.png"));

                // 2. Đã chọn file
                mainVm.FileImportVm.BrowseFilesCommand.Execute(null);
                WpfTestHost.PumpDispatcher(window);
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "02-file-import-selected.png"));

                // 3. Kết quả thành công
                nextResult = new FileImportResult
                {
                    Ok = true,
                    TikTok = new TikTokImportResult { Ok = true, Added = 4, Updated = 2 }
                };
                nextResult.Files.Add(new FileImportItem("tiktok-hoan-23-11.xlsx", FileKind.TikTok, 12, null));
                nextResult.Files.Add(new FileImportItem("shopee-tra-hang.csv", FileKind.Shopee, 8, null));
                WpfTestHost.RunTask(() => mainVm.FileImportVm.ImportFilesCommand.ExecuteAsync(null));
                WpfTestHost.PumpDispatcher(window);
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "03-file-import-success.png"));

                // 4. Có file lạ + cảnh báo nhập nhằng
                mainVm.FileImportVm.AddFiles([@"C:\demo\bang-luong-thang-11.csv"]);
                nextResult = new FileImportResult
                {
                    Ok = true,
                    TikTok = new TikTokImportResult
                    {
                        Ok = true,
                        Added = 3,
                        Updated = 1,
                        Ambiguous =
                        {
                            new AmbiguousParcel("TikTok", "TT-AMB-01", "FWD-1", "RET-1", 2),
                            new AmbiguousParcel("Shopee", "SP-AMB-02", "FWD-2", "RET-2", 2)
                        }
                    }
                };
                nextResult.Files.Add(new FileImportItem("tiktok-hoan-23-11.xlsx", FileKind.TikTok, 12, null));
                nextResult.Files.Add(new FileImportItem("shopee-tra-hang.csv", FileKind.Shopee, 8, null));
                nextResult.Files.Add(new FileImportItem("bang-luong-thang-11.csv", FileKind.Unknown, 15, "Không phải file đơn hàng (không thấy cột mã đơn / mã vận đơn)."));
                WpfTestHost.RunTask(() => mainVm.FileImportVm.ImportFilesCommand.ExecuteAsync(null));
                WpfTestHost.PumpDispatcher(window);
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "04-file-import-unknown-ambiguous.png"));
            }
            finally
            {
                window.Close();
                WpfTestHost.PumpDispatcher();
            }
        });

        Assert.True(File.Exists(Path.Combine(outDir, "01-file-import-empty.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "02-file-import-selected.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "03-file-import-success.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "04-file-import-unknown-ambiguous.png")));
    }

    private string CreateInitializedTempDb()
    {
        var dbPath = Path.Combine(_tempRoot, $"test-{Guid.NewGuid():N}.db");
        AppDatabase.Initialize(dbPath);
        return dbPath;
    }

    private static string BuildTikTokReturnCsv(string orderId, string tracking)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Order ID,Return Order ID,Return Logistics Tracking ID,Order Status,Return Reason,SKU Category,Product Name,Variation,Quantity,Sku Quantity of Return,Order Amount,Created Time");
        sb.AppendLine($"{orderId},RET-{orderId},{tracking},Đã hoàn tiền,Khách đổi ý,Áo,Áo thun trắng,M,1,1,150000,23/11/2026 10:00");
        return sb.ToString();
    }

    private static string BuildShopeeReturnCsv(string orderId, string tracking)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Mã đơn hàng,Mã vận đơn,Loại,Trạng thái về,Sản phẩm,Số lượng,Hoàn tiền,Ngày yêu cầu");
        sb.AppendLine($"{orderId},{tracking},Trả hàng/Hoàn tiền,Đang trả hàng,Áo thun đen · L × 1,1,180000,23/11/2026 11:00");
        return sb.ToString();
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

    private sealed class StubFilePicker : IImportFilePickerService
    {
        private readonly IReadOnlyList<string> _files;
        public StubFilePicker(IReadOnlyList<string> files) => _files = files;
        public IReadOnlyList<string> PickFiles() => _files;
    }

    private sealed class StubConfirmationService : IConfirmationService
    {
        private readonly bool _result;
        public StubConfirmationService(bool result) => _result = result;
        public bool Called { get; private set; }
        public string LastMessage { get; private set; } = string.Empty;

        public bool Confirm(string title, string message)
        {
            Called = true;
            LastMessage = message;
            return _result;
        }
    }
}
