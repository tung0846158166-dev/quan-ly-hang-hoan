using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Giao diện tùy chọn cho transport giả trong test (UiTests) để mô phỏng trạng thái kết nối và lỗi
/// mà không cần sửa lớp thật trong Data.
/// </summary>
public interface IManagedSyncTransport : ISyncTransport, IDisposable
{
    bool IsConnected { get; }
    int ConnectedCount { get; }
    string LastError { get; }
    event Action? StatusChanged;
    void Start();
}

public interface ISyncController : IDisposable
{
    string Room { get; }
    bool SyncEnabled { get; }
    bool NtfyEnabled { get; }
    int ConnectedBrokers { get; }
    int TotalBrokers { get; }
    bool NtfyConnected { get; }
    string MqttLastError { get; }
    string NtfyLastError { get; }
    string LastError { get; }
    bool IsDemoMode { get; }

    event Action<SyncHandled>? Handled;
    event Action? StatusChanged;

    void Start();
    void SetSyncEnabled(bool enabled);
    void SetNtfyEnabled(bool enabled);
    void RegenerateRoom();

    /// <summary>Địa chỉ broker riêng (wss://tên:mật-khẩu@máy-chủ/mqtt); rỗng = dùng 3 broker công cộng.</summary>
    string PrivateBroker { get; }
    void SetPrivateBroker(string url);
}

/// <summary>
/// Điều phối đồng bộ thật: nạp AppSettings, khởi chạy SyncService trước rồi mới Start các đường truyền (MQTT + ntfy nếu bật).
/// Lỗi mạng/broker không bao giờ làm sập hoặc chậm khởi động ứng dụng.
/// </summary>
public sealed class RealSyncController : ISyncController
{
    private readonly string _dbPath;
    private readonly string _settingsPath;
    private readonly Func<IEnumerable<string>, ISyncTransport> _mqttTransportFactory;
    private readonly Func<ISyncTransport> _ntfyTransportFactory;
    private readonly Func<DateTime> _clock;
    private readonly AppSettings _settings;
    private readonly object _gate = new();

    private ISyncTransport? _mqttTransport;
    private ISyncTransport? _ntfyTransport;
    private ISyncTransport? _transport;
    private SyncService? _service;
    private string _lastError = string.Empty;
    private bool _disposed;

    public RealSyncController(
        string dbPath,
        string settingsPath,
        Func<IEnumerable<string>, ISyncTransport>? transportFactory = null,
        Func<DateTime>? clock = null,
        Func<ISyncTransport>? ntfyTransportFactory = null)
    {
        _dbPath = dbPath;
        _settingsPath = settingsPath;
        _mqttTransportFactory = transportFactory ?? (brokers => new MqttTransport(brokers));
        _ntfyTransportFactory = ntfyTransportFactory ?? (() => new NtfyTransport());
        _clock = clock ?? (() => DateTime.Now);
        lock (SettingsFileSync.Gate)
        {
            _settings = AppSettings.LoadOrCreate(_settingsPath);
        }
    }

    public string Room => _settings.Room;
    public bool SyncEnabled => _settings.SyncEnabled;
    public bool NtfyEnabled => _settings.NtfyEnabled;
    public int TotalBrokers => Math.Max(1, _settings.Brokers.Count);
    public bool IsDemoMode => false;

    public int ConnectedBrokers
    {
        get
        {
            lock (_gate)
            {
                if (_service is null || !_settings.SyncEnabled) return 0;
                if (_mqttTransport is MqttTransport mqtt) return mqtt.ConnectedBrokers;
                if (_mqttTransport is IManagedSyncTransport managed) return managed.ConnectedCount;
                return _mqttTransport is not null ? TotalBrokers : 0;
            }
        }
    }

    public bool NtfyConnected
    {
        get
        {
            lock (_gate)
            {
                if (_service is null || !_settings.SyncEnabled || !_settings.NtfyEnabled) return false;
                if (_ntfyTransport is NtfyTransport ntfy) return ntfy.Connected;
                if (_ntfyTransport is IManagedSyncTransport managed) return managed.IsConnected;
                return _ntfyTransport is not null;
            }
        }
    }

