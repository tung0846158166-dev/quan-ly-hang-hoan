using System.Globalization;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Core.Tracking;

namespace QuanLyHangHoan.Data;

public enum ScanOutcome
{
    Received,          // vừa nhận kiện
    AlreadyReceived,   // kiện đã nhận trước đó (giữ nguyên giờ nhận)
    Ambiguous,         // nhiều kiện khớp: KHÔNG tự nhận, cần kiểm tra
    NotFound,
    NeedsManualCheck,  // trạng thái "Không cần nhận"/"Bị mất"...: không tự đổi
    Error,
}

/// <param name="Ack">True CHỈ khi mọi thứ cần lưu (trạng thái và/hoặc ảnh) đã COMMIT xuống SQLite (quy tắc 6).</param>
public sealed record ScanResult(ScanOutcome Outcome, bool Ack, string Message, Order? Order);

/// <summary>
/// Nhận kiện qua quét mã (điện thoại/máy quét). Dùng TrackingResolver: nhận kiện A không bao giờ nhận B/C; mã giao đi dùng chung
/// hoặc mã đơn gốc nhiều ứng viên => Ambiguous. Ghi trong MỘT giao dịch rồi mới trả ACK. Không xóa/ghi đè ảnh cũ.
/// </summary>
public static class ScanReceiver
{
    public static ScanResult Receive(string dbPath, string code, DateTime now, byte[]? photo = null, string photoMime = "image/jpeg")
    {
        if (!File.Exists(dbPath)) return new(ScanOutcome.NotFound, false, "Chưa có cơ sở dữ liệu.", null);

        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            conn.Open();
            using var tx = conn.BeginTransaction(); // đọc + ghi cùng giao dịch: kiểm tra lại trạng thái ngay trước khi ghi

            var candidates = Load(conn, tx);
            var d = ScanDecider.Decide(candidates, code);   // quyết định thuần, dùng chung với bản cloud

            if (d.Kind == ScanDecisionKind.NotFound) return new(ScanOutcome.NotFound, false, "Không tìm thấy kiện.", null);
            if (d.Kind == ScanDecisionKind.Ambiguous)
                return new(ScanOutcome.Ambiguous, false, $"{d.Candidates} kiện cùng khớp mã này, cần kiểm tra thủ công.", null);

            var o = d.Order!;
            var received = d.Kind == ScanDecisionKind.AlreadyReceived;

            if (d.Kind == ScanDecisionKind.NeedsManualCheck)
                return new(ScanOutcome.NeedsManualCheck, false, $"Trạng thái \"{o.Status}\": cần kiểm tra thủ công.", o);

            if (!received)
            {
                using var u = conn.CreateCommand();
                u.Transaction = tx;
                u.CommandText = "UPDATE orders SET status=$s, recv_date=$d, v2_modified=1 WHERE id=$id AND status=$old";
                u.Parameters.AddWithValue("$s", ReceiveStatus.Received);
                u.Parameters.AddWithValue("$d", now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture));
                u.Parameters.AddWithValue("$id", o.Id);
                u.Parameters.AddWithValue("$old", o.Status);
                if (u.ExecuteNonQuery() != 1) return new(ScanOutcome.Error, false, "Trạng thái vừa bị thay đổi, thử lại.", o);
                o.Status = ReceiveStatus.Received;
            }

            if (photo is { Length: > 0 })
            {
                var photoId = $"p_{TrackingResolver.Normalize(o.Track)}_{now:yyyyMMddHHmmss}";
                using var p = conn.CreateCommand();
                p.Transaction = tx;
                p.CommandText = "INSERT INTO photos(photo_id, source, mime, data, sha256, inline_differs) VALUES($id,'phone',$m,$d,$h,0)";
                p.Parameters.AddWithValue("$id", photoId);
                p.Parameters.AddWithValue("$m", photoMime);
                p.Parameters.AddWithValue("$d", photo);
                p.Parameters.AddWithValue("$h", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(photo)));
                p.ExecuteNonQuery();

