using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

public interface IReturnWatchService
{
    Task<IReadOnlyList<ReturnWatchEntry>> LoadAsync(DateTime now, CancellationToken cancellationToken = default);
    Task<EditResult> RemoveAsync(string orderId, DateTime now, CancellationToken cancellationToken = default);
    Task<EditResult> RestoreAsync(string orderId, DateTime now, CancellationToken cancellationToken = default);
}

public sealed class SqliteReturnWatchService : IReturnWatchService
{
    private readonly string _dbPath;

    public SqliteReturnWatchService(string dbPath)
    {
        _dbPath = dbPath;
    }

    public Task<IReadOnlyList<ReturnWatchEntry>> LoadAsync(DateTime now, CancellationToken cancellationToken = default)
        => Task.Run(() => ReturnWatchService.Load(_dbPath, now), cancellationToken);

    public Task<EditResult> RemoveAsync(string orderId, DateTime now, CancellationToken cancellationToken = default)
        => Task.Run(() => ReturnWatchService.Remove(_dbPath, orderId, now), cancellationToken);

    public Task<EditResult> RestoreAsync(string orderId, DateTime now, CancellationToken cancellationToken = default)
        => Task.Run(() => ReturnWatchService.Restore(_dbPath, orderId, now), cancellationToken);
}

public sealed class FakeReturnWatchService : IReturnWatchService
{
    private readonly object _gate = new();
    private readonly List<ReturnWatchEntry> _entries;
    private readonly HashSet<string> _removedIds = new(StringComparer.Ordinal);

    public FakeReturnWatchService(IEnumerable<ReturnWatchEntry>? seed = null)
    {
        _entries = seed is not null ? seed.ToList() : CreateDemoEntries(DateTime.Now);
    }

    public Task<IReadOnlyList<ReturnWatchEntry>> LoadAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var result = _entries
                .Where(e => !_removedIds.Contains(e.Stored.Order.Id))
                .OrderBy(e => e.Stage)
                .ThenBy(e => e.Since)
                .ToList();
            return Task.FromResult<IReadOnlyList<ReturnWatchEntry>>(result);
        }
    }

    public Task<EditResult> RemoveAsync(string orderId, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var exists = _entries.Any(e => e.Stored.Order.Id == orderId);
            if (!exists) return Task.FromResult(new EditResult(EditKind.NotFound, "Đơn không có trong Bảng Hàng Hoàn Về."));
            if (!_removedIds.Add(orderId)) return Task.FromResult(new EditResult(EditKind.NoChange, "Không có thay đổi."));
            return Task.FromResult(new EditResult(EditKind.Saved, "Đã bỏ khỏi bảng."));
        }
    }

    public Task<EditResult> RestoreAsync(string orderId, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var exists = _entries.Any(e => e.Stored.Order.Id == orderId);
            if (!exists) return Task.FromResult(new EditResult(EditKind.NotFound, "Đơn không có trong Bảng Hàng Hoàn Về."));
            if (!_removedIds.Remove(orderId)) return Task.FromResult(new EditResult(EditKind.NoChange, "Không có thay đổi."));
            return Task.FromResult(new EditResult(EditKind.Saved, "Đã đưa lại vào bảng."));
        }
    }

    private static List<ReturnWatchEntry> CreateDemoEntries(DateTime now)
    {
        long Ms(DateTime dt) => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), TimeZoneInfo.Local.GetUtcOffset(dt)).ToUnixTimeMilliseconds();

        StoredOrder Make(string id, string orderId, string platform, string track, string returnTrack, string platformStatus, string status, string returnedAt, string recvDate, string product)
            => new(
                new Order
                {
                    Id = id,
                    OrderId = orderId,
                    ReturnOrderId = "RT-" + orderId,
                    Platform = platform,
                    OrderType = OrderTypeNames.ReturnRefund,
                    Track = track,
                    ReturnTrack = returnTrack,
                    PlatformStatus = platformStatus,
                    Status = status,
                    Carrier = platform == PlatformNames.Shopee ? "SPX Express" : "J&T Express",
                    ReturnedAt = returnedAt,
                    RecvDate = recvDate,
                    Price = 245000m,
                    Items = [new OrderItem { Product = product, Color = "Đen", Size = "L", Qty = 1 }]
                },
                IsArchived: false,
                ArchivedAt: "",
                IsDeleted: false,
                DeletedAt: "",
                DeletedFrom: "");

        return
        [
            new ReturnWatchEntry(Make("demo-rw-1", "261123SP001", PlatformNames.Shopee, "SPXVN001001", "", "Đã đồng ý trả hàng", ReceiveStatus.NotReturned, "", "", "Áo khoác gió 2 lớp"), Ms(now.AddHours(-30)), 1),
            new ReturnWatchEntry(Make("demo-rw-2", "78291004411", PlatformNames.TikTok, "JTVN002002", "", "Đã đồng ý hoàn tiền", ReceiveStatus.NotReturned, "", "", "Quần jean ống suông"), Ms(now.AddHours(-26)), 1),
            new ReturnWatchEntry(Make("demo-rw-3a", "261123SP002", PlatformNames.Shopee, "SPXVN003001", "RET_SPX_3001", "Đang vận chuyển về", ReceiveStatus.NotReturned, "", "", "Áo thun cotton (Kiện A)"), Ms(now.AddHours(-22)), 2),
            new ReturnWatchEntry(Make("demo-rw-3b", "261123SP002", PlatformNames.Shopee, "SPXVN003002", "RET_SPX_3002", "Đang vận chuyển về", ReceiveStatus.NotReturned, "", "", "Váy midi (Kiện B)"), Ms(now.AddHours(-21)), 2),
            new ReturnWatchEntry(Make("demo-rw-4", "78291004422", PlatformNames.TikTok, "JTVN004004", "RET_JT_4004", "Đang về shop", ReceiveStatus.NotReturned, "", "", "Giày sneaker trắng"), Ms(now.AddHours(-19)), 2),
            new ReturnWatchEntry(Make("demo-rw-5", "261123SP005", PlatformNames.Shopee, "SPXVN005005", "RET_SPX_5005", "Đã giao hàng hoàn", ReceiveStatus.NotReturned, now.AddHours(-15).ToString("dd/MM/yyyy HH:mm"), "", "Túi đeo chéo mini"), Ms(now.AddHours(-40)), 3),
            new ReturnWatchEntry(Make("demo-rw-6", "78291004433", PlatformNames.TikTok, "JTVN006006", "RET_JT_6006", "Đã về bưu cục/shop", ReceiveStatus.NotReturned, now.AddHours(-28).ToString("dd/MM/yyyy HH:mm"), "", "Áo sơ mi linen"), Ms(now.AddHours(-48)), 3),
            new ReturnWatchEntry(Make("demo-rw-7", "261123SP007", PlatformNames.Shopee, "SPXVN007007", "RET_SPX_7007", "Hoàn tất", ReceiveStatus.Received, now.AddHours(-32).ToString("dd/MM/yyyy HH:mm"), now.AddHours(-2).ToString("dd/MM/yyyy HH:mm"), "Bộ pijama lụa"), Ms(now.AddHours(-60)), 4)
        ];
    }
}
