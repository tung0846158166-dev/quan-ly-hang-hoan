using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Tests;

/// <summary>Đọc lại từ SQLite sau khi nhập. Dữ liệu GIẢ.</summary>
public sealed class OrderRepositoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-repo-" + Guid.NewGuid().ToString("N"));
    private readonly string _db;

    public OrderRepositoryTests() { Directory.CreateDirectory(_dir); _db = Path.Combine(_dir, "t.db"); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static object Row(string oid, string track, string platform, string type, int qty = 2) => new
    {
        platform, orderType = type, orderId = oid, track, status = "Chưa về", price = 50000, shipDate = "01/11/2026",
        items = new[] { new { product = "Áo giả", color = "Đen", size = "M", qty } },
        trackingCodes = new[] { track + "-OLD" }
    };

    private void Write(string file, object[] rows, bool wrapped)
    {
        var json = JsonSerializer.Serialize(rows);
        File.WriteAllText(Path.Combine(_dir, file), wrapped ? JsonSerializer.Serialize(new { v = json }) : json);
    }

    private void Seed()
    {
        Write("du_lieu_hang_hoan.json", new[]
        {
            Row("O1", "A", "Shopee", "Trả hàng/Hoàn tiền"), Row("O1", "B", "Shopee", "Trả hàng/Hoàn tiền"),
            Row("O2", "C", "Shopee", "Đơn hủy"), Row("O3", "D", "TikTok Shop", "THẤT THOÁT"),
        }, false);
        Write("phu_shopee_archived_orders_v1.json", new[] { Row("O4", "E", "Shopee", "Đơn hủy") }, true);
        Write("phu_shopee_deleted_orders_v1.json", new[] { Row("O5", "F", "TikTok Shop", "THẤT THOÁT") }, true);
        LegacyImporter.Import(_dir, _db);
    }

    [Fact]
    public void Loads_all_with_items_codes_and_flags()
    {
        Seed();
        var all = OrderRepository.Load(_db);

        Assert.Equal(6, all.Count);
        var a = all.Single(x => x.Order.Track == "A");
        Assert.Equal("O1", a.Order.OrderId);
        Assert.Equal(2, a.Order.Items.Single().Qty);
        Assert.Contains("A-OLD", a.Order.TrackingCodes);
        Assert.Equal(50000m, a.Order.Price);
        Assert.False(a.IsArchived || a.IsDeleted);
        Assert.True(all.Single(x => x.Order.Track == "E").IsArchived);
        Assert.True(all.Single(x => x.Order.Track == "F").IsDeleted);
        Assert.Equal("TikTok", all.Single(x => x.Order.Track == "D").Order.Platform);
    }

    [Fact]
    public void Active_excludes_archived_and_deleted_and_keeps_siblings()
    {
        Seed();
        var active = OrderRepository.LoadActive(_db);

        Assert.Equal(4, active.Count);
        Assert.Equal(2, active.Count(o => o.OrderId == "O1"));
    }

    [Fact]
    public void PhotoRepository_returns_bytes_source_and_handles_missing()
    {
        var bytes = new byte[] { 1, 2, 3 };
        Write("du_lieu_hang_hoan.json", Array.Empty<object>(), false);
        Write("phu_shopee_archived_orders_v1.json", new object[]
        {
            new { platform = "Shopee", orderId = "O1", track = "T1", photoId = "p_IN", photo = "data:image/jpeg;base64," + Convert.ToBase64String(bytes), items = Array.Empty<object>() },
            new { platform = "Shopee", orderId = "O2", track = "T2", photoId = "p_GONE", photo = "", items = Array.Empty<object>() },
        }, true);
        LegacyImporter.Import(_dir, _db);

        var ok = PhotoRepository.Get(_db, "p_IN");
        Assert.NotNull(ok);
        Assert.Equal(bytes, ok!.Bytes);
        Assert.Equal("image/jpeg", ok.Mime);
        Assert.Equal("inline", ok.Source);
        Assert.Null(PhotoRepository.Get(_db, "p_GONE"));   // mất cả hai nguồn: không có ảnh
        Assert.Null(PhotoRepository.Get(_db, "khong-co"));
        Assert.Null(PhotoRepository.Get(Path.Combine(_dir, "none.db"), "p_IN"));
        Assert.Equal(new[] { "p_GONE" }, PhotoRepository.MissingIds(_db));
    }

    [Fact]
    public void Missing_database_returns_empty_not_error()
    {
        Assert.Empty(OrderRepository.Load(Path.Combine(_dir, "none.db")));
    }
}
