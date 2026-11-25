using Microsoft.Data.Sqlite;

namespace QuanLyHangHoan.Data.Cloud;

/// <summary>
/// Bộ nhớ đệm ảnh tải từ cloud (máy tính ở chế độ cloud). Bảng RIÊNG <c>cloud_photo_cache</c>, KHÔNG ghi vào bảng <c>photos</c> gốc:
/// công cụ chuyển dữ liệu đọc bảng gốc để đẩy ảnh cũ lên, nếu lẫn ảnh đệm vào thì ảnh sẽ bị đẩy ngược. Cloud tự xóa ảnh sau 90 ngày nên đệm cũng dọn theo.
/// </summary>
public static class CloudPhotoCache
{
    private const string TableSql = "CREATE TABLE IF NOT EXISTS cloud_photo_cache (photo_id TEXT PRIMARY KEY, mime TEXT NOT NULL, data BLOB NOT NULL, cached_at INTEGER NOT NULL);";

    private static SqliteConnection Open(string dbPath)
    {
        var c = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout=5000;" + TableSql;
        cmd.ExecuteNonQuery();
        return c;
    }

    public static PhotoData? Get(string dbPath, string photoId)
    {
        if (string.IsNullOrEmpty(photoId) || !File.Exists(dbPath)) return null;
        using var c = Open(dbPath);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT mime, data FROM cloud_photo_cache WHERE photo_id=$id";
        cmd.Parameters.AddWithValue("$id", photoId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new PhotoData(photoId, r.GetString(0), (byte[])r["data"], "cloud", false) : null;
    }

    public static void Put(string dbPath, string photoId, string mime, byte[] bytes, DateTimeOffset? at = null)
    {
        using var c = Open(dbPath);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO cloud_photo_cache(photo_id, mime, data, cached_at) VALUES ($id,$m,$d,$t)";
        cmd.Parameters.AddWithValue("$id", photoId);
        cmd.Parameters.AddWithValue("$m", mime);
        cmd.Parameters.AddWithValue("$d", bytes);
        cmd.Parameters.AddWithValue("$t", (at ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds());
        cmd.ExecuteNonQuery();
    }

    /// <summary>Xóa ảnh đệm cũ hơn <paramref name="keep"/>. Trả số ảnh đã xóa.</summary>
    public static int Prune(string dbPath, TimeSpan keep)
    {
        if (!File.Exists(dbPath)) return 0;
        using var c = Open(dbPath);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM cloud_photo_cache WHERE cached_at < $t";
        cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.Subtract(keep).ToUnixTimeMilliseconds());
        return cmd.ExecuteNonQuery();
    }

    /// <summary>Nhận dạng định dạng theo byte đầu (cloud chỉ nhận JPEG/PNG/WebP); không rõ thì coi là JPEG.</summary>
    public static string DetectMime(byte[] b)
    {
        if (b.Length >= 4 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return "image/webp";
        return "image/jpeg";
    }
}
