namespace QuanLyHangHoan.App.Services;

public sealed record NotificationRecord(string Title, string Message, bool HasRed, DateTime Timestamp);

public interface INotificationService
{
    event Action? NotificationClicked;
    void ShowInfo(string title, string message);
    void ShowAlert(string title, string message, bool hasRed);
}

/// <summary>
/// Phát thông báo Windows thông qua biểu tượng khay hệ thống.
/// </summary>
public sealed class WindowsTrayNotificationService : INotificationService
{
    private readonly ITrayService _trayService;

    public WindowsTrayNotificationService(ITrayService trayService)
    {
        _trayService = trayService;
        _trayService.NotificationClicked += () => NotificationClicked?.Invoke();
    }

    public event Action? NotificationClicked;

    public void ShowInfo(string title, string message)
        => _trayService.ShowBalloon(title, message, isWarningOrError: false);

    public void ShowAlert(string title, string message, bool hasRed)
        => _trayService.ShowBalloon(title, message, isWarningOrError: hasRed);
}

/// <summary>
/// Bản giả cho --demo và UiTests: ghi thông báo vào danh sách trong bộ nhớ.
/// </summary>
public sealed class FakeNotificationService : INotificationService
{
    private readonly List<NotificationRecord> _notifications = new();
    private readonly Func<DateTime> _clock;

    public FakeNotificationService(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.Now);
    }

    public IReadOnlyList<NotificationRecord> Notifications => _notifications;

    public event Action? NotificationClicked;

    public void ShowInfo(string title, string message)
        => _notifications.Add(new NotificationRecord(title, message, HasRed: false, _clock()));

    public void ShowAlert(string title, string message, bool hasRed)
        => _notifications.Add(new NotificationRecord(title, message, hasRed, _clock()));

    public void SimulateClick() => NotificationClicked?.Invoke();
}
