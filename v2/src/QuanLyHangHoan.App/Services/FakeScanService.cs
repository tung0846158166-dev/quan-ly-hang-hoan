using System.Globalization;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Core.Tracking;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Dịch vụ quét giả lập cho chế độ --demo (chỉ lưu trong bộ nhớ, không ghi SQLite).
/// </summary>
public sealed class FakeScanService : IScanService
{
    private readonly List<Order> _orders;
    private readonly Func<DateTime> _clock;

    public FakeScanService(IEnumerable<Order> seedOrders, Func<DateTime>? clock = null)
    {
        _orders = seedOrders.Select(CloneOrder).ToList();
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
        var match = TrackingResolver.Resolve(_orders, code);
        if (match.Kind == MatchKind.NotFound)
        {
            return Task.FromResult(new ScanResult(ScanOutcome.NotFound, false, "Không tìm thấy kiện (demo).", null));
        }

        if (match.Kind == MatchKind.Ambiguous)
        {
            return Task.FromResult(new ScanResult(
                ScanOutcome.Ambiguous,
                false,
                $"{match.Candidates.Count} kiện cùng khớp mã này, cần kiểm tra thủ công (demo).",
                null));
        }

        var order = match.Single!;
        var alreadyReceived = order.Status == ReceiveStatus.Received;

        if (!alreadyReceived && order.Status is not (ReceiveStatus.NotReturned or ReceiveStatus.NotYetArrived or ""))
        {
            return Task.FromResult(new ScanResult(
                ScanOutcome.NeedsManualCheck,
                false,
                $"Trạng thái \"{order.Status}\": cần kiểm tra thủ công (demo).",
                CloneOrder(order)));
        }

        if (!alreadyReceived)
        {
            order.Status = ReceiveStatus.Received;
            order.RecvDate = now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (photo is { Length: > 0 })
        {
            order.PhotoId = $"demo_p_{TrackingResolver.Normalize(order.Track)}_{now:yyyyMMddHHmmss}";
        }

        var result = alreadyReceived
            ? new ScanResult(ScanOutcome.AlreadyReceived, photo is { Length: > 0 }, "Kiện đã nhận trước đó (demo).", CloneOrder(order))
            : new ScanResult(ScanOutcome.Received, true, "Đã nhận kiện (demo).", CloneOrder(order));

        return Task.FromResult(result);
    }

    private static Order CloneOrder(Order source) => new()
    {
        Id = source.Id,
        OrderId = source.OrderId,
        ReturnOrderId = source.ReturnOrderId,
        Track = source.Track,
        ReturnTrack = source.ReturnTrack,
        AltTrack = source.AltTrack,
        ForwardTrack = source.ForwardTrack,
        TrackingCodes = [.. source.TrackingCodes],
        ScanAliases = [.. source.ScanAliases],
        Platform = source.Platform,
        OrderType = source.OrderType,
        PlatformStatus = source.PlatformStatus,
        PreOrder = source.PreOrder,
        Status = source.Status,
        Carrier = source.Carrier,
        ShipDate = source.ShipDate,
        ReturnDate = source.ReturnDate,
        RecvDate = source.RecvDate,
        ReturnedAt = source.ReturnedAt,
        Deadline = source.Deadline,
        Price = source.Price,
        Note = source.Note,
        BuyerNote = source.BuyerNote,
        UserNote = source.UserNote,
        ImportWeek = source.ImportWeek,
        ImportedAt = source.ImportedAt,
        IsWatchlist = source.IsWatchlist,
        WatchlistReason = source.WatchlistReason,
        IsPartialReturn = source.IsPartialReturn,
        PhotoId = source.PhotoId,
        ExtraJson = source.ExtraJson,
        Items = source.Items.Select(i => new OrderItem
        {
            Product = i.Product,
            Color = i.Color,
            Size = i.Size,
            Qty = i.Qty,
            Sku = i.Sku,
            SkuId = i.SkuId
        }).ToList()
    };
}
