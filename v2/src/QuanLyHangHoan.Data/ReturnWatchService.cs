using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Core.Tracking;
using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.Data;

/// <summary>Một đơn trong Bảng Hàng Hoàn Về: `Since` = giờ vào bảng (ms epoch), `Stage` 1 Đã đồng ý, 2 Đang về, 3 Đã về shop, 4 Đã nhận kho.</summary>
public sealed record ReturnWatchEntry(StoredOrder Stored, long Since, int Stage);

/// <summary>
/// BẢNG HÀNG HOÀN VỀ (port v1.9.72 của app cũ). Đơn chưa nhận mà trạng thái sàn là "Đã đồng ý" (khách yêu cầu hoàn được chấp thuận)
/// TỰ vào bảng (ghi giờ vào), theo dõi 4 chặng: Đã đồng ý -> Đang về -> Đã về shop -> Đã nhận kho (quét). Vào bảng rồi thì ở lại dù
/// trạng thái sàn đổi tiếp; người dùng bỏ khỏi bảng bằng <see cref="Remove"/> (không xóa đơn, khôi phục được bằng <see cref="Restore"/>).
/// Lưu ở bảng riêng `return_watch` (khóa theo Order.Id), KHÔNG sửa bảng orders nên không ảnh hưởng nhập lại dữ liệu cũ hay nạp file sàn.
/// Bảng chỉ gồm đơn ở BẢNG CHÍNH (đã dời lưu trữ/đã xóa thì rời bảng), không gồm "Không cần nhận"/"Bị mất". Kiện A/B/C là các dòng riêng.
/// </summary>
public static class ReturnWatchService
{
    public static readonly string[] StageNames = { "", "Đã đồng ý", "Đang về", "Đã về shop", "Đã nhận kho" };

    private static readonly Regex Agreed = new(@"(đồng|dong)\s*(ý|y)|agreed", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex OnTheWay = new(@"dang ve|dang van chuyen|van chuyen ve|dang giao", RegexOptions.CultureInvariant);

    public const string TableSql = """
        CREATE TABLE IF NOT EXISTS return_watch (
            order_id TEXT PRIMARY KEY, since INTEGER NOT NULL, off INTEGER NOT NULL DEFAULT 0
        );
        """;

    private static bool IsAgreed(string? text) => Agreed.IsMatch((text ?? "").Normalize(NormalizationForm.FormC));

    private static string Fold(string s)
    {
        var d = (s ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in d) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch == 'đ' ? 'd' : ch == 'Đ' ? 'D' : ch);
        return sb.ToString().ToLowerInvariant();
    }

    /// <summary>Chặng hiện tại (port getReturnWatchStage): đã nhận 4; có giờ hàng về 3; đang về/có mã vận đơn hoàn 2; còn lại 1.</summary>
    public static int StageOf(Order o)
    {
        if (PhoneSnapshot.IsReceived(o)) return 4;
        if (PhoneSnapshot.ArrivedAt(o) is not null) return 3;
        if (OnTheWay.IsMatch(Fold(o.PlatformStatus ?? "")) || TrackingResolver.Normalize(o.ReturnTrack ?? "").Length > 0) return 2;
        return 1;
    }

    // v1.9.76 của app cũ: TikTok không có trạng thái "Đã đồng ý"; đơn TikTok đang chờ hàng về (khách chưa gửi / đang về /
    // đang vận chuyển về) cũng vào bảng.
    private static readonly Regex TikTokEntry = new(@"cho khach gui hang|dang ve\b|dang van chuyen ve", RegexOptions.CultureInvariant);

    private static bool EntryStatus(Order o)
    {
        var ps = o.PlatformStatus ?? "";
        if (ps.Length == 0) return false;
        if (IsAgreed(ps)) return true;
        return (o.Platform == "TikTok" || o.Platform == "TikTok Shop") && TikTokEntry.IsMatch(Fold(ps));
    }

    private static bool Eligible(Order o) =>
        EntryStatus(o) && !PhoneSnapshot.IsReceived(o) && o.Status != ReceiveStatus.NotNeeded && o.Status != ReceiveStatus.Lost;

    private static bool Excluded(Order o) => o.Status == ReceiveStatus.NotNeeded || o.Status == ReceiveStatus.Lost;

