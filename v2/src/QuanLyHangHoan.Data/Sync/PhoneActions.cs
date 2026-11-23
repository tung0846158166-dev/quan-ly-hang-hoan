using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Core.Tracking;

namespace QuanLyHangHoan.Data.Sync;

/// <summary>Kết quả xử lý một thao tác từ app iPhone: bản tin xác nhận (pc_ack) và có đổi dữ liệu không (để gửi lại danh sách).</summary>
public sealed record PhoneActionResult(string Ack, bool Changed, string RequestId, string Code, string Outcome, string Message, string Stage)
{
    /// <summary>"new_order" khi điện thoại vừa tạo đơn mới ở bảng chính; "" với các thao tác khác.</summary>
    public string Kind { get; init; } = "";
}

/// <summary>
/// Các thao tác app iPhone gửi lên ngoài việc quét nhận kiện: order_note (ghi chú + theo dõi), order_edit (đổi trạng thái),
/// order_delete (xóa MỀM, khôi phục được), cancel_receive (đánh dấu đã nhận đơn hủy), new_order_scan / inbox_push(_many) (chưa hỗ trợ).
/// Quy tắc: không đoán (mã khớp nhiều kiện = ambiguous, không đổi gì); mọi thay đổi đi qua OrderEditor (ghi lịch sử + nhật ký
/// cùng giao dịch); chỉ báo "success" SAU KHI đã lưu; gửi lại cùng requestId trả đúng xác nhận cũ.
/// </summary>
public static class PhoneActions
{
    private static readonly ConcurrentDictionary<string, object> DbLocks = new(StringComparer.OrdinalIgnoreCase);
    private static object LockFor(string db) => DbLocks.GetOrAdd(Path.GetFullPath(db), _ => new object());

    private static readonly HashSet<string> Handled = new()
    {
        "order_note", "order_edit", "order_delete", "cancel_receive", "new_order_scan", "inbox_push", "inbox_push_many",
    };

