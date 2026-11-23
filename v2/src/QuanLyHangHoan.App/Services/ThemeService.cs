using System.Windows;

namespace QuanLyHangHoan.App.Services;

public static class ThemeService
{
    public const string LightTheme = "light";
    public const string DarkTheme = "dark";

    private const string LightThemePackUri = "pack://application:,,,/QuanLyHangHoan.App;component/Themes/LightTheme.xaml";
    private const string DarkThemePackUri = "pack://application:,,,/QuanLyHangHoan.App;component/Themes/DarkTheme.xaml";

    public static string NormalizeTheme(string? theme)
        => string.Equals(theme, DarkTheme, StringComparison.OrdinalIgnoreCase) ? DarkTheme : LightTheme;

    public static string CurrentTheme { get; private set; } = LightTheme;

    public static void ApplyTheme(string? theme)
    {
        var normalized = NormalizeTheme(theme);
        CurrentTheme = normalized;

        var app = Application.Current;
        if (app is null) return;

        if (!app.Dispatcher.CheckAccess())
        {
            if (!app.Dispatcher.HasShutdownStarted)
            {
                app.Dispatcher.Invoke(() => ApplyThemeCore(app, normalized));
            }
            return;
        }

        ApplyThemeCore(app, normalized);
    }

    private static void ApplyThemeCore(Application app, string normalized)
    {
        var targetUri = new Uri(normalized == DarkTheme ? DarkThemePackUri : LightThemePackUri, UriKind.Absolute);
        var merged = app.Resources.MergedDictionaries;

        var existingIndex = -1;
        for (var i = 0; i < merged.Count; i++)
        {
            var src = merged[i].Source?.ToString() ?? string.Empty;
            if (src.EndsWith("LightTheme.xaml", StringComparison.OrdinalIgnoreCase)
                || src.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase))
            {
                existingIndex = i;
                break;
            }
        }

        var dict = new ResourceDictionary { Source = targetUri };
        if (existingIndex >= 0)
        {
            merged[existingIndex] = dict;
        }
        else
        {
            merged.Insert(0, dict);
        }
    }
}