    /// <summary>
    /// Cập nhật tư cách thành viên: (1) lần đầu, lấy rwSince/rwOff của app cũ từ bản gốc đã nhập (raw_json) nếu có; (2) tự thêm đơn chưa có dấu
    /// mà đủ điều kiện. Không bao giờ đổi dòng đã có (kể cả đã bị bỏ). Trả số đơn mới vào bảng. Idempotent.
    /// </summary>
    public static int Sync(string dbPath, DateTime now)
    {
        if (!File.Exists(dbPath)) return 0;
        var nowMs = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Unspecified), TimeZoneInfo.Local.GetUtcOffset(now)).ToUnixTimeMilliseconds();
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using var tx = conn.BeginTransaction();
        Exec(conn, tx, TableSql);

        var have = new HashSet<string>();
        using (var c = conn.CreateCommand()) { c.Transaction = tx; c.CommandText = "SELECT order_id FROM return_watch"; using var r = c.ExecuteReader(); while (r.Read()) have.Add(r.GetString(0)); }

        var added = 0;
        var main = OrderRepository.Load(dbPath).Where(s => !s.IsArchived && !s.IsDeleted).ToList();

        // (1) Lấy dấu của app cũ.
        using (var c = conn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = "SELECT id, raw_json FROM orders WHERE is_archived=0 AND is_deleted=0 AND raw_json LIKE '%rwSince%'";
            using var r = c.ExecuteReader();
            var seeds = new List<(string Id, long Since, bool Off)>();
            while (r.Read())
            {
                var id = r.GetString(0);
                if (have.Contains(id)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(r.GetString(1));
                    var root = doc.RootElement;
                    var since = root.TryGetProperty("rwSince", out var s) && s.TryGetInt64(out var v) ? v : 0;
                    var off = root.TryGetProperty("rwOff", out var o) && o.ValueKind == JsonValueKind.True;
                    if (since > 0 || off) seeds.Add((id, since > 0 ? since : nowMs, off));
                }
                catch (JsonException) { /* bản gốc hỏng: bỏ qua, không đoán */ }
            }
            r.Close();
            foreach (var (id, since, off) in seeds) { Exec(conn, tx, "INSERT OR IGNORE INTO return_watch(order_id, since, off) VALUES($i,$s,$o)", ("$i", id), ("$s", since), ("$o", off ? 1 : 0)); have.Add(id); }
        }

        // (2) Tự thêm đơn "Đã đồng ý".
        foreach (var s in main)
        {
            if (have.Contains(s.Order.Id) || !Eligible(s.Order)) continue;
            Exec(conn, tx, "INSERT OR IGNORE INTO return_watch(order_id, since, off) VALUES($i,$s,0)", ("$i", s.Order.Id), ("$s", nowMs));
            have.Add(s.Order.Id); added++;
        }
        tx.Commit();
        return added;
    }

    /// <summary>Các đơn đang ở bảng (đã Sync), chặng thấp trước rồi giờ vào sớm trước.</summary>
    public static IReadOnlyList<ReturnWatchEntry> Load(string dbPath, DateTime now)
    {
        Sync(dbPath, now);
        var marks = Marks(dbPath);
        return OrderRepository.Load(dbPath)
            .Where(s => !s.IsArchived && !s.IsDeleted && !Excluded(s.Order) && marks.TryGetValue(s.Order.Id, out var m) && !m.Off)
            .Select(s => new ReturnWatchEntry(s, marks[s.Order.Id].Since, StageOf(s.Order)))
            .OrderBy(e => e.Stage).ThenBy(e => e.Since).ToList();
    }

    /// <summary>Tư cách thành viên đang hiệu lực: id đơn -> (giờ vào, chặng). Dùng cho danh sách gửi điện thoại. Không tự Sync.</summary>
    public static Dictionary<string, (long Since, int Stage)> ActiveMarks(string dbPath, IEnumerable<StoredOrder> all)
    {
        var marks = Marks(dbPath);
        var result = new Dictionary<string, (long, int)>();
        foreach (var s in all)
            if (!s.IsArchived && !s.IsDeleted && !Excluded(s.Order) && marks.TryGetValue(s.Order.Id, out var m) && !m.Off)
                result[s.Order.Id] = (m.Since, StageOf(s.Order));
        return result;
    }

    private static Dictionary<string, (long Since, bool Off)> Marks(string dbPath)
    {
        var d = new Dictionary<string, (long, bool)>();
        if (!File.Exists(dbPath)) return d;
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var t = conn.CreateCommand();
        t.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='return_watch'";
        if (Convert.ToInt32(t.ExecuteScalar()) == 0) return d;
        using var c = conn.CreateCommand();
        c.CommandText = "SELECT order_id, since, off FROM return_watch";
        using var r = c.ExecuteReader();
        while (r.Read()) d[r.GetString(0)] = (r.GetInt64(1), r.GetInt32(2) != 0);
        return d;
    }

    /// <summary>Bỏ đơn khỏi bảng (không xóa đơn). Ghi nhật ký.</summary>
    public static EditResult Remove(string dbPath, string orderId, DateTime now) => SetOff(dbPath, orderId, true, now);

    /// <summary>Đưa lại đơn đã bỏ vào bảng (hoàn tác).</summary>
    public static EditResult Restore(string dbPath, string orderId, DateTime now) => SetOff(dbPath, orderId, false, now);

    private static EditResult SetOff(string dbPath, string orderId, bool off, DateTime now)
    {
        try
        {
            Sync(dbPath, now);
            using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            conn.Open();
            using var tx = conn.BeginTransaction();
            var cur = OrderStore.Scalar(conn, tx, "SELECT off FROM return_watch WHERE order_id=$i", ("$i", orderId));
            if (cur is null) return new(EditKind.NotFound, "Đơn không có trong Bảng Hàng Hoàn Về.");
            if ((Convert.ToInt32(cur) != 0) == off) return new(EditKind.NoChange, "Không có thay đổi.");
            OrderStore.Exec(conn, tx, "UPDATE return_watch SET off=$o WHERE order_id=$i", ("$o", off ? 1 : 0), ("$i", orderId));
            var label = OrderStore.Scalar(conn, tx, "SELECT COALESCE(NULLIF(track,''), NULLIF(return_track,''), order_id) FROM orders WHERE id=$i", ("$i", orderId)) as string ?? orderId;
            OrderStore.AppendAudit(conn, tx, "return_watch", now, (off ? "Bỏ khỏi Bảng Hàng Hoàn Về: " : "Đưa lại vào Bảng Hàng Hoàn Về: ") + label);
            tx.Commit();
            return new(EditKind.Saved, off ? "Đã bỏ khỏi bảng." : "Đã đưa lại vào bảng.");
        }
        catch (Exception ex) { return new(EditKind.Error, "Lỗi: " + ex.Message); }
    }

    private static void Exec(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] ps)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }
}