    public string MqttLastError
    {
        get
        {
            lock (_gate)
            {
                if (_mqttTransport is MqttTransport mqtt && !string.IsNullOrWhiteSpace(mqtt.LastError))
                {
                    return mqtt.LastError;
                }
                if (_mqttTransport is IManagedSyncTransport managed && !string.IsNullOrWhiteSpace(managed.LastError))
                {
                    return managed.LastError;
                }
                return _lastError;
            }
        }
    }

    public string NtfyLastError
    {
        get
        {
            lock (_gate)
            {
                if (!_settings.SyncEnabled || !_settings.NtfyEnabled) return string.Empty;
                if (_ntfyTransport is NtfyTransport ntfy && !string.IsNullOrWhiteSpace(ntfy.LastError))
                {
                    return ntfy.LastError;
                }
                if (_ntfyTransport is IManagedSyncTransport managed && !string.IsNullOrWhiteSpace(managed.LastError))
                {
                    return managed.LastError;
                }
                return string.Empty;
            }
        }
    }

    public string LastError
    {
        get
        {
            var mqttErr = MqttLastError;
            if (!string.IsNullOrWhiteSpace(mqttErr)) return mqttErr;
            return NtfyLastError;
        }
    }

    public event Action<SyncHandled>? Handled;
    public event Action? StatusChanged;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || !_settings.SyncEnabled || _service is not null) return;
            StartCoreLocked();
        }
        StatusChanged?.Invoke();
    }

    public void SetSyncEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_disposed) return;
            SaveSyncSettingsLocked(s => s.SyncEnabled = enabled);

            if (enabled)
            {
                if (_service is null) StartCoreLocked();
            }
            else
            {
                StopCoreLocked();
            }
        }
        StatusChanged?.Invoke();
    }

    public void SetNtfyEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_disposed) return;
            SaveSyncSettingsLocked(s => s.NtfyEnabled = enabled);

            StopCoreLocked();
            if (_settings.SyncEnabled)
            {
                StartCoreLocked();
            }
        }
        StatusChanged?.Invoke();
    }

    public string PrivateBroker
    {
        get
        {
            lock (_gate)
                return _settings.Brokers.Count == 1 && !MqttTransport.DefaultBrokers.Contains(_settings.Brokers[0])
                    ? _settings.Brokers[0] : string.Empty;
        }
    }

    public void SetPrivateBroker(string url)
    {
        url = (url ?? "").Trim();
        if (url.Length > 0 && !(Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "wss" || u.Scheme == "ws")))
            return;
        lock (_gate)
        {
            if (_disposed) return;
            SaveSyncSettingsLocked(s => s.Brokers = url.Length == 0 ? new(MqttTransport.DefaultBrokers) : new List<string> { url });
            StopCoreLocked();
            if (_settings.SyncEnabled) StartCoreLocked();
        }
        StatusChanged?.Invoke();
    }

    public void RegenerateRoom()
    {
        lock (_gate)
        {
            if (_disposed) return;
            var newRoom = SyncRoom.Generate();
            SaveSyncSettingsLocked(s => s.Room = newRoom);

            StopCoreLocked();
            if (_settings.SyncEnabled)
            {
                StartCoreLocked();
            }
        }
        StatusChanged?.Invoke();
    }

    private void SaveSyncSettingsLocked(Action<AppSettings> mutate)
    {
        lock (SettingsFileSync.Gate)
        {
            var latest = AppSettings.LoadOrCreate(_settingsPath);
            mutate(latest);
            latest.Save(_settingsPath);
            _settings.Room = latest.Room;
            _settings.SyncEnabled = latest.SyncEnabled;
            _settings.NtfyEnabled = latest.NtfyEnabled;
            _settings.Brokers = latest.Brokers;
        }
    }

    private void StartCoreLocked()
    {
        try
        {
            _lastError = string.Empty;
            var mqtt = _mqttTransportFactory(_settings.Brokers);
            ISyncTransport? ntfy = _settings.NtfyEnabled ? _ntfyTransportFactory() : null;
            ISyncTransport combined = ntfy is not null
                ? new CompositeTransport(mqtt, ntfy)
                : mqtt;

            var service = new SyncService(_dbPath, _settings.Room, combined, _clock);
            service.Handled += OnServiceHandled;

            SubscribeStatusChanged(mqtt);
            if (ntfy is not null) SubscribeStatusChanged(ntfy);

            _mqttTransport = mqtt;
            _ntfyTransport = ntfy;
            _transport = combined;
            _service = service;

            // Khởi động SyncService trước rồi mới Start từng đường truyền theo đúng thiết kế
            service.Start();
            StartTransport(mqtt);
            if (ntfy is not null) StartTransport(ntfy);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            StopCoreLocked();
        }
    }

    private void StopCoreLocked()
    {
        if (_service is not null)
        {
            _service.Handled -= OnServiceHandled;
            try { _service.Dispose(); } catch { /* bỏ qua khi dừng */ }
            _service = null;
        }

        if (_mqttTransport is not null)
        {
            UnsubscribeStatusChanged(_mqttTransport);
        }

        if (_ntfyTransport is not null)
        {
            UnsubscribeStatusChanged(_ntfyTransport);
        }

        if (_transport is IDisposable disposable)
        {
            try { disposable.Dispose(); } catch { /* bỏ qua khi dừng */ }
        }
        else
        {
            if (_mqttTransport is IDisposable mqttDisp)
            {
                try { mqttDisp.Dispose(); } catch { /* bỏ qua khi dừng */ }
            }
            if (_ntfyTransport is IDisposable ntfyDisp)
            {
                try { ntfyDisp.Dispose(); } catch { /* bỏ qua khi dừng */ }
            }
        }

        _transport = null;
        _mqttTransport = null;
        _ntfyTransport = null;
    }

    private void SubscribeStatusChanged(ISyncTransport transport)
    {
        if (transport is MqttTransport mqtt) mqtt.StatusChanged += OnTransportStatusChanged;
        else if (transport is NtfyTransport ntfy) ntfy.StatusChanged += OnTransportStatusChanged;
        else if (transport is IManagedSyncTransport managed) managed.StatusChanged += OnTransportStatusChanged;
    }

    private void UnsubscribeStatusChanged(ISyncTransport transport)
    {
        if (transport is MqttTransport mqtt) mqtt.StatusChanged -= OnTransportStatusChanged;
        else if (transport is NtfyTransport ntfy) ntfy.StatusChanged -= OnTransportStatusChanged;
        else if (transport is IManagedSyncTransport managed) managed.StatusChanged -= OnTransportStatusChanged;
    }

    private static void StartTransport(ISyncTransport transport)
    {
        if (transport is MqttTransport mqtt) mqtt.Start();
        else if (transport is NtfyTransport ntfy) ntfy.Start();
        else if (transport is IManagedSyncTransport managed) managed.Start();
    }

    private void OnServiceHandled(SyncHandled item) => Handled?.Invoke(item);

    private void OnTransportStatusChanged() => StatusChanged?.Invoke();

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            StopCoreLocked();
        }
    }
}

