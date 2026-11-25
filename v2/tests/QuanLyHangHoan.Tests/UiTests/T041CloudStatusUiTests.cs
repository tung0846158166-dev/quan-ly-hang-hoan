using System.IO;
using System.Windows;
using System.Windows.Controls;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.Tests.UiTests;

/// <summary>
/// T041: Giao diện trạng thái Cloud, khóa ghi tập trung khi mất mạng, banner "chỉ xem",
/// hạn mức hôm nay (ok/warn/stop), ghép thiết bị và tạo mã ghép cho owner trên WpfTestHost + FakeCloudLink.
/// </summary>
public sealed class T041CloudStatusUiTests
{
    private static readonly DateTime FixedNow = new(2026, 11, 25, 14, 35, 0);

    private sealed class StubClipboard : IClipboardService
    {
        public string? LastText;
        public void SetText(string text) => LastText = text;
    }

    private sealed class StubConfirm : IConfirmationService
    {
        public bool NextResult = true;
        public int CallCount;
        public bool Confirm(string title, string message)
        {
            CallCount++;
            return NextResult;
        }
    }

    private sealed class CountingScanService : IScanService
    {
        public int Calls;
        public DateTime GetCurrentTime() => FixedNow;
        public Task<ScanResult> ReceiveAsync(string code, DateTime now, byte[]? photoBytes = null, string photoMime = "image/jpeg", CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ScanResult(ScanOutcome.NotFound, false, "Không tìm thấy", null));
        }
    }

    private sealed class CountingExportService : IOrderExportService
    {
        public int Calls;
        public Task<ExportResult> ExportAsync(IReadOnlyList<Order> orders, string tableName, ExportFormat format, bool archived = false, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new ExportResult(true, false, $"Đã xuất {orders.Count} dòng", "fake.xlsx", orders.Count));
        }
    }

    private sealed class StubDialogService : IOrderActionDialogService
    {
        public int Calls;
        public string? PromptStatus(string orderId, string tracking, string currentStatus) { Calls++; return ReceiveStatus.Received; }
        public string? PromptMultilineText(string title, string subtitle, string fieldLabel, string initialText) { Calls++; return "Ghi chú mới"; }
        public (bool IsWatchlist, string Reason)? PromptWatchlist(string orderId, string tracking, bool isWatchlist, string currentReason) { Calls++; return (true, "Lý do"); }
        public bool ConfirmDeleteSingle(string tracking, string orderId, string sourceTable) { Calls++; return true; }
        public bool ConfirmBulkAction(string actionTitle, int count, string detail) { Calls++; return true; }
        public void ShowHistory(string orderId, string tracking, IReadOnlyList<HistoryEntry> entries) { Calls++; }
    }

    private static (MainViewModel Vm, FakeCloudLink? Cloud, StubConfirm Confirm, StubClipboard Clip, CountingScanService Scan, CountingExportService Export, StubDialogService Dialog)
        CreateRig(ICloudLink? cloudLink = null, bool useRealDispatcher = false, Func<DateTime>? clock = null)
    {
        var nowFn = clock ?? (() => FixedNow);
        var fakeData = new FakeDataService(clock: nowFn, enableMutableDemoStore: true);
        var confirm = new StubConfirm();
        var clip = new StubClipboard();
        var scan = new CountingScanService();
        var export = new CountingExportService();
        var dialog = new StubDialogService();
        var folderWatch = new FakeFolderWatchController();
        folderWatch.Configure(@"C:\FakeDownloads", true);
        var sheet = new FakeSheetSyncController();
        sheet.SetUrl("https://script.google.com/macros/s/fake/exec", out _);
        var salework = new FakeSaleworkController();
        salework.OpenLogin(null, out _);
        salework.SaveCurrentPage(out _);

        var vm = new MainViewModel(
            fakeData, fakeData, fakeData, fakeData,
            new FakeLegacyImporter(nowFn),
            isDemoMode: true,
            deferInitialLoad: false,
            scanService: scan,
            clipboardService: clip,
            confirmationService: confirm,
            dispatchToUi: useRealDispatcher ? null : (a => a()),
            uiSettingsStore: new UiSettingsStore(null, isDemoMode: true),
            orderEditService: new FakeOrderEditService(fakeData),
            orderActionDialogService: dialog,
            toastDuration: TimeSpan.Zero,
            folderWatchController: folderWatch,
            sheetSyncController: sheet,
            saleworkController: salework,
            fileImportController: new FakeFileImportController(),
            returnWatchService: new FakeReturnWatchService(),
            orderExportService: export,
            cloudLink: cloudLink);

        return (vm, cloudLink as FakeCloudLink, confirm, clip, scan, export, dialog);
    }

    [Fact]
    public void AllSixCloudStates_ShowExpectedVietnameseLabelsAndColors_IncludingBackgroundThreadUpdates()
    {
        WpfTestHost.Run(() =>
        {
            var cloud = new FakeCloudLink();
            using var vm = CreateRig(cloud, useRealDispatcher: true).Vm;

            var window = new MainWindow
            {
                DataContext = vm,
                Width = 1360,
                Height = 820,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowInTaskbar = false,
                ShowActivated = false
            };

            try
            {
                window.Show();
                WpfTestHost.PumpDispatcher(window);

                var cases = new (CloudState State, string Label, string ColorKey, bool CanWrite, bool Prominent)[]
                {
                    (CloudState.Connected, "Đã kết nối", "Green", true, false),
                    (CloudState.Syncing, "Đang đồng bộ", "Blue", true, false),
                    (CloudState.Connecting, "Đang kết nối...", "Yellow", false, false),
                    (CloudState.Disconnected, "MẤT KẾT NỐI", "Red", false, true),
                    (CloudState.Unpaired, "Chưa ghép thiết bị", "Gray", false, false),
                    (CloudState.Revoked, "Thiết bị đã bị thu hồi", "Red", false, true),
                };

                foreach (var c in cases)
                {
                    // Cập nhật từ luồng nền để chứng minh ICloudLink.Changed tự đưa về luồng UI an toàn
                    WpfTestHost.RunTask(() => Task.Run(() => cloud.Set(c.State, lastSync: FixedNow)));
                    WpfTestHost.PumpDispatcher(window);

                    Assert.Equal(c.Label, vm.CloudVm.StatusText);
                    Assert.Equal(c.ColorKey, vm.CloudVm.StatusColorKey);
                    Assert.Equal(c.CanWrite, vm.CanWrite);
                    Assert.Equal(c.Prominent, vm.CloudVm.IsDisconnectedProminent);

                    var statusLabel = Assert.IsType<TextBlock>(window.FindName("CloudStatusLabel"));
                    Assert.Equal(c.Label, statusLabel.Text);
                }

                // Bấm chấm trạng thái mở popup chi tiết và gọi Đồng bộ ngay
                cloud.Set(CloudState.Connected, error: "Lỗi mạng tạm thời lúc 14:30", lastSync: FixedNow);
                WpfTestHost.PumpDispatcher(window);
                vm.CloudVm.TogglePopupCommand.Execute(null);
                WpfTestHost.PumpDispatcher(window);
                Assert.True(vm.CloudVm.IsPopupOpen);
                Assert.Equal("Lỗi mạng tạm thời lúc 14:30", vm.CloudVm.LastError);

                WpfTestHost.RunTask(() => vm.CloudVm.SyncNowCommand.ExecuteAsync(null));
                Assert.Equal(1, cloud.SyncNowCalls);
            }
            finally
            {
                window.Close();
                WpfTestHost.PumpDispatcher();
            }
        });
    }

    [Fact]
    public void Disconnected_LocksAllWriteCommands_ShowsReadOnlyBanner_KeepsReadCommandsWorking_AndUnlocksOnReconnect()
    {
        WpfTestHost.Run(() =>
        {
            var cloud = new FakeCloudLink();
            cloud.Set(CloudState.Disconnected, error: "Mất WebSocket quá 20 giây", lastSync: new DateTime(2026, 11, 25, 14, 22, 0), role: "staff", deviceName: "PC Kho 1", baseUrl: "https://qlhh.workers.dev");
            var rig = CreateRig(cloud, useRealDispatcher: true);
            using var vm = rig.Vm;

            // Chuẩn bị dữ liệu cho các màn nạp & hoàn tác để kiểm tra khóa ghi thực thụ
            vm.FileImportVm.AddFiles([@"C:\fake\tiktok.xlsx"]);
            vm.LegacyImportVm.SourceFolderPath = @"C:\fake\legacy";
            WpfTestHost.RunTask(() => vm.LegacyImportVm.RunDryRunAsync());
            vm.LegacyImportVm.IsConfirmed = true;
            vm.ShowDeletedUndoToast([("ord-1", "SPX-1")]);

            var shopeeRow = vm.ShopeeReturnVm.VisibleOrders.First();
            vm.ShopeeReturnVm.SelectedRow = shopeeRow;
            var archivedRow = vm.ArchivedOrdersVm.VisibleOrders.First();
            vm.ArchivedOrdersVm.SelectedRow = archivedRow;
            var deletedRow = vm.DeletedOrdersVm.VisibleOrders.First();
            vm.DeletedOrdersVm.SelectedRow = deletedRow;

            // Banner "chỉ xem" hiện đúng mẫu HH:mm
            Assert.True(vm.CloudVm.ShowReadOnlyBanner);
            Assert.Equal("Mất kết nối cloud. Đang xem dữ liệu cũ lúc 14:22. Mọi thao tác ghi bị tạm khóa.", vm.CloudVm.ReadOnlyBannerText);

            // Tooltip lý do khóa ghi dùng chung
            Assert.False(vm.CanWrite);
            Assert.Equal("Mất kết nối cloud — chỉ xem được", vm.WriteDisabledTooltip);
            Assert.Equal("Mất kết nối cloud — chỉ xem được", vm.ShopeeReturnVm.WriteDisabledTooltip);
            Assert.Equal("Mất kết nối cloud — chỉ xem được", vm.ScanReceiveVm.WriteDisabledTooltip);

            // TẤT CẢ lệnh ghi phải bị khóa (CanExecute == false)
            Assert.False(vm.UndoDeleteCommand.CanExecute(null));
            Assert.False(vm.NewCreatedVm.CreateNewOrderCommand.CanExecute(null));
            Assert.False(vm.ShopeeReturnVm.ChangeStatusCommand.CanExecute(shopeeRow));
            Assert.False(vm.ShopeeReturnVm.EditUserNoteCommand.CanExecute(shopeeRow));
            Assert.False(vm.ShopeeReturnVm.EditNoteCommand.CanExecute(shopeeRow));
            Assert.False(vm.ShopeeReturnVm.ToggleWatchlistCommand.CanExecute(shopeeRow));
            Assert.False(vm.ShopeeReturnVm.ArchiveRowCommand.CanExecute(shopeeRow));
            Assert.False(vm.ShopeeReturnVm.DeleteRowCommand.CanExecute(shopeeRow));
            Assert.False(vm.ShopeeReturnVm.BulkMarkReceivedCommand.CanExecute(null));
            Assert.False(vm.ShopeeReturnVm.BulkArchiveCommand.CanExecute(null));
            Assert.False(vm.ShopeeReturnVm.BulkWatchlistCommand.CanExecute(null));
            Assert.False(vm.ShopeeReturnVm.BulkDeleteCommand.CanExecute(null));
            Assert.False(vm.ShopeeReturnVm.HandleDeleteKeyCommand.CanExecute(null));
            Assert.False(vm.ArchivedOrdersVm.UnarchiveRowCommand.CanExecute(archivedRow));
            Assert.False(vm.ArchivedOrdersVm.DeleteRowCommand.CanExecute(archivedRow));
            Assert.False(vm.DeletedOrdersVm.RestoreRowCommand.CanExecute(deletedRow));
            Assert.False(vm.WatchlistVm.ChangeStatusCommand.CanExecute(null));
            Assert.False(vm.WatchlistVm.EditWatchlistReasonCommand.CanExecute(null));
            Assert.False(vm.WatchlistVm.RemoveFromWatchlistCommand.CanExecute(null));
            Assert.False(vm.ReturnWatchVm.RemoveFromBoardCommand.CanExecute(null));
            Assert.False(vm.ScanReceiveVm.SubmitScanCommand.CanExecute(null));
            Assert.False(vm.FileImportVm.ImportFilesCommand.CanExecute(null));
            Assert.False(vm.FolderWatchVm.RunNowCommand.CanExecute(null));
            Assert.False(vm.FolderWatchVm.RunExistingCommand.CanExecute(null));
            Assert.False(vm.GoogleSheetVm.RunNowCommand.CanExecute(null));
            Assert.False(vm.SaleworkVm.RunNowCommand.CanExecute(null));
            Assert.False(vm.LegacyImportVm.ExecuteImportCommand.CanExecute(null));

            // Gọi trực tiếp các hàm ghi (như phím Del / Enter) cũng không được phép chạy
            vm.ScanReceiveVm.ScanInput = "SPX-TEST";
            WpfTestHost.RunTask(() => vm.ScanReceiveVm.SubmitScanAsync());
            Assert.Equal(0, rig.Scan.Calls);
            WpfTestHost.RunTask(() => vm.ShopeeReturnVm.HandleDeleteKeyAsync());
            Assert.Equal(0, rig.Dialog.Calls);

            // Các lệnh XEM (lọc, tìm kiếm, xem chi tiết, xuất file) vẫn chạy bình thường!
            vm.ShopeeReturnVm.SearchText = shopeeRow.OrderId;
            Assert.NotEmpty(vm.ShopeeReturnVm.VisibleOrders);
            vm.ShopeeReturnVm.ResetAllFiltersCommand.Execute(null);
            Assert.True(vm.ShopeeReturnVm.OpenDetailCommand.CanExecute(shopeeRow));
            Assert.True(vm.ShopeeReturnVm.ShowHistoryCommand.CanExecute(shopeeRow));
            Assert.True(vm.ShopeeReturnVm.ExportVisibleCommand.CanExecute("xlsx"));
            WpfTestHost.RunTask(() => vm.ShopeeReturnVm.ExportVisibleCommand.ExecuteAsync("xlsx"));
            Assert.Equal(1, rig.Export.Calls);

            // Kết nối lại → mở khóa toàn bộ & banner biến mất!
            vm.ShowDeletedUndoToast([("ord-1", "SPX-1")]);
            Assert.False(vm.UndoDeleteCommand.CanExecute(null));
            cloud.Set(CloudState.Connected, lastSync: FixedNow);
            Assert.True(vm.CanWrite);
            Assert.False(vm.CloudVm.ShowReadOnlyBanner);
            Assert.Null(vm.WriteDisabledTooltip);
            Assert.True(vm.UndoDeleteCommand.CanExecute(null));
            Assert.True(vm.NewCreatedVm.CreateNewOrderCommand.CanExecute(null));
            Assert.True(vm.ShopeeReturnVm.ChangeStatusCommand.CanExecute(shopeeRow));
            Assert.True(vm.ShopeeReturnVm.DeleteRowCommand.CanExecute(shopeeRow));
            Assert.True(vm.ArchivedOrdersVm.UnarchiveRowCommand.CanExecute(archivedRow));
            Assert.True(vm.DeletedOrdersVm.RestoreRowCommand.CanExecute(deletedRow));
            Assert.True(vm.WatchlistVm.ChangeStatusCommand.CanExecute(null));
            Assert.True(vm.ReturnWatchVm.RemoveFromBoardCommand.CanExecute(null));
            Assert.True(vm.ScanReceiveVm.SubmitScanCommand.CanExecute(null));
            Assert.True(vm.FileImportVm.ImportFilesCommand.CanExecute(null));
            Assert.True(vm.FolderWatchVm.RunNowCommand.CanExecute(null));
            Assert.True(vm.GoogleSheetVm.RunNowCommand.CanExecute(null));
            Assert.True(vm.SaleworkVm.RunNowCommand.CanExecute(null));
            Assert.True(vm.LegacyImportVm.ExecuteImportCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Pairing_WrongCodeShowsVietnameseError_ValidCodeConnects_AndUnpairRequiresConfirmation()
    {
        WpfTestHost.Run(() =>
        {
            var cloud = new FakeCloudLink();
            var rig = CreateRig(cloud, useRealDispatcher: true);
            using var vm = rig.Vm;

            Assert.Equal(CloudState.Unpaired, vm.CloudVm.State);
            Assert.True(vm.CloudVm.ShowPairForm);
            Assert.False(vm.CloudVm.ShowPairedInfo);

            // Mở Cài đặt > Cloud
            vm.NavigateByKey("cloud");
            Assert.Same(vm.SettingsVm, vm.CurrentPage);
            Assert.Equal(SettingsViewModel.CloudTabKey, vm.SettingsVm.SelectedTabKey);
            Assert.True(vm.SettingsVm.IsCloudTab);

            // Ghép sai mã -> hiện lỗi tiếng Việt, giữ trạng thái Unpaired
            vm.CloudVm.WorkerUrlInput = "https://qlhh.shop.workers.dev";
            vm.CloudVm.PairingCodeInput = "999999";
            vm.CloudVm.DeviceNameInput = "PC Kho 2";
            WpfTestHost.RunTask(() => vm.CloudVm.PairCommand.ExecuteAsync(null));

            Assert.Equal(CloudState.Unpaired, vm.CloudVm.State);
            Assert.True(vm.CloudVm.HasPairError);
            Assert.Contains("Mã ghép không đúng", vm.CloudVm.PairError);

            // Ghép đúng mã -> Connected, hiện thông tin thiết bị
            vm.CloudVm.PairingCodeInput = FakeCloudLink.ValidCode;
            WpfTestHost.RunTask(() => vm.CloudVm.PairCommand.ExecuteAsync(null));

            Assert.Equal(CloudState.Connected, vm.CloudVm.State);
            Assert.True(vm.CanWrite);
            Assert.False(vm.CloudVm.HasPairError);
            Assert.Equal("PC Kho 2", vm.CloudVm.DeviceName);
            Assert.Equal("staff", vm.CloudVm.Role);
            Assert.True(vm.CloudVm.ShowPairedInfo);
            Assert.False(vm.CloudVm.ShowPairForm);

            // Quên ghép: hủy xác nhận -> giữ nguyên
            rig.Confirm.NextResult = false;
            vm.CloudVm.UnpairCommand.Execute(null);
            Assert.Equal(1, rig.Confirm.CallCount);
            Assert.Equal(0, cloud.UnpairCalls);
            Assert.Equal(CloudState.Connected, vm.CloudVm.State);

            // Quên ghép: đồng ý -> gọi Unpair và về Unpaired
            rig.Confirm.NextResult = true;
            vm.CloudVm.UnpairCommand.Execute(null);
            Assert.Equal(2, rig.Confirm.CallCount);
            Assert.Equal(1, cloud.UnpairCalls);
            Assert.Equal(CloudState.Unpaired, vm.CloudVm.State);
            Assert.False(vm.CanWrite);
        });
    }

    [Fact]
    public void Quota_FormatsReadableNumbers_AndShowsYellowWarnAndRedStopWithExactMessage()
    {
        WpfTestHost.Run(() =>
        {
            var cloud = new FakeCloudLink();
            cloud.Set(
                CloudState.Connected,
                quota: new CloudQuota("2026-11-25", 8200, 100000, 5100, 100000, 90000, 5000000, "ok"),
                lastSync: FixedNow,
                role: "staff",
                deviceName: "PC Kho 1",
                baseUrl: "https://qlhh.workers.dev");
            using var vm = CreateRig(cloud).Vm;

            Assert.True(vm.CloudVm.HasQuota);
            Assert.Equal("Green", vm.CloudVm.QuotaColorKey);
            Assert.Equal("8.200 / 100.000 yêu cầu (8%)", vm.CloudVm.RequestsQuotaText);
            Assert.Equal("5.100 / 100.000 hàng ghi (5%)", vm.CloudVm.RowsWrittenQuotaText);
            Assert.Contains("8.200 / 100.000 yêu cầu (8%)", vm.CloudVm.StatusTooltip);
            Assert.False(vm.CloudVm.HasQuotaWarning);

            // Warn (>= 70%)
            cloud.Set(
                CloudState.Connected,
                quota: new CloudQuota("2026-11-25", 74500, 100000, 72000, 100000, 1200000, 5000000, "warn"),
                lastSync: FixedNow);
            Assert.Equal("Yellow", vm.CloudVm.QuotaColorKey);
            Assert.True(vm.CloudVm.IsQuotaWarn);
            Assert.False(vm.CloudVm.IsQuotaStop);
            Assert.True(vm.CloudVm.HasQuotaWarning);
            Assert.Equal("74.500 / 100.000 yêu cầu (75%)", vm.CloudVm.RequestsQuotaText);

            // Stop (>= 98%)
            cloud.Set(
                CloudState.Connected,
                quota: new CloudQuota("2026-11-25", 99100, 100000, 98500, 100000, 4800000, 5000000, "stop"),
                lastSync: FixedNow);
            Assert.Equal("Red", vm.CloudVm.QuotaColorKey);
            Assert.True(vm.CloudVm.IsQuotaStop);
            Assert.Equal("Hết hạn mức hôm nay: thao tác ghi tạm dừng tới 07:00 sáng mai (giờ Việt Nam)", vm.CloudVm.QuotaWarningText);
            Assert.Contains("Hết hạn mức hôm nay: thao tác ghi tạm dừng tới 07:00 sáng mai (giờ Việt Nam)", vm.CloudVm.StatusTooltip);
        });
    }

    [Fact]
    public void RoleOwner_CanGeneratePairingCodeWithCountdown_AndStaffCannot()
    {
        WpfTestHost.Run(() =>
        {
            var now = FixedNow;
            var cloud = new FakeCloudLink();
            cloud.Set(CloudState.Connected, lastSync: now, role: "owner", deviceName: "PC Chủ", baseUrl: "https://qlhh.workers.dev");
            var rig = CreateRig(cloud, clock: () => now);
            using var vm = rig.Vm;

            Assert.True(vm.CloudVm.IsOwner);
            Assert.True(vm.CloudVm.StartPairingCommand.CanExecute(null));

            WpfTestHost.RunTask(() => vm.CloudVm.StartPairingCommand.ExecuteAsync(null));
            Assert.Equal(FakeCloudLink.ValidCode, vm.CloudVm.GeneratedPairCode);
            Assert.True(vm.CloudVm.HasGeneratedPairCode);
            Assert.Contains("Hết hạn sau", vm.CloudVm.GeneratedPairCountdownText);

            // Sao chép mã hoạt động
            vm.CloudVm.CopyGeneratedCodeCommand.Execute(null);
            Assert.Equal(FakeCloudLink.ValidCode, rig.Clip.LastText);

            // Hết 10 phút -> báo hết hạn
            now = DateTime.Now.AddMinutes(11);
            vm.CloudVm.UpdatePairingCountdown();
            Assert.Contains("hết hạn", vm.CloudVm.GeneratedPairCountdownText, StringComparison.OrdinalIgnoreCase);

            // Chuyển sang staff -> ẩn nút tạo mã, không gọi được
            cloud.Set(CloudState.Connected, lastSync: now, role: "staff", deviceName: "PC Nhân viên");
            Assert.False(vm.CloudVm.IsOwner);
            Assert.False(vm.CloudVm.StartPairingCommand.CanExecute(null));
            Assert.False(vm.CloudVm.HasGeneratedPairCode);
        });
    }

    [Fact]
    public void NullCloudLink_KeepsLegacyUiUnchanged_NoStatusDotNoBannerAndCanWriteTrue()
    {
        WpfTestHost.Run(() =>
        {
            using var vm = CreateRig(cloudLink: null).Vm;

            Assert.False(vm.CloudVm.IsEnabled);
            Assert.True(vm.CanWrite);
            Assert.False(vm.CloudVm.ShowReadOnlyBanner);
            Assert.Equal(5, vm.SettingsVm.Tabs.Count);
            Assert.DoesNotContain(vm.SettingsVm.Tabs, t => t.Key == SettingsViewModel.CloudTabKey);

            var window = new MainWindow
            {
                DataContext = vm,
                Width = 1360,
                Height = 820,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowInTaskbar = false,
                ShowActivated = false
            };
            try
            {
                window.Show();
                WpfTestHost.PumpDispatcher(window);

                var statusBtn = Assert.IsType<Button>(window.FindName("CloudStatusButton"));
                var banner = Assert.IsType<Border>(window.FindName("CloudReadOnlyBanner"));
                Assert.Equal(Visibility.Collapsed, statusBtn.Visibility);
                Assert.Equal(Visibility.Collapsed, banner.Visibility);
            }
            finally
            {
                window.Close();
                WpfTestHost.PumpDispatcher();
            }
        });
    }

    [Fact]
    public void RenderScreenshots_CloudStates_ToReportsFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(Directory.Exists(Path.Combine(dir.FullName, "tasks")) && Directory.Exists(Path.Combine(dir.FullName, "v2"))))
        {
            dir = dir.Parent;
        }
        var outDir = dir is null
            ? Path.Combine(Path.GetTempPath(), "T041-screens")
            : Path.Combine(dir.FullName, "tasks", "reports", "T041-screens");
        Directory.CreateDirectory(outDir);

        WpfTestHost.Run(() =>
        {
            var cloud = new FakeCloudLink();
            cloud.Set(
                CloudState.Connected,
                quota: new CloudQuota("2026-11-25", 8200, 100000, 5100, 100000, 90000, 5000000, "ok"),
                lastSync: FixedNow,
                role: "owner",
                deviceName: "Máy tính kho 1",
                baseUrl: "https://qlhh-demo.workers.dev");

            var rig = CreateRig(cloud, useRealDispatcher: true);
            using var vm = rig.Vm;

            var window = new MainWindow
            {
                DataContext = vm,
                Width = 1360,
                Height = 820,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowInTaskbar = false,
                ShowActivated = false
            };

            try
            {
                window.Show();

                // 1. Connected trên bảng Shopee Hoàn hàng
                vm.NavigateByKey("shopee_return");
                WpfTestHost.PumpDispatcher(window);
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "cloud-01-connected-table.png"));

                // 2. Disconnected: chấm đỏ MẤT KẾT NỐI + Banner chỉ xem + nút khóa mờ
                cloud.Set(CloudState.Disconnected, error: "Không thể kết nối tới Cloudflare Worker (timeout).", lastSync: new DateTime(2026, 11, 25, 14, 22, 0));
                vm.NavigateByKey("new_orders");
                WpfTestHost.PumpDispatcher(window);
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "cloud-02-disconnected-banner-locked.png"));

                // 3. Popup chi tiết trạng thái Cloud
                vm.CloudVm.TogglePopupCommand.Execute(null);
                WpfTestHost.PumpDispatcher(window);
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "cloud-03-status-popup.png"));
                vm.CloudVm.ClosePopup();

                // 4. Màn Quét nhận hàng bị khóa khi mất kết nối
                vm.NavigateByKey("scan_receive");
                WpfTestHost.PumpDispatcher(window);
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "cloud-04-scan-locked.png"));

                // 5. Cài đặt > Cloud: trạng thái Chưa ghép thiết bị (biểu mẫu ghép)
                cloud.Unpair();
                vm.CloudVm.WorkerUrlInput = "https://qlhh-demo.workers.dev";
                vm.CloudVm.PairingCodeInput = "999999";
                WpfTestHost.RunTask(() => vm.CloudVm.PairCommand.ExecuteAsync(null));
                vm.NavigateByKey("cloud");
                WpfTestHost.PumpDispatcher(window);
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "cloud-05-settings-cloud-unpaired.png"));

                // 6. Cài đặt > Cloud: Owner + mã ghép 6 số + đếm ngược + hạn mức vàng (warn)
                cloud.Set(
                    CloudState.Connected,
                    error: null,
                    quota: new CloudQuota("2026-11-25", 76400, 100000, 72500, 100000, 1400000, 5000000, "warn"),
                    lastSync: FixedNow,
                    role: "owner",
                    deviceName: "Máy tính kho 1",
                    baseUrl: "https://qlhh-demo.workers.dev");
                WpfTestHost.RunTask(() => vm.CloudVm.StartPairingCommand.ExecuteAsync(null));
                WpfTestHost.PumpDispatcher(window);
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "cloud-06-settings-cloud-owner-warn.png"));

                // 7. Cài đặt > Cloud: Hạn mức đỏ (stop)
                cloud.Set(
                    CloudState.Connected,
                    quota: new CloudQuota("2026-11-25", 99400, 100000, 98900, 100000, 4900000, 5000000, "stop"),
                    lastSync: FixedNow);
                WpfTestHost.PumpDispatcher(window);
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "cloud-07-settings-cloud-quota-stop.png"));

                // 8. Thiết bị đã bị thu hồi (Dark theme)
                vm.SetTheme(ThemeService.DarkTheme);
                cloud.Set(CloudState.Revoked, error: "Token thiết bị đã bị chủ cửa hàng thu hồi.", lastSync: new DateTime(2026, 11, 25, 13, 40, 0));
                WpfTestHost.PumpDispatcher(window);
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "cloud-08-revoked-dark.png"));
            }
            finally
            {
                window.Close();
                WpfTestHost.PumpDispatcher();
            }
        });

        Assert.True(File.Exists(Path.Combine(outDir, "cloud-01-connected-table.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "cloud-02-disconnected-banner-locked.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "cloud-06-settings-cloud-owner-warn.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "cloud-08-revoked-dark.png")));
    }
}
