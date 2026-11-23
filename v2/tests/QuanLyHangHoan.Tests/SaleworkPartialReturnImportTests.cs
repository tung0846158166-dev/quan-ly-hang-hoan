using System.IO;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.Tests;

/// <summary>
/// Từ file Salework thật của chủ dự án: đơn "Trả hàng hoàn tiền - Một phần" (trạng thái THÀNH CÔNG, nhiều sản phẩm) trước đây bị bỏ.
/// Dữ liệu GIẢ, cùng tên cột với file thật.
/// </summary>
public sealed class SaleworkPartialReturnImportTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 24, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-swp-" + Guid.NewGuid().ToString("N"));
    public SaleworkPartialReturnImportTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static Dictionary<string, string> Row(string orderId, string status, string kind, string track, string product, string returnCode = "") => new()
    {
        ["Mã đơn hàng"] = orderId, ["Trạng thái"] = status, ["Ngày tạo đơn"] = "2026-11-10 08:00:00", ["Mã sản phẩm"] = "SKU-" + product,
        ["Số lượng"] = orderId.Length == 0 ? "1" : "1", ["Giá Salework"] = "100000", ["Tên sản phẩm trên sàn"] = product + " - Đen, L",
        ["Số lượng_1"] = "1", ["Giá sàn TMĐT"] = "120000", ["Tổng COD"] = orderId.Length == 0 ? "" : "120000", ["Lý do hoàn"] = orderId.Length == 0 ? "" : "Change of mind",
        ["Mã vận đơn"] = track, ["Đơn vị vận chuyển"] = orderId.Length == 0 ? "" : "J&T Express", ["Cập nhật lúc"] = orderId.Length == 0 ? "" : "2026-11-20 10:00:00",
        ["Shop"] = orderId.Length == 0 ? "" : "Shop Giả", ["Tổng giá trị"] = orderId.Length == 0 ? "" : "120000", ["Mã trả hàng"] = returnCode,
        ["Loại đơn hoàn"] = kind,
    };

    private static List<Dictionary<string, string>> Sample() => new()
    {
        Row("ORD-A", "THẤT THU", "Trả hàng hoàn tiền", "840000000001", "AoA", "RET-A"),
        Row("ORD-B", "THÀNH CÔNG", "Trả hàng hoàn tiền - Một phần", "840000000002", "AoB1", "RET-B"),
        Row("", "", "", "", "QuanB2"),                                    // dòng tiếp nối: sản phẩm thứ 2 của ORD-B
        Row("ORD-C", "THÀNH CÔNG", "", "840000000003", "AoC"),             // đơn giao thành công bình thường: vẫn KHÔNG nhận
    };

    private static List<string> Q(string db, string sql)
    {
        using var c = new SqliteConnection($"Data Source={db};Pooling=False"); c.Open();
        using var m = c.CreateCommand(); m.CommandText = sql;
        using var r = m.ExecuteReader();
        var l = new List<string>(); while (r.Read()) l.Add(string.Join("|", Enumerable.Range(0, r.FieldCount).Select(i => r.GetValue(i).ToString())));
        return l;
    }

    [Fact]
    public void Salework_route_accepts_the_partial_return_order_keeps_all_products_flags_it_and_still_skips_plain_success_rows()
    {
        var db = Path.Combine(_dir, "a.db"); AppDatabase.Initialize(db);
        var r = SaleworkImportService.Apply(db, Sample(), new TikTokMapOptions(), ImportProductMode.Supplement, Now);
        Assert.True(r.Ok);
        Assert.Equal(2, r.Import!.Added);                                                     // ORD-A và ORD-B; ORD-C không nhận
        Assert.Equal(new[] { "ORD-A|THẤT THOÁT|0", "ORD-B|Trả hàng/Hoàn tiền|1" },
            Q(db, "SELECT order_id, order_type, is_partial_return FROM orders ORDER BY order_id"));
        Assert.Equal(new[] { "AoB1|1", "QuanB2|1" },                                          // giữ ĐỦ 2 sản phẩm
            Q(db, "SELECT product, qty FROM order_items WHERE source_key=(SELECT source_key FROM orders WHERE order_id='ORD-B') ORDER BY line_no"));
    }

    [Fact]
    public void Importing_the_same_rows_again_does_not_duplicate_the_partial_order()
    {
        var db = Path.Combine(_dir, "b.db"); AppDatabase.Initialize(db);
        SaleworkImportService.Apply(db, Sample(), new TikTokMapOptions(), ImportProductMode.Supplement, Now);
        var r2 = SaleworkImportService.Apply(db, Sample(), new TikTokMapOptions(), ImportProductMode.Supplement, Now);
        Assert.True(r2.Ok);
        Assert.Equal(0, r2.Import!.Added);
        Assert.Equal(new[] { "2" }, Q(db, "SELECT COUNT(*) FROM orders"));
        Assert.Equal(new[] { "2" }, Q(db, "SELECT COUNT(*) FROM order_items WHERE source_key=(SELECT source_key FROM orders WHERE order_id='ORD-B')"));
    }

    [Fact]
    public void A_salework_shaped_file_in_manual_file_import_is_treated_like_the_salework_route_not_as_shopee()
    {
        Assert.Equal(FileKind.TikTok, FileImportService.Detect(Sample()));
        var plainShopee = new List<Dictionary<string, string>> { new() { ["Mã đơn hàng"] = "X1", ["Mã vận đơn"] = "SPX1", ["Trạng thái"] = "Hoàn thành" } };
        Assert.Equal(FileKind.Shopee, FileImportService.Detect(plainShopee));              // file Shopee thường không đổi
    }

    [Fact]
    public void Salework_rows_without_the_kind_column_behave_exactly_as_before()
    {
        var rows = Sample().Select(x => { var d = new Dictionary<string, string>(x); d.Remove("Loại đơn hoàn"); return d; }).ToList();
        var db = Path.Combine(_dir, "c.db"); AppDatabase.Initialize(db);
        var r = SaleworkImportService.Apply(db, rows, new TikTokMapOptions(), ImportProductMode.Supplement, Now);
        Assert.Equal(1, r.Import!.Added);                                                     // chỉ THẤT THU như app cũ
        Assert.Equal(new[] { "ORD-A" }, Q(db, "SELECT order_id FROM orders"));
    }
}