/// <summary>
/// Bản giả dùng cho chế độ --demo: KHÔNG tạo MqttTransport/NtfyTransport, KHÔNG kết nối mạng thật.
/// Cho phép đặt trạng thái giả đủ các tổ hợp để kiểm thử và chụp ảnh.
/// </summary>
public sealed class FakeSyncController : ISyncController
{
    public FakeSyncController(string? initialRoom = null)
    {
        Room = string.IsNullOrWhiteSpace(initialRoom) ? SyncRoom.Generate() : initialRoom;
    }

    public string Room { get; private set; }
    public bool SyncEnabled { get; private set; } = true;
    public bool NtfyEnabled { get; private set; } = true;
    public int ConnectedBrokers { get; private set; }
    public int TotalBrokers => 3;
    public bool NtfyConnected { get; private set; }
    public string MqttLastError { get; private set; } = "Chế độ giả lập (--demo): không mở kết nối broker thật.";
    public string NtfyLastError { get; private set; } = "Chế độ giả lập (--demo): không mở kết nối ntfy thật.";
    public string LastError => SyncEnabled
        ? (string.IsNullOrWhiteSpace(MqttLastError) ? NtfyLastError : MqttLastError)
        : "Đã tắt đồng bộ.";
    public bool IsDemoMode => true;

