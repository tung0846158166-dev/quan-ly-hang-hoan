using System.IO;
using System.Windows;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Data.Sheet;

namespace QuanLyHangHoan.App;

public partial class App : Application
{
    private MainViewModel? _mainViewModel;
    private CloudMode? _cloud;
    private ISingleInstanceGuard? _singleInstanceGuard;
    private System.Threading.Timer? _digestTimer;
    private ITrayService? _trayService;

    /// <summary>Ghi lỗi khởi động/lỗi không bắt được vào thư mục dữ liệu để biết vì sao app không hiện (không có nhật ký thì chỉ thấy "mở lên rồi mất").</summary>

    internal static void LogCrash(string where, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.BaseFolder);
            File.AppendAllText(Path.Combine(AppPaths.BaseFolder, "startup-error.txt"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception) { /* không ghi được thì thôi */ }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) => LogCrash("Lỗi không bắt được (giao diện)", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => { if (args.ExceptionObject is Exception ex) LogCrash("Lỗi không bắt được (nền)", ex); };
        try
        {
            StartCore(e);
        }
        catch (Exception ex)
        {
            LogCrash("Lỗi khi khởi động", ex);
            MessageBox.Show("App không khởi động được:\n" + ex.Message + "\n\nChi tiết đã ghi vào: " + Path.Combine(AppPaths.BaseFolder, "startup-error.txt"),
                "Quản lý hàng hoàn", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void StartCore(StartupEventArgs e)
    {
        base.OnStartup(e);

        var isDemoMode = e.Args.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        var startInTray = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

        _singleInstanceGuard = isDemoMode
            ? new FakeSingleInstanceGuard()
            : new WindowsSingleInstanceGuard();
        if (!_singleInstanceGuard.TryAcquire(() => Dispatcher.BeginInvoke(() =>
        {
            if (MainWindow is MainWindow mw)
            {
                mw.RestoreFromTray();
            }
        })))
        {
            Shutdown(0);
            return;
        }

        IFolderPickerService folderPicker = new WindowsFolderPickerService();
        IFileSaveService fileSaveService = new WindowsFileSaveService();
        IScanSoundPlayer soundPlayer = isDemoMode ? new NoOpScanSoundPlayer() : new SystemScanSoundPlayer();
        IImageFilePickerService imagePicker = new WindowsImageFilePickerService();
        IImportFilePickerService importFilePicker = new WindowsImportFilePickerService();

        IAutoStartService autoStartService = isDemoMode
            ? new FakeAutoStartService()
            : new WindowsAutoStartService();
        ITrayService trayService = isDemoMode
            ? new FakeTrayService()
            : new WindowsTrayService();
        INotificationService notificationService = isDemoMode
            ? new FakeNotificationService()
            : new WindowsTrayNotificationService(trayService);
        IAlertNotifyStateStore alertStateStore = isDemoMode
            ? new InMemoryAlertNotifyStateStore()
            : new FileAlertNotifyStateStore(AppPaths.AlertNotifyStatePath);
        _trayService = trayService;

        MainViewModel mainViewModel;
        ISyncController? realSyncController = null;
        IFolderWatchController? realFolderWatchController = null;
        ISheetSyncController? realSheetSyncController = null;
        ISaleworkController? realSaleworkController = null;
        if (isDemoMode)
        {
            var fakeData = new FakeDataService(enableMutableDemoStore: true);
            ILegacyImporter fakeImporter = new FakeLegacyImporter(fakeData.GetCurrentTime);
            IPhotoProvider noOpPhotos = new NoOpPhotoProvider();
            IScanService fakeScan = new FakeScanService(fakeData.GetReturnOrders(40), fakeData.GetCurrentTime);
            ISyncController fakeSync = new FakeSyncController();
            IOrderEditService fakeEdit = new FakeOrderEditService(fakeData);
            IFolderWatchController fakeFolderWatch = new FakeFolderWatchController();
            ISheetSyncController fakeSheetSync = new FakeSheetSyncController();
            ISaleworkController fakeSalework = new FakeSaleworkController();
            IFileImportController fakeFileImport = new FakeFileImportController();
            IReturnWatchService fakeReturnWatch = new FakeReturnWatchService();
            var demoNow = fakeData.GetCurrentTime();
            var fakeCloud = new FakeCloudLink();
            fakeCloud.Set(
                CloudState.Connected,
                error: null,
                quota: new CloudQuota(demoNow.ToString("yyyy-MM-dd"), 8200, 100000, 5100, 100000, 90000, 5000000, "ok"),
                lastSync: demoNow,
                role: "owner",
                deviceName: "Máy tính kho (Demo)",
                baseUrl: "https://qlhh-demo.workers.dev");
            mainViewModel = new MainViewModel(
                fakeData,
                fakeData,
                fakeData,
                fakeData,
                fakeImporter,
                folderPicker,
                fileSaveService,
                isDemoMode: true,
                archiveSweeper: new NoOpArchiveSweeper(),
                photoProvider: noOpPhotos,
                photoViewerService: new WindowsPhotoViewerService(noOpPhotos),
                scanService: fakeScan,
                soundPlayer: soundPlayer,
                imagePicker: imagePicker,
                syncController: fakeSync,
                uiSettingsStore: new UiSettingsStore(null, isDemoMode: true),
                orderEditService: fakeEdit,
                orderActionDialogService: new WpfOrderActionDialogService(),
                folderWatchController: fakeFolderWatch,
                sheetSyncController: fakeSheetSync,
                saleworkController: fakeSalework,
                fileImportController: fakeFileImport,
                importFilePickerService: importFilePicker,
                returnWatchService: fakeReturnWatch,
                autoStartService: autoStartService,
                trayService: trayService,
                notificationService: notificationService,
                alertNotifyStateStore: alertStateStore,
                cloudLink: fakeCloud);
        }
        else
        {
            var initialized = AppStartupInitializer.TryInitializeDatabase(
                AppPaths.DatabasePath,
                (title, message) => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error));
            if (!initialized)
            {
                Shutdown(1);
                return;
            }

            var sqliteProvider = new SqliteOrderProvider(AppPaths.DatabasePath);
            ILegacyImporter sqliteImporter = new SqliteLegacyImporter(AppPaths.LegacyCopiesFolder, AppPaths.DatabasePath);
            // chế độ cloud: tự lưu trữ và tạo đơn mới đều đi qua cloud (không ghi riêng vào máy)
            IArchiveSweeper archiveSweeper = _cloud is { } cs
                ? new CloudArchiveSweeper(AppPaths.DatabasePath, () => cs.Link.CanWrite,
                    (ops, ct) => cs.Link.Api is { } a ? a.OpsAsync(ops, ct) : throw new QuanLyHangHoan.Data.Cloud.CloudApiException("unavailable", "Chưa kết nối cloud.", 0))
                : new SqliteArchiveSweeper(AppPaths.DatabasePath);
            // Công tắc cloud: --cloud hoặc đã ghép thiết bị. Tắt thì mọi thứ như cũ (SQLite cục bộ + MQTT).
            _cloud = CloudMode.IsEnabled(e.Args) ? new CloudMode(AppPaths.DatabasePath, new DpapiProtector()) : null;
            var cloudForPhotos = _cloud;
            // Cloud: ảnh trong máy trước, không có thì tải từ cloud (ảnh điện thoại chụp, ảnh đã chuyển lên) và lưu đệm
            IPhotoProvider photoProvider = cloudForPhotos is not null
                ? new CloudPhotoProvider(AppPaths.DatabasePath, () => cloudForPhotos.Link.Api)
                : new SqlitePhotoProvider(AppPaths.DatabasePath);
            if (cloudForPhotos is not null)
                _ = Task.Run(() => { try { QuanLyHangHoan.Data.Cloud.CloudPhotoCache.Prune(AppPaths.DatabasePath, TimeSpan.FromDays(90)); } catch (Exception) { } });
            IPhotoViewerService photoViewerService = new WindowsPhotoViewerService(photoProvider);
            IScanService sqliteScan = _cloud?.Scan ?? new SqliteScanService(AppPaths.DatabasePath);
            realSyncController = new RealSyncController(AppPaths.DatabasePath, AppPaths.SettingsPath);
            IOrderEditService sqliteEdit = _cloud?.Edit ?? new SqliteOrderEditService(AppPaths.DatabasePath);
            realFolderWatchController = new RealFolderWatchController(
                AppPaths.DatabasePath,
                AppPaths.FolderWatchStatePath,
                () => DateTime.Now);
            realSheetSyncController = new RealSheetSyncController(
                AppPaths.DatabasePath,
                AppPaths.SheetSyncStatePath,
                new DpapiProtector(),
                () => DateTime.Now);
            // Cloud: Sheet do Apps Script gửi thẳng lên cloud, máy tính không nạp Sheet (tránh hai nơi cùng ghi).
            if (_cloud is not null) realSheetSyncController = new CloudSheetSyncController(realSheetSyncController);
            realSaleworkController = new RealSaleworkController(
                AppPaths.DatabasePath,
                AppPaths.SaleworkAutoStatePath,
                AppPaths.SaleworkWebViewFolder,
                () => DateTime.Now);
            IFileImportController realFileImport = new RealFileImportController(AppPaths.DatabasePath, () => DateTime.Now);
            IReturnWatchService sqliteReturnWatch = _cloud?.ReturnWatch ?? new SqliteReturnWatchService(AppPaths.DatabasePath);
            var aiStore = new QuanLyHangHoan.Data.Ai.AiSettingsStore(AppPaths.BaseFolder);
            var notifCenter = isDemoMode ? null : new NotificationCenter(new FileNotificationStore(AppPaths.NotificationsPath));
            mainViewModel = new MainViewModel(
                sqliteProvider,
                sqliteProvider,
                sqliteProvider,
                sqliteProvider,
                sqliteImporter,
                folderPicker,
                fileSaveService,
                isDemoMode: false,
                archiveSweeper: archiveSweeper,
                photoProvider: photoProvider,
                photoViewerService: photoViewerService,
                deferInitialLoad: true,
                scanService: sqliteScan,
                soundPlayer: soundPlayer,
                imagePicker: imagePicker,
                syncController: realSyncController,
                uiSettingsStore: new UiSettingsStore(AppPaths.SettingsPath, isDemoMode: false),
                orderEditService: sqliteEdit,
                orderActionDialogService: new WpfOrderActionDialogService(),
                folderWatchController: realFolderWatchController,
                sheetSyncController: realSheetSyncController,
                saleworkController: realSaleworkController,
                fileImportController: realFileImport,
                importFilePickerService: importFilePicker,
                returnWatchService: sqliteReturnWatch,
                autoStartService: autoStartService,
                trayService: trayService,
                notificationService: notificationService,
                alertNotifyStateStore: alertStateStore,
                backupController: new SqliteBackupController(AppPaths.DatabasePath),
                aiSettingsStore: aiStore,
                aiOrderLoader: () => QuanLyHangHoan.Data.OrderRepository.Load(AppPaths.DatabasePath),
                aiActivityLoader: (limit, type, text) => QuanLyHangHoan.Data.AuditLogReader.Read(AppPaths.DatabasePath, limit, 0, type, text),
                aiReturnWatch: all => QuanLyHangHoan.Data.ReturnWatchService.ActiveMarks(AppPaths.DatabasePath, all),
                activityLogProvider: new SqliteActivityLogProvider(AppPaths.DatabasePath),
                orderExportService: new OrderExportService(fileSaveService),
                notificationCenter: notifCenter,
                newOrderCreator: isDemoMode ? null : _cloud is { } cn
                    ? new CloudNewOrderCreator(() => cn.Link.CanWrite,
                        (op, ct) => cn.Link.Api is { } a ? a.OpAsync(op, ct) : throw new QuanLyHangHoan.Data.Cloud.CloudApiException("unavailable", "Chưa kết nối cloud.", 0))
                    : new SqliteNewOrderCreator(AppPaths.DatabasePath),
                newOrderDialogService: new WindowsNewOrderDialogService(),
                diagnosticsProvider: new SqliteDiagnosticsProvider(AppPaths.DatabasePath, new UiSettingsStore(AppPaths.SettingsPath, isDemoMode: false)),
                // Chưa bật cloud: null giữ nguyên giao diện/hành vi cũ.
                cloudLink: _cloud?.Link,
                cloudImportLog: _cloud is { } cl
                    ? ct => cl.Link.Api is { } api ? api.ImportLogAsync(ct)
                        : throw new QuanLyHangHoan.Data.Cloud.CloudApiException("unavailable", "Chưa kết nối cloud.", 0)
                    : null);
            if (_cloud is { } cloud)
            {
                var vmForCloud = mainViewModel;
                cloud.Start(() => vmForCloud.ReloadAllDataAsync(), a => Dispatcher.BeginInvoke(a));
                // nạp xong ở máy này thì đẩy các đơn đã đổi lên cloud cho mọi máy
                realFolderWatchController.Completed += r => { _ = cloud.PublishAsync("folder"); };
                realSaleworkController.Completed += r => { _ = cloud.PublishAsync("salework"); };
                realFileImport.Completed += r => { _ = cloud.PublishAsync("file"); };
                cloud.Published += (note, isError) => Dispatcher.BeginInvoke(() => vmForCloud.ShowToast(note, isError));
                // AI dùng khóa trên cloud (token thiết bị); khóa trên máy không dùng nữa
                aiStore.UseCloud(() => cloud.AiAccess);
                cloud.AiAccessChanged += () => Dispatcher.BeginInvoke(() => vmForCloud.AiChatVm.RefreshKeyState());
                // gợi ý AI theo khung giờ (cloud viết lúc 8:00, 10:30, 13:30, 16:30, 19:00, 21:00): hỏi mỗi 10 phút, đưa vào chuông thông báo
                if (notifCenter is not null)
                {
                    var poller = new CloudDigestPoller(
                        ct => cloud.Link.Api is { } a ? a.AiDigestsAsync(12, ct) : throw new QuanLyHangHoan.Data.Cloud.CloudApiException("unavailable", "Chưa kết nối cloud.", 0),
                        System.IO.Path.Combine(AppPaths.BaseFolder, "ai_digest_seen.txt"));
                    _digestTimer = new System.Threading.Timer(async _ =>
                    {
                        foreach (var d in await poller.PollAsync())
                            _ = Dispatcher.BeginInvoke(() => notifCenter.Add(NotificationLevel.Info, "ai", $"✨ AI gợi ý lúc {d.Slot}", d.Text));
                    }, null, TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(10));
                }
            }
        }

        _mainViewModel = mainViewModel;
        var mainWindow = new MainWindow
        {
            DataContext = mainViewModel
        };
        MainWindow = mainWindow;

        trayService.OpenRequested += () => Dispatcher.BeginInvoke(() => mainWindow.RestoreFromTray());
        trayService.ExitRequested += () => Dispatcher.BeginInvoke(() => mainWindow.RequestExplicitExit());
        notificationService.NotificationClicked += () => Dispatcher.BeginInvoke(() =>
        {
            mainWindow.RestoreFromTray();
            mainViewModel.NavigateByKey("home");
        });
        trayService.Initialize();

        if (!startInTray)
        {
            mainWindow.Show();
        }

        if (!isDemoMode)
        {
            // chế độ cloud: không chạy MQTT/ntfy (điện thoại đồng bộ qua cloud)
            if (_cloud is null) realSyncController?.Start();
            if (realFolderWatchController?.Enabled == true)
            {
                realFolderWatchController.Start();
            }
            if (realSheetSyncController?.Enabled == true && realSheetSyncController.HasUrl)
            {
                realSheetSyncController.Start();
            }
            _ = mainViewModel.InitializeAsync();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainViewModel?.Dispose();
        _cloud?.Dispose();
        _trayService?.Dispose();
        _singleInstanceGuard?.Dispose();
        base.OnExit(e);
    }
}
