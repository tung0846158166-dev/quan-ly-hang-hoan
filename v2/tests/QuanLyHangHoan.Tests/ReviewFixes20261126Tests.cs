using System.IO;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.Tests;

/// <summary>Rà lỗi 26/11/2026 (dữ liệu GIẢ): nhập Salework không được nhận kiện bừa; TikTok không điền mã khi nhiều kiện chưa mã; sao lưu trước khi nhập.</summary>
public sealed class ReviewFixes20261126Tests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 26, 15, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-rv-" + Guid.NewGuid().ToString("N"));
    public ReviewFixes20261126Tests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Supersede_never_copies_a_received_status_without_a_receive_date()
    {
        // chủ dự án: "sau khi nhập file có đơn tự chuyển sang Hàng đã trả và không có thời gian nhận tại kho"
        var stale = new Order { OrderId = "A1", Status = ReceiveStatus.Received, Items = { new OrderItem { Product = "Ao" } } };
        var keep = new Order { OrderId = "A1", ReturnTrack = "SPXVN0000000001", Status = "Chưa về", Items = { new OrderItem { Product = "Ao" } } };
        SaleworkSupersede.Apply(new List<Order> { stale, keep }, new HashSet<string> { "A1" });
        Assert.Equal("Chưa về", keep.Status);
        Assert.Equal("", keep.RecvDate);
    }

    private static Order Trackless(string id, string orderId) => new()
    {
        Id = id, OrderId = orderId, Platform = "TikTok", OrderType = "Trả hàng", Status = ReceiveStatus.NotYetArrived,
        Items = new() { new OrderItem { Product = "Áo", Qty = 1 } },
    };

    [Fact]
    public void Tiktok_fill_is_ambiguous_when_two_trackless_parcels_share_the_order()
    {
        var main = new List<Order> { Trackless("a", "TT1"), Trackless("b", "TT1") };
        var incoming = new ImportedParcel
        {
            Platform = "TikTok", OrderId = "TT1", Track = "JT0000001", ReturnTrack = "JT0000001", OrderType = "Trả hàng",
            TrackingCodes = new() { "JT0000001" }, Items = new() { new ImportedItem { Product = "Áo", Qty = 1 } },
        };
        var r = TikTokMerger.Apply(main, new List<OtherEntry>(), new[] { incoming }, ImportProductMode.Supplement, Now);
        Assert.Single(r.Ambiguous);
        Assert.All(main, o => Assert.Equal("", o.Track));
        Assert.Equal(2, main.Count);
    }

    // chủ dự án 28/11/2026 (ảnh 31): Sheet gửi dòng Shopee "Chưa gửi" không có mã nào cho đơn ĐÃ có kiện -> trước đây tạo bản trùng "Chưa có MVĐ"
    [Fact]
    public void Codeless_shopee_row_of_an_order_that_already_has_a_parcel_does_not_create_a_duplicate()
    {
        var existing = new Order { Id = "a", OrderId = "SPO1", Platform = "Shopee", Track = "SPXVN0000000001B", ForwardTrack = "SPXVN0000000001B", PlatformStatus = "Đang về", Items = { new OrderItem { Product = "Áo", Qty = 1 } } };
        var main = new List<Order> { existing };
        ImportedParcel Codeless(string oid) => new() { Platform = "Shopee", OrderId = oid, OrderType = "Trả hàng/Hoàn tiền", PlatformStatus = "Chưa gửi", Items = new() { new ImportedItem { Product = "Áo", Qty = 1 } } };
        var r = TikTokMerger.Apply(main, new List<OtherEntry>(), new[] { Codeless("SPO1") }, ImportProductMode.Supplement, Now, ShopeeMapper.ForNewRow);
        Assert.Single(main);
        Assert.Empty(r.Added);
        Assert.Equal(1, r.SkippedCodeless);
        Assert.Equal("Đang về", existing.PlatformStatus);                  // không bị dòng "Chưa gửi" ghi đè
        // đơn chỉ có kiện đã lưu trữ: cũng không tạo bản trùng
        var arch = new List<OtherEntry> { new(new Order { Id = "z", OrderId = "SPO2", Platform = "Shopee", Track = "SPXVN0000000002B", Items = { new OrderItem { Product = "Áo", Qty = 1 } } }, OtherWhere.Archive) };
        var r2 = TikTokMerger.Apply(main, arch, new[] { Codeless("SPO2") }, ImportProductMode.Supplement, Now, ShopeeMapper.ForNewRow);
        Assert.Empty(r2.Added);
        // đơn CHƯA có kiện nào: vẫn tạo dòng chờ như cũ; sau đó có mã thì điền vào chính dòng đó
        var r3 = TikTokMerger.Apply(main, new List<OtherEntry>(), new[] { Codeless("SPO3") }, ImportProductMode.Supplement, Now, ShopeeMapper.ForNewRow);
        Assert.Single(r3.Added);
        var tracked = Codeless("SPO3"); tracked.Track = "SPXVN0000000003"; tracked.ReturnTrack = "SPXVN0000000003"; tracked.TrackingCodes = new() { "SPXVN0000000003" };
        var r4 = TikTokMerger.Apply(main, new List<OtherEntry>(), new[] { tracked }, ImportProductMode.Supplement, Now, ShopeeMapper.ForNewRow);
        Assert.Empty(r4.Added);
        Assert.Equal(2, main.Count);                                       // SPO1 + SPO3 (đã điền mã), không thêm dòng
    }

    [Theory]
    // quyết định chủ dự án 26/11/2026: đơn "Hủy sau chuẩn bị" có hàng về -> có mã vận đơn thì chờ nhận (máy tính phải LƯU trạng thái này để đẩy lên cloud)
    [InlineData("Không cần nhận", "Đơn hủy", true, "Chưa trả")]
    // mọi trường hợp khác: nhập không đổi trạng thái kho
    [InlineData("Bị mất", "Đơn hủy", true, "Bị mất")]
    [InlineData("Không cần nhận", "Trả hàng/Hoàn tiền", true, "Không cần nhận")]
    [InlineData("Không cần nhận", "Đơn hủy", false, "Không cần nhận")]
    public void Import_update_saves_status_only_for_a_cancelled_order_that_now_has_a_tracking_code(string localStatus, string orderType, bool hasCode, string expected)
    {
        var db = Path.Combine(_dir, "s.db"); AppDatabase.Initialize(db);
        var o = new Order { Id = "h1", OrderId = "X1", Platform = "Shopee", OrderType = orderType, Status = localStatus, Items = { new OrderItem { Product = "Ao", Qty = 1 } } };
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db};Pooling=False"))
        {
            c.Open();
            using (var tx = c.BeginTransaction()) { OrderStore.Insert(c, tx, o, "k1"); tx.Commit(); }
            if (hasCode) { o.Track = "SPXH1"; o.TrackingCodes = new() { "SPXH1" }; }
            o.Status = "Chưa trả";   // ParcelMerge đã quyết định trong bộ nhớ
            using (var tx = c.BeginTransaction()) { OrderStore.UpdateImportedFields(c, tx, o); tx.Commit(); }
            using var q = c.CreateCommand(); q.CommandText = "SELECT status FROM orders WHERE id='h1'";
            Assert.Equal(expected, q.ExecuteScalar() as string);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void Ensure_recent_backup_skips_when_a_fresh_one_exists_and_cleans_old_tmp_files()
    {
        var db = Path.Combine(_dir, "data.db");
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db};Pooling=False")) { c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "CREATE TABLE t(x)"; cmd.ExecuteNonQuery(); }
        var bk = Path.Combine(_dir, "backups");
        Assert.True(BackupService.EnsureRecent(db, bk, TimeSpan.FromMinutes(30), 7, Now).Ok);
        Assert.Single(BackupService.List(bk));
        Assert.True(BackupService.EnsureRecent(db, bk, TimeSpan.FromMinutes(30), 7, Now.AddMinutes(10)).Ok);   // vừa có bản mới: không tạo thêm
        Assert.Single(BackupService.List(bk));
        BackupService.EnsureRecent(db, bk, TimeSpan.FromMinutes(30), 7, Now.AddMinutes(45));
        Assert.Equal(2, BackupService.List(bk).Count);

        var tmp = Path.Combine(bk, "data-20261120-010101.db.tmp");
        File.WriteAllText(tmp, "dở");
        File.SetLastWriteTime(tmp, Now.AddDays(-1));
        BackupService.Prune(bk, 7, Now);
        Assert.False(File.Exists(tmp));
    }
}
