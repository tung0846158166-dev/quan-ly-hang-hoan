using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.Tests.UiTests;

/// <summary>
/// Chạy thử giao diện THẬT (nguồn dữ liệu SQLite thật, không phải bản giả) trên một BẢN SAO dữ liệu.
/// Chỉ chạy khi đặt QLHH_REAL_SANDBOX = thư mục chứa data.db (bản sao, không phải dữ liệu đang dùng); QLHH_REAL_SHOTS = nơi lưu ảnh chụp.
/// Không đặt biến thì bỏ qua (không ảnh hưởng bộ test thường). Test chỉ ĐỌC: dùng một bản sao tạm của data.db.
/// </summary>
public sealed class RealDataSandboxSmokeTests
{
    private static readonly string? Sandbox = Environment.GetEnvironmentVariable("QLHH_REAL_SANDBOX");
    private static readonly string? Shots = Environment.GetEnvironmentVariable("QLHH_REAL_SHOTS");

    [Fact]
    public void Real_providers_on_a_copy_of_real_data_load_every_screen_and_render_it()
    {
        if (string.IsNullOrWhiteSpace(Sandbox) || !File.Exists(Path.Combine(Sandbox, "data.db"))) return;   // không có bản sao: bỏ qua

        var temp = Path.Combine(Path.GetTempPath(), "qlhh-realsmoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var db = Path.Combine(temp, "data.db");
        File.Copy(Path.Combine(Sandbox, "data.db"), db);
        var outDir = string.IsNullOrWhiteSpace(Shots) ? Path.Combine(temp, "shots") : Shots;
        Directory.CreateDirectory(outDir);
        var report = new StringBuilder();

        try
        {
            WpfTestHost.Run(() =>
            {
                var provider = new SqliteOrderProvider(db);
                var importer = new SqliteLegacyImporter(Path.Combine(temp, "legacy"), db);
                using var vm = new MainViewModel(provider, provider, provider, provider, importer, isDemoMode: false,
                    uiSettingsStore: new UiSettingsStore(null, isDemoMode: true), dispatchToUi: a => a(),
                    orderEditService: new SqliteOrderEditService(db), newOrderCreator: new SqliteNewOrderCreator(db));
                WpfTestHost.RunTask(() => vm.InitializeAsync());

                report.AppendLine($"Shopee hoan: {vm.ShopeeReturnVm.TotalCount} | Shopee huy: {vm.ShopeeCancelledVm.TotalCount} | TikTok: {vm.TikTokReturnVm.TotalCount} | Don moi: {vm.NewCreatedVm.TotalCount} | Luu tru: {vm.ArchivedOrdersVm.TotalCount} | Da xoa: {vm.DeletedOrdersVm.TotalCount}");
                vm.NavigateByKey("stats");
                report.AppendLine($"Thong ke: {vm.StatisticsVm.TotalOrders} don | {vm.StatisticsVm.ProductRows.Count} san pham | {vm.StatisticsVm.ProductSummaryText}");
                foreach (var p in vm.StatisticsVm.ProductRows.Take(5)) report.AppendLine($"   #{p.Rank} {p.Units} cai {p.ShareText} | {p.PremiumText} | {p.Name}");

                var window = new MainWindow
                {
                    DataContext = vm, Width = 1360, Height = 900, WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false,
                };
                try
                {
                    window.Show();
                    WpfTestHost.PumpDispatcher();
                    vm.SetTheme(ThemeService.LightTheme);
                    foreach (var key in new[] { "home", "stats", "tiktok_return", "new_orders", "archive", "settings" })
                    {
                        vm.NavigateByKey(key);
                        WpfTestHost.PumpDispatcher();
                        WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, $"real-{key}.png"));
                    }
                }
                finally { ThemeService.ApplyTheme(ThemeService.LightTheme); window.Close(); WpfTestHost.PumpDispatcher(); }
            });
            File.WriteAllText(Path.Combine(outDir, "real-report.txt"), report.ToString(), new UTF8Encoding(true));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(temp, true); } catch { /* bỏ qua */ }
        }
    }
}
