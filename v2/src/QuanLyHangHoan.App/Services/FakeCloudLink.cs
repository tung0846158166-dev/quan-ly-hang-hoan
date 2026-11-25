namespace QuanLyHangHoan.App.Services;

/// <summary>Cài đặt GIẢ của <see cref="ICloudLink"/> cho giao diện thử và test (không mạng). Đổi trạng thái bằng <see cref="Set"/>.</summary>
public sealed class FakeCloudLink : ICloudLink
{
    public CloudState State { get; private set; } = CloudState.Unpaired;
    public bool CanWrite => State is CloudState.Connected or CloudState.Syncing;
    public string? LastError { get; private set; }
    public DateTime? LastSyncAt { get; private set; }
    public CloudQuota? Quota { get; private set; }
    public string? DeviceName { get; private set; }
    public string? Role { get; private set; }
    public string? BaseUrl { get; private set; }
    public event Action? Changed;

    public int SyncNowCalls { get; private set; }
    public int UnpairCalls { get; private set; }

    public void Set(CloudState state, string? error = null, CloudQuota? quota = null, DateTime? lastSync = null, string? role = null, string? deviceName = null, string? baseUrl = null)
    {
        State = state;
        LastError = error;
        if (quota is not null) Quota = quota;
        if (lastSync is not null) LastSyncAt = lastSync;
        if (role is not null) Role = role;
        if (deviceName is not null) DeviceName = deviceName;
        if (baseUrl is not null) BaseUrl = baseUrl;
        Changed?.Invoke();
    }

    /// <summary>Mã ghép hợp lệ trong bản giả.</summary>
    public const string ValidCode = "123456";

    public Task<string?> PairAsync(string baseUrl, string code, string deviceName, CancellationToken ct = default)
    {
        if (code != ValidCode) return Task.FromResult<string?>("Mã ghép không đúng hoặc đã hết hạn.");
        Set(CloudState.Connected, null, lastSync: DateTime.Now, role: "staff", deviceName: deviceName, baseUrl: baseUrl);
        return Task.FromResult<string?>(null);
    }

    public Task<(string? Code, DateTime? ExpiresAt, string? Error)> StartPairingAsync(CancellationToken ct = default) =>
        Role == "owner"
            ? Task.FromResult<(string?, DateTime?, string?)>((ValidCode, DateTime.Now.AddMinutes(10), null))
            : Task.FromResult<(string?, DateTime?, string?)>((null, null, "Chỉ chủ cửa hàng mới tạo được mã ghép."));

    public Task SyncNowAsync(CancellationToken ct = default) { SyncNowCalls++; LastSyncAt = DateTime.Now; Changed?.Invoke(); return Task.CompletedTask; }

    public void Unpair() { UnpairCalls++; State = CloudState.Unpaired; Role = null; DeviceName = null; Changed?.Invoke(); }
}
