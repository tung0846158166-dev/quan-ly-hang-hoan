using System.IO;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.Tests;

/// <summary>Nạp file Shopee vào SQLite (ánh xạ -> gộp -> lưu). Dữ liệu GIẢ.</summary>
public sealed class ShopeeImportServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-sp-" + Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_dir, "t.db");

    public ShopeeImportServiceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static Dictionary<string, string> Row(string order, string claim, string track, string status = "Chờ xử lý", string money = "199000") => new()
    {
        ["Mã đơn hàng"] = order, ["Mã số khiếu nại"] = claim, ["Mã vận đơn"] = track, ["Tên sản phẩm"] = "Ao thun", ["Tên phân loại hàng"] = "Den,M",
        ["Số lượng"] = "1", ["Trạng thái Trả hàng/Hoàn tiền"] = status, ["Tổng số tiền hoàn trả"] = money, ["Người bán cần phản hồi trước"] = "25/11/2026 23:59",
    };

    private TikTokImportResult Import(params Dictionary<string, string>[] rows) =>
        TikTokImportService.ApplyGroups(Db, new[] { (IReadOnlyList<Dictionary<string, string>>)rows.ToList() },
            new TikTokMapOptions { Today = Now }, ImportProductMode.Supplement, Now, ImportSource.Shopee);

    private void Exec(string sql)
    {
        using var c = new SqliteConnection($"Data Source={Db};Pooling=False"); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
    }

    [Fact]
    public void New_row_is_saved_with_shopee_defaults_deadline_and_raw_details()
    {
        AppDatabase.Initialize(Db);
        var r = Import(Row("SP1", "RR1", "SPXVN0000000001"));
        Assert.True(r.Ok);
        Assert.Equal(1, r.Added);
        var o = OrderRepository.Load(Db).Single().Order;
        Assert.Equal("Shopee", o.Platform);
        Assert.Equal("Chưa trả", o.Status);
        Assert.Equal(199000m, o.Price);
        Assert.Equal("25/11/2026 23:59", o.Deadline);
        Assert.Equal("order", o.Import.ImportPriceBasis);
        Assert.Equal("RR1", o.Import.RawDetails["claimId"]);
        Assert.Equal("199000", o.Import.RawDetails["refundAmount"]);
    }

    [Fact]
    public void Reimport_updates_platform_fields_but_never_touches_warehouse_fields()
    {
        AppDatabase.Initialize(Db);
        Import(Row("SP1", "RR1", "SPXVN0000000001"));
        Exec("UPDATE orders SET status='Đã nhận', recv_date='22/11/2026', user_note='kho ghi chú'");
        var r = Import(Row("SP1", "RR1", "SPXVN0000000001", status: "Đã hoàn tiền"));
        Assert.Equal(0, r.Added);
        Assert.Equal(1, r.Updated);
        var o = OrderRepository.Load(Db).Single().Order;
        Assert.Equal("Đã hoàn tiền", o.PlatformStatus);
        Assert.Equal("Đã nhận", o.Status);
        Assert.Equal("22/11/2026", o.RecvDate);
        Assert.Equal("kho ghi chú", o.UserNote);
        Assert.Single(OrderRepository.Load(Db));
    }

    [Fact]
    public void Two_parcels_of_one_order_stay_two_rows_and_ambiguous_share_is_not_guessed()
    {
        AppDatabase.Initialize(Db);
        var r = Import(Row("SP1", "RR1A", "SPXVN0000000011"), Row("SP1", "RR1B", "SPXVN0000000012"));
        Assert.Equal(2, r.Added);
        Assert.Equal(2, OrderRepository.Load(Db).Count);
        var again = Import(Row("SP1", "RR1B", "SPXVN0000000012"));
        Assert.Equal(0, again.Added);
        Assert.Equal(2, OrderRepository.Load(Db).Count);
    }
}
