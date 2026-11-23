using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Tests;

/// <summary>Nạp file TikTok/Salework vào SQLite (đường đi đầy đủ: ánh xạ -> gộp -> lưu). Dữ liệu GIẢ.</summary>
public sealed class TikTokImportServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-tt-" + Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_dir, "t.db");

    public TikTokImportServiceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static Dictionary<string, string> Row(string orderId, string claim, string ret, string product = "Shirt", int qty = 1, string extraKey = "", string extraVal = "")
    {
        var d = new Dictionary<string, string>
        {
            ["Order ID"] = orderId, ["Return Order ID"] = claim, ["Return Logistics Tracking ID"] = ret, ["Product Name"] = product,
            ["Return Status"] = "In Process", ["Quantity"] = qty.ToString(),
        };
        if (extraKey.Length > 0) d[extraKey] = extraVal;
        return d;
    }

    private static TikTokMapOptions Opt() => new() { Today = Now };

    private TikTokImportResult Import(params Dictionary<string, string>[] rows) =>
        TikTokImportService.Apply(Db, rows.ToList(), Opt(), ImportProductMode.Supplement, Now);

    private void SeedLegacy(object[] main, object[]? archived = null, object[]? deleted = null)
    {
        File.WriteAllText(Path.Combine(_dir, "du_lieu_hang_hoan.json"), JsonSerializer.Serialize(main));
        if (archived is not null)
            File.WriteAllText(Path.Combine(_dir, "phu_shopee_archived_orders_v1.json"), JsonSerializer.Serialize(new { v = JsonSerializer.Serialize(archived) }));
        if (deleted is not null)
            File.WriteAllText(Path.Combine(_dir, "phu_shopee_deleted_orders_v1.json"), JsonSerializer.Serialize(new { v = JsonSerializer.Serialize(deleted) }));
        LegacyImporter.Import(_dir, Db);
    }

    private IReadOnlyList<StoredOrder> All => OrderRepository.Load(Db);
    private long Count(string sql)
    {
        using var c = new SqliteConnection($"Data Source={Db};Pooling=False"); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public void New_database_gets_new_rows_with_defaults_prices_and_items_round_trip()
    {
        AppDatabase.Initialize(Db);
        var r = Import(Row("O1", "C1", "RT111111", qty: 2, extraKey: "Return unit price", extraVal: "10000"));

        Assert.True(r.Ok);
        Assert.Equal(1, r.Added);
        var o = All.Single().Order;
        Assert.Equal("TikTok", o.Platform);                           // nhãn sàn ngắn
        Assert.Equal("Chưa về", o.Status);
        Assert.Equal("RT111111", o.ReturnTrack);
        Assert.Equal(20000m, o.Price);
        Assert.Equal("23/11/2026", o.ReturnDate);                     // mặc định chỉ khi tạo dòng mới
        Assert.Equal(20000, o.Import.PriceModes["tong_gia_tri"]);
        Assert.Equal("unitReturn", o.Import.ImportModeSlots["tong_gia_tri"]);
        Assert.Equal(20000, o.Items.Single().ImportPrices!["unitReturn"]);
        Assert.Contains(OrderRepository.LoadActive(Db), x => x.ReturnTrack == "RT111111");
    }

    [Fact]
    public void Importing_the_same_file_twice_changes_nothing_the_second_time()
    {
        AppDatabase.Initialize(Db);
        Import(Row("O1", "C1", "RT111111", qty: 2));
        var second = Import(Row("O1", "C1", "RT111111", qty: 2));

        Assert.Equal(0, second.Added);
        Assert.Equal(1, Count("SELECT COUNT(*) FROM orders"));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM order_items"));
        Assert.Equal(2, All.Single().Order.Items.Single().Qty);        // không cộng dồn thành 4
    }

    [Fact]
    public void Existing_row_from_legacy_is_updated_and_warehouse_fields_are_untouched()
    {
        SeedLegacy(new object[]
        {
            new { platform = "TikTok Shop", orderType = "Trả hàng/Hoàn tiền", orderId = "O1", returnOrderId = "C1", track = "RT111111", returnTrack = "RT111111",
                  status = "Hàng đã trả", recvDate = "10/11/2026 09:00:00", photoId = "p_X", userNote = "đã kiểm", isWatchlist = true, watchlistReason = "hay hoàn",
                  price = 0, platformStatus = "Chờ xử lý", items = new[] { new { product = "Shirt", color = "", size = "", qty = 1 } } },
        });

        var r = Import(Row("O1", "C1", "RT111111", qty: 3, extraKey: "Return Reason", extraVal: "Poor quality"));

        Assert.Equal(0, r.Added);
        Assert.Equal(1, r.Updated);
        Assert.Equal(1, Count("SELECT COUNT(*) FROM orders"));
        var o = All.Single().Order;
        Assert.Equal("Chất lượng kém", o.Note);                        // dữ liệu sàn được cập nhật
        Assert.Equal("Đang xử lý", o.PlatformStatus);
        Assert.Equal(3, o.Items.Single().Qty);
        Assert.Equal("Hàng đã trả", o.Status);                         // trường kho giữ nguyên
        Assert.Equal("10/11/2026 09:00:00", o.RecvDate);
        Assert.Equal("p_X", o.PhotoId);
        Assert.Equal("đã kiểm", o.UserNote);
        Assert.True(o.IsWatchlist);
    }

    [Fact]
    public void Supplement_keeps_products_missing_from_the_file()
    {
        SeedLegacy(new object[]
        {
            new { platform = "TikTok Shop", orderId = "O1", returnOrderId = "C1", track = "RT111111", returnTrack = "RT111111", status = "Chưa về",
                  items = new[] { new { product = "Shirt", color = "", size = "", qty = 1 }, new { product = "Pants", color = "", size = "", qty = 2 } } },
        });

        Import(Row("O1", "C1", "RT111111", qty: 3));

        var items = All.Single().Order.Items;
        Assert.Equal(2, items.Count);
        Assert.Equal(3, items.Single(i => i.Product == "Shirt").Qty);
        Assert.Equal(2, items.Single(i => i.Product == "Pants").Qty);
    }

    [Fact]
    public void Deleted_order_is_not_recreated_and_archived_order_is_only_enriched()
    {
        SeedLegacy(
            main: Array.Empty<object>(),
            archived: new object[]
            {
                new { platform = "TikTok Shop", orderId = "OA", track = "RT222222", status = "Hàng đã trả", recvDate = "10/11/2026 09:00:00", price = 0,
                      items = new[] { new { product = "", color = "", size = "", qty = 1 } } },
            },
            deleted: new object[] { new { platform = "TikTok Shop", orderId = "OD", returnOrderId = "CD", track = "RT333333", returnTrack = "RT333333", items = Array.Empty<object>() } });

        var r = Import(
            Row("OD", "CD", "RT333333"),
            Row("OA", "CA", "RT222222", qty: 2, extraKey: "Return unit price", extraVal: "5000"));

        Assert.Equal(1, r.SkippedDeleted);
        Assert.Equal(1, r.EnrichedArchived);
        Assert.Equal(0, r.Added);
        Assert.Equal(2, Count("SELECT COUNT(*) FROM orders"));          // không có dòng mới
        var arch = All.Single(x => x.Order.Track == "RT222222");
        Assert.True(arch.IsArchived);                                   // vẫn ở kho lưu trữ
        Assert.Equal("CA", arch.Order.ReturnOrderId);                   // được bổ sung
        Assert.Equal(10000m, arch.Order.Price);
        Assert.Equal("Hàng đã trả", arch.Order.Status);                 // không đụng trạng thái nhận
        Assert.Equal("10/11/2026 09:00:00", arch.Order.RecvDate);
        Assert.True(All.Single(x => x.Order.Track == "RT333333").IsDeleted);
    }

    [Fact]
    public void Same_order_new_claim_is_a_separate_row_and_siblings_stay_separate()
    {
        SeedLegacy(new object[]
        {
            new { platform = "TikTok Shop", orderId = "O1", returnOrderId = "C1", track = "RT111111", returnTrack = "RT111111", status = "Chưa về", items = new[] { new { product = "Shirt", color = "", size = "", qty = 1 } } },
        });

        var r = Import(Row("O1", "C2", "RT222222", product: "Pants"));

        Assert.Equal(1, r.Added);
        Assert.Equal(2, All.Count(x => x.Order.OrderId == "O1"));
        Assert.Equal("Shirt", All.Single(x => x.Order.ReturnOrderId == "C1").Order.Items.Single().Product);
    }

    [Fact]
    public void Ambiguous_row_is_reported_and_nothing_is_written_for_it()
    {
        SeedLegacy(new object[]
        {
            new { platform = "TikTok Shop", orderId = "O1", track = "AAA11111", trackingCodes = new[] { "SHARED11" }, status = "Chưa về", items = new[] { new { product = "Shirt", color = "", size = "", qty = 1 } } },
            new { platform = "TikTok Shop", orderId = "O1", track = "BBB22222", trackingCodes = new[] { "SHARED11" }, status = "Chưa về", items = new[] { new { product = "Shirt", color = "", size = "", qty = 1 } } },
        });
        var before = Count("SELECT COUNT(*) FROM orders");

        var r = Import(new Dictionary<string, string> { ["Order ID"] = "O1", ["Tracking ID"] = "SHARED11", ["Product Name"] = "Pants", ["Order Status"] = "Refunded", ["Quantity"] = "1" });

        Assert.Single(r.Ambiguous);
        Assert.Equal(before, Count("SELECT COUNT(*) FROM orders"));
        Assert.All(All, o => Assert.Equal("Shirt", o.Order.Items.Single().Product));
    }

    [Fact]
    public void Ordinary_shipping_rows_are_filtered_out_by_default()
    {
        AppDatabase.Initialize(Db);
        var r = Import(new Dictionary<string, string> { ["Order ID"] = "O1", ["Tracking ID"] = "SPXVN0123456789", ["Product Name"] = "Shirt", ["Quantity"] = "1", ["Order Status"] = "Delivered" });
        Assert.Equal(0, r.Added);
        Assert.Equal(1, r.RowsSkipped);
    }

    [Fact]
    public void Summary_goes_to_audit_log_and_missing_database_fails_cleanly()
    {
        AppDatabase.Initialize(Db);
        Import(Row("O1", "C1", "RT111111"));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM audit_log WHERE type='import'"));

        var none = Path.Combine(_dir, "none.db");
        var r = TikTokImportService.Apply(none, new List<Dictionary<string, string>> { Row("O1", "C1", "RT111111") }, Opt(), ImportProductMode.Supplement, Now);
        Assert.False(r.Ok);
        Assert.False(File.Exists(none));
    }

    [Fact]
    public void Legacy_items_keep_seller_sku_and_import_prices_so_they_merge_like_the_old_app()
    {
        SeedLegacy(new object[]
        {
            new { platform = "TikTok Shop", orderId = "O1", returnOrderId = "C1", track = "RT111111", returnTrack = "RT111111", status = "Chưa về",
                  priceModes = new { tong_gia_tri = 20000, sau_chiet_khau = 18000, hasOrderLevelZ = false },
                  importSelectedPriceMode = "tong_gia_tri", priceHasValue = true,
                  items = new[] { new { product = "Shirt", color = "Đen", size = "M", qty = 2, sellerSku = "SKU-A", importPrices = new { unitReturn = 20000 } } } },
        });

        var o = All.Single().Order;
        Assert.Equal("SKU-A", o.Items.Single().Sku);
        Assert.Equal(20000, o.Items.Single().ImportPrices!["unitReturn"]);
        Assert.Equal(18000, o.Import.PriceModes["sau_chiet_khau"]);
        Assert.False(o.Import.PriceModeFlags["hasOrderLevelZ"]);
        Assert.Equal("tong_gia_tri", o.Import.ImportSelectedPriceMode);
        Assert.True(o.Import.PriceHasValue);
    }

    [Fact]
    public void Schema_v2_database_is_upgraded_in_place_without_losing_rows()
    {
        // DB kiểu phiên bản 2: order_items chưa có cột import_prices
        AppDatabase.Initialize(Db);
        using (var c = new SqliteConnection($"Data Source={Db};Pooling=False"))
        {
            c.Open();
            foreach (var sql in new[]
            {
                "DROP TABLE order_items",
                "CREATE TABLE order_items (source_key TEXT NOT NULL, line_no INTEGER NOT NULL, product TEXT NOT NULL, color TEXT NOT NULL, size TEXT NOT NULL, qty INTEGER NOT NULL, sku TEXT, sku_id TEXT, PRIMARY KEY (source_key, line_no))",
                "INSERT INTO order_items VALUES ('k',0,'Áo','','',1,NULL,NULL)",
                "PRAGMA user_version = 2",
            })
            { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        }
        

        AppDatabase.Initialize(Db);

        Assert.Equal(1, Count("SELECT COUNT(*) FROM order_items"));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM pragma_table_info('order_items') WHERE name='import_prices'"));
        Assert.Equal(AppDatabase.SchemaVersion, Count("PRAGMA user_version"));
    }
}
