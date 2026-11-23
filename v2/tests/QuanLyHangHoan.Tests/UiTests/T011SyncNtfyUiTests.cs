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

public sealed class T011SyncNtfyUiTests : IDisposable
{
    private static readonly DateTime FixedNow = new(2026, 11, 23, 17, 15, 0);
    private readonly string _tempRoot;

    public T011SyncNtfyUiTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "QuanLyHangHoan_T011_" + Guid.NewGuid().ToString("N"));
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
            """{"id":"n1","orderId":"SHP-NTFY-1","track":"SPX-NTFY-A","returnTrack":"SPX-NTFY-A","platform":"Shopee","orderType":"Trả hàng/Hoàn tiền","status":"Chưa trả","returnedAt":"23/11/2026 08:00","price":150000,"items":[{"product":"Áo Demo ntfy","color":"Trắng","size":"M","qty":1}]}""",
            """{"id":"n2","orderId":"SHP-NTFY-2","track":"SPX-NTFY-B","returnTrack":"SPX-NTFY-B","platform":"Shopee","orderType":"Trả hàng/Hoàn tiền","status":"Chưa trả","returnedAt":"23/11/2026 08:00","price":120000,"items":[{"product":"Quần Demo ntfy","color":"Đen","size":"L","qty":1}]}"""
        };

        File.WriteAllText(Path.Combine(legacyDir, "du_lieu_hang_hoan.json"), "[" + string.Join(",", rows) + "]");
        LegacyImporter.Import(legacyDir, dbPath);
        return dbPath;
    }

    private string CreateSettingsFile(string room, bool syncEnabled = true, bool ntfyEnabled = true)
    {
        var path = Path.Combine(_tempRoot, "settings_" + Guid.NewGuid().ToString("N")[..8] + ".json");
        var settings = new AppSettings
        {
            Room = room,
            SyncEnabled = syncEnabled,
            NtfyEnabled = ntfyEnabled
        };
        settings.Save(path);
        return path;
    }

    private static string BuildScanMessage(string room, string requestId, string code) =>
        $$"""{"type":"barcode","room":"{{room}}","requestId":"{{requestId}}","code":"{{code}}","captureId":"cap-1","time":1700000000}""";

    [Fact]
    public void NtfyEnabled_CreatesBothTransports_StartsServiceBeforeTransports_AndNtfyDisabled_CreatesOnlyMqtt()
    {
        var dbPath = CreateInitializedDbWithFakeOrders();
        var room = SyncRoom.Generate();
        var callLog = new List<string>();

        // 1. NtfyEnabled = true -> tạo đủ 2 đường, Start() sau Subscribe()
        var settingsOn = CreateSettingsFile(room, syncEnabled: true, ntfyEnabled: true);
        var mqttCount = 0;
        var ntfyCount = 0;
        FakeManagedTransport? lastMqtt = null;
        FakeManagedTransport? lastNtfy = null;

        using (var controller = new RealSyncController(
            dbPath,
            settingsOn,
            _ => { mqttCount++; lastMqtt = new FakeManagedTransport("mqtt", callLog); return lastMqtt; },
            () => FixedNow,
            () => { ntfyCount++; lastNtfy = new FakeManagedTransport("ntfy", callLog); return lastNtfy; }))
        {
            controller.Start();
            Assert.Equal(1, mqttCount);
            Assert.Equal(1, ntfyCount);
            Assert.NotNull(lastMqtt);
            Assert.NotNull(lastNtfy);
            Assert.True(lastMqtt!.Started);
            Assert.True(lastNtfy!.Started);

            // SyncService.Start() (Subscribe) phải chạy trước Transport.Start()
            var mqttSubIdx = callLog.IndexOf("mqtt:subscribe");
            var ntfySubIdx = callLog.IndexOf("ntfy:subscribe");
            var mqttStartIdx = callLog.IndexOf("mqtt:start");
            var ntfyStartIdx = callLog.IndexOf("ntfy:start");
            Assert.True(mqttSubIdx >= 0 && mqttSubIdx < mqttStartIdx);
            Assert.True(ntfySubIdx >= 0 && ntfySubIdx < ntfyStartIdx);
        }

        // 2. NtfyEnabled = false -> chỉ tạo MQTT
        var settingsOff = CreateSettingsFile(room, syncEnabled: true, ntfyEnabled: false);
        mqttCount = 0;
        ntfyCount = 0;

        using (var controllerOff = new RealSyncController(
            dbPath,
            settingsOff,
            _ => { mqttCount++; return new FakeManagedTransport("mqtt"); },
            () => FixedNow,
            () => { ntfyCount++; return new FakeManagedTransport("ntfy"); }))
        {
            controllerOff.Start();
            Assert.Equal(1, mqttCount);
            Assert.Equal(0, ntfyCount);
        }
    }

    [Fact]
    public void RegenerateRoom_DisposesBothTransports_AndUnsubscribesStatusChanged()
    {
        var dbPath = CreateInitializedDbWithFakeOrders();
        var room = SyncRoom.Generate();
        var settingsPath = CreateSettingsFile(room, syncEnabled: true, ntfyEnabled: true);

        var createdMqtt = new List<FakeManagedTransport>();
        var createdNtfy = new List<FakeManagedTransport>();

        using var controller = new RealSyncController(
            dbPath,
            settingsPath,
            _ => { var t = new FakeManagedTransport("mqtt"); createdMqtt.Add(t); return t; },
            () => FixedNow,
            () => { var t = new FakeManagedTransport("ntfy"); createdNtfy.Add(t); return t; });

        controller.Start();
        Assert.Single(createdMqtt);
        Assert.Single(createdNtfy);

        var statusChangeEvents = 0;
        controller.StatusChanged += () => statusChangeEvents++;

        controller.RegenerateRoom();

        Assert.True(createdMqtt[0].Disposed);
        Assert.True(createdNtfy[0].Disposed);
        Assert.Equal(2, createdMqtt.Count);
        Assert.Equal(2, createdNtfy.Count);

        var before = statusChangeEvents;
        // Sự kiện từ transport cũ đã hủy đăng ký -> không làm tăng statusChangeEvents
        createdMqtt[0].RaiseStatusChanged();
        createdNtfy[0].RaiseStatusChanged();
        Assert.Equal(before, statusChangeEvents);

        // Sự kiện từ transport mới vẫn hoạt động
        createdMqtt[1].RaiseStatusChanged();
        Assert.Equal(before + 1, statusChangeEvents);
    }

    [Fact]
    public async Task MessageViaMqttOrNtfy_OrBothWithSameRequestId_ProcessedOnce_AndNoDuplicateLog()
    {
        var dbPath = CreateInitializedDbWithFakeOrders();
        var room = SyncRoom.Generate();
        var settingsPath = CreateSettingsFile(room, syncEnabled: true, ntfyEnabled: true);

        var mqttTransport = new FakeManagedTransport("mqtt");
        var ntfyTransport = new FakeManagedTransport("ntfy");

        using var controller = new RealSyncController(
            dbPath,
            settingsPath,
            _ => mqttTransport,
            () => FixedNow,
            () => ntfyTransport);

        using var vm = new PhoneSyncViewModel(controller, new SpyClipboard(), new FixedConfirm(true), a => a());
        controller.Start();

        // Cùng 1 bản tin (req-dup-1) đến qua CẢ MQTT lẫn ntfy -> chỉ xử lý 1 lần, nhật ký chỉ 1 dòng
        var msg1 = BuildScanMessage(room, "req-dup-1", "SPX-NTFY-A");
        await mqttTransport.PublishAsync(room, msg1);
        await ntfyTransport.PublishAsync(room, msg1);

        var orders = OrderRepository.LoadActive(dbPath);
        Assert.Equal(ReceiveStatus.Received, orders.First(o => o.Track == "SPX-NTFY-A").Status);
        Assert.Single(vm.Logs);
        Assert.Equal("SPX-NTFY-A", vm.Logs[0].Code);
        Assert.Equal("success", vm.Logs[0].Outcome);

        // Bản tin thứ 2 chỉ đi qua đường ntfy -> vẫn nhận bình thường
        var msg2 = BuildScanMessage(room, "req-ntfy-only", "SPX-NTFY-B");
        await ntfyTransport.PublishAsync(room, msg2);

        orders = OrderRepository.LoadActive(dbPath);
        Assert.Equal(ReceiveStatus.Received, orders.First(o => o.Track == "SPX-NTFY-B").Status);
        Assert.Equal(2, vm.Logs.Count);
        Assert.Equal("SPX-NTFY-B", vm.Logs[0].Code);
        Assert.Equal("success", vm.Logs[0].Outcome);
    }

    [Fact]
    public void ChannelStatusCombinations_AndToggleNtfy_SavesSettingsAndUpdatesColors()
    {
        var dbPath = CreateInitializedDbWithFakeOrders();
        var room = SyncRoom.Generate();
        var settingsPath = CreateSettingsFile(room, syncEnabled: true, ntfyEnabled: true);

        var mqtt = new FakeManagedTransport("mqtt");
        var ntfy = new FakeManagedTransport("ntfy");

        using var controller = new RealSyncController(
            dbPath,
            settingsPath,
            _ => mqtt,
            () => FixedNow,
            () => ntfy);

        using var vm = new PhoneSyncViewModel(controller, new SpyClipboard(), new FixedConfirm(true), a => a());
        controller.Start();

        // 1. MQTT up + ntfy down -> tổng hợp xanh, MQTT xanh, ntfy đỏ
        mqtt.SetState(connected: true, connectedCount: 2, error: "");
        ntfy.SetState(connected: false, connectedCount: 0, error: "ntfy: Timeout kết nối");
        Assert.True(vm.IsConnected);
        Assert.Equal("#DCFCE7", vm.ConnectionBgHex);
        Assert.Equal("MQTT: 2/3 broker", vm.MqttStatusText);
        Assert.Equal("#DCFCE7", vm.MqttBgHex);
        Assert.Equal("ntfy: Mất kết nối", vm.NtfyStatusText);
        Assert.Equal("#FEE2E2", vm.NtfyBgHex);
        Assert.Equal("ntfy: Timeout kết nối", vm.NtfyLastError);

        // 2. MQTT down + ntfy up -> tổng hợp xanh, MQTT đỏ, ntfy xanh
        mqtt.SetState(connected: false, connectedCount: 0, error: "wss://broker.emqx.io: Timeout");
        ntfy.SetState(connected: true, connectedCount: 1, error: "");
        Assert.True(vm.IsConnected);
        Assert.Equal("#DCFCE7", vm.ConnectionBgHex);
        Assert.Equal("MQTT: 0/3 broker", vm.MqttStatusText);
        Assert.Equal("#FEE2E2", vm.MqttBgHex);
        Assert.Equal("wss://broker.emqx.io: Timeout", vm.MqttLastError);
        Assert.Equal("ntfy: Đã kết nối", vm.NtfyStatusText);
        Assert.Equal("#DCFCE7", vm.NtfyBgHex);

        // 3. Cả hai down -> tổng hợp đỏ
        mqtt.SetState(connected: false, connectedCount: 0, error: "MQTT offline");
        ntfy.SetState(connected: false, connectedCount: 0, error: "ntfy offline");
        Assert.False(vm.IsConnected);
        Assert.Equal("#FEE2E2", vm.ConnectionBgHex);

        // 4. Cả hai up -> tổng hợp xanh
        mqtt.SetState(connected: true, connectedCount: 3, error: "");
        ntfy.SetState(connected: true, connectedCount: 1, error: "");
        Assert.True(vm.IsConnected);
        Assert.Equal("#DCFCE7", vm.ConnectionBgHex);

        // 5. Tắt ntfy -> lưu AppSettings.NtfyEnabled = false, ntfy chuyển xám ("Đã tắt"), dispose ntfy cũ
        vm.ToggleNtfyCommand.Execute(null);
        Assert.False(vm.NtfyEnabled);
        Assert.False(AppSettings.LoadOrCreate(settingsPath).NtfyEnabled);
        Assert.True(ntfy.Disposed);
        Assert.Equal("ntfy: Đã tắt", vm.NtfyStatusText);
        Assert.Equal("#F1F5F9", vm.NtfyBgHex);
    }

    [Fact]
    public void DemoMode_FakeSyncController_RendersScreenshotsForAllChannelCombinations()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var outDir = Path.Combine(repoRoot, "tasks", "reports", "T011-screens");
        Directory.CreateDirectory(outDir);

        var fakeData = new FakeDataService(() => FixedNow);
        var fakeSync = new FakeSyncController();

        WpfTestHost.Run(() =>
        {
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
                deferInitialLoad: false,
                syncController: fakeSync,
                clipboardService: new SpyClipboard(),
                confirmationService: new FixedConfirm(true),
                dispatchToUi: dispatchToUi);

            window.DataContext = mainVm;
            window.Width = 1280;
            window.Height = 760;
            mainVm.NavigateByKey("phone_sync");
            window.Show();
            WpfTestHost.PumpDispatcher(window);

            fakeSync.EmitDemoHandled(new SyncHandled("demo-1", "SPX-DEMO-001", "success", "Đã nhận kiện", FixedNow));

            // 1. MQTT up + ntfy down
            fakeSync.SetDemoStatus(
                connectedBrokers: 3,
                ntfyConnected: false,
                mqttLastError: "",
                ntfyLastError: "ntfy: Kết nối ntfy.sh quá hạn (mạng có thể đang chặn ntfy.sh)",
                ntfyEnabled: true);
            WpfTestHost.PumpDispatcher(window);
            Assert.True(mainVm.PhoneSyncVm.IsConnected);
            Assert.Equal("MQTT: 3/3 broker", mainVm.PhoneSyncVm.MqttStatusText);
            Assert.Equal("ntfy: Mất kết nối", mainVm.PhoneSyncVm.NtfyStatusText);
            WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "01-mqtt-up-ntfy-down.png"));

            // 2. Cả hai up
            fakeSync.SetDemoStatus(
                connectedBrokers: 3,
                ntfyConnected: true,
                mqttLastError: "",
                ntfyLastError: "",
                ntfyEnabled: true);
            WpfTestHost.PumpDispatcher(window);
            Assert.True(mainVm.PhoneSyncVm.IsConnected);
            Assert.Equal("ntfy: Đã kết nối", mainVm.PhoneSyncVm.NtfyStatusText);
            WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "02-both-up.png"));

            // 3. ntfy đã tắt
            fakeSync.SetDemoStatus(
                connectedBrokers: 3,
                ntfyConnected: false,
                mqttLastError: "",
                ntfyLastError: "",
                ntfyEnabled: false);
            WpfTestHost.PumpDispatcher(window);
            Assert.True(mainVm.PhoneSyncVm.IsConnected);
            Assert.Equal("ntfy: Đã tắt", mainVm.PhoneSyncVm.NtfyStatusText);
            WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "03-ntfy-disabled.png"));

            window.Close();
        });
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

    private sealed class FakeManagedTransport : IManagedSyncTransport
    {
        private readonly string _name;
        private readonly List<string>? _callLog;
        private readonly List<(string Room, Action<string, string> Handler)> _subs = new();

        public FakeManagedTransport(string name, List<string>? callLog = null)
        {
            _name = name;
            _callLog = callLog;
        }

        public bool Started { get; private set; }
        public bool Disposed { get; private set; }
        public bool IsConnected { get; private set; }
        public int ConnectedCount { get; private set; }
        public string LastError { get; private set; } = string.Empty;

        public event Action? StatusChanged;

        public void Start()
        {
            Started = true;
            _callLog?.Add($"{_name}:start");
        }

        public void SetState(bool connected, int connectedCount, string error)
        {
            IsConnected = connected;
            ConnectedCount = connectedCount;
            LastError = error;
            StatusChanged?.Invoke();
        }

        public void RaiseStatusChanged() => StatusChanged?.Invoke();

        public Task PublishAsync(string room, string message)
        {
            List<Action<string, string>> targets;
            lock (_subs) targets = _subs.Where(s => s.Room == room).Select(s => s.Handler).ToList();
            foreach (var h in targets) h(room, message);
            return Task.CompletedTask;
        }

        public IDisposable Subscribe(string room, Action<string, string> onMessage)
        {
            _callLog?.Add($"{_name}:subscribe");
            var entry = (room, onMessage);
            lock (_subs) _subs.Add(entry);
            return new Unsub(() => { lock (_subs) _subs.Remove(entry); });
        }

        public void Dispose() => Disposed = true;

        private sealed class Unsub(Action a) : IDisposable { public void Dispose() => a(); }
    }
}
