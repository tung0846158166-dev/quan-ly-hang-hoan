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
using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.Tests.UiTests;

public sealed class T010SyncScreenTests : IDisposable
{
    private static readonly DateTime FixedNow = new(2026, 11, 23, 16, 45, 0);
    private readonly string _tempRoot;

    public T010SyncScreenTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "QuanLyHangHoan_T010_" + Guid.NewGuid().ToString("N"));
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
        var dbPath = Path.Combine(_tempRoot, "sync_" + Guid.NewGuid().ToString("N")[..8] + ".db");
        AppDatabase.Initialize(dbPath);

        var legacyDir = Path.Combine(_tempRoot, "legacy_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(legacyDir);

        var rows = new List<string>
        {
            """{"id":"s1","orderId":"SHP-SYNC-1","track":"SPX-SYNC-A","returnTrack":"SPX-SYNC-A","platform":"Shopee","orderType":"Trả hàng/Hoàn tiền","status":"Chưa trả","returnedAt":"23/11/2026 08:00","price":150000,"items":[{"product":"Áo Demo Sync","color":"Trắng","size":"M","qty":1}]}""",
            """{"id":"s2","orderId":"SHP-SYNC-2","track":"SPX-SYNC-B","returnTrack":"SPX-SYNC-B","platform":"Shopee","orderType":"Trả hàng/Hoàn tiền","status":"Chưa trả","returnedAt":"23/11/2026 08:00","price":120000,"items":[{"product":"Quần Demo Sync","color":"Đen","size":"L","qty":1}]}""",
            """{"id":"sDone","orderId":"TT-SYNC-DONE","track":"TT-SYNC-DONE-1","returnTrack":"TT-SYNC-DONE-1","platform":"TikTok","orderType":"Giao không thành công","status":"Hàng đã trả","recvDate":"23/11/2026 09:15:30","price":200000,"items":[{"product":"Váy Demo Sync","color":"Hồng","size":"S","qty":1}]}""",
            """{"id":"sAmb1","orderId":"SHP-AMB-1","track":"SPX-AMB-1","returnTrack":"SPX-AMB-1","forwardTrack":"FWD-SYNC-AMB","platform":"Shopee","orderType":"Trả hàng/Hoàn tiền","status":"Chưa trả","returnedAt":"23/11/2026 07:00","price":90000,"items":[{"product":"Mũ Demo 1","color":"Xanh","size":"Free","qty":1}]}""",
            """{"id":"sAmb2","orderId":"SHP-AMB-2","track":"SPX-AMB-2","returnTrack":"SPX-AMB-2","forwardTrack":"FWD-SYNC-AMB","platform":"Shopee","orderType":"Trả hàng/Hoàn tiền","status":"Chưa trả","returnedAt":"23/11/2026 07:00","price":95000,"items":[{"product":"Mũ Demo 2","color":"Đỏ","size":"Free","qty":1}]}"""
        };

        File.WriteAllText(Path.Combine(legacyDir, "du_lieu_hang_hoan.json"), "[" + string.Join(",", rows) + "]");
        LegacyImporter.Import(legacyDir, dbPath);
        return dbPath;
    }

    private string CreateSettingsFile(string room, bool syncEnabled = true)
    {
        var path = Path.Combine(_tempRoot, "settings_" + Guid.NewGuid().ToString("N")[..8] + ".json");
        var settings = new AppSettings
        {
            Room = room,
            SyncEnabled = syncEnabled,
            NtfyEnabled = false
        };
        settings.Save(path);
        return path;
    }

    private static string BuildScanMessage(string room, string requestId, string code) =>
        $$"""{"type":"barcode","room":"{{room}}","requestId":"{{requestId}}","code":"{{code}}","captureId":"cap-1","time":1700000000}""";

    [Fact]
    public async Task InMemoryTransport_ScanUpdatesOrder_AddsColoredLog_AndReloadsOtherScreens()
    {
        var dbPath = CreateInitializedDbWithFakeOrders();
        var strongRoom = SyncRoom.Generate();
        var settingsPath = CreateSettingsFile(strongRoom, syncEnabled: true);
        var transport = new InMemoryTransport();

        using var controller = new RealSyncController(
            dbPath,
            settingsPath,
            _ => transport,
            () => FixedNow);

        var provider = new SqliteOrderProvider(dbPath);
        var importer = new SqliteLegacyImporter(Path.Combine(_tempRoot, "copies"), dbPath);
        using var mainVm = new MainViewModel(
            provider, provider, provider, provider, importer,
            isDemoMode: false,
            deferInitialLoad: false,
            syncController: controller,
            clipboardService: new SpyClipboard(),
            confirmationService: new FixedConfirm(true),
            dispatchToUi: a => a());

        controller.Start();

        Assert.Equal(0, mainVm.SidebarReceivedCount - 1); // TT-SYNC-DONE đã nhận sẵn (1)

        // 1. Gửi bản tin quét hợp lệ qua InMemoryTransport
        await transport.PublishAsync(strongRoom, BuildScanMessage(strongRoom, "req-1", "SPX-SYNC-A"));
        await Task.Delay(650); // chờ debounce (~500ms) + ReloadAllDataAsync hoàn tất

        var orders = OrderRepository.LoadActive(dbPath);
        Assert.Equal(ReceiveStatus.Received, orders.First(o => o.Track == "SPX-SYNC-A").Status);
        Assert.Equal(2, mainVm.SidebarReceivedCount); // tăng từ 1 -> 2 do ReloadAllDataAsync

        // 2. Gửi trùng, không tìm thấy, ambiguous
        await transport.PublishAsync(strongRoom, BuildScanMessage(strongRoom, "req-2", "TT-SYNC-DONE-1"));
        await transport.PublishAsync(strongRoom, BuildScanMessage(strongRoom, "req-3", "MA-KHONG-CO"));
        await transport.PublishAsync(strongRoom, BuildScanMessage(strongRoom, "req-4", "FWD-SYNC-AMB"));

        Assert.Equal(4, mainVm.PhoneSyncVm.Logs.Count);
        // Mới nhất trên cùng
        Assert.Equal("ambiguous", mainVm.PhoneSyncVm.Logs[0].Outcome);
        Assert.Equal("#FEE2E2", mainVm.PhoneSyncVm.Logs[0].BgHex);

        Assert.Equal("not_found", mainVm.PhoneSyncVm.Logs[1].Outcome);
        Assert.Equal("#FEE2E2", mainVm.PhoneSyncVm.Logs[1].BgHex);

        Assert.Equal("duplicate", mainVm.PhoneSyncVm.Logs[2].Outcome);
        Assert.Equal("#FEF3C7", mainVm.PhoneSyncVm.Logs[2].BgHex);

        Assert.Equal("success", mainVm.PhoneSyncVm.Logs[3].Outcome);
        Assert.Equal("#DCFCE7", mainVm.PhoneSyncVm.Logs[3].BgHex);
    }

    [Fact]
    public async Task WeakRoom_ShowsWarning_AndRegenerateRoom_StopsOldRoom_AndSavesSettings()
    {
        var dbPath = CreateInitializedDbWithFakeOrders();
        var settingsPath = CreateSettingsFile(SyncRoom.LegacyDefault, syncEnabled: true);
        var transport = new InMemoryTransport();

        using var controller = new RealSyncController(
            dbPath,
            settingsPath,
            _ => transport,
            () => FixedNow);

        var confirm = new FixedConfirm(false);
        var clipboard = new SpyClipboard();
        using var vm = new PhoneSyncViewModel(controller, clipboard, confirm, a => a());

        controller.Start();

        // Mã yếu -> hiện cảnh báo đỏ
        Assert.True(vm.IsRoomWeak);
        Assert.Equal(SyncRoom.LegacyDefault, vm.Room);

        // Sao chép
        vm.CopyRoomCommand.Execute(null);
        Assert.Equal(SyncRoom.LegacyDefault, clipboard.LastText);

        // Từ chối xác nhận -> không đổi mã
        vm.RegenerateRoomCommand.Execute(null);
        Assert.Equal(SyncRoom.LegacyDefault, vm.Room);

        // Chấp nhận xác nhận -> đổi sang mã mạnh, lưu settings, phòng cũ không còn xử lý
        confirm.Result = true;
        vm.RegenerateRoomCommand.Execute(null);

        var newRoom = vm.Room;
        Assert.NotEqual(SyncRoom.LegacyDefault, newRoom);
        Assert.False(vm.IsRoomWeak);
        Assert.True(SyncRoom.IsStrong(newRoom));

        var saved = AppSettings.LoadOrCreate(settingsPath);
        Assert.Equal(newRoom, saved.Room);

        // Bản tin gửi vào phòng cũ KHÔNG được xử lý
        await transport.PublishAsync(SyncRoom.LegacyDefault, BuildScanMessage(SyncRoom.LegacyDefault, "req-old", "SPX-SYNC-A"));
        Assert.Empty(vm.Logs);

        // Bản tin gửi vào phòng mới được xử lý
        await transport.PublishAsync(newRoom, BuildScanMessage(newRoom, "req-new", "SPX-SYNC-A"));
        Assert.Single(vm.Logs);
        Assert.Equal("success", vm.Logs[0].Outcome);
    }

    [Fact]
    public async Task ToggleSync_StopsAndResumesProcessing_AndSavesSettings()
    {
        var dbPath = CreateInitializedDbWithFakeOrders();
        var room = SyncRoom.Generate();
        var settingsPath = CreateSettingsFile(room, syncEnabled: true);
        var transport = new InMemoryTransport();

        using var controller = new RealSyncController(dbPath, settingsPath, _ => transport, () => FixedNow);
        using var vm = new PhoneSyncViewModel(controller, new SpyClipboard(), new FixedConfirm(true), a => a());
        controller.Start();

        Assert.True(vm.SyncEnabled);

        // Tắt đồng bộ -> lưu settings và ngừng xử lý
        vm.ToggleSyncCommand.Execute(null);
        Assert.False(vm.SyncEnabled);
        Assert.False(AppSettings.LoadOrCreate(settingsPath).SyncEnabled);

        await transport.PublishAsync(room, BuildScanMessage(room, "req-off", "SPX-SYNC-B"));
        Assert.Empty(vm.Logs);

        // Bật lại -> xử lý bình thường
        vm.ToggleSyncCommand.Execute(null);
        Assert.True(vm.SyncEnabled);
        Assert.True(AppSettings.LoadOrCreate(settingsPath).SyncEnabled);

        await transport.PublishAsync(room, BuildScanMessage(room, "req-on", "SPX-SYNC-B"));
        Assert.Single(vm.Logs);
        Assert.Equal("success", vm.Logs[0].Outcome);
    }

    [Fact]
    public void DemoMode_UsesFakeController_DoesNotConnectRealBroker()
    {
        var fakeData = new FakeDataService(() => FixedNow);
        var fakeSync = new FakeSyncController();
        using var mainVm = new MainViewModel(
            fakeData,
            fakeData,
            fakeData,
            fakeData,
            new FakeLegacyImporter(() => FixedNow),
            isDemoMode: true,
            syncController: fakeSync);

        Assert.True(mainVm.PhoneSyncVm.IsDemoMode);
        Assert.Equal(0, mainVm.PhoneSyncVm.ConnectedBrokers);
        Assert.False(mainVm.PhoneSyncVm.IsRoomWeak);
        Assert.Contains("--demo", mainVm.PhoneSyncVm.LastError);
    }

    [Fact]
    public void BackgroundThreadEvents_MarshalToUiThread_AndRenderScreenshots()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var outDir = Path.Combine(repoRoot, "tasks", "reports", "T010-screens");
        Directory.CreateDirectory(outDir);

        var dbPath = CreateInitializedDbWithFakeOrders();
        var provider = new SqliteOrderProvider(dbPath);
        var importer = new SqliteLegacyImporter(Path.Combine(_tempRoot, "copies"), dbPath);

        WpfTestHost.Run(() =>
        {
            var window = new MainWindow();
            var dispatcher = window.Dispatcher;
            Action<Action> dispatchToUi = a =>
            {
                if (dispatcher.CheckAccess()) a();
                else _ = dispatcher.BeginInvoke(a);
            };

            var stubController = new StubStatusSyncController(SyncRoom.Generate(), connected: 2, total: 3, lastError: "");
            using var mainVm = new MainViewModel(
                provider, provider, provider, provider, importer,
                isDemoMode: false,
                deferInitialLoad: false,
                syncController: stubController,
                clipboardService: new SpyClipboard(),
                confirmationService: new FixedConfirm(true),
                dispatchToUi: dispatchToUi);

            window.DataContext = mainVm;
            window.Width = 1280;
            window.Height = 760;
            mainVm.NavigateByKey("phone_sync");
            window.Show();
            WpfTestHost.PumpDispatcher(window);

            // Bắn sự kiện Handled từ luồng nền để kiểm chứng marshal UI thread không ném lỗi cross-thread
            RunOnBackgroundThread(() =>
            {
                stubController.EmitHandled(new SyncHandled("r1", "SPX-SYNC-A", "success", "Đã nhận kiện", FixedNow));
                stubController.EmitHandled(new SyncHandled("r2", "TT-SYNC-DONE-1", "duplicate", "Kiện đã nhận lúc 23/11/2026 09:15:30", FixedNow.AddSeconds(5)));
                stubController.EmitHandled(new SyncHandled("r3", "FWD-SYNC-AMB", "ambiguous", "Nhiều kiện khớp mã giao đi", FixedNow.AddSeconds(10)));
            });

            WpfTestHost.PumpDispatcher(window);
            Assert.Equal(3, mainVm.PhoneSyncVm.Logs.Count);
            WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "01-sync-connected.png"));

            // 2. Mất kết nối 0 broker + LastError từ luồng nền
            RunOnBackgroundThread(() => stubController.SetStatus(0, 3, "wss://broker.emqx.io:8084/mqtt: Timeout"));
            WpfTestHost.PumpDispatcher(window);
            WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "02-sync-disconnected.png"));

            // 3. Mã phòng yếu -> hiện cảnh báo đỏ
            RunOnBackgroundThread(() => stubController.SetWeakRoom(SyncRoom.LegacyDefault));
            WpfTestHost.PumpDispatcher(window);
            WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "03-sync-weak-room.png"));

            window.Close();
        });
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

    private sealed class SpyClipboard : IClipboardService
    {
        public string? LastText { get; private set; }
        public void SetText(string text) => LastText = text;
    }

    private sealed class FixedConfirm(bool result) : IConfirmationService
    {
        public bool Result { get; set; } = result;
        public bool Confirm(string title, string message) => Result;
    }

    private sealed class StubStatusSyncController(string room, int connected, int total, string lastError) : ISyncController
    {
        public string PrivateBroker => "";
        public void SetPrivateBroker(string url) { }
        public string Room { get; private set; } = room;
        public bool SyncEnabled { get; private set; } = true;
        public bool NtfyEnabled { get; private set; } = true;
        public int ConnectedBrokers { get; private set; } = connected;
        public int TotalBrokers { get; } = total;
        public bool NtfyConnected { get; private set; }
        public string MqttLastError { get; private set; } = lastError;
        public string NtfyLastError { get; private set; } = string.Empty;
        public string LastError { get; private set; } = lastError;
        public bool IsDemoMode => false;

        public event Action<SyncHandled>? Handled;
        public event Action? StatusChanged;

        public void Start() => StatusChanged?.Invoke();
        public void SetSyncEnabled(bool enabled) { SyncEnabled = enabled; StatusChanged?.Invoke(); }
        public void SetNtfyEnabled(bool enabled) { NtfyEnabled = enabled; StatusChanged?.Invoke(); }
        public void RegenerateRoom() { Room = SyncRoom.Generate(); StatusChanged?.Invoke(); }
        public void EmitHandled(SyncHandled item) => Handled?.Invoke(item);

        public void SetStatus(int connectedBrokers, int totalBrokers, string error)
        {
            ConnectedBrokers = connectedBrokers;
            MqttLastError = error;
            LastError = error;
            StatusChanged?.Invoke();
        }

        public void SetWeakRoom(string weakRoom)
        {
            Room = weakRoom;
            StatusChanged?.Invoke();
        }

        public void Dispose() { }
    }
}
