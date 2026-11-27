using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.App.Converters;

public sealed class AlertLevelToRowBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush RedRowBrush = CreateFrozen("#FEE2E2");
    private static readonly SolidColorBrush YellowRowBrush = CreateFrozen("#FEF3C7");
    private static readonly SolidColorBrush DefaultRowBrush = CreateFrozen("#FFFFFF");

    private static SolidColorBrush ResolveThemeBrush(string resourceKey, SolidColorBrush fallback)
    {
        if (Application.Current?.TryFindResource(resourceKey) is SolidColorBrush brush)
        {
            return brush;
        }
        return fallback;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is AlertLevel level
            ? level switch
            {
                AlertLevel.Red => ResolveThemeBrush("AlertRedRowBrush", RedRowBrush),
                AlertLevel.Yellow => ResolveThemeBrush("AlertYellowRowBrush", YellowRowBrush),
                _ => ResolveThemeBrush("DataGridRowBgBrush", DefaultRowBrush)
            }
            : ResolveThemeBrush("DataGridRowBgBrush", DefaultRowBrush);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush CreateFrozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}

public sealed class AlertLevelToBadgeBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush RedBg = CreateFrozen("#DC2626");
    private static readonly SolidColorBrush YellowBg = CreateFrozen("#D97706");
    private static readonly SolidColorBrush SafeBg = CreateFrozen("#E0F2FE");
    private static readonly SolidColorBrush DoneBg = CreateFrozen("#DCFCE7");
    private static readonly SolidColorBrush NoneBg = CreateFrozen("#F1F5F9");

    private static readonly SolidColorBrush WhiteFg = CreateFrozen("#FFFFFF");
    private static readonly SolidColorBrush SafeFg = CreateFrozen("#0369A1");
    private static readonly SolidColorBrush DoneFg = CreateFrozen("#15803D");
    private static readonly SolidColorBrush NoneFg = CreateFrozen("#64748B");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isForeground = string.Equals(parameter as string, "Foreground", StringComparison.OrdinalIgnoreCase);
        if (value is not AlertLevel level) return isForeground ? NoneFg : NoneBg;

        if (isForeground)
        {
            return level switch
            {
                AlertLevel.Red or AlertLevel.Yellow => WhiteFg,
                AlertLevel.Safe => Resolve("SafeBadgeFgBrush", SafeFg),
                AlertLevel.Done => Resolve("AlertGreenTextBrush", DoneFg),
                _ => Resolve("InkMutedBrush", NoneFg)
            };
        }

        return level switch
        {
            AlertLevel.Red => RedBg,
            AlertLevel.Yellow => YellowBg,
            AlertLevel.Safe => Resolve("SafeBadgeBgBrush", SafeBg),
            AlertLevel.Done => Resolve("AlertGreenBadgeBgBrush", DoneBg),
            _ => Resolve("NeutralBadgeBgBrush", NoneBg)
        };
    }

    private static SolidColorBrush Resolve(string key, SolidColorBrush fallback)
        => Application.Current?.TryFindResource(key) as SolidColorBrush ?? fallback;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush CreateFrozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}

