using System.Text.Json.Nodes;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Cloud;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Quét nhận kiện qua cloud. Chỉ ACK khi cloud xác nhận đã ghi bền (quy tắc 6). Không có mạng thì KHÔNG quét (không hàng đợi).
/// Máy tính không chụp/đưa ảnh lên: ảnh đi kèm (nếu có) bị bỏ qua, chỉ điện thoại mới đưa ảnh vào kiện.
/// </summary>
public sealed class CloudScanService : IScanService
{
    private readonly CloudLinkService _link;
    private readonly Func<DateTime> _clock;

    public CloudScanService(CloudLinkService link, Func<DateTime>? clock = null) { _link = link; _clock = clock ?? (() => DateTime.Now); }

    public DateTime GetCurrentTime() => _clock();

    public async Task<ScanResult> ReceiveAsync(string code, DateTime now, byte[]? photo = null, string photoMime = "image/jpeg", CancellationToken cancellationToken = default)
    {
        if (!_link.CanWrite || _link.Api is not { } api)
            return new(ScanOutcome.Error, false, "Mất kết nối cloud: chưa thể quét. Kiểm tra mạng rồi thử lại.", null);
        try
        {
            var res = await api.OpAsync(new JsonObject { ["type"] = "scan", ["code"] = code }, cancellationToken).ConfigureAwait(false);
            if (!res.Ok || res.Result is null)
                return new(ScanOutcome.Error, false, res.ErrorMessage ?? "Cloud từ chối thao tác quét.", null);

            var r = res.Result;
            var outcome = r["outcome"]?.GetValue<string>() switch
            {
                "received" => ScanOutcome.Received,
                "alreadyReceived" => ScanOutcome.AlreadyReceived,
                "ambiguous" => ScanOutcome.Ambiguous,
                "notFound" => ScanOutcome.NotFound,
                "needsManualCheck" => ScanOutcome.NeedsManualCheck,
                _ => ScanOutcome.Error,
            };
            var ack = r["ack"]?.GetValue<bool>() ?? false;
            var message = r["message"]?.GetValue<string>() ?? "";
            if (outcome == ScanOutcome.AlreadyReceived && r["receivedBy"]?.GetValue<string>() is { Length: > 0 } who)
                message += $" (bởi {who}" + (r["receivedAt"]?.GetValue<string>() is { Length: > 0 } at ? $" lúc {at}" : "") + ")";

            Core.Models.Order? order = null;
            if (r["parcel"] is JsonObject p)
            {
                order = ParcelMapper.ToStored(p).Order;
                try { _link.Engine?.ApplyLocal(p); } catch (Exception) { /* bản sao sẽ được đồng bộ lại ở lần /sync kế tiếp */ }
            }
            return new(outcome, ack, message, order);
        }
        catch (CloudApiException ex)
        {
            // Không biết cloud đã nhận hay chưa: KHÔNG ACK, yêu cầu kiểm tra lại đơn trước khi quét lại.
            var msg = ex.IsNetwork ? "Mất kết nối cloud giữa chừng: chưa chắc kiện đã được nhận. Hãy kiểm tra lại đơn trước khi quét lại." : ex.Message;
            return new(ScanOutcome.Error, false, msg, null);
        }
    }
}

/// <summary>Sửa/xóa mềm/lưu trữ qua cloud. Cùng quy tắc: cần mạng, không hàng đợi; kết quả cloud được áp ngay vào bản sao.</summary>
public sealed class CloudOrderEditService : IOrderEditService
{
    private readonly CloudLinkService _link;

    public CloudOrderEditService(CloudLinkService link) { _link = link; }

    private static EditResult Offline() => new(EditKind.Error, "Mất kết nối cloud: mọi thao tác sửa tạm khóa. Kiểm tra mạng rồi thử lại.");

