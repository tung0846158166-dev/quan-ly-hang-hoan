using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Tests;

/// <summary>
/// Nhập lại dữ liệu cũ SAU KHI đã dùng v2 phải an toàn: không ghi đè trạng thái/ngày nhận/ảnh/ghi chú đã sửa ở v2,
/// không tạo bản trùng khi app cũ dời đơn sang kho lưu trữ. Dữ liệu GIẢ.
/// </summary>
public sealed class LegacyReimportSafetyTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-reimp-" + Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_dir, "t.db");

    public LegacyReimportSafetyTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static object Row(string oid, string track, string status = "Chưa trả", string note = "", string carrier = "SPX Express", string claim = "") => new
    {
        platform = "Shopee", orderType = "Trả hàng", orderId = oid, returnOrderId = claim, track, status, note, carrier, price = 1,
        items = new[] { new { product = "Áo giả", color = "", size = "", qty = 1 } },
    };

    private void Legacy(object[] main, object[]? archived = null, object[]? deleted = null)
    {
        File.WriteAllText(Path.Combine(_dir, "du_lieu_hang_hoan.json"), JsonSerializer.Serialize(main));
        File.WriteAllText(Path.Combine(_dir, "phu_shopee_archived_orders_v1.json"), JsonSerializer.Serialize(new { v = JsonSerializer.Serialize(archived ?? Array.Empty<object>()) }));
        File.WriteAllText(Path.Combine(_dir, "phu_shopee_deleted_orders_v1.json"), JsonSerializer.Serialize(new { v = JsonSerializer.Serialize(deleted ?? Array.Empty<object>()) }));
        LegacyImporter.Import(_dir, Db);
    }

    private IReadOnlyList<StoredOrder> All => OrderRepository.Load(Db);
    private StoredOrder Get(string track) => All.Single(o => o.Order.Track == track);
    private long Count(string sql)
    {
        using var c = new SqliteConnection($"Data Source={Db};Pooling=False"); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public void Untouched_rows_still_take_the_latest_legacy_values()
    {
        Legacy(new[] { Row("O1", "A") });
        Legacy(new[] { Row("O1", "A", status: "Hàng đã trả", note: "mới") });

        Assert.Equal("Hàng đã trả", Get("A").Order.Status);
        Assert.Equal("mới", Get("A").Order.Note);
        Assert.Equal(1, Count("SELECT COUNT(*) FROM orders"));
    }

    [Fact]
    public void Row_scanned_in_v2_keeps_its_received_state_after_legacy_reimport()
    {
        Legacy(new[] { Row("O1", "A") });
        ScanReceiver.Receive(Db, "A", Now);

        Legacy(new[] { Row("O1", "A", status: "Chưa trả") });   // app cũ chưa biết kiện đã nhận

        var o = Get("A").Order;
        Assert.Equal("Hàng đã trả", o.Status);
        Assert.Equal("23/11/2026 12:00:00", o.RecvDate);
        Assert.Equal(1, Count("SELECT COUNT(*) FROM orders"));
    }

    [Fact]
    public void Row_with_photo_from_phone_keeps_the_photo_link()
    {
        Legacy(new[] { Row("O1", "A") });
        ScanReceiver.Receive(Db, "A", Now, new byte[] { 1, 2, 3 });
        var photoId = Get("A").Order.PhotoId;

        Legacy(new[] { Row("O1", "A") });

        Assert.Equal(photoId, Get("A").Order.PhotoId);
        Assert.Equal(new byte[] { 1, 2, 3 }, PhotoRepository.Get(Db, photoId)!.Bytes);
    }

    [Fact]
    public void Manual_edits_are_kept_and_only_empty_fields_are_filled_from_legacy()
    {
        Legacy(new[] { Row("O1", "A", note: "", carrier: "") });
        var id = Get("A").Order.Id;
        OrderEditor.SetField(Db, id, EditField.UserNote, "ghi chú của tôi", Now);
        OrderEditor.SetField(Db, id, EditField.Note, "lý do tôi sửa", Now);

        Legacy(new[] { Row("O1", "A", note: "lý do app cũ", carrier: "GHN") });

        var o = Get("A").Order;
        Assert.Equal("ghi chú của tôi", o.UserNote);
        Assert.Equal("lý do tôi sửa", o.Note);        // giá trị đã có: không bị ghi đè
        Assert.Equal("GHN", o.Carrier);               // ô trống: được bổ sung từ dữ liệu cũ
    }

    [Fact]
    public void Order_the_old_app_moved_to_archive_becomes_flagged_not_duplicated()
    {
        Legacy(new[] { Row("O1", "A"), Row("O2", "B") });
        var before = Count("SELECT COUNT(*) FROM orders");

        Legacy(new[] { Row("O2", "B") }, archived: new[] { Row("O1", "A", status: "Hàng đã trả") });

        Assert.Equal(before, Count("SELECT COUNT(*) FROM orders"));          // không sinh bản trùng
        Assert.True(Get("A").IsArchived);
        Assert.Equal("Hàng đã trả", Get("A").Order.Status);
        Assert.False(Get("B").IsArchived);
    }

    [Fact]
    public void Row_changed_in_v2_is_not_moved_by_legacy_and_not_duplicated()
    {
        Legacy(new[] { Row("O1", "A") });
        OrderEditor.SetField(Db, Get("A").Order.Id, EditField.UserNote, "đang xử lý", Now);

        Legacy(Array.Empty<object>(), archived: new[] { Row("O1", "A", status: "Hàng đã trả") });

        Assert.Equal(1, Count("SELECT COUNT(*) FROM orders"));
        Assert.False(Get("A").IsArchived);                                    // quyết định của v2 được tôn trọng
        Assert.Equal("đang xử lý", Get("A").Order.UserNote);
    }

    [Fact]
    public void Order_restored_to_main_by_the_old_app_is_unflagged_when_untouched_in_v2()
    {
        Legacy(Array.Empty<object>(), archived: new[] { Row("O1", "A") });
        Assert.True(Get("A").IsArchived);

        Legacy(new[] { Row("O1", "A") });

        Assert.False(Get("A").IsArchived);
        Assert.Equal(1, Count("SELECT COUNT(*) FROM orders"));
    }

    [Fact]
    public void Automatic_archive_sweep_counts_as_a_v2_change()
    {
        Legacy(new[] { Row("O1", "A", status: "Hàng đã trả") });
        using (var c = new SqliteConnection($"Data Source={Db};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE orders SET recv_date='18/11/2026 10:00:00'";
            cmd.ExecuteNonQuery();
        }
        Assert.Equal(1, ArchiveSweep.Run(Db, Now));

        Legacy(new[] { Row("O1", "A", status: "Hàng đã trả") });              // app cũ vẫn để ở bảng chính

        Assert.True(Get("A").IsArchived);                                     // v2 đã dời: không bị kéo ngược
    }

    [Fact]
    public void Row_created_by_a_v2_import_is_not_duplicated_when_the_old_app_later_has_it()
    {
        AppDatabase.Initialize(Db);
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["Order ID"] = "O1", ["Return Order ID"] = "C1", ["Return Logistics Tracking ID"] = "RT111111", ["Product Name"] = "Shirt", ["Return Status"] = "In Process", ["Quantity"] = "1" },
        };
        TikTokImportService.Apply(Db, rows, new TikTokMapOptions { Today = Now }, ImportProductMode.Supplement, Now);
        Assert.Equal(1, Count("SELECT COUNT(*) FROM orders"));

        Legacy(new object[]
        {
            new { platform = "TikTok Shop", orderType = "Trả hàng/Hoàn tiền", orderId = "O1", returnOrderId = "C1", track = "RT111111", returnTrack = "RT111111",
                  status = "Chưa về", carrier = "GHN", price = 0, items = new[] { new { product = "Shirt", color = "", size = "", qty = 1 } } },
        });

        Assert.Equal(1, Count("SELECT COUNT(*) FROM orders"));
    }

    [Fact]
    public void Genuine_duplicate_groups_in_legacy_stay_separate_across_reimports()
    {
        Legacy(new[] { Row("O1", "A"), Row("O1", "A") });
        Legacy(new[] { Row("O1", "A"), Row("O1", "A") });
        Assert.Equal(2, Count("SELECT COUNT(*) FROM orders"));
    }

    [Fact]
    public void Schema_v3_database_gets_the_v2_modified_column_without_losing_rows()
    {
        Legacy(new[] { Row("O1", "A") });
        using (var c = new SqliteConnection($"Data Source={Db};Pooling=False"))
        {
            c.Open();
            foreach (var sql in new[] { "ALTER TABLE orders DROP COLUMN v2_modified", "PRAGMA user_version = 3" })
            { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        }
        

        AppDatabase.Initialize(Db);

        Assert.Equal(1, Count("SELECT COUNT(*) FROM orders"));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM pragma_table_info('orders') WHERE name='v2_modified'"));
        Assert.Equal(AppDatabase.SchemaVersion, Count("PRAGMA user_version"));
    }
}
