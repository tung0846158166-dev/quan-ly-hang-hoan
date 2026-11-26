using System.Globalization;
using Microsoft.Data.Sqlite;

namespace QuanLyHangHoan.Data;

public sealed record BackupResult(bool Ok, string Path, long Bytes, string Message);
public sealed record BackupEntry(string Path, string Name, DateTime Time, long Bytes);

/// <summary>
/// Sao lưu cơ sở dữ liệu SQLite (an toàn khi app đang ghi: dùng API sao lưu trực tuyến của SQLite, không chép tệp thô).
/// Tên tệp `data-yyyyMMdd-HHmmss.db`. Dọn bản cũ CHỈ với tệp đúng tên đó trong thư mục sao lưu, và luôn giữ lại tối thiểu
/// <see cref="MinKeep"/> bản mới nhất dù quá hạn (quy tắc 8: không xóa dữ liệu bừa bãi). Không bao giờ ghi vào DB gốc.
/// </summary>
public static class BackupService
{
    public const int MinKeep = 3;
    private const string Prefix = "data-";

    public static string DefaultDir(string dbPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".", "backups");

    public static BackupResult Backup(string dbPath, string backupDir, DateTime now)
    {
        try
        {
            if (!File.Exists(dbPath)) return new(false, "", 0, "Chưa có cơ sở dữ liệu để sao lưu.");
            Directory.CreateDirectory(backupDir);
            var name = $"{Prefix}{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.db";
            var dest = Path.Combine(backupDir, name);
            if (File.Exists(dest)) return new(true, dest, new FileInfo(dest).Length, "Bản sao lưu cùng giây đã có.");
            var tmp = dest + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);

            using (var src = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False"))
            using (var dst = new SqliteConnection($"Data Source={tmp};Pooling=False"))
            {
                src.Open(); dst.Open();
                src.BackupDatabase(dst);
            }
            // Kiểm tra bản sao đọc được và nguyên vẹn trước khi coi là xong.
            using (var chk = new SqliteConnection($"Data Source={tmp};Mode=ReadOnly;Pooling=False"))
            {
                chk.Open();
                using var cmd = chk.CreateCommand();
                cmd.CommandText = "PRAGMA integrity_check";
                var res = cmd.ExecuteScalar() as string;
                if (res != "ok") { chk.Close(); TryDelete(tmp); return new(false, "", 0, "Bản sao lưu không qua kiểm tra toàn vẹn: " + res); }
            }
            File.Move(tmp, dest);
            return new(true, dest, new FileInfo(dest).Length, "Đã sao lưu.");
        }
        catch (Exception ex) { return new(false, "", 0, "Sao lưu lỗi: " + ex.Message); }
    }

    public static IReadOnlyList<BackupEntry> List(string backupDir)
    {
        if (!Directory.Exists(backupDir)) return Array.Empty<BackupEntry>();
        var list = new List<BackupEntry>();
        foreach (var f in Directory.EnumerateFiles(backupDir, Prefix + "*.db"))
        {
            var name = Path.GetFileName(f);
            if (!TryParse(name, out var t)) continue;
            list.Add(new BackupEntry(f, name, t, new FileInfo(f).Length));
        }
        return list.OrderByDescending(e => e.Time).ToList();
    }

    /// <summary>Số bản sao lưu tự động tối đa được giữ (chủ dự án 26/11/2026: mỗi bản ~175 MB, không giới hạn thì 1 ngày đã 6 GB).</summary>
    public const int MaxKeep = 5;

    /// <summary>
    /// Giữ tối đa <see cref="MaxKeep"/> bản mới nhất; trong số đó bản quá `keepDays` ngày cũng bị xóa, nhưng luôn còn ít nhất
    /// <see cref="MinKeep"/> bản. Chỉ đụng tệp `data-yyyyMMdd-HHmmss.db`. Trả số bản đã xóa.
    /// </summary>
    public static int Prune(string backupDir, int keepDays, DateTime now)
    {
        var all = List(backupDir);
        var removed = 0;
        for (var i = MinKeep; i < all.Count; i++)
        {
            var e = all[i];
            var tooMany = i >= MaxKeep;
            var tooOld = now - e.Time > TimeSpan.FromDays(Math.Max(1, keepDays));
            if (!tooMany && !tooOld) continue;
            if (TryDelete(e.Path)) removed++;
        }
        return removed;
    }

    /// <summary>Sao lưu nếu bản mới nhất đã cũ hơn `everyHours` (hoặc chưa có); lần nào cũng dọn bản thừa/quá hạn.</summary>
    public static BackupResult? RunIfDue(string dbPath, string backupDir, int everyHours, int keepDays, DateTime now)
    {
        var latest = List(backupDir).FirstOrDefault();
        if (latest is not null && now - latest.Time < TimeSpan.FromHours(Math.Max(1, everyHours)))
        {
            Prune(backupDir, keepDays, now);
            return null;
        }
        var r = Backup(dbPath, backupDir, now);
        if (r.Ok) Prune(backupDir, keepDays, now);
        return r;
    }

    private static bool TryParse(string name, out DateTime t) =>
        DateTime.TryParseExact(name[Prefix.Length..^3], "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t);

    private static bool TryDelete(string p) { try { File.Delete(p); return true; } catch { return false; } }
}