    public event Action<SyncHandled>? Handled;
    public event Action? StatusChanged;

    public void Start() => StatusChanged?.Invoke();

    public void SetSyncEnabled(bool enabled)
    {
        SyncEnabled = enabled;
        if (!enabled)
        {
            ConnectedBrokers = 0;
            NtfyConnected = false;
        }
        StatusChanged?.Invoke();
    }

    public string PrivateBroker { get; private set; } = string.Empty;

    public void SetPrivateBroker(string url)
    {
        PrivateBroker = (url ?? "").Trim();
        StatusChanged?.Invoke();
    }

    public void SetNtfyEnabled(bool enabled)
    {
        NtfyEnabled = enabled;
        if (!enabled)
        {
            NtfyConnected = false;
            NtfyLastError = string.Empty;
        }
        StatusChanged?.Invoke();
    }

    public void RegenerateRoom()
    {
        Room = SyncRoom.Generate();
        StatusChanged?.Invoke();
    }

    public void SetDemoStatus(
        int connectedBrokers,
        bool ntfyConnected,
        string mqttLastError = "",
        string ntfyLastError = "",
        bool? ntfyEnabled = null,
        bool? syncEnabled = null)
    {
        if (syncEnabled.HasValue) SyncEnabled = syncEnabled.Value;
        if (ntfyEnabled.HasValue) NtfyEnabled = ntfyEnabled.Value;
        ConnectedBrokers = SyncEnabled ? Math.Clamp(connectedBrokers, 0, TotalBrokers) : 0;
        NtfyConnected = SyncEnabled && NtfyEnabled && ntfyConnected;
        MqttLastError = mqttLastError;
        NtfyLastError = NtfyEnabled ? ntfyLastError : string.Empty;
        StatusChanged?.Invoke();
    }

    public void EmitDemoHandled(SyncHandled item) => Handled?.Invoke(item);

    /// <summary>Phát chuỗi sự kiện mẫu để xem giao diện ở chế độ --demo (không chạm DB/mạng).</summary>
    public void EmitDemoSampleEvents(DateTime? now = null)
    {
        var t = now ?? DateTime.Now;
        var prefix = Guid.NewGuid().ToString("N")[..6];
        EmitDemoHandled(new SyncHandled($"demo-{prefix}-1", "SPX-DEMO-NEW-01", "success", "ĐÃ TẠO ĐƠN MỚI Ở BẢNG CHÍNH (Shopee)", t, "scan", "new_order"));
        EmitDemoHandled(new SyncHandled($"demo-{prefix}-2", "SPX-DEMO-001", "success", "Đã nhận kiện", t.AddSeconds(1), "scan"));
        EmitDemoHandled(new SyncHandled($"demo-{prefix}-3", "SPX-DEMO-DUP-02", "duplicate", "ĐƠN ĐÃ CÓ Ở BẢNG CHÍNH, KHÔNG TẠO LẠI", t.AddSeconds(2), "scan"));
        EmitDemoHandled(new SyncHandled($"demo-{prefix}-4", "TT-DEMO-003", "success", "PC đã lưu ghi chú và đưa đơn TT-DEMO-003 vào bảng theo dõi", t.AddSeconds(3), "note"));
        EmitDemoHandled(new SyncHandled($"demo-{prefix}-5", "SPX-DEMO-004", "success", "PC đã cập nhật đơn SPX-DEMO-004", t.AddSeconds(4), "edit"));
        EmitDemoHandled(new SyncHandled($"demo-{prefix}-6", "SPX-DEMO-005", "success", "PC đã xóa đơn SPX-DEMO-005 (khôi phục được ở màn Đã xóa)", t.AddSeconds(5), "delete"));
        EmitDemoHandled(new SyncHandled($"demo-{prefix}-7", "SPX-DEMO-001", "success", "Đã lưu ảnh chụp", t.AddSeconds(6), "photo"));
        EmitDemoHandled(new SyncHandled($"demo-{prefix}-8", "SPX-DEMO-ERR", "error", "v2 không có chức năng tạo đơn hủy từ điện thoại", t.AddSeconds(7), "scan"));
    }

    public void Dispose() { }
}
