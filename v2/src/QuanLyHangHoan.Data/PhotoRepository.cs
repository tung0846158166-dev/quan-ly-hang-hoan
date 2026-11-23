using Microsoft.Data.Sqlite;

namespace QuanLyHangHoan.Data;

public sealed record PhotoData(string PhotoId, string Mime, byte[] Bytes, string Source, bool InlineDiffers);

/// <summary>Chỉ ĐỌC ảnh từ SQLite. DB chưa có / ảnh không có dữ liệu (missing) => null, không lỗi.</summary>
public static class PhotoRepository
{
    public static PhotoData? Get(string dbPath, string photoId)
    {
        if (!File.Exists(dbPath) || string.IsNullOrEmpty(photoId)) return null;
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var c = conn.CreateCommand();
        c.CommandText = "SELECT mime, data, source, inline_differs FROM photos WHERE photo_id=$id";
        c.Parameters.AddWithValue("$id", photoId);
        using var r = c.ExecuteReader();
        if (!r.Read() || r.IsDBNull(1)) return null;
        return new PhotoData(photoId, r.GetString(0), (byte[])r["data"], r.GetString(2), r.GetInt32(3) != 0);
    }

    /// <summary>photoId có trong đơn nhưng mất cả tệp lẫn ảnh nhúng.</summary>
    public static IReadOnlyList<string> MissingIds(string dbPath)
    {
        if (!File.Exists(dbPath)) return Array.Empty<string>();
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var c = conn.CreateCommand();
        c.CommandText = "SELECT photo_id FROM photos WHERE source='missing' ORDER BY photo_id";
        using var r = c.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }
}
