using System.Text.Json.Nodes;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Cloud;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Chế độ cloud: tạo đơn mới bằng tay trên máy tính đi qua cloud (thao tác "newOrder", cùng quy tắc NewOrderService:
/// mã đã có ở bất kỳ đâu thì không tạo lại). Mất mạng thì từ chối, không ghi riêng vào máy (không hàng đợi ngoại tuyến).
/// </summary>
public sealed class CloudNewOrderCreator : INewOrderCreator
{
    private readonly Func<bool> _canWrite;
    private readonly Func<JsonObject, CancellationToken, Task<CloudOpResult>> _op;

    public CloudNewOrderCreator(Func<bool> canWrite, Func<JsonObject, CancellationToken, Task<CloudOpResult>> op) { _canWrite = canWrite; _op = op; }

    public async Task<NewOrderResult> CreateAsync(NewOrderInput input)
    {
        var raw = (input.Track ?? "").Trim();
        if (!_canWrite()) return new(false, "invalid", "Mất kết nối cloud: chưa tạo được đơn. Kiểm tra mạng rồi thử lại.", raw, "", "");
        var op = new JsonObject
        {
            ["type"] = "newOrder", ["code"] = raw, ["orderId"] = input.OrderId ?? "", ["platform"] = input.Platform ?? "", ["carrier"] = input.Carrier ?? "",
            ["product"] = input.Product ?? "", ["color"] = input.Color ?? "", ["size"] = input.Size ?? "", ["qty"] = input.Qty > 0 ? input.Qty : 1,
            ["note"] = input.Note ?? "", ["origin"] = string.Equals(input.Origin, "phone", StringComparison.OrdinalIgnoreCase) ? "phone" : "manual",
        };
        // đơn ngoài sàn (27/11/2026): nhiều sản phẩm, giá trị, loại đơn, kênh bán; mã trống thì cloud tự sinh mã "NS..."
        if (input.Items.Count > 0)
            op["items"] = new JsonArray(input.Items.Select(x => (JsonNode)new JsonObject { ["product"] = x.Product ?? "", ["color"] = x.Color ?? "", ["size"] = x.Size ?? "", ["qty"] = x.Qty > 0 ? x.Qty : 1 }).ToArray());
        if (input.Price > 0) op["price"] = input.Price;
        if (input.OrderType.Length > 0) op["orderType"] = input.OrderType;
        if (input.Channel.Length > 0) op["channel"] = input.Channel;
        try
        {
            var res = await _op(op, default).ConfigureAwait(false);
            if (!res.Ok) return new(false, "invalid", res.ErrorMessage ?? "Cloud từ chối tạo đơn.", raw, "", "");
            var r = res.Result ?? new JsonObject();
            string S(string k) => r[k] is JsonValue v && v.TryGetValue<string>(out var x) ? x : "";
            var outcome = S("outcome");
            return new(outcome == "success", outcome.Length > 0 ? outcome : "invalid", S("message"), S("code").Length > 0 ? S("code") : raw, S("platform"), S("orderId"));
        }
        catch (CloudApiException ex)
        {
            return new(false, "invalid", ex.IsNetwork ? "Mất kết nối cloud giữa chừng: chưa chắc đã tạo. Tìm lại mã trước khi tạo lại." : ex.Message, raw, "", "");
        }
    }
}

/// <summary>
/// Chế độ cloud: tự chuyển đơn đã nhận quá hạn vào Lưu trữ bằng thao tác "archive" lên cloud (không sửa bản sao trên máy;
/// bản sao đổi khi cloud gửi về). opId cố định theo ngày nên gửi lại không làm hai lần. Mất mạng: bỏ qua, lần sau quét lại.
/// </summary>
public sealed class CloudArchiveSweeper : IArchiveSweeper
{
    private readonly string _dbPath;
    private readonly Func<bool> _canWrite;
    private readonly Func<IEnumerable<JsonObject>, CancellationToken, Task<IReadOnlyList<CloudOpResult>>> _ops;

    public CloudArchiveSweeper(string dbPath, Func<bool> canWrite, Func<IEnumerable<JsonObject>, CancellationToken, Task<IReadOnlyList<CloudOpResult>>> ops)
    { _dbPath = dbPath; _canWrite = canWrite; _ops = ops; }

    public async Task<int> SweepAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        if (!_canWrite()) return 0;
        var ids = await Task.Run(() => ArchiveSweep.DueIds(_dbPath, now), cancellationToken).ConfigureAwait(false);
        var day = now.ToString("yyyyMMdd");
        var done = 0;
        foreach (var chunk in ids.Chunk(50))
        {
            try
            {
                var res = await _ops(chunk.Select(id => new JsonObject { ["opId"] = $"sweep-{day}-{id}", ["type"] = "archive", ["id"] = id }), cancellationToken).ConfigureAwait(false);
                done += res.Count(r => r.Ok);
            }
            catch (CloudApiException) { break; }   // mất mạng / hết hạn mức: để lần quét sau
        }
        return done;
    }
}
