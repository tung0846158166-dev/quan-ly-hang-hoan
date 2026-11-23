using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.App.Views;
using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Tests.UiTests;

public sealed class T018OrderDetailUiTests : IDisposable
{
    private static readonly DateTime FixedNow = new(2026, 11, 23, 18, 0, 0);
    private readonly string _tempRoot;

    public T018OrderDetailUiTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "QuanLyHangHoan_T018_" + Guid.NewGuid().ToString("N"));
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

    private sealed class StubClipboardService : IClipboardService
    {
        public List<string> CopiedTexts { get; } = [];
        public void SetText(string text) => CopiedTexts.Add(text);
    }

    private sealed class StubPhotoProvider : IPhotoProvider
    {
        private readonly Dictionary<string, PhotoData> _photos = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string id, byte[] bytes) => _photos[id] = new PhotoData(id, "image/png", bytes, "test", false);

        public Task<PhotoData?> GetPhotoAsync(string photoId, CancellationToken cancellationToken = default)
            => Task.FromResult(_photos.TryGetValue(photoId, out var data) ? data : null);

        public Task<IReadOnlyList<string>> GetMissingPhotoIdsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private sealed class StubPhotoViewerService : IPhotoViewerService
    {
        public List<(string PhotoId, string? OrderId, string? Tracking)> Calls { get; } = [];
        public void ShowPhoto(string photoId, string? orderId = null, string? tracking = null)
            => Calls.Add((photoId, orderId, tracking));
    }

    private sealed class StubDetailService : IOrderDetailService
    {
        public List<Order> OpenedOrders { get; } = [];
        public void ShowOrderDetail(Order order, DateTime? now = null) => OpenedOrders.Add(order);
    }

    private sealed class StubOrderEditService : IOrderEditService
    {
        public IReadOnlyList<HistoryEntry> Entries { get; set; } = Array.Empty<HistoryEntry>();

        public Task<EditResult> SetFieldAsync(string orderId, EditField field, string newValue, string? expectedOld = null, CancellationToken ct = default)
            => throw new NotSupportedException("Read-only detail view must not edit");

        public Task<EditResult> DeleteAsync(string orderId, string from, CancellationToken ct = default)
            => throw new NotSupportedException("Read-only detail view must not delete");

        public Task<EditResult> RestoreAsync(string orderId, CancellationToken ct = default)
            => throw new NotSupportedException("Read-only detail view must not restore");

        public Task<EditResult> ArchiveAsync(string orderId, CancellationToken ct = default)
            => throw new NotSupportedException("Read-only detail view must not archive");

        public Task<EditResult> UnarchiveAsync(string orderId, CancellationToken ct = default)
            => throw new NotSupportedException("Read-only detail view must not unarchive");

        public Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(string orderId, CancellationToken ct = default)
            => Task.FromResult(Entries);
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

    private static byte[] CreateSamplePngBytes(Color color)
    {
        var bmp = BitmapSource.Create(64, 48, 96, 96, PixelFormats.Bgra32, null,
            Enumerable.Repeat(new[] { color.B, color.G, color.R, color.A }, 64 * 48).SelectMany(x => x).ToArray(),
            64 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static Order CreateRichShopeeOrder() => new()
    {
        Id = "ord_rich_shp_01",
        OrderId = "2611SHP9901A",
        ReturnOrderId = "RET-2611-8812",
        Track = "SPXVN069991112",
        ReturnTrack = "SPXRET00991112",
        ForwardTrack = "SPXFWD00112233",
        AltTrack = "ALT-991112",
        TrackingCodes = ["SPXOLD0001", "SPXOLD0002"],
        ScanAliases = ["ALIAS-991112"],
        Platform = PlatformNames.Shopee,
        OrderType = OrderTypeNames.ReturnRefund,
        PlatformStatus = "Đang trả hàng về người bán",
        Status = ReceiveStatus.NotReturned,
        PreOrder = "Không",
        Carrier = "SPX Express",
        ShipDate = "18/11/2026 09:15",
        ReturnDate = "21/11/2026 14:20",
        ReturnedAt = "22/11/2026 10:00",
        RecvDate = "",
        Deadline = "25/11/2026 23:59",
        ImportWeek = "2026-W47",
        ImportedAt = "22/11/2026 10:30",
        Price = 425000,
        Note = "Khách báo sai kích thước, trả lại toàn bộ 2 áo",
        BuyerNote = "Xin đổi sang size XL ở đơn sau",
        UserNote = "Dòng 1: Kiểm tra tem mác\nDòng 2: Còn nguyên túi zip",
        IsWatchlist = true,
        WatchlistReason = "Đơn giá trị cao, cần quay video mở hộp",
        IsPartialReturn = true,
        PhotoId = "photo_rich_01",
        Items =
        [
            new OrderItem
            {
                Product = "Áo Khoác Dù 2 Lớp Demo",
                Color = "Đen",
                Size = "L",
                Qty = 2,
                Sku = "AKD-DEN-L",
                SkuId = "SKU-1001",
                ImportPrices = new Dictionary<string, long>
                {
                    ["discounted"] = 180000,
                    ["originalUnit"] = 220000
                }
            },
            new OrderItem
            {
                Product = "Áo Thun Cotton Cổ Tròn Demo",
                Color = "Trắng",
                Size = "M",
                Qty = 1,
                Sku = "ATC-TRA-M",
                SkuId = "SKU-1002",
                ImportPrices = new Dictionary<string, long>
                {
                    ["discounted"] = 65000
                }
            }
        ],
        Import = new ImportMeta
        {
            ImportPriceBasis = "line",
            ImportSelectedPriceMode = "discounted",
            ForwardCarrier = "SPX Express",
            ReturnCarrier = "GHN",
            PriceModes = new Dictionary<string, long>
            {
                ["discounted"] = 425000,
                ["original"] = 505000
            },
            RawDetails = new Dictionary<string, string>
            {
                ["Mã đơn hàng"] = "2611SHP9901A",
                ["Lý do trả hàng"] = "Không vừa size",
                ["Kho nhận"] = "Kho Hà Nội"
            }
        }
    };

    private static Order CreateRichTikTokOrder() => new()
    {
        Id = "ord_tt_02",
        OrderId = "5789TT004422",
        ReturnOrderId = "TT-RET-4422",
        Track = "TTVN00442211",
        ReturnTrack = "TTVN00442211",
        ForwardTrack = "JTFWD004422",
        Platform = PlatformNames.TikTok,
        OrderType = OrderTypeNames.Lost,
        PlatformStatus = "Đơn thất thoát chờ đối soát",
        Status = ReceiveStatus.Received,
        Carrier = "J&T Express",
        ShipDate = "16/11/2026 08:00",
        ReturnDate = "20/11/2026 11:00",
        ReturnedAt = "21/11/2026 16:30",
        RecvDate = "22/11/2026 09:15",
        Price = 290000,
        Note = "ĐVVC báo thất lạc khi giao hoàn",
        UserNote = "Đã gửi khiếu nại lên sàn",
        Items =
        [
            new OrderItem { Product = "Quần Jeans Ống Suông Demo", Color = "Xanh nhạt", Size = "29", Qty = 1, Sku = "QJ-XN-29" }
        ]
    };

    [Fact]
    public void OrderDetailViewModel_PopulatesAllGroups_AndFiltersRawDetails()
    {
        var order = CreateRichShopeeOrder();
        var clipboard = new StubClipboardService();
        var photoViewer = new StubPhotoViewerService();
        var photoProvider = new StubPhotoProvider();
        photoProvider.Add("photo_rich_01", CreateSamplePngBytes(Colors.SteelBlue));
        var editService = new StubOrderEditService
        {
            Entries =
            [
                new HistoryEntry("23/11/2026 12:00", "userNote", "Ghi chú của tôi", "", "Dòng 1: Kiểm tra tem mác", "manual")
            ]
        };

        var vm = new OrderDetailViewModel(order, FixedNow, photoProvider, photoViewer, editService, clipboard);

        WpfTestHost.Run(() =>
        {
            WpfTestHost.RunTask(() => vm.LoadAsync());
        });

        // Overview
        Assert.True(vm.HasOverviewGroup);
        Assert.Equal(PlatformNames.Shopee, vm.Platform);
        Assert.Equal(OrderTypeNames.ReturnRefund, vm.OrderType);
        Assert.Equal("Đang trả hàng về người bán", vm.PlatformStatus);
        Assert.Equal(ReceiveStatus.NotReturned, vm.Status);
        Assert.Equal(AlertLevel.Red, vm.AlertLevel);
        Assert.Contains("Báo Đỏ", vm.AlertText);
        Assert.Equal("SPX Express", vm.Carrier);
        Assert.Equal("GHN", vm.ReturnCarrier);
        Assert.Contains("Đơn giá trị cao", vm.WatchlistText);

        // Codes + Copy
        Assert.True(vm.HasCodesGroup);
        Assert.Contains(vm.CodeRows, r => r.Label == "Mã đơn" && r.RawValue == "2611SHP9901A");
        Assert.Contains(vm.CodeRows, r => r.Label == "Mã giao đi" && r.RawValue == "SPXFWD00112233");
        Assert.True(vm.HasHistoryCodes);
        Assert.Equal(3, vm.HistoryCodes.Count); // 2 TrackingCodes + 1 ScanAlias
        Assert.Equal("Mã giao đi dùng chung không đủ để nhận kiện", vm.ForwardTrackWarning);

        vm.CopyCodeCommand.Execute("SPXVN069991112");
        Assert.Single(clipboard.CopiedTexts);
        Assert.Equal("SPXVN069991112", clipboard.CopiedTexts[0]);

        // Timeline
        Assert.True(vm.HasTimelineGroup);
        Assert.Equal("18/11/2026 09:15", vm.ShipDate);
        Assert.Equal("25/11/2026 23:59", vm.Deadline);
        Assert.Equal("—", vm.RecvDate);

        // Items
        Assert.True(vm.HasItemsGroup);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(3, vm.TotalQty);
        Assert.True(vm.IsPartialReturn);
        Assert.Contains("discounted:", vm.Items[0].ImportPricesText);

        // Notes
        Assert.True(vm.HasNotesGroup);
        Assert.Contains("\n", vm.UserNote);

        // Photo
        Assert.True(vm.HasPhotoLoaded);
        Assert.NotNull(vm.ThumbnailImage);
        vm.OpenPhotoViewerCommand.Execute(null);
        Assert.Single(photoViewer.Calls);
        Assert.Equal("photo_rich_01", photoViewer.Calls[0].PhotoId);

        // Raw import (collapsed by default)
        Assert.True(vm.HasRawImportGroup);
        Assert.False(vm.IsRawImportExpanded);
        Assert.Equal("line", vm.ImportPriceBasis);
        Assert.Equal(2, vm.PriceModes.Count);
        Assert.Equal(3, vm.FilteredRawDetails.Count);
        vm.RawDetailsFilter = "Hà Nội";
        Assert.Single(vm.FilteredRawDetails);
        Assert.Equal("Kho nhận", vm.FilteredRawDetails[0].Column);

        // History
        Assert.False(vm.IsHistoryEmpty);
        Assert.Single(vm.HistoryEntries);
    }

    [Fact]
    public void OrderDetailViewModel_EmptyOrderAndBrokenPhoto_DoesNotCrash_AndHidesEmptyGroups()
    {
        var emptyOrder = new Order { Id = "empty_01", PhotoId = "broken_photo" };
        var photoProvider = new StubPhotoProvider();
        photoProvider.Add("broken_photo", [1, 2, 3, 4]); // corrupt image bytes

        var vm = new OrderDetailViewModel(
            emptyOrder,
            FixedNow,
            photoProvider,
            new StubPhotoViewerService(),
            new StubOrderEditService(),
            new StubClipboardService());

        WpfTestHost.Run(() =>
        {
            WpfTestHost.RunTask(() => vm.LoadAsync());
        });

        Assert.False(vm.HasOverviewGroup);
        Assert.False(vm.HasCodesGroup);
        Assert.False(vm.HasTimelineGroup);
        Assert.False(vm.HasItemsGroup);
        Assert.False(vm.HasNotesGroup);
        Assert.False(vm.HasRawImportGroup);
        Assert.False(vm.HasPhotoLoaded);
        Assert.Contains("Không tải được ảnh", vm.PhotoStatusText);
        Assert.True(vm.IsHistoryEmpty);
        Assert.Equal("—", vm.OrderType);
        Assert.Equal("—", vm.ShipDate);
        Assert.Equal("—", vm.Note);
    }

    [Fact]
    public void OpenTriggers_DoubleClick_Enter_And_FirstMenuItem_WorkAcrossAllTables_WithoutBreakingDelete()
    {
        WpfTestHost.Run(() =>
        {
            var provider = new StubOrderProvider();
            var parcelA = new Order
            {
                Id = "id_A",
                OrderId = "SHARED-ORD-1",
                Track = "SPX-A",
                ReturnTrack = "SPX-A",
                Platform = PlatformNames.Shopee,
                OrderType = OrderTypeNames.ReturnRefund,
                Status = ReceiveStatus.NotReturned
            };
            var parcelB = new Order
            {
                Id = "id_B",
                OrderId = "SHARED-ORD-1",
                Track = "SPX-B",
                ReturnTrack = "SPX-B",
                Platform = PlatformNames.Shopee,
                OrderType = OrderTypeNames.ReturnRefund,
                Status = ReceiveStatus.NotReturned
            };
            var archivedOrder = new Order
            {
                Id = "id_arc",
                OrderId = "ARC-1",
                Track = "SPX-ARC",
                Platform = PlatformNames.Shopee,
                Status = ReceiveStatus.Received
            };
            var deletedOrder = new Order
            {
                Id = "id_del",
                OrderId = "DEL-1",
                Track = "SPX-DEL",
                Platform = PlatformNames.Shopee
            };
            var newOrderWithFull = new Order
            {
                Id = "id_new",
                OrderId = "NEW-1",
                Track = "SPX-NEW",
                Platform = PlatformNames.Shopee
            };

            provider.ReturnOrders.AddRange([parcelA, parcelB]);
            provider.ArchivedOrders.Add(archivedOrder);
            provider.DeletedOrders.Add(new DeletedOrderRecord
            {
                Id = deletedOrder.Id,
                OrderId = deletedOrder.OrderId,
                Track = deletedOrder.Track,
                Platform = deletedOrder.Platform,
                FullOrder = deletedOrder
            });
            provider.NewOrders.Add(new NewOrderRecord
            {
                Id = newOrderWithFull.Id,
                Track = newOrderWithFull.Track,
                Platform = newOrderWithFull.Platform,
                FullOrder = newOrderWithFull
            });

            var detailSvc = new StubDetailService();
            var returnVm = new ReturnOrdersViewModel(
                provider,
                ReturnTableScope.ShopeeReturn,
                onOpenDetail: o => detailSvc.ShowOrderDetail(o));
            var returnView = new ReturnOrdersView { DataContext = returnVm };
            var hostWin = new Window { Content = returnView, Width = 1000, Height = 600 };
            hostWin.Show();
            WpfTestHost.PumpDispatcher(hostWin);

            var grid = (DataGrid)returnView.FindName("OrdersGrid")!;
            var menu = (ContextMenu)grid.Resources["RowActionContextMenu"]!;
            var firstItem = menu.Items[0] as MenuItem;
            Assert.NotNull(firstItem);
            Assert.Equal("🔍 Xem chi tiết", firstItem!.Header);

            // Parcel A vs Parcel B isolation
            var rowA = returnVm.VisibleOrders.Single(r => r.Source.Id == "id_A");
            returnVm.SelectedRow = rowA;
            returnVm.OpenDetailCommand.Execute(rowA);
            Assert.Single(detailSvc.OpenedOrders);
            Assert.Equal("id_A", detailSvc.OpenedOrders[0].Id);
            Assert.Equal("SPX-A", detailSvc.OpenedOrders[0].Track);

            // Enter key opens detail
            var enterArgs = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(grid)!, 0, Key.Enter)
            {
                RoutedEvent = UIElement.PreviewKeyDownEvent
            };
            grid.RaiseEvent(enterArgs);
            Assert.Equal(2, detailSvc.OpenedOrders.Count);
            Assert.Equal("id_A", detailSvc.OpenedOrders[1].Id);

            // Archived table
            var archivedVm = new ArchivedOrdersViewModel(provider, onOpenDetail: o => detailSvc.ShowOrderDetail(o));
            var archivedView = new ArchivedOrdersView { DataContext = archivedVm };
            var arcGrid = (DataGrid)archivedView.FindName("ArchivedGrid")!;
            var arcMenu = (ContextMenu)arcGrid.Resources["ArchivedRowContextMenu"]!;
            Assert.Equal("🔍 Xem chi tiết", ((MenuItem)arcMenu.Items[0]).Header);
            archivedVm.OpenDetailCommand.Execute(archivedVm.VisibleOrders[0]);
            Assert.Equal("id_arc", detailSvc.OpenedOrders[^1].Id);

            // Deleted table
            var deletedVm = new DeletedOrdersViewModel(provider, onOpenDetail: o => detailSvc.ShowOrderDetail(o));
            var deletedView = new DeletedOrdersView { DataContext = deletedVm };
            var delGrid = (DataGrid)deletedView.FindName("DeletedGrid")!;
            var delMenu = (ContextMenu)delGrid.Resources["DeletedRowContextMenu"]!;
            Assert.Equal("🔍 Xem chi tiết", ((MenuItem)delMenu.Items[0]).Header);
            deletedVm.OpenDetailCommand.Execute(deletedVm.VisibleOrders[0]);
            Assert.Equal("id_del", detailSvc.OpenedOrders[^1].Id);

            // New orders table
            var newVm = new NewOrdersViewModel(provider, onOpenDetail: o => detailSvc.ShowOrderDetail(o));
            var newView = new NewOrdersView { DataContext = newVm };
            var newGrid = (DataGrid)newView.FindName("NewOrdersGrid")!;
            var newMenu = (ContextMenu)newGrid.Resources["NewRowContextMenu"]!;
            Assert.Equal("🔍 Xem chi tiết", ((MenuItem)newMenu.Items[0]).Header);
            newVm.OpenDetailCommand.Execute(newVm.VisibleOrders[0]);
            Assert.Equal("id_new", detailSvc.OpenedOrders[^1].Id);

            hostWin.Close();
        });
    }

    [Fact]
    public void OrderDetailWindow_EscCloses_SingleWindowOnly_And_DoesNotModifyDatabase()
    {
        var dbPath = Path.Combine(_tempRoot, "t018_readonly.db");
        AppDatabase.Initialize(dbPath);
        var legacyDir = Path.Combine(_tempRoot, "legacy");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(
            Path.Combine(legacyDir, "du_lieu_hang_hoan.json"),
            """[{"id":"db_ord_1","orderId":"ORD-DB-1","track":"SPX-DB-1","returnTrack":"SPX-DB-1","platform":"Shopee","orderType":"Trả hàng/Hoàn tiền","status":"Chưa trả","price":100000,"items":[{"product":"Áo Demo","qty":1}]}]""");
        LegacyImporter.Import(legacyDir, dbPath);

        var dbBefore = File.ReadAllBytes(dbPath);
        var provider = new SqliteOrderProvider(dbPath, () => FixedNow);
        var editService = new SqliteOrderEditService(dbPath, () => FixedNow);
        var photoProvider = new SqlitePhotoProvider(dbPath);
        var order = provider.GetReturnOrders().Single();

        WpfTestHost.Run(() =>
        {
            var svc = new WindowsOrderDetailService(
                photoProvider,
                new WindowsPhotoViewerService(photoProvider),
                editService,
                new StubClipboardService(),
                () => FixedNow);

            // Yêu cầu chủ dự án: chỉ MỘT cửa sổ chi tiết; mở đơn khác thì cửa sổ cũ tự đóng, cửa sổ mới hiện ra.
            svc.ShowOrderDetail(order);
            WpfTestHost.PumpDispatcher();
            var first = Application.Current!.Windows.OfType<OrderDetailWindow>().Single();
            svc.ShowOrderDetail(CreateRichTikTokOrder());
            WpfTestHost.PumpDispatcher();

            var openDetails = Application.Current.Windows.OfType<OrderDetailWindow>().ToList();
            Assert.Single(openDetails);
            Assert.NotSame(first, openDetails[0]);

            var escArgs = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(openDetails[0])!, 0, Key.Escape)
            {
                RoutedEvent = UIElement.PreviewKeyDownEvent
            };
            openDetails[0].RaiseEvent(escArgs);
            WpfTestHost.PumpDispatcher();

            Assert.Empty(Application.Current.Windows.OfType<OrderDetailWindow>());
        });

        SqliteConnection.ClearAllPools();
        var dbAfter = File.ReadAllBytes(dbPath);
        Assert.Equal(dbBefore, dbAfter);
    }

    [Fact]
    public void DemoMode_OpensDetailWithoutTouchingDisk()
    {
        var fake = new FakeDataService(enableMutableDemoStore: true);
        using var main = new MainViewModel(
            fake, fake, fake, fake,
            new FakeLegacyImporter(fake.GetCurrentTime),
            isDemoMode: true,
            deferInitialLoad: false,
            dispatchToUi: a => a(),
            uiSettingsStore: new UiSettingsStore(null, isDemoMode: true));

        WpfTestHost.Run(() =>
        {
            var row = main.ShopeeReturnVm.VisibleOrders.First();
            main.ShopeeReturnVm.OpenDetailCommand.Execute(row);
            WpfTestHost.PumpDispatcher();

            var detailWin = Application.Current!.Windows.OfType<OrderDetailWindow>().Single();
            Assert.Contains(row.OrderId, detailWin.Title);
            detailWin.Close();
        });
    }

    [Fact]
    public void RenderOrderDetailScreens_ForVisualVerification()
    {
        var outDir = Path.Combine(FindRepoRoot(), "tasks", "reports", "T018-screens");
        Directory.CreateDirectory(outDir);

        WpfTestHost.Run(() =>
        {
            var photoProvider = new StubPhotoProvider();
            photoProvider.Add("photo_rich_01", CreateSamplePngBytes(Colors.CornflowerBlue));
            var editService = new StubOrderEditService
            {
                Entries =
                [
                    new HistoryEntry("23/11/2026 14:10", "userNote", "Ghi chú của tôi", "", "Dòng 1: Kiểm tra tem mác", "manual"),
                    new HistoryEntry("23/11/2026 14:05", "isWatchlist", "Theo dõi", "Không", "Có", "manual")
                ]
            };

            // 1. Shopee multi-item
            RenderAndSave(
                new OrderDetailViewModel(CreateRichShopeeOrder(), FixedNow, photoProvider, new StubPhotoViewerService(), editService, new StubClipboardService()),
                Path.Combine(outDir, "01-shopee-multi-items.png"),
                expandRaw: false);

            // 2. TikTok
            RenderAndSave(
                new OrderDetailViewModel(CreateRichTikTokOrder(), FixedNow, photoProvider, new StubPhotoViewerService(), new StubOrderEditService(), new StubClipboardService()),
                Path.Combine(outDir, "02-tiktok-order.png"),
                expandRaw: false);

            // 3. No photo / minimal data
            RenderAndSave(
                new OrderDetailViewModel(new Order { Id = "min_01", OrderId = "MIN-001", Track = "SPX-MIN", Platform = PlatformNames.Shopee }, FixedNow, photoProvider, new StubPhotoViewerService(), new StubOrderEditService(), new StubClipboardService()),
                Path.Combine(outDir, "03-empty-no-photo.png"),
                expandRaw: false);

            // 4. Raw import expanded
            RenderAndSave(
                new OrderDetailViewModel(CreateRichShopeeOrder(), FixedNow, photoProvider, new StubPhotoViewerService(), editService, new StubClipboardService()),
                Path.Combine(outDir, "04-raw-import-expanded.png"),
                expandRaw: true);
        });

        Assert.True(File.Exists(Path.Combine(outDir, "01-shopee-multi-items.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "02-tiktok-order.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "03-empty-no-photo.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "04-raw-import-expanded.png")));
    }

    private static void RenderAndSave(OrderDetailViewModel vm, string path, bool expandRaw)
    {
        WpfTestHost.RunTask(() => vm.LoadAsync());
        if (expandRaw) vm.IsRawImportExpanded = true;

        var win = new OrderDetailWindow
        {
            DataContext = vm,
            Width = 920,
            Height = 820
        };
        win.Show();
        WpfTestHost.PumpDispatcher(win);
        if (expandRaw)
        {
            (win.FindName("DetailScrollViewer") as ScrollViewer)?.ScrollToBottom();
            WpfTestHost.PumpDispatcher(win);
        }
        WpfTestHost.SaveWindowPng(win, path);
        win.Close();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tasks")) &&
                Directory.Exists(Path.Combine(dir.FullName, "v2")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Không tìm thấy thư mục gốc repo.");
    }
}
