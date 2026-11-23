using System.Security.Cryptography;
using System.Text;

namespace QuanLyHangHoan.App.Services;

public interface ISingleInstanceGuard : IDisposable
{
    bool TryAcquire(Action onSecondInstanceActivated);
}

/// <summary>
/// Bảo đảm chỉ một phiên bản app chạy cho mỗi người dùng Windows (Mutex đặt tên theo người dùng).
/// Nếu mở lần hai, đánh thức bản đang chạy để đưa cửa sổ lên trước.
/// </summary>
public sealed class WindowsSingleInstanceGuard : ISingleInstanceGuard
{
    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _waitHandle;
    private bool _ownsMutex;
    private bool _disposed;

    public bool TryAcquire(Action onSecondInstanceActivated)
    {
        var userKey = BuildUserScopedSuffix();
        var mutexName = $@"Local\QuanLyHangHoan_{userKey}";
        var eventName = $@"Local\QuanLyHangHoan_Activate_{userKey}";

        var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            try
            {
                using var existingEvent = EventWaitHandle.OpenExisting(eventName);
                existingEvent.Set();
            }
            catch
            {
                // Bản cũ có thể đang tắt hoặc chưa kịp đăng ký event
            }
            return false;
        }

        _mutex = mutex;
        _ownsMutex = true;
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
        _waitHandle = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            (_, timedOut) =>
            {
                if (!timedOut && !_disposed)
                {
                    onSecondInstanceActivated();
                }
            },
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);

        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _waitHandle?.Unregister(null);
        _waitHandle = null;

        _activateEvent?.Dispose();
        _activateEvent = null;

        if (_mutex is not null)
        {
            if (_ownsMutex)
            {
                try { _mutex.ReleaseMutex(); } catch { /* bỏ qua nếu khác luồng */ }
            }
            _mutex.Dispose();
            _mutex = null;
        }
    }

    private static string BuildUserScopedSuffix()
    {
        var identity = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash[..8]);
    }
}

/// <summary>
/// Bản giả cho --demo và UiTests: không tạo Mutex/Event hệ điều hành thật.
/// </summary>
public sealed class FakeSingleInstanceGuard : ISingleInstanceGuard
{
    private readonly bool _allowAcquire;
    private Action? _onSecondInstanceActivated;

    public FakeSingleInstanceGuard(bool allowAcquire = true)
    {
        _allowAcquire = allowAcquire;
    }

    public bool Acquired { get; private set; }
    public bool Disposed { get; private set; }

    public bool TryAcquire(Action onSecondInstanceActivated)
    {
        if (!_allowAcquire)
        {
            return false;
        }

        _onSecondInstanceActivated = onSecondInstanceActivated;
        Acquired = true;
        return true;
    }

    public void SimulateSecondInstance()
    {
        _onSecondInstanceActivated?.Invoke();
    }

    public void Dispose()
    {
        Disposed = true;
    }
}
