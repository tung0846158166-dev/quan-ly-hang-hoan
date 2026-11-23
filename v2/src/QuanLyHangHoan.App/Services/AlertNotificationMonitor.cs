using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.App.Services;

public sealed record AlertCheckResult(
    int NewYellowCount,
    int NewRedCount,
    bool HasAnyRed,
    bool NotificationSent,
    string? Message);

/// <summary>
/// Kiểm tra định kỳ các đơn chưa nhận quá 12h (Vàng) / 24h (Đỏ),
/// gửi MỘT thông báo gộp cho mỗi lần kiểm tra và nhớ mỗi đơn mỗi mức chỉ báo 1 lần.
/// </summary>
public sealed class AlertNotificationMonitor : IDisposable
{
    public static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(5);

    private readonly IReturnOrderProvider _orderProvider;
    private readonly IAlertNotifyStateStore _stateStore;
    private readonly INotificationService _notificationService;
    private readonly ITrayService _trayService;
    private readonly Func<bool> _isNotifyEnabled;
    private readonly Func<DateTime> _clock;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;

    public AlertNotificationMonitor(
        IReturnOrderProvider orderProvider,
        IAlertNotifyStateStore stateStore,
        INotificationService notificationService,
        ITrayService trayService,
        Func<bool> isNotifyEnabled,
        Func<DateTime>? clock = null,
        TimeSpan? initialDelay = null,
        TimeSpan? interval = null)
    {
        _orderProvider = orderProvider;
        _stateStore = stateStore;
        _notificationService = notificationService;
        _trayService = trayService;
        _isNotifyEnabled = isNotifyEnabled;
        _clock = clock ?? (() => DateTime.Now);
        _initialDelay = initialDelay ?? DefaultInitialDelay;
        _interval = interval ?? DefaultInterval;
    }

    public static string BuildKey(string orderId, AlertLevel level)
    {
        var levelName = level == AlertLevel.Red ? "red" : "yellow";
        return $"{orderId}|{levelName}";
    }

    public static string FormatMessage(int yellowCount, int redCount)
    {
        if (yellowCount > 0 && redCount > 0)
        {
            return $"{yellowCount} đơn vừa quá 12h, {redCount} đơn vừa quá 24h";
        }

        if (redCount > 0)
        {
            return $"{redCount} đơn vừa quá 24h";
        }

        if (yellowCount > 0)
        {
            return $"{yellowCount} đơn vừa quá 12h";
        }

        return string.Empty;
    }

    public AlertCheckResult EvaluateAndNotify(IReadOnlyList<Order> orders, DateTime now)
    {
        var hasAnyRed = false;
        var newYellowKeys = new List<string>();
        var newRedKeys = new List<string>();
        var notifyEnabled = _isNotifyEnabled();

        foreach (var order in orders)
        {
            var level = AlertThresholds.EvaluateOrder(order, now);
            if (level == AlertLevel.Red)
            {
                hasAnyRed = true;
            }

            if (!notifyEnabled) continue;
            if (level is not (AlertLevel.Yellow or AlertLevel.Red)) continue;
            if (string.IsNullOrWhiteSpace(order.Id)) continue;

            var key = BuildKey(order.Id, level);
            if (_stateStore.HasNotified(key)) continue;

            if (level == AlertLevel.Red)
            {
                newRedKeys.Add(key);
            }
            else
            {
                newYellowKeys.Add(key);
            }
        }

        _trayService.SetRedAlertState(hasAnyRed);

        if (!notifyEnabled || (newYellowKeys.Count == 0 && newRedKeys.Count == 0))
        {
            return new AlertCheckResult(0, 0, hasAnyRed, NotificationSent: false, Message: null);
        }

        var message = FormatMessage(newYellowKeys.Count, newRedKeys.Count);
        _notificationService.ShowAlert("Cảnh báo hàng hoàn", message, hasRed: newRedKeys.Count > 0);
        _stateStore.MarkNotified(newYellowKeys.Concat(newRedKeys));

        return new AlertCheckResult(
            newYellowKeys.Count,
            newRedKeys.Count,
            hasAnyRed,
            NotificationSent: true,
            Message: message);
    }

    public async Task<AlertCheckResult> CheckNowAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock();
        var orders = await _orderProvider.GetReturnOrdersAsync(int.MaxValue, cancellationToken);
        return EvaluateAndNotify(orders, now);
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_cts is not null) return;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = RunLoopAsync(token);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_cts is null) return;
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
    }

    public void Dispose() => Stop();

    private async Task RunLoopAsync(CancellationToken token)
    {
        try
        {
            if (_initialDelay > TimeSpan.Zero)
            {
                await Task.Delay(_initialDelay, token);
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await CheckNowAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Lỗi đọc DB tạm thời không được làm sập vòng lặp nền
                }

                if (_interval <= TimeSpan.Zero) break;
                await Task.Delay(_interval, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Dừng bình thường khi tắt app
        }
    }
}
