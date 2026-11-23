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
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Tests.UiTests;

public sealed class T009ScanScreenTests : IDisposable
{
    private static readonly DateTime FixedNow = new(2026, 11, 23, 14, 30, 0);
    private readonly string _tempRoot;

    public T009ScanScreenTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "QuanLyHangHoan_T009_" + Guid.NewGuid().ToString("N"));
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

    private string CreateInitializedDbWithFakeOrders()
    {
        var dbPath = Path.Combine(_tempRoot, "scan_" + Guid.NewGuid().ToString("N")[..8] + ".db");
        AppDatabase.Initialize(dbPath);

        var legacyDir = Path.Combine(_tempRoot, "legacy_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(legacyDir);

        var rows = new List<string>
        {
            // Kiện A và Kiện B cùng mã đơn SHP-MULTI-1 (quy tắc 1 & 2: nhận A KHÔNG nhận B)
            """{"id":"pA","orderId":"SHP-MULTI-1","track":"SPX-KIEN-A","returnTrack":"SPX-KIEN-A","platform":"Shopee","orderType":"Trả hàng/Hoàn tiền","status":"Chưa trả","returnedAt":"23/11/2026 08:00","price":150000,"items":[{"product":"Áo Thun Demo","color":"Trắng","size":"M","qty":1}]}""",
            """{"id":"pB","orderId":"SHP-MULTI-1","track":"SPX-KIEN-B","returnTrack":"SPX-KIEN-B","platform":"Shopee","orderType":"Trả hàng/Hoàn tiền","status":"Chưa trả","returnedAt":"23/11/2026 08:00","price":120000,"items":[{"product":"Quần Short Demo","color":"Đen","size":"L","qty":1}]}""",
            // Kiện đã nhận trước đó
            """{"id":"pDone","orderId":"TT-DONE-2","track":"TT-ALREADY-1","returnTrack":"TT-ALREADY-1","platform":"TikTok","orderType":"Giao không thành công","status":"Hàng đã trả","recvDate":"23/11/2026 09:15:30","price":200000,"items":[{"product":"Váy Hoa Demo","color":"Hồng","size":"S","qty":1}]}""",
            // Hai kiện dùng chung mã giao đi -> Ambiguous
            """{"id":"pAmb1","orderId":"SHP-AMB-1","track":"SPX-AMB-1","returnTrack":"SPX-AMB-1","forwardTrack":"FWD-CHUNG-99","platform":"Shopee","orderType":"Trả hàng/Hoàn tiền","status":"Chưa trả","returnedAt":"23/11/2026 07:00","price":90000,"items":[{"product":"Mũ Demo 1","color":"Xanh","size":"Free","qty":1}]}""",
            """{"id":"pAmb2","orderId":"SHP-AMB-2","track":"SPX-AMB-2","returnTrack":"SPX-AMB-2","forwardTrack":"FWD-CHUNG-99","platform":"Shopee","orderType":"Trả hàng/Hoàn tiền","status":"Chưa trả","returnedAt":"23/11/2026 07:00","price":95000,"items":[{"product":"Mũ Demo 2","color":"Đỏ","size":"Free","qty":1}]}""",
            // Kiện trạng thái "Không cần nhận" -> NeedsManualCheck
            """{"id":"pMan","orderId":"SHP-MAN-3","track":"SPX-MANUAL-1","returnTrack":"SPX-MANUAL-1","platform":"Shopee","orderType":"Chỉ hoàn tiền","status":"Không cần nhận","returnedAt":"22/11/2026 10:00","price":50000,"items":[{"product":"Tất Demo","color":"Xám","size":"Free","qty":2}]}"""
        };

        File.WriteAllText(Path.Combine(legacyDir, "du_lieu_hang_hoan.json"), "[" + string.Join(",", rows) + "]");
        LegacyImporter.Import(legacyDir, dbPath);
        return dbPath;
    }

    [Fact]
    public async Task AllScanOutcomes_ShowCorrectCardColorsText_AndNeverShowSavedWhenAckFalse()
    {
        var dbPath = CreateInitializedDbWithFakeOrders();
        var scanService = new SqliteScanService(dbPath, () => FixedNow);
        var soundSpy = new SpySoundPlayer();
        var debounceTime = new DateTime(2026, 11, 23, 14, 0, 0, DateTimeKind.Utc);

        var vm = new ScanReceiveViewModel(scanService, soundSpy, debounceClock: () => debounceTime);

        // 1. Received -> xanh, "ĐÃ NHẬN", Ack=true, có "đã lưu", thông tin đơn đầy đủ
        vm.ScanInput = "SPX-KIEN-A";
        await vm.SubmitScanCommand.ExecuteAsync(null);

        Assert.True(vm.HasResult);
        Assert.Equal(ScanOutcome.Received, vm.ResultOutcome);
        Assert.True(vm.ResultAck);
        Assert.Equal("ĐÃ NHẬN", vm.ResultHeadline);
        Assert.Equal("#DCFCE7", vm.ResultBgHex);
        Assert.Contains("đã lưu", vm.ResultAckText, StringComparison.OrdinalIgnoreCase);
        Assert.True(vm.HasOrderDetails);
        Assert.Equal("SHP-MULTI-1", vm.ResultOrderId);
        Assert.Equal("Shopee", vm.ResultPlatform);
        Assert.Equal("Trả hàng/Hoàn tiền", vm.ResultOrderType);
        Assert.Contains("Áo Thun Demo", vm.ResultItemsText);
        Assert.Equal(1, soundSpy.SuccessCount);

        // Quy tắc 2: quét A KHÔNG làm B đổi
        var ordersAfterA = OrderRepository.LoadActive(dbPath);
        var parcelA = ordersAfterA.First(o => o.Track == "SPX-KIEN-A");
        var parcelB = ordersAfterA.First(o => o.Track == "SPX-KIEN-B");
        Assert.Equal(ReceiveStatus.Received, parcelA.Status);
        Assert.Equal(ReceiveStatus.NotReturned, parcelB.Status);

        // 2. AlreadyReceived -> vàng, "ĐÃ NHẬN TRƯỚC ĐÓ" kèm giờ nhận, Ack=false, KHÔNG "đã lưu"
        debounceTime = debounceTime.AddSeconds(2);
        vm.ScanInput = "TT-ALREADY-1";
        await vm.SubmitScanCommand.ExecuteAsync(null);

        Assert.Equal(ScanOutcome.AlreadyReceived, vm.ResultOutcome);
        Assert.False(vm.ResultAck);
        Assert.Equal("ĐÃ NHẬN TRƯỚC ĐÓ", vm.ResultHeadline);
        Assert.Equal("#FEF3C7", vm.ResultBgHex);
        Assert.Contains("23/11/2026 09:15:30", vm.ResultReason);
        Assert.DoesNotContain("đã lưu", vm.ResultAckText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("TT-DONE-2", vm.ResultOrderId);
        Assert.Equal("TikTok", vm.ResultPlatform);

        // 3. Ambiguous -> đỏ, "NHIỀU KIỆN KHỚP - KIỂM TRA THỦ CÔNG", Ack=false, không đổi đơn nào
        debounceTime = debounceTime.AddSeconds(2);
        vm.ScanInput = "FWD-CHUNG-99";
        await vm.SubmitScanCommand.ExecuteAsync(null);

        Assert.Equal(ScanOutcome.Ambiguous, vm.ResultOutcome);
        Assert.False(vm.ResultAck);
        Assert.Equal("NHIỀU KIỆN KHỚP - KIỂM TRA THỦ CÔNG", vm.ResultHeadline);
        Assert.Equal("#FEE2E2", vm.ResultBgHex);
        Assert.DoesNotContain("đã lưu", vm.ResultAckText, StringComparison.OrdinalIgnoreCase);

        var ordersAfterAmb = OrderRepository.LoadActive(dbPath);
        Assert.Equal(ReceiveStatus.NotReturned, ordersAfterAmb.First(o => o.Track == "SPX-AMB-1").Status);
        Assert.Equal(ReceiveStatus.NotReturned, ordersAfterAmb.First(o => o.Track == "SPX-AMB-2").Status);

        // 4. NotFound -> đỏ, "KHÔNG TÌM THẤY", Ack=false
        debounceTime = debounceTime.AddSeconds(2);
        vm.ScanInput = "MA-KHONG-TON-TAI";
        await vm.SubmitScanCommand.ExecuteAsync(null);

        Assert.Equal(ScanOutcome.NotFound, vm.ResultOutcome);
        Assert.False(vm.ResultAck);
        Assert.Equal("KHÔNG TÌM THẤY", vm.ResultHeadline);
        Assert.Equal("#FEE2E2", vm.ResultBgHex);
        Assert.DoesNotContain("đã lưu", vm.ResultAckText, StringComparison.OrdinalIgnoreCase);

        // 5. NeedsManualCheck -> cam, "CẦN KIỂM TRA" kèm lý do, Ack=false
        debounceTime = debounceTime.AddSeconds(2);
        vm.ScanInput = "SPX-MANUAL-1";
        await vm.SubmitScanCommand.ExecuteAsync(null);

        Assert.Equal(ScanOutcome.NeedsManualCheck, vm.ResultOutcome);
        Assert.False(vm.ResultAck);
        Assert.Equal("CẦN KIỂM TRA", vm.ResultHeadline);
        Assert.Equal("#FFEDD5", vm.ResultBgHex);
        Assert.Contains("Không cần nhận", vm.ResultReason);
        Assert.DoesNotContain("đã lưu", vm.ResultAckText, StringComparison.OrdinalIgnoreCase);

        // 6. Error (Service trả Error, Ack=false) -> cam, "CẦN KIỂM TRA", KHÔNG "đã lưu"
        var errorVm = new ScanReceiveViewModel(new ErrorScanService(), soundSpy);
        errorVm.ScanInput = "ANY-CODE";
        await errorVm.SubmitScanCommand.ExecuteAsync(null);

        Assert.Equal(ScanOutcome.Error, errorVm.ResultOutcome);
        Assert.False(errorVm.ResultAck);
        Assert.Equal("CẦN KIỂM TRA", errorVm.ResultHeadline);
        Assert.Equal("#FFEDD5", errorVm.ResultBgHex);
        Assert.DoesNotContain("đã lưu", errorVm.ResultAckText, StringComparison.OrdinalIgnoreCase);

        // Đếm đúng & thứ tự danh sách phiên mới nhất trên cùng
        Assert.Equal(1, vm.ReceivedCount);
        Assert.Equal(1, vm.AlreadyReceivedCount);
        Assert.Equal(3, vm.ErrorCount);
        Assert.Equal(5, vm.SessionHistory.Count);
        Assert.Equal("SPX-MANUAL-1", vm.SessionHistory[0].Code);
        Assert.Equal("SPX-KIEN-A", vm.SessionHistory[^1].Code);
    }

    [Fact]
    public async Task DuplicateScanUnderOneSecond_IsIgnoredOnce_AndAllowedAfterOneSecond()
    {
        var dbPath = CreateInitializedDbWithFakeOrders();
        var scanService = new SqliteScanService(dbPath, () => FixedNow);
        var debounceTime = new DateTime(2026, 11, 23, 14, 0, 0, DateTimeKind.Utc);
        var vm = new ScanReceiveViewModel(scanService, new NoOpScanSoundPlayer(), debounceClock: () => debounceTime);

        vm.ScanInput = "SPX-KIEN-A";
        await vm.SubmitScanCommand.ExecuteAsync(null);
        Assert.Single(vm.SessionHistory);
        Assert.Equal(1, vm.ReceivedCount);

        // Quét đúp cùng mã sau 300ms (< 1s) -> bỏ qua
        debounceTime = debounceTime.AddMilliseconds(300);
        vm.ScanInput = "spx-kien-a";
        await vm.SubmitScanCommand.ExecuteAsync(null);
        Assert.Single(vm.SessionHistory);
        Assert.Equal(0, vm.AlreadyReceivedCount);

        // Sau 1.1s (>= 1s) -> xử lý bình thường và báo AlreadyReceived
        debounceTime = debounceTime.AddMilliseconds(1100);
        vm.ScanInput = "SPX-KIEN-A";
        await vm.SubmitScanCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.SessionHistory.Count);
        Assert.Equal(1, vm.AlreadyReceivedCount);
        Assert.Equal(ScanOutcome.AlreadyReceived, vm.ResultOutcome);
    }

    [Fact]
    public async Task AttachedPhoto_IsSavedToPhotoRepository_OnlyForNextScan()
    {
        var dbPath = CreateInitializedDbWithFakeOrders();
        var scanService = new SqliteScanService(dbPath, () => FixedNow);
        var samplePng = CreateSamplePngBytes(Colors.MediumSeaGreen);
        var photoPath = Path.Combine(_tempRoot, "scan_photo.png");
        File.WriteAllBytes(photoPath, samplePng);

        var picker = new FixedImagePicker(photoPath);
        var debounceTime = new DateTime(2026, 11, 23, 14, 0, 0, DateTimeKind.Utc);
        var vm = new ScanReceiveViewModel(scanService, new NoOpScanSoundPlayer(), picker, () => debounceTime);

        vm.PickPhotoCommand.Execute(null);
        Assert.True(vm.HasPendingPhoto);
        Assert.Equal("scan_photo.png", vm.PendingPhotoName);

        vm.ScanInput = "SPX-KIEN-A";
        await vm.SubmitScanCommand.ExecuteAsync(null);

        // Ảnh đã được xóa khỏi hàng chờ sau lần quét kế tiếp
        Assert.False(vm.HasPendingPhoto);
        Assert.Equal(string.Empty, vm.PendingPhotoName);

        var parcelA = OrderRepository.LoadActive(dbPath).First(o => o.Track == "SPX-KIEN-A");
        Assert.False(string.IsNullOrWhiteSpace(parcelA.PhotoId));

        var savedPhoto = PhotoRepository.Get(dbPath, parcelA.PhotoId);
        Assert.NotNull(savedPhoto);
        Assert.Equal("image/png", savedPhoto.Mime);
        Assert.Equal(samplePng, savedPhoto.Bytes);
    }

    [Fact]
    public void StartupInitializer_RejectsNewerSchemaSafely_WithoutModifyingDb()
    {
        var dbPath = Path.Combine(_tempRoot, "newer_schema.db");
        AppDatabase.Initialize(dbPath);

        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version = 99;";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        string? capturedTitle = null;
        string? capturedMessage = null;
        var ok = AppStartupInitializer.TryInitializeDatabase(dbPath, (t, m) =>
        {
            capturedTitle = t;
            capturedMessage = m;
        });

        Assert.False(ok);
        Assert.NotNull(capturedTitle);
        Assert.Contains("99", capturedMessage);

        // Kiểm tra DB không bị hạ cấp hay sửa
        using (var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            Assert.Equal(99L, Convert.ToInt64(cmd.ExecuteScalar()));
        }
    }

    [Fact]
    public async Task MainViewModel_ReloadsAllScreensAndSidebarAfterReceivedScan_AndDemoModeDoesNotTouchDb()
    {
        var dbPath = CreateInitializedDbWithFakeOrders();
        var orderProvider = new SqliteOrderProvider(dbPath, () => FixedNow);
        var importer = new SqliteLegacyImporter(Path.Combine(_tempRoot, "copies"), dbPath, () => FixedNow);
        var scanService = new SqliteScanService(dbPath, () => FixedNow);

        using var mainVm = new MainViewModel(
            orderProvider,
            orderProvider,
            orderProvider,
            orderProvider,
            importer,
            isDemoMode: false,
            scanService: scanService,
            soundPlayer: new NoOpScanSoundPlayer(),
            deferInitialLoad: true);

        await mainVm.InitializeAsync(sweepInterval: TimeSpan.FromMinutes(10));
        mainVm.StopPeriodicSweep();

        var initialReceived = mainVm.SidebarReceivedCount;
        var initialToday = mainVm.HomeVm.OverallKpi.ReceivedTodayCount;
        mainVm.NavigateByKey("scan_receive");
        Assert.Same(mainVm.ScanReceiveVm, mainVm.CurrentPage);

        mainVm.ScanReceiveVm.ScanInput = "SPX-KIEN-A";
        await mainVm.ScanReceiveVm.SubmitScanCommand.ExecuteAsync(null);

        Assert.Equal(initialReceived + 1, mainVm.SidebarReceivedCount);
        Assert.Equal(initialToday + 1, mainVm.HomeVm.OverallKpi.ReceivedTodayCount);

        // Chế độ --demo: dùng FakeScanService, không ghi DB
        var fakeData = new FakeDataService();
        var fakeScan = new FakeScanService(fakeData.GetReturnOrders(40), fakeData.GetCurrentTime);
        using var demoVm = new MainViewModel(
            fakeData,
            fakeData,
            fakeData,
            fakeData,
            new FakeLegacyImporter(fakeData.GetCurrentTime),
            isDemoMode: true,
            scanService: fakeScan,
            soundPlayer: new NoOpScanSoundPlayer());

        Assert.True(demoVm.ScanReceiveVm.IsDemoMode);
        demoVm.ScanReceiveVm.ScanInput = "DEMO-NOT-FOUND";
        await demoVm.ScanReceiveVm.SubmitScanCommand.ExecuteAsync(null);
        Assert.Equal(ScanOutcome.NotFound, demoVm.ScanReceiveVm.ResultOutcome);
    }

    [Fact]
    public void CaptureScanScreenScreenshots()
    {
        var dbPath = CreateInitializedDbWithFakeOrders();
        var orderProvider = new SqliteOrderProvider(dbPath, () => FixedNow);
        var importer = new SqliteLegacyImporter(Path.Combine(_tempRoot, "copies"), dbPath, () => FixedNow);
        var scanService = new SqliteScanService(dbPath, () => FixedNow);

        var outDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "tasks", "reports", "T009-screens"));
        Directory.CreateDirectory(outDir);

        WpfTestHost.Run(() =>
        {
            using var mainVm = new MainViewModel(
                orderProvider,
                orderProvider,
                orderProvider,
                orderProvider,
                importer,
                isDemoMode: false,
                scanService: scanService,
                soundPlayer: new NoOpScanSoundPlayer(),
                deferInitialLoad: true);

            var window = new MainWindow { DataContext = mainVm, Width = 1360, Height = 820 };
            window.Show();
            WpfTestHost.RunTask(() => mainVm.InitializeAsync(sweepInterval: TimeSpan.FromMinutes(10)));
            mainVm.StopPeriodicSweep();

            mainVm.NavigateByKey("scan_receive");
            WpfTestHost.PumpDispatcher(window);

            // 1. Received
            mainVm.ScanReceiveVm.ScanInput = "SPX-KIEN-A";
            WpfTestHost.RunTask(() => mainVm.ScanReceiveVm.SubmitScanCommand.ExecuteAsync(null));
            WpfTestHost.PumpDispatcher(window);
            WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "01-scan-received.png"));

            // 2. AlreadyReceived
            mainVm.ScanReceiveVm.ScanInput = "TT-ALREADY-1";
            WpfTestHost.RunTask(() => mainVm.ScanReceiveVm.SubmitScanCommand.ExecuteAsync(null));
            WpfTestHost.PumpDispatcher(window);
            WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "02-scan-already-received.png"));

            // 3. Ambiguous
            mainVm.ScanReceiveVm.ScanInput = "FWD-CHUNG-99";
            WpfTestHost.RunTask(() => mainVm.ScanReceiveVm.SubmitScanCommand.ExecuteAsync(null));
            WpfTestHost.PumpDispatcher(window);
            WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "03-scan-ambiguous.png"));

            // 4. NotFound (+ danh sách phiên đầy đủ 4 lượt)
            mainVm.ScanReceiveVm.ScanInput = "MA-KHONG-TON-TAI";
            WpfTestHost.RunTask(() => mainVm.ScanReceiveVm.SubmitScanCommand.ExecuteAsync(null));
            WpfTestHost.PumpDispatcher(window);
            WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "04-scan-not-found.png"));

            window.Close();
        });
    }

    private static byte[] CreateSamplePngBytes(Color color, int width = 80, int height = 60)
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

    private sealed class SpySoundPlayer : IScanSoundPlayer
    {
        public int SuccessCount { get; private set; }
        public int ErrorCount { get; private set; }
        public void PlaySuccess() => SuccessCount++;
        public void PlayError() => ErrorCount++;
    }

    private sealed class FixedImagePicker(string path) : IImageFilePickerService
    {
        public string? PickImageFile(string title) => path;
    }

    private sealed class ErrorScanService : IScanService
    {
        public DateTime GetCurrentTime() => FixedNow;
        public Task<ScanResult> ReceiveAsync(string code, DateTime now, byte[]? photo = null, string photoMime = "image/jpeg", CancellationToken cancellationToken = default)
            => Task.FromResult(new ScanResult(ScanOutcome.Error, false, "Lỗi ghi SQLite mô phỏng (NO-ACK).", null));
    }
}
