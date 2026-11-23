using System.Collections.Concurrent;
using System.Text;
using Microsoft.Data.Sqlite;

namespace QuanLyHangHoan.Data.Sync;

/// <summary>
/// Xử lý MỘT bản tin từ điện thoại. Trả JSON xác nhận (pc_ack) hoặc null nếu không có gì để xác nhận
/// (bản tin không dành cho app này, khác phòng, hoặc mới nhận một phần ảnh).
/// - Khác phòng hoặc bản tin lạ: bỏ qua (null).
/// - Chống xử lý lặp (bảng sync_inbox): gửi lại cùng requestId (quét) / captureId (ảnh) trả đúng xác nhận cũ.
/// - Quy tắc 6: xác nhận thành công chỉ sinh SAU KHI đã commit; lỗi lưu => "error", không bao giờ "success".
/// - Ảnh KHÔNG đổi trạng thái nhận kiện; ảnh cũ không bị ghi đè; ảnh phải đúng loại/kích thước.
/// </summary>
public static class SyncReceiver
{
    public static string? Handle(string dbPath, string room, string messageJson, DateTime now)
    {
        if (SyncProtocol.TryParseScan(messageJson, out var scan))
            return string.Equals(scan.Room, room, StringComparison.Ordinal) ? HandleScan(dbPath, scan, now) : null;

        if (SyncProtocol.TryParsePhoto(messageJson, out var photo))
            return string.Equals(photo.Room, room, StringComparison.Ordinal) ? HandlePhoto(dbPath, photo, now) : null;

        if (SyncProtocol.TryParseChunk(messageJson, out var chunk))
            return string.Equals(chunk.Room, room, StringComparison.Ordinal) ? HandleChunk(dbPath, chunk, now) : null;

        return null;
    }

    // Cùng một bản tin thường đến 2-4 lần gần như ĐỒNG THỜI (3 broker MQTT + ntfy). Phải xử lý tuần tự theo từng cơ sở dữ liệu
    // để "kiểm tra đã xử lý chưa -> lưu -> ghi nhớ" là một khối: bản sao đến sau thấy kết quả đã ghi nhớ và trả đúng xác nhận đó.
    private static readonly ConcurrentDictionary<string, object> DbLocks = new(StringComparer.OrdinalIgnoreCase);
    private static object LockFor(string dbPath) => DbLocks.GetOrAdd(Path.GetFullPath(dbPath), _ => new object());

    // ---------------- quét mã ----------------
    private static string HandleScan(string dbPath, ScanRequest req, DateTime now)
    {
        lock (LockFor(dbPath)) return HandleScanLocked(dbPath, req, now);
    }

    private static string HandleScanLocked(string dbPath, ScanRequest req, DateTime now)
    {
        try
        {
            var previous = Lookup(dbPath, req.RequestId);
            if (previous is not null) return previous;

            var result = ScanReceiver.Receive(dbPath, req.Code, now);
            var ack = SyncProtocol.BuildAck(req, result);

            // Lỗi tạm thời (Error) không ghi nhớ để lần gửi lại còn được xử lý; kết quả dứt khoát thì ghi nhớ.
            if (result.Outcome != ScanOutcome.Error) Remember(dbPath, req.RequestId, req.Code, ack, now);
            return ack;
        }
        catch (Exception ex)
        {
            return SyncProtocol.BuildAck(req, new ScanResult(ScanOutcome.Error, false, "Lưu thất bại, KHÔNG xác nhận: " + ex.Message, null));
        }
    }

    // ---------------- ảnh trọn gói ----------------
    private static string HandlePhoto(string dbPath, SyncProtocol.PhotoRequest req, DateTime now)
    {
        lock (LockFor(dbPath)) return HandlePhotoLocked(dbPath, req, now);
    }

