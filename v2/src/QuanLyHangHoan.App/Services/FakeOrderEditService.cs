using System.Globalization;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Bản giả cho chế độ --demo và kiểm thử: sửa trực tiếp trong bộ nhớ, KHÔNG bao giờ ghi DB.
/// </summary>
public sealed class FakeOrderEditService : IOrderEditService
{
    private static readonly string[] ValidStatuses =
    [
        ReceiveStatus.Received,
        ReceiveStatus.NotReturned,
        ReceiveStatus.NotNeeded,
        ReceiveStatus.Lost,
        ReceiveStatus.NotYetArrived
    ];

    private readonly FakeDataService _fakeData;

    public FakeOrderEditService(FakeDataService fakeData)
    {
        _fakeData = fakeData;
    }

    private static string Stamp(DateTime t) => t.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);

    public Task<EditResult> SetFieldAsync(
        string orderId,
        EditField field,
        string newValue,
        string? expectedOld = null,
        CancellationToken cancellationToken = default)
    {
        newValue ??= "";
        if (field == EditField.Status && !ValidStatuses.Contains(newValue))
            return Task.FromResult(new EditResult(EditKind.Invalid, "Trạng thái không hợp lệ."));
        if (field == EditField.IsWatchlist && newValue is not ("0" or "1"))
            return Task.FromResult(new EditResult(EditKind.Invalid, "Cờ theo dõi chỉ nhận 0 hoặc 1."));

        lock (_fakeData.StoreLock)
        {
            var entry = _fakeData.EnsureDemoStore().FirstOrDefault(e => e.Order.Id == orderId);
            if (entry is null) return Task.FromResult(new EditResult(EditKind.NotFound, "Không tìm thấy đơn."));
            if (entry.IsDeleted) return Task.FromResult(new EditResult(EditKind.NotEditable, "Đơn đã xóa: khôi phục trước khi sửa."));

            var old = ReadField(entry.Order, field);
            if (expectedOld is not null && old != expectedOld)
                return Task.FromResult(new EditResult(EditKind.Conflict, "Giá trị đã bị thay đổi ở nơi khác, hãy tải lại."));
            if (old == newValue)
                return Task.FromResult(new EditResult(EditKind.NoChange, "Không có thay đổi."));

            var now = _fakeData.GetCurrentTime();
            WriteField(entry.Order, field, newValue);
            var (jsonField, label) = FieldMeta(field);
            entry.History.Insert(0, new HistoryEntry(Stamp(now), jsonField, label, old, newValue, "manual"));

            if (field == EditField.Status && newValue == ReceiveStatus.Received && string.IsNullOrEmpty(entry.Order.RecvDate))
            {
                var recvStamp = Stamp(now);
                entry.Order.RecvDate = recvStamp;
                entry.History.Insert(0, new HistoryEntry(recvStamp, "recvDate", "Ngày nhận", "", recvStamp, "manual"));
            }

            return Task.FromResult(new EditResult(EditKind.Saved, "Đã lưu."));
        }
    }

    public Task<EditResult> DeleteAsync(string orderId, string from, CancellationToken cancellationToken = default)
    {
        lock (_fakeData.StoreLock)
        {
            var entry = _fakeData.EnsureDemoStore().FirstOrDefault(e => e.Order.Id == orderId);
            if (entry is null) return Task.FromResult(new EditResult(EditKind.NotFound, "Không tìm thấy đơn."));
            if (entry.IsDeleted) return Task.FromResult(new EditResult(EditKind.NoChange, "Đơn đã xóa từ trước."));

            entry.IsDeleted = true;
            entry.DeletedFrom = from ?? "";
            entry.DeletedAt = Stamp(_fakeData.GetCurrentTime());
            return Task.FromResult(new EditResult(EditKind.Saved, "Đã xóa (có thể khôi phục)."));
        }
    }

    public Task<EditResult> RestoreAsync(string orderId, CancellationToken cancellationToken = default)
    {
        lock (_fakeData.StoreLock)
        {
            var entry = _fakeData.EnsureDemoStore().FirstOrDefault(e => e.Order.Id == orderId);
            if (entry is null) return Task.FromResult(new EditResult(EditKind.NotFound, "Không tìm thấy đơn."));
            if (!entry.IsDeleted) return Task.FromResult(new EditResult(EditKind.NoChange, "Đơn không ở trạng thái đã xóa."));

            entry.IsDeleted = false;
            entry.DeletedFrom = string.Empty;
            entry.DeletedAt = string.Empty;
            return Task.FromResult(new EditResult(EditKind.Saved, "Đã khôi phục."));
        }
    }

    public Task<EditResult> ArchiveAsync(string orderId, CancellationToken cancellationToken = default)
    {
        lock (_fakeData.StoreLock)
        {
            var entry = _fakeData.EnsureDemoStore().FirstOrDefault(e => e.Order.Id == orderId);
            if (entry is null) return Task.FromResult(new EditResult(EditKind.NotFound, "Không tìm thấy đơn."));
            if (entry.IsDeleted) return Task.FromResult(new EditResult(EditKind.NotEditable, "Đơn đã xóa."));
            if (entry.IsArchived) return Task.FromResult(new EditResult(EditKind.NoChange, "Đơn đã ở kho lưu trữ."));

            entry.IsArchived = true;
            return Task.FromResult(new EditResult(EditKind.Saved, "Đã dời sang kho lưu trữ."));
        }
    }

    public Task<EditResult> UnarchiveAsync(string orderId, CancellationToken cancellationToken = default)
    {
        lock (_fakeData.StoreLock)
        {
            var entry = _fakeData.EnsureDemoStore().FirstOrDefault(e => e.Order.Id == orderId);
            if (entry is null) return Task.FromResult(new EditResult(EditKind.NotFound, "Không tìm thấy đơn."));
            if (entry.IsDeleted) return Task.FromResult(new EditResult(EditKind.NotEditable, "Đơn đã xóa."));
            if (!entry.IsArchived) return Task.FromResult(new EditResult(EditKind.NoChange, "Đơn không ở kho lưu trữ."));

            entry.IsArchived = false;
            return Task.FromResult(new EditResult(EditKind.Saved, "Đã trả về bảng chính."));
        }
    }

    public Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(string orderId, CancellationToken cancellationToken = default)
    {
        lock (_fakeData.StoreLock)
        {
            var entry = _fakeData.EnsureDemoStore().FirstOrDefault(e => e.Order.Id == orderId);
            IReadOnlyList<HistoryEntry> result = entry is null
                ? Array.Empty<HistoryEntry>()
                : entry.History.ToList();
            return Task.FromResult(result);
        }
    }

    private static string ReadField(Order o, EditField field) => field switch
    {
        EditField.Status => o.Status,
        EditField.Note => o.Note,
        EditField.UserNote => o.UserNote,
        EditField.BuyerNote => o.BuyerNote,
        EditField.Carrier => o.Carrier,
        EditField.Deadline => o.Deadline,
        EditField.IsWatchlist => o.IsWatchlist ? "1" : "0",
        EditField.WatchlistReason => o.WatchlistReason,
        _ => ""
    };

    private static void WriteField(Order o, EditField field, string val)
    {
        switch (field)
        {
            case EditField.Status: o.Status = val; break;
            case EditField.Note: o.Note = val; break;
            case EditField.UserNote: o.UserNote = val; break;
            case EditField.BuyerNote: o.BuyerNote = val; break;
            case EditField.Carrier: o.Carrier = val; break;
            case EditField.Deadline: o.Deadline = val; break;
            case EditField.IsWatchlist: o.IsWatchlist = val == "1"; break;
            case EditField.WatchlistReason: o.WatchlistReason = val; break;
        }
    }

    private static (string Field, string Label) FieldMeta(EditField f) => f switch
    {
        EditField.Status => ("status", "Nhận tại kho"),
        EditField.Note => ("note", "Lý do"),
        EditField.UserNote => ("userNote", "Ghi chú của tôi"),
        EditField.BuyerNote => ("buyerNote", "Ghi chú người mua"),
        EditField.Carrier => ("carrier", "Đơn vị vận chuyển"),
        EditField.Deadline => ("deadline", "Hạn xử lý"),
        EditField.IsWatchlist => ("isWatchlist", "Theo dõi"),
        _ => ("watchlistReason", "Lý do theo dõi")
    };
}
