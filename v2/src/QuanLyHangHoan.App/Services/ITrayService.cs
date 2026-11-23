using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace QuanLyHangHoan.App.Services;

public interface ITrayService : IDisposable
{
    bool IsVisible { get; }
    bool IsRedAlertBadge { get; }
    bool NotifyAlertsEnabled { get; }

    event Action? OpenRequested;
    event Action? ToggleNotifyAlertsRequested;
    event Action? ExitRequested;
    event Action? NotificationClicked;

    void Initialize();
    void SetRedAlertState(bool hasRedAlert);
    void SetNotifyAlertsEnabled(bool enabled);
    void ShowBalloon(string title, string message, bool isWarningOrError = false);
}

/// <summary>
/// Khay hệ thống thật dùng System.Windows.Forms.NotifyIcon.
/// </summary>
public sealed class WindowsTrayService : ITrayService
{
    private Forms.NotifyIcon? _notifyIcon;
    private Forms.ToolStripMenuItem? _toggleNotifyItem;
    private Drawing.Icon? _normalIcon;
    private Drawing.Icon? _redBadgeIcon;
    private bool _disposed;

    public bool IsVisible => _notifyIcon?.Visible ?? false;
    public bool IsRedAlertBadge { get; private set; }
    public bool NotifyAlertsEnabled { get; private set; } = true;

    public event Action? OpenRequested;
    public event Action? ToggleNotifyAlertsRequested;
    public event Action? ExitRequested;
    public event Action? NotificationClicked;

    public void Initialize()
    {
        if (_disposed || _notifyIcon is not null) return;

        _normalIcon = CreateTrayIcon(hasRedDot: false);
        _redBadgeIcon = CreateTrayIcon(hasRedDot: true);

        var menu = new Forms.ContextMenuStrip();
        var openItem = new Forms.ToolStripMenuItem("Mở app");
        openItem.Click += (_, _) => OpenRequested?.Invoke();

        _toggleNotifyItem = new Forms.ToolStripMenuItem(GetToggleNotifyText(NotifyAlertsEnabled));
        _toggleNotifyItem.Click += (_, _) => ToggleNotifyAlertsRequested?.Invoke();

        var exitItem = new Forms.ToolStripMenuItem("Thoát hẳn");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(openItem);
        menu.Items.Add(_toggleNotifyItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "Quản Lý Hàng Hoàn",
            Icon = IsRedAlertBadge ? _redBadgeIcon : _normalIcon,
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _notifyIcon.BalloonTipClicked += (_, _) => NotificationClicked?.Invoke();
    }

    public void SetRedAlertState(bool hasRedAlert)
    {
        IsRedAlertBadge = hasRedAlert;
        if (_notifyIcon is not null)
        {
            _notifyIcon.Icon = hasRedAlert ? _redBadgeIcon : _normalIcon;
            _notifyIcon.Text = hasRedAlert
                ? "Quản Lý Hàng Hoàn (Có đơn đỏ >24h)"
                : "Quản Lý Hàng Hoàn";
        }
    }

    public void SetNotifyAlertsEnabled(bool enabled)
    {
        NotifyAlertsEnabled = enabled;
        if (_toggleNotifyItem is not null)
        {
            _toggleNotifyItem.Text = GetToggleNotifyText(enabled);
        }
    }

    public void ShowBalloon(string title, string message, bool isWarningOrError = false)
    {
        if (_disposed || _notifyIcon is null) return;
        var icon = isWarningOrError ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(5000, title, message, icon);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _normalIcon?.Dispose();
        _normalIcon = null;
        _redBadgeIcon?.Dispose();
        _redBadgeIcon = null;
    }

    private static string GetToggleNotifyText(bool enabled)
        => enabled ? "Tắt thông báo" : "Bật thông báo";

    private static Drawing.Icon CreateTrayIcon(bool hasRedDot)
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);

            using var bgBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(238, 77, 45));
            g.FillRectangle(bgBrush, 3, 5, 24, 24);

            using var pen = new Drawing.Pen(Drawing.Color.White, 2f);
            g.DrawRectangle(pen, 4, 6, 22, 22);
            g.DrawLine(pen, 4, 13, 26, 13);
            g.DrawLine(pen, 15, 6, 15, 13);

            if (hasRedDot)
            {
                using var borderBrush = new Drawing.SolidBrush(Drawing.Color.White);
                g.FillEllipse(borderBrush, 18, 1, 13, 13);
                using var redBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(220, 38, 38));
                g.FillEllipse(redBrush, 20, 3, 9, 9);
            }
        }

        var hIcon = bmp.GetHicon();
        try
        {
            using var temp = Drawing.Icon.FromHandle(hIcon);
            return (Drawing.Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

public sealed record TrayBalloonRecord(string Title, string Message, bool IsWarningOrError);

/// <summary>
/// Bản giả cho --demo và UiTests: không tạo biểu tượng khay Windows thật.
/// </summary>
public sealed class FakeTrayService : ITrayService
{
    private readonly List<TrayBalloonRecord> _balloons = new();

    public bool IsVisible { get; private set; }
    public bool IsRedAlertBadge { get; private set; }
    public bool NotifyAlertsEnabled { get; private set; } = true;
    public bool Disposed { get; private set; }
    public IReadOnlyList<TrayBalloonRecord> Balloons => _balloons;

    public IReadOnlyList<string> MenuItems =>
    [
        "Mở app",
        NotifyAlertsEnabled ? "Tắt thông báo" : "Bật thông báo",
        "Thoát hẳn"
    ];

    public event Action? OpenRequested;
    public event Action? ToggleNotifyAlertsRequested;
    public event Action? ExitRequested;
    public event Action? NotificationClicked;

    public void Initialize() => IsVisible = true;
    public void SetRedAlertState(bool hasRedAlert) => IsRedAlertBadge = hasRedAlert;
    public void SetNotifyAlertsEnabled(bool enabled) => NotifyAlertsEnabled = enabled;

    public void ShowBalloon(string title, string message, bool isWarningOrError = false)
    {
        _balloons.Add(new TrayBalloonRecord(title, message, isWarningOrError));
    }

    public void SimulateOpen() => OpenRequested?.Invoke();
    public void SimulateDoubleClick() => OpenRequested?.Invoke();
    public void SimulateToggleNotifyAlerts() => ToggleNotifyAlertsRequested?.Invoke();
    public void SimulateExit() => ExitRequested?.Invoke();
    public void SimulateNotificationClick() => NotificationClicked?.Invoke();

    public void Dispose()
    {
        Disposed = true;
        IsVisible = false;
    }
}
