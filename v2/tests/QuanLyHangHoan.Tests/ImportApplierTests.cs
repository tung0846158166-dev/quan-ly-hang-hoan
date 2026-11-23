using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Tests;

/// <summary>Áp dụng dữ liệu nhập (từ Excel/Salework) vào SQLite: cập nhật không cộng lặp, kiện A/B/C riêng, đơn đã xóa không nạp lại. Dữ liệu GIẢ.</summary>
public sealed class ImportApplierTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-apply-" + Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_dir, "t.db");

    public ImportApplierTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "du_lieu_hang_hoan.json"), JsonSerializer.Serialize(new[]
        {
            new { platform = "Shopee", orderType = "Trả hàng", orderId = "O1", track = "A", status = "Hàng đã trả", price = 100, userNote = "đã kiểm",
                  photoId = "p_X", items = new[] { new { product = "Áo giả", color = "Đen", size = "M", qty = 2 } } },
        }));
        File.WriteAllText(Path.Combine(_dir, "phu_shopee_deleted_orders_v1.json"), JsonSerializer.Serialize(new
        {
            v = JsonSerializer.Serialize(new[] { new { platform = "Shopee", orderType = "Trả hàng", orderId = "OD", track = "DEL", status = "Chưa trả", price = 1, items = Array.Empty<object>() } })
        }));
        File.WriteAllText(Path.Combine(_dir, "phu_shopee_archived_orders_v1.json"), JsonSerializer.Serialize(new
        {
            v = JsonSerializer.Serialize(new[] { new { platform = "Shopee", orderType = "Trả hàng", orderId = "OAR", track = "ARC", status = "Hàng đã trả", price = 5, items = Array.Empty<object>() } })
        }));
        LegacyImporter.Import(_dir, Db);
    }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static Order In(string orderId, string track, int qty = 1, decimal price = 100, string platform = "Shopee") => new()
    {
        Platform = platform, OrderType = "Trả hàng", OrderId = orderId, Track = track, Price = price,
        Items = { new OrderItem { Product = "Áo giả", Color = "Đen", Size = "M", Qty = qty } },
    };

    private IReadOnlyList<StoredOrder> All => OrderRepository.Load(Db);
    private long Count(string sql)
    {
        using var c = new SqliteConnection($"Data Source={Db};Pooling=False"); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public void New_parcel_is_inserted_and_visible_as_active()
    {
        var r = ImportApplier.Apply(Db, new[] { In("O9", "N1") }, Now);

        Assert.Equal(1, r.Inserted);
        Assert.Contains(OrderRepository.LoadActive(Db), o => o.Track == "N1");
        Assert.Single(All.Single(o => o.Order.Track == "N1").Order.Items);
    }

    [Fact]
    public void Reimport_updates_existing_parcel_without_doubling_items_and_keeps_warehouse_fields()
    {
        var r = ImportApplier.Apply(Db, new[] { In("O1", "A", qty: 2, price: 150) }, Now);

        Assert.Equal(0, r.Inserted);
        Assert.Equal(1, r.Updated);
        var o = All.Single(x => x.Order.Track == "A").Order;
        Assert.Equal(150m, o.Price);                                   // dữ liệu nhập được cập nhật
        Assert.Equal(2, o.Items.Single().Qty);                         // KHÔNG thành 4
        Assert.Equal("Hàng đã trả", o.Status);                         // trường kho giữ nguyên
        Assert.Equal("đã kiểm", o.UserNote);
        Assert.Equal("p_X", o.PhotoId);
    }

    [Fact]
    public void Applying_same_batch_twice_is_idempotent()
    {
        var batch = new[] { In("O9", "N1"), In("O1", "A", qty: 2) };
        ImportApplier.Apply(Db, batch, Now);
        var second = ImportApplier.Apply(Db, new[] { In("O9", "N1"), In("O1", "A", qty: 2) }, Now);

        Assert.Equal(0, second.Inserted);
        Assert.Equal(2, second.Updated);
        Assert.Equal(1, Count("SELECT COUNT(*) FROM orders WHERE track='N1'"));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM order_items WHERE source_key IN (SELECT source_key FROM orders WHERE track='N1')"));
    }

    [Fact]
    public void Sibling_with_same_order_id_is_a_new_row_not_a_merge()
    {
        var r = ImportApplier.Apply(Db, new[] { In("O1", "B") }, Now);

        Assert.Equal(1, r.Inserted);
        Assert.Equal(2, All.Count(o => o.Order.OrderId == "O1"));
        Assert.Equal("A", All.Single(o => o.Order.Track == "A").Order.Track);
    }

    [Fact]
    public void Deleted_order_is_not_resurrected_and_is_reported()
    {
        var before = Count("SELECT COUNT(*) FROM orders");
        var r = ImportApplier.Apply(Db, new[] { In("OD", "DEL", price: 999) }, Now);

        Assert.Equal(1, r.SkippedDeleted);
        Assert.Equal(0, r.Inserted);
        Assert.Equal(0, r.Updated);
        Assert.Equal(before, Count("SELECT COUNT(*) FROM orders"));
        var d = All.Single(o => o.Order.Track == "DEL");
        Assert.True(d.IsDeleted);
        Assert.Equal(1m, d.Order.Price);                               // đơn đã xóa không bị sửa
    }

    [Fact]
    public void Archived_order_is_updated_in_place_and_stays_archived()
    {
        var r = ImportApplier.Apply(Db, new[] { In("OAR", "ARC", price: 77) }, Now);

        Assert.Equal(1, r.Updated);
        var a = All.Single(o => o.Order.Track == "ARC");
        Assert.True(a.IsArchived);
        Assert.Equal(77m, a.Order.Price);
        Assert.Equal(1, Count("SELECT COUNT(*) FROM orders WHERE track='ARC'")); // không sinh bản sao ở bảng chính
    }

    [Fact]
    public void Ambiguous_incoming_changes_nothing_and_is_reported()
    {
        // hai kiện cùng chia sẻ một mã riêng => nhập mã đó là nhập nhằng
        ImportApplier.Apply(Db, new[] { In("OX", "X1"), In("OX", "X2") }, Now);
        using (var c = new SqliteConnection($"Data Source={Db};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE orders SET tracking_codes='[\"SHARED\"]' WHERE track IN ('X1','X2')";
            cmd.ExecuteNonQuery();
        }
        var before = Count("SELECT COUNT(*) FROM orders");

        var r = ImportApplier.Apply(Db, new[] { In("OX", "SHARED", price: 555) }, Now);

        Assert.Single(r.Ambiguous);
        Assert.Equal("SHARED", r.Ambiguous[0].Track);
        Assert.Equal(before, Count("SELECT COUNT(*) FROM orders"));
        Assert.DoesNotContain(All, o => o.Order.Price == 555m);
    }

    [Fact]
    public void Different_platform_same_codes_is_inserted_separately()
    {
        var r = ImportApplier.Apply(Db, new[] { In("O1", "A", platform: "TikTok") }, Now);
        Assert.Equal(1, r.Inserted);
        Assert.Equal(2, All.Count(o => o.Order.Track == "A"));
    }

    [Fact]
    public void Summary_is_written_to_audit_log_and_database_untouched_when_missing()
    {
        ImportApplier.Apply(Db, new[] { In("O9", "N1") }, Now);
        Assert.Equal(1, Count("SELECT COUNT(*) FROM audit_log WHERE type='import'"));

        var none = Path.Combine(_dir, "none.db");
        var r = ImportApplier.Apply(none, new[] { In("O9", "N1") }, Now);
        Assert.False(r.Ok);
        Assert.False(File.Exists(none));
    }

    [Fact]
    public void Platform_label_tiktok_shop_is_normalized_to_tiktok_on_insert()
    {
        ImportApplier.Apply(Db, new[] { In("O9", "N1", platform: "TikTok Shop") }, Now);
        Assert.Equal("TikTok", All.Single(o => o.Order.Track == "N1").Order.Platform);
    }
}
