using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

public interface IUiSettingsStore
{
    AppSettings Load();
    void Update(Action<AppSettings> mutate);
}

/// <summary>
/// Khóa dùng chung trong tiến trình để thao tác đọc-sửa-lưu settings.json không ghi đè lẫn nhau
/// giữa UI (Theme, CollapsedNavGroups, LastPage, Window) và RealSyncController (Room, SyncEnabled, NtfyEnabled, Brokers).
/// </summary>
public static class SettingsFileSync
{
    public static readonly object Gate = new();
}

/// <summary>
/// Lưu trữ cài đặt giao diện. Ở chế độ --demo, chỉ giữ trong bộ nhớ và tuyệt đối không đọc/ghi tệp settings.json thật.
/// </summary>
public sealed class UiSettingsStore : IUiSettingsStore
{
    private readonly string? _settingsPath;
    private readonly bool _isDemoMode;
    private readonly AppSettings _inMemorySettings = new();

    public UiSettingsStore(string? settingsPath, bool isDemoMode)
    {
        _settingsPath = settingsPath;
        _isDemoMode = isDemoMode;
    }

    public AppSettings Load()
    {
        if (_isDemoMode || string.IsNullOrWhiteSpace(_settingsPath))
        {
            return Clone(_inMemorySettings);
        }

        lock (SettingsFileSync.Gate)
        {
            return AppSettings.LoadOrCreate(_settingsPath);
        }
    }

    public void Update(Action<AppSettings> mutate)
    {
        if (mutate is null) return;

        if (_isDemoMode || string.IsNullOrWhiteSpace(_settingsPath))
        {
            mutate(_inMemorySettings);
            _inMemorySettings.Theme = ThemeService.NormalizeTheme(_inMemorySettings.Theme);
            _inMemorySettings.CollapsedNavGroups ??= new();
            _inMemorySettings.LastPage ??= "";
            return;
        }

        lock (SettingsFileSync.Gate)
        {
            var latest = AppSettings.LoadOrCreate(_settingsPath);
            mutate(latest);
            latest.Theme = ThemeService.NormalizeTheme(latest.Theme);
            latest.CollapsedNavGroups ??= new();
            latest.LastPage ??= "";
            latest.Save(_settingsPath);
        }
    }

    private static AppSettings Clone(AppSettings src) => new()
    {
        Room = src.Room,
        SyncEnabled = src.SyncEnabled,
        NtfyEnabled = src.NtfyEnabled,
        Brokers = new List<string>(src.Brokers),
        Theme = ThemeService.NormalizeTheme(src.Theme),
        CollapsedNavGroups = new List<string>(src.CollapsedNavGroups ?? new()),
        LastPage = src.LastPage ?? "",
        AutoStart = src.AutoStart,
        MinimizeToTray = src.MinimizeToTray,
        NotifyAlerts = src.NotifyAlerts,
        Sounds = src.Sounds,
        BackupEnabled = src.BackupEnabled,
        BackupEveryHours = src.BackupEveryHours,
        BackupKeepDays = src.BackupKeepDays,
        Window = src.Window is null
            ? null
            : new WindowPlacement
            {
                Left = src.Window.Left,
                Top = src.Window.Top,
                Width = src.Window.Width,
                Height = src.Window.Height,
                Maximized = src.Window.Maximized
            }
    };
}
