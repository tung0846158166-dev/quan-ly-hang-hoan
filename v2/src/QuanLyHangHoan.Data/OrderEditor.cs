using System.Globalization;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Data;

public enum EditField { Status, Note, UserNote, BuyerNote, Carrier, Deadline, IsWatchlist, WatchlistReason, ComplaintStatus, ComplaintNote }

public enum EditKind { Saved, NoChange, Invalid, Conflict, NotEditable, NotFound, Error }

public sealed record EditResult(EditKind Kind, string Message)
{
    /// <summary>Thành công hoặc không có gì để đổi (cả hai đều không phải lỗi).</summary>
    public bool Ok => Kind is EditKind.Saved or EditKind.NoChange;
}

public sealed record HistoryEntry(string Time, string Field, string Label, string OldValue, string NewValue, string EditType);

/// <summary>
/// Thao tác thủ công trên đơn (sửa ô, xóa mềm, khôi phục, dời/trả lưu trữ). Quy tắc:
///  - Mọi thay đổi ghi vào cell_edit_history (liên kết đơn theo id) và audit_log TRONG CÙNG giao dịch với thay đổi.
///  - Không xóa dòng, không ghi đè dữ liệu cũ: xóa chỉ đặt cờ; giờ nhận đầu tiên được giữ; lịch sử luôn giữ giá trị cũ.
///  - Đơn đã xóa không sửa được cho tới khi khôi phục. DB chưa có => NotFound, không tạo tệp.
///  - expectedOld (tùy chọn): nếu giá trị hiện tại khác thì báo Conflict, tránh ghi đè sửa đổi của nơi khác (quét/đồng bộ).
/// </summary>
public static class OrderEditor
{
    private static readonly Dictionary<EditField, (string Column, string Label)> Fields = new()
    {
        [EditField.Status] = ("status", "Nhận tại kho"),
        [EditField.Note] = ("note", "Lý do"),
        [EditField.UserNote] = ("user_note", "Ghi chú của tôi"),
        [EditField.BuyerNote] = ("buyer_note", "Ghi chú người mua"),
        [EditField.Carrier] = ("carrier", "Đơn vị vận chuyển"),
        [EditField.Deadline] = ("deadline", "Hạn xử lý"),
        [EditField.IsWatchlist] = ("is_watchlist", "Theo dõi"),
        [EditField.WatchlistReason] = ("watchlist_reason", "Lý do theo dõi"),
        [EditField.ComplaintStatus] = ("complaint_status", "Khiếu nại"),
        [EditField.ComplaintNote] = ("complaint_note", "Ghi chú khiếu nại"),
    };

    private static readonly string[] ValidStatuses =
    {
        ReceiveStatus.Received, ReceiveStatus.NotReturned, ReceiveStatus.NotNeeded, ReceiveStatus.Lost, ReceiveStatus.NotYetArrived,
    };