    public static PhoneActionResult? Handle(string dbPath, string room, string messageJson, DateTime now)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(messageJson); } catch (JsonException) { return null; }
        using (doc)
        {
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            var type = Str(r, "type");
            if (!Handled.Contains(type) || Str(r, "room") != room) return null;
            var requestId = Str(r, "requestId").Trim();
            if (requestId.Length == 0) return null; // không có requestId thì không thể xác nhận có nghĩa

            lock (LockFor(dbPath))
            {
                var prev = Lookup(dbPath, requestId);
                if (prev is not null) return FromStored(prev, requestId);
                var res = Dispatch(dbPath, room, type, r, requestId, now);
                // Lỗi tạm thời không ghi nhớ để lần gửi lại còn được xử lý; kết quả dứt khoát thì ghi nhớ.
                if (res.Outcome != "error") Remember(dbPath, requestId, res.Code, res.Ack, now);
                return res;
            }
        }
    }

    private static PhoneActionResult Dispatch(string db, string room, string type, JsonElement r, string requestId, DateTime now)
    {
        try
        {
            return type switch
            {
                "order_note" => Note(db, room, r, requestId, now),
                "order_edit" => Edit(db, room, r, requestId, now, delete: false),
                "order_delete" => Edit(db, room, r, requestId, now, delete: true),
                "cancel_receive" => CancelReceive(db, room, r, requestId, now),
                "new_order_scan" when r.TryGetProperty("cancelled", out var cx) && cx.ValueKind == JsonValueKind.True =>
                    Unsupported(room, r, requestId, "scan", "v2 không có chức năng tạo đơn hủy từ điện thoại (đã bỏ theo quyết định của chủ dự án). Đơn hủy được nạp từ file/Google Sheet/Salework."),
                "new_order_scan" => NewOrder(db, room, r, requestId, now),
                _ => Unsupported(room, r, requestId, "push", "PC v2 chưa có Hộp Đơn Mới nên chưa đẩy được đơn từ điện thoại."),
            };
        }
        catch (Exception ex)
        {
            return Make(room, requestId, Str(r, "code"), "error", "Lưu thất bại, KHÔNG xác nhận: " + ex.Message, StageFor(type), changed: false);
        }
    }

    // ---------------- tìm kiện ----------------
    /// <summary>Kiện ở bảng chính/kho lưu trữ/đơn hủy (không gồm đã xóa) có mã trùng một mã RIÊNG của kiện. Không dùng mã giao đi dùng chung.</summary>
    public static List<Order> Find(string db, string code, string orderId)
    {
        var all = OrderRepository.Load(db).Where(s => !s.IsDeleted && !s.IsArchived).Select(s => s.Order).ToList();
        var c = TrackingResolver.Normalize(code);
        var hits = c.Length == 0 ? new List<Order>() : all.Where(o => OwnCodes(o).Contains(c)).ToList();
        if (hits.Count == 0 && TrackingResolver.Normalize(orderId) is { Length: > 0 } oid)
            hits = all.Where(o => TrackingResolver.Normalize(o.OrderId) == oid).ToList();
        return hits;
    }

    private static HashSet<string> OwnCodes(Order o)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in new[] { o.Track, o.ReturnTrack, o.AltTrack }.Concat(o.TrackingCodes).Concat(o.ScanAliases))
            if (TrackingResolver.Normalize(v ?? "") is { Length: > 0 } n) set.Add(n);
        if (set.Count == 0 && TrackingResolver.Normalize(o.OrderId ?? "") is { Length: > 0 } oid) set.Add(oid);
        return set;
    }

    // ---------------- order_note ----------------
    private static PhoneActionResult Note(string db, string room, JsonElement r, string requestId, DateTime now)
    {
        var code = Str(r, "code");
        var hits = Find(db, code, Str(r, "orderId"));
        var watch = !(r.TryGetProperty("watch", out var w) && w.ValueKind == JsonValueKind.False);
        var text = Clip(Str(r, "note").Trim(), 300);
        if (hits.Count == 0) return Make(room, requestId, code, "not_found", "Không thấy đơn " + code + " ở bảng chính để ghi chú", "note", false);
        if (hits.Count > 1) return Make(room, requestId, code, "ambiguous", $"Mã {code} trùng {hits.Count} đơn, hãy xử lý trên máy tính", "note", false);
        var o = hits[0];
        var a = OrderEditor.SetField(db, o.Id, EditField.IsWatchlist, watch ? "1" : "0", now);
        if (!a.Ok) return Fail(room, requestId, code, a, "note");
        var b = OrderEditor.SetField(db, o.Id, EditField.WatchlistReason, watch ? text : "", now);
        if (!b.Ok) return Fail(room, requestId, code, b, "note");
        return Make(room, requestId, code, "success",
            watch ? "PC đã lưu ghi chú và đưa đơn " + code + " vào bảng theo dõi" : "PC đã bỏ theo dõi đơn " + code, "note", true);
    }

    // ---------------- order_edit / order_delete ----------------
    private static PhoneActionResult Edit(string db, string room, JsonElement r, string requestId, DateTime now, bool delete)
    {
        var stage = delete ? "delete" : "edit";
        var code = Str(r, "code");
        var hits = Find(db, code, Str(r, "orderId"));
        if (hits.Count == 0) return Make(room, requestId, code, "not_found", "Không thấy đơn " + code + " ở bảng chính (có thể đã bị xóa)", stage, false);
        if (hits.Count > 1) return Make(room, requestId, code, "ambiguous", $"Mã {code} trùng {hits.Count} đơn, hãy xử lý trên máy tính", stage, false);
        var o = hits[0];

        if (delete)
        {
            var from = o.Platform == "Shopee" ? (o.OrderType == PhoneSnapshot.CancelType ? "Shopee - Đơn hủy" : "Shopee - Hoàn hàng") : "TikTok - Hoàn hàng";
            var d = OrderEditor.Delete(db, o.Id, from, now);
            return d.Ok ? Make(room, requestId, code, "success", "PC đã xóa đơn " + code + " (khôi phục được ở màn Đã xóa)", stage, true) : Fail(room, requestId, code, d, stage);
        }

        if (!r.TryGetProperty("fields", out var f) || f.ValueKind != JsonValueKind.Object)
            return Make(room, requestId, code, "invalid", "Không có trường nào để sửa", stage, false);
        // Chỉ nhận đổi trạng thái; sản phẩm/màu/size/số lượng/loại đơn chưa sửa được từ điện thoại ở v2.
        // Từ chối CẢ gói nếu có trường chưa hỗ trợ (không lưu một nửa).
        string? newStatus = null;
        foreach (var p in f.EnumerateObject())
        {
            if (p.Name == "st") { newStatus = MapStatus(p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : ""); if (newStatus is null) return Make(room, requestId, code, "invalid", "Trạng thái không hợp lệ ở v2", stage, false); }
            else return Make(room, requestId, code, "invalid", "PC v2 chưa cho sửa sản phẩm/loại đơn từ điện thoại, hãy sửa trên máy tính", stage, false);
        }
        if (newStatus is null) return Make(room, requestId, code, "invalid", "Không có trường nào để sửa", stage, false);
        var e = OrderEditor.SetField(db, o.Id, EditField.Status, newStatus, now);
        if (!e.Ok) return Fail(room, requestId, code, e, stage);
        return Make(room, requestId, code, "success", "PC đã cập nhật đơn " + code, stage, e.Kind == EditKind.Saved);
    }

    /// <summary>Trạng thái app iPhone dùng -> trạng thái v2. "Hư hỏng" không có ở v2 => null (từ chối).</summary>
    public static string? MapStatus(string phone) => phone switch
    {
        "Hàng đã trả" => ReceiveStatus.Received,
        "Chưa nhận" or "Chưa trả" => ReceiveStatus.NotReturned,
        "Chưa về" => ReceiveStatus.NotYetArrived,   // giá trị có thật của đơn TikTok; điện thoại gửi lại đúng giá trị đang hiển thị
        "Không cần nhận" => ReceiveStatus.NotNeeded,
        "Bị mất" => ReceiveStatus.Lost,
        _ => null,
    };

    // ---------------- cancel_receive ----------------
    private static PhoneActionResult CancelReceive(string db, string room, JsonElement r, string requestId, DateTime now)
    {
        var ids = new HashSet<string>();
        if (r.TryGetProperty("ids", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var v in arr.EnumerateArray()) if (v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s) ids.Add(s);
        var want = !(r.TryGetProperty("received", out var rv) && rv.ValueKind == JsonValueKind.False);
        var target = want ? ReceiveStatus.Received : ReceiveStatus.NotReturned;
        var list = OrderRepository.Load(db)
            .Where(s => !s.IsDeleted && !s.IsArchived && ids.Contains(s.Order.Id) && s.Order.OrderType == PhoneSnapshot.CancelType)
            .Select(s => s.Order).ToList();
        var todo = list.Where(o => PhoneSnapshot.IsReceived(o) != want).ToList();
        if (todo.Count == 0) return Make(room, requestId, "", "duplicate", "Các đơn hủy đã chọn đã ở trạng thái này rồi", "edit", false);
        var done = 0;
        string? firstError = null;
        foreach (var o in todo)
        {
            var e = OrderEditor.SetField(db, o.Id, EditField.Status, target, now);
            if (e.Ok) done++; else firstError ??= e.Message;
        }
        if (done == 0) return Make(room, requestId, "", "error", "Máy tính chưa lưu được: " + firstError, "edit", false);
        var msg = $"PC đã đánh dấu {(want ? "đã nhận" : "chưa nhận")} {done} đơn hủy" + (done < todo.Count ? $" ({todo.Count - done} đơn lỗi: {firstError})" : "");
        return Make(room, requestId, "", done == todo.Count ? "success" : "error", msg, "edit", true);
    }

    // ---------------- tạo đơn mới từ điện thoại (đi THẲNG vào bảng chính; quyết định chủ dự án 23/11/2026) ----------------
    /// <summary>Nhận diện sàn theo mã (port autoDetectPlatformFromCode, bỏ bước tra DB vì đã kiểm tra trùng trước đó).</summary>
    public static string DetectPlatform(string rawCode)
    {
        var code = (rawCode ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0) return "TikTok";
        if (code.StartsWith("SPX") || code.StartsWith("SHOPEE") || System.Text.RegularExpressions.Regex.IsMatch(code, @"^VN2[4-9][0-9A-Z]{8,14}$")) return "Shopee";
        if (System.Text.RegularExpressions.Regex.IsMatch(code, @"^8[0245]\d{10,12}$")) return "TikTok";
        if (System.Text.RegularExpressions.Regex.IsMatch(code, @"^5\d{17}$") || System.Text.RegularExpressions.Regex.IsMatch(code, @"^4\d{18}$")) return "TikTok";
        if (System.Text.RegularExpressions.Regex.IsMatch(code, @"^2[3-9][0-1]\d[0-3]\d[A-Z0-9]{8,10}$")
            || (System.Text.RegularExpressions.Regex.IsMatch(code, @"^[A-Z0-9]{14,15}$") && !System.Text.RegularExpressions.Regex.IsMatch(code, @"^\d+$"))) return "Shopee";
        return "TikTok";
    }

    private static PhoneActionResult NewOrder(string db, string room, JsonElement r, string requestId, DateTime now)
    {
        var raw = Str(r, "code"); if (raw.Length == 0) raw = Str(r, "track");
        var code = TrackingResolver.Normalize(raw);
        var cap = Str(r, "captureId");
        if (code.Length == 0) return Make(room, requestId, raw, "invalid", "Mã vận đơn trống", "scan", false, cap);

        var orderId = Str(r, "orderId").Trim();
        var oidNorm = TrackingResolver.Normalize(orderId);
        // Đã có ở đâu đó (kể cả kho lưu trữ / đã xóa): không tạo lại, không "hồi sinh" đơn đã xóa.
        var hit = OrderRepository.Load(db).FirstOrDefault(s =>
            OwnCodes(s.Order).Contains(code) || (oidNorm.Length > 0 && TrackingResolver.Normalize(s.Order.OrderId) == oidNorm && s.Order.Track.Length == 0));
        if (hit is not null)
        {
            var where = hit.IsDeleted ? "mục Đã xóa" : hit.IsArchived ? "Kho lưu trữ" : "Bảng chính";
            return Make(room, requestId, code, "duplicate", $"ĐƠN ĐÃ CÓ Ở {where.ToUpperInvariant()}, KHÔNG TẠO LẠI", "scan", false, cap);
        }

        var platformIn = Str(r, "platform");
        var platform = platformIn.Length > 0 && platformIn != "auto" ? (platformIn.StartsWith("TikTok") ? "TikTok" : platformIn) : DetectPlatform(code);
        var carrierIn = Str(r, "carrier");
        var carrier = carrierIn.Length > 0 ? carrierIn : (Core.Import.LegacyText.DetectCarrierByTrackingCode(code) ?? (platform == "Shopee" ? "SPX Express" : "J&T Express"));
        var stamp = now.ToString("dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        var note = Str(r, "note").Trim();
        var o = new Order
        {
            OrderId = orderId.Length > 0 ? orderId : code, Track = code, Platform = platform, Carrier = carrier,
            OrderType = "Trả hàng/Hoàn tiền", PlatformStatus = "Đang chuyển hoàn", PreOrder = "Đơn thường",
            Status = ReceiveStatus.NotReturned, ShipDate = stamp, ReturnDate = stamp, ImportedAt = stamp,
            Note = note.Length > 0 ? Clip(note, 300) : "Đơn mới tạo từ máy quét điện thoại",
            Items = { new OrderItem { Product = "Sản phẩm mới (Quét điện thoại)", Qty = 1 } },
        };

        using (var conn = new SqliteConnection($"Data Source={db};Pooling=False"))
        {
            conn.Open();
            using var tx = conn.BeginTransaction();
            OrderStore.Insert(conn, tx, o, "phone|" + o.Id);
            OrderStore.AppendAudit(conn, tx, "phone_new_order", now, $"Điện thoại quét tạo đơn mới (MVĐ: {code}, sàn: {platform}) vào bảng chính");
            tx.Commit();
        }
        return Make(room, requestId, code, "success", $"ĐÃ TẠO ĐƠN MỚI Ở BẢNG CHÍNH ({platform})", "scan", true, cap) with { Kind = "new_order" };
    }

    // ---------------- chưa hỗ trợ ----------------
    private static PhoneActionResult Unsupported(string room, JsonElement r, string requestId, string stage, string message) =>
        Make(room, requestId, Str(r, "code"), "error", message, stage, false, captureId: Str(r, "captureId"));

    // ---------------- tiện ích ----------------
    private static string StageFor(string type) => type switch
    {
        "order_note" => "note", "order_delete" => "delete", "new_order_scan" => "scan", "inbox_push" => "push", _ => "edit",
    };

    private static PhoneActionResult Fail(string room, string requestId, string code, EditResult res, string stage)
    {
        var outcome = res.Kind switch
        {
            EditKind.NotFound => "not_found",
            EditKind.NotEditable => "locked",
            EditKind.Invalid => "invalid",
            EditKind.Conflict => "error",
            _ => "error",
        };
        return Make(room, requestId, code, outcome, res.Message, stage, false);
    }

    private static PhoneActionResult Make(string room, string requestId, string code, string outcome, string message, string stage, bool changed, string captureId = "")
    {
        var ack = PhoneSnapshot.ToJson(new Dictionary<string, object?>
        {
            ["type"] = "pc_ack", ["room"] = room, ["requestId"] = requestId, ["code"] = code, ["outcome"] = outcome,
            ["extra"] = message, ["ackStage"] = stage, ["captureId"] = captureId.Length > 0 ? captureId : null,
        });
        return new PhoneActionResult(ack, changed, requestId, code, outcome, message, stage);
    }

    private static PhoneActionResult FromStored(string ack, string requestId)
    {
        using var d = JsonDocument.Parse(ack);
        var r = d.RootElement;
        return new PhoneActionResult(ack, false, requestId, Str(r, "code"), Str(r, "outcome"), Str(r, "extra"), Str(r, "ackStage"));
    }

    private static string Clip(string s, int n) => s.Length <= n ? s : s[..n];
    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string? Lookup(string db, string requestId)
    {
        using var c = new SqliteConnection($"Data Source={db};Pooling=False");
        c.Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT ack_json FROM sync_inbox WHERE request_id=$r";
        cmd.Parameters.AddWithValue("$r", requestId);
        return cmd.ExecuteScalar() as string;
    }

    private static void Remember(string db, string requestId, string code, string ack, DateTime now)
    {
        using var c = new SqliteConnection($"Data Source={db};Pooling=False");
        c.Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO sync_inbox(request_id, code, ack_json, handled_at) VALUES($r,$c,$a,$t)";
        cmd.Parameters.AddWithValue("$r", requestId);
        cmd.Parameters.AddWithValue("$c", code);
        cmd.Parameters.AddWithValue("$a", ack);
        cmd.Parameters.AddWithValue("$t", now.ToString("o"));
        cmd.ExecuteNonQuery();
    }
}
