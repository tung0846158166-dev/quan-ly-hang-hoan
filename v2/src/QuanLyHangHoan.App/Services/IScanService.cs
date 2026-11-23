using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Dịch vụ quét nhận kiện hàng (bọc <see cref="ScanReceiver"/> cho chế độ thật và giả lập).
/// </summary>
public interface IScanService
{
    Task<ScanResult> ReceiveAsync(
        string code,
        DateTime now,
        byte[]? photo = null,
        string photoMime = "image/jpeg",
        CancellationToken cancellationToken = default);

    DateTime GetCurrentTime();
}
