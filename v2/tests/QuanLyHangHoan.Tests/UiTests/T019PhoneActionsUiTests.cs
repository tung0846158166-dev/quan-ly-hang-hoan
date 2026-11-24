using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.App.Views;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;
using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.Tests.UiTests;

public sealed class T019PhoneActionsUiTests : IDisposable
{
    private static readonly DateTime FixedNow = new(2026, 11, 23, 19, 10, 0);
    private readonly string _tempRoot;

    public T019PhoneActionsUiTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "QuanLyHangHoan_T019_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* ignore temp cleanup */ }
        }
    }

    private sealed class StubSyncController : ISyncController
    {
        public string Room => "TEST-ROOM-1234567890123456";
        public bool SyncEnabled => true;
        public bool NtfyEnabled => false;
        public int ConnectedBrokers => 3;
        public int TotalBrokers => 3;
        public bool NtfyConnected => false;
        public string MqttLastError => string.Empty;
        public string NtfyLastError => string.Empty;
        public string LastError => string.Empty;
        public bool IsDemoMode => false;

        public event Action<SyncHandled>? Handled;
        public event Action? StatusChanged;

        public void Emit(SyncHandled item) => Handled?.Invoke(item);
        public void Start() => StatusChanged?.Invoke();
        public void SetSyncEnabled(bool enabled) { }
        public void SetNtfyEnabled(bool enabled) { }
        public void RegenerateRoom() { }
        public void Dispose() { }
    }

    private sealed class CountingReturnProvider : IReturnOrderProvider
    {
        private readonly IReturnOrderProvider _inner;
        public int AsyncLoadCount { get; private set; }

        public CountingReturnProvider(IReturnOrderProvider inner) => _inner = inner;

        public DateTime GetCurrentTime() => FixedNow;
        public IReadOnlyList<Order> GetReturnOrders(int count = 40) => _inner.GetReturnOrders(count);

        public Task<IReadOnlyList<Order>> GetReturnOrdersAsync(int count = 40, CancellationToken ct = default)
        {
            AsyncLoadCount++;
            return _inner.GetReturnOrdersAsync(count, ct);
        }

        public PlaceholderScreenData GetPlaceholderScreenData(string screenKey) => _inner.GetPlaceholderScreenData(screenKey);
    }

    private sealed class StubClipboard : IClipboardService
    {
        public void SetText(string text) { }
    }

    private sealed class StubConfirm : IConfirmationService
    {
        public bool Confirm(string title, string message) => true;
    }

    private string CreateTempDbWithFakeOrders()
    {
        var dbPath = Path.Combine(_tempRoot, "t019_" + Guid.NewGuid().ToString("N")[..8] + ".db");
        AppDatabase.Initialize(dbPath);

        var legacyDir = Path.Combine(_tempRoot, "legacy_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(legacyDir);

        var rows = new List<string>
        {
            """{"id":"o1","orderId":"SHP-T019-1","track":"SPX-T019-001","returnTrack":"SPX-T019-001","platform":"Shopee","orderType":"Trả hàng/Hoàn tiền","status":"Chưa trả","returnedAt":"23/11/2026 08:00","price":150000,"items":[{"product":"Áo thun giả lập","color":"Trắng","size":"M","qty":1}]}""",
            """{"id":"o2","orderId":"TT-T019-2","track":"TT-T019-002","returnTrack":"TT-T019-002","platform":"TikTok","orderType":"Trả hàng/Hoàn tiền","status":"Chưa trả","returnedAt":"23/11/2026 09:00","price":180000,"items":[{"product":"Quần giả lập","color":"Đen","size":"L","qty":1}]}"""
        };

        File.WriteAllText(Path.Combine(legacyDir, "du_lieu_hang_hoan.json"), "[" + string.Join(",", rows) + "]");
        LegacyImporter.Import(legacyDir, dbPath);
        return dbPath;
    }

    private void InsertFakeNewOrderIntoTempDb(string dbPath, string track, string platform)
    {
        var stamp = FixedNow.ToString("dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        var legacyDir = Path.Combine(_tempRoot, "new_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(legacyDir);
        var id = "phone_" + Guid.NewGuid().ToString("N")[..8];
        var json = $$"""[{"id":"{{id}}","orderId":"{{track}}","track":"{{track}}","returnTrack":"{{track}}","platform":"{{platform}}","orderType":"Trả hàng/Hoàn tiền","status":"Chưa trả","returnedAt":"{{stamp}}","note":"Đơn mới tạo từ máy quét điện thoại","price":0,"items":[{"product":"Sản phẩm mới (Quét điện thoại)","qty":1}]}]""";
        File.WriteAllText(Path.Combine(legacyDir, "du_lieu_hang_hoan.json"), json);
        LegacyImporter.Import(legacyDir, dbPath);
    }

    [Fact]
    public async Task NewOrder_ReloadsMainTable_ShowsToast_IncrementsCounter_AndLogsNewOrder()
    {
        var dbPath = CreateTempDbWithFakeOrders();
        var sqliteProvider = new SqliteOrderProvider(dbPath);
        var countingProvider = new CountingReturnProvider(sqliteProvider);
        var importer = new SqliteLegacyImporter(Path.Combine(_tempRoot, "copies"), dbPath);
        var sync = new StubSyncController();

        using var vm = new MainViewModel(
            countingProvider, sqliteProvider, sqliteProvider, sqliteProvider, importer,
            isDemoMode: false,
            deferInitialLoad: false,
            syncController: sync,
            clipboardService: new StubClipboard(),
            confirmationService: new StubConfirm(),
            dispatchToUi: a => a(),
            syncReloadDebounce: TimeSpan.FromMilliseconds(50));

        await vm.ReloadAllDataAsync();
        var loadsBefore = countingProvider.AsyncLoadCount;
        Assert.Single(vm.ShopeeReturnVm.VisibleOrders);

        // Giữ dòng đang chọn qua lần tải lại
        vm.ShopeeReturnVm.SelectedRow = vm.ShopeeReturnVm.VisibleOrders[0];
        var selectedId = vm.ShopeeReturnVm.SelectedRow.Source.Id;

        InsertFakeNewOrderIntoTempDb(dbPath, "SPXVN061234567890", "Shopee");
        sync.Emit(new SyncHandled("req-new-1", "SPXVN061234567890", "success", "ĐÃ TẠO ĐƠN MỚI Ở BẢNG CHÍNH (Shopee)", FixedNow, "scan", "new_order"));

        await Task.Delay(150);

        Assert.Equal(loadsBefore + 1, countingProvider.AsyncLoadCount);
        Assert.Equal(2, vm.ShopeeReturnVm.VisibleOrders.Count);
        Assert.Contains(vm.ShopeeReturnVm.VisibleOrders, r => r.DisplayTracking == "SPXVN061234567890");
        Assert.Equal(selectedId, vm.ShopeeReturnVm.SelectedRow?.Source.Id);

        Assert.True(vm.IsToastVisible);
        Assert.False(vm.ToastIsError);
        Assert.False(vm.ToastIsWarning);
        Assert.Equal("📥 Điện thoại vừa tạo đơn mới: SPXVN061234567890 (Shopee)", vm.ToastMessage);

        Assert.Equal(1, vm.PhoneSyncVm.NewOrdersTodayCount);
        Assert.Equal("Đơn mới từ điện thoại hôm nay: 1", vm.PhoneSyncVm.NewOrdersTodayText);
        Assert.Single(vm.PhoneSyncVm.Logs);
        Assert.Equal("Đơn mới", vm.PhoneSyncVm.Logs[0].StageLabel);
        Assert.True(vm.PhoneSyncVm.Logs[0].IsNewOrder);
        Assert.Equal("#DCFCE7", vm.PhoneSyncVm.Logs[0].BgHex);
    }

    [Fact]
    public async Task DuplicateScan_ShowsYellowToast_DoesNotReload_AndLogsYellow()
    {
        var dbPath = CreateTempDbWithFakeOrders();
        var sqliteProvider = new SqliteOrderProvider(dbPath);
        var countingProvider = new CountingReturnProvider(sqliteProvider);
        var importer = new SqliteLegacyImporter(Path.Combine(_tempRoot, "copies"), dbPath);
        var sync = new StubSyncController();

        using var vm = new MainViewModel(
            countingProvider, sqliteProvider, sqliteProvider, sqliteProvider, importer,
            isDemoMode: false,
            deferInitialLoad: false,
            syncController: sync,
            clipboardService: new StubClipboard(),
            confirmationService: new StubConfirm(),
            dispatchToUi: a => a(),
            syncReloadDebounce: TimeSpan.FromMilliseconds(50));

        await vm.ReloadAllDataAsync();
        var loadsBefore = countingProvider.AsyncLoadCount;

        sync.Emit(new SyncHandled("req-dup-1", "SPX-T019-001", "duplicate", "ĐƠN ĐÃ CÓ Ở BẢNG CHÍNH, KHÔNG TẠO LẠI", FixedNow, "scan"));
        await Task.Delay(120);

        Assert.Equal(loadsBefore, countingProvider.AsyncLoadCount);
        Assert.True(vm.IsToastVisible);
        Assert.False(vm.ToastIsError);
        Assert.True(vm.ToastIsWarning);
        Assert.Equal("⚠️ Điện thoại quét đơn đã có: SPX-T019-001", vm.ToastMessage);

        Assert.Single(vm.PhoneSyncVm.Logs);
        Assert.Equal("Nhận kiện", vm.PhoneSyncVm.Logs[0].StageLabel);
        Assert.Equal("#FEF3C7", vm.PhoneSyncVm.Logs[0].BgHex);
        Assert.Equal(0, vm.PhoneSyncVm.NewOrdersTodayCount);
    }

    [Theory]
    [InlineData("error")]
    [InlineData("invalid")]
    [InlineData("ambiguous")]
    [InlineData("not_found")]
    [InlineData("locked")]
    public async Task Errors_OnlyGoToLog_NoToast_NoReload(string outcome)
    {
        var dbPath = CreateTempDbWithFakeOrders();
        var sqliteProvider = new SqliteOrderProvider(dbPath);
        var countingProvider = new CountingReturnProvider(sqliteProvider);
        var importer = new SqliteLegacyImporter(Path.Combine(_tempRoot, "copies"), dbPath);
        var sync = new StubSyncController();

        using var vm = new MainViewModel(
            countingProvider, sqliteProvider, sqliteProvider, sqliteProvider, importer,
            isDemoMode: false,
            deferInitialLoad: false,
            syncController: sync,
            clipboardService: new StubClipboard(),
            confirmationService: new StubConfirm(),
            dispatchToUi: a => a(),
            syncReloadDebounce: TimeSpan.FromMilliseconds(50));

        await vm.ReloadAllDataAsync();
        var loadsBefore = countingProvider.AsyncLoadCount;

        sync.Emit(new SyncHandled("req-err-" + outcome, "SPX-ERR-1", outcome, "Lỗi xử lý " + outcome, FixedNow, "scan"));
        await Task.Delay(120);

        Assert.Equal(loadsBefore, countingProvider.AsyncLoadCount);
        Assert.False(vm.IsToastVisible);
        Assert.Single(vm.PhoneSyncVm.Logs);
        Assert.Equal("#FEE2E2", vm.PhoneSyncVm.Logs[0].BgHex);
    }

    [Fact]
    public async Task DeleteEditNote_ReloadAndToast_WhilePhoto_DoesNotReloadOrToast()
    {
        var dbPath = CreateTempDbWithFakeOrders();
        var sqliteProvider = new SqliteOrderProvider(dbPath);
        var countingProvider = new CountingReturnProvider(sqliteProvider);
        var importer = new SqliteLegacyImporter(Path.Combine(_tempRoot, "copies"), dbPath);
        var sync = new StubSyncController();

        using var vm = new MainViewModel(
            countingProvider, sqliteProvider, sqliteProvider, sqliteProvider, importer,
            isDemoMode: false,
            deferInitialLoad: false,
            syncController: sync,
            clipboardService: new StubClipboard(),
            confirmationService: new StubConfirm(),
            dispatchToUi: a => a(),
            syncReloadDebounce: TimeSpan.FromMilliseconds(40));

        await vm.ReloadAllDataAsync();
        var loads = countingProvider.AsyncLoadCount;

        // Chờ theo điều kiện (tối đa 5 giây) thay vì ngủ cố định 100 ms: máy bận thì tải lại chậm hơn nhưng vẫn đúng.
        static async Task WaitFor(Func<bool> cond)
        {
            for (var i = 0; i < 100 && !cond(); i++) await Task.Delay(50);
        }

        // 1. note
        sync.Emit(new SyncHandled("req-note", "SPX-T019-001", "success", "Đã lưu ghi chú", FixedNow, "note"));
        loads++; await WaitFor(() => countingProvider.AsyncLoadCount >= loads && vm.PhoneSyncVm.Logs.Count > 0 && vm.ToastMessage.Contains("ghi chú"));
        Assert.Equal(loads, countingProvider.AsyncLoadCount);
        Assert.Equal("📝 Điện thoại cập nhật ghi chú đơn SPX-T019-001", vm.ToastMessage);
        Assert.Equal("Ghi chú", vm.PhoneSyncVm.Logs[0].StageLabel);

        // 2. edit
        sync.Emit(new SyncHandled("req-edit", "SPX-T019-001", "success", "Đã cập nhật trạng thái", FixedNow, "edit"));
        loads++; await WaitFor(() => countingProvider.AsyncLoadCount >= loads && vm.ToastMessage.Contains("trạng thái"));
        Assert.Equal(loads, countingProvider.AsyncLoadCount);
        Assert.Equal("📝 Điện thoại cập nhật trạng thái đơn SPX-T019-001", vm.ToastMessage);
        Assert.Equal("Sửa trạng thái", vm.PhoneSyncVm.Logs[0].StageLabel);

        // 3. delete
        sync.Emit(new SyncHandled("req-del", "SPX-T019-001", "success", "Đã xóa đơn", FixedNow, "delete"));
        loads++; await WaitFor(() => countingProvider.AsyncLoadCount >= loads && vm.ToastMessage.Contains("xóa đơn"));
        Assert.Equal(loads, countingProvider.AsyncLoadCount);
        Assert.Equal("🗑 Điện thoại xóa đơn SPX-T019-001 (khôi phục được ở Đã xóa)", vm.ToastMessage);
        Assert.Equal("Xóa", vm.PhoneSyncVm.Logs[0].StageLabel);

        // 4. photo -> không tải lại, không toast mới
        vm.DismissToast();
        sync.Emit(new SyncHandled("req-photo", "SPX-T019-001", "success", "Đã lưu ảnh", FixedNow, "photo"));
        await WaitFor(() => vm.PhoneSyncVm.Logs[0].StageLabel == "Ảnh");   // log ghi nhận trước; sau đó đợi thêm một nhịp để chắc chắn không có tải lại
        await Task.Delay(300);
        Assert.Equal(loads, countingProvider.AsyncLoadCount);
        Assert.False(vm.IsToastVisible);
        Assert.Equal("Ảnh", vm.PhoneSyncVm.Logs[0].StageLabel);
    }
    [Fact]
    public async Task BurstEvents_CoalesceIntoSingleReload()
    {
        var dbPath = CreateTempDbWithFakeOrders();
        var sqliteProvider = new SqliteOrderProvider(dbPath);
        var countingProvider = new CountingReturnProvider(sqliteProvider);
        var importer = new SqliteLegacyImporter(Path.Combine(_tempRoot, "copies"), dbPath);
        var sync = new StubSyncController();

        using var vm = new MainViewModel(
            countingProvider, sqliteProvider, sqliteProvider, sqliteProvider, importer,
            isDemoMode: false,
            deferInitialLoad: false,
            syncController: sync,
            clipboardService: new StubClipboard(),
            confirmationService: new StubConfirm(),
            dispatchToUi: a => a()); // mặc định 500ms

        await vm.ReloadAllDataAsync();
        var loadsBefore = countingProvider.AsyncLoadCount;

        for (var i = 1; i <= 5; i++)
        {
            sync.Emit(new SyncHandled($"burst-{i}", $"SPX-B-{i}", "success", "Đã nhận", FixedNow.AddSeconds(i), "scan"));
        }

        await Task.Delay(750);
        Assert.Equal(loadsBefore + 1, countingProvider.AsyncLoadCount);
        Assert.Equal(5, vm.PhoneSyncVm.Logs.Count);
    }

    [Fact]
    public void DemoMode_EmitsSampleEvents_WithoutTouchingDisk()
    {
        var fakeData = new FakeDataService(() => FixedNow);
        var fakeSync = new FakeSyncController();
        using var vm = new MainViewModel(
            fakeData, fakeData, fakeData, fakeData,
            new FakeLegacyImporter(() => FixedNow),
            isDemoMode: true,
            syncController: fakeSync,
            clipboardService: new StubClipboard(),
            confirmationService: new StubConfirm(),
            dispatchToUi: a => a(),
            syncReloadDebounce: TimeSpan.Zero);

        vm.PhoneSyncVm.EmitDemoSamplesCommand.Execute(null);

        Assert.Equal(8, vm.PhoneSyncVm.Logs.Count);
        Assert.Equal(1, vm.PhoneSyncVm.NewOrdersTodayCount);
        Assert.Contains(vm.PhoneSyncVm.Logs, l => l.StageLabel == "Đơn mới" && l.IsNewOrder);
        Assert.Contains(vm.PhoneSyncVm.Logs, l => l.StageLabel == "Nhận kiện");
        Assert.Contains(vm.PhoneSyncVm.Logs, l => l.StageLabel == "Ghi chú");
        Assert.Contains(vm.PhoneSyncVm.Logs, l => l.StageLabel == "Sửa trạng thái");
        Assert.Contains(vm.PhoneSyncVm.Logs, l => l.StageLabel == "Xóa");
        Assert.Contains(vm.PhoneSyncVm.Logs, l => l.StageLabel == "Ảnh");
    }

    [Fact]
    public void RenderScreenshots_PhoneSyncAndNewOrderToastOnMainTable()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var outDir = Path.Combine(repoRoot, "tasks", "reports", "T019-screens");
        Directory.CreateDirectory(outDir);

        var dbPath = CreateTempDbWithFakeOrders();
        InsertFakeNewOrderIntoTempDb(dbPath, "SPXVN069876543210", "Shopee");
        var provider = new SqliteOrderProvider(dbPath);
        var importer = new SqliteLegacyImporter(Path.Combine(_tempRoot, "copies"), dbPath);
        var sync = new StubSyncController();

        WpfTestHost.Run(() =>
        {
            using var mainVm = new MainViewModel(
                provider, provider, provider, provider, importer,
                isDemoMode: false,
                deferInitialLoad: false,
                syncController: sync,
                clipboardService: new StubClipboard(),
                confirmationService: new StubConfirm(),
                dispatchToUi: a => a(),
                syncReloadDebounce: TimeSpan.Zero);

            mainVm.ShopeeReturnVm.LoadOrders(provider.GetReturnOrders(), FixedNow);

            sync.Emit(new SyncHandled("s-err", "SPX-CANCEL", "error", "v2 không có chức năng tạo đơn hủy từ điện thoại", FixedNow.AddSeconds(1), "scan"));
            sync.Emit(new SyncHandled("s-photo", "SPX-T019-001", "success", "Đã lưu ảnh chụp", FixedNow.AddSeconds(2), "photo"));
            sync.Emit(new SyncHandled("s-del", "SPX-T019-009", "success", "PC đã xóa đơn SPX-T019-009", FixedNow.AddSeconds(3), "delete"));
            sync.Emit(new SyncHandled("s-edit", "SPX-T019-001", "success", "PC đã cập nhật đơn SPX-T019-001", FixedNow.AddSeconds(4), "edit"));
            sync.Emit(new SyncHandled("s-note", "TT-T019-002", "success", "PC đã lưu ghi chú và đưa đơn TT-T019-002 vào bảng theo dõi", FixedNow.AddSeconds(5), "note"));
            sync.Emit(new SyncHandled("s-dup", "SPX-T019-001", "duplicate", "ĐƠN ĐÃ CÓ Ở BẢNG CHÍNH, KHÔNG TẠO LẠI", FixedNow.AddSeconds(6), "scan"));
            sync.Emit(new SyncHandled("s-new", "SPXVN069876543210", "success", "ĐÃ TẠO ĐƠN MỚI Ở BẢNG CHÍNH (Shopee)", FixedNow.AddSeconds(7), "scan", "new_order"));

            // 1. Chụp màn Đồng bộ điện thoại
            mainVm.NavigateByKey("phone_sync");
            var window = new MainWindow { DataContext = mainVm, Width = 1360, Height = 820 };
            window.Show();
            WpfTestHost.PumpDispatcher(window);
            WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "01-phone-sync-logs.png"));

            // 2. Chụp Bảng chính Shopee với dòng đơn mới + toast đơn mới
            mainVm.NavigateByKey("shopee_return");
            WpfTestHost.PumpDispatcher(window);
            WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "02-main-table-new-order-toast.png"));
            window.Close();
        });

        Assert.True(File.Exists(Path.Combine(outDir, "01-phone-sync-logs.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "02-main-table-new-order-toast.png")));
    }
}