    private static string HandlePhotoLocked(string dbPath, SyncProtocol.PhotoRequest req, DateTime now)
    {
        try
        {
            var previous = Lookup(dbPath, req.IdempotencyKey);
            if (previous is not null) return previous;

            string Ack(string outcome, string msg) =>
                SyncProtocol.BuildPhotoAck(req.Room, req.RequestId, req.CaptureId, req.Code, outcome, msg);

            if (!SyncProtocol.TryDecodeImage(req.DataUri, out var mime, out var bytes, out var error))
                return Ack("error", "Ảnh bị từ chối: " + error); // không ghi nhớ: không phải kết quả dứt khoát về kiện

            var photoId = "p_" + SafeId(req.CaptureId.Length > 0 ? req.CaptureId : req.RequestId);
            var r = ScanReceiver.AttachPhoto(dbPath, req.Code, bytes, mime, photoId);
            var outcome = r.Outcome switch
            {
                ScanReceiver.PhotoOutcome.Saved => "success",
                ScanReceiver.PhotoOutcome.NotFound => "not_found",
                ScanReceiver.PhotoOutcome.Ambiguous => "ambiguous",
                _ => "error",
            };
            var ack = Ack(outcome, r.Message);
            if (r.Outcome != ScanReceiver.PhotoOutcome.Error) Remember(dbPath, req.IdempotencyKey, req.Code, ack, now);
            return ack;
        }
        catch (Exception ex)
        {
            return SyncProtocol.BuildPhotoAck(req.Room, req.RequestId, req.CaptureId, req.Code, "error", "Lưu ảnh thất bại, KHÔNG xác nhận: " + ex.Message);
        }
    }

    // ---------------- ảnh theo mảnh (ntfy) ----------------
    private sealed class Assembly
    {
        public required SyncProtocol.ChunkRequest First { get; init; }
        public SortedDictionary<int, string> Parts { get; } = new();
        public DateTime Created { get; init; }
    }

    private static readonly ConcurrentDictionary<string, Assembly> Pending = new();
    private static readonly TimeSpan AssemblyTtl = TimeSpan.FromMinutes(10);
    private const int MaxPendingAssemblies = 64;

    private static string? HandleChunk(string dbPath, SyncProtocol.ChunkRequest c, DateTime now)
    {
        try
        {
            var key = (c.CaptureId.Length > 0 ? c.CaptureId : c.RequestId);
            var previous = Lookup(dbPath, "photo:" + key);
            if (previous is not null) return previous; // ảnh này đã xử lý xong: trả lại xác nhận cũ

            foreach (var stale in Pending.Where(kv => now - kv.Value.Created > AssemblyTtl).Select(kv => kv.Key).ToList())
                Pending.TryRemove(stale, out _);

            var id = $"{dbPath}|{c.Room}|{c.PhotoId}";
            if (!Pending.ContainsKey(id) && Pending.Count >= MaxPendingAssemblies) return null; // chống phình bộ nhớ

            var asm = Pending.GetOrAdd(id, _ => new Assembly { First = c, Created = now });
            lock (asm)
            {
                if (asm.First.Total != c.Total || asm.First.Code != c.Code) return null; // mảnh không nhất quán: bỏ
                asm.Parts.TryAdd(c.Index, c.Chunk);                                          // mảnh lặp: giữ bản đầu
                if (asm.Parts.Count < c.Total) return null;                                   // chưa đủ: KHÔNG ack
            }
            Pending.TryRemove(id, out _);

            var sb = new StringBuilder();
            foreach (var part in asm.Parts.Values) sb.Append(part);
            return HandlePhoto(dbPath,
                new SyncProtocol.PhotoRequest(c.Room, c.RequestId, c.CaptureId, c.Code, sb.ToString()), now);
        }
        catch (Exception) { return null; }
    }

    private static string SafeId(string s)
    {
        var chars = s.Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-').Take(64).ToArray();
        return chars.Length > 0 ? new string(chars) : Guid.NewGuid().ToString("N");
    }

    // ---------------- chống lặp ----------------
    private static string? Lookup(string dbPath, string key)
    {
        if (!File.Exists(dbPath)) return null;
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var c = conn.CreateCommand();
        c.CommandText = "SELECT ack_json FROM sync_inbox WHERE request_id=$r";
        c.Parameters.AddWithValue("$r", key);
        return c.ExecuteScalar() as string;
    }

    private static void Remember(string dbPath, string key, string code, string ackJson, DateTime now)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using var c = conn.CreateCommand();
        c.CommandText = "INSERT OR IGNORE INTO sync_inbox(request_id, code, ack_json, handled_at) VALUES($r,$c,$a,$t)";
        c.Parameters.AddWithValue("$r", key);
        c.Parameters.AddWithValue("$c", code);
        c.Parameters.AddWithValue("$a", ackJson);
        c.Parameters.AddWithValue("$t", now.ToString("dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
        c.ExecuteNonQuery();
    }
}