    private async Task<EditResult> RunAsync(JsonObject op, CancellationToken ct)
    {
        if (!_link.CanWrite || _link.Api is not { } api) return Offline();
        try
        {
            var res = await api.OpAsync(op, ct).ConfigureAwait(false);
            if (!res.Ok)
                return new(res.ErrorCode switch
                {
                    "conflict" => EditKind.Conflict, "not_found" => EditKind.NotFound, "invalid" => EditKind.Invalid, _ => EditKind.Error,
                }, res.ErrorMessage ?? "Cloud từ chối thao tác.");
            if (res.Result?["parcel"] is JsonObject p)
                try { _link.Engine?.ApplyLocal(p); } catch (Exception) { /* sẽ đồng bộ lại */ }
            return new(EditKind.Saved, "Đã lưu.");
        }
        catch (CloudApiException ex) { return new(EditKind.Error, ex.IsNetwork ? "Mất kết nối cloud giữa chừng: chưa chắc đã lưu. Hãy tải lại rồi kiểm tra." : ex.Message); }
    }

    public Task<EditResult> SetFieldAsync(string orderId, EditField field, string newValue, string? expectedOld = null, CancellationToken cancellationToken = default)
    {
        newValue ??= "";
        JsonObject? op = field switch
        {
            EditField.Status => new JsonObject { ["type"] = "setStatus", ["id"] = orderId, ["status"] = newValue, ["expectedStatus"] = expectedOld },
            EditField.Note => new JsonObject { ["type"] = "editNote", ["id"] = orderId, ["note"] = newValue },
            EditField.UserNote => new JsonObject { ["type"] = "editNote", ["id"] = orderId, ["userNote"] = newValue },
            EditField.Carrier => Cell(orderId, "carrier", newValue),
            EditField.Deadline => Cell(orderId, "deadline", newValue),
            EditField.WatchlistReason => Cell(orderId, "watchlistReason", newValue),
            EditField.ComplaintStatus => Cell(orderId, "complaintStatus", newValue),
            EditField.ComplaintNote => Cell(orderId, "complaintNote", newValue),
            EditField.IsWatchlist when newValue is "0" or "1" => new JsonObject { ["type"] = "setWatch", ["id"] = orderId, ["on"] = newValue == "1" },
            _ => null,
        };
        if (op is null)
            return Task.FromResult(new EditResult(EditKind.Invalid,
                field == EditField.BuyerNote ? "Ghi chú người mua không lưu trên cloud." : "Giá trị hoặc ô không hợp lệ."));
        return RunAsync(op, cancellationToken);
    }

    public Task<EditResult> SetComplaintAsync(string orderId, string status, string reason, string? expectedOld = null, CancellationToken cancellationToken = default)
    {
        var op = Cell(orderId, "complaintStatus", status ?? "");
        if (!string.IsNullOrWhiteSpace(reason)) op["reason"] = reason.Trim();
        return RunAsync(op, cancellationToken);
    }

    private static JsonObject Cell(string id, string field, string value) => new() { ["type"] = "editCell", ["id"] = id, ["field"] = field, ["value"] = value };

