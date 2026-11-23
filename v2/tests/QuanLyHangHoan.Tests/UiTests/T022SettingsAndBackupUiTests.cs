using System.IO;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Tests.UiTests;

/// <summary>Màn Cài đặt: công tắc chạy nền (chuyển từ Trang chủ), mã phòng, sao lưu. DB tạm, bản giả; không chạm %LocalAppData% thật.</summary>
public sealed class T022SettingsAndBackupUiTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-t022-" + Guid.NewGuid().ToString("N"));
    public T022SettingsAndBackupUiTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class Clip : IClipboardService { public string? Last; public void SetText(string text) => Last = text; }

    private string MakeDb()
    {
        var src = Path.Combine(_dir, "src"); Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "du_lieu_hang_hoan.json"), "[{\"platform\":\"Shopee\",\"orderId\":\"A\",\"track\":\"SPXA1\",\"status\":\"Chưa trả\",\"items\":[]}]");
        var db = Path.Combine(_dir, "t.db");
        LegacyImporter.Import(src, db);
        return db;
    }

    private static MainViewModel NewMain(FakeBackupController backup, IUiSettingsStore store, FakeAutoStartService auto, Clip? clip = null)
    {
        var fake = new FakeDataService(enableMutableDemoStore: true);
        return new MainViewModel(fake, fake, fake, fake, new FakeLegacyImporter(fake.GetCurrentTime), isDemoMode: true,
            uiSettingsStore: store, autoStartService: auto, backupController: backup, clipboardService: clip,
            syncController: new FakeSyncController("ROOM-ABCDEFGH-123456"));
    }

    // ---------------- vị trí và công tắc ----------------
    [Fact]
    public void Settings_page_exists_last_in_common_group_and_home_no_longer_has_background_switches()
    {
        using var vm = NewMain(new FakeBackupController(), new UiSettingsStore(null, true), new FakeAutoStartService());
        var item = Assert.Single(vm.NavigationItems.Where(n => n.Key == "settings"));
        Assert.Equal("CHUNG", item.Section);
        Assert.Equal("⚙️", item.Icon);
        vm.NavigateByKey("settings");
        Assert.IsType<SettingsViewModel>(vm.CurrentPage);
        Assert.DoesNotContain(typeof(HomeViewModel).GetProperties(), p => p.Name is "AutoStart" or "MinimizeToTray" or "NotifyAlerts" or "Sounds");
    }

    [Fact]
    public void Background_switches_on_settings_page_are_saved_and_applied_through_the_same_services()
    {
        var store = new UiSettingsStore(null, true);
        var auto = new FakeAutoStartService();
        using var vm = NewMain(new FakeBackupController(), store, auto);

        vm.SettingsVm.AutoStart = true;
        Assert.True(auto.IsEnabled());
        Assert.True(store.Load().AutoStart);
        vm.SettingsVm.MinimizeToTray = false;
        Assert.False(store.Load().MinimizeToTray);
        vm.SettingsVm.NotifyAlerts = false;
        Assert.False(store.Load().NotifyAlerts);
        vm.SettingsVm.Sounds = false;
        Assert.False(store.Load().Sounds);
        Assert.True(vm.ScanReceiveVm.IsSoundEnabled == false);
        // trạng thái hai chiều: đổi ở MainViewModel (ví dụ từ khay) thì màn Cài đặt thấy ngay
        vm.NotifyAlerts = true;
        Assert.True(vm.SettingsVm.NotifyAlerts);
    }

    [Fact]
    public void Settings_keep_the_room_code_and_other_fields_when_a_switch_changes()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new UiSettingsStore(path, false);
        var room = store.Load().Room;
        Assert.False(string.IsNullOrWhiteSpace(room));
        using var vm = NewMain(new FakeBackupController(), store, new FakeAutoStartService());
        vm.SettingsVm.Sounds = false;
        Assert.Equal(room, new UiSettingsStore(path, false).Load().Room);
    }

    // ---------------- mã phòng ----------------
    [Fact]
    public void Room_code_is_shown_and_copy_uses_the_clipboard_service()
    {
        var clip = new Clip();
        using var vm = NewMain(new FakeBackupController(), new UiSettingsStore(null, true), new FakeAutoStartService(), clip);
        Assert.Equal("ROOM-ABCDEFGH-123456", vm.SettingsVm.RoomCode);
        vm.SettingsVm.CopyRoomCommand.Execute(null);
        Assert.Equal("ROOM-ABCDEFGH-123456", clip.Last);
        Assert.Contains("Đã sao chép", vm.SettingsVm.RoomStatus);
        vm.SettingsVm.OpenPhoneSyncCommand.Execute(null);
        Assert.IsType<PhoneSyncViewModel>(vm.CurrentPage);
    }

    // ---------------- sao lưu (giao diện) ----------------
    [Fact]
    public async Task Backup_now_adds_a_row_shows_success_and_error_is_shown_in_red_without_changing_the_list()
    {
        var backup = new FakeBackupController(() => Now);
        using var vm = NewMain(backup, new UiSettingsStore(null, true), new FakeAutoStartService());
        var s = vm.SettingsVm;
        var before = s.Backups.Count;
        await s.BackupNowCommand.ExecuteAsync(null);
        Assert.Equal(before + 1, s.Backups.Count);
        Assert.False(s.BackupStatusIsError);
        Assert.Contains("Đã sao lưu", s.BackupStatus);
        Assert.False(s.IsBackupBusy);

        backup.FailNext = true;
        await s.BackupNowCommand.ExecuteAsync(null);
        Assert.True(s.BackupStatusIsError);
        Assert.Contains("lỗi", s.BackupStatus);
        Assert.Equal(before + 1, s.Backups.Count);
    }

    [Fact]
    public void Open_folder_goes_through_the_fake_and_backup_settings_are_saved_and_schedule_applied()
    {
        var backup = new FakeBackupController();
        var store = new UiSettingsStore(null, true);
        using var vm = NewMain(backup, store, new FakeAutoStartService());
        vm.SettingsVm.OpenBackupFolderCommand.Execute(null);
        Assert.Equal(1, backup.OpenFolderCalls);

        vm.SettingsVm.BackupEveryHours = 6;
        vm.SettingsVm.BackupKeepDays = 14;
        vm.SettingsVm.BackupEnabled = false;
        var saved = store.Load();
        Assert.Equal(6, saved.BackupEveryHours);
        Assert.Equal(14, saved.BackupKeepDays);
        Assert.False(saved.BackupEnabled);
        Assert.False(backup.ScheduleEnabled);                       // demo: không bao giờ bật lịch thật
    }

    [Fact]
    public void Demo_mode_never_enables_the_real_schedule_even_when_backup_is_on()
    {
        var backup = new FakeBackupController();
        using var vm = NewMain(backup, new UiSettingsStore(null, true), new FakeAutoStartService());
        vm.SettingsVm.BackupEnabled = true;
        Assert.False(backup.ScheduleEnabled);
    }

    [Fact]
    public void Latest_backup_text_and_hours_choices_follow_the_data()
    {
        using var vm = NewMain(new FakeBackupController(() => Now), new UiSettingsStore(null, true), new FakeAutoStartService());
        Assert.Contains("Bản gần nhất", vm.SettingsVm.LatestBackupText);
        vm.SettingsVm.SyncBackupSettings(true, 3, 10);              // giá trị lạ vẫn có trong danh sách chọn
        Assert.Contains(3, vm.SettingsVm.EveryHoursChoices);
        Assert.Contains(10, vm.SettingsVm.KeepDaysChoices);
    }

    // ---------------- sao lưu thật trên DB tạm ----------------
    [Fact]
    public async Task Real_controller_backs_up_a_temp_database_lists_it_and_the_timer_logic_respects_the_schedule()
    {
        var db = MakeDb();
        var now = Now;
        using var real = new SqliteBackupController(db, () => now, openFolder: _ => { });
        Assert.Empty(real.List());
        Assert.Null(real.Tick());                                    // chưa bật lịch: không làm gì

        var r = await real.BackupNowAsync();
        Assert.True(r.Ok, r.Message);
        var one = Assert.Single(real.List());
        Assert.True(one.Bytes > 0);

        real.ApplySchedule(true, everyHours: 2, keepDays: 7);
        Assert.Null(real.Tick());                                    // bản mới nhất còn mới (< 2 giờ)
        now = Now.AddHours(3);
        var due = real.Tick();
        Assert.True(due?.Ok);
        Assert.Equal(2, real.List().Count);

        real.ApplySchedule(false, 2, 7);
        now = Now.AddHours(30);
        Assert.Null(real.Tick());                                    // tắt thì không sao lưu
        Assert.Equal(2, real.List().Count);
    }

    [Fact]
    public async Task Real_controller_reports_an_error_for_a_missing_database_without_creating_files()
    {
        using var real = new SqliteBackupController(Path.Combine(_dir, "khong-co.db"), () => Now, openFolder: _ => { });
        var r = await real.BackupNowAsync();
        Assert.False(r.Ok);
        Assert.False(Directory.Exists(real.FolderPath));
    }

    [Fact]
    public void Real_controller_open_folder_creates_the_folder_and_calls_the_opener_once()
    {
        var db = MakeDb();
        var opened = new List<string>();
        using var real = new SqliteBackupController(db, () => Now, openFolder: opened.Add);
        real.OpenFolder();
        Assert.Single(opened);
        Assert.True(Directory.Exists(opened[0]));
    }
}