                using var link = conn.CreateCommand();
                link.Transaction = tx;
                link.CommandText = "UPDATE orders SET photo_id=$p, v2_modified=1 WHERE id=$id";
                link.Parameters.AddWithValue("$p", photoId);
                link.Parameters.AddWithValue("$id", o.Id);
                link.ExecuteNonQuery();
                o.PhotoId = photoId;
            }

            tx.Commit(); // chỉ sau dòng này mới được ACK
            return received
                ? new(ScanOutcome.AlreadyReceived, photo is { Length: > 0 }, "Kiện đã nhận trước đó.", o)
                : new(ScanOutcome.Received, true, "Đã nhận kiện.", o);
        }
        catch (Exception ex)
        {
            return new(ScanOutcome.Error, false, "Lưu thất bại, KHÔNG ACK: " + ex.Message, null);
        }
    }

    public enum PhotoOutcome { Saved, NotFound, Ambiguous, Error }
    public sealed record PhotoResult(PhotoOutcome Outcome, bool Ack, string Message);

    /// <summary>
    /// Gắn ảnh vào kiện khớp mã. KHÔNG đổi trạng thái nhận kiện. Ảnh cũ giữ nguyên. Chỉ Ack khi ảnh đã commit.
    /// <paramref name="photoId"/> phải duy nhất cho mỗi ảnh (dùng captureId) để hai ảnh cùng giây không đè nhau.
    /// </summary>
    public static PhotoResult AttachPhoto(string dbPath, string code, byte[] photo, string mime, string photoId)
    {
        if (!File.Exists(dbPath)) return new(PhotoOutcome.NotFound, false, "Chưa có cơ sở dữ liệu.");
        if (photo.Length == 0) return new(PhotoOutcome.Error, false, "Ảnh rỗng.");
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            conn.Open();
            using var tx = conn.BeginTransaction();

            var m = TrackingResolver.Resolve(Load(conn, tx), code);
            if (m.Kind == MatchKind.NotFound) return new(PhotoOutcome.NotFound, false, "Không tìm thấy kiện để gắn ảnh.");
            if (m.Kind == MatchKind.Ambiguous) return new(PhotoOutcome.Ambiguous, false, $"{m.Candidates.Count} kiện cùng khớp mã này, không gắn ảnh.");

            using var p = conn.CreateCommand();
            p.Transaction = tx;
            p.CommandText = "INSERT OR REPLACE INTO photos(photo_id, source, mime, data, sha256, inline_differs) VALUES($id,'phone',$m,$d,$h,0)";
            p.Parameters.AddWithValue("$id", photoId);
            p.Parameters.AddWithValue("$m", mime);
            p.Parameters.AddWithValue("$d", photo);
            p.Parameters.AddWithValue("$h", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(photo)));
            p.ExecuteNonQuery();

            using var link = conn.CreateCommand();
            link.Transaction = tx;
            link.CommandText = "UPDATE orders SET photo_id=$p, v2_modified=1 WHERE id=$id";
            link.Parameters.AddWithValue("$p", photoId);
            link.Parameters.AddWithValue("$id", m.Single!.Id);
            link.ExecuteNonQuery();

            tx.Commit();
            return new(PhotoOutcome.Saved, true, "Đã lưu ảnh.");
        }
        catch (Exception ex) { return new(PhotoOutcome.Error, false, "Lưu ảnh thất bại, KHÔNG ACK: " + ex.Message); }
    }

    /// <summary>Chỉ đơn đang ở bảng chính (không lưu trữ, không đã xóa).</summary>
    private static List<Order> Load(SqliteConnection conn, SqliteTransaction tx)
    {
        var list = new List<Order>();
        using var c = conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = """
            SELECT id, order_id, return_order_id, track, return_track, alt_track, forward_track, tracking_codes, scan_aliases, status, photo_id
            FROM orders WHERE is_archived=0 AND is_deleted=0
            """;
        using var r = c.ExecuteReader();
        while (r.Read())
            list.Add(new Order
            {
                Id = r.GetString(0), OrderId = r.GetString(1), ReturnOrderId = r.GetString(2), Track = r.GetString(3),
                ReturnTrack = r.GetString(4), AltTrack = r.GetString(5), ForwardTrack = r.GetString(6),
                TrackingCodes = Strings(r.GetString(7)), ScanAliases = Strings(r.GetString(8)),
                Status = r.GetString(9), PhotoId = r.GetString(10),
            });
        return list;
    }

    private static List<string> Strings(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch (System.Text.Json.JsonException) { return new(); }
    }
}
