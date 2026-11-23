using System.Globalization;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Data;

/// <summary>Tự dời lưu trữ: đặt cờ is_archived + archived_at cho đơn đủ điều kiện. Không xóa, không đụng đơn đã xóa/đã lưu trữ.</summary>
public static class ArchiveSweep
{
    /// <returns>Số đơn vừa được dời.</returns>
    public static int Run(string dbPath, DateTime now, ArchivePolicy? policy = null)
    {
        if (!File.Exists(dbPath)) return 0;
        policy ??= ArchivePolicy.Default;

        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using var tx = conn.BeginTransaction();

        var due = new List<string>();
        using (var q = conn.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT source_key, status, recv_date FROM orders WHERE is_archived=0 AND is_deleted=0";
            using var r = q.ExecuteReader();
            while (r.Read())
                if (policy.Check(new Order { Status = r.GetString(1), RecvDate = r.GetString(2) }, now)) due.Add(r.GetString(0));
        }

        var stamp = now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        foreach (var key in due)
        {
            using var u = conn.CreateCommand();
            u.Transaction = tx;
            u.CommandText = "UPDATE orders SET is_archived=1, archived_at=$t, v2_modified=1 WHERE source_key=$k AND is_archived=0 AND is_deleted=0";
            u.Parameters.AddWithValue("$t", stamp);
            u.Parameters.AddWithValue("$k", key);
            u.ExecuteNonQuery();
        }
        tx.Commit();
        return due.Count;
    }
}
