using System.Net.Http;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Cloud;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Liên kết THẬT với cloud cho máy tính: ghép thiết bị (mã 6 số), giữ WebSocket + đồng bộ bản sao SQLite, theo dõi hạn mức.
/// Quy tắc: chỉ ghi được khi <see cref="CanWrite"/> (đã kết nối); mất mạng = khóa, không có hàng đợi ngoại tuyến.
/// </summary>
public sealed class CloudLinkService : ICloudLink, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _dbPath;
    private readonly CloudSessionStore _store;
    private readonly object _gate = new();
    private CloudSession? _session;
    private CloudSyncEngine? _engine;
    private CloudApi? _api;
    private CloudState _state = CloudState.Unpaired;
    private string? _error;

    public CloudLinkService(HttpClient http, string dbPath, CloudSessionStore store)
    {
        _http = http;
        _dbPath = dbPath;
        _store = store;
    }

    public CloudApi? Api { get { lock (_gate) return _api; } }
    public CloudSyncEngine? Engine { get { lock (_gate) return _engine; } }

    public CloudState State { get { lock (_gate) return _engine is null ? _state : Map(_engine.State); } }
    public bool CanWrite => State is CloudState.Connected or CloudState.Syncing;
    public string? LastError { get { lock (_gate) return _engine?.LastError ?? _error; } }
    public DateTime? LastSyncAt => Engine?.LastSyncAt;
    public CloudQuota? Quota
    {
        get
        {
            var q = Engine?.Quota;
            return q is null ? null : new CloudQuota(q.Day, q.RequestsUsed, q.RequestsLimit, q.RowsWrittenUsed, q.RowsWrittenLimit, q.RowsReadUsed, q.RowsReadLimit, q.Level);
        }
    }
    public string? DeviceName { get { lock (_gate) return _session?.DeviceName; } }
    public string? Role { get { lock (_gate) return _session?.Role; } }
    public string? BaseUrl { get { lock (_gate) return _session?.BaseUrl; } }

    public event Action? Changed;
    /// <summary>Có dữ liệu mới từ cloud đã áp vào SQLite: nạp lại các bảng (có thể gọi từ luồng nền).</summary>
    public event Action? DataChanged;

    private static CloudState Map(CloudConnState s) => s switch
    {
        CloudConnState.Unpaired => CloudState.Unpaired,
        CloudConnState.Connecting => CloudState.Connecting,
        CloudConnState.Syncing => CloudState.Syncing,
        CloudConnState.Connected => CloudState.Connected,
        CloudConnState.Revoked => CloudState.Revoked,
        _ => CloudState.Disconnected,
    };

    /// <summary>Gọi lúc khởi động: nếu đã ghép từ trước thì nối lại ngay.</summary>
    public void Start()
    {
        var s = _store.Load();
        if (s is null) return;
        Attach(s);
    }

    private void Attach(CloudSession s)
    {
        CloudSyncEngine? old;
        lock (_gate)
        {
            old = _engine;
            _session = s;
            _api = new CloudApi(_http, s.BaseUrl, s.Token);
            _engine = new CloudSyncEngine(_api, _dbPath);
            _engine.Changed += () => Changed?.Invoke();
            _engine.DataChanged += () => DataChanged?.Invoke();
        }
        old?.Dispose();
        Engine!.Start();
        Changed?.Invoke();
    }

    public async Task<string?> PairAsync(string baseUrl, string code, string deviceName, CancellationToken ct = default)
    {
        try
        {
            code = (code ?? "").Trim();
            if (code.Length != 6 || !code.All(char.IsDigit)) return "Mã ghép gồm 6 chữ số.";
            if (string.IsNullOrWhiteSpace(deviceName)) return "Hãy đặt tên cho thiết bị này.";
            var api = new CloudApi(_http, baseUrl, null);
            var p = await api.PairCompleteAsync(code, deviceName.Trim(), "pc", ct).ConfigureAwait(false);
            var s = new CloudSession(api.BaseUrl.Replace("/v1", ""), p.Token, p.DeviceId, deviceName.Trim(), p.Role);
            if (_session is { } prev && !string.Equals(prev.BaseUrl, s.BaseUrl, StringComparison.OrdinalIgnoreCase)) CloudReplica.ResetRev(_dbPath);
            _store.Save(s);
            Attach(s);
            return null;
        }
        catch (CloudApiException ex) { lock (_gate) _error = ex.Message; return ex.Message; }
    }

    public async Task<(string? Code, DateTime? ExpiresAt, string? Error)> StartPairingAsync(CancellationToken ct = default)
    {
        var api = Api;
        if (api is null) return (null, null, "Chưa ghép thiết bị với cloud.");
        if (Role != "owner") return (null, null, "Chỉ chủ cửa hàng mới tạo được mã ghép.");
        try { var r = await api.PairStartAsync(ct).ConfigureAwait(false); return (r.Code, r.ExpiresAt, null); }
        catch (CloudApiException ex) { return (null, null, ex.Message); }
    }

    public async Task<(IReadOnlyList<CloudDeviceInfo> Devices, string? Error)> ListDevicesAsync(CancellationToken ct = default)
    {
        if (Api is not { } api) return (Array.Empty<CloudDeviceInfo>(), "Chưa ghép thiết bị với cloud.");
        if (Role != "owner") return (Array.Empty<CloudDeviceInfo>(), "Chỉ chủ cửa hàng mới xem được danh sách thiết bị.");
        try { return (await api.DevicesAsync(ct).ConfigureAwait(false), null); }
        catch (CloudApiException ex) { return (Array.Empty<CloudDeviceInfo>(), ex.Message); }
    }

    public async Task<string?> RevokeDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        if (Api is not { } api) return "Chưa ghép thiết bị với cloud.";
        if (Role != "owner") return "Chỉ chủ cửa hàng mới thu hồi được thiết bị.";
        try { await api.RevokeDeviceAsync(deviceId, ct).ConfigureAwait(false); return null; }
        catch (CloudApiException ex) { return ex.Message; }
    }

    public async Task SyncNowAsync(CancellationToken ct = default)
    {
        var e = Engine;
        if (e is null) return;
        try { await e.PullAsync(ct).ConfigureAwait(false); await e.RefreshQuotaAsync(ct).ConfigureAwait(false); }
        catch (CloudApiException ex) { lock (_gate) _error = ex.Message; Changed?.Invoke(); }
    }

    public void Unpair()
    {
        CloudSyncEngine? old;
        lock (_gate)
        {
            old = _engine;
            _engine = null; _api = null; _session = null; _state = CloudState.Unpaired; _error = null;
        }
        old?.Dispose();
        _store.Clear();
        CloudReplica.ResetRev(_dbPath);   // lần ghép sau lấy lại toàn bộ; dữ liệu đã có trên máy được giữ nguyên
        Changed?.Invoke();
    }

    public void Dispose() { Engine?.Dispose(); }
}