    private static string Stamp(DateTime t) => t.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);

    public static EditResult SetField(string dbPath, string orderId, EditField field, string newValue, DateTime now, string? expectedOld = null)
    {
        newValue ??= "";
        if (field == EditField.Status && !ValidStatuses.Contains(newValue))
            return new(EditKind.Invalid, "Trạng thái không hợp lệ.");
        if (field == EditField.IsWatchlist && newValue is not ("0" or "1"))
            return new(EditKind.Invalid, "Cờ theo dõi chỉ nhận 0 hoặc 1.");
        if (field == EditField.ComplaintStatus && newValue.Length > 0 && !ComplaintStatuses.All.Contains(newValue))
            return new(EditKind.Invalid, "Trạng thái khiếu nại không hợp lệ.");

        return Run(dbPath, (conn, tx) =>
        {
            var row = ReadRow(conn, tx, orderId);
            if (row is null) return new(EditKind.NotFound, "Không tìm thấy đơn.");
            if (row.Value.IsDeleted) return new(EditKind.NotEditable, "Đơn đã xóa: khôi phục trước khi sửa.");

            var (col, label) = Fields[field];
            var old = Scalar(conn, tx, $"SELECT {col} FROM orders WHERE id=$id", orderId)?.ToString() ?? "";
            if (expectedOld is not null && old != expectedOld)
                return new(EditKind.Conflict, "Giá trị đã bị thay đổi ở nơi khác, hãy tải lại.");
            if (old == newValue) return new(EditKind.NoChange, "Không có thay đổi.");

            Exec(conn, tx, $"UPDATE orders SET {col}=$v, v2_modified=1 WHERE id=$id", ("$v", field == EditField.IsWatchlist ? int.Parse(newValue) : newValue), ("$id", orderId));
            AddHistory(conn, tx, orderId, ColumnToJsonField(field), label, old, newValue, now);

            // Khiếu nại: đặt lần đầu thì ghi ngày khiếu nại (giữ ngày cũ khi đổi trạng thái); bỏ khiếu nại thì xóa ngày + ghi chú.
            if (field == EditField.ComplaintStatus)
            {
                if (newValue.Length == 0)
                    Exec(conn, tx, "UPDATE orders SET complaint_at='', complaint_note='' WHERE id=$id", ("$id", orderId));
                else if (old.Length == 0 || (Scalar(conn, tx, "SELECT complaint_at FROM orders WHERE id=$id", orderId)?.ToString() ?? "").Length == 0)
                    Exec(conn, tx, "UPDATE orders SET complaint_at=$d WHERE id=$id", ("$d", Stamp(now)), ("$id", orderId));
            }

            // Đánh dấu "đã nhận" thủ công: đặt giờ nhận nếu CHƯA có (giữ giờ nhận đầu tiên, không ghi đè).
            if (field == EditField.Status && newValue == ReceiveStatus.Received && string.IsNullOrEmpty(row.Value.RecvDate))
            {
                Exec(conn, tx, "UPDATE orders SET recv_date=$d, v2_modified=1 WHERE id=$id", ("$d", Stamp(now)), ("$id", orderId));
                AddHistory(conn, tx, orderId, "recvDate", "Ngày nhận", "", Stamp(now), now);
            }

            AddAudit(conn, tx, "edit", $"{label}: \"{old}\" -> \"{newValue}\" (đơn {row.Value.Track})", now);
            return new(EditKind.Saved, "Đã lưu.");
        });
    }

    public static EditResult Delete(string dbPath, string orderId, string from, DateTime now) =>
        Run(dbPath, (conn, tx) =>
        {
            var row = ReadRow(conn, tx, orderId);
            if (row is null) return new(EditKind.NotFound, "Không tìm thấy đơn.");
            if (row.Value.IsDeleted) return new(EditKind.NoChange, "Đơn đã xóa từ trước.");

            Exec(conn, tx, "UPDATE orders SET is_deleted=1, deleted_at=$t, deleted_from=$f, v2_modified=1 WHERE id=$id",
                ("$t", Stamp(now)), ("$f", from ?? ""), ("$id", orderId));
            AddAudit(conn, tx, "delete", $"Xóa đơn {row.Value.Track} khỏi \"{from}\" (xóa mềm, vẫn khôi phục được)", now);
            return new(EditKind.Saved, "Đã xóa (có thể khôi phục).");
        });

    public static EditResult Restore(string dbPath, string orderId, DateTime now) =>
        Run(dbPath, (conn, tx) =>
        {
            var row = ReadRow(conn, tx, orderId);
            if (row is null) return new(EditKind.NotFound, "Không tìm thấy đơn.");
            if (!row.Value.IsDeleted) return new(EditKind.NoChange, "Đơn không ở trạng thái đã xóa.");

            Exec(conn, tx, "UPDATE orders SET is_deleted=0, deleted_at='', deleted_from='', v2_modified=1 WHERE id=$id", ("$id", orderId));
            AddAudit(conn, tx, "restore", $"Khôi phục đơn {row.Value.Track}", now);
            return new(EditKind.Saved, "Đã khôi phục.");
        });

    public static EditResult Archive(string dbPath, string orderId, DateTime now) =>
        Run(dbPath, (conn, tx) =>
        {
            var row = ReadRow(conn, tx, orderId);
            if (row is null) return new(EditKind.NotFound, "Không tìm thấy đơn.");
            if (row.Value.IsDeleted) return new(EditKind.NotEditable, "Đơn đã xóa.");
            if (row.Value.IsArchived) return new(EditKind.NoChange, "Đơn đã ở kho lưu trữ.");

            Exec(conn, tx, "UPDATE orders SET is_archived=1, archived_at=$t, v2_modified=1 WHERE id=$id", ("$t", Stamp(now)), ("$id", orderId));
            AddAudit(conn, tx, "archive", $"Dời đơn {row.Value.Track} sang kho lưu trữ (thủ công)", now);
            return new(EditKind.Saved, "Đã dời sang kho lưu trữ.");
        });

    public static EditResult Unarchive(string dbPath, string orderId, DateTime now) =>
        Run(dbPath, (conn, tx) =>
        {
            var row = ReadRow(conn, tx, orderId);
            if (row is null) return new(EditKind.NotFound, "Không tìm thấy đơn.");
            if (row.Value.IsDeleted) return new(EditKind.NotEditable, "Đơn đã xóa.");
            if (!row.Value.IsArchived) return new(EditKind.NoChange, "Đơn không ở kho lưu trữ.");

            Exec(conn, tx, "UPDATE orders SET is_archived=0, archived_at='', v2_modified=1 WHERE id=$id", ("$id", orderId));
            AddAudit(conn, tx, "unarchive", $"Trả đơn {row.Value.Track} về bảng chính", now);
            return new(EditKind.Saved, "Đã trả về bảng chính.");
        });

    /// <summary>Lịch sử sửa của một đơn, mới nhất trước (chỉ các dòng đã liên kết theo id).</summary>
    public static IReadOnlyList<HistoryEntry> History(string dbPath, string orderId)
    {
        if (!File.Exists(dbPath)) return Array.Empty<HistoryEntry>();
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var c = conn.CreateCommand();
        c.CommandText = """
            SELECT time, field, label, old_val, new_val, edit_type FROM cell_edit_history
            WHERE linked_order_key=$id ORDER BY rowid DESC
            """;
        c.Parameters.AddWithValue("$id", orderId);
        var list = new List<HistoryEntry>();
        using var r = c.ExecuteReader();
        while (r.Read()) list.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)));
        return list;
    }

    // ---------------- nội bộ ----------------
    private readonly record struct Row(string Track, string RecvDate, bool IsDeleted, bool IsArchived);

    private static EditResult Run(string dbPath, Func<SqliteConnection, SqliteTransaction, EditResult> work)
    {
        if (!File.Exists(dbPath)) return new(EditKind.NotFound, "Chưa có cơ sở dữ liệu.");
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            conn.Open();
            using var tx = conn.BeginTransaction();
            var result = work(conn, tx);
            if (result.Kind == EditKind.Saved) tx.Commit(); else tx.Rollback();
            return result;
        }
        catch (Exception ex) { return new(EditKind.Error, "Lưu thất bại, không có gì bị thay đổi: " + ex.Message); }
    }

    private static Row? ReadRow(SqliteConnection conn, SqliteTransaction tx, string id)
    {
        using var c = conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "SELECT track, recv_date, is_deleted, is_archived FROM orders WHERE id=$id";
        c.Parameters.AddWithValue("$id", id);
        using var r = c.ExecuteReader();
        return r.Read() ? new Row(r.GetString(0), r.GetString(1), r.GetInt32(2) != 0, r.GetInt32(3) != 0) : null;
    }

    private static object? Scalar(SqliteConnection conn, SqliteTransaction tx, string sql, string id) =>
        ScalarP(conn, tx, sql, ("$id", id));

    private static object? ScalarP(SqliteConnection conn, SqliteTransaction tx, string sql, params (string Name, object Value)[] ps)
    {
        using var c = conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = sql;
        foreach (var (n, v) in ps) c.Parameters.AddWithValue(n, v);
        return c.ExecuteScalar();
    }

    private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql, params (string Name, object Value)[] ps)
    {
        using var c = conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = sql;
        foreach (var (n, v) in ps) c.Parameters.AddWithValue(n, v);
        c.ExecuteNonQuery();
    }

    private static void AddHistory(SqliteConnection conn, SqliteTransaction tx, string orderId, string field, string label, string oldVal, string newVal, DateTime now)
    {
        var seq = Convert.ToInt32(ScalarP(conn, tx,
            "SELECT COALESCE(MAX(seq)+1,0) FROM cell_edit_history WHERE row_ref=$id AND field=$f", ("$id", orderId), ("$f", field)));
        Exec(conn, tx, "INSERT INTO cell_edit_history VALUES ($rr,$f,$s,$t,$l,$o,$n,'manual',$link,'{}')",
            ("$rr", orderId), ("$f", field), ("$s", seq), ("$t", Stamp(now)), ("$l", label), ("$o", oldVal), ("$n", newVal), ("$link", orderId));
    }

    private static void AddAudit(SqliteConnection conn, SqliteTransaction tx, string type, string detail, DateTime now)
    {
        var seq = Convert.ToInt32(ScalarP(conn, tx, "SELECT COALESCE(MAX(seq)+1,0) FROM audit_log"));
        Exec(conn, tx, "INSERT INTO audit_log VALUES ($s,$t,$ty,$d,'{}')", ("$s", seq), ("$t", Stamp(now)), ("$ty", type), ("$d", detail));
    }

    private static string ColumnToJsonField(EditField f) => f switch
    {
        EditField.Status => "status", EditField.Note => "note", EditField.UserNote => "userNote", EditField.BuyerNote => "buyerNote",
        EditField.Carrier => "carrier", EditField.Deadline => "deadline", EditField.IsWatchlist => "isWatchlist",
        EditField.ComplaintStatus => "complaintStatus", EditField.ComplaintNote => "complaintNote",
        _ => "watchlistReason",
    };
}
