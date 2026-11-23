using System.IO;
using System.Windows;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.App.Views;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Tests.UiTests;

/// <summary>Màn Nhật ký hoạt động (chỉ xem). DB tạm + dữ liệu GIẢ; không chạm %LocalAppData% thật.</summary>
public sealed class T023ActivityLogUiTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-t023-" + Guid.NewGuid().ToString("N"));
    public T023ActivityLogUiTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class Clip : IClipboardService { public string? Last; public void SetText(string text) => Last = text; }

    private static List<AuditEntry> Entries(int n, string type = "edit") =>
        Enumerable.Range(1, n).Select(i => new AuditEntry(i, $"23/11/2026 10:{i % 60:00}:00", type, AuditLogReader.LabelOf(type), $"dòng {i}")).ToList();

    private static ActivityLogViewModel Vm(IActivityLogProvider p, Clip? clip = null) => new(p, clip, debounce: TimeSpan.Zero);

    // ---------------- hiển thị ----------------
    [Fact]
    public async Task Entries_show_newest_first_with_labels_icons_and_total()
    {
        var vm = Vm(new FakeActivityLogProvider(new[]
        {
            new AuditEntry(1, "23/11/2026 09:00:00", "import", "Nạp đơn", "nạp file"),
            new AuditEntry(2, "23/11/2026 10:00:00", "edit", "Sửa đơn", "sửa A"),
            new AuditEntry(3, "23/11/2026 11:00:00", "phone_new_order", "Đơn mới từ điện thoại", "đơn mới"),
        }));
        await vm.ReloadAsync();
        Assert.Equal(new long[] { 3, 2, 1 }, vm.Entries.Select(e => e.Seq).ToArray());
        Assert.Equal("📱", vm.Entries[0].Icon);
        Assert.Equal("Sửa đơn", vm.Entries[1].TypeLabel);
        Assert.Equal("Hiển thị 3 / tổng 3", vm.ShownText);
        Assert.False(vm.HasMore);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task Empty_log_says_so_and_does_not_crash()
    {
        var vm = Vm(new FakeActivityLogProvider(Array.Empty<AuditEntry>()));
        await vm.ReloadAsync();
        Assert.Empty(vm.Entries);
        Assert.True(vm.IsEmpty);
        Assert.Equal("Hiển thị 0 / tổng 0", vm.ShownText);
    }

    // ---------------- lọc ----------------
    [Fact]
    public async Task Type_filter_and_text_filter_work_and_type_list_comes_from_the_data()
    {
        var fake = new FakeActivityLogProvider(new[]
        {
            new AuditEntry(1, "t", "import", "Nạp đơn", "nạp SPXA1"),
            new AuditEntry(2, "t", "edit", "Sửa đơn", "sửa SPXA1"),
            new AuditEntry(3, "t", "edit", "Sửa đơn", "sửa SPXB2"),
        });
        var vm = Vm(fake);
        await vm.ReloadAsync();
        Assert.Equal(new[] { "", "edit", "import" }, vm.TypeOptions.Select(o => o.Key).ToArray());

        vm.SelectedType = vm.TypeOptions.Single(o => o.Key == "edit");
        await Task.Delay(100); await vm.ReloadAsync();
        Assert.Equal(2, vm.Entries.Count);
        Assert.All(vm.Entries, e => Assert.Equal("edit", e.Type));

        vm.SearchText = "SPXB2";
        await Task.Delay(150);
        Assert.Single(vm.Entries);
        Assert.Contains("SPXB2", vm.Entries[0].Detail);
        Assert.Equal("edit", vm.SelectedType!.Key);                       // lọc loại giữ nguyên khi danh sách loại được nạp lại
    }

    // ---------------- phân trang ----------------
    [Fact]
    public async Task Load_more_pages_by_200_and_never_loads_everything_at_once()
    {
        var fake = new FakeActivityLogProvider(Entries(450));
        var vm = Vm(fake);
        await vm.ReloadAsync();
        Assert.Equal(200, vm.Entries.Count);
        Assert.True(vm.HasMore);
        Assert.Equal("Hiển thị 200 / tổng 450 (còn nữa: bấm \"Tải thêm\")", vm.ShownText);

        await vm.LoadMoreCommand.ExecuteAsync(null);
        Assert.Equal(400, vm.Entries.Count);
        Assert.Equal(450, vm.Entries[0].Seq);                              // mới nhất trên cùng
        Assert.Equal(51, vm.Entries[^1].Seq);
        Assert.True(vm.HasMore);

        await vm.LoadMoreCommand.ExecuteAsync(null);
        Assert.Equal(450, vm.Entries.Count);
        Assert.False(vm.HasMore);
        Assert.Equal(450, vm.Entries.Select(e => e.Seq).Distinct().Count());   // không trùng dòng giữa các trang
    }

    // ---------------- làm mới ----------------
    [Fact]
    public async Task Refresh_shows_new_activity_and_quiet_refresh_is_throttled()
    {
        var fake = new FakeActivityLogProvider(Entries(3));
        var vm = Vm(fake);
        await vm.ReloadAsync();
        var reads = fake.ReadCalls;
        fake.Add("scan", "Nhận kiện SPXZ9");
        await vm.RefreshQuietlyAsync();
        Assert.Equal(reads, fake.ReadCalls);                               // chưa quá 2 giây: không đọc lại
        await vm.RefreshCommand.ExecuteAsync(null);                        // làm mới bằng tay: luôn đọc
        Assert.Equal("Nhận kiện SPXZ9", vm.Entries[0].Detail);
        Assert.Equal(4, vm.Entries.Count);
    }

    // ---------------- sao chép, chỉ xem ----------------
    [Fact]
    public async Task Copy_detail_uses_the_clipboard_service_and_the_screen_has_no_write_commands()
    {
        var clip = new Clip();
        var vm = Vm(new FakeActivityLogProvider(Entries(2)), clip);
        await vm.ReloadAsync();
        vm.CopyDetailCommand.Execute(vm.Entries[0]);
        Assert.Contains("dòng 2", clip.Last);
        Assert.Contains("Sửa đơn", clip.Last);
        Assert.DoesNotContain(typeof(ActivityLogViewModel).GetMethods().Select(m => m.Name), n => n.Contains("Delete") || n.Contains("Remove") || n.Contains("Clear") && n != "Clear");
    }

    // ---------------- đọc thật từ DB tạm: sau một thao tác sửa có dòng mới ----------------
    [Fact]
    public async Task Real_provider_reads_audit_log_of_a_temp_database_and_sees_a_new_edit_after_refresh()
    {
        var src = Path.Combine(_dir, "src"); Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "du_lieu_hang_hoan.json"),
            "[{\"platform\":\"Shopee\",\"orderId\":\"A\",\"track\":\"SPXA1\",\"status\":\"Chưa trả\",\"items\":[]}]");
        var db = Path.Combine(_dir, "t.db");
        LegacyImporter.Import(src, db);
        var vm = Vm(new SqliteActivityLogProvider(db));
        await vm.ReloadAsync();
        var before = vm.Entries.Count;

        var id = OrderRepository.Load(db).Single().Order.Id;
        OrderEditor.SetField(db, id, EditField.Status, ReceiveStatus.Received, Now);
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(before + 1, vm.Entries.Count);
        Assert.Equal("Sửa đơn", vm.Entries[0].TypeLabel);
        Assert.Contains("edit", vm.TypeOptions.Select(o => o.Key));

        vm.SearchText = "100%_khong-co";                                   // ký tự % và _ không thành ký tự đại diện
        await Task.Delay(150);
        Assert.Empty(vm.Entries);
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public async Task Real_provider_on_a_missing_database_shows_an_empty_list_without_throwing()
    {
        var vm = Vm(new SqliteActivityLogProvider(Path.Combine(_dir, "khong-co.db")));
        await vm.ReloadAsync();
        Assert.Empty(vm.Entries);
        Assert.False(File.Exists(Path.Combine(_dir, "khong-co.db")));
    }

    // ---------------- vị trí, mở trang, ảnh chụp ----------------
    private static MainViewModel NewMain(FakeActivityLogProvider fake)
    {
        var d = new FakeDataService(enableMutableDemoStore: true);
        return new MainViewModel(d, d, d, d, new FakeLegacyImporter(d.GetCurrentTime), isDemoMode: true, activityLogProvider: fake);
    }

    [Fact]
    public async Task Page_is_in_the_common_group_before_settings_and_opening_it_loads_the_log()
    {
        var fake = new FakeActivityLogProvider(Entries(5));
        using var vm = NewMain(fake);
        var keys = vm.NavigationItems.Select(n => n.Key).ToList();
        Assert.True(keys.IndexOf("activity") >= 0 && keys.IndexOf("activity") < keys.IndexOf("settings"));
        Assert.Equal("CHUNG", vm.NavigationItems.Single(n => n.Key == "activity").Section);
        vm.NavigateByKey("activity");
        Assert.IsType<ActivityLogViewModel>(vm.CurrentPage);
        await Task.Delay(300);
        Assert.Equal(5, vm.ActivityLogVm.Entries.Count);
    }

    [Fact]
    public void RenderScreenshots_ActivityLog_ToReportsFolder()
    {
        var repo = FindRepoRoot();
        var outDir = Path.Combine(repo, "tasks", "reports", "T023-screens");
        Directory.CreateDirectory(outDir);
        WpfTestHost.Run(() =>
        {
            using var vm = NewMain(new FakeActivityLogProvider());
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
                vm.NavigateByKey("activity");
                WpfTestHost.RunTask(() => vm.ActivityLogVm.ReloadAsync());
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "01-activity-light.png"));

                vm.SetTheme(ThemeService.DarkTheme);
                vm.NavigateByKey("activity");
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "02-activity-dark.png"));

                vm.SetTheme(ThemeService.LightTheme);
                vm.ActivityLogVm.SearchText = "khong-co-dong-nao";
                WpfTestHost.RunTask(() => vm.ActivityLogVm.ReloadAsync());
                WpfTestHost.PumpDispatcher();
                WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "03-activity-empty.png"));
            }
            finally
            {
                ThemeService.ApplyTheme(ThemeService.LightTheme);
                window.Close();
                WpfTestHost.PumpDispatcher();
            }
        });
        foreach (var f in new[] { "01-activity-light.png", "02-activity-dark.png", "03-activity-empty.png" })
            Assert.True(File.Exists(Path.Combine(outDir, f)), f);
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