    public Task<EditResult> DeleteAsync(string orderId, string from, CancellationToken cancellationToken = default) =>
        RunAsync(new JsonObject { ["type"] = "delete", ["id"] = orderId }, cancellationToken);
    public Task<EditResult> RestoreAsync(string orderId, CancellationToken cancellationToken = default) =>
        RunAsync(new JsonObject { ["type"] = "restore", ["id"] = orderId }, cancellationToken);
    public Task<EditResult> PurgeAsync(string orderId, CancellationToken cancellationToken = default) =>
        RunAsync(new JsonObject { ["type"] = "purge", ["id"] = orderId }, cancellationToken);
    public Task<EditResult> ArchiveAsync(string orderId, CancellationToken cancellationToken = default) =>
        RunAsync(new JsonObject { ["type"] = "archive", ["id"] = orderId }, cancellationToken);
    public Task<EditResult> UnarchiveAsync(string orderId, CancellationToken cancellationToken = default) =>
        RunAsync(new JsonObject { ["type"] = "unarchive", ["id"] = orderId }, cancellationToken);

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["status"] = "Nhận tại kho", ["note"] = "Lý do", ["userNote"] = "Ghi chú của tôi", ["carrier"] = "Đơn vị vận chuyển", ["deadline"] = "Hạn xử lý",
        ["isWatchlist"] = "Theo dõi", ["watchlistReason"] = "Lý do theo dõi", ["isArchived"] = "Lưu trữ", ["isDeleted"] = "Đã xóa", ["recvDate"] = "Ngày nhận",
    };

    public async Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var list = new List<HistoryEntry>();
        if (_link.Api is not { } api) return list;
        try
        {
            foreach (var e in await api.HistoryAsync(orderId, cancellationToken).ConfigureAwait(false))
            {
                if (e is not JsonObject o) continue;
                var at = DateTimeOffset.FromUnixTimeMilliseconds(o["at"]?.GetValue<long>() ?? 0).ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm:ss");
                var action = o["action"]?.GetValue<string>() ?? "";
                var before = o["before"] as JsonObject; var after = o["after"] as JsonObject;
                var who = o["deviceName"]?.GetValue<string>() ?? "";
                foreach (var kv in after ?? new JsonObject())
                    list.Add(new HistoryEntry(at, kv.Key, (Labels.GetValueOrDefault(kv.Key, kv.Key)) + (who.Length > 0 ? $" ({who})" : ""),
                        before?[kv.Key]?.ToString() ?? "", kv.Value?.ToString() ?? "", action));
            }
        }
        catch (CloudApiException) { /* không có lịch sử khi mất mạng */ }
        return list;
    }
}

/// <summary>
/// Bảng Hàng Hoàn Về qua cloud: đọc từ bản sao SQLite (KHÔNG tự thêm đơn ở máy: việc tự vào bảng do cloud làm, mọi máy thấy như nhau);
/// bỏ/đưa lại vào bảng đi qua cloud, cần mạng.
/// </summary>
public sealed class CloudReturnWatchService : IReturnWatchService
{
    private readonly CloudLinkService _link;
    private readonly string _dbPath;

    public CloudReturnWatchService(CloudLinkService link, string dbPath) { _link = link; _dbPath = dbPath; }

    public Task<IReadOnlyList<ReturnWatchEntry>> LoadAsync(DateTime now, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<ReturnWatchEntry>>(() =>
        {
            var all = OrderRepository.Load(_dbPath);
            var marks = ReturnWatchService.ActiveMarks(_dbPath, all);
            return all.Where(s => marks.ContainsKey(s.Order.Id))
                .Select(s => new ReturnWatchEntry(s, marks[s.Order.Id].Since, marks[s.Order.Id].Stage))
                .OrderBy(e => e.Stage).ThenBy(e => e.Since).ToList();
        }, cancellationToken);

    public Task<EditResult> RemoveAsync(string orderId, DateTime now, CancellationToken cancellationToken = default) => SetAsync(orderId, true, cancellationToken);
    public Task<EditResult> RestoreAsync(string orderId, DateTime now, CancellationToken cancellationToken = default) => SetAsync(orderId, false, cancellationToken);

    private async Task<EditResult> SetAsync(string orderId, bool off, CancellationToken ct)
    {
        if (!_link.CanWrite || _link.Api is not { } api) return new(EditKind.Error, "Mất kết nối cloud: tạm khóa thao tác này. Kiểm tra mạng rồi thử lại.");
        try
        {
            var res = await api.OpAsync(new JsonObject { ["type"] = off ? "rwRemove" : "rwRestore", ["id"] = orderId }, ct).ConfigureAwait(false);
            if (!res.Ok)
                return new(res.ErrorCode == "not_found" ? EditKind.NotFound : EditKind.Error, res.ErrorMessage ?? "Cloud từ chối thao tác.");
            try { _link.Engine?.ApplyWatchLocal(orderId, CloudReplica.WatchSince(_dbPath, orderId), off); } catch (Exception) { /* sẽ đồng bộ lại */ }
            return new(EditKind.Saved, off ? "Đã bỏ khỏi bảng." : "Đã đưa lại vào bảng.");
        }
        catch (CloudApiException ex) { return new(EditKind.Error, ex.IsNetwork ? "Mất kết nối cloud giữa chừng: chưa chắc đã lưu. Hãy tải lại rồi kiểm tra." : ex.Message); }
    }
}