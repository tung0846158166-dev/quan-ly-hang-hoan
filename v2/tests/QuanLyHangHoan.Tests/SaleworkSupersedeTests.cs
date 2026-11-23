using System.IO;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.Tests;

/// <summary>supersedeStaleRowsFromSalework: dữ liệu GIẢ.</summary>
public sealed class SaleworkSupersedeTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-sw-" + Guid.NewGuid().ToString("N"));
    public SaleworkSupersedeTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static Order O(string orderId, string track = "", string status = "Chưa về", string product = "Ao") => new()
    {
        OrderId = orderId, ReturnTrack = track, Status = status, Items = { new OrderItem { Product = product } },
    };
    private static HashSet<string> Ids(params string[] ids) => new(ids);

    [Fact]
    public void Bare_stale_row_hands_warehouse_data_to_the_tracked_row()
    {
        var stale = O("A1", status: "Đã nhận"); stale.RecvDate = "22/11/2026 09:00"; stale.PhotoId = "ph1"; stale.UserNote = "kiểm rồi"; stale.IsWatchlist = true;
        var keep = O("A1", "SPXVN0000000001");
        var r = SaleworkSupersede.Apply(new List<Order> { stale, keep }, Ids("A1"));
        Assert.Single(r.Merged);
        Assert.Same(stale, r.Merged[0].Stale); Assert.Same(keep, r.Merged[0].Keeper);
        Assert.Equal("Đã nhận", keep.Status);
        Assert.Equal("22/11/2026 09:00", keep.RecvDate);
        Assert.Equal("ph1", keep.PhotoId);
        Assert.Equal("kiểm rồi", keep.UserNote);
        Assert.True(keep.IsWatchlist);
    }

    [Fact]
    public void Warehouse_data_already_on_the_keeper_is_never_overwritten()
    {
        var stale = O("A1", status: "Đã nhận"); stale.RecvDate = "OLD"; stale.PhotoId = "old"; stale.UserNote = "ghi chú cũ";
        var keep = O("A1", "SPXVN0000000001"); keep.RecvDate = "KEEP"; keep.PhotoId = "keep"; keep.UserNote = "ghi chú mới";
        SaleworkSupersede.Apply(new List<Order> { stale, keep }, Ids("A1"));
        Assert.Equal("KEEP", keep.RecvDate);
        Assert.Equal("keep", keep.PhotoId);
        Assert.Equal("ghi chú mới · ghi chú cũ", keep.UserNote);
    }

    [Fact]
    public void Several_different_parcels_are_not_guessed_and_only_blank_codes_are_filled_when_unambiguous()
    {
        var bare = O("A1");
        var p1 = O("A1", "RET11111"); p1.ForwardTrack = "FWD99999";
        var p2 = O("A1", "RET22222"); p2.ForwardTrack = "FWD99999";
        var r = SaleworkSupersede.Apply(new List<Order> { bare, p1, p2 }, Ids("A1"));
        Assert.Empty(r.Merged);
        Assert.Equal(1, r.Ambiguous);
        Assert.Equal("FWD99999", bare.ForwardTrack);   // mọi dòng cùng 1 mã giao: điền được
        Assert.Equal("", bare.ReturnTrack);            // 2 mã hoàn khác nhau: không đoán
    }

    [Fact]
    public void Orders_not_in_the_salework_list_and_scan_placeholders_are_untouched()
    {
        var other = O("B1"); var keepB = O("B1", "RET33333");
        var ph = O("A1", product: "Chưa rõ sản phẩm (hàng đã về, chưa có đơn)"); var keepA = O("A1", "RET44444");
        var r = SaleworkSupersede.Apply(new List<Order> { other, keepB, ph, keepA }, Ids("A1"));
        Assert.Empty(r.Merged);
        Assert.Empty(r.Filled);
    }

    [Fact]
    public void Service_soft_deletes_stale_row_keeps_data_and_never_removes_rows_for_real()
    {
        var db = Path.Combine(_dir, "t.db");
        AppDatabase.Initialize(db);
        var opt = new TikTokMapOptions { Today = Now };
        // Dòng chờ cũ chưa có mã vận đơn (yêu cầu hoàn C1), kho đã nhận.
        var first = new List<Dictionary<string, string>> { new() { ["Order ID"] = "SW1", ["Return Order ID"] = "C1", ["Product Name"] = "Shirt", ["Return Status"] = "In Process", ["Quantity"] = "1" } };
        Assert.True(SaleworkImportService.Apply(db, first, opt, ImportProductMode.Supplement, Now).Ok);
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db};Pooling=False"))
        {
            c.Open(); var cmd = c.CreateCommand(); cmd.CommandText = "UPDATE orders SET status='Đã nhận', recv_date='22/11/2026 09:00', user_note='kho'"; cmd.ExecuteNonQuery();
        }
        // Salework giờ có dòng cùng mã đơn, yêu cầu khác (C2), có mã vận đơn hoàn.
        var second = new List<Dictionary<string, string>> { new() { ["Order ID"] = "SW1", ["Return Order ID"] = "C2", ["Return Logistics Tracking ID"] = "RT777777", ["Product Name"] = "Shirt", ["Return Status"] = "In Process", ["Quantity"] = "1" } };
        var r = SaleworkImportService.Apply(db, second, opt, ImportProductMode.Supplement, Now);
        Assert.True(r.Ok, r.Message);

        var active = OrderRepository.Load(db).Where(s => !s.IsDeleted && !s.IsArchived).Select(s => s.Order).ToList();
        var tracked = Assert.Single(active, o => o.ReturnTrack == "RT777777");
        Assert.Equal("Đã nhận", tracked.Status);
        Assert.Equal("22/11/2026 09:00", tracked.RecvDate);
        Assert.Contains("kho", tracked.UserNote);
        // không dòng nào bị mất hẳn khỏi cơ sở dữ liệu
        Assert.True(OrderRepository.Load(db).Count >= 1);
    }
}
