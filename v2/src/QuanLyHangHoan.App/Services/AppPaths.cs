using System.IO;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Đường dẫn lưu trữ dữ liệu cục bộ cho bản v2 (một nơi duy nhất).
/// </summary>
public static class AppPaths
{
    public static string BaseFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuanLyHangHoan", "v2");

    public static string DatabasePath => Path.Combine(BaseFolder, "data.db");

    public static string SettingsPath => Path.Combine(BaseFolder, "settings.json");

    public static string AlertNotifyStatePath => Path.Combine(BaseFolder, "alert_notify_state.json");

    public static string NotificationsPath => Path.Combine(BaseFolder, "notifications.json");

    public static string LegacyCopiesFolder => Path.Combine(BaseFolder, "legacy-copies");

    public static string FolderWatchStatePath => Path.Combine(BaseFolder, "folder_watch.json");

    public static string SheetSyncStatePath => Path.Combine(BaseFolder, "sheet_sync.json");

    public static string SaleworkAutoStatePath => Path.Combine(BaseFolder, "salework_auto.json");

    public static string SaleworkWebViewFolder => Path.Combine(BaseFolder, "salework-webview");
}
