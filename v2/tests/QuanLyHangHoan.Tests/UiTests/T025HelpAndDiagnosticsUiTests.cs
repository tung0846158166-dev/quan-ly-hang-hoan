using System.IO;
using System.Windows;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Tests.UiTests;

/// <summary>Màn Trợ giúp: bắt đầu nhanh (✓/○ theo trạng thái thật), chẩn đoán không lộ dữ liệu nhạy cảm, mở thư mục. Không chạm đĩa thật.</summary>
public sealed class T025HelpAndDiagnosticsUiTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private const string FakeAppsScript = "https://script.google.com/macros/s/FAKE_SECRET_ID_ABC123/exec";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-t025-" + Guid.NewGuid().ToString("N"));
    public T025HelpAndDiagnosticsUiTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class Clip : IClipboardService { public string? Last; public void SetText(string text) => Last = text; }

    private static MainViewModel NewMain(IUiSettingsStore? store = null, Clip? clip = null, FakeDiagnosticsProvider? diag = null, FakeBackupController? backup = null)
    {
        var d = new FakeDataService(enableMutableDemoStore: true);
        return new MainViewModel(d, d, d, d, new FakeLegacyImporter(d.GetCurrentTime), isDemoMode: true,
            uiSettingsStore: store ?? new UiSettingsStore(null, true), clipboardService: clip, diagnosticsProvider: diag, backupController: backup,
            syncController: new FakeSyncController("ROOM-ABCDEFGH-123456"));
    }

    // ---------------- vị trí, mở trang ----------------
    [Fact]
    public async Task Help_is_the_last_page_in_the_common_group_and_opening_it_computes_five_steps_and_the_report()
    {
        using var vm = NewMain();
        var keys = vm.NavigationItems.Select(n => n.Key).ToList();
        Assert.True(keys.IndexOf("help") > keys.IndexOf("settings"));
        Assert.Equal("CHUNG", vm.NavigationItems.Single(n => n.Key == "help").Section);
        vm.NavigateByKey("help");
        Assert.IsType<HelpViewModel>(vm.CurrentPage);
        await vm.HelpVm.RefreshAsync();
        Assert.Equal(5, vm.HelpVm.Steps.Count);
        Assert.Contains("Chẩn đoán", vm.HelpVm.ReportText);
        Assert.Contains("Trạng thái các đường nạp", vm.HelpVm.ReportText);
        Assert.Contains("2.0.0-demo", vm.HelpVm.VersionText);
    }

    // ---------------- các bước ✓/○ ----------------
    [Fact]
    public async Task Steps_reflect_real_state_orders_present_sync_on_backup_on_but_auto_import_off()
    {
        using var vm = NewMain();
        await vm.HelpVm.RefreshAsync();
        var s = vm.HelpVm.Steps.ToDictionary(x => x.Number);
        Assert.True(s[1].Done);                                         // demo có đơn
        Assert.True(s[2].Done);                                         // đồng bộ bật (mặc định)
        Assert.True(s[3].Done);                                         // nhật ký có lần nạp
        Assert.False(s[4].Done);                                        // chưa bật nạp tự động
        Assert.True(s[5].Done);                                         // tự sao lưu bật (mặc định)
        Assert.Equal("○", s[4].Mark);
        Assert.Equal("✓", s[1].Mark);
        Assert.Equal("legacy_import", s[1].TargetKey);
    }

    [Fact]
    public async Task Steps_change_when_the_user_changes_settings_and_go_to_step_navigates()
    {
        var store = new UiSettingsStore(null, true);
        using var vm = NewMain(store);
        vm.SettingsVm.BackupEnabled = false;
        await vm.HelpVm.RefreshAsync();
        Assert.False(vm.HelpVm.Steps.Single(x => x.Number == 5).Done);
        vm.HelpVm.GoToStepCommand.Execute("folder_watch");
        Assert.IsType<FolderWatchViewModel>(vm.CurrentPage);
        vm.HelpVm.GoToStepCommand.Execute(null);                       // khóa rỗng: không làm gì, không sập
        Assert.IsType<FolderWatchViewModel>(vm.CurrentPage);
    }

    // ---------------- chẩn đoán ----------------
    [Fact]
    public async Task Copy_all_puts_the_report_on_the_clipboard_and_never_includes_the_apps_script_address_or_tracking_codes()
    {
        var clip = new Clip();
        using var vm = NewMain(clip: clip);
        vm.GoogleSheetVm.LastNote = "Lỗi khi gọi " + FakeAppsScript;     // câu trạng thái lỡ có địa chỉ: phải bị ẩn
        await vm.HelpVm.RefreshAsync();
        vm.HelpVm.CopyAllCommand.Execute(null);
        Assert.NotNull(clip.Last);
        Assert.Contains("Phiên bản app", clip.Last);
        Assert.DoesNotContain("script.google.com", clip.Last, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FAKE_SECRET_ID_ABC123", clip.Last);
        Assert.Contains("đã ẩn", clip.Last);
        Assert.True(vm.HelpVm.ReportHasNoSecrets);
        Assert.Contains("Đã sao chép", vm.HelpVm.CopyStatus);
    }

    [Fact]
    public async Task Refresh_rebuilds_the_report_after_state_changes()
    {
        using var vm = NewMain();
        await vm.HelpVm.RefreshAsync();
        vm.FolderWatchVm.LastNote = "Đã nạp 3 file";
        Assert.DoesNotContain("Đã nạp 3 file", vm.HelpVm.ReportText);
        await vm.HelpVm.RefreshCommand.ExecuteAsync(null);
        Assert.Contains("Đã nạp 3 file", vm.HelpVm.ReportText);
    }

    // ---------------- mở thư mục ----------------
    [Fact]
    public void Open_folder_buttons_go_through_the_fakes()
    {
        var diag = new FakeDiagnosticsProvider();
        var backup = new FakeBackupController();
        using var vm = NewMain(diag: diag, backup: backup);
        vm.HelpVm.OpenDataFolderCommand.Execute(null);
        vm.HelpVm.OpenBackupFolderCommand.Execute(null);
        Assert.Equal(1, diag.OpenCalls);
        Assert.Equal(1, backup.OpenFolderCalls);
    }

    // ---------------- bản thật trên DB tạm / DB không tồn tại ----------------
    [Fact]
    public void Real_provider_reports_a_missing_database_without_creating_anything_and_masks_the_room()
    {
        var db = Path.Combine(_dir, "khong-co.db");
        var store = new UiSettingsStore(Path.Combine(_dir, "settings.json"), false);
        var room = store.Load().Room;
        var diag = new SqliteDiagnosticsProvider(db, store, () => Now, openFolder: _ => { });
        var text = diag.BuildReport();
        Assert.Contains("CHƯA CÓ", text);
        Assert.DoesNotContain(room, text);                              // chỉ hiện 4 ký tự đầu
        Assert.False(File.Exists(db));
    }

    [Fact]
    public void Real_provider_counts_orders_of_a_temp_database_but_never_prints_order_data()
    {
        var src = Path.Combine(_dir, "src"); Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "du_lieu_hang_hoan.json"), "[{\"platform\":\"Shopee\",\"orderId\":\"BIMAT123\",\"track\":\"SPXBIMAT999\",\"status\":\"Chưa trả\",\"items\":[{\"product\":\"Áo-bí-mật\",\"qty\":1}]}]");
        var db = Path.Combine(_dir, "t.db");
        LegacyImporter.Import(src, db);
        var diag = new SqliteDiagnosticsProvider(db, new UiSettingsStore(null, true), () => Now, openFolder: _ => { });
        var text = diag.BuildReport();
        Assert.Contains("bảng chính 1", text);
        Assert.DoesNotContain("BIMAT123", text);
        Assert.DoesNotContain("SPXBIMAT999", text);
        Assert.DoesNotContain("Áo-bí-mật", text);
    }

    [Fact]
    public void Real_provider_open_folder_calls_the_opener_once_with_the_data_folder()
    {
        var opened = new List<string>();
        var diag = new SqliteDiagnosticsProvider(Path.Combine(_dir, "x.db"), new UiSettingsStore(null, true), () => Now, openFolder: opened.Add);
        diag.OpenDataFolder();
        Assert.Equal(new[] { _dir }, opened.ToArray());
    }

    // ---------------- ảnh chụp ----------------
    [Fact]
    public void RenderScreenshots_Help_ToReportsFolder()
    {
        var repo = FindRepoRoot();
        var outDir = Path.Combine(repo, "tasks", "reports", "T025-screens");
        Directory.CreateDirectory(outDir);
        WpfTestHost.Run(() =>
        {
            using var vm = NewMain();
            var window = new MainWindow
            {
                DataContext = vm, Width = 1360, Height = 820, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false,
            };
            try
            {
                window.Show();
                WpfTestHost.PumpDispatcher();
                vm.SetTheme(ThemeService.LightTheme);
                vm.NavigateByKey("help");
                WpfTestHost.RunTask(() => vm.HelpVm.RefreshAsync());
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "01-help-light.png"));
                vm.SetTheme(ThemeService.DarkTheme);
                vm.NavigateByKey("help");
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "02-help-dark.png"));
            }
            finally
            {
                ThemeService.ApplyTheme(ThemeService.LightTheme);
                window.Close();
                WpfTestHost.PumpDispatcher();
            }
        });
        Assert.True(File.Exists(Path.Combine(outDir, "01-help-light.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "02-help-dark.png")));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tasks")) && Directory.Exists(Path.Combine(dir.FullName, "v2"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Không tìm thấy thư mục gốc repo.");
    }
}
