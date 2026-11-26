using System.Text.Json.Nodes;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Data.Cloud;

/// <summary>
/// Chuyển đổi Parcel (JSON gọn của cloud, cloud/API.md mục 1) ⇄ Order của v2. Cloud KHÔNG có ghi chú người mua, dữ liệu thô và
/// dữ liệu cá nhân; phần "extra" chỉ gồm các khóa trong danh sách trắng.
/// </summary>
public static class ParcelMapper
{
    private static string S(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    /// <summary>Số từ JSON (nguyên hay thập phân, dạng số hay chữ số); không đọc được thì 0.</summary>
    public static decimal Num(JsonNode? n) =>
        n is null ? 0m : decimal.TryParse(n.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0m;

    private static bool B(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static List<string> Strs(JsonNode? n)
    {
        var l = new List<string>();
        if (n is JsonArray a) foreach (var x in a) l.Add(S(x));
        return l;
    }

    /// <summary>Kiện trên cloud → đơn (kèm cờ lưu trữ/đã xóa).</summary>
    public static StoredOrder ToStored(JsonObject p)
    {
        var o = new Order
        {
            Id = S(p["id"]), OrderId = S(p["orderId"]), ReturnOrderId = S(p["returnOrderId"]), Track = S(p["track"]),
            ReturnTrack = S(p["returnTrack"]), AltTrack = S(p["altTrack"]), ForwardTrack = S(p["forwardTrack"]),
            TrackingCodes = Strs(p["trackingCodes"]), ScanAliases = Strs(p["scanAliases"]),
            Platform = S(p["platform"]), OrderType = S(p["orderType"]), PlatformStatus = S(p["platformStatus"]), PreOrder = S(p["preOrder"]),
            Status = S(p["status"]), Carrier = S(p["carrier"]), ShipDate = S(p["shipDate"]), ReturnDate = S(p["returnDate"]),
            RecvDate = S(p["recvDate"]), ReturnedAt = S(p["returnedAt"]), Deadline = S(p["deadline"]),
            Price = Num(p["price"]),
            Note = S(p["note"]), UserNote = S(p["userNote"]), ImportWeek = S(p["importWeek"]), ImportedAt = S(p["importedAt"]),
            IsWatchlist = B(p["isWatchlist"]), WatchlistReason = S(p["watchlistReason"]), IsPartialReturn = B(p["isPartialReturn"]),
            PhotoId = S(p["photoId"]),
            ComplaintStatus = S(p["complaintStatus"]), ComplaintAt = S(p["complaintAt"]), ComplaintNote = S(p["complaintNote"]), ComplaintDoneAt = S(p["complaintDoneAt"]),
        };
        if (p["items"] is JsonArray items)
            foreach (var i in items)
                if (i is JsonObject it)
                    o.Items.Add(new OrderItem
                    {
                        Product = S(it["product"]), Color = S(it["color"]), Size = S(it["size"]),
                        Qty = (int)Num(it["qty"]),
                        ReturnedQty = (int)Num(it["returnedQty"]),
                        Sku = it["sku"] is null ? null : S(it["sku"]), SkuId = it["skuId"] is null ? null : S(it["skuId"]),
                    });
        o.ExtraJson = ExtraFrom(p["extra"] as JsonObject, null);
        var extraText = o.ExtraJson;
        o.SetLazyImport(() => OrderExtra.Read(extraText));   // giống OrderRepository: đọc extra_json khi cần
        return new StoredOrder(o, B(p["isArchived"]), S(p["archivedAt"]), B(p["isDeleted"]), S(p["deletedAt"]), S(p["deletedFrom"]));
    }

    /// <summary>Băm ổn định của một kiện (bỏ trường `hash`/`rev`/`updatedAt`): so sánh "đã đồng bộ với cloud chưa".</summary>
    public static string Hash(JsonObject parcel)
    {
        var c = (JsonObject)parcel.DeepClone();
        c.Remove("hash"); c.Remove("rev"); c.Remove("updatedAt");
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(c.ToJsonString()))).ToLowerInvariant();
    }

    /// <summary>
    /// Đổi cả danh sách đơn thành kiện cloud, xử lý đơn TRÙNG khóa id (dữ liệu cũ có các dòng hệt nhau): dòng trùng có nội dung gửi lên cloud
    /// GIỐNG HỆT dòng trước thì bỏ qua (không mất thông tin nào), khác thì giữ lại với id có hậu tố "~2", "~3"... để không ai ghi đè ai.
    /// </summary>
    public static List<JsonObject> ToParcels(IEnumerable<StoredOrder> orders, out int skippedIdentical, out int renamed)
    {
        var list = new List<JsonObject>();
        var firstHash = new Dictionary<string, string>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        skippedIdentical = 0; renamed = 0;
        foreach (var s in orders)
        {
            var p = ToParcel(s);
            var id = p["id"]!.GetValue<string>();
            var h = Hash(p);
            if (!firstHash.TryGetValue(id, out var h0)) { firstHash[id] = h; counts[id] = 1; list.Add(p); continue; }
            if (h0 == h) { skippedIdentical++; continue; }
            counts[id]++;
            p["id"] = id + "~" + counts[id];
            renamed++;
            list.Add(p);
        }
        return list;
    }
    private static readonly string[] RawKeys = { "returnReason", "returnStatus", "returnShippingStatus", "returnSuccessTime", "cancelType" };
    private static readonly string[] TopKeys = { "forwardCarrier", "returnCarrier" };

    /// <summary>
    /// Ghép các khóa "extra" của cloud vào extra_json cục bộ: khóa trong danh sách trắng được CẬP NHẬT, mọi khóa khác của dữ liệu cũ
    /// (rawDetails khác, giá...) giữ nguyên.
    /// </summary>
    public static string ExtraFrom(JsonObject? extra, string? existingJson)
    {
        JsonObject obj;
        try { obj = string.IsNullOrWhiteSpace(existingJson) ? new JsonObject() : JsonNode.Parse(existingJson) as JsonObject ?? new JsonObject(); }
        catch (System.Text.Json.JsonException) { obj = new JsonObject(); }
        if (extra is null) return obj.Count == 0 ? "" : obj.ToJsonString();

        var raw = obj["rawDetails"] as JsonObject ?? new JsonObject();
        foreach (var k in RawKeys) if (extra[k] is JsonValue v) raw[k] = JsonValue.Create(v.ToString());
        if (raw.Count > 0) obj["rawDetails"] = raw;
        foreach (var k in TopKeys) if (extra[k] is JsonValue v && v.ToString().Length > 0) obj[k] = JsonValue.Create(v.ToString());
        if (extra["priceNeedsReview"] is JsonValue pn && pn.TryGetValue<bool>(out var pnb)) obj["priceNeedsReview"] = pnb;
        if (extra["priceHasValue"] is JsonValue ph && ph.TryGetValue<bool>(out var phb)) obj["priceHasValue"] = phb;
        return obj.Count == 0 ? "" : obj.ToJsonString();
    }

    /// <summary>Đơn → kiện gọn để gửi lên cloud (nhập đơn). KHÔNG đưa ghi chú người mua, dữ liệu thô, giá theo chế độ nhập.</summary>
    public static JsonObject ToParcel(StoredOrder s)
    {
        var o = s.Order;
        var meta = o.Import;
        var extra = new JsonObject();
        foreach (var k in RawKeys) if (meta.RawDetails.TryGetValue(k, out var v) && v.Length > 0) extra[k] = v;
        if (meta.ForwardCarrier.Length > 0) extra["forwardCarrier"] = meta.ForwardCarrier;
        if (meta.ReturnCarrier.Length > 0) extra["returnCarrier"] = meta.ReturnCarrier;
        if (meta.PriceNeedsReview is not null) extra["priceNeedsReview"] = meta.PriceNeedsReview;
        if (meta.PriceHasValue is not null) extra["priceHasValue"] = meta.PriceHasValue;

        var items = new JsonArray();
        foreach (var i in o.Items)
        {
            var it = new JsonObject { ["product"] = i.Product, ["color"] = i.Color, ["size"] = i.Size, ["qty"] = i.Qty, ["returnedQty"] = i.ReturnedQty };
            if (i.Sku is not null) it["sku"] = i.Sku;
            if (i.SkuId is not null) it["skuId"] = i.SkuId;
            items.Add(it);
        }
        JsonArray Arr(IEnumerable<string> x) { var a = new JsonArray(); foreach (var y in x) a.Add(y); return a; }
        var parcel = new JsonObject
        {
            ["id"] = o.Id, ["orderId"] = o.OrderId, ["returnOrderId"] = o.ReturnOrderId, ["track"] = o.Track, ["returnTrack"] = o.ReturnTrack,
            ["altTrack"] = o.AltTrack, ["forwardTrack"] = o.ForwardTrack, ["trackingCodes"] = Arr(o.TrackingCodes), ["scanAliases"] = Arr(o.ScanAliases),
            ["platform"] = o.Platform, ["orderType"] = o.OrderType, ["platformStatus"] = o.PlatformStatus, ["preOrder"] = o.PreOrder,
            ["status"] = o.Status, ["carrier"] = o.Carrier, ["shipDate"] = o.ShipDate, ["returnDate"] = o.ReturnDate, ["recvDate"] = o.RecvDate,
            ["returnedAt"] = o.ReturnedAt, ["deadline"] = o.Deadline, ["price"] = o.Price, ["note"] = o.Note, ["userNote"] = o.UserNote,
            ["importWeek"] = o.ImportWeek, ["importedAt"] = o.ImportedAt, ["isWatchlist"] = o.IsWatchlist, ["watchlistReason"] = o.WatchlistReason,
            ["isPartialReturn"] = o.IsPartialReturn, ["photoId"] = o.PhotoId, ["items"] = items, ["extra"] = extra,
            ["isArchived"] = s.IsArchived, ["archivedAt"] = s.ArchivedAt, ["isDeleted"] = s.IsDeleted, ["deletedAt"] = s.DeletedAt, ["deletedFrom"] = s.DeletedFrom,
        };
        // khiếu nại: chỉ có khóa khi đơn đang/đã khiếu nại (đơn thường giữ nguyên băm cũ)
        if (o.ComplaintStatus.Length > 0)
        {
            parcel["complaintStatus"] = o.ComplaintStatus;
            if (o.ComplaintAt.Length > 0) parcel["complaintAt"] = o.ComplaintAt;
            if (o.ComplaintNote.Length > 0) parcel["complaintNote"] = o.ComplaintNote;
            if (o.ComplaintDoneAt.Length > 0) parcel["complaintDoneAt"] = o.ComplaintDoneAt;
        }
        return parcel;
    }
}
