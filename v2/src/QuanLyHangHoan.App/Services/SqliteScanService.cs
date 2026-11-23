using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Thực thi quét nhận kiện trên SQLite thông qua <see cref="ScanReceiver"/>.
/// Bổ sung thông tin đầy đủ của đơn từ <see cref="OrderRepository"/> để hiển thị trên màn hình quét.
/// </summary>
public sealed class SqliteScanService : IScanService
{
    private readonly string _dbPath;
    private readonly Func<DateTime> _clock;

    public SqliteScanService(string dbPath, Func<DateTime>? clock = null)
    {
        _dbPath = dbPath;
        _clock = clock ?? (() => DateTime.Now);
    }

    public DateTime GetCurrentTime() => _clock();

    public Task<ScanResult> ReceiveAsync(
        string code,
        DateTime now,
        byte[]? photo = null,
        string photoMime = "image/jpeg",
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var result = ScanReceiver.Receive(_dbPath, code, now, photo, photoMime);
            if (result.Order is null) return result;

            var fullOrder = OrderRepository.LoadActive(_dbPath)
                .FirstOrDefault(o => string.Equals(o.Id, result.Order.Id, StringComparison.Ordinal));
            if (fullOrder is null) return result;

            fullOrder.Status = result.Order.Status;
            if (!string.IsNullOrEmpty(result.Order.PhotoId))
            {
                fullOrder.PhotoId = result.Order.PhotoId;
            }
            return result with { Order = fullOrder };
        }, cancellationToken);
    }
}
