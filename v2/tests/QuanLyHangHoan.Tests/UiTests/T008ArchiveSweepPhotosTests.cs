using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.App.Views;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Tests.UiTests;

public sealed class T008ArchiveSweepPhotosTests : IDisposable
{
    private static readonly DateTime FixedNow = new(2026, 11, 23, 12, 0, 0);
    private readonly string _tempRoot;

    public T008ArchiveSweepPhotosTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "QuanLyHangHoan_T008_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* ignore temp cleanup lock */ }
        }
    }

    private static byte[] CreateSamplePngBytes(Color color, int width = 120, int height = 80)
    {
        var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = color.A;
        }
        wb.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        wb.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(wb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private string CreateFakeLegacyFolderWithSweepAndPhotos()
    {
        var folder = Path.Combine(_tempRoot, "legacy_src_" + Guid.NewGuid().ToString("N")[..8]);
        var photosDir = Path.Combine(folder, "photos");
        Directory.CreateDirectory(photosDir);

        var filePng = CreateSamplePngBytes(Colors.OrangeRed, 160, 110);
        var fileBase64 = "data:image/png;base64," + Convert.ToBase64String(filePng);
        var inlinePng = CreateSamplePngBytes(Colors.DodgerBlue, 80, 80);
        var inlineBase64 = "data:image/png;base64," + Convert.ToBase64String(inlinePng);

        WriteLegacyPhotoJson(photosDir, "photo_ok", fileBase64);
        WriteLegacyPhotoJson(photosDir, "photo_diff", fileBase64);

        var mainRows = new List<string>
        {
            // Đủ điều kiện tự dời lưu trữ: Hàng đã trả, recvDate >= 24h (22/11 10:00 -> 23/11 12:00 = 26h)
            $$"""{"id":"sw1","orderId":"SHP-SWEEP-1","track":"SPXSW1","returnTrack":"SPXSW1","platform":"Shopee","orderType":"Trả hàng/Hoàn tiền","status":"Hàng đã trả","recvDate":"22/11/2026 10:00","price":180000,"photoId":"photo_ok","note":"Đã trả đủ 26h","items":[{"product":"Áo Khoác Demo","color":"Đen","size":"L","qty":1}]}""",
            // Chưa đủ 24h: Hàng đã trả, recvDate 23/11 06:00 (6h) -> giữ ở bảng chính, có ảnh khác inline
            $$"""{"id":"kp1","orderId":"SHP-KEEP-2","track":"SPXKP2","returnTrack":"SPXKP2","platform":"Shopee","orderType":"Trả hàng/Hoàn tiền","status":"Hàng đã trả","recvDate":"23/11/2026 06:00","price":210000,"photoId":"photo_diff","photo":"{{inlineBase64}}","note":"Mới 6h","items":[{"product":"Quần Jeans Demo","color":"Xanh","size":"30","qty":1}]}""",
            // Chưa nhận hàng, ảnh bị mất
            """{"id":"ms1","orderId":"SHP-MISS-3","track":"SPXMS3","returnTrack":"SPXMS3","platform":"Shopee","orderType":"Giao không thành công","status":"Chưa nhận hàng","returnedAt":"22/11/2026 09:00","price":140000,"photoId":"photo_missing","note":"Ảnh bị mất","items":[{"product":"Váy Demo","color":"Trắng","size":"S","qty":1}]}""",
            // TikTok không có ảnh
            """{"id":"tt1","orderId":"TT-NO-4","track":"TTVN401","returnTrack":"TTVN401","platform":"TikTok","orderType":"Trả hàng/Hoàn tiền","status":"Chưa nhận hàng","returnedAt":"23/11/2026 08:00","price":125000,"note":"Không ảnh","items":[{"product":"Sơ Mi Demo","color":"Be","size":"M","qty":1}]}"""
        };

        File.WriteAllText(Path.Combine(folder, "du_lieu_hang_hoan.json"), "[" + string.Join(",", mainRows) + "]");
        return folder;
    }

    [Fact]
    public async Task InitializeAsync_RunsArchiveSweepBeforeLoading_IsIdempotent_AndPopulatesMissingPhotos()
    {
        var legacyFolder = CreateFakeLegacyFolderWithSweepAndPhotos();
        var dbPath = Path.Combine(_tempRoot, "imported.db");
        LegacyImporter.Import(legacyFolder, dbPath);

        var orderProvider = new SqliteOrderProvider(dbPath, () => FixedNow);
        var importer = new SqliteLegacyImporter(Path.Combine(_tempRoot, "copies"), dbPath, () => FixedNow);
        var sweeper = new SqliteArchiveSweeper(dbPath);
        var photoProvider = new SqlitePhotoProvider(dbPath);

        using var mainVm = new MainViewModel(
            orderProvider,
            orderProvider,
            orderProvider,
            orderProvider,
            importer,
            isDemoMode: false,
            archiveSweeper: sweeper,
            photoProvider: photoProvider,
            deferInitialLoad: true);

        await mainVm.InitializeAsync(sweepInterval: TimeSpan.FromMinutes(10));
        mainVm.StopPeriodicSweep();

        // SHP-SWEEP-1 đã được dời sang Kho lưu trữ trước khi nạp lên các màn hình
        Assert.Equal(2, mainVm.ShopeeReturnVm.TotalCount); // SHP-KEEP-2 + SHP-MISS-3
        Assert.Equal(1, mainVm.TikTokReturnVm.TotalCount); // TT-NO-4
        Assert.Equal(1, mainVm.ArchivedOrdersVm.TotalCount); // SHP-SWEEP-1
        Assert.Equal(3, mainVm.HomeVm.TotalOrders);
        mainVm.NavigateByKey("stats"); // Thống kê chỉ được tính khi mở màn (hoãn để nạp lại dữ liệu nhanh)
        Assert.Equal(4, mainVm.StatisticsVm.TotalOrders); // 3 chính + 1 lưu trữ

        // Badge sidebar đồng bộ
        var archiveNav = mainVm.NavigationItems.First(n => n.Key == "archive");
        Assert.Equal("1", archiveNav.Badge);

        // Thống kê ảnh mất trên Trang chủ và Thống kê
        Assert.Equal(1, mainVm.HomeVm.MissingPhotoCount);
        Assert.True(mainVm.HomeVm.HasMissingPhotos);
        Assert.Equal("Ảnh mất: 1", mainVm.HomeVm.MissingPhotoText);
        Assert.Equal(1, mainVm.StatisticsVm.MissingPhotoCount);
        Assert.Equal("Ảnh mất: 1", mainVm.StatisticsVm.MissingPhotoText);

        // Chạy sweep lần 2 -> idempotent, không dời lặp
        var secondMoved = await mainVm.RunArchiveSweepAsync();
        Assert.Equal(0, secondMoved);
        Assert.Equal(1, mainVm.ArchivedOrdersVm.TotalCount);
    }

    [Fact]
    public async Task DemoMode_NeverCallsArchiveSweeper()
    {
        var fakeData = new FakeDataService();
        var spySweeper = new SpyArchiveSweeper();
        using var mainVm = new MainViewModel(
            fakeData,
            fakeData,
            fakeData,
            fakeData,
            new FakeLegacyImporter(fakeData.GetCurrentTime),
            isDemoMode: true,
            archiveSweeper: spySweeper);

        await mainVm.InitializeAsync();
        var moved = await mainVm.RunArchiveSweepAsync();

        Assert.Equal(0, moved);
        Assert.Equal(0, spySweeper.CallCount);
    }

    [Fact]
    public async Task SweeperFailure_RecordsErrorWithoutCrashing()
    {
        var fakeData = new FakeDataService();
        var failingSweeper = new FailingArchiveSweeper();
        using var mainVm = new MainViewModel(
            fakeData,
            fakeData,
            fakeData,
            fakeData,
            new FakeLegacyImporter(fakeData.GetCurrentTime),
            isDemoMode: false,
            archiveSweeper: failingSweeper,
            deferInitialLoad: true);

        var moved = await mainVm.RunArchiveSweepAsync();
        Assert.Equal(0, moved);
        Assert.Contains("DB locked", mainVm.LastSweepError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyImportSuccess_TriggersArchiveSweepAndReloadsScreens()
    {
        var legacyFolder = CreateFakeLegacyFolderWithSweepAndPhotos();
        var dbPath = Path.Combine(_tempRoot, "post_import.db");
        var orderProvider = new SqliteOrderProvider(dbPath, () => FixedNow);
        var importer = new SqliteLegacyImporter(Path.Combine(_tempRoot, "copies"), dbPath, () => FixedNow);
        var sweeper = new SqliteArchiveSweeper(dbPath);
        var photoProvider = new SqlitePhotoProvider(dbPath);

        using var mainVm = new MainViewModel(
            orderProvider,
            orderProvider,
            orderProvider,
            orderProvider,
            importer,
            isDemoMode: false,
            archiveSweeper: sweeper,
            photoProvider: photoProvider);

        mainVm.LegacyImportVm.SourceFolderPath = legacyFolder;
        await mainVm.LegacyImportVm.RunDryRunAsync();
        mainVm.LegacyImportVm.IsConfirmed = true;
        await mainVm.LegacyImportVm.ExecuteImportAsync();

        // Sau khi nhập xong, sweep tự chạy -> SHP-SWEEP-1 sang Kho lưu trữ
        Assert.Equal(2, mainVm.ShopeeReturnVm.TotalCount);
        Assert.Equal(1, mainVm.ArchivedOrdersVm.TotalCount);
        Assert.Equal(1, mainVm.HomeVm.MissingPhotoCount);
    }

    [Fact]
    public async Task SqlitePhotoProvider_And_PhotoViewerViewModel_HandlePresentMissingAndInlineDiffers()
    {
        var legacyFolder = CreateFakeLegacyFolderWithSweepAndPhotos();
        var dbPath = Path.Combine(_tempRoot, "photos.db");
        LegacyImporter.Import(legacyFolder, dbPath);

        var photoProvider = new SqlitePhotoProvider(dbPath);

        // 1. Ảnh có thật
        var okData = await photoProvider.GetPhotoAsync("photo_ok");
        Assert.NotNull(okData);
        Assert.True(okData!.Bytes.Length > 0);
        Assert.False(okData.InlineDiffers);

        // 2. Ảnh mất -> null
        var missingData = await photoProvider.GetPhotoAsync("photo_missing");
        Assert.Null(missingData);

        // 3. Ảnh nhúng khác tệp ảnh
        var diffData = await photoProvider.GetPhotoAsync("photo_diff");
        Assert.NotNull(diffData);
        Assert.True(diffData!.InlineDiffers);

        // 4. PhotoViewerViewModel xử lý cả 3 trường hợp
        var viewerVm = new PhotoViewerViewModel(photoProvider);
        await viewerVm.LoadAsync("photo_diff", "SHP-KEEP-2", "SPXKP2");
        Assert.False(viewerVm.IsMissing);
        Assert.True(viewerVm.HasImage);
        Assert.NotNull(viewerVm.ImageSource);
        Assert.True(viewerVm.ImageSource!.IsFrozen);
        Assert.True(viewerVm.ShowInlineDiffersNote);
        Assert.Equal("Ảnh nhúng khác tệp ảnh, đang hiện tệp ảnh", viewerVm.InlineDiffersNote);

        viewerVm.ZoomIn();
        Assert.Equal(1.25, viewerVm.ZoomLevel);
        viewerVm.ZoomOut();
        Assert.Equal(1.0, viewerVm.ZoomLevel);

        await viewerVm.LoadAsync("photo_missing", "SHP-MISS-3", "SPXMS3");
        Assert.True(viewerVm.IsMissing);
        Assert.False(viewerVm.HasImage);
        Assert.Equal("Ảnh bị mất khỏi dữ liệu cũ", viewerVm.MissingMessage);
    }

    [Fact]
    public void CaptureScreenshots_WhenEnvSet()
    {
        if (Environment.GetEnvironmentVariable("T008_CAPTURE_SCREENS") != "1")
        {
            return;
        }

        var outDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "tasks", "reports", "T008-screens"));
        Directory.CreateDirectory(outDir);

        var legacyFolder = CreateFakeLegacyFolderWithSweepAndPhotos();
        var dbPath = Path.Combine(_tempRoot, "screen.db");
        LegacyImporter.Import(legacyFolder, dbPath);

        WpfTestHost.Run(() =>
        {
            var orderProvider = new SqliteOrderProvider(dbPath, () => FixedNow);
            var importer = new SqliteLegacyImporter(Path.Combine(_tempRoot, "copies"), dbPath, () => FixedNow);
            var sweeper = new SqliteArchiveSweeper(dbPath);
            var photoProvider = new SqlitePhotoProvider(dbPath);

            using var mainVm = new MainViewModel(
                orderProvider,
                orderProvider,
                orderProvider,
                orderProvider,
                importer,
                isDemoMode: false,
                archiveSweeper: sweeper,
                photoProvider: photoProvider,
                deferInitialLoad: true);

            WpfTestHost.RunTask(() => mainVm.InitializeAsync());
            mainVm.StopPeriodicSweep();

            var window = new MainWindow { DataContext = mainVm };
            window.Show();
            WpfTestHost.PumpDispatcher(window);

            // 1. Trang chủ có dòng "Ảnh mất: 1"
            WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "04-trang-chu-anh-mat.png"));

            // 2. Bảng đơn Shopee có biểu tượng/nút xem ảnh (cuộn tới cột Ảnh để thấy rõ nút 📷 Xem)
            mainVm.NavigateByKey("shopee_return");
            WpfTestHost.PumpDispatcher(window);
            var dataGrid = FindVisualChild<System.Windows.Controls.DataGrid>(window);
            if (dataGrid is not null && dataGrid.Items.Count > 0)
            {
                var photoCol = dataGrid.Columns.FirstOrDefault(c => Equals(c.Header, "Ảnh"));
                if (photoCol is not null)
                {
                    dataGrid.ScrollIntoView(dataGrid.Items[0], photoCol);
                    WpfTestHost.PumpDispatcher(window);
                }
            }
            WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "01-bang-don-bieu-tuong-anh.png"));

            // 3. Khung xem ảnh (có ảnh + ghi chú InlineDiffers)
            var viewerVm = new PhotoViewerViewModel(photoProvider);
            WpfTestHost.RunTask(() => viewerVm.LoadAsync("photo_diff", "SHP-KEEP-2", "SPXKP2"));
            var photoWin = new PhotoViewerWindow { DataContext = viewerVm, Owner = window };
            photoWin.Show();
            WpfTestHost.PumpDispatcher(photoWin);
            WpfTestHost.SaveWindowPng(photoWin, Path.Combine(outDir, "02-xem-anh-don-hang.png"));
            photoWin.Close();

            // 4. Thông báo ảnh bị mất
            var missingVm = new PhotoViewerViewModel(photoProvider);
            WpfTestHost.RunTask(() => missingVm.LoadAsync("photo_missing", "SHP-MISS-3", "SPXMS3"));
            var missingWin = new PhotoViewerWindow { DataContext = missingVm, Owner = window };
            missingWin.Show();
            WpfTestHost.PumpDispatcher(missingWin);
            WpfTestHost.SaveWindowPng(missingWin, Path.Combine(outDir, "03-anh-bi-mat.png"));
            missingWin.Close();

            window.Close();
        });
    }

    private static void WriteLegacyPhotoJson(string photosDir, string photoId, string dataUri)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(photoId))).ToLowerInvariant();
        var json = System.Text.Json.JsonSerializer.Serialize(new { id = photoId, data = dataUri });
        File.WriteAllText(Path.Combine(photosDir, hash + ".json"), json);
    }

    private static void RunTaskOnDispatcher(Dispatcher dispatcher, Func<Task> action)
    {
        var frame = new DispatcherFrame();
        Exception? captured = null;
        _ = RunAsync();
        Dispatcher.PushFrame(frame);
        if (captured is not null) throw new AggregateException(captured);

        async Task RunAsync()
        {
            try { await action(); }
            catch (Exception ex) { captured = ex; }
            finally { _ = dispatcher.BeginInvoke(() => frame.Continue = false); }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var nested = FindVisualChild<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static void PumpDispatcher(Window window)
    {
        window.UpdateLayout();
        var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new DispatcherOperationCallback(_ =>
        {
            frame.Continue = false;
            return null;
        }), null);
        Dispatcher.PushFrame(frame);
    }

    private static void RenderToPng(Window window, string path)
    {
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    private sealed class SpyArchiveSweeper : IArchiveSweeper
    {
        public int CallCount { get; private set; }
        public Task<int> SweepAsync(DateTime now, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(0);
        }
    }

    private sealed class FailingArchiveSweeper : IArchiveSweeper
    {
        public Task<int> SweepAsync(DateTime now, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("DB locked");
    }
}
