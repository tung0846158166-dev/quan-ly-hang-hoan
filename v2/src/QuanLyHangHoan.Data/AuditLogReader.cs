using Microsoft.Data.Sqlite;

namespace QuanLyHangHoan.Data;

public sealed record AuditEntry(long Seq, string Time, string Type, string Label, string Detail);

/// <summary>Đọc nhật ký hoạt động (bảng audit_log) để hiển thị. Chỉ ĐỌC.</summary>
public static class AuditLogReader
{
    public static string LabelOf(string type) => type switch
    {
        "import" => "Nạp đơn",
        "edit" => "Sửa đơn",
        "scan" => "Quét nhận kiện",
        "salework" => "Salework",
        "phone_new_order" => "Đơn mới từ điện thoại",
        "archive" => "Dời lưu trữ",
        "delete" => "Xóa đơn",
        "restore" => "Khôi phục đơn",
        "" => "Khác",
        _ => type,
    };

    /// <summary>Mới nhất trước. `type` rỗng = mọi loại; `text` lọc theo chi tiết (không phân biệt hoa thường).</summary>
    public static IReadOnlyList<AuditEntry> Read(string dbPath, int limit = 200, int offset = 0, string type = "", string text = "")
    {
        if (!File.Exists(dbPath)) return Array.Empty<AuditEntry>();
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT seq, time, type, detail FROM audit_log
            WHERE ($type = '' OR type = $type) AND ($text = '' OR detail LIKE $like ESCAPE '\')
            ORDER BY seq DESC LIMIT $limit OFFSET $offset
            """;
        cmd.Parameters.AddWithValue("$type", type ?? "");
        cmd.Parameters.AddWithValue("$text", text ?? "");
        cmd.Parameters.AddWithValue("$like", "%" + (text ?? "").Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));
        cmd.Parameters.AddWithValue("$offset", Math.Max(0, offset));
        var list = new List<AuditEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var ty = r.GetString(2);
            list.Add(new AuditEntry(r.GetInt64(0), r.GetString(1), ty, LabelOf(ty), r.GetString(3)));
        }
        return list;
    }

    public static IReadOnlyList<string> Types(string dbPath)
    {
        if (!File.Exists(dbPath)) return Array.Empty<string>();
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT type FROM audit_log ORDER BY type";
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public static long Count(string dbPath)
    {
        if (!File.Exists(dbPath)) return 0;
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM audit_log";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }
}
