using System.IO;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-set-" + Guid.NewGuid().ToString("N"));
    private string Path1 => Path.Combine(_dir, "sub", "settings.json");
    public AppSettingsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void First_load_creates_file_with_strong_random_room_and_default_brokers()
    {
        var s = AppSettings.LoadOrCreate(Path1);

        Assert.True(File.Exists(Path1));
        Assert.True(SyncRoom.IsStrong(s.Room));
        Assert.Equal(MqttTransport.DefaultBrokers, s.Brokers);
        Assert.True(s.SyncEnabled);
    }

    [Fact]
    public void Ntfy_is_enabled_by_default_and_old_settings_file_without_it_still_loads()
    {
        Assert.True(AppSettings.LoadOrCreate(Path1).NtfyEnabled);

        var legacy = Path.Combine(_dir, "old.json"); // tệp cài đặt cũ chưa có trường NtfyEnabled
        File.WriteAllText(legacy, "{\"Room\":\"phong-cu-cua-toi-123456\",\"SyncEnabled\":true,\"Brokers\":[\"wss://x\"]}");
        var s = AppSettings.LoadOrCreate(legacy);
        Assert.True(s.NtfyEnabled);
        Assert.Equal("phong-cu-cua-toi-123456", s.Room);

        s.NtfyEnabled = false;
        s.Save(legacy);
        Assert.False(AppSettings.LoadOrCreate(legacy).NtfyEnabled);
    }

    [Fact]
    public void Ui_preferences_default_sensibly_roundtrip_and_old_files_still_load()
    {
        var s = AppSettings.LoadOrCreate(Path1);
        Assert.Equal("light", s.Theme);
        Assert.Empty(s.CollapsedNavGroups);
        Assert.Null(s.Window);
        Assert.Equal("", s.LastPage);

        s.Theme = "dark";
        s.CollapsedNavGroups = new() { "SHOPEE", "TIKTOK" };
        s.LastPage = "scan";
        s.Window = new WindowPlacement { Left = 10, Top = 20, Width = 1200, Height = 800, Maximized = true };
        s.Save(Path1);

        var again = AppSettings.LoadOrCreate(Path1);
        Assert.Equal("dark", again.Theme);
        Assert.Equal(new[] { "SHOPEE", "TIKTOK" }, again.CollapsedNavGroups);
        Assert.Equal("scan", again.LastPage);
        Assert.Equal(1200, again.Window!.Width);
        Assert.True(again.Window.Maximized);

        var legacy = Path.Combine(_dir, "old2.json"); // tệp cũ từ trước khi có các trường này
        File.WriteAllText(legacy, "{\"Room\":\"phong-cu-cua-toi-123456\"}");
        Assert.Equal("light", AppSettings.LoadOrCreate(legacy).Theme);
    }

    [Fact]
    public void Unknown_theme_value_falls_back_to_light()
    {
        var s = AppSettings.LoadOrCreate(Path1);
        s.Theme = "hồng";
        s.Save(Path1);
        Assert.Equal("light", AppSettings.LoadOrCreate(Path1).Theme);
    }

    [Fact]
    public void Second_load_returns_same_room_not_a_new_one()
    {
        var a = AppSettings.LoadOrCreate(Path1);
        var b = AppSettings.LoadOrCreate(Path1);
        Assert.Equal(a.Room, b.Room);
    }

    [Fact]
    public void Save_then_load_roundtrips_and_leaves_no_temp_file()
    {
        var s = AppSettings.LoadOrCreate(Path1);
        s.Room = "phong-rieng-cua-toi-12345"; s.SyncEnabled = false;
        s.Save(Path1);

        var again = AppSettings.LoadOrCreate(Path1);
        Assert.Equal("phong-rieng-cua-toi-12345", again.Room);
        Assert.False(again.SyncEnabled);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Path1)!, "*.tmp"));
    }

    [Fact]
    public void Corrupt_file_is_kept_aside_not_overwritten_silently()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path1)!);
        File.WriteAllText(Path1, "{ hỏng");

        var s = AppSettings.LoadOrCreate(Path1);

        Assert.True(SyncRoom.IsStrong(s.Room));                       // vẫn chạy được với cài đặt mới
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(Path1)!, "settings.json.corrupt-*")); // bản hỏng được giữ lại
    }

    [Fact]
    public void Empty_or_blank_room_falls_back_to_a_generated_strong_room()
    {
        var s = AppSettings.LoadOrCreate(Path1);
        s.Room = "  ";
        s.Save(Path1);
        Assert.True(SyncRoom.IsStrong(AppSettings.LoadOrCreate(Path1).Room));
    }
}