public sealed class ReceiveStatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ReceivedBg = CreateFrozen("#DCFCE7");
    private static readonly SolidColorBrush ReceivedFg = CreateFrozen("#15803D");
    private static readonly SolidColorBrush PendingBg = CreateFrozen("#FEF3C7");
    private static readonly SolidColorBrush PendingFg = CreateFrozen("#B45309");
    private static readonly SolidColorBrush LostBg = CreateFrozen("#FEE2E2");
    private static readonly SolidColorBrush LostFg = CreateFrozen("#B91C1C");
    private static readonly SolidColorBrush UnneededBg = CreateFrozen("#E2E8F0");
    private static readonly SolidColorBrush UnneededFg = CreateFrozen("#475569");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isForeground = string.Equals(parameter as string, "Foreground", StringComparison.OrdinalIgnoreCase);
        var status = value as string ?? string.Empty;

        if (status.Equals(ReceiveStatus.Received, StringComparison.OrdinalIgnoreCase)
            || status.Equals("Đã nhận", StringComparison.OrdinalIgnoreCase))
        {
            return isForeground ? Resolve("AlertGreenTextBrush", ReceivedFg) : Resolve("AlertGreenBadgeBgBrush", ReceivedBg);
        }

        if (status.Equals(ReceiveStatus.Lost, StringComparison.OrdinalIgnoreCase)
            || status.Equals("Hư hỏng", StringComparison.OrdinalIgnoreCase))
        {
            return isForeground ? Resolve("AlertRedTextBrush", LostFg) : Resolve("AlertRedBadgeBgBrush", LostBg);
        }

        if (status.Equals(ReceiveStatus.NotNeeded, StringComparison.OrdinalIgnoreCase))
        {
            return isForeground ? Resolve("NeutralBadgeFgBrush", UnneededFg) : Resolve("NeutralBadgeBgBrush", UnneededBg);
        }

        return isForeground ? Resolve("AlertYellowTextBrush", PendingFg) : Resolve("AlertYellowBadgeBgBrush", PendingBg);
    }

    private static SolidColorBrush Resolve(string key, SolidColorBrush fallback)
        => Application.Current?.TryFindResource(key) as SolidColorBrush ?? fallback;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush CreateFrozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}

public sealed class PlatformToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ShopeeBg = CreateFrozen("#FFEDD5");
    private static readonly SolidColorBrush ShopeeFg = CreateFrozen("#C2410C");
    private static readonly SolidColorBrush TikTokBg = CreateFrozen("#0F172A");
    private static readonly SolidColorBrush TikTokFg = CreateFrozen("#FFFFFF");
    private static readonly SolidColorBrush OtherBg = CreateFrozen("#DBEAFE");
    private static readonly SolidColorBrush OffBg = CreateFrozen("#DCFCE7");
    private static readonly SolidColorBrush OffFg = CreateFrozen("#15803D");
    private static readonly SolidColorBrush OtherFg = CreateFrozen("#1D4ED8");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isForeground = string.Equals(parameter as string, "Foreground", StringComparison.OrdinalIgnoreCase);
        var platform = value as string ?? string.Empty;

        if (platform.Equals(PlatformNames.Shopee, StringComparison.OrdinalIgnoreCase))
        {
            return isForeground ? Resolve("DemoBadgeFgBrush", ShopeeFg) : Resolve("OrangeBadgeBgBrush", ShopeeBg);
        }

        if (platform.Equals(PlatformNames.TikTok, StringComparison.OrdinalIgnoreCase))
        {
            return isForeground ? Resolve("TikTokBadgeFgBrush", TikTokFg) : Resolve("TikTokBadgeBgBrush", TikTokBg);
        }

        if (platform.Equals(PlatformNames.OffPlatform, StringComparison.OrdinalIgnoreCase))
        {
            return isForeground ? Resolve("AlertGreenTextBrush", OffFg) : Resolve("AlertGreenCardBgBrush", OffBg);
        }

        return isForeground ? Resolve("InfoBlueTextBrush", OtherFg) : Resolve("InfoBlueBadgeBgBrush", OtherBg);
    }

    private static SolidColorBrush Resolve(string key, SolidColorBrush fallback)
        => Application.Current?.TryFindResource(key) as SolidColorBrush ?? fallback;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush CreateFrozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Cầu nối DataContext cho các phần tử nằm ngoài Visual Tree (ví dụ: DataGridColumn.Header).
/// </summary>
public sealed class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BindingProxy();
}

public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush FallbackBrush = Brushes.Transparent;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex)) return FallbackBrush;
        try
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            brush.Freeze();
            return brush;
        }
        catch
        {
            return FallbackBrush;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
