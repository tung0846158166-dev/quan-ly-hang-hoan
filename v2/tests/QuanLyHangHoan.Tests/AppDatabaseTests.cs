using System.IO;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.Tests;

public sealed class AppDatabaseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-db-" + Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_dir, "sub", "v2.db");

    public AppDatabaseTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private long Scalar(string sql)
    {
        using var c = new SqliteConnection($"Data Source={Db};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public void Initialize_creates_folder_file_and_all_tables_and_is_idempotent()
    {
        AppDatabase.Initialize(Db);
        AppDatabase.Initialize(Db);

        Assert.True(File.Exists(Db));
        foreach (var t in new[] { "orders", "order_items", "photos", "audit_log", "cell_edit_history" })
            Assert.Equal(1, Scalar($"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{t}'"));
        Assert.Equal(AppDatabase.SchemaVersion, Scalar("PRAGMA user_version"));
    }

    [Fact]
    public void Initialize_does_not_touch_existing_rows()
    {
        AppDatabase.Initialize(Db);
        using (var c = new SqliteConnection($"Data Source={Db};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO audit_log VALUES (1,'t','x','keep','{}')";
            cmd.ExecuteNonQuery();
        }
        AppDatabase.Initialize(Db);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM audit_log"));
    }

    [Fact]
    public void Newer_schema_version_is_refused_not_downgraded()
    {
        AppDatabase.Initialize(Db);
        using (var c = new SqliteConnection($"Data Source={Db};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"PRAGMA user_version = {AppDatabase.SchemaVersion + 1}";
            cmd.ExecuteNonQuery();
        }
        Assert.Throws<InvalidOperationException>(() => AppDatabase.Initialize(Db));
    }

    [Fact]
    public void Fresh_database_works_with_repository_and_scan_receiver_without_any_import()
    {
        AppDatabase.Initialize(Db);

        Assert.Empty(OrderRepository.Load(Db));
        var r = ScanReceiver.Receive(Db, "A", new DateTime(2026, 11, 23, 12, 0, 0));
        Assert.Equal(ScanOutcome.NotFound, r.Outcome);
        Assert.Equal(0, ArchiveSweep.Run(Db, DateTime.Now));
        Assert.Empty(PhotoRepository.MissingIds(Db));
    }
}
