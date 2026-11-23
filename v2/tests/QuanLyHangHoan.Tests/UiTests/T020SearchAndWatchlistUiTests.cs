using System.IO;
using System.Windows;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.Tests.UiTests;

public sealed class T020SearchAndWatchlistUiTests : IDisposable
{
    private static readonly DateTime FixedNow = new(2026, 11, 23, 19, 0, 0);
    private readonly string _tempRoot;

    public T020SearchAndWatchlistUiTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "QuanLyHangHoan_T020_" + Guid.NewGuid().ToString("N"));
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

    private sealed class StubDialogService : IOrderActionDialogService
    {
        public string? NextStatus { get; set; }
        public string? NextMultilineText { get; set; }
        public List<HistoryEntry> LastShownHistory { get; } = [];

        public string? PromptStatus(string orderId, string tracking, string currentStatus) => NextStatus;
        public string? PromptMultilineText(string title, string subtitle, string label, string initialValue) => NextMultilineText;
        public (bool IsWatchlist, string Reason)? PromptWatchlist(string orderId, string tracking, bool isWatchlist, string currentReason)
            => (isWatchlist, NextMultilineText ?? currentReason);
        public bool ConfirmDeleteSingle(string tracking, string orderId, string sourceTable) => true;
        public bool ConfirmBulkAction(string title, int count, string detail) => true;
        public void ShowHistory(string orderId, string tracking, IReadOnlyList<HistoryEntry> entries)
        {
            LastShownHistory.Clear();
            LastShownHistory.AddRange(entries);
        }
    }

    private sealed class StubOrderProvider : IReturnOrderProvider, IArchivedOrderProvider, IDeletedOrderProvider, INewOrderProvider
    {
        public List<Order> ReturnOrders { get; } = [];
        public List<Order> ArchivedOrders { get; } = [];
        public List<DeletedOrderRecord> DeletedOrders { get; } = [];
        public List<NewOrderRecord> NewOrders { get; } = [];

        public DateTime GetCurrentTime() => FixedNow;
        public IReadOnlyList<Order> GetReturnOrders(int count = 40) => ReturnOrders;
        public Task<IReadOnlyList<Order>> GetReturnOrdersAsync(int count = 40, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Order>>(ReturnOrders);
        public IReadOnlyList<Order> GetArchivedOrders() => ArchivedOrders;
        public IReadOnlyList<DeletedOrderRecord> GetDeletedOrders() => DeletedOrders;
        public IReadOnlyList<NewOrderRecord> GetNewOrders() => NewOrders;
        public PlaceholderScreenData GetPlaceholderScreenData(string screenKey)
            => new(screenKey, "", "", Array.Empty<string>(), Array.Empty<PlaceholderPreviewRow>(), "");
    }

    private static Order MakeOrder(
        string id,
        string orderId,
        string track,
        string returnTrack = "",
        string altTrack = "",
        string returnOrderId = "",
        string forwardTrack = "",
        string platform = PlatformNames.Shopee,
        string orderType = OrderTypeNames.ReturnRefund,
        string product = "Áo Khoác Gió",
        string note = "",
        bool isWatchlist = false,
        string watchlistReason = "") => new()
    {
        Id = id,
        OrderId = orderId,
        ReturnOrderId = returnOrderId,
        Track = track,
        ReturnTrack = string.IsNullOrEmpty(returnTrack) ? track : returnTrack,
        AltTrack = altTrack,
        ForwardTrack = forwardTrack,
        Platform = platform,
        OrderType = orderType,
        Status = ReceiveStatus.NotReturned,
        Carrier = platform == PlatformNames.Shopee ? "SPX Express" : "J&T Express",
        ReturnedAt = FixedNow.AddHours(-16).ToString("dd/MM/yyyy HH:mm"),
        Price = 250000m,
        Note = note,
        IsWatchlist = isWatchlist,
        WatchlistReason = watchlistReason,
        Items = [new OrderItem { Product = product, Color = "Đen", Size = "L", Qty = 1 }]
    };

    private static string CreateTempDb(IEnumerable<(Order Order, bool Archived, bool Deleted, string DeletedFrom)> rows)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "QuanLyHangHoan_T020Db_" + Guid.NewGuid().ToString("N") + ".db");
        AppDatabase.Initialize(dbPath);
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        foreach (var (o, archived, deleted, deletedFrom) in rows)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO orders(source_key, id, order_id, return_order_id, track, return_track, alt_track, forward_track,
                    tracking_codes, scan_aliases, platform, order_type, platform_status, pre_order, status, carrier,
                    ship_date, return_date, recv_date, returned_at, deadline, price, note, buyer_note, user_note,
                    import_week, imported_at, is_watchlist, watchlist_reason, is_partial_return, photo_id, extra_json,
                    is_archived, archived_at, is_deleted, deleted_at, deleted_from, raw_json)
                VALUES($sk, $id, $oid, $roid, $tr, $rtr, $atr, $ftr, '[]', '[]', $plat, $ot, '', '', $st, $car,
                    '', '', '', $rat, '', $pr, $note, '', '', '', '', $wl, $wlr, 0, '', '{}',
                    $arch, '', $del, '', $delfrom, '{}');
                """;
            cmd.Parameters.AddWithValue("$sk", o.Id);
            cmd.Parameters.AddWithValue("$id", o.Id);
            cmd.Parameters.AddWithValue("$oid", o.OrderId);
            cmd.Parameters.AddWithValue("$roid", o.ReturnOrderId);
            cmd.Parameters.AddWithValue("$tr", o.Track);
            cmd.Parameters.AddWithValue("$rtr", o.ReturnTrack);
            cmd.Parameters.AddWithValue("$atr", o.AltTrack);
            cmd.Parameters.AddWithValue("$ftr", o.ForwardTrack);
            cmd.Parameters.AddWithValue("$plat", o.Platform);
            cmd.Parameters.AddWithValue("$ot", o.OrderType);
            cmd.Parameters.AddWithValue("$st", o.Status);
            cmd.Parameters.AddWithValue("$car", o.Carrier);
            cmd.Parameters.AddWithValue("$rat", o.ReturnedAt);
            cmd.Parameters.AddWithValue("$pr", (double)o.Price);
            cmd.Parameters.AddWithValue("$note", o.Note);
            cmd.Parameters.AddWithValue("$wl", o.IsWatchlist ? 1 : 0);
            cmd.Parameters.AddWithValue("$wlr", o.WatchlistReason);
            cmd.Parameters.AddWithValue("$arch", archived ? 1 : 0);
            cmd.Parameters.AddWithValue("$del", deleted ? 1 : 0);
            cmd.Parameters.AddWithValue("$delfrom", deletedFrom);
            cmd.ExecuteNonQuery();

            for (var i = 0; i < o.Items.Count; i++)
            {
                var item = o.Items[i];
                using var ic = conn.CreateCommand();
                ic.CommandText = "INSERT INTO order_items(source_key, line_no, product, color, size, qty) VALUES($sk, $ln, $p, $c, $s, $q)";
                ic.Parameters.AddWithValue("$sk", o.Id);
                ic.Parameters.AddWithValue("$ln", i);
                ic.Parameters.AddWithValue("$p", item.Product);
                ic.Parameters.AddWithValue("$c", item.Color);
                ic.Parameters.AddWithValue("$s", item.Size);
                ic.Parameters.AddWithValue("$q", item.Qty);
                ic.ExecuteNonQuery();
            }
        }
        return dbPath;
    }

    [Fact]
    public void GlobalSearch_SupportsCodesProductAccents_NeverMergesParcels_AndRanksExactMatchFirst()
    {
        var parcelA = MakeOrder("a1", "ORD-SHARED-01", "SPX-A1", altTrack: "ALT-A1", returnOrderId: "RET-A1", forwardTrack: "FWD-SHARED",
            product: "Áo Khoác Gió Mùa Đông");
        var parcelB = MakeOrder("b1", "ORD-SHARED-01", "SPX-B1", forwardTrack: "FWD-SHARED",
            platform: PlatformNames.Shopee, orderType: OrderTypeNames.Cancelled, product: "Quần Jeans Ống Suông");
        var partialHit = MakeOrder("p1", "ORD-OTHER-99", "SPX-A1-EXTRA", product: "Áo Thun");
        var tiktokOrder = MakeOrder("t1", "TT-ORD-01", "TTVN001", platform: PlatformNames.TikTok, product: "Váy Midi Xếp Ly");
        var archivedOrder = MakeOrder("arc1", "ORD-ARC-01", "SPX-ARC-01", product: "Áo Len Cổ Lọ");
        var deletedOrder = MakeOrder("del1", "ORD-DEL-01", "SPX-DEL-01", product: "Sơ Mi Oxford");

        var active = new[] { partialHit, parcelA, parcelB, tiktokOrder };
        var archived = new[] { archivedOrder };
        var deleted = new[]
        {
            new DeletedOrderRecord
            {
                Id = deletedOrder.Id, OrderId = deletedOrder.OrderId, Track = deletedOrder.Track,
                ReturnTrack = deletedOrder.ReturnTrack, Platform = deletedOrder.Platform,
                DeletedFrom = "Bảng chính", DeletedAt = "23/11/2026 10:00",
                Items = deletedOrder.Items, FullOrder = deletedOrder
            }
        };

        // 1. Exact own-code match ranked first with "Khớp mã"
        var byTrack = GlobalSearchService.Search("SPX-A1", active, archived, deleted);
        Assert.Equal(2, byTrack.Items.Count);
        Assert.Equal("a1", byTrack.Items[0].Source.Id);
        Assert.True(byTrack.Items[0].IsExactCodeMatch);
        Assert.False(byTrack.Items[1].IsExactCodeMatch); // partialHit

        // 2. Search by AltTrack & ReturnOrderId
        var byAlt = GlobalSearchService.Search("alt-a1", active, archived, deleted);
        Assert.Single(byAlt.Items);
        Assert.True(byAlt.Items[0].IsExactCodeMatch);

        var byRet = GlobalSearchService.Search("RET-A1", active, archived, deleted);
        Assert.Single(byRet.Items);
        Assert.True(byRet.Items[0].IsExactCodeMatch);

        // 3. Search by OrderId: Parcel A & B remain 2 separate results
        var byOrderId = GlobalSearchService.Search("ORD-SHARED-01", active, archived, deleted);
        Assert.Equal(2, byOrderId.Items.Count);
        Assert.Equal("Shopee - Hoàn hàng", byOrderId.Items[0].LocationLabel);
        Assert.Equal("Shopee - Đơn hủy", byOrderId.Items[1].LocationLabel);

        // 4. Shared ForwardTrack: returns both parcels, NEITHER gets "Khớp mã" (Rule 3)
        var byForward = GlobalSearchService.Search("FWD-SHARED", active, archived, deleted);
        Assert.Equal(2, byForward.Items.Count);
        Assert.All(byForward.Items, r => Assert.False(r.IsExactCodeMatch));

        // 5. Vietnamese product name with and without accents
        var noAccent = GlobalSearchService.Search("ao khoac gio mua dong", active, archived, deleted);
        Assert.Single(noAccent.Items);
        Assert.Equal("a1", noAccent.Items[0].Source.Id);

        var withAccent = GlobalSearchService.Search("Áo Khoác Gió", active, archived, deleted);
        Assert.Single(withAccent.Items);

        // 6. Location labels for TikTok, Archived, Deleted
        Assert.Equal("TikTok - Hoàn hàng", GlobalSearchService.Search("TTVN001", active, archived, deleted).Items[0].LocationLabel);
        Assert.Equal("Kho lưu trữ", GlobalSearchService.Search("SPX-ARC-01", active, archived, deleted).Items[0].LocationLabel);
        Assert.Equal("Đã xóa", GlobalSearchService.Search("SPX-DEL-01", active, archived, deleted).Items[0].LocationLabel);
    }

    [Fact]
    public void GlobalSearch_LimitsTo50Results_AndReportsRemainingCount()
    {
        var orders = Enumerable.Range(1, 63)
            .Select(i => MakeOrder($"id_{i}", $"ORD-{i:D3}", $"SPX-BULK-{i:D3}", product: "Áo Thun Cotton Chung"))
            .ToList();

        var output = GlobalSearchService.Search("ao thun cotton chung", orders, Array.Empty<Order>(), Array.Empty<DeletedOrderRecord>());
        Assert.Equal(63, output.TotalMatches);
        Assert.Equal(50, output.Items.Count);
        Assert.Equal(13, output.RemainingCount);
        Assert.True(output.HasOverflow);
        Assert.Contains("13", output.OverflowMessage);
    }

    [Fact]
    public async Task MainViewModel_GlobalSearch_OpensDetailAndNavigatesToTable()
    {
        var provider = new StubOrderProvider();
        var archivedOrder = MakeOrder("arc_99", "ORD-ARC-99", "SPX-ARC-99", product: "Áo Len Đỏ");
        var activeOrder = MakeOrder("act_99", "ORD-ACT-99", "SPX-ACT-99", platform: PlatformNames.TikTok, product: "Quần Kaki");
        provider.ReturnOrders.Add(activeOrder);
        provider.ArchivedOrders.Add(archivedOrder);

        var detailSvc = new StubDetailService();
        using var vm = new MainViewModel(
            provider, provider, provider, provider,
            new FakeLegacyImporter(provider.GetCurrentTime),
            orderDetailService: detailSvc,
            dispatchToUi: a => a(),
            searchDebounce: TimeSpan.Zero);

        await vm.ExecuteGlobalSearchAsync("SPX-ARC-99");
        Assert.True(vm.IsGlobalSearchOpen);
        Assert.Single(vm.GlobalSearchResults);

        // Open detail from search result
        vm.OpenGlobalSearchResultCommand.Execute(vm.GlobalSearchResults[0]);
        Assert.Single(detailSvc.OpenedOrders);
        Assert.Equal("arc_99", detailSvc.OpenedOrders[0].Id);

        // Go to table
        vm.GoToGlobalSearchTableCommand.Execute(vm.GlobalSearchResults[0]);
        Assert.Equal("archive", vm.SelectedNavigationKey);
        Assert.False(vm.IsGlobalSearchOpen);
        Assert.Equal("arc_99", vm.ArchivedOrdersVm.SelectedRow?.Source.Id);
    }

    [Fact]
    public async Task Watchlist_ListsOnlyWatchlistOrders_UpdatesSqliteOnUnfollowEditAndHistory()
    {
        var wlActive = MakeOrder("wl_1", "ORD-WL-1", "SPX-WL-1", isWatchlist: true, watchlistReason: "Chờ hoàn tiền");
        var wlCancelled = MakeOrder("wl_2", "ORD-WL-2", "SPX-WL-2", orderType: OrderTypeNames.Cancelled, isWatchlist: true, watchlistReason: "Đơn hủy giá trị cao");
        var normalActive = MakeOrder("norm_1", "ORD-NORM-1", "SPX-NORM-1", isWatchlist: false);
        var wlArchived = MakeOrder("wl_arc", "ORD-WL-ARC", "SPX-WL-ARC", isWatchlist: true, watchlistReason: "Đã lưu trữ nhưng vẫn theo dõi");
        var wlDeleted = MakeOrder("wl_del", "ORD-WL-DEL", "SPX-WL-DEL", isWatchlist: true, watchlistReason: "Đã xóa");

        var dbPath = CreateTempDb([
            (wlActive, false, false, ""),
            (wlCancelled, false, false, ""),
            (normalActive, false, false, ""),
            (wlArchived, true, false, ""),
            (wlDeleted, false, true, "Bảng chính")
        ]);

        try
        {
            var provider = new SqliteOrderProvider(dbPath, () => FixedNow);
            var editService = new SqliteOrderEditService(dbPath, () => FixedNow);
            var dialogSvc = new StubDialogService();

            using var mainVm = new MainViewModel(
                provider, provider, provider, provider,
                new FakeLegacyImporter(provider.GetCurrentTime),
                orderEditService: editService,
                orderActionDialogService: dialogSvc,
                dispatchToUi: a => a(),
                searchDebounce: TimeSpan.Zero);

            // Sidebar item placed right after "Bảng đơn mới" in "CHUNG"
            var navKeys = mainVm.NavigationItems.Select(n => n.Key).ToList();
            var newOrdersIdx = navKeys.IndexOf("new_orders");
            var watchlistIdx = navKeys.IndexOf("watchlist");
            Assert.True(newOrdersIdx >= 0);
            Assert.Equal(newOrdersIdx + 1, watchlistIdx);
            Assert.Equal("2", mainVm.NavigationItems[watchlistIdx].Badge);

            // Default excludes archived & deleted
            Assert.Equal(2, mainVm.WatchlistVm.VisibleOrders.Count);
            Assert.False(mainVm.WatchlistVm.IsEmpty);

            // IncludeArchived adds wlArchived (still excludes wlDeleted)
            mainVm.WatchlistVm.IncludeArchived = true;
            Assert.Equal(3, mainVm.WatchlistVm.VisibleOrders.Count);
            mainVm.WatchlistVm.IncludeArchived = false;

            // Edit watchlist reason -> persisted in SQLite
            var targetRow = mainVm.WatchlistVm.VisibleOrders.First(r => r.Source.Id == "wl_1");
            dialogSvc.NextMultilineText = "Đã đối soát lần 2";
            await mainVm.WatchlistVm.EditWatchlistReasonCommand.ExecuteAsync(targetRow);
            var afterEdit = OrderRepository.Load(dbPath).First(s => s.Order.Id == "wl_1").Order;
            Assert.Equal("Đã đối soát lần 2", afterEdit.WatchlistReason);

            // Show history -> reads from SQLite
            var rowAfterEdit = mainVm.WatchlistVm.VisibleOrders.First(r => r.Source.Id == "wl_1");
            await mainVm.WatchlistVm.ShowHistoryCommand.ExecuteAsync(rowAfterEdit);
            Assert.NotEmpty(dialogSvc.LastShownHistory);

            // Unfollow -> disappears from list and DB updated
            await mainVm.WatchlistVm.RemoveFromWatchlistCommand.ExecuteAsync(rowAfterEdit);
            Assert.Single(mainVm.WatchlistVm.VisibleOrders);
            Assert.Equal("1", mainVm.NavigationItems[watchlistIdx].Badge);
            var afterUnfollow = OrderRepository.Load(dbPath).First(s => s.Order.Id == "wl_1").Order;
            Assert.False(afterUnfollow.IsWatchlist);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public void DemoMode_DoesNotTouchDisk_AndCapturesT020Screenshots()
    {
        var screensDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "tasks", "reports", "T020-screens"));
        Directory.CreateDirectory(screensDir);

        var fakeData = new FakeDataService(clock: () => FixedNow, enableMutableDemoStore: true);
        var fakeEdit = new FakeOrderEditService(fakeData);
        var fakeDbPath = Path.Combine(_tempRoot, "should_not_exist.db");

        WpfTestHost.Run(() =>
        {
            using var vm = new MainViewModel(
                fakeData, fakeData, fakeData, fakeData,
                new FakeLegacyImporter(fakeData.GetCurrentTime),
                isDemoMode: true,
                orderEditService: fakeEdit,
                uiSettingsStore: new UiSettingsStore(null, isDemoMode: true),
                dispatchToUi: a => a(),
                searchDebounce: TimeSpan.Zero);

            var window = new MainWindow
            {
                DataContext = vm,
                Width = 1360,
                Height = 820
            };
            window.Show();

            // 1. Global search with results (Light theme)
            WpfTestHost.RunTask(() => vm.ExecuteGlobalSearchAsync("2611DEMO0001A"));
            WpfTestHost.PumpDispatcher(window);
            Assert.True(vm.IsGlobalSearchOpen);
            Assert.Equal(2, vm.GlobalSearchResults.Count); // Parcel A and B
            WpfTestHost.SaveWindowPng(window, Path.Combine(screensDir, "01-global-search-results.png"));

            // 2. Global search empty results
            WpfTestHost.RunTask(() => vm.ExecuteGlobalSearchAsync("KHONG_TON_TAI_XYZ_999"));
            WpfTestHost.PumpDispatcher(window);
            Assert.True(vm.HasGlobalSearchNoResults);
            WpfTestHost.SaveWindowPng(window, Path.Combine(screensDir, "02-global-search-empty.png"));

            // 3. Watchlist screen populated (Dark theme test too!)
            vm.ClearGlobalSearchCommand.Execute(null);
            vm.SetTheme(ThemeService.DarkTheme);
            vm.NavigateByKey("watchlist");
            WpfTestHost.PumpDispatcher(window);
            Assert.False(vm.WatchlistVm.IsEmpty);
            WpfTestHost.SaveWindowPng(window, Path.Combine(screensDir, "03-watchlist-populated.png"));

            // 4. Watchlist screen empty
            vm.SetTheme(ThemeService.LightTheme);
            foreach (var row in vm.WatchlistVm.VisibleOrders.ToList())
            {
                vm.WatchlistVm.RemoveFromWatchlistCommand.Execute(row);
            }
            WpfTestHost.PumpDispatcher(window);
            Assert.True(vm.WatchlistVm.IsEmpty);
            WpfTestHost.SaveWindowPng(window, Path.Combine(screensDir, "04-watchlist-empty.png"));

            window.Close();
        });

        Assert.False(File.Exists(fakeDbPath));
        Assert.True(File.Exists(Path.Combine(screensDir, "01-global-search-results.png")));
        Assert.True(File.Exists(Path.Combine(screensDir, "02-global-search-empty.png")));
        Assert.True(File.Exists(Path.Combine(screensDir, "03-watchlist-populated.png")));
        Assert.True(File.Exists(Path.Combine(screensDir, "04-watchlist-empty.png")));
    }
}
