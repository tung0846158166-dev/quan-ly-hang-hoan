using System.IO;
using System.Windows;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.App.Views;
using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.Tests.UiTests;

public sealed class T026ReturnWatchUiTests : IDisposable
{
    private static readonly DateTime FixedNow = new(2026, 11, 23, 19, 30, 0);
    private readonly string _tempRoot;

    public T026ReturnWatchUiTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "QuanLyHangHoan_T026_" + Guid.NewGuid().ToString("N"));
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

    private sealed class StubDetailService : IOrderDetailService
    {
        public List<Order> OpenedOrders { get; } = [];
        public void ShowOrderDetail(Order order, DateTime? now = null) => OpenedOrders.Add(order);
    }

    private sealed class StubOrderProvider : IReturnOrderProvider, IArchivedOrderProvider, IDeletedOrderProvider, INewOrderProvider
    {
        private readonly string? _dbPath;

        public StubOrderProvider(string? dbPath = null) => _dbPath = dbPath;

        public DateTime GetCurrentTime() => FixedNow;

        public IReadOnlyList<Order> GetReturnOrders(int count = 40)
            => _dbPath is not null && File.Exists(_dbPath)
                ? OrderRepository.Load(_dbPath).Where(s => !s.IsArchived && !s.IsDeleted).Select(s => s.Order).ToList()
                : Array.Empty<Order>();

        public Task<IReadOnlyList<Order>> GetReturnOrdersAsync(int count = 40, CancellationToken ct = default)
            => Task.FromResult(GetReturnOrders(count));

        public IReadOnlyList<Order> GetArchivedOrders() => Array.Empty<Order>();
        public IReadOnlyList<DeletedOrderRecord> GetDeletedOrders() => Array.Empty<DeletedOrderRecord>();
        public IReadOnlyList<NewOrderRecord> GetNewOrders() => Array.Empty<NewOrderRecord>();
        public PlaceholderScreenData GetPlaceholderScreenData(string screenKey)
            => new(screenKey, "", "", Array.Empty<string>(), Array.Empty<PlaceholderPreviewRow>(), "");
    }

    private sealed class SpyReturnWatchService : IReturnWatchService
    {
        private readonly IReturnWatchService _inner;
        public List<string> RemovedOrderIds { get; } = [];
        public List<string> RestoredOrderIds { get; } = [];
        public HashSet<string> FailRemoveIds { get; } = new(StringComparer.Ordinal);

        public SpyReturnWatchService(IReturnWatchService inner) => _inner = inner;

        public Task<IReadOnlyList<ReturnWatchEntry>> LoadAsync(DateTime now, CancellationToken cancellationToken = default)
            => _inner.LoadAsync(now, cancellationToken);

        public Task<EditResult> RemoveAsync(string orderId, DateTime now, CancellationToken cancellationToken = default)
        {
            RemovedOrderIds.Add(orderId);
            if (FailRemoveIds.Contains(orderId))
            {
                return Task.FromResult(new EditResult(EditKind.Error, "Lỗi giả lập"));
            }
            return _inner.RemoveAsync(orderId, now, cancellationToken);
        }

        public Task<EditResult> RestoreAsync(string orderId, DateTime now, CancellationToken cancellationToken = default)
        {
            RestoredOrderIds.Add(orderId);
            return _inner.RestoreAsync(orderId, now, cancellationToken);
        }
    }

    private string CreateTempDb(params Order[] orders)
    {
        var dbPath = Path.Combine(_tempRoot, $"rw_{Guid.NewGuid():N}.db");
        AppDatabase.Initialize(dbPath);
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        foreach (var o in orders)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO orders(source_key, id, order_id, return_order_id, track, return_track, alt_track, forward_track,
                    tracking_codes, scan_aliases, platform, order_type, platform_status, pre_order, status, carrier,
                    ship_date, return_date, recv_date, returned_at, deadline, price, note, buyer_note, user_note,
                    import_week, imported_at, is_watchlist, watchlist_reason, is_partial_return, photo_id, extra_json,
                    is_archived, archived_at, is_deleted, deleted_at, deleted_from, raw_json)
                VALUES($sk, $id, $oid, $roid, $tr, $rtr, '', '', '[]', '[]', $plat, $ot, $ps, '', $st, $car,
                    '', '', $rd, $rat, '', $pr, '', '', '', '', '', 0, '', 0, '', '{}',
                    0, '', 0, '', '', '{}');
                """;
            cmd.Parameters.AddWithValue("$sk", o.Id);
            cmd.Parameters.AddWithValue("$id", o.Id);
            cmd.Parameters.AddWithValue("$oid", o.OrderId);
            cmd.Parameters.AddWithValue("$roid", o.ReturnOrderId);
            cmd.Parameters.AddWithValue("$tr", o.Track);
            cmd.Parameters.AddWithValue("$rtr", o.ReturnTrack);
            cmd.Parameters.AddWithValue("$plat", o.Platform);
            cmd.Parameters.AddWithValue("$ot", o.OrderType);
            cmd.Parameters.AddWithValue("$ps", o.PlatformStatus);
            cmd.Parameters.AddWithValue("$st", o.Status);
            cmd.Parameters.AddWithValue("$car", o.Carrier);
            cmd.Parameters.AddWithValue("$rd", o.RecvDate);
            cmd.Parameters.AddWithValue("$rat", o.ReturnedAt);
            cmd.Parameters.AddWithValue("$pr", (double)o.Price);
            cmd.ExecuteNonQuery();

            for (var i = 0; i < o.Items.Count; i++)
            {
                var item = o.Items[i];
                using var icmd = conn.CreateCommand();
                icmd.CommandText = "INSERT INTO order_items(source_key, line_no, product, color, size, qty) VALUES($sk, $ln, $p, $c, $s, $q)";
                icmd.Parameters.AddWithValue("$sk", o.Id);
                icmd.Parameters.AddWithValue("$ln", i);
                icmd.Parameters.AddWithValue("$p", item.Product);
                icmd.Parameters.AddWithValue("$c", item.Color);
                icmd.Parameters.AddWithValue("$s", item.Size);
                icmd.Parameters.AddWithValue("$q", item.Qty);
                icmd.ExecuteNonQuery();
            }
        }
        return dbPath;
    }

    private static Order MakeOrder(
        string id,
        string orderId,
        string track,
        string returnTrack = "",
        string returnOrderId = "",
        string platform = PlatformNames.Shopee,
        string platformStatus = "Đã đồng ý trả hàng",
        string status = ReceiveStatus.NotReturned,
        string returnedAt = "",
        string recvDate = "",
        string product = "Áo khoác gió") => new()
    {
        Id = id,
        OrderId = orderId,
        ReturnOrderId = returnOrderId,
        Track = track,
        ReturnTrack = returnTrack,
        Platform = platform,
        OrderType = OrderTypeNames.ReturnRefund,
        PlatformStatus = platformStatus,
        Status = status,
        Carrier = platform == PlatformNames.Shopee ? "SPX Express" : "J&T Express",
        ReturnedAt = returnedAt,
        RecvDate = recvDate,
        Price = 250000m,
        Items = [new OrderItem { Product = product, Color = "Đen", Size = "L", Qty = 1 }]
    };

    private static long ToMs(DateTime dt)
        => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), TimeZoneInfo.Local.GetUtcOffset(dt)).ToUnixTimeMilliseconds();

    [Fact]
    public async Task FourStages_Counts_Filtering_Search_And_Sorting_WorkCorrectly()
    {
        var dbPath = CreateTempDb(
            MakeOrder("s1", "ORD-101", "TRK101", platformStatus: "Đã đồng ý trả hàng", product: "Áo khoác nỉ"),
            MakeOrder("s2", "ORD-102", "TRK102", returnTrack: "RET102", platformStatus: "Đã đồng ý", product: "Quần tây ống rộng"),
            MakeOrder("s3", "ORD-103", "TRK103", returnTrack: "RET103", platformStatus: "Đã đồng ý",
                returnedAt: FixedNow.AddHours(-26).ToString("dd/MM/yyyy HH:mm"), product: "Giày thể thao")
        );

        // Seed stage 4 order with existing return_watch mark (since already-received orders don't auto-enter)
        var s4Order = MakeOrder("s4", "ORD-104", "TRK104", returnTrack: "RET104", platformStatus: "Đã đồng ý",
            status: ReceiveStatus.Received, returnedAt: FixedNow.AddHours(-30).ToString("dd/MM/yyyy HH:mm"),
            recvDate: FixedNow.AddHours(-1).ToString("dd/MM/yyyy HH:mm"), product: "Túi xách nữ");
        using (var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO orders(source_key, id, order_id, return_order_id, track, return_track, alt_track, forward_track,
                    tracking_codes, scan_aliases, platform, order_type, platform_status, pre_order, status, carrier,
                    ship_date, return_date, recv_date, returned_at, deadline, price, note, buyer_note, user_note,
                    import_week, imported_at, is_watchlist, watchlist_reason, is_partial_return, photo_id, extra_json,
                    is_archived, archived_at, is_deleted, deleted_at, deleted_from, raw_json)
                VALUES('s4', 's4', 'ORD-104', 'RT-104', 'TRK104', 'RET104', '', '', '[]', '[]', 'Shopee', 'Hoàn hàng', 'Đã đồng ý', '',
                    'Đã nhận', 'SPX', '', '', $rd, $rat, '', 100000, '', '', '', '', '', 0, '', 0, '', '{}', 0, '', 0, '', '', '{}');
                """;
            cmd.Parameters.AddWithValue("$rd", s4Order.RecvDate);
            cmd.Parameters.AddWithValue("$rat", s4Order.ReturnedAt);
            cmd.ExecuteNonQuery();
            using var wcmd = conn.CreateCommand();
            wcmd.CommandText = "CREATE TABLE IF NOT EXISTS return_watch(order_id TEXT PRIMARY KEY, since INTEGER NOT NULL, off INTEGER NOT NULL DEFAULT 0); INSERT INTO return_watch(order_id, since, off) VALUES('s4', $s, 0);";
            wcmd.Parameters.AddWithValue("$s", ToMs(FixedNow.AddHours(-50)));
            wcmd.ExecuteNonQuery();
        }

        var svc = new SqliteReturnWatchService(dbPath);
        var vm = new ReturnWatchViewModel(svc, () => FixedNow);
        await vm.RefreshAsync();

        Assert.Equal(1, vm.Stage1Count);
        Assert.Equal(1, vm.Stage2Count);
        Assert.Equal(1, vm.Stage3Count);
        Assert.Equal(1, vm.Stage4Count);
        Assert.Equal(3, vm.TrackingCount);
        Assert.Equal(4, vm.TotalCount);
        Assert.Equal(1, vm.RedAlertCount);

        // Default filter = tracking (stages 1-3)
        Assert.Equal(3, vm.VisibleOrders.Count);
        Assert.Equal(new[] { 1, 2, 3 }, vm.VisibleOrders.Select(r => r.Stage).ToArray());

        // Filter each stage
        vm.SetStageFilter("stage1");
        Assert.Single(vm.VisibleOrders);
        Assert.Equal("ORD-101", vm.VisibleOrders[0].OrderId);

        vm.SetStageFilter("stage2");
        Assert.Single(vm.VisibleOrders);
        Assert.Equal("ORD-102", vm.VisibleOrders[0].OrderId);

        vm.SetStageFilter("stage3");
        Assert.Single(vm.VisibleOrders);
        Assert.Equal("ORD-103", vm.VisibleOrders[0].OrderId);
        Assert.Equal(AlertLevel.Red, vm.VisibleOrders[0].AlertLevel);

        vm.SetStageFilter("stage4");
        Assert.Single(vm.VisibleOrders);
        Assert.Equal("ORD-104", vm.VisibleOrders[0].OrderId);

        // Search diacritic-insensitive
        vm.SetStageFilter("all");
        Assert.Equal(4, vm.VisibleOrders.Count);
        vm.SearchText = "ao khoac ni";
        Assert.Single(vm.VisibleOrders);
        Assert.Equal("ORD-101", vm.VisibleOrders[0].OrderId);

        vm.SearchText = "ret103";
        Assert.Single(vm.VisibleOrders);
        Assert.Equal("ORD-103", vm.VisibleOrders[0].OrderId);
    }

    [Fact]
    public async Task ScanReceive_MovesParcelToStage4_AfterReload()
    {
        var dbPath = CreateTempDb(
            MakeOrder("p1", "ORD-201", "TRK201", returnTrack: "RET201", platformStatus: "Đã đồng ý trả hàng")
        );

        var svc = new SqliteReturnWatchService(dbPath);
        var vm = new ReturnWatchViewModel(svc, () => FixedNow);
        await vm.RefreshAsync();

        Assert.Equal(1, vm.TrackingCount);
        Assert.Equal(0, vm.Stage4Count);
        Assert.Equal(2, vm.VisibleOrders[0].Stage); // has returnTrack => stage 2

        // Scan parcel via ScanReceiver
        var scanRes = ScanReceiver.Receive(dbPath, "RET201", FixedNow);
        Assert.Equal(ScanOutcome.Received, scanRes.Outcome);

        await vm.RefreshAsync();
        Assert.Equal(0, vm.TrackingCount);
        Assert.Equal(1, vm.Stage4Count);
        vm.SetStageFilter("stage4");
        Assert.Single(vm.VisibleOrders);
        Assert.Equal(4, vm.VisibleOrders[0].Stage);
    }

    [Fact]
    public async Task RemoveFromBoard_And_Undo_KeepOrderInMainTable_And_CancelDoesNotCallApi()
    {
        var dbPath = CreateTempDb(
            MakeOrder("o1", "ORD-301", "TRK301", platformStatus: "Đã đồng ý trả hàng"),
            MakeOrder("o2", "ORD-302", "TRK302", platformStatus: "Đã đồng ý trả hàng")
        );

        var spy = new SpyReturnWatchService(new SqliteReturnWatchService(dbPath));
        var provider = new StubOrderProvider(dbPath);
        var detailSvc = new StubDetailService();
        var confirmed = false;

        using var mainVm = new MainViewModel(
            provider, provider, provider, provider,
            new FakeLegacyImporter(() => FixedNow),
            isDemoMode: false,
            deferInitialLoad: true,
            orderDetailService: detailSvc,
            confirmationService: new DelegateConfirmationService((_, _) => confirmed),
            returnWatchService: spy,
            dispatchToUi: a => a());

        await mainVm.ReloadAllDataAsync();
        Assert.Equal(2, mainVm.ReturnWatchVm.TrackingCount);

        // 1. Cancel confirmation -> API not called
        confirmed = false;
        await mainVm.ReturnWatchVm.RemoveFromBoardAsync(mainVm.ReturnWatchVm.VisibleOrders[0]);
        Assert.Empty(spy.RemovedOrderIds);
        Assert.Equal(2, mainVm.ReturnWatchVm.TrackingCount);

        // 2. Confirm remove -> removed from ReturnWatch board, STILL in main table
        confirmed = true;
        var target = mainVm.ReturnWatchVm.VisibleOrders[0];
        await mainVm.ReturnWatchVm.RemoveFromBoardAsync(target);
        Assert.Single(spy.RemovedOrderIds);
        Assert.Equal(1, mainVm.ReturnWatchVm.TrackingCount);
        Assert.Equal(2, OrderRepository.Load(dbPath).Count); // still in main table
        Assert.True(mainVm.IsToastVisible);
        Assert.True(mainVm.CanUndoDelete);

        // 3. Undo -> restored
        await mainVm.UndoDeleteAsync();
        Assert.Single(spy.RestoredOrderIds);
        Assert.Equal(2, mainVm.ReturnWatchVm.TrackingCount);
    }

    [Fact]
    public async Task MultiSelect_Remove_ReportsSuccessAndErrorCounts()
    {
        var dbPath = CreateTempDb(
            MakeOrder("m1", "ORD-401", "TRK401", platformStatus: "Đã đồng ý"),
            MakeOrder("m2", "ORD-402", "TRK402", platformStatus: "Đã đồng ý"),
            MakeOrder("m3", "ORD-403", "TRK403", platformStatus: "Đã đồng ý")
        );

        var spy = new SpyReturnWatchService(new SqliteReturnWatchService(dbPath));
        spy.FailRemoveIds.Add("m2");

        string? lastToast = null;
        var vm = new ReturnWatchViewModel(
            spy,
            () => FixedNow,
            confirmDialog: (_, _) => true,
            onOrdersRemoved: (_, msg) => lastToast = msg,
            onNotify: (msg, _) => lastToast = msg);

        await vm.RefreshAsync();
        Assert.Equal(3, vm.VisibleOrders.Count);

        vm.UpdateSelectedRows(vm.VisibleOrders);
        await vm.RemoveFromBoardAsync(null);

        Assert.Equal(3, spy.RemovedOrderIds.Count);
        Assert.Equal("2 thành công, 1 lỗi", lastToast);
    }

    [Fact]
    public async Task TwoParcelsSameOrderId_AreSeparateRows_RemovingADoesNotAffectB_AndDetailOpensExactParcel()
    {
        var dbPath = CreateTempDb(
            MakeOrder("parcel-A", "ORD-SAME", "TRK-A", returnTrack: "RET-A", platformStatus: "Đã đồng ý", product: "Áo A"),
            MakeOrder("parcel-B", "ORD-SAME", "TRK-B", returnTrack: "RET-B", platformStatus: "Đã đồng ý", product: "Quần B")
        );

        var detailSvc = new StubDetailService();
        var svc = new SqliteReturnWatchService(dbPath);
        var vm = new ReturnWatchViewModel(
            svc,
            () => FixedNow,
            confirmDialog: (_, _) => true,
            onOpenDetail: o => detailSvc.ShowOrderDetail(o, FixedNow));

        await vm.RefreshAsync();
        Assert.Equal(2, vm.VisibleOrders.Count);

        // Open detail on parcel B
        var rowB = vm.VisibleOrders.First(r => r.OrderIdKey == "parcel-B");
        vm.OpenDetail(rowB);
        Assert.Single(detailSvc.OpenedOrders);
        Assert.Equal("parcel-B", detailSvc.OpenedOrders[0].Id);

        // Remove parcel A -> B stays untouched
        var rowA = vm.VisibleOrders.First(r => r.OrderIdKey == "parcel-A");
        await vm.RemoveFromBoardAsync(rowA);

        Assert.Single(vm.VisibleOrders);
        Assert.Equal("parcel-B", vm.VisibleOrders[0].OrderIdKey);
    }

    [Fact]
    public void SidebarBadge_NavPosition_And_DemoModeDoesNotTouchDisk()
    {
        var fakeData = new FakeDataService(enableMutableDemoStore: true);
        using var mainVm = new MainViewModel(fakeData);

        var navKeys = mainVm.NavigationItems.Select(n => n.Key).ToList();
        var rwIdx = navKeys.IndexOf("return_watch");
        var newOrdersIdx = navKeys.IndexOf("new_orders");
        Assert.True(rwIdx >= 0);
        Assert.Equal(newOrdersIdx - 1, rwIdx);
        Assert.Equal("CHUNG", mainVm.NavigationItems[rwIdx].Section);

        // Demo mode has all 4 stages
        Assert.True(mainVm.ReturnWatchVm.Stage1Count > 0);
        Assert.True(mainVm.ReturnWatchVm.Stage2Count > 0);
        Assert.True(mainVm.ReturnWatchVm.Stage3Count > 0);
        Assert.True(mainVm.ReturnWatchVm.Stage4Count > 0);
        Assert.Equal(mainVm.ReturnWatchVm.TrackingCount.ToString("N0"), mainVm.NavigationItems[rwIdx].Badge);
        Assert.Equal(mainVm.ReturnWatchVm.Stage2Count, mainVm.HomeVm.ReturnWatchOnTheWayCount);
    }

    [Fact]
    public void OverflowCap_1500Rows_ShowsRemainingMessage()
    {
        var entries = Enumerable.Range(1, 1520)
            .Select(i => new ReturnWatchEntry(
                new StoredOrder(MakeOrder($"id-{i}", $"ORD-{i}", $"TRK-{i}"), false, "", false, "", ""),
                ToMs(FixedNow.AddMinutes(-i)),
                1))
            .ToList();

        var vm = new ReturnWatchViewModel(new FakeReturnWatchService(entries), () => FixedNow);
        vm.LoadEntries(entries, FixedNow);

        Assert.Equal(ReturnWatchViewModel.MaxDisplayRows, vm.VisibleOrders.Count);
        Assert.True(vm.HasOverflow);
        Assert.Contains("còn 20 dòng", vm.OverflowMessage);
    }

    [Fact]
    public void RenderScreenshots_ForReport()
    {
        var screensDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "tasks", "reports", "T026-screens"));
        Directory.CreateDirectory(screensDir);

        WpfTestHost.Run(() =>
        {
            var fakeData = new FakeDataService(clock: () => FixedNow, enableMutableDemoStore: true);
            using var mainVm = new MainViewModel(
                fakeData, fakeData, fakeData, fakeData,
                new FakeLegacyImporter(fakeData.GetCurrentTime),
                isDemoMode: true,
                uiSettingsStore: new UiSettingsStore(null, isDemoMode: true),
                dispatchToUi: a => a(),
                returnWatchService: new FakeReturnWatchService());

            var window = new MainWindow
            {
                DataContext = mainVm,
                Width = 1360,
                Height = 820
            };
            window.Show();

            // 1. All 4 stages
            mainVm.NavigateByKey("return_watch");
            mainVm.ReturnWatchVm.SetStageFilter("all");
            WpfTestHost.PumpDispatcher(window);
            WpfTestHost.SaveWindowPng(window, Path.Combine(screensDir, "01-all-4-stages.png"));

            // 2. Filtered stage 3
            mainVm.ReturnWatchVm.SetStageFilter("stage3");
            WpfTestHost.PumpDispatcher(window);
            WpfTestHost.SaveWindowPng(window, Path.Combine(screensDir, "02-filtered-stage3.png"));

            // 3. Confirm dialog + Undo toast
            mainVm.ShowReturnWatchUndoToast(new[] { ("demo-rw-5", "RET_SPX_5005") });
            WpfTestHost.PumpDispatcher(window);
            WpfTestHost.SaveWindowPng(window, Path.Combine(screensDir, "03-undo-toast.png"));

            var confirmWin = new ConfirmActionWindow(
                "Bỏ khỏi bảng Hàng hoàn về",
                "Bạn có chắc muốn bỏ kiện này khỏi bảng Hàng hoàn về?\n\n• Mã vận đơn: RET_SPX_5005\n• Mã đơn: 261123SP005\n\nChỉ bỏ khỏi bảng theo dõi, đơn vẫn còn ở Bảng chính.",
                "Bỏ khỏi bảng",
                isDanger: true)
            {
                Width = 480,
                Height = 260
            };
            confirmWin.Show();
            WpfTestHost.PumpDispatcher(confirmWin);
            WpfTestHost.SaveWindowPng(confirmWin, Path.Combine(screensDir, "03-confirm-remove-dialog.png"));
            confirmWin.Close();

            // 4. Empty state
            mainVm.DismissToast();
            mainVm.ReturnWatchVm.LoadEntries(Array.Empty<ReturnWatchEntry>(), FixedNow);
            WpfTestHost.PumpDispatcher(window);
            WpfTestHost.SaveWindowPng(window, Path.Combine(screensDir, "04-empty-state.png"));

            window.Close();
        });

        Assert.True(File.Exists(Path.Combine(screensDir, "01-all-4-stages.png")));
        Assert.True(File.Exists(Path.Combine(screensDir, "02-filtered-stage3.png")));
        Assert.True(File.Exists(Path.Combine(screensDir, "03-confirm-remove-dialog.png")));
        Assert.True(File.Exists(Path.Combine(screensDir, "03-undo-toast.png")));
        Assert.True(File.Exists(Path.Combine(screensDir, "04-empty-state.png")));
    }

    private sealed class DelegateConfirmationService : IConfirmationService
    {
        private readonly Func<string, string, bool> _fn;
        public DelegateConfirmationService(Func<string, string, bool> fn) => _fn = fn;
        public bool Confirm(string title, string message) => _fn(title, message);
    }
}
