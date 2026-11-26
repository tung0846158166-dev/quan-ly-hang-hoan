namespace QuanLyHangHoan.App.Services;

/// <summary>Trạng thái kết nối với cloud (cloud/API.md mục 10).</summary>
public enum CloudState
{
    /// <summary>Chưa ghép thiết bị với cloud.</summary>
    Unpaired,
    Connecting,
    /// <summary>Đang lấy phần thay đổi.</summary>
    Syncing,
    Connected,
    /// <summary>MẤT KẾT NỐI: khóa mọi thao tác ghi, chỉ xem dữ liệu đã tải.</summary>
    Disconnected,
    /// <summary>Token bị chủ thu hồi.</summary>
    Revoked,
}

/// <summary>Mức dùng hạn mức trong ngày (số đếm ước lượng của ứng dụng, cloud/API.md mục 9).</summary>
public sealed record CloudQuota(
    string Day,
    long RequestsUsed, long RequestsLimit,
    long RowsWrittenUsed, long RowsWrittenLimit,
    long RowsReadUsed, long RowsReadLimit,
    string Level)   // "ok" | "warn" | "stop"
{
    public double RequestsPercent => RequestsLimit <= 0 ? 0 : RequestsUsed * 100.0 / RequestsLimit;
    public double RowsWrittenPercent => RowsWrittenLimit <= 0 ? 0 : RowsWrittenUsed * 100.0 / RowsWrittenLimit;
    public double RowsReadPercent => RowsReadLimit <= 0 ? 0 : RowsReadUsed * 100.0 / RowsReadLimit;
}

/// <summary>
/// Liên kết với cloud nhìn từ giao diện: trạng thái, hạn mức, ghép thiết bị. Giao diện CHỈ đọc <see cref="CanWrite"/> để khóa/mở nút ghi
/// (quét, sửa, đổi trạng thái, nạp) — mất mạng là khóa, KHÔNG có hàng đợi ngoại tuyến.
/// </summary>
public interface ICloudLink
{
    CloudState State { get; }
    /// <summary>Chỉ true khi <see cref="CloudState.Connected"/> hoặc <see cref="CloudState.Syncing"/>.</summary>
    bool CanWrite { get; }
    /// <summary>Thông báo lỗi gần nhất cho người dùng (tiếng Việt), null nếu không có.</summary>
    string? LastError { get; }
    DateTime? LastSyncAt { get; }
    CloudQuota? Quota { get; }
    string? DeviceName { get; }
    /// <summary>"owner" | "staff" | null khi chưa ghép.</summary>
    string? Role { get; }
    /// <summary>Địa chỉ Worker đang dùng (không chứa token).</summary>
    string? BaseUrl { get; }

    /// <summary>Phát khi bất kỳ thuộc tính nào ở trên đổi (có thể từ luồng nền: giao diện tự đưa về luồng giao diện).</summary>
    event Action? Changed;

    /// <summary>Ghép bằng mã 6 số do chủ tạo; trả null nếu thành công, ngược lại thông báo lỗi tiếng Việt.</summary>
    Task<string?> PairAsync(string baseUrl, string code, string deviceName, CancellationToken ct = default);
    /// <summary>Chủ tạo mã ghép cho thiết bị khác; trả (mã, hết hạn) hoặc lỗi.</summary>
    Task<(string? Code, DateTime? ExpiresAt, string? Error)> StartPairingAsync(CancellationToken ct = default);
    /// <summary>Chủ cửa hàng: danh sách thiết bị đã ghép (hoặc lỗi tiếng Việt).</summary>
    Task<(IReadOnlyList<QuanLyHangHoan.Data.Cloud.CloudDeviceInfo> Devices, string? Error)> ListDevicesAsync(CancellationToken ct = default);
    /// <summary>Chủ cửa hàng thu hồi một thiết bị; trả null nếu thành công, ngược lại thông báo lỗi.</summary>
    Task<string?> RevokeDeviceAsync(string deviceId, CancellationToken ct = default);
    /// <summary>Lấy ngay phần thay đổi từ cloud.</summary>
    Task SyncNowAsync(CancellationToken ct = default);
    /// <summary>Quên ghép trên máy này (không thu hồi trên cloud).</summary>
    void Unpair();
}
