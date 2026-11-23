using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Tests.ImporterTests;

/// <summary>Dữ liệu GIẢ. Kiểm tra nhập thật vào SQLite: đủ nhóm, idempotent, không gộp A/B/C, không sửa nguồn.</summary>
public sealed class LegacyImporterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-imp-" + Guid.NewGuid().ToString("N"));
    private readonly string _db;

    public LegacyImporterTests()
    {
        Directory.CreateDirectory(_dir);
        _db = Path.Combine(_dir, "test.db");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static object Row(string orderId, string track, string platform = "Shopee", string rr = "", string? extraKey = null) => new
    {
        platform, orderType = "Trả hàng", platformStatus = "Đang về", preOrder = "Không", orderId, track,
        returnOrderId = rr, altTrack = "", carrier = "SPX", price = 100000, status = "Chưa về", photo = "", note = "n",
        buyerNote = "", importWeek = "T45", importedAt = "2026-11-05", importedAtFormatted = "05/11/2026",
        shipDate = "01/11/2026", returnDate = "", recvDate = "",
        items = new[] { new { product = "Áo giả", color = "Đen", size = "M", qty = 1 } }
    };

    private void Write(string file, object[] rows, bool wrapped)
    {
        var json = JsonSerializer.Serialize(rows);
        File.WriteAllText(Path.Combine(_dir, file), wrapped ? JsonSerializer.Serialize(new { v = json }) : json);
    }

    private long Count(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_db};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }

    private void Seed()
    {
        Write("du_lieu_hang_hoan.json", new[] { Row("O1", "A"), Row("O1", "B") }, false);
        Write("phu_shopee_archived_orders_v1.json", new[] { Row("O2", "C") }, true);
        Write("phu_shopee_deleted_orders_v1.json", new[] { Row("O3", "D", "TikTok Shop") }, true);
    }

    [Fact]
    public void Imports_all_three_groups_with_flags_and_keeps_siblings_separate()
    {
        Seed();
        var r = LegacyImporter.Import(_dir, _db);

        Assert.Equal(4, r.Inserted);
        Assert.Equal(4, Count("SELECT COUNT(*) FROM orders"));
        Assert.Equal(2, Count("SELECT COUNT(*) FROM orders WHERE order_id='O1'")); // A và B tách riêng
        Assert.Equal(1, Count("SELECT COUNT(*) FROM orders WHERE is_archived=1"));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM orders WHERE is_deleted=1"));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM orders WHERE platform='TikTok'"));
        Assert.Equal(4, Count("SELECT COUNT(*) FROM orders WHERE raw_json <> ''"));
    }

    [Fact]
    public void Import_is_idempotent_no_duplicate_rows()
    {
        Seed();
        LegacyImporter.Import(_dir, _db);
        var second = LegacyImporter.Import(_dir, _db);

        Assert.Equal(0, second.Inserted);
        Assert.Equal(4, second.Updated);
        Assert.Equal(4, Count("SELECT COUNT(*) FROM orders"));
        Assert.Equal(4, Count("SELECT COUNT(*) FROM order_items"));
    }

    [Fact]
    public void Same_key_duplicates_are_both_kept_not_merged()
    {
        Write("du_lieu_hang_hoan.json", new[] { Row("O1", "A"), Row("O1", "A") }, false);
        LegacyImporter.Import(_dir, _db);
        LegacyImporter.Import(_dir, _db);

        Assert.Equal(2, Count("SELECT COUNT(*) FROM orders"));
    }

    [Fact]
    public void Imports_audit_log_and_cell_edit_history_idempotently()
    {
        Seed();
        File.WriteAllText(Path.Combine(_dir, "phu_shopee_excel_audit_log_v1.json"), JsonSerializer.Serialize(new
        {
            v = JsonSerializer.Serialize(new[] { new { time = "2026-11-05 10:00", type = "import", detail = "giả 1" },
                                                  new { time = "2026-11-05 10:00", type = "import", detail = "giả 1" } })
        }));
        File.WriteAllText(Path.Combine(_dir, "phu_ShopeeReturnCellEditHistory_v1.json"), JsonSerializer.Serialize(new
        {
            v = JsonSerializer.Serialize(new Dictionary<string, object[]>
            {
                ["290:status"] = new object[] { new { time = "t1", label = "Trạng thái", oldVal = "Chưa về", newVal = "Hàng đã trả", editType = "manual" } }
            })
        }));

        LegacyImporter.Import(_dir, _db);
        LegacyImporter.Import(_dir, _db);

        Assert.Equal(2, Count("SELECT COUNT(*) FROM audit_log")); // hai dòng giống nhau vẫn giữ cả hai
        Assert.Equal(1, Count("SELECT COUNT(*) FROM cell_edit_history"));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM cell_edit_history WHERE linked_order_key IS NULL AND row_ref='290' AND field='status'"));
    }

    private static string DataUri(byte[] b) => "data:image/jpeg;base64," + Convert.ToBase64String(b);

    private void WritePhotoFile(string photoId, byte[] bytes)
    {
        var dir = Path.Combine(_dir, "photos");
        Directory.CreateDirectory(dir);
        var name = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(photoId))).ToLowerInvariant();
        File.WriteAllText(Path.Combine(dir, name + ".json"), JsonSerializer.Serialize(new { id = photoId, data = DataUri(bytes) }));
    }

    private static object ArchivedRow(string orderId, string track, string photoId, string photo) => new
    {
        platform = "Shopee", orderType = "x", orderId, track, status = "Hàng đã trả", price = 1, items = Array.Empty<object>(),
        photoId, photo
    };

    [Fact]
    public void Imports_photos_file_first_inline_fallback_and_marks_missing()
    {
        var a = new byte[] { 1, 2, 3 }; var b = new byte[] { 9, 9 }; var c = new byte[] { 7 };
        WritePhotoFile("p_FILE", a);        // có tệp, không nhúng
        WritePhotoFile("p_BOTH", b);        // có tệp, nhúng khác nội dung
        Write("du_lieu_hang_hoan.json", Array.Empty<object>(), false);
        Write("phu_shopee_archived_orders_v1.json", new[]
        {
            ArchivedRow("O1", "T1", "p_FILE", ""),
            ArchivedRow("O2", "T2", "p_BOTH", DataUri(c)),
            ArchivedRow("O3", "T3", "p_INLINE", DataUri(c)),   // chỉ nhúng
            ArchivedRow("O4", "T4", "p_GONE", ""),             // mất cả hai
        }, true);

        LegacyImporter.Import(_dir, _db);
        LegacyImporter.Import(_dir, _db); // idempotent

        Assert.Equal(4, Count("SELECT COUNT(*) FROM photos"));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM photos WHERE photo_id='p_FILE' AND source='file' AND length(data)=3"));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM photos WHERE photo_id='p_BOTH' AND source='file' AND inline_differs=1 AND length(data)=2"));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM photos WHERE photo_id='p_INLINE' AND source='inline' AND length(data)=1"));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM photos WHERE photo_id='p_GONE' AND source='missing' AND data IS NULL"));
    }

    [Fact]
    public void Reconcile_matches_dry_run_counts_and_flags_mismatch()
    {
        Seed();
        LegacyImporter.Import(_dir, _db);

        var ok = LegacyImporter.Reconcile(_dir, _db);
        Assert.True(ok.AllMatch);
        Assert.Equal(4, ok.DbOrders);
        Assert.Equal(4, ok.ExpectedOrders);

        using (var c = new SqliteConnection($"Data Source={_db};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM orders WHERE track='A'";
            cmd.ExecuteNonQuery();
        }
        var bad = LegacyImporter.Reconcile(_dir, _db);
        Assert.False(bad.AllMatch);
        Assert.Equal(3, bad.DbOrders);
    }

    [Fact]
    public void Does_not_modify_source_files()
    {
        Seed();
        var p = Path.Combine(_dir, "du_lieu_hang_hoan.json");
        var before = File.ReadAllBytes(p);
        LegacyImporter.Import(_dir, _db);
        Assert.Equal(before, File.ReadAllBytes(p));
    }
}
