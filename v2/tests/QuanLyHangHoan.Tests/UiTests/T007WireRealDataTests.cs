using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Tests.UiTests;

public sealed class T007WireRealDataTests : IDisposable
{
    private static readonly DateTime FixedNow = new(2026, 11, 23, 12, 0, 0);
    private readonly string _tempRoot;

    public T007WireRealDataTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "QuanLyHangHoan_T007_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* ignore cleanup lock in temp */ }
        }
    }

    private string CreateFakeLegacyFolder(bool includeDuplicate = false)
    {
        var folder = Path.Combine(_tempRoot, "legacy_src_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        var mainRows = new List<string>
        {
            """{"id":"m1","orderId":"SHP-RET-1","track":"SPX101","returnTrack":"SPX101","platform":"Shopee","orderType":"Trả hàng/Hoàn tiền","status":"Chưa nhận hàng","returnedAt":"23/11/2026 06:00","price":150000,"note":"Khách đổi ý","items":[{"product":"Áo Thun Demo","color":"Đen","size":"M","qty":1}]}""",
            """{"id":"m2","orderId":"SHP-FAIL-2","track":"SPX102","returnTrack":"SPX102","platform":"Shopee","orderType":"Giao không thành công","status":"Chưa nhận hàng","returnedAt":"22/11/2026 08:00","price":220000,"note":"Không nghe máy","items":[{"product":"Quần Jeans Demo","color":"Xanh","size":"30","qty":1}]}""",
            """{"id":"m3","orderId":"SHP-CAN-3","track":"SPX103","returnTrack":"SPX103","platform":"Shopee","orderType":"Đơn hủy","status":"Đã nhận hàng","recvDate":"23/11/2026 09:00","price":90000,"note":"Hủy đơn","items":[{"product":"Váy Demo","color":"Trắng","size":"S","qty":1}]}""",
            """{"id":"m4","orderId":"TT-LOST-4","track":"TTVN201","returnTrack":"TTVN201","platform":"TikTok Shop","orderType":"Thất lạc & Đền bù","status":"Chưa nhận hàng","returnedAt":"22/11/2026 10:00","price":310000,"note":"Thất lạc","items":[{"product":"Áo Khoác Demo","color":"Be","size":"L","qty":1}]}""",
            """{"id":"m5","orderId":"TT-RET-5","track":"TTVN202","returnTrack":"TTVN202","platform":"TikTok","orderType":"Trả hàng/Hoàn tiền","status":"Chưa nhận hàng","returnedAt":"23/11/2026 11:00","price":180000,"note":"Nhầm size","items":[{"product":"Sơ Mi Demo","color":"Xám","size":"XL","qty":2}]}"""
        };

        if (includeDuplicate)
        {
            mainRows.Add("""{"id":"m1_dup","orderId":"SHP-RET-1","track":"SPX101","returnTrack":"SPX101","platform":"Shopee","orderType":"Trả hàng/Hoàn tiền","status":"Chưa nhận hàng","returnedAt":"23/11/2026 06:00","price":150000,"note":"Bản trùng giữ riêng","items":[{"product":"Áo Thun Demo","color":"Đen","size":"M","qty":1}]}""");
        }

        File.WriteAllText(Path.Combine(folder, "du_lieu_hang_hoan.json"), "[" + string.Join(",", mainRows) + "]");

        var archivedJson = """
            [
              {"id":"a1","orderId":"ARC-1","track":"SPXARC1","returnTrack":"SPXARC1","platform":"Shopee","orderType":"Trả hàng/Hoàn tiền","status":"Đã nhận hàng","recvDate":"20/11/2026 10:00","archivedAt":"21/11/2026 11:00","price":250000,"note":"Đã lưu trữ","items":[{"product":"Áo Len Demo","color":"Nâu","size":"L","qty":1}]},
              {"id":"a2","orderId":"ARC-2","track":"TTARC2","returnTrack":"TTARC2","platform":"TikTok Shop","orderType":"Giao không thành công","status":"Đã nhận hàng","recvDate":"20/11/2026 14:00","archivedAt":"21/11/2026 15:00","price":195000,"note":"Lưu trữ TikTok","items":[{"product":"Quần Kaki Demo","color":"Đen","size":"31","qty":1}]}
            ]
            """;
        File.WriteAllText(Path.Combine(folder, "phu_shopee_archived_orders_v1.json"), archivedJson);

        var deletedJson = """
            [
              {"id":"d1","orderId":"DEL-1","track":"SPXDEL1","returnTrack":"SPXDEL1","platform":"Shopee","deletedFrom":"Kho lưu trữ","deletedAt":"22/11/2026 09:30","note":"Xóa test","items":[{"product":"Áo Phông Demo","color":"Trắng","size":"M","qty":1}]}
            ]
            """;
        File.WriteAllText(Path.Combine(folder, "phu_shopee_deleted_orders_v1.json"), deletedJson);

        return folder;
    }

    [Fact]
    public async Task SqliteOrderProvider_MissingDb_ReturnsEmptyWithoutErrorAndDoesNotCreateFile()
    {
        var dbPath = Path.Combine(_tempRoot, "missing", "data.db");
        var provider = new SqliteOrderProvider(dbPath, () => FixedNow);

        Assert.Empty(provider.GetReturnOrders());
        Assert.Empty(await provider.GetReturnOrdersAsync());
        Assert.Empty(provider.GetArchivedOrders());
        Assert.Empty(provider.GetDeletedOrders());
        Assert.Empty(provider.GetNewOrders());
        Assert.False(File.Exists(dbPath));
    }

    [Fact]
    public void SqliteOrderProvider_ReadsImportedTempDb_AndSplitsThreePlatformTablesCorrectly()
    {
        var legacyFolder = CreateFakeLegacyFolder();
        var dbPath = Path.Combine(_tempRoot, "imported.db");
        LegacyImporter.Import(legacyFolder, dbPath);

        var provider = new SqliteOrderProvider(dbPath, () => FixedNow);

        // Tham số count bị bỏ qua, trả hết đơn chính
        var active = provider.GetReturnOrders(count: 2);
        var archived = provider.GetArchivedOrders();
        var deleted = provider.GetDeletedOrders();

        Assert.Equal(5, active.Count);
        Assert.Equal(2, archived.Count);
        Assert.Single(deleted);
        Assert.Equal("DEL-1", deleted[0].OrderId);
        Assert.Equal("Kho lưu trữ", deleted[0].DeletedFrom);
        Assert.Equal("22/11/2026 09:30", deleted[0].DeletedAt);
        Assert.Equal("Xóa test", deleted[0].DeleteReason);

        // Kiểm tra nhãn sàn chuẩn hóa "TikTok" (không "TikTok Shop")
        Assert.DoesNotContain(active, o => o.Platform.Contains("TikTok Shop", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(archived, o => o.Platform.Contains("TikTok Shop", StringComparison.OrdinalIgnoreCase));

        // Kiểm tra tách 3 bảng sàn trên MainViewModel
        var importer = new SqliteLegacyImporter(Path.Combine(_tempRoot, "copies"), dbPath, () => FixedNow);
        var mainVm = new MainViewModel(provider, provider, provider, provider, importer, isDemoMode: false);

        Assert.False(mainVm.IsDemoMode);
        Assert.False(mainVm.HomeVm.IsEmpty);
        Assert.Equal(2, mainVm.ShopeeReturnVm.TotalCount);    // SHP-RET-1 + SHP-FAIL-2
        Assert.Equal(1, mainVm.ShopeeCancelledVm.TotalCount); // SHP-CAN-3
        Assert.Equal(2, mainVm.TikTokReturnVm.TotalCount);    // TT-LOST-4 + TT-RET-5
        Assert.Equal(active.Count, mainVm.ShopeeReturnVm.TotalCount + mainVm.ShopeeCancelledVm.TotalCount + mainVm.TikTokReturnVm.TotalCount);
        Assert.Equal(2, mainVm.ArchivedOrdersVm.TotalCount);
        Assert.Equal(1, mainVm.DeletedOrdersVm.TotalCount);
        Assert.Equal(0, mainVm.NewOrdersVm.TotalCount);
    }

    [Fact]
    public async Task SqliteLegacyImporter_DryRunDoesNotCreateDb_AndImportReloadsAllScreens()
    {
        var legacyFolder = CreateFakeLegacyFolder(includeDuplicate: true);
        var workRoot = Path.Combine(_tempRoot, "legacy-copies");
        var dbPath = Path.Combine(_tempRoot, "v2", "data.db");

        var provider = new SqliteOrderProvider(dbPath, () => FixedNow);
        var importer = new SqliteLegacyImporter(workRoot, dbPath, () => FixedNow);
        var mainVm = new MainViewModel(provider, provider, provider, provider, importer, isDemoMode: false);

        // Ban đầu DB chưa có -> Trang chủ rỗng
        Assert.True(mainVm.HomeVm.IsEmpty);
        Assert.Equal(0, mainVm.ShopeeReturnVm.TotalCount);
        Assert.Equal(0, mainVm.ArchivedOrdersVm.TotalCount);
        Assert.Equal(0, mainVm.DeletedOrdersVm.TotalCount);

        // Bước 1: Dry-run không tạo DB, báo đúng số liệu & nhóm trùng khóa
        mainVm.LegacyImportVm.SourceFolderPath = legacyFolder;
        await mainVm.LegacyImportVm.RunDryRunAsync();

        Assert.False(File.Exists(dbPath));
        Assert.True(mainVm.LegacyImportVm.HasCompletedDryRun);
        Assert.NotNull(mainVm.LegacyImportVm.DryRunReport);
        Assert.Equal(6, mainVm.LegacyImportVm.DryRunReport!.MainOrdersCount);
        Assert.Equal(2, mainVm.LegacyImportVm.DryRunReport.ArchivedOrdersCount);
        Assert.Equal(1, mainVm.LegacyImportVm.DryRunReport.DeletedOrdersCount);
        Assert.Equal(1, mainVm.LegacyImportVm.DryRunReport.DuplicateGroupsCount);
        Assert.Contains(mainVm.LegacyImportVm.DryRunReport.Warnings, w => w.Contains("Trùng khóa", StringComparison.OrdinalIgnoreCase));

        // Bước 2: Xác nhận & nhập thật -> tạo DB và tự động tải lại toàn bộ màn hình
        mainVm.LegacyImportVm.IsConfirmed = true;
        await mainVm.LegacyImportVm.ExecuteImportAsync();

        Assert.True(File.Exists(dbPath));
        Assert.NotNull(mainVm.LegacyImportVm.ImportResult);
        Assert.True(mainVm.LegacyImportVm.ImportResult!.Success);

        Assert.False(mainVm.HomeVm.IsEmpty);
        Assert.Equal(6, mainVm.HomeVm.TotalOrders);
        Assert.Equal(3, mainVm.ShopeeReturnVm.TotalCount);    // 2 + 1 bản trùng giữ riêng
        Assert.Equal(1, mainVm.ShopeeCancelledVm.TotalCount);
        Assert.Equal(2, mainVm.TikTokReturnVm.TotalCount);
        Assert.Equal(2, mainVm.ArchivedOrdersVm.TotalCount);
        Assert.Equal(1, mainVm.DeletedOrdersVm.TotalCount);
        Assert.Equal(8, mainVm.StatisticsVm.TotalOrders);     // 6 chính + 2 lưu trữ (include_archive)
    }

    [Fact]
    public void CaptureScreenshots_WhenEnvSet()
    {
        if (Environment.GetEnvironmentVariable("T007_CAPTURE_SCREENS") != "1")
        {
            return;
        }

        var outDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "tasks", "reports", "T007-screens"));
        Directory.CreateDirectory(outDir);

        var legacyFolder = CreateFakeLegacyFolder(includeDuplicate: true);
        var workRoot = Path.Combine(_tempRoot, "screen-copies");
        var dbPath = Path.Combine(_tempRoot, "screen-db", "data.db");

        WpfTestHost.Run(() =>
        {
            var provider = new SqliteOrderProvider(dbPath, () => FixedNow);
            var importer = new SqliteLegacyImporter(workRoot, dbPath, () => FixedNow);
            using var mainVm = new MainViewModel(provider, provider, provider, provider, importer, isDemoMode: false);

            var window = new MainWindow
            {
                DataContext = mainVm,
                WindowStyle = WindowStyle.None,
                ShowActivated = false,
                Left = -3000,
                Top = -3000,
                Width = 1360,
                Height = 820
            };
            window.Show();

            void SavePng(string fileName)
            {
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(150) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                timer.Start();
                Dispatcher.PushFrame(frame);

                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, fileName));
            }

            // 1. Trang chủ trống (chưa có DB)
            SavePng("01-trang-chu-trong.png");

            // 2. Nhập dữ liệu cũ hiện kết quả
            mainVm.NavigateByKey("legacy_import");
            mainVm.LegacyImportVm.SourceFolderPath = legacyFolder;
            WpfTestHost.RunTask(() => mainVm.LegacyImportVm.RunDryRunAsync());
            mainVm.LegacyImportVm.IsConfirmed = true;
            WpfTestHost.RunTask(() => mainVm.LegacyImportVm.ExecuteImportAsync());
            SavePng("02-nhap-du-lieu-cu-ket-qua.png");

            // 3. Trang chủ sau khi nhập dữ liệu giả
            mainVm.NavigateByKey("home");
            SavePng("03-trang-chu-sau-nhap.png");

            window.Close();
        });
        Assert.True(File.Exists(Path.Combine(outDir, "01-trang-chu-trong.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "02-nhap-du-lieu-cu-ket-qua.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "03-trang-chu-sau-nhap.png")));
    }
}
