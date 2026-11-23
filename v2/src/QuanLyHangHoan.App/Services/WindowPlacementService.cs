using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

public readonly record struct ScreenWorkArea(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public static class WindowPlacementService
{
    private const double MinVisibleWidth = 120;
    private const double MinVisibleHeight = 80;
    private const double MinWindowWidth = 400;
    private const double MinWindowHeight = 300;

    public static bool IsPlacementVisibleOnScreens(WindowPlacement? placement, IReadOnlyList<ScreenWorkArea> screens)
    {
        if (placement is null || screens is null || screens.Count == 0) return false;
        if (!IsFinite(placement.Left) || !IsFinite(placement.Top)
            || !IsFinite(placement.Width) || !IsFinite(placement.Height))
        {
            return false;
        }
        if (placement.Width < MinWindowWidth || placement.Height < MinWindowHeight) return false;

        var winRight = placement.Left + placement.Width;
        var winBottom = placement.Top + placement.Height;

        foreach (var screen in screens)
        {
            if (screen.Width <= 0 || screen.Height <= 0) continue;

            var intersectLeft = Math.Max(placement.Left, screen.Left);
            var intersectTop = Math.Max(placement.Top, screen.Top);
            var intersectRight = Math.Min(winRight, screen.Right);
            var intersectBottom = Math.Min(winBottom, screen.Bottom);

            var intersectWidth = intersectRight - intersectLeft;
            var intersectHeight = intersectBottom - intersectTop;

            if (intersectWidth >= MinVisibleWidth
                && intersectHeight >= MinVisibleHeight
                && placement.Top >= screen.Top - 40
                && placement.Top <= screen.Bottom - MinVisibleHeight)
            {
                return true;
            }
        }

        return false;
    }

    public static WindowPlacement? CapturePlacement(
        WindowState state,
        double left,
        double top,
        double width,
        double height,
        Rect restoreBounds)
    {
        if (state == WindowState.Minimized)
        {
            return null;
        }

        if (state == WindowState.Maximized)
        {
            var boundsValid = !restoreBounds.IsEmpty
                && IsFinite(restoreBounds.Left) && IsFinite(restoreBounds.Top)
                && IsFinite(restoreBounds.Width) && IsFinite(restoreBounds.Height)
                && restoreBounds.Width >= MinWindowWidth && restoreBounds.Height >= MinWindowHeight;

            return new WindowPlacement
            {
                Left = boundsValid ? restoreBounds.Left : left,
                Top = boundsValid ? restoreBounds.Top : top,
                Width = boundsValid ? restoreBounds.Width : width,
                Height = boundsValid ? restoreBounds.Height : height,
                Maximized = true
            };
        }

        if (!IsFinite(left) || !IsFinite(top) || !IsFinite(width) || !IsFinite(height))
        {
            return null;
        }

        return new WindowPlacement
        {
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            Maximized = false
        };
    }

    public static void ApplyToWindow(Window window, WindowPlacement? placement)
    {
        var screens = GetCurrentScreenWorkAreas(window);
        if (!IsPlacementVisibleOnScreens(placement, screens) || placement is null)
        {
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = placement.Left;
        window.Top = placement.Top;
        window.Width = Math.Max(window.MinWidth, placement.Width);
        window.Height = Math.Max(window.MinHeight, placement.Height);
        if (placement.Maximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    public static IReadOnlyList<ScreenWorkArea> GetCurrentScreenWorkAreas(Visual? visual = null)
    {
        var scaleX = 1.0;
        var scaleY = 1.0;
        if (visual is not null)
        {
            var dpi = VisualTreeHelper.GetDpi(visual);
            if (dpi.DpiScaleX > 0) scaleX = dpi.DpiScaleX;
            if (dpi.DpiScaleY > 0) scaleY = dpi.DpiScaleY;
        }

        var result = new List<ScreenWorkArea>();
        EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(hMonitor, ref info))
                {
                    var work = info.rcWork;
                    result.Add(new ScreenWorkArea(
                        work.Left / scaleX,
                        work.Top / scaleY,
                        (work.Right - work.Left) / scaleX,
                        (work.Bottom - work.Top) / scaleY));
                }
                return true;
            },
            IntPtr.Zero);

        if (result.Count == 0)
        {
            var area = SystemParameters.WorkArea;
            result.Add(new ScreenWorkArea(area.Left, area.Top, area.Width, area.Height));
        }

        return result;
    }

    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
