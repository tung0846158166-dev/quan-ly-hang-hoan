using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Provider đọc dữ liệu thật từ SQLite thông qua <see cref="OrderRepository"/>.
/// DB chưa tồn tại trả danh sách rỗng, không lỗi và không tạo tệp.
/// </summary>
public sealed class SqliteOrderProvider :
    IReturnOrderProvider,
    INewOrderProvider,
    IArchivedOrderProvider,
    IDeletedOrderProvider
{
    private readonly string _dbPath;
    private readonly Func<DateTime> _clock;

    public SqliteOrderProvider(string dbPath, Func<DateTime>? clock = null)
    {
        _dbPath = dbPath;
        _clock = clock ?? (() => DateTime.Now);
    }

    public DateTime GetCurrentTime() => _clock();

    public IReadOnlyList<Order> GetReturnOrders(int count = 40)
        => LoadAllShortLived().Where(s => !s.IsArchived && !s.IsDeleted).Select(s => s.Order).ToList();

    public Task<IReadOnlyList<Order>> GetReturnOrdersAsync(int count = 40, CancellationToken cancellationToken = default)
        => Task.Run(() => GetReturnOrders(count), cancellationToken);

    public IReadOnlyList<Order> GetArchivedOrders()
        => LoadAllShortLived()
            .Where(s => s.IsArchived && !s.IsDeleted)
            .Select(s => s.Order)
            .ToList();

    public IReadOnlyList<DeletedOrderRecord> GetDeletedOrders()
        => LoadAllShortLived()
            .Where(s => s.IsDeleted)
            .Select(MapDeletedOrder)
            .ToList();

    // Nạp lại dữ liệu gọi Kho lưu trữ và Đã xóa gần như cùng lúc: dùng chung MỘT lần đọc cả CSDL (thay vì hai lần, mỗi lần hàng chục MB).
    // Bộ nhớ đệm chỉ sống vài giây và chỉ dùng khi tệp CSDL (và -wal) chưa đổi, nên không bao giờ trả dữ liệu cũ sau khi có ghi.
    private readonly object _cacheLock = new();
    private string? _cacheSignature;
    private IReadOnlyList<StoredOrder>? _cacheData;
    private System.Threading.Timer? _cacheExpiry;

    private IReadOnlyList<StoredOrder> LoadAllShortLived()
    {
        lock (_cacheLock)
        {
            var sig = FileSignature();
            if (_cacheData is not null && _cacheSignature == sig) return _cacheData;
            var data = OrderRepository.Load(_dbPath);
            _cacheData = data;
            _cacheSignature = sig;
            _cacheExpiry?.Dispose();
            _cacheExpiry = new System.Threading.Timer(_ => { lock (_cacheLock) { _cacheData = null; _cacheSignature = null; } }, null, 4000, System.Threading.Timeout.Infinite);
            return data;
        }
    }

    private string FileSignature()
    {
        string F(string path)
        {
            try { var i = new System.IO.FileInfo(path); return i.Exists ? $"{i.LastWriteTimeUtc.Ticks}:{i.Length}" : "-"; }
            catch { return "?"; }
        }
        return F(_dbPath) + "|" + F(_dbPath + "-wal");
    }

    public IReadOnlyList<NewOrderRecord> GetNewOrders()
        => Array.Empty<NewOrderRecord>();

    public PlaceholderScreenData GetPlaceholderScreenData(string screenKey)
        => new(
            screenKey,
            "Chưa có dữ liệu cho màn hình này.",
            "SQLite v2",
            ["Cột 1", "Cột 2", "Cột 3", "Cột 4", "Cột 5"],
            Array.Empty<PlaceholderPreviewRow>(),
            "Dữ liệu thật sẽ được nối ở giai đoạn tiếp theo.");

    private static DeletedOrderRecord MapDeletedOrder(StoredOrder stored)
    {
        var order = stored.Order;
        return new DeletedOrderRecord
        {
            Id = order.Id,
            OrderId = order.OrderId,
            Track = order.Track,
            ReturnTrack = order.ReturnTrack,
            Platform = order.Platform,
            DeletedFrom = string.IsNullOrWhiteSpace(stored.DeletedFrom) ? "Bảng chính" : stored.DeletedFrom,
            DeletedAt = stored.DeletedAt,
            DeleteReason = !string.IsNullOrWhiteSpace(order.UserNote) ? order.UserNote : order.Note,
            Items = order.Items,
            FullOrder = order
        };
    }
}
