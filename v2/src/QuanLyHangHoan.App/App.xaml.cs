using System.Windows;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Data.Sheet;

namespace QuanLyHangHoan.App;

public partial class App : Application
{
    private MainViewModel? _mainViewModel;
    private ISingleInstanceGuard? _singleInstanceGuard;
    private ITrayService? _trayService;

    protected override void OnStartup(StartupEventArgs e)
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
                alertNotifyStateStore: alertStateStore);
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
            IArchiveSweeper archiveSweeper = new SqliteArchiveSweeper(AppPaths.DatabasePath);
            IPhotoProvider photoProvider = new SqlitePhotoProvider(AppPaths.DatabasePath);
            IPhotoViewerService photoViewerService = new WindowsPhotoViewerService(photoProvider);
            IScanService sqliteScan = new SqliteScanService(AppPaths.DatabasePath);
            realSyncController = new RealSyncController(AppPaths.DatabasePath, AppPaths.SettingsPath);
            IOrderEditService sqliteEdit = new SqliteOrderEditService(AppPaths.DatabasePath);
            realFolderWatchController = new RealFolderWatchController(
                AppPaths.DatabasePath,
                AppPaths.FolderWatchStatePath,
                () => DateTime.Now);
            realSheetSyncController = new RealSheetSyncController(
                AppPaths.DatabasePath,
                AppPaths.SheetSyncStatePath,
                new DpapiProtector(),
                () => DateTime.Now);
            realSaleworkController = new RealSaleworkController(
                AppPaths.DatabasePath,
                AppPaths.SaleworkAutoStatePath,
                AppPaths.SaleworkWebViewFolder,
                () => DateTime.Now);
            IFileImportController realFileImport = new RealFileImportController(AppPaths.DatabasePath, () => DateTime.Now);
            IReturnWatchService sqliteReturnWatch = new SqliteReturnWatchService(AppPaths.DatabasePath);
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
                activityLogProvider: new SqliteActivityLogProvider(AppPaths.DatabasePath),
                orderExportService: new OrderExportService(fileSaveService),
                notificationCenter: isDemoMode ? null : new NotificationCenter(new FileNotificationStore(AppPaths.NotificationsPath)),
                diagnosticsProvider: new SqliteDiagnosticsProvider(AppPaths.DatabasePath, new UiSettingsStore(AppPaths.SettingsPath, isDemoMode: false)));
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
            realSyncController?.Start();
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
        _trayService?.Dispose();
        _singleInstanceGuard?.Dispose();
        base.OnExit(e);
    }
}
