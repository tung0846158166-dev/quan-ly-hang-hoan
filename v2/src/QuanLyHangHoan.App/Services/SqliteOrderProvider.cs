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
        => OrderRepository.LoadActive(_dbPath);

    public Task<IReadOnlyList<Order>> GetReturnOrdersAsync(int count = 40, CancellationToken cancellationToken = default)
        => Task.Run(() => GetReturnOrders(count), cancellationToken);

    public IReadOnlyList<Order> GetArchivedOrders()
        => OrderRepository.Load(_dbPath)
            .Where(s => s.IsArchived && !s.IsDeleted)
            .Select(s => s.Order)
            .ToList();

    public IReadOnlyList<DeletedOrderRecord> GetDeletedOrders()
        => OrderRepository.Load(_dbPath)
            .Where(s => s.IsDeleted)
            .Select(MapDeletedOrder)
            .ToList();

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
