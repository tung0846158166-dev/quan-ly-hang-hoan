using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.App.Views;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Tests.UiTests;

public sealed class T013ManualActionsUiTests : IDisposable
{
    private static readonly DateTime FixedNow = new(2026, 11, 23, 15, 0, 0);
    private readonly string _tempRoot;

    public T013ManualActionsUiTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "QuanLyHangHoan_T013_" + Guid.NewGuid().ToString("N"));
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

    private string CreateFakeDb()
    {
        var dbPath = Path.Combine(_tempRoot, "t013_" + Guid.NewGuid().ToString("N")[..8] + ".db");
        AppDatabase.Initialize(dbPath);

        var legacyDir = Path.Combine(_tempRoot, "legacy_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(legacyDir);

        var rows = new List<string>
        {
            """{"id":"pA","orderId":"SHP-MULTI-1","track":"SPX-KIEN-A","returnTrack":"SPX-KIEN-A","platform":"Shopee","orderType":"Trả hàng/Hoàn tiền","status":"Chưa trả","returnedAt":"23/11/2026 08:00","note":"Lý do gốc A","userNote":"","price":150000,"items":[{"product":"Áo Thun Demo A","color":"Trắng","size":"M","qty":1}]}""",
            """{"id":"pB","orderId":"SHP-MULTI-1","track":"SPX-KIEN-B","returnTrack":"SPX-KIEN-B","platform":"Shopee","orderType":"Trả hàng/Hoàn tiền","status":"Chưa trả","returnedAt":"23/11/2026 08:00","note":"Lý do gốc B","userNote":"","price":120000,"items":[{"product":"Quần Short Demo B","color":"Đen","size":"L","qty":1}]}""",
            """{"id":"pC","orderId":"SHP-OTHER-2","track":"SPX-KIEN-C","returnTrack":"SPX-KIEN-C","platform":"Shopee","orderType":"Trả hàng/Hoàn tiền","status":"Chưa trả","returnedAt":"23/11/2026 09:00","note":"Lý do gốc C","userNote":"","price":200000,"items":[{"product":"Váy Hoa Demo C","color":"Hồng","size":"S","qty":1}]}"""
        };

        File.WriteAllText(Path.Combine(legacyDir, "du_lieu_hang_hoan.json"), "[" + string.Join(",", rows) + "]");
        LegacyImporter.Import(legacyDir, dbPath);
        return dbPath;
    }

    private MainViewModel CreateRealMainVm(string dbPath, StubDialogService dialog, TimeSpan? toastDuration = null)
    {
        var provider = new SqliteOrderProvider(dbPath, () => FixedNow);
        var editService = new SqliteOrderEditService(dbPath, () => FixedNow);
        var settingsPath = Path.Combine(_tempRoot, "settings.json");

        return new MainViewModel(
            provider,
            provider,
            provider,
            provider,
            new FakeLegacyImporter(() => FixedNow),
            isDemoMode: false,
            syncController: new FakeSyncController("ROOM-T013"),
            uiSettingsStore: new UiSettingsStore(settingsPath, isDemoMode: false),
            orderEditService: editService,
            orderActionDialogService: dialog,
            toastDuration: toastDuration ?? TimeSpan.Zero);
    }

    [Fact]
    public async Task EditStatusNoteWatchlist_SavesAndReloads_WithHistoryNewestFirst()
    {
        var dbPath = CreateFakeDb();
        var dialog = new StubDialogService
        {
            NextStatus = ReceiveStatus.Received,
            NextText = "Đã kiểm hàng kỹ, đủ phụ kiện",
            NextWatchlist = (true, "Khách hay khiếu nại")
        };

        using var main = CreateRealMainVm(dbPath, dialog);
        await main.ReloadAllDataAsync();

        var rowA = main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Track == "SPX-KIEN-A");
        var idA = rowA.Source.Id;
        await main.ShopeeReturnVm.ChangeStatusCommand.ExecuteAsync(rowA);

        rowA = main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Id == idA);
        Assert.Equal(ReceiveStatus.Received, rowA.Status);

        await main.ShopeeReturnVm.EditUserNoteCommand.ExecuteAsync(rowA);
        rowA = main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Id == idA);
        Assert.Equal("Đã kiểm hàng kỹ, đủ phụ kiện", rowA.Source.UserNote);

        await main.ShopeeReturnVm.ToggleWatchlistCommand.ExecuteAsync(rowA);
        rowA = main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Id == idA);
        Assert.True(rowA.IsWatchlist);
        Assert.Equal("Khách hay khiếu nại", rowA.Source.WatchlistReason);

        await main.ShopeeReturnVm.ShowHistoryCommand.ExecuteAsync(rowA);
        Assert.NotNull(dialog.LastShownHistory);
        Assert.True(dialog.LastShownHistory!.Count >= 4); // Status + RecvDate + UserNote + IsWatchlist + WatchlistReason
        Assert.Equal("watchlistReason", dialog.LastShownHistory[0].Field);
        Assert.All(dialog.LastShownHistory, h => Assert.Equal("manual", h.EditType));
    }

    [Fact]
    public async Task Delete_UndoAndRestore_MovesBetweenTables()
    {
        var dbPath = CreateFakeDb();
        var dialog = new StubDialogService { ConfirmSingleResult = true };

        using var main = CreateRealMainVm(dbPath, dialog);
        await main.ReloadAllDataAsync();

        var rowA = main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Track == "SPX-KIEN-A");
        var idA = rowA.Source.Id;
        await main.ShopeeReturnVm.DeleteRowCommand.ExecuteAsync(rowA);

        Assert.DoesNotContain(main.ShopeeReturnVm.VisibleOrders, r => r.Source.Id == idA);
        Assert.Contains(main.DeletedOrdersVm.VisibleOrders, r => r.Source.Id == idA);
        Assert.True(main.IsToastVisible);
        Assert.True(main.CanUndoDelete);
        Assert.Contains("SPX-KIEN-A", main.ToastMessage);

        // Hoàn tác qua toast
        await main.UndoDeleteCommand.ExecuteAsync(null);
        Assert.False(main.IsToastVisible);
        Assert.Contains(main.ShopeeReturnVm.VisibleOrders, r => r.Source.Id == idA);
        Assert.DoesNotContain(main.DeletedOrdersVm.VisibleOrders, r => r.Source.Id == idA);

        // Xóa lại lần nữa rồi Khôi phục từ màn Đã xóa
        rowA = main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Id == idA);
        await main.ShopeeReturnVm.DeleteRowCommand.ExecuteAsync(rowA);
        var delA = main.DeletedOrdersVm.VisibleOrders.Single(r => r.Source.Id == idA);

        await main.DeletedOrdersVm.RestoreRowCommand.ExecuteAsync(delA);
        Assert.Contains(main.ShopeeReturnVm.VisibleOrders, r => r.Source.Id == idA);
        Assert.DoesNotContain(main.DeletedOrdersVm.VisibleOrders, r => r.Source.Id == idA);
    }

    [Fact]
    public async Task ArchiveAndUnarchive_MovesBetweenReturnAndArchive()
    {
        var dbPath = CreateFakeDb();
        var dialog = new StubDialogService();

        using var main = CreateRealMainVm(dbPath, dialog);
        await main.ReloadAllDataAsync();

        var rowC = main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Track == "SPX-KIEN-C");
        var idC = rowC.Source.Id;
        await main.ShopeeReturnVm.ArchiveRowCommand.ExecuteAsync(rowC);

        Assert.DoesNotContain(main.ShopeeReturnVm.VisibleOrders, r => r.Source.Id == idC);
        Assert.Contains(main.ArchivedOrdersVm.VisibleOrders, r => r.Source.Id == idC);

        var arcC = main.ArchivedOrdersVm.VisibleOrders.Single(r => r.Source.Id == idC);
        await main.ArchivedOrdersVm.UnarchiveRowCommand.ExecuteAsync(arcC);

        Assert.Contains(main.ShopeeReturnVm.VisibleOrders, r => r.Source.Id == idC);
        Assert.DoesNotContain(main.ArchivedOrdersVm.VisibleOrders, r => r.Source.Id == idC);
    }

    [Fact]
    public async Task ActionOnParcelA_DoesNotAffectParcelB_WithSameOrderId()
    {
        var dbPath = CreateFakeDb();
        var dialog = new StubDialogService
        {
            NextStatus = ReceiveStatus.Received,
            ConfirmSingleResult = true
        };

        using var main = CreateRealMainVm(dbPath, dialog);
        await main.ReloadAllDataAsync();

        var rowA = main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Track == "SPX-KIEN-A");
        var idA = rowA.Source.Id;
        var idB = main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Track == "SPX-KIEN-B").Source.Id;
        await main.ShopeeReturnVm.ChangeStatusCommand.ExecuteAsync(rowA);

        var rowB = main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Id == idB);
        Assert.Equal(ReceiveStatus.NotReturned, rowB.Status);
        Assert.Equal("", rowB.RecvDate);

        // Xóa A không xóa B
        rowA = main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Id == idA);
        await main.ShopeeReturnVm.DeleteRowCommand.ExecuteAsync(rowA);
        Assert.Contains(main.ShopeeReturnVm.VisibleOrders, r => r.Source.Id == idB);
        Assert.DoesNotContain(main.DeletedOrdersVm.VisibleOrders, r => r.Source.Id == idB);
    }

    [Fact]
    public async Task Conflict_WhenModifiedElsewhere_DoesNotOverwrite_AndReloads()
    {
        var dbPath = CreateFakeDb();
        var dialog = new StubDialogService { NextText = "Ghi chú cũ từ UI" };

        using var main = CreateRealMainVm(dbPath, dialog);
        await main.ReloadAllDataAsync();

        var staleRowA = main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Track == "SPX-KIEN-A");
        var idA = staleRowA.Source.Id;

        // Nơi khác (ví dụ quét/đồng bộ) sửa user_note của pA trước
        var ext = OrderEditor.SetField(dbPath, idA, EditField.UserNote, "Ghi chú từ quét điện thoại", FixedNow.AddMinutes(1));
        Assert.Equal(EditKind.Saved, ext.Kind);

        await main.ShopeeReturnVm.EditUserNoteCommand.ExecuteAsync(staleRowA);

        Assert.True(main.IsToastVisible);
        Assert.True(main.ToastIsError);
        Assert.Contains("nơi khác", main.ToastMessage, StringComparison.OrdinalIgnoreCase);

        // Phải tải lại giá trị mới, KHÔNG ghi đè bằng "Ghi chú cũ từ UI"
        var refreshedA = main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Id == idA);
        Assert.Equal("Ghi chú từ quét điện thoại", refreshedA.Source.UserNote);
    }

    [Fact]
    public async Task BulkMarkReceived_ReportsExactSuccessAndConflictCount()
    {
        var dbPath = CreateFakeDb();
        var dialog = new StubDialogService { ConfirmBulkResult = true };

        using var main = CreateRealMainVm(dbPath, dialog);
        await main.ReloadAllDataAsync();

        var rowA = main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Track == "SPX-KIEN-A");
        var rowB = main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Track == "SPX-KIEN-B");
        var rowC = main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Track == "SPX-KIEN-C");

        // Làm stale riêng pB bằng cách sửa DB bên ngoài
        OrderEditor.SetField(dbPath, rowB.Source.Id, EditField.Status, ReceiveStatus.Lost, FixedNow.AddMinutes(1));

        main.ShopeeReturnVm.UpdateSelectedRows([rowA, rowB, rowC]);
        await main.ShopeeReturnVm.BulkMarkReceivedCommand.ExecuteAsync(null);

        Assert.True(main.IsToastVisible);
        Assert.Contains("2 thành công", main.ToastMessage);
        Assert.Contains("1 không đổi/lỗi", main.ToastMessage);
        Assert.Equal(ReceiveStatus.Received, main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Id == rowA.Source.Id).Status);
        Assert.Equal(ReceiveStatus.Lost, main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Id == rowB.Source.Id).Status);
        Assert.Equal(ReceiveStatus.Received, main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Id == rowC.Source.Id).Status);
    }

    [Fact]
    public async Task CancelConfirmation_DoesNotCallEditApi()
    {
        var spyEdit = new SpyOrderEditService();
        var dialog = new StubDialogService
        {
            ConfirmSingleResult = false,
            ConfirmBulkResult = false,
            NextStatus = null,
            NextText = null,
            NextWatchlist = null
        };
        var fakeData = new FakeDataService(enableMutableDemoStore: true);

        var vm = new ReturnOrdersViewModel(
            fakeData,
            ReturnTableScope.ShopeeReturn,
            editService: spyEdit,
            dialogService: dialog);

        var first = vm.VisibleOrders.First();
        vm.UpdateSelectedRows(vm.VisibleOrders.Take(2));

        await vm.DeleteRowCommand.ExecuteAsync(first);
        await vm.BulkDeleteCommand.ExecuteAsync(null);
        await vm.BulkMarkReceivedCommand.ExecuteAsync(null);
        await vm.ChangeStatusCommand.ExecuteAsync(first);
        await vm.EditUserNoteCommand.ExecuteAsync(first);
        await vm.ToggleWatchlistCommand.ExecuteAsync(first);

        Assert.Equal(0, spyEdit.CallCount);
    }

    [Fact]
    public async Task DemoMode_EditsInMemory_AndNeverCreatesDb()
    {
        var nonExistentDb = Path.Combine(_tempRoot, "never_created.db");
        var fakeData = new FakeDataService(() => FixedNow, enableMutableDemoStore: true);
        var fakeEdit = new FakeOrderEditService(fakeData);
        var dialog = new StubDialogService
        {
            NextStatus = ReceiveStatus.Received,
            ConfirmSingleResult = true
        };

        using var main = new MainViewModel(
            fakeData,
            fakeData,
            fakeData,
            fakeData,
            new FakeLegacyImporter(() => FixedNow),
            isDemoMode: true,
            syncController: new FakeSyncController("ROOM-DEMO"),
            uiSettingsStore: new UiSettingsStore(null, isDemoMode: true),
            orderEditService: fakeEdit,
            orderActionDialogService: dialog,
            toastDuration: TimeSpan.Zero);

        var target = main.ShopeeReturnVm.VisibleOrders.First(r => r.Status != ReceiveStatus.Received);
        var targetId = target.Source.Id;
        await main.ShopeeReturnVm.ChangeStatusCommand.ExecuteAsync(target);

        Assert.Equal(ReceiveStatus.Received, main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Id == targetId).Status);

        await main.ShopeeReturnVm.DeleteRowCommand.ExecuteAsync(
            main.ShopeeReturnVm.VisibleOrders.Single(r => r.Source.Id == targetId));
        Assert.DoesNotContain(main.ShopeeReturnVm.VisibleOrders, r => r.Source.Id == targetId);
        Assert.Contains(main.DeletedOrdersVm.VisibleOrders, r => r.Source.Id == targetId);

        Assert.False(File.Exists(nonExistentDb));
    }

    [Fact]
    public void RenderScreenshots_ForT013()
    {
        WpfTestHost.Run(() =>
        {
            var outDir = Path.Combine(FindRepoRoot(), "tasks", "reports", "T013-screens");
            Directory.CreateDirectory(outDir);

            var fakeData = new FakeDataService(() => FixedNow, enableMutableDemoStore: true);
            var fakeEdit = new FakeOrderEditService(fakeData);
            using var vm = new MainViewModel(
                fakeData,
                fakeData,
                fakeData,
                fakeData,
                new FakeLegacyImporter(() => FixedNow),
                isDemoMode: true,
                syncController: new FakeSyncController("ROOM-DEMO"),
                uiSettingsStore: new UiSettingsStore(null, isDemoMode: true),
                orderEditService: fakeEdit,
                toastDuration: TimeSpan.Zero);

            var window = new MainWindow { DataContext = vm, Width = 1360, Height = 820 };
            try
            {
                // 1. Bảng Shopee Hoàn hàng với menu thao tác trên dòng
                vm.NavigateByKey("shopee_return");
                window.Show();
                WpfTestHost.PumpDispatcher();
                var returnView = FindVisualChild<ReturnOrdersView>(window);
                if (returnView is not null)
                {
                    var grid = returnView.FindName("OrdersGrid") as DataGrid;
                    if (grid?.Resources["RowActionContextMenu"] is ContextMenu menu && vm.ShopeeReturnVm.VisibleOrders.Count > 0)
                    {
                        menu.DataContext = vm.ShopeeReturnVm.VisibleOrders[0];
                        menu.PlacementTarget = grid;
                        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Center;
                        menu.IsOpen = true;
                        WpfTestHost.PumpDispatcher();
                    }
                }
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "01-row-actions-menu.png"));
                if (returnView?.FindName("OrdersGrid") is DataGrid gClose && gClose.Resources["RowActionContextMenu"] is ContextMenu mClose)
                {
                    mClose.IsOpen = false;
                    WpfTestHost.PumpDispatcher();
                }

                // 2. Hộp xác nhận xóa
                var confirmWin = new ConfirmActionWindow(
                    "Xác nhận xóa đơn",
                    "Bạn có chắc muốn xóa đơn này khỏi \"Shopee - Hoàn hàng\"?\n\n• Mã vận đơn: SPXFAKE000101\n• Mã đơn: 2611DEMO0001A\n\nLưu ý: Đơn vẫn khôi phục được ở màn Đã xóa.",
                    "Xóa đơn",
                    isDanger: true);
                confirmWin.Show();
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(confirmWin, Path.Combine(outDir, "02-delete-confirm-dialog.png"));
                confirmWin.Close();

                // 3. Thông báo Hoàn tác
                vm.ShowDeletedUndoToast([("fake_001", "SPXFAKE000101")]);
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "03-undo-toast.png"));
                vm.DismissToastCommand.Execute(null);

                // 4. Khung Lịch sử sửa
                var historyEntries = new List<HistoryEntry>
                {
                    new("23/11/2026 15:00:05", "userNote", "Ghi chú của tôi", "", "Ưu tiên bóc kiểm quay video", "manual"),
                    new("23/11/2026 15:00:00", "status", "Nhận tại kho", "Chưa trả", "Hàng đã trả", "manual")
                };
                var histWin = new OrderHistoryWindow("2611DEMO0001A", "SPXFAKE000101", historyEntries);
                histWin.Show();
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(histWin, Path.Combine(outDir, "04-order-history-dialog.png"));
                histWin.Close();

                // 5. Kết quả nhiều dòng (thanh bulk + thông báo kết quả)
                vm.ShopeeReturnVm.UpdateSelectedRows(vm.ShopeeReturnVm.VisibleOrders.Take(3));
                vm.ShowToast("2 thành công, 1 không đổi/lỗi.", isError: false);
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "05-bulk-selection-result.png"));
                vm.DismissToastCommand.Execute(null);

                // 6. Màn Đã xóa với nút Khôi phục
                vm.NavigateByKey("deleted");
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "06-deleted-orders-restore.png"));
            }
            finally
            {
                window.Close();
                WpfTestHost.PumpDispatcher();
            }
        });

        var screensDir = Path.Combine(FindRepoRoot(), "tasks", "reports", "T013-screens");
        Assert.True(File.Exists(Path.Combine(screensDir, "01-row-actions-menu.png")));
        Assert.True(File.Exists(Path.Combine(screensDir, "02-delete-confirm-dialog.png")));
        Assert.True(File.Exists(Path.Combine(screensDir, "03-undo-toast.png")));
        Assert.True(File.Exists(Path.Combine(screensDir, "04-order-history-dialog.png")));
        Assert.True(File.Exists(Path.Combine(screensDir, "05-bulk-selection-result.png")));
        Assert.True(File.Exists(Path.Combine(screensDir, "06-deleted-orders-restore.png")));
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var found = FindVisualChild<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    private static void EnsureWpfApplication()
    {
        if (Application.Current is null)
        {
            var app = new QuanLyHangHoan.App.App();
            app.InitializeComponent();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new DispatcherOperationCallback(_ => { frame.Continue = false; return null; }),
            null);
        Dispatcher.PushFrame(frame);
    }

    private static void SaveWindowPng(Window window, string path)
    {
        window.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        encoder.Save(fs);
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
        throw new DirectoryNotFoundException("Không tìm thấy thư mục gốc repo.");
    }

    private sealed class StubDialogService : IOrderActionDialogService
    {
        public string? NextStatus { get; set; }
        public string? NextText { get; set; }
        public (bool IsWatchlist, string Reason)? NextWatchlist { get; set; }
        public bool ConfirmSingleResult { get; set; }
        public bool ConfirmBulkResult { get; set; }
        public IReadOnlyList<HistoryEntry>? LastShownHistory { get; private set; }

        public string? PromptStatus(string orderId, string tracking, string currentStatus) => NextStatus;
        public string? PromptMultilineText(string title, string subtitle, string fieldLabel, string initialValue) => NextText;
        public (bool IsWatchlist, string Reason)? PromptWatchlist(string orderId, string tracking, bool currentWatchlist, string currentReason) => NextWatchlist;
        public bool ConfirmDeleteSingle(string tracking, string orderId, string fromTable) => ConfirmSingleResult;
        public bool ConfirmBulkAction(string title, int count, string detail) => ConfirmBulkResult;
        public void ShowHistory(string orderId, string tracking, IReadOnlyList<HistoryEntry> entries) => LastShownHistory = entries;
    }

    private sealed class SpyOrderEditService : IOrderEditService
    {
        public int CallCount { get; private set; }

        public Task<EditResult> SetFieldAsync(string orderId, EditField field, string newValue, string? expectedOld = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new EditResult(EditKind.Saved, "ok"));
        }

        public Task<EditResult> DeleteAsync(string orderId, string from, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new EditResult(EditKind.Saved, "ok"));
        }

        public Task<EditResult> RestoreAsync(string orderId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new EditResult(EditKind.Saved, "ok"));
        }

        public Task<EditResult> ArchiveAsync(string orderId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new EditResult(EditKind.Saved, "ok"));
        }

        public Task<EditResult> UnarchiveAsync(string orderId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new EditResult(EditKind.Saved, "ok"));
        }

        public Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(string orderId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<HistoryEntry>>(Array.Empty<HistoryEntry>());
        }
    }
}
