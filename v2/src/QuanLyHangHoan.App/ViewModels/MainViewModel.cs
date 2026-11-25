using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.FolderWatch;
using QuanLyHangHoan.Data.Sheet;
using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IReturnOrderProvider _returnOrderProvider;
    private readonly INewOrderProvider _newOrderProvider;
    private readonly IArchivedOrderProvider _archivedOrderProvider;
    private readonly IDeletedOrderProvider _deletedOrderProvider;
    private readonly IArchiveSweeper? _archiveSweeper;
    private readonly IPhotoProvider? _photoProvider;
    private readonly IPhotoViewerService? _photoViewerService;
    private readonly ISyncController _syncController;
    private readonly IFolderWatchController _folderWatchController;
    private readonly ISheetSyncController _sheetSyncController;
    private readonly ISaleworkController _saleworkController;
    private readonly IFileImportController _fileImportController;
    private readonly IUiSettingsStore _uiSettingsStore;
    private readonly IOrderEditService _orderEditService;
    private readonly IOrderDetailService _orderDetailService;
    private readonly IReturnWatchService _returnWatchService;
    private readonly IScanSoundPlayer _soundPlayer;
    private readonly IAutoStartService _autoStartService;
    private readonly IBackupController _backupController;
    private readonly IOrderExportService _orderExportService;
    private readonly IActivityLogProvider _activityLogProvider;
    private readonly ITrayService _trayService;
    private readonly INotificationService _notificationService;
    private readonly AlertNotificationMonitor _alertMonitor;
    private readonly WriteGate _writeGate = new();
    private readonly Action<Action> _dispatchToUi;
    private readonly TimeSpan _toastDuration;
    private readonly TimeSpan _syncReloadDebounce;
    private readonly TimeSpan _searchDebounce;
    private readonly Dictionary<string, ObservableObject> _pageCache = new();
    private readonly HashSet<string> _collapsedGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenSyncHandledKeys = new(StringComparer.Ordinal);
    private readonly object _syncReloadGate = new();
    private readonly object _globalSearchGate = new();
    private readonly NavigationItem _homeNavItem;
    private readonly NavigationItem _shopeeReturnNavItem;
    private readonly NavigationItem _shopeeCancelledNavItem;
    private readonly NavigationItem _tiktokReturnNavItem;
    private readonly NavigationItem _scanReceiveNavItem;
    private readonly NavigationItem _phoneSyncNavItem;
    private readonly NavigationItem _returnWatchNavItem;
    private readonly NavigationItem _newOrdersNavItem;
    private readonly NavigationItem _watchlistNavItem;
    private readonly NavigationItem _archiveNavItem;
    private readonly NavigationItem _deletedNavItem;
    private readonly NavigationItem _fileImportNavItem;
    private readonly NavigationItem _folderWatchNavItem;
    private readonly NavigationItem _googleSheetNavItem;
    private readonly NavigationItem _saleworkNavItem;
    private CancellationTokenSource? _periodicSweepCts;
    private CancellationTokenSource? _toastCts;
    private CancellationTokenSource? _syncReloadCts;
    private CancellationTokenSource? _globalSearchCts;
    private IReadOnlyList<Core.Models.Order> _searchActiveOrders = Array.Empty<Core.Models.Order>();
    private IReadOnlyList<Core.Models.Order> _searchArchivedOrders = Array.Empty<Core.Models.Order>();
    private IReadOnlyList<DeletedOrderRecord> _searchDeletedOrders = Array.Empty<DeletedOrderRecord>();
    private IReadOnlyList<string> _pendingUndoOrderIds = Array.Empty<string>();
    private bool _pendingUndoIsReturnWatch;
    private int _sweepRunning;

    public MainViewModel(
        FakeDataService fakeDataService,
        ILegacyImporter? legacyImporter = null,
        IFolderPickerService? folderPickerService = null,
        IFileSaveService? fileSaveService = null)
        : this(
            fakeDataService,
            fakeDataService,
            fakeDataService,
            fakeDataService,
            legacyImporter ?? new FakeLegacyImporter(fakeDataService.GetCurrentTime),
            folderPickerService,
            fileSaveService,
            isDemoMode: true)
    {
    }

    public MainViewModel(
        IReturnOrderProvider returnOrderProvider,
        INewOrderProvider newOrderProvider,
        IArchivedOrderProvider archivedOrderProvider,
        IDeletedOrderProvider deletedOrderProvider,
        ILegacyImporter legacyImporter,
        IFolderPickerService? folderPickerService = null,
        IFileSaveService? fileSaveService = null,
        bool isDemoMode = false,
        IArchiveSweeper? archiveSweeper = null,
        IPhotoProvider? photoProvider = null,
        IPhotoViewerService? photoViewerService = null,
        bool deferInitialLoad = false,
        IScanService? scanService = null,
        IScanSoundPlayer? soundPlayer = null,
        IImageFilePickerService? imagePicker = null,
        ISyncController? syncController = null,
        IClipboardService? clipboardService = null,
        IConfirmationService? confirmationService = null,
        Action<Action>? dispatchToUi = null,
        IUiSettingsStore? uiSettingsStore = null,
        IOrderEditService? orderEditService = null,
        IOrderActionDialogService? orderActionDialogService = null,
        TimeSpan? toastDuration = null,
        IFolderWatchController? folderWatchController = null,
        ISheetSyncController? sheetSyncController = null,
        ISaleworkController? saleworkController = null,
        IFileImportController? fileImportController = null,
        IImportFilePickerService? importFilePickerService = null,
        IOrderDetailService? orderDetailService = null,
        TimeSpan? syncReloadDebounce = null,
        TimeSpan? searchDebounce = null,
        IReturnWatchService? returnWatchService = null,
        IAutoStartService? autoStartService = null,
        ITrayService? trayService = null,
        INotificationService? notificationService = null,
        IAlertNotifyStateStore? alertNotifyStateStore = null,
        TimeSpan? alertInitialDelay = null,
        TimeSpan? alertCheckInterval = null,
        IBackupController? backupController = null,
        IActivityLogProvider? activityLogProvider = null,
        IOrderExportService? orderExportService = null,
        IDiagnosticsProvider? diagnosticsProvider = null,
        NotificationCenter? notificationCenter = null,
        INewOrderCreator? newOrderCreator = null,
        INewOrderDialogService? newOrderDialogService = null,
        QuanLyHangHoan.Data.Ai.AiSettingsStore? aiSettingsStore = null,
        Func<IReadOnlyList<QuanLyHangHoan.Data.StoredOrder>>? aiOrderLoader = null,
        Func<int, string, string, IReadOnlyList<QuanLyHangHoan.Data.AuditEntry>>? aiActivityLoader = null,
        Func<IEnumerable<QuanLyHangHoan.Data.StoredOrder>, IReadOnlyDictionary<string, (long Since, int Stage)>>? aiReturnWatch = null,
        ICloudLink? cloudLink = null)
    {
        _notificationCenter = notificationCenter ?? new NotificationCenter(new InMemoryNotificationStore(), returnOrderProvider.GetCurrentTime);
        _returnOrderProvider = returnOrderProvider;
        _newOrderProvider = newOrderProvider;
        _archivedOrderProvider = archivedOrderProvider;
        _deletedOrderProvider = deletedOrderProvider;
        _archiveSweeper = archiveSweeper;
        _photoProvider = photoProvider;
        _photoViewerService = photoViewerService;
        _uiSettingsStore = uiSettingsStore ?? new UiSettingsStore(null, isDemoMode: true);
        _dispatchToUi = dispatchToUi ?? DefaultDispatchToUi;
        _toastDuration = toastDuration ?? TimeSpan.FromSeconds(8);
        _syncReloadDebounce = syncReloadDebounce ?? TimeSpan.FromMilliseconds(500);
        _searchDebounce = searchDebounce ?? TimeSpan.FromMilliseconds(250);
        _orderEditService = orderEditService ?? (returnOrderProvider is FakeDataService fakeSvc
            ? new FakeOrderEditService(fakeSvc)
            : new FakeOrderEditService(new FakeDataService(enableMutableDemoStore: true)));
        var dialogSvc = orderActionDialogService ?? new WpfOrderActionDialogService();
        _orderDetailService = orderDetailService ?? new WindowsOrderDetailService(
            photoProvider,
            photoViewerService,
            _orderEditService,
            clipboardService,
            returnOrderProvider.GetCurrentTime);
        _syncController = syncController ?? new FakeSyncController();
        _syncController.Handled += OnSyncHandled;
        _folderWatchController = folderWatchController ?? new FakeFolderWatchController();
        _folderWatchController.Completed += OnFolderWatchCompleted;
        _folderWatchController.ConfigChanged += OnFolderWatchConfigChanged;
        _sheetSyncController = sheetSyncController ?? new FakeSheetSyncController();
        _sheetSyncController.Completed += OnSheetSyncCompleted;
        _sheetSyncController.ConfigChanged += OnSheetSyncConfigChanged;
        _saleworkController = saleworkController ?? new FakeSaleworkController();
        _saleworkController.Completed += OnSaleworkCompleted;
        _saleworkController.StateChanged += OnSaleworkStateChanged;
        _fileImportController = fileImportController ?? new FakeFileImportController();
        _fileImportController.Completed += OnFileImportCompleted;
        _returnWatchService = returnWatchService ?? new FakeReturnWatchService();
        _soundPlayer = soundPlayer ?? new NoOpScanSoundPlayer();
        _autoStartService = autoStartService ?? new FakeAutoStartService();
        _trayService = trayService ?? new FakeTrayService();
        _notificationService = notificationService ?? new FakeNotificationService(returnOrderProvider.GetCurrentTime);
        var resolvedAlertStateStore = alertNotifyStateStore ?? new InMemoryAlertNotifyStateStore();
        _orderExportService = orderExportService ?? new FakeOrderExportService();
        _alertMonitor = new AlertNotificationMonitor(
            returnOrderProvider,
            resolvedAlertStateStore,
            _notificationService,
            _trayService,
            () => NotifyAlerts,
            returnOrderProvider.GetCurrentTime,
            alertInitialDelay,
            alertCheckInterval);
        _trayService.ToggleNotifyAlertsRequested += OnTrayToggleNotifyAlertsRequested;
        _alertMonitor.NewAlerts += OnNewAlerts;
        IsDemoMode = isDemoMode;
        NotificationsVm = new NotificationCenterViewModel(
            _notificationCenter, returnOrderProvider.GetCurrentTime, OpenNotificationTarget, a => _dispatchToUi(a))
        {
            IsDemoMode = isDemoMode
        };
        if (isDemoMode && notificationCenter is null) SeedDemoNotifications();

        var resolvedScanService = scanService ?? new FakeScanService(
            isDemoMode ? returnOrderProvider.GetReturnOrders(40) : Array.Empty<Core.Models.Order>(),
            returnOrderProvider.GetCurrentTime);

        HomeVm = new HomeViewModel(
            returnOrderProvider,
            fileSaveService,
            AlertThresholds.MainPolicy,
            onOpenOrder: OpenOrderFromDashboard,
            onNavigateLegacyImport: () => NavigateByKey("legacy_import"))
        {
            IsDemoMode = isDemoMode
        };

        _writeGate.Changed += OnWriteGateChanged;
        CloudVm = new CloudStatusViewModel(
            cloudLink,
            _writeGate,
            confirmationService,
            clipboardService,
            _dispatchToUi,
            returnOrderProvider.GetCurrentTime,
            openCloudSettings: () => NavigateByKey(SettingsViewModel.CloudTabKey),
            onNotify: ShowToast);
        CloudVm.PropertyChanged += OnCloudVmPropertyChanged;
        NotificationsVm.PropertyChanged += OnNotificationsVmPropertyChanged;

        _activityLogProvider = activityLogProvider ?? new FakeActivityLogProvider();
        ActivityLogVm = new ActivityLogViewModel(_activityLogProvider, clipboardService) { IsDemoMode = isDemoMode };
        _backupController = backupController ?? new FakeBackupController(returnOrderProvider.GetCurrentTime);
        SettingsVm = new SettingsViewModel(
            _backupController,
            clipboardService,
            () => _syncController.Room,
            returnOrderProvider.GetCurrentTime,
            onBackgroundSettingChanged: OnSettingsBackgroundSettingChanged,
            onBackupSettingChanged: OnSettingsBackupSettingChanged,
            openPhoneSync: () => NavigateByKey("phone_sync"),
            cloud: CloudVm)
        {
            IsDemoMode = isDemoMode
        };
        AiChatVm = new AiChatViewModel(aiSettingsStore, aiOrderLoader, returnOrderProvider.GetCurrentTime, OpenOrderDetail,
            openSettings: () => { SettingsVm.SelectTab(SettingsViewModel.GeneralTabKey); NavigateByKey("settings"); },
            activityLoader: aiActivityLoader,
            statusProvider: BuildAiStatus,
            returnWatchProvider: aiReturnWatch,
            notificationsProvider: BuildAiNotices);
        SettingsVm.AiChat = AiChatVm;
        HelpVm = new HelpViewModel(
            diagnosticsProvider ?? new FakeDiagnosticsProvider(),
            _backupController,
            clipboardService,
            BuildHelpStepsAsync,
            BuildIntegrationStatusText,
            key => NavigateByKey(key))
        {
            IsDemoMode = isDemoMode
        };

        ShopeeReturnVm = new ReturnOrdersViewModel(
            returnOrderProvider,
            ReturnTableScope.ShopeeReturn,
            onOpenPhoto: OpenOrderPhoto,
            editService: _orderEditService,
            dialogService: dialogSvc,
            onDataChangedAsync: ReloadAllDataAsync,
            onOrdersDeleted: ShowDeletedUndoToast,
            onNotify: ShowToast,
            onOpenDetail: OpenOrderDetail,
            exportService: _orderExportService,
            loadInitialData: !deferInitialLoad,
            writeGate: _writeGate) { IsDemoMode = isDemoMode };
        ShopeeCancelledVm = new ReturnOrdersViewModel(
            returnOrderProvider,
            ReturnTableScope.ShopeeCancelled,
            onOpenPhoto: OpenOrderPhoto,
            editService: _orderEditService,
            dialogService: dialogSvc,
            onDataChangedAsync: ReloadAllDataAsync,
            onOrdersDeleted: ShowDeletedUndoToast,
            onNotify: ShowToast,
            onOpenDetail: OpenOrderDetail,
            exportService: _orderExportService,
            loadInitialData: !deferInitialLoad,
            writeGate: _writeGate) { IsDemoMode = isDemoMode };
        TikTokReturnVm = new ReturnOrdersViewModel(
            returnOrderProvider,
            ReturnTableScope.TikTokReturn,
            onOpenPhoto: OpenOrderPhoto,
            editService: _orderEditService,
            dialogService: dialogSvc,
            onDataChangedAsync: ReloadAllDataAsync,
            onOrdersDeleted: ShowDeletedUndoToast,
            onNotify: ShowToast,
            onOpenDetail: OpenOrderDetail,
            exportService: _orderExportService,
            loadInitialData: !deferInitialLoad,
            writeGate: _writeGate) { IsDemoMode = isDemoMode };

        NewOrdersVm = new NewOrdersViewModel(newOrderProvider, onOpenDetail: OpenOrderDetail, loadInitialData: !deferInitialLoad) { IsDemoMode = isDemoMode };   // bảng cũ (không còn trên thanh bên)
        // "Bảng đơn mới" thật: cửa sổ lọc các đơn tạo tay/quét tạo từ điện thoại, có đủ thao tác của các bảng khác + nút "Tạo đơn mới".
        var resolvedCreator = newOrderCreator ?? (returnOrderProvider is FakeDataService fakeForCreate ? new FakeNewOrderCreator(fakeForCreate) : null);
        NewCreatedVm = new ReturnOrdersViewModel(
            returnOrderProvider,
            ReturnTableScope.NewCreated,
            onOpenPhoto: OpenOrderPhoto,
            editService: _orderEditService,
            dialogService: dialogSvc,
            onDataChangedAsync: ReloadAllDataAsync,
            onOrdersDeleted: ShowDeletedUndoToast,
            onNotify: ShowToast,
            onOpenDetail: OpenOrderDetail,
            exportService: _orderExportService,
            newOrderCreator: resolvedCreator,
            newOrderDialog: newOrderDialogService ?? new FakeNewOrderDialogService(),
            loadInitialData: !deferInitialLoad,
            writeGate: _writeGate) { IsDemoMode = isDemoMode };
        Func<string, string, bool> returnWatchConfirm = confirmationService is not null
            ? confirmationService.Confirm
            : (title, message) =>
            {
                var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                            ?? Application.Current?.MainWindow;
                var dlg = new Views.ConfirmActionWindow(title, message, "Bỏ khỏi bảng", isDanger: true) { Owner = owner };
                return dlg.ShowDialog() == true;
            };
        ReturnWatchVm = new ReturnWatchViewModel(
            _returnWatchService,
            returnOrderProvider.GetCurrentTime,
            confirmDialog: returnWatchConfirm,
            onDataChangedAsync: ReloadAllDataAsync,
            onOrdersRemoved: ShowReturnWatchUndoToast,
            onNotify: ShowToast,
            onOpenDetail: OpenOrderDetail,
            onGoToMainTable: GoToMainTableFromOrder,
            writeGate: _writeGate)
        {
            IsDemoMode = isDemoMode
        };
        WatchlistVm = new WatchlistViewModel(
            editService: _orderEditService,
            dialogService: dialogSvc,
            onDataChangedAsync: ReloadAllDataAsync,
            onNotify: ShowToast,
            onOpenDetail: OpenOrderDetail,
            writeGate: _writeGate) { IsDemoMode = isDemoMode };
        ArchivedOrdersVm = new ArchivedOrdersViewModel(
            archivedOrderProvider,
            onOpenPhoto: OpenOrderPhoto,
            editService: _orderEditService,
            dialogService: dialogSvc,
            onDataChangedAsync: ReloadAllDataAsync,
            onOrdersDeleted: ShowDeletedUndoToast,
            onNotify: ShowToast,
            onOpenDetail: OpenOrderDetail,
            exportService: _orderExportService,
            loadInitialData: !deferInitialLoad,
            writeGate: _writeGate) { IsDemoMode = isDemoMode };
        DeletedOrdersVm = new DeletedOrdersViewModel(
            deletedOrderProvider,
            editService: _orderEditService,
            dialogService: dialogSvc,
            onDataChangedAsync: ReloadAllDataAsync,
            onNotify: ShowToast,
            onOpenDetail: OpenOrderDetail,
            loadInitialData: !deferInitialLoad,
            writeGate: _writeGate) { IsDemoMode = isDemoMode };
        StatisticsVm = new StatisticsViewModel(returnOrderProvider, archivedOrderProvider, fileSaveService, loadInitialData: !deferInitialLoad) { IsDemoMode = isDemoMode };
        LegacyImportVm = new LegacyImportViewModel(legacyImporter, folderPickerService, isDemoMode, writeGate: _writeGate);
        LegacyImportVm.ImportSucceeded += OnLegacyImportSucceededAsync;
        ScanReceiveVm = new ScanReceiveViewModel(
            resolvedScanService,
            soundPlayer,
            imagePicker,
            isDemoMode: isDemoMode,
            writeGate: _writeGate);
        ScanReceiveVm.ScanReceivedSucceeded += OnScanReceivedSucceededAsync;
        PhoneSyncVm = new PhoneSyncViewModel(
            _syncController,
            clipboardService,
            confirmationService,
            _dispatchToUi);
        FolderWatchVm = new FolderWatchViewModel(
            _folderWatchController,
            folderPickerService,
            confirmationService,
            _dispatchToUi,
            returnOrderProvider.GetCurrentTime,
            writeGate: _writeGate);
        GoogleSheetVm = new GoogleSheetViewModel(
            _sheetSyncController,
            clipboardService,
            _dispatchToUi,
            returnOrderProvider.GetCurrentTime,
            writeGate: _writeGate);
        SaleworkVm = new SaleworkViewModel(
            _saleworkController,
            confirmationService,
            _dispatchToUi,
            returnOrderProvider.GetCurrentTime,
            writeGate: _writeGate);
        FileImportVm = new FileImportViewModel(
            _fileImportController,
            importFilePickerService,
            confirmationService,
            _dispatchToUi,
            writeGate: _writeGate);

        _pageCache["home"] = HomeVm;
        _pageCache["shopee_return"] = ShopeeReturnVm;
        _pageCache["shopee_cancelled"] = ShopeeCancelledVm;
        _pageCache["tiktok_return"] = TikTokReturnVm;
        _pageCache["scan_receive"] = ScanReceiveVm;
        _pageCache["phone_sync"] = PhoneSyncVm;
        _pageCache["file_import"] = FileImportVm;
        _pageCache["folder_watch"] = FolderWatchVm;
        _pageCache["google_sheet"] = GoogleSheetVm;
        _pageCache["salework"] = SaleworkVm;
        _pageCache["return_watch"] = ReturnWatchVm;
        _pageCache["new_orders"] = NewCreatedVm;
        _pageCache["watchlist"] = WatchlistVm;
        _pageCache["archive"] = ArchivedOrdersVm;
        _pageCache["deleted"] = DeletedOrdersVm;
        _pageCache["stats"] = StatisticsVm;
        _pageCache["legacy_import"] = LegacyImportVm;
        _pageCache["settings"] = SettingsVm;
        SettingsVm.AttachImportPages(k => k switch { "file_import" => FileImportVm, "folder_watch" => FolderWatchVm, "google_sheet" => GoogleSheetVm, "salework" => SaleworkVm, _ => null });
        _pageCache["help"] = HelpVm;
        _pageCache["activity"] = ActivityLogVm;

        _homeNavItem = new NavigationItem(
            "home", "🏠", "Trang chủ", string.Empty, () => HomeVm);
        _shopeeReturnNavItem = new NavigationItem(
            "shopee_return", "🟠", "Hoàn hàng", "SHOPEE", () => ShopeeReturnVm, ShopeeReturnVm.TotalCount.ToString("N0"), showSectionHeader: true);
        _shopeeCancelledNavItem = new NavigationItem(
            "shopee_cancelled", "🚫", "Đơn hủy", "SHOPEE", () => ShopeeCancelledVm, ShopeeCancelledVm.TotalCount.ToString("N0"));
        _tiktokReturnNavItem = new NavigationItem(
            "tiktok_return", "⚫", "Hoàn hàng", "TIKTOK", () => TikTokReturnVm, TikTokReturnVm.TotalCount.ToString("N0"), showSectionHeader: true);
        _scanReceiveNavItem = new NavigationItem(
            "scan_receive", "📷", "Quét nhận hàng", "CHUNG", () => ScanReceiveVm, showSectionHeader: true);
        _phoneSyncNavItem = new NavigationItem(
            "phone_sync", "📱", "Đồng bộ điện thoại", "CHUNG", () => PhoneSyncVm);
        _returnWatchNavItem = new NavigationItem(
            "return_watch", "🚚", "Hàng hoàn về", "CHUNG", () => ReturnWatchVm, ReturnWatchVm.TrackingCount.ToString("N0"));
        _newOrdersNavItem = new NavigationItem(
            "new_orders", "📥", "Bảng đơn mới", "CHUNG", () => NewCreatedVm, NewCreatedVm.TotalCount.ToString("N0"));
        _watchlistNavItem = new NavigationItem(
            "watchlist", "⭐", "Đang theo dõi", "CHUNG", () => WatchlistVm, WatchlistVm.ActiveWatchlistCount.ToString("N0"));
        _archiveNavItem = new NavigationItem(
            "archive", "📦", "Kho lưu trữ", "CHUNG", () => ArchivedOrdersVm, ArchivedOrdersVm.TotalCount.ToString("N0"));
        _deletedNavItem = new NavigationItem(
            "deleted", "🗑️", "Đã xóa", "CHUNG", () => DeletedOrdersVm, DeletedOrdersVm.TotalCount.ToString("N0"));
        _fileImportNavItem = new NavigationItem(
            "file_import", "📄", "Nạp file Excel", "NẠP ĐƠN TỰ ĐỘNG", () => FileImportVm, showSectionHeader: true);
        _folderWatchNavItem = new NavigationItem(
            "folder_watch", "📁", "Theo dõi thư mục", "NẠP ĐƠN TỰ ĐỘNG", () => FolderWatchVm);
        _googleSheetNavItem = new NavigationItem(
            "google_sheet", "📊", "Google Sheet", "NẠP ĐƠN TỰ ĐỘNG", () => GoogleSheetVm);
        _saleworkNavItem = new NavigationItem(
            "salework", "🛒", "Salework", "NẠP ĐƠN TỰ ĐỘNG", () => SaleworkVm);

        NavigationItems =
        [
            _homeNavItem,
            _shopeeReturnNavItem,
            _shopeeCancelledNavItem,
            _tiktokReturnNavItem,
            _scanReceiveNavItem,
            _phoneSyncNavItem,
            _returnWatchNavItem,
            _newOrdersNavItem,
            _watchlistNavItem,
            _archiveNavItem,
            _deletedNavItem,
            new NavigationItem("stats", "📊", "Thống kê đơn & sản phẩm", "CHUNG", () => StatisticsVm),
            new NavigationItem("legacy_import", "📂", "Nhập dữ liệu cũ", "CHUNG", () => LegacyImportVm),
            new NavigationItem("activity", "🧾", "Nhật ký hoạt động", "CHUNG", () => ActivityLogVm),
            new NavigationItem("settings", "⚙️", "Cài đặt", "CHUNG", () => SettingsVm),
            new NavigationItem("help", "❓", "Trợ giúp", "CHUNG", () => HelpVm)
        ];

        ShopeeReturnVm.CountsUpdated += SyncSidebarKpis;
        ShopeeCancelledVm.CountsUpdated += SyncSidebarKpis;
        TikTokReturnVm.CountsUpdated += SyncSidebarKpis;
        WatchlistVm.CountsUpdated += SyncSidebarKpis;
        ReturnWatchVm.CountsUpdated += SyncSidebarKpis;

        var uiSettings = _uiSettingsStore.Load();
        foreach (var g in uiSettings.CollapsedNavGroups ?? Enumerable.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(g)) _collapsedGroups.Add(g);
        }

        CurrentTheme = ThemeService.NormalizeTheme(uiSettings.Theme);
        ThemeService.ApplyTheme(CurrentTheme);

        _autoStart = uiSettings.AutoStart;
        _minimizeToTray = uiSettings.MinimizeToTray;
        _notifyAlerts = uiSettings.NotifyAlerts;
        _sounds = uiSettings.Sounds;
        ScanReceiveVm.IsSoundEnabled = _sounds;
        _trayService.SetNotifyAlertsEnabled(_notifyAlerts);
        SettingsVm.SyncBackgroundSettings(_autoStart, _minimizeToTray, _notifyAlerts, _sounds);
        SettingsVm.SyncBackupSettings(uiSettings.BackupEnabled, uiSettings.BackupEveryHours, uiSettings.BackupKeepDays);
        _backupController.ApplySchedule(uiSettings.BackupEnabled && !isDemoMode, uiSettings.BackupEveryHours, uiSettings.BackupKeepDays);

        // Khởi động vào màn hình lần trước (nếu hợp lệ), không thì về Trang chủ
        var initialKey = string.IsNullOrWhiteSpace(uiSettings.LastPage) ? "home" : uiSettings.LastPage;
        var initialItem = NavigationItems.FirstOrDefault(n => n.Key.Equals(initialKey, StringComparison.OrdinalIgnoreCase))
                          ?? _homeNavItem;
        SelectNavigation(initialItem, persistLastPage: false);
        SyncSidebarKpis();
        if (!deferInitialLoad)
        {
            var initialNow = returnOrderProvider.GetCurrentTime();
            _searchActiveOrders = returnOrderProvider.GetReturnOrders(40);
            _searchArchivedOrders = archivedOrderProvider.GetArchivedOrders();
            _searchDeletedOrders = deletedOrderProvider.GetDeletedOrders();
            HomeVm.LoadData(_searchActiveOrders, initialNow);
            WatchlistVm.LoadOrders(_searchActiveOrders, _searchArchivedOrders, initialNow);
            var rwEntries = _returnWatchService.LoadAsync(initialNow).GetAwaiter().GetResult();
            ReturnWatchVm.LoadEntries(rwEntries, initialNow);
            HomeVm.ReturnWatchOnTheWayCount = ReturnWatchVm.Stage2Count;
        }
    }

    public bool IsDemoMode { get; }
    public WriteGate WriteGate => _writeGate;
    public bool CanWrite => _writeGate.CanWrite;
    public string? WriteDisabledTooltip => _writeGate.TooltipReason;
    public CloudStatusViewModel CloudVm { get; }
    public ITrayService TrayService => _trayService;
    public INotificationService NotificationService => _notificationService;
    public HomeViewModel HomeVm { get; }
    public ReturnOrdersViewModel ShopeeReturnVm { get; }
    public ReturnOrdersViewModel ShopeeCancelledVm { get; }
    public ReturnOrdersViewModel TikTokReturnVm { get; }
    public ReturnWatchViewModel ReturnWatchVm { get; }
    public NewOrdersViewModel NewOrdersVm { get; }
    public WatchlistViewModel WatchlistVm { get; }
    public ArchivedOrdersViewModel ArchivedOrdersVm { get; }
    public DeletedOrdersViewModel DeletedOrdersVm { get; }
    public StatisticsViewModel StatisticsVm { get; }
    public LegacyImportViewModel LegacyImportVm { get; }
    public SettingsViewModel SettingsVm { get; }
    public ActivityLogViewModel ActivityLogVm { get; }
    public HelpViewModel HelpVm { get; }
    public AiChatViewModel AiChatVm { get; }
    public ReturnOrdersViewModel NewCreatedVm { get; }
    public NotificationCenterViewModel NotificationsVm { get; }

    /// <summary>Thanh bên mặc định thu gọn (chỉ biểu tượng); rê chuột vào thì mở rộng đè lên nội dung.</summary>
    [ObservableProperty] private bool _isSidebarExpanded;
    private readonly NotificationCenter _notificationCenter;
    private Timer? _syncHealthTimer;

    /// <summary>Bấm thông báo: "search:&lt;mã&gt;" mở tìm kiếm đơn, còn lại là khóa màn hình.</summary>
    private void OpenNotificationTarget(string target)
    {
        const string prefix = "search:";
        if (target.StartsWith(prefix, StringComparison.Ordinal)) GlobalSearchText = target[prefix.Length..];
        else NavigateByKey(target);
    }

    private void OnNewAlerts(IReadOnlyList<NewAlertItem> items, string summary)
    {
        if (items.Count == 0) return;
        if (items.Count > 10)
        {
            var red = items.Count(i => i.Level == QuanLyHangHoan.Core.Alerts.AlertLevel.Red);
            _notificationCenter.Add(red > 0 ? NotificationLevel.Red : NotificationLevel.Yellow, "alert", "Cảnh báo hàng hoàn", summary, "returns");
            return;
        }
        foreach (var i in items)
        {
            var red = i.Level == QuanLyHangHoan.Core.Alerts.AlertLevel.Red;
            _notificationCenter.Add(
                red ? NotificationLevel.Red : NotificationLevel.Yellow, "alert:" + i.Code + (red ? ":r" : ":y"),
                red ? "Đơn quá 24h chưa nhận" : "Đơn quá 12h chưa nhận",
                string.IsNullOrWhiteSpace(i.Platform) ? i.Code : $"{i.Code} ({i.Platform})", "search:" + i.Code);
        }
    }

    private void SeedDemoNotifications()
    {
        var c = _notificationCenter;
        c.Add(NotificationLevel.Red, "alert:demo1:r", "Đơn quá 24h chưa nhận", "SPX-DEMO-001 (Shopee)", "search:SPX-DEMO-001");
        c.Add(NotificationLevel.Yellow, "alert:demo2:y", "Đơn quá 12h chưa nhận", "TT-DEMO-003 (TikTok Shop)", "search:TT-DEMO-003");
        c.Add(NotificationLevel.Success, "phone", "Điện thoại tạo đơn mới", "SPX-DEMO-NEW-01 Shopee", "search:SPX-DEMO-NEW-01");
        c.Add(NotificationLevel.Success, "file", "Đã nạp (file Excel)", "Thêm 12, cập nhật 3", "file_import");
        c.Add(NotificationLevel.Yellow, "file:amb", "Có 2 đơn nhập nhằng (file Excel)", "Giữ nguyên để bạn kiểm tra, chưa gộp.", "file_import");
        c.Add(NotificationLevel.Red, "sheet:err", "Lỗi Google Sheet", "Không tải được bảng tính. Dữ liệu không bị đổi.", "google_sheet");
        c.Add(NotificationLevel.Info, "phone", "Điện thoại cập nhật ghi chú", "SPX-DEMO-004", "search:SPX-DEMO-004");
    }

    /// <summary>Kiểm tra sức khỏe kết nối đồng bộ (gọi mỗi 30 giây ở bản thật; test gọi trực tiếp).</summary>
    public void CheckSyncHealthOnce()
        => _notificationCenter.ReportConnection(_syncController.SyncEnabled, _syncController.ConnectedBrokers > 0 || _syncController.NtfyConnected);

    private void AddSyncNotification(SyncHandled item, bool isSuccess, bool isNewOrder)
    {
        var code = item.Code ?? "";
        var stage = item.Stage ?? "";
        var target = string.IsNullOrWhiteSpace(code) ? "phone_sync" : "search:" + code;
        if (string.Equals(stage, "photo", StringComparison.OrdinalIgnoreCase) && !isNewOrder) return;
        if (isSuccess && isNewOrder)
            _notificationCenter.Add(NotificationLevel.Success, "phone", "Điện thoại tạo đơn mới", $"{code} {ExtractPlatformFromMessage(item.Message)}".Trim(), target);
        else if (isSuccess && string.Equals(stage, "delete", StringComparison.OrdinalIgnoreCase))
            _notificationCenter.Add(NotificationLevel.Info, "phone", "Điện thoại xóa đơn", $"{code} (khôi phục được ở màn Đã xóa)", "deleted");
        else if (isSuccess && string.Equals(stage, "note", StringComparison.OrdinalIgnoreCase))
            _notificationCenter.Add(NotificationLevel.Info, "phone", "Điện thoại cập nhật ghi chú", code, target);
        else if (isSuccess && string.Equals(stage, "edit", StringComparison.OrdinalIgnoreCase))
            _notificationCenter.Add(NotificationLevel.Info, "phone", "Điện thoại cập nhật trạng thái đơn", code, target);
        else if (isSuccess && string.Equals(stage, "scan", StringComparison.OrdinalIgnoreCase))
            _notificationCenter.Add(NotificationLevel.Success, "phone", "Điện thoại quét nhận kiện", code, target);
        else if (string.Equals(item.Outcome, "duplicate", StringComparison.OrdinalIgnoreCase) && (item.Message ?? "").Contains("ĐƠN ĐÃ CÓ", StringComparison.OrdinalIgnoreCase))
            _notificationCenter.Add(NotificationLevel.Yellow, "phone", "Điện thoại quét đơn đã có", code, target);
        else if (!isSuccess && (string.Equals(item.Outcome, "error", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(item.Outcome, "ambiguous", StringComparison.OrdinalIgnoreCase)))
            _notificationCenter.Add(NotificationLevel.Yellow, "phone", "Yêu cầu từ điện thoại cần kiểm tra", $"{code}: {item.Message}".Trim(' ', ':'), "phone_sync");
    }

    private void AddImportNotification(string source, string label, string target, int added, int updated, int ambiguous)
    {
        _notificationCenter.Add(NotificationLevel.Success, source, $"Đã nạp ({label})", $"Thêm {added}, cập nhật {updated}", target);
        if (ambiguous > 0)
            _notificationCenter.Add(NotificationLevel.Yellow, source + ":amb", $"Có {ambiguous} đơn nhập nhằng ({label})", "Giữ nguyên để bạn kiểm tra, chưa gộp.", target);
    }

    private void AddImportFailure(string source, string label, string target, string? why)
        => _notificationCenter.Add(NotificationLevel.Red, source + ":err", $"Lỗi {label}", string.IsNullOrWhiteSpace(why) ? "Không rõ nguyên nhân." : why, target);
    public ScanReceiveViewModel ScanReceiveVm { get; }
    public PhoneSyncViewModel PhoneSyncVm { get; }
    public FolderWatchViewModel FolderWatchVm { get; }
    public GoogleSheetViewModel GoogleSheetVm { get; }
    public SaleworkViewModel SaleworkVm { get; }
    public FileImportViewModel FileImportVm { get; }

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    [ObservableProperty]
    private NavigationItem? _selectedNavigation;

    [ObservableProperty]
    private string _selectedNavigationKey = string.Empty;

    [ObservableProperty]
    private ObservableObject? _currentPage;

    [ObservableProperty]
    private int _sidebarPendingCount;

    [ObservableProperty]
    private int _sidebarRedCount;

    [ObservableProperty]
    private int _sidebarYellowCount;

    [ObservableProperty]
    private int _sidebarReceivedCount;

    [ObservableProperty]
    private string _lastSweepError = string.Empty;

    [ObservableProperty]
    private bool _autoStart;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _notifyAlerts = true;

    [ObservableProperty]
    private bool _sounds = true;

    partial void OnAutoStartChanged(bool value)
    {
        _autoStartService.SetEnabled(value);
        _uiSettingsStore.Update(s => s.AutoStart = value);
        SettingsVm.SyncBackgroundSettings(AutoStart, MinimizeToTray, NotifyAlerts, Sounds);
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        _uiSettingsStore.Update(s => s.MinimizeToTray = value);
        SettingsVm.SyncBackgroundSettings(AutoStart, MinimizeToTray, NotifyAlerts, Sounds);
    }

    partial void OnNotifyAlertsChanged(bool value)
    {
        _uiSettingsStore.Update(s => s.NotifyAlerts = value);
        _trayService.SetNotifyAlertsEnabled(value);
        SettingsVm.SyncBackgroundSettings(AutoStart, MinimizeToTray, NotifyAlerts, Sounds);
    }

    partial void OnSoundsChanged(bool value)
    {
        _uiSettingsStore.Update(s => s.Sounds = value);
        ScanReceiveVm.IsSoundEnabled = value;
        SettingsVm.SyncBackgroundSettings(AutoStart, MinimizeToTray, NotifyAlerts, Sounds);
    }

    /// <summary>5 bước bắt đầu nhanh, ✓/○ tính từ trạng thái thật. Chỉ gợi ý, không làm thay người dùng.</summary>
    private async Task<IReadOnlyList<HelpStep>> BuildHelpStepsAsync()
    {
        var ui = _uiSettingsStore.Load();
        var hasOrders = ShopeeReturnVm.TotalCount + ShopeeCancelledVm.TotalCount + TikTokReturnVm.TotalCount + ArchivedOrdersVm.TotalCount > 0;
        var importedBefore = false;
        try { importedBefore = (await _activityLogProvider.ReadAsync(1, 0, "import", "")).Count > 0; } catch (Exception) { /* không đọc được: coi như chưa */ }
        var autoOn = FolderWatchVm.IsEnabled || GoogleSheetVm.IsEnabled;
        return new List<HelpStep>
        {
            new(1, "Nhập dữ liệu cũ từ app cũ", "Màn \"Nhập dữ liệu cũ\": chạy thử, so số liệu, rồi nhập thật. Nhập lại nhiều lần vẫn an toàn.", hasOrders, "legacy_import"),
            new(2, "Nhập mã phòng vào app iPhone", "Lấy mã ở màn Cài đặt hoặc Đồng bộ điện thoại. Kiểm tra trên điện thoại xem danh sách đơn đã về chưa.", ui.SyncEnabled, "settings"),
            new(3, "Thử nạp một file Excel", "Màn \"Nạp file Excel\": chọn file Shopee/TikTok đã xóa thông tin khách, xem số dòng thêm/cập nhật.", importedBefore, "file_import"),
            new(4, "Bật nạp tự động", "Theo dõi thư mục tải về hoặc Google Sheet (Salework bấm \"Lấy ngay\" khi cần).", autoOn, "folder_watch"),
            new(5, "Bật tự sao lưu", "Màn Cài đặt: tự sao lưu mỗi vài giờ, giữ nhiều ngày.", ui.BackupEnabled, "settings"),
        };
    }

    /// <summary>Các câu trạng thái đang hiển thị ở từng màn nạp/đồng bộ. KHÔNG kèm địa chỉ Apps Script, token hay tên tệp khách.</summary>
    private string BuildIntegrationStatusText()
    {
        string One(string name, bool? enabled, string note)
        {
            var on = enabled is null ? "" : (enabled.Value ? "bật; " : "tắt; ");
            var n = string.IsNullOrWhiteSpace(note) ? "(chưa có thông tin)" : note.Trim();
            if (n.Contains("script.google.com", StringComparison.OrdinalIgnoreCase) || n.Contains("googleusercontent", StringComparison.OrdinalIgnoreCase))
                n = "(đã ẩn: có địa chỉ)";
            return $"{name}: {on}{n}";
        }
        return string.Join(Environment.NewLine, new[]
        {
            One("Đồng bộ điện thoại", PhoneSyncVm.SyncEnabled, PhoneSyncVm.ConnectionStatusText),
            One("Theo dõi thư mục", FolderWatchVm.IsEnabled, FolderWatchVm.LastNote),
            One("Google Sheet", GoogleSheetVm.IsEnabled, GoogleSheetVm.LastNote),
            One("Salework", null, SaleworkVm.LastNote),
        });
    }

    private void OnSettingsBackupSettingChanged(bool enabled, int everyHours, int keepDays)
    {
        _uiSettingsStore.Update(s => { s.BackupEnabled = enabled; s.BackupEveryHours = everyHours; s.BackupKeepDays = keepDays; });
        _backupController.ApplySchedule(enabled && !IsDemoMode, everyHours, keepDays);
    }

    private void OnSettingsBackgroundSettingChanged(string propertyName, bool value)
    {
        switch (propertyName)
        {
            case nameof(AutoStart):
                AutoStart = value;
                break;
            case nameof(MinimizeToTray):
                MinimizeToTray = value;
                break;
            case nameof(NotifyAlerts):
                NotifyAlerts = value;
                break;
            case nameof(Sounds):
                Sounds = value;
                break;
        }
    }

    private void OnTrayToggleNotifyAlertsRequested()
    {
        _dispatchToUi(() => NotifyAlerts = !NotifyAlerts);
    }

    public Task<AlertCheckResult> CheckAlertNotificationsAsync(CancellationToken cancellationToken = default)
        => _alertMonitor.CheckNowAsync(cancellationToken);

    [ObservableProperty]
    private bool _isToastVisible;

    [ObservableProperty]
    private string _toastMessage = string.Empty;

    [ObservableProperty]
    private bool _toastIsError;

    [ObservableProperty]
    private bool _toastIsWarning;

    private void OnWriteGateChanged()
    {
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(WriteDisabledTooltip));
        UndoDeleteCommand.NotifyCanExecuteChanged();
    }

    private void OnCloudVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CloudStatusViewModel.IsPopupOpen) && CloudVm.IsPopupOpen)
        {
            NotificationsVm.CloseCommand.Execute(null);
        }
    }

    private void OnNotificationsVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotificationCenterViewModel.IsOpen) && NotificationsVm.IsOpen)
        {
            CloudVm.ClosePopup();
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoDeleteCommand))]
    private bool _canUndoDelete;

    [ObservableProperty]
    private string _globalSearchText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<GlobalSearchResultItem> _globalSearchResults = Array.Empty<GlobalSearchResultItem>();

    [ObservableProperty]
    private GlobalSearchResultItem? _selectedGlobalSearchResult;

    [ObservableProperty]
    private bool _isGlobalSearchOpen;

    [ObservableProperty]
    private bool _isGlobalSearchBusy;

    [ObservableProperty]
    private string _globalSearchOverflowMessage = string.Empty;

    [ObservableProperty]
    private bool _hasGlobalSearchOverflow;

    [ObservableProperty]
    private bool _hasGlobalSearchNoResults;

    [ObservableProperty]
    private string _globalSearchStatusText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDarkTheme))]
    [NotifyPropertyChangedFor(nameof(ThemeToggleText))]
    private string _currentTheme = ThemeService.LightTheme;

    public bool IsDarkTheme => string.Equals(CurrentTheme, ThemeService.DarkTheme, StringComparison.OrdinalIgnoreCase);

    public string ThemeToggleText => IsDarkTheme ? "☀️ Giao diện sáng" : "🌙 Giao diện tối";

    public string WindowTitle => "Quản Lý Hàng Hoàn v2 — Native WPF (.NET 8)";

    [RelayCommand]
    public void ToggleTheme()
    {
        SetTheme(IsDarkTheme ? ThemeService.LightTheme : ThemeService.DarkTheme);
    }

    public void SetTheme(string? theme)
    {
        var normalized = ThemeService.NormalizeTheme(theme);
        CurrentTheme = normalized;
        ThemeService.ApplyTheme(normalized);
        _uiSettingsStore.Update(s => s.Theme = normalized);
    }

    [RelayCommand]
    public void ToggleNavGroup(string? section)
    {
        if (string.IsNullOrWhiteSpace(section)) return;

        if (!_collapsedGroups.Remove(section))
        {
            _collapsedGroups.Add(section);
        }

        UpdateNavigationVisibilityAndGroupBadges();
        var snapshot = _collapsedGroups.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        _uiSettingsStore.Update(s => s.CollapsedNavGroups = snapshot);
    }

    public bool IsGroupCollapsed(string section) => _collapsedGroups.Contains(section);

    public QuanLyHangHoan.Data.WindowPlacement? LoadWindowPlacement() => _uiSettingsStore.Load().Window;

    public void SaveWindowPlacement(QuanLyHangHoan.Data.WindowPlacement? placement)
    {
        if (placement is null) return;
        _uiSettingsStore.Update(s => s.Window = placement);
    }

    [RelayCommand]
    public void SelectNavigation(NavigationItem? item) => SelectNavigation(item, persistLastPage: true);

    private void SelectNavigation(NavigationItem? item, bool persistLastPage)
    {
        if (item is null) return;

        foreach (var nav in NavigationItems)
        {
            nav.IsSelected = ReferenceEquals(nav, item);
        }

        SelectedNavigation = item;
        SelectedNavigationKey = item.Key;

        if (!_pageCache.TryGetValue(item.Key, out var vm))
        {
            vm = item.CreateViewModel();
            _pageCache[item.Key] = vm;
        }

        CurrentPage = vm;
        if (ReferenceEquals(vm, StatisticsVm)) FlushPendingStats();
        UpdateNavigationVisibilityAndGroupBadges();

        if (persistLastPage)
        {
            if (item.Key.Equals("return_watch", StringComparison.OrdinalIgnoreCase))
            {
                _ = ReturnWatchVm.RefreshAsync();
            }
            if (item.Key.Equals("activity", StringComparison.OrdinalIgnoreCase))
            {
                _ = ActivityLogVm.ReloadAsync();
            }
            if (item.Key.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                _ = HelpVm.RefreshAsync();
            }
            var lastPage = item.Key.Equals("home", StringComparison.OrdinalIgnoreCase) ? "" : item.Key;
            _uiSettingsStore.Update(s => s.LastPage = lastPage);
        }
    }

    public void NavigateByKey(string key)
    {
        if (key.Equals(SettingsViewModel.CloudTabKey, StringComparison.OrdinalIgnoreCase) && CloudVm.IsEnabled)
        {
            SettingsVm.SelectTab(SettingsViewModel.CloudTabKey);
            key = "settings";
        }
        // Các đường nạp đơn nay nằm trong Cài đặt (thẻ riêng): khóa cũ mở Cài đặt đúng thẻ đó.
        var importKey = SettingsViewModel.ImportTabKeys.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (importKey is not null) { SettingsVm.SelectTab(importKey); key = "settings"; }
        var item = NavigationItems.FirstOrDefault(n => n.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (item is not null) SelectNavigation(item);
    }

    public async Task InitializeAsync(TimeSpan? sweepInterval = null, CancellationToken cancellationToken = default)
    {
        if (!IsDemoMode)
        {
            await RunArchiveSweepAsync(reloadIfMoved: false, cancellationToken);
        }

        await ReloadAllDataAsync();

        if (!IsDemoMode)
        {
            StartPeriodicSweep(sweepInterval ?? TimeSpan.FromMinutes(10));
            _alertMonitor.Start();
            _syncHealthTimer ??= new Timer(_ => { try { CheckSyncHealthOnce(); } catch (Exception) { } }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }
    }

    public void StartPeriodicSweep(TimeSpan interval)
    {
        if (IsDemoMode || _archiveSweeper is null || interval <= TimeSpan.Zero) return;

        StopPeriodicSweep();
        var cts = new CancellationTokenSource();
        _periodicSweepCts = cts;
        _ = RunPeriodicSweepLoopAsync(interval, cts.Token);
    }

    public void StopPeriodicSweep()
    {
        var cts = Interlocked.Exchange(ref _periodicSweepCts, null);
        if (cts is not null)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    public async Task<int> RunArchiveSweepAsync(bool reloadIfMoved = true, CancellationToken cancellationToken = default)
    {
        if (IsDemoMode || _archiveSweeper is null || !CanWrite) return 0;
        if (Interlocked.CompareExchange(ref _sweepRunning, 1, 0) != 0) return 0;

        try
        {
            var moved = await _archiveSweeper.SweepAsync(_returnOrderProvider.GetCurrentTime(), cancellationToken);
            LastSweepError = string.Empty;
            if (moved > 0 && reloadIfMoved)
            {
                await ReloadAllDataAsync();
            }
            return moved;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            LastSweepError = ex.Message;
            return 0;
        }
        finally
        {
            Interlocked.Exchange(ref _sweepRunning, 0);
        }
    }

    private async Task RunPeriodicSweepLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RunArchiveSweepAsync(reloadIfMoved: true, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Dừng timer khi đóng ứng dụng
        }
    }

    private async Task OnLegacyImportSucceededAsync()
    {
        if (!IsDemoMode)
        {
            await RunArchiveSweepAsync(reloadIfMoved: false);
        }
        await ReloadAllDataAsync();
    }

    private Task OnScanReceivedSucceededAsync() => ReloadAllDataAsync();

    private void OnSyncHandled(SyncHandled item)
    {
        if (!string.IsNullOrWhiteSpace(item.RequestId))
        {
            var dedupKey = $"{item.RequestId}\0{item.Stage}";
            lock (_seenSyncHandledKeys)
            {
                if (!_seenSyncHandledKeys.Add(dedupKey))
                {
                    return;
                }
            }
        }

        var isSuccess = string.Equals(item.Outcome, "success", StringComparison.OrdinalIgnoreCase);
        var isNewOrder = string.Equals(item.Kind, "new_order", StringComparison.OrdinalIgnoreCase);
        var isPhoto = string.Equals(item.Stage, "photo", StringComparison.OrdinalIgnoreCase);

        AddSyncNotification(item, isSuccess, isNewOrder);

        if (Sounds)
        {
            if (isSuccess && (string.Equals(item.Stage, "scan", StringComparison.OrdinalIgnoreCase) || isNewOrder))
            {
                _soundPlayer.PlaySuccess();
            }
            else if (string.Equals(item.Outcome, "error", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(item.Outcome, "duplicate", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(item.Outcome, "ambiguous", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(item.Outcome, "manual_review", StringComparison.OrdinalIgnoreCase))
            {
                _soundPlayer.PlayError();
            }
        }

        if (isSuccess && (!isPhoto || isNewOrder))
        {
            ScheduleSyncReload();
        }

        if (isSuccess && isNewOrder)
        {
            var platform = ExtractPlatformFromMessage(item.Message);
            var msg = string.IsNullOrWhiteSpace(platform)
                ? $"📥 Điện thoại vừa tạo đơn mới: {item.Code}"
                : $"📥 Điện thoại vừa tạo đơn mới: {item.Code} ({platform})";
            _dispatchToUi(() => ShowToast(msg, isError: false));
            return;
        }

        if (string.Equals(item.Outcome, "duplicate", StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Stage, "scan", StringComparison.OrdinalIgnoreCase)
            && item.Message.Contains("ĐƠN ĐÃ CÓ", StringComparison.OrdinalIgnoreCase))
        {
            var msg = $"⚠️ Điện thoại quét đơn đã có: {item.Code}";
            _dispatchToUi(() => ShowToast(msg, isError: false, isWarning: true));
            return;
        }

        if (!isSuccess)
        {
            return;
        }

        if (string.Equals(item.Stage, "delete", StringComparison.OrdinalIgnoreCase))
        {
            var msg = $"🗑 Điện thoại xóa đơn {item.Code} (khôi phục được ở Đã xóa)";
            _dispatchToUi(() => ShowToast(msg, isError: false));
        }
        else if (string.Equals(item.Stage, "note", StringComparison.OrdinalIgnoreCase))
        {
            var msg = string.IsNullOrWhiteSpace(item.Code)
                ? "📝 Điện thoại cập nhật ghi chú đơn"
                : $"📝 Điện thoại cập nhật ghi chú đơn {item.Code}";
            _dispatchToUi(() => ShowToast(msg, isError: false));
        }
        else if (string.Equals(item.Stage, "edit", StringComparison.OrdinalIgnoreCase))
        {
            var msg = string.IsNullOrWhiteSpace(item.Code)
                ? "📝 Điện thoại cập nhật trạng thái đơn"
                : $"📝 Điện thoại cập nhật trạng thái đơn {item.Code}";
            _dispatchToUi(() => ShowToast(msg, isError: false));
        }
    }

    private void ScheduleSyncReload()
    {
        if (_syncReloadDebounce <= TimeSpan.Zero)
        {
            _dispatchToUi(() => _ = ReloadAllDataAsync());
            return;
        }

        CancellationTokenSource cts;
        lock (_syncReloadGate)
        {
            _syncReloadCts?.Cancel();
            _syncReloadCts?.Dispose();
            cts = new CancellationTokenSource();
            _syncReloadCts = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_syncReloadDebounce, cts.Token);
                if (!cts.Token.IsCancellationRequested)
                {
                    _dispatchToUi(() => _ = ReloadAllDataAsync());
                }
            }
            catch (OperationCanceledException)
            {
                // Gộp các sự kiện dồn dập vào một lần tải lại
            }
        });
    }

    private static string ExtractPlatformFromMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;
        var open = message.LastIndexOf('(');
        var close = message.LastIndexOf(')');
        if (open >= 0 && close > open + 1)
        {
            return message[(open + 1)..close].Trim();
        }
        return string.Empty;
    }

    private void OnFolderWatchConfigChanged()
    {
        _dispatchToUi(UpdateNavigationVisibilityAndGroupBadges);
    }

    private void OnFolderWatchCompleted(FolderWatchRunResult result)
    {
        if (result.Ran && !result.Ok) AddImportFailure("folder", "theo dõi thư mục", "folder_watch", result.Note);
        if (!result.Ran || !result.Ok || result.Import is null) return;
        var hasChanges = result.Import.Added > 0 || result.Import.Updated > 0 || result.Import.EnrichedArchived > 0;
        if (!hasChanges) return;
        AddImportNotification("folder", "theo dõi thư mục", "folder_watch", result.Import.Added, result.Import.Updated, result.Import.Ambiguous.Count);

        var filesText = result.Names.Count > 0 ? string.Join(", ", result.Names) : "file mới";
        var msg = $"Đã tự nạp {result.FilesImported} file TikTok: {filesText}";
        _dispatchToUi(() =>
        {
            _ = ReloadAllDataAsync();
            ShowToast(msg, isError: false);
        });
    }

    private void OnSheetSyncConfigChanged()
    {
        _dispatchToUi(UpdateNavigationVisibilityAndGroupBadges);
    }

    private void OnSheetSyncCompleted(SheetRunResult result)
    {
        if (result.Ran && !result.Ok) AddImportFailure("sheet", "Google Sheet", "google_sheet", result.Note);
        if (!result.Ran || !result.Ok || result.Import is null) return;
        var added = result.Import.Added;
        var updated = result.Import.Updated;
        var enriched = result.Import.EnrichedArchived;
        if (added <= 0 && updated <= 0 && enriched <= 0) return;
        AddImportNotification("sheet", "Google Sheet", "google_sheet", added, updated, result.Import.Ambiguous.Count);

        var msg = $"Đã cập nhật từ Google Sheet: thêm {added}, cập nhật {updated}";
        _dispatchToUi(() =>
        {
            _ = ReloadAllDataAsync();
            ShowToast(msg, isError: false);
        });
    }

    private void OnSaleworkStateChanged()
    {
        _dispatchToUi(UpdateNavigationVisibilityAndGroupBadges);
    }

    private void OnSaleworkCompleted(SaleworkRunResult result)
    {
        if (result.Ran && !result.Ok && !result.Stopped)
            AddImportFailure("salework", "Salework", "salework", result.NeedsRelogin ? "Cần đăng nhập lại Salework." : result.Error);
        if (!result.Ran || !result.Ok) return;

        var merged = result.Import?.SupersededMerged ?? 0;
        var ambiguous = result.Import?.SupersedeAmbiguous ?? 0;
        _notificationCenter.Add(NotificationLevel.Success, "salework", "Đã lấy từ Salework", $"{result.RowCount} dòng" + (merged > 0 ? $", gộp {merged} dòng chờ cũ" : ""), "salework");
        if (ambiguous > 0)
            _notificationCenter.Add(NotificationLevel.Yellow, "salework:amb", $"Có {ambiguous} đơn nhập nhằng (Salework)", "Giữ nguyên để bạn kiểm tra, chưa gộp.", "salework");
        var msg = $"Đã lấy {result.RowCount} dòng từ Salework và nạp vào bảng"
            + (merged > 0 ? $", gộp {merged} dòng chờ cũ" : string.Empty)
            + (ambiguous > 0 ? $" — ⚠️ {ambiguous} đơn có nhiều kiện khác nhau, giữ nguyên để bạn xem" : string.Empty);

        _dispatchToUi(() =>
        {
            _ = ReloadAllDataAsync();
            ShowToast(msg, isError: false);
        });
    }

    private void OnFileImportCompleted(FileImportResult result)
    {
        if (!result.Ok)
        {
            var errMsg = string.IsNullOrWhiteSpace(result.Message) ? "Nạp file Excel thất bại." : result.Message;
            AddImportFailure("file", "nạp file Excel", "file_import", errMsg);
            _dispatchToUi(() => ShowToast(errMsg, isError: true));
            return;
        }

        var enriched = (result.TikTok?.EnrichedArchived ?? 0) + (result.Shopee?.EnrichedArchived ?? 0);
        if (result.Added <= 0 && result.Updated <= 0 && enriched <= 0)
        {
            return;
        }

        AddImportNotification("file", "file Excel", "file_import", result.Added, result.Updated, result.Ambiguous);
        var msg = $"Đã nạp file Excel: thêm {result.Added}, cập nhật {result.Updated}";
        _dispatchToUi(() =>
        {
            _ = ReloadAllDataAsync();
            ShowToast(msg, isError: false);
        });
    }

    private static void DefaultDispatchToUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null
            && !dispatcher.HasShutdownStarted
            && !dispatcher.HasShutdownFinished
            && dispatcher.Thread.IsAlive
            && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    private void OpenOrderDetail(Core.Models.Order order)
    {
        _orderDetailService.ShowOrderDetail(order, _returnOrderProvider.GetCurrentTime());
    }

    private void OpenOrderPhoto(string photoId, string orderId, string tracking)
    {
        if (string.IsNullOrWhiteSpace(photoId)) return;
        _photoViewerService?.ShowPhoto(photoId, orderId, tracking);
    }

    /// <summary>Thông báo trong trung tâm thông báo cho trợ lý AI (công cụ notifications).</summary>
    private IReadOnlyList<QuanLyHangHoan.Data.Ai.AiOrderTools.AiNotice> BuildAiNotices() =>
        _notificationCenter.Snapshot()
            .Select(e => new QuanLyHangHoan.Data.Ai.AiOrderTools.AiNotice(e.Time, e.Level.ToString(), e.Source, e.Title, e.Detail, e.Read))
            .ToList();

    /// <summary>Thông tin tình trạng cho trợ lý AI (công cụ system_status): kết nối điện thoại, thông báo, sao lưu. Không chứa mã phòng hay khóa.</summary>
    private IReadOnlyDictionary<string, object?> BuildAiStatus()
    {
        var d = new Dictionary<string, object?>();
        d["phoneSync"] = !_syncController.SyncEnabled ? "đã tắt"
            : (_syncController.ConnectedBrokers > 0 || _syncController.NtfyConnected) ? "đang kết nối" : "chưa kết nối được";
        d["mqtt"] = $"{_syncController.ConnectedBrokers}/{_syncController.TotalBrokers} máy chủ";
        d["ntfy"] = !_syncController.NtfyEnabled ? "đã tắt" : (_syncController.NtfyConnected ? "đang kết nối" : "chưa kết nối");
        d["unreadNotifications"] = _notificationCenter.UnreadCount;
        try
        {
            var backups = _backupController.List();
            var last = backups.OrderByDescending(b => b.Time).FirstOrDefault();
            d["lastBackup"] = last is null ? "chưa có bản sao lưu" : last.Time.ToString("dd/MM/yyyy HH:mm");
            d["backupCount"] = backups.Count;
        }
        catch { d["lastBackup"] = "không đọc được"; }
        return d;
    }

    private (IReadOnlyList<Core.Models.Order> Main, IReadOnlyList<Core.Models.Order> Archived, DateTime Now)? _pendingStats;

    private void FlushPendingStats()
    {
        if (_pendingStats is not { } p) return;
        _pendingStats = null;
        StatisticsVm.LoadData(p.Main, p.Archived, p.Now);
    }

    public async Task ReloadAllDataAsync()
    {
        _syncController.NotifyDataChanged();   // nạp Sheet/Excel/thư mục xong: điện thoại nhận phần thay đổi ngay
        if (ReferenceEquals(CurrentPage, ActivityLogVm)) _ = ActivityLogVm.RefreshQuietlyAsync();   // có hoạt động mới: làm mới nhẹ nếu đang xem nhật ký
        var now = _returnOrderProvider.GetCurrentTime();
        var returnOrdersTask = _returnOrderProvider.GetReturnOrdersAsync();
        var archivedOrdersTask = Task.Run(() => _archivedOrderProvider.GetArchivedOrders());
        var deletedOrdersTask = Task.Run(() => _deletedOrderProvider.GetDeletedOrders());
        var newOrdersTask = Task.Run(() => _newOrderProvider.GetNewOrders());
        var returnWatchTask = _returnWatchService.LoadAsync(now);
        var missingPhotosTask = !IsDemoMode && _photoProvider is not null
            ? _photoProvider.GetMissingPhotoIdsAsync()
            : Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        await Task.WhenAll(returnOrdersTask, archivedOrdersTask, deletedOrdersTask, newOrdersTask, returnWatchTask, missingPhotosTask);

        var returnOrders = returnOrdersTask.Result;
        var archivedOrders = archivedOrdersTask.Result;
        var deletedOrders = deletedOrdersTask.Result;
        var newOrders = newOrdersTask.Result;
        var returnWatchEntries = returnWatchTask.Result;
        var missingCount = missingPhotosTask.Result.Count;

        _searchActiveOrders = returnOrders;
        _searchArchivedOrders = archivedOrders;
        _searchDeletedOrders = deletedOrders;

        // Phần NẶNG (dựng dòng bảng, tính số liệu trang chủ) chạy song song ở luồng nền: nhiều nghìn/chục nghìn đơn cũng không làm đứng giao diện.
        // Luồng giao diện chỉ áp kết quả đã tính (nhanh).
        var homeTask = Task.Run(() => HomeVm.Compute(returnOrders, now));
        var shopeeRowsTask = Task.Run(() => ShopeeReturnVm.BuildRows(returnOrders, now));
        var cancelledRowsTask = Task.Run(() => ShopeeCancelledVm.BuildRows(returnOrders, now));
        var tiktokRowsTask = Task.Run(() => TikTokReturnVm.BuildRows(returnOrders, now));
        var archivedRowsTask = Task.Run(() => ArchivedOrdersVm.BuildRows(archivedOrders));
        await Task.WhenAll(homeTask, shopeeRowsTask, cancelledRowsTask, tiktokRowsTask, archivedRowsTask);

        ReturnWatchVm.LoadEntries(returnWatchEntries, now);
        HomeVm.ApplyComputed(homeTask.Result, returnOrders, now);
        HomeVm.ReturnWatchOnTheWayCount = ReturnWatchVm.Stage2Count;
        HomeVm.MissingPhotoCount = missingCount;
        ShopeeReturnVm.LoadRows(shopeeRowsTask.Result);
        ShopeeCancelledVm.LoadRows(cancelledRowsTask.Result);
        TikTokReturnVm.LoadRows(tiktokRowsTask.Result);
        WatchlistVm.LoadOrders(returnOrders, archivedOrders, now);
        ArchivedOrdersVm.LoadRows(archivedRowsTask.Result);
        DeletedOrdersVm.LoadOrders(deletedOrders);
        NewOrdersVm.LoadOrders(newOrders);
        NewCreatedVm.LoadOrders(returnOrders, now);
        // Thống kê tính nặng nhất: chỉ tính ngay khi đang mở màn đó, còn lại hoãn tới lúc mở (luôn dùng dữ liệu mới nhất).
        _pendingStats = (returnOrders, archivedOrders, now);
        if (ReferenceEquals(CurrentPage, StatisticsVm)) FlushPendingStats();
        StatisticsVm.MissingPhotoCount = missingCount;
        SyncSidebarKpis();
    }

    public void Dispose()
    {
        _writeGate.Changed -= OnWriteGateChanged;
        CloudVm.PropertyChanged -= OnCloudVmPropertyChanged;
        NotificationsVm.PropertyChanged -= OnNotificationsVmPropertyChanged;
        CloudVm.Dispose();
        _syncController.Handled -= OnSyncHandled;
        _folderWatchController.Completed -= OnFolderWatchCompleted;
        _folderWatchController.ConfigChanged -= OnFolderWatchConfigChanged;
        _sheetSyncController.Completed -= OnSheetSyncCompleted;
        _sheetSyncController.ConfigChanged -= OnSheetSyncConfigChanged;
        _saleworkController.Completed -= OnSaleworkCompleted;
        _saleworkController.StateChanged -= OnSaleworkStateChanged;
        _fileImportController.Completed -= OnFileImportCompleted;
        PhoneSyncVm.Dispose();
        FolderWatchVm.Dispose();
        GoogleSheetVm.Dispose();
        SaleworkVm.Dispose();
        _syncController.Dispose();
        _folderWatchController.Dispose();
        _sheetSyncController.Dispose();
        _saleworkController.Dispose();
        _trayService.ToggleNotifyAlertsRequested -= OnTrayToggleNotifyAlertsRequested;
        _syncHealthTimer?.Dispose();
        _alertMonitor.Dispose();
        _backupController.Dispose();
        _trayService.Dispose();
        StopPeriodicSweep();
        CancelToastTimer();
        lock (_syncReloadGate)
        {
            _syncReloadCts?.Cancel();
            _syncReloadCts?.Dispose();
            _syncReloadCts = null;
        }
        lock (_globalSearchGate)
        {
            _globalSearchCts?.Cancel();
            _globalSearchCts?.Dispose();
            _globalSearchCts = null;
        }
    }

    public void ShowDeletedUndoToast(IReadOnlyList<(string OrderId, string Tracking)> deletedOrders)
    {
        if (deletedOrders.Count == 0) return;

        _pendingUndoIsReturnWatch = false;
        _pendingUndoOrderIds = deletedOrders.Select(d => d.OrderId).ToList();
        CanUndoDelete = true;
        ToastIsError = false;
        ToastIsWarning = false;
        ToastMessage = deletedOrders.Count == 1
            ? $"Đã xóa {deletedOrders[0].Tracking}"
            : $"Đã xóa {deletedOrders.Count:N0} đơn";
        IsToastVisible = true;
        RestartToastTimer();
    }

    public void ShowReturnWatchUndoToast(IReadOnlyList<(string OrderId, string Tracking)> removedOrders, string? customMessage = null)
    {
        if (removedOrders.Count == 0) return;

        _pendingUndoIsReturnWatch = true;
        _pendingUndoOrderIds = removedOrders.Select(d => d.OrderId).ToList();
        CanUndoDelete = true;
        ToastIsError = false;
        ToastIsWarning = false;
        ToastMessage = !string.IsNullOrWhiteSpace(customMessage)
            ? customMessage
            : (removedOrders.Count == 1
                ? $"Đã bỏ {removedOrders[0].Tracking} khỏi bảng Hàng hoàn về"
                : $"Đã bỏ {removedOrders.Count:N0} đơn khỏi bảng Hàng hoàn về");
        IsToastVisible = true;
        RestartToastTimer();
    }

    public void ShowToast(string message, bool isError)
        => ShowToast(message, isError, isWarning: false);

    public void ShowToast(string message, bool isError, bool isWarning)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        _pendingUndoOrderIds = Array.Empty<string>();
        CanUndoDelete = false;
        ToastIsError = isError;
        ToastIsWarning = !isError && isWarning;
        ToastMessage = message;
        IsToastVisible = true;
        RestartToastTimer();
    }

    private bool CanExecuteUndoDelete => CanUndoDelete && CanWrite;

    [RelayCommand(CanExecute = nameof(CanExecuteUndoDelete))]
    public async Task UndoDeleteAsync()
    {
        if (!CanWrite) return;
        var ids = _pendingUndoOrderIds;
        var isReturnWatch = _pendingUndoIsReturnWatch;
        DismissToast();
        if (ids.Count == 0) return;

        int restored = 0;
        var now = _returnOrderProvider.GetCurrentTime();
        foreach (var id in ids)
        {
            var res = isReturnWatch
                ? await _returnWatchService.RestoreAsync(id, now)
                : await _orderEditService.RestoreAsync(id);
            if (res.Kind == QuanLyHangHoan.Data.EditKind.Saved)
            {
                restored++;
            }
        }

        if (restored > 0)
        {
            await ReloadAllDataAsync();
        }
    }

    [RelayCommand]
    public void DismissToast()
    {
        CancelToastTimer();
        IsToastVisible = false;
        ToastIsError = false;
        ToastIsWarning = false;
        CanUndoDelete = false;
        _pendingUndoIsReturnWatch = false;
        _pendingUndoOrderIds = Array.Empty<string>();
    }

    private void RestartToastTimer()
    {
        CancelToastTimer();
        if (_toastDuration <= TimeSpan.Zero) return;

        var cts = new CancellationTokenSource();
        _toastCts = cts;
        _ = AutoDismissToastAsync(cts.Token);
    }

    private void CancelToastTimer()
    {
        var cts = Interlocked.Exchange(ref _toastCts, null);
        if (cts is not null)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private async Task AutoDismissToastAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(_toastDuration, ct);
            _dispatchToUi(() =>
            {
                if (!ct.IsCancellationRequested)
                {
                    IsToastVisible = false;
                    ToastIsError = false;
                    ToastIsWarning = false;
                    CanUndoDelete = false;
                    _pendingUndoOrderIds = Array.Empty<string>();
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Hủy khi có thông báo mới hoặc người dùng đóng
        }
    }

    private void OpenOrderFromDashboard(UrgentOrderItem item)
    {
        var source = _searchActiveOrders.FirstOrDefault(o => o.OrderId.Equals(item.OrderId, StringComparison.OrdinalIgnoreCase)
            && (o.Track.Equals(item.Track, StringComparison.OrdinalIgnoreCase) || o.ReturnTrack.Equals(item.Track, StringComparison.OrdinalIgnoreCase)))
            ?? _searchActiveOrders.FirstOrDefault(o => o.OrderId.Equals(item.OrderId, StringComparison.OrdinalIgnoreCase));
        RevealInTables(item.OrderId, item.Track, item.TargetNavKey, source);
    }

    /// <summary>
    /// Mở bảng chứa đơn và chọn đúng dòng. Thử bảng dự kiến trước, nếu đơn không nằm ở đó (loại đơn lạ, đơn mới tạo...) thì tìm ở các bảng còn lại.
    /// Không bảng nào có thì mở cửa sổ chi tiết đơn và báo rõ, để người dùng không bị đưa tới một bảng "trống rỗng".
    /// </summary>
    private bool RevealInTables(string orderId, string track, string preferredKey, Core.Models.Order? source)
    {
        var tables = new (string Key, ReturnOrdersViewModel Vm)[]
        {
            ("shopee_return", ShopeeReturnVm), ("shopee_cancelled", ShopeeCancelledVm),
            ("tiktok_return", TikTokReturnVm), ("new_orders", NewCreatedVm),
        };
        foreach (var t in tables.OrderBy(t => t.Key == preferredKey ? 0 : 1))
        {
            if (!t.Vm.HasRow(orderId, track)) continue;
            NavigateByKey(t.Key);
            if (t.Vm.SelectOrder(orderId, track)) return true;
        }
        NavigateByKey(preferredKey);
        ShowToast($"Không thấy đơn {orderId} trong các bảng — đang mở chi tiết đơn.", false, true);
        if (source is not null) OpenOrderDetail(source);
        return false;
    }

    private void GoToMainTableFromOrder(Core.Models.Order order)
    {
        var (label, targetNavKey) = GlobalSearchService.ResolveLocation(order, isArchived: false, isDeleted: false);
        var tracking = !string.IsNullOrWhiteSpace(order.ReturnTrack) ? order.ReturnTrack : order.Track;
        RevealInTables(order.OrderId, tracking, targetNavKey, order);
    }

    private void SyncSidebarKpis()
    {
        _shopeeReturnNavItem.Badge = ShopeeReturnVm.TotalCount.ToString("N0");
        _shopeeCancelledNavItem.Badge = ShopeeCancelledVm.TotalCount.ToString("N0");
        _tiktokReturnNavItem.Badge = TikTokReturnVm.TotalCount.ToString("N0");
        _returnWatchNavItem.Badge = ReturnWatchVm.TrackingCount.ToString("N0");
        _newOrdersNavItem.Badge = NewCreatedVm.TotalCount.ToString("N0");
        _watchlistNavItem.Badge = WatchlistVm.ActiveWatchlistCount.ToString("N0");
        _archiveNavItem.Badge = ArchivedOrdersVm.TotalCount.ToString("N0");
        _deletedNavItem.Badge = DeletedOrdersVm.TotalCount.ToString("N0");

        SidebarPendingCount = ShopeeReturnVm.PendingCount + ShopeeCancelledVm.PendingCount + TikTokReturnVm.PendingCount;
        SidebarRedCount = ShopeeReturnVm.RedAlertCount + ShopeeCancelledVm.RedAlertCount + TikTokReturnVm.RedAlertCount;
        SidebarYellowCount = ShopeeReturnVm.YellowAlertCount + ShopeeCancelledVm.YellowAlertCount + TikTokReturnVm.YellowAlertCount;
        SidebarReceivedCount = ShopeeReturnVm.ReceivedCount + ShopeeCancelledVm.ReceivedCount + TikTokReturnVm.ReceivedCount;
        _trayService.SetRedAlertState(SidebarRedCount > 0);

        UpdateNavigationVisibilityAndGroupBadges();
    }

    private void UpdateNavigationVisibilityAndGroupBadges()
    {
        var shopeeAlerts = ShopeeReturnVm.RedAlertCount + ShopeeReturnVm.YellowAlertCount
                           + ShopeeCancelledVm.RedAlertCount + ShopeeCancelledVm.YellowAlertCount;
        var shopeeTotal = ShopeeReturnVm.TotalCount + ShopeeCancelledVm.TotalCount;

        var tiktokAlerts = TikTokReturnVm.RedAlertCount + TikTokReturnVm.YellowAlertCount;
        var tiktokTotal = TikTokReturnVm.TotalCount;

        var chungTotal = ReturnWatchVm.TrackingCount + NewCreatedVm.TotalCount + WatchlistVm.ActiveWatchlistCount + ArchivedOrdersVm.TotalCount + DeletedOrdersVm.TotalCount;

        foreach (var item in NavigationItems)
        {
            if (string.IsNullOrWhiteSpace(item.Section))
            {
                item.IsVisible = true;
                item.IsGroupCollapsed = false;
                item.GroupBadge = string.Empty;
                continue;
            }

            var collapsed = _collapsedGroups.Contains(item.Section);
            item.IsGroupCollapsed = collapsed;
            // Mục đang chọn luôn hiển thị ngay cả khi nhóm chứa nó đang thu gọn
            item.IsVisible = !collapsed || item.IsSelected;

            if (item.ShowSectionHeader)
            {
                item.GroupBadge = item.Section switch
                {
                    "SHOPEE" => shopeeAlerts > 0 ? $"⚠ {shopeeAlerts:N0}" : $"{shopeeTotal:N0}",
                    "TIKTOK" => tiktokAlerts > 0 ? $"⚠ {tiktokAlerts:N0}" : $"{tiktokTotal:N0}",
                    "NẠP ĐƠN TỰ ĐỘNG" => (_folderWatchController.Enabled || _sheetSyncController.Enabled) ? "Bật" : string.Empty,
                    "CHUNG" => chungTotal > 0 ? $"{chungTotal:N0}" : string.Empty,
                    _ => string.Empty
                };
            }
        }
    }

    // ---------------- Tìm kiếm toàn cục (T020) ----------------

    partial void OnGlobalSearchTextChanged(string value)
    {
        ScheduleGlobalSearch(value);
    }

    private void ScheduleGlobalSearch(string query)
    {
        CancellationTokenSource cts;
        lock (_globalSearchGate)
        {
            _globalSearchCts?.Cancel();
            _globalSearchCts?.Dispose();
            cts = new CancellationTokenSource();
            _globalSearchCts = cts;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            IsGlobalSearchBusy = false;
            IsGlobalSearchOpen = false;
            GlobalSearchResults = Array.Empty<GlobalSearchResultItem>();
            SelectedGlobalSearchResult = null;
            GlobalSearchOverflowMessage = string.Empty;
            HasGlobalSearchOverflow = false;
            HasGlobalSearchNoResults = false;
            GlobalSearchStatusText = string.Empty;
            return;
        }

        IsGlobalSearchBusy = true;
        IsGlobalSearchOpen = true;
        GlobalSearchStatusText = "Đang tìm...";

        _ = Task.Run(async () =>
        {
            try
            {
                if (_searchDebounce > TimeSpan.Zero)
                {
                    await Task.Delay(_searchDebounce, cts.Token);
                }
                if (cts.Token.IsCancellationRequested) return;

                await ExecuteGlobalSearchAsync(query, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Đã có từ khóa mới hơn
            }
        });
    }

    public async Task ExecuteGlobalSearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _dispatchToUi(() =>
            {
                IsGlobalSearchBusy = false;
                IsGlobalSearchOpen = false;
                GlobalSearchResults = Array.Empty<GlobalSearchResultItem>();
                SelectedGlobalSearchResult = null;
                HasGlobalSearchOverflow = false;
                GlobalSearchOverflowMessage = string.Empty;
                HasGlobalSearchNoResults = false;
                GlobalSearchStatusText = string.Empty;
            });
            return;
        }

        var active = _searchActiveOrders;
        var archived = _searchArchivedOrders;
        var deleted = _searchDeletedOrders;

        var output = await Task.Run(() => GlobalSearchService.Search(query, active, archived, deleted), cancellationToken);
        if (cancellationToken.IsCancellationRequested) return;

        _dispatchToUi(() =>
        {
            if (cancellationToken.IsCancellationRequested) return;
            GlobalSearchResults = output.Items;
            SelectedGlobalSearchResult = output.Items.Count > 0 ? output.Items[0] : null;
            HasGlobalSearchOverflow = output.HasOverflow;
            GlobalSearchOverflowMessage = output.OverflowMessage;
            HasGlobalSearchNoResults = output.Items.Count == 0;
            GlobalSearchStatusText = output.TotalMatches > 0
                ? $"Tìm thấy {output.TotalMatches:N0} kiện"
                : "Không tìm thấy";
            IsGlobalSearchBusy = false;
            IsGlobalSearchOpen = true;
        });
    }

    [RelayCommand]
    public void ClearGlobalSearch()
    {
        GlobalSearchText = string.Empty;
        IsGlobalSearchOpen = false;
    }

    [RelayCommand]
    public void CloseGlobalSearchDropdown()
    {
        IsGlobalSearchOpen = false;
    }

    [RelayCommand]
    public void OpenGlobalSearchResult(GlobalSearchResultItem? item = null)
    {
        var target = item ?? SelectedGlobalSearchResult ?? GlobalSearchResults.FirstOrDefault();
        if (target is null) return;
        OpenOrderDetail(target.Source);
    }

    [RelayCommand]
    public void GoToGlobalSearchTable(GlobalSearchResultItem? item = null)
    {
        var target = item ?? SelectedGlobalSearchResult;
        if (target is null) return;

        var tracking = !string.IsNullOrWhiteSpace(target.Source.ReturnTrack)
            ? target.Source.ReturnTrack
            : target.Source.Track;

        switch (target.TargetNavKey)
        {
            case "shopee_return":
            case "shopee_cancelled":
            case "tiktok_return":
                RevealInTables(target.OrderId, tracking, target.TargetNavKey, target.Source);
                break;
            case "archive":
                NavigateByKey(target.TargetNavKey);
                ArchivedOrdersVm.SelectOrder(target.OrderId, tracking);
                break;
            case "deleted":
                NavigateByKey(target.TargetNavKey);
                DeletedOrdersVm.SelectOrder(target.OrderId, tracking);
                break;
        }

        IsGlobalSearchOpen = false;
    }

    public void MoveGlobalSearchSelection(int delta)
    {
        if (GlobalSearchResults.Count == 0) return;
        var currentIdx = SelectedGlobalSearchResult is null
            ? -1
            : GlobalSearchResults.ToList().IndexOf(SelectedGlobalSearchResult);
        var nextIdx = currentIdx < 0
            ? 0
            : Math.Clamp(currentIdx + delta, 0, GlobalSearchResults.Count - 1);
        SelectedGlobalSearchResult = GlobalSearchResults[nextIdx];
    }
}
