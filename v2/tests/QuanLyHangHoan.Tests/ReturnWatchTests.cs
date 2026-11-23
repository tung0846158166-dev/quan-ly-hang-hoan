using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;
using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.Tests;

/// <summary>Bảng Hàng Hoàn Về (port v1.9.72): tự vào bảng khi "Đã đồng ý", 4 chặng, ở lại, bỏ/khôi phục, gửi điện thoại. Dữ liệu GIẢ.</summary>
public sealed class ReturnWatchTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-rw-" + Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_dir, "t.db");

    public ReturnWatchTests()
    {
        Directory.CreateDirectory(_dir);
        object Item() => new { product = "Áo", color = "Đen", size = "M", qty = 1 };
        File.WriteAllText(Path.Combine(_dir, "du_lieu_hang_hoan.json"), JsonSerializer.Serialize(new object[]
        {
            // chặng 1: vừa đồng ý, chưa có mã hoàn, chưa về
            new { platform = "Shopee", orderType = "Trả hàng/Hoàn tiền", orderId = "O1", track = "SPXA1", status = "Chưa trả", platformStatus = "Đã đồng ý", items = new[] { Item() } },
            // chặng 2: có mã vận đơn hoàn
            new { platform = "Shopee", orderType = "Trả hàng/Hoàn tiền", orderId = "O2", track = "SPXB1", returnTrack = "RTB1", status = "Chưa trả", platformStatus = "Đã đồng ý", items = new[] { Item() } },
            // chặng 3: đã có giờ hàng về
            new { platform = "TikTok", orderType = "Trả hàng/Hoàn tiền", orderId = "O3", track = "TKC1", status = "Chưa về", platformStatus = "Agreed", returnedAt = "22/11/2026 09:00:00", items = new[] { Item() } },
            // chặng 4: đã nhận kho
            new { platform = "Shopee", orderType = "Trả hàng/Hoàn tiền", orderId = "O4", track = "SPXD1", status = "Hàng đã trả", platformStatus = "Đã đồng ý", items = new[] { Item() } },
            // KHÔNG vào bảng: không phải "đồng ý"
            new { platform = "Shopee", orderType = "Trả hàng/Hoàn tiền", orderId = "O5", track = "SPXE1", status = "Chưa trả", platformStatus = "Chờ xử lý", items = new[] { Item() } },
            // KHÔNG vào bảng: không cần nhận
            new { platform = "Shopee", orderType = "Trả hàng/Hoàn tiền", orderId = "O6", track = "SPXF1", status = "Không cần nhận", platformStatus = "Đã đồng ý", items = new[] { Item() } },
            // đơn app cũ đã đánh dấu: rwSince có, vẫn giữ giờ cũ; và đơn app cũ đã bỏ (rwOff) không tự quay lại
            new { platform = "Shopee", orderType = "Trả hàng/Hoàn tiền", orderId = "O7", track = "SPXG1", status = "Chưa trả", platformStatus = "Đang vận chuyển về", rwSince = 1700000000000L, items = new[] { Item() } },
            new { platform = "Shopee", orderType = "Trả hàng/Hoàn tiền", orderId = "O8", track = "SPXH1", status = "Chưa trả", platformStatus = "Đã đồng ý", rwSince = 1700000000000L, rwOff = true, items = new[] { Item() } },
        }));
        LegacyImporter.Import(_dir, Db);
        AppDatabase.Initialize(Db);
    }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string IdOf(string track) => OrderRepository.Load(Db).Single(o => o.Order.Track == track).Order.Id;
    private long Count(string sql)
    {
        using var c = new SqliteConnection($"Data Source={Db};Pooling=False"); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public void Agreed_orders_enter_the_board_with_correct_stages_and_others_do_not()
    {
        var list = ReturnWatchService.Load(Db, Now);
        var stage = list.ToDictionary(e => e.Stored.Order.Track, e => e.Stage);
        Assert.Equal(1, stage["SPXA1"]);
        Assert.Equal(2, stage["SPXB1"]);
        Assert.Equal(3, stage["TKC1"]);
        Assert.DoesNotContain("SPXD1", stage.Keys);              // đã nhận từ trước khi thấy lần đầu: không vào bảng (như app cũ)
        Assert.Equal(2, stage["SPXG1"]);                       // "Đang vận chuyển về" (từ app cũ, đã có rwSince)
        Assert.DoesNotContain("SPXE1", stage.Keys);              // chưa "đồng ý"
        Assert.DoesNotContain("SPXF1", stage.Keys);              // không cần nhận
        Assert.DoesNotContain("SPXH1", stage.Keys);              // app cũ đã bỏ khỏi bảng (rwOff): không tự quay lại
    }

    [Fact]
    public void Tiktok_orders_waiting_for_the_goods_also_enter_like_legacy_1_9_76_but_shopee_ones_do_not()
    {
        using (var c = new SqliteConnection($"Data Source={Db};Pooling=False"))
        {
            c.Open();
            foreach (var sql in new[]
            {
                "UPDATE orders SET platform_status='Chờ khách gửi hàng' WHERE track='TKC1'",          // TikTok, đã có giờ về => vẫn chặng 3
                "UPDATE orders SET platform_status='Đang vận chuyển về' WHERE track='SPXE1'",         // Shopee không nằm trong luật TikTok nhưng 'Đang...' không phải 'Đã đồng ý'
                "UPDATE orders SET platform='TikTok', platform_status='Chờ khách gửi hàng' WHERE track='SPXF1'",
                "UPDATE orders SET status='Chưa về' WHERE track='SPXF1'",
            }) { var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        }
        var tracks = ReturnWatchService.Load(Db, Now).ToDictionary(e => e.Stored.Order.Track, e => e.Stage);
        Assert.Equal(3, tracks["TKC1"]);                    // TikTok vẫn vào
        Assert.DoesNotContain("SPXE1", tracks.Keys);        // đơn SHOPEE chỉ vào khi "đồng ý"
        Assert.Equal(1, tracks["SPXF1"]);                   // (đã đổi thành TikTok, chờ khách gửi hàng, trạng thái kho "Chưa về") => vào chặng 1
    }

    [Fact]
    public void Legacy_marks_are_kept_not_replaced_and_sorting_is_stage_then_entry_time()
    {
        var list = ReturnWatchService.Load(Db, Now);
        Assert.Equal(1700000000000L, list.Single(e => e.Stored.Order.Track == "SPXG1").Since);
        Assert.Equal(new[] { 1, 2, 2, 3 }, list.Select(e => e.Stage).ToArray());
        var two = list.Where(e => e.Stage == 2).ToList();
        Assert.True(two[0].Since <= two[1].Since);
    }

    [Fact]
    public void Entering_the_board_is_idempotent_and_stays_even_if_platform_status_changes()
    {
        Assert.True(ReturnWatchService.Sync(Db, Now) > 0);
        Assert.Equal(0, ReturnWatchService.Sync(Db, Now.AddMinutes(5)));               // chạy lại không thêm
        var since = ReturnWatchService.Load(Db, Now).Single(e => e.Stored.Order.Track == "SPXA1").Since;

        using (var c = new SqliteConnection($"Data Source={Db};Pooling=False")) { c.Open(); var cmd = c.CreateCommand(); cmd.CommandText = "UPDATE orders SET platform_status='Đã hủy yêu cầu' WHERE track='SPXA1'"; cmd.ExecuteNonQuery(); }
        var after = ReturnWatchService.Load(Db, Now.AddHours(1)).SingleOrDefault(e => e.Stored.Order.Track == "SPXA1");
        Assert.NotNull(after);                                                          // vào rồi thì ở lại
        Assert.Equal(since, after!.Since);                                              // giờ vào không đổi
    }

    [Fact]
    public void Receiving_by_scan_moves_to_stage_four_and_archive_or_delete_leaves_the_board()
    {
        ReturnWatchService.Load(Db, Now);                         // đã vào bảng ở chặng 1
        ScanReceiver.Receive(Db, "SPXA1", Now);
        Assert.Equal(4, ReturnWatchService.Load(Db, Now).Single(e => e.Stored.Order.Track == "SPXA1").Stage);

        OrderEditor.Archive(Db, IdOf("SPXB1"), Now);
        OrderEditor.Delete(Db, IdOf("TKC1"), "TikTok - Hoàn hàng", Now);
        var tracks = ReturnWatchService.Load(Db, Now).Select(e => e.Stored.Order.Track).ToList();
        Assert.DoesNotContain("SPXB1", tracks);
        Assert.DoesNotContain("TKC1", tracks);
        OrderEditor.Unarchive(Db, IdOf("SPXB1"), Now);
        Assert.Contains("SPXB1", ReturnWatchService.Load(Db, Now).Select(e => e.Stored.Order.Track));   // trả về bảng chính thì quay lại bảng
    }

    [Fact]
    public void Marking_not_needed_or_lost_removes_it_from_the_board_without_forgetting_it()
    {
        OrderEditor.SetField(Db, IdOf("SPXA1"), EditField.Status, ReceiveStatus.Lost, Now);
        Assert.DoesNotContain("SPXA1", ReturnWatchService.Load(Db, Now).Select(e => e.Stored.Order.Track));
        OrderEditor.SetField(Db, IdOf("SPXA1"), EditField.Status, ReceiveStatus.NotReturned, Now.AddMinutes(1));
        Assert.Contains("SPXA1", ReturnWatchService.Load(Db, Now).Select(e => e.Stored.Order.Track));
    }

    [Fact]
    public void Remove_and_restore_change_only_the_board_with_audit_and_never_touch_the_order()
    {
        var id = IdOf("SPXA1");
        ReturnWatchService.Load(Db, Now);
        var before = OrderRepository.Load(Db).Single(o => o.Order.Id == id);

        Assert.Equal(EditKind.Saved, ReturnWatchService.Remove(Db, id, Now).Kind);
        Assert.DoesNotContain("SPXA1", ReturnWatchService.Load(Db, Now).Select(e => e.Stored.Order.Track));
        Assert.Equal(EditKind.NoChange, ReturnWatchService.Remove(Db, id, Now).Kind);
        Assert.Equal(1, Count("SELECT COUNT(*) FROM audit_log WHERE type='return_watch' AND detail LIKE 'Bỏ khỏi%SPXA1'"));

        var after = OrderRepository.Load(Db).Single(o => o.Order.Id == id);
        Assert.Equal(before.Order.Status, after.Order.Status);
        Assert.False(after.IsDeleted); Assert.False(after.IsArchived);                 // đơn còn nguyên ở bảng chính

        Assert.Equal(EditKind.Saved, ReturnWatchService.Restore(Db, id, Now).Kind);
        Assert.Contains("SPXA1", ReturnWatchService.Load(Db, Now).Select(e => e.Stored.Order.Track));
        Assert.Equal(EditKind.NotFound, ReturnWatchService.Remove(Db, "khong-co", Now).Kind);
    }

    [Fact]
    public void Two_parcels_of_one_order_are_separate_entries()
    {
        using (var c = new SqliteConnection($"Data Source={Db};Pooling=False"))
        {
            c.Open();
            var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE orders SET order_id='O1' WHERE track IN ('SPXB1')";
            cmd.ExecuteNonQuery();
        }
        var list = ReturnWatchService.Load(Db, Now).Where(e => e.Stored.Order.OrderId == "O1").ToList();
        Assert.Equal(2, list.Count);
        Assert.NotEqual(list[0].Stage, list[1].Stage);
    }

    [Fact]
    public void Phone_list_carries_rw_fields_only_for_main_table_board_members()
    {
        ReturnWatchService.Sync(Db, Now);
        var all = OrderRepository.Load(Db);
        var rows = PhoneSnapshot.BuildRows(all, Now, ReturnWatchService.ActiveMarks(Db, all));
        var a = rows.Single(r => (string)r["c"] == "SPXA1");
        Assert.True((long)a["rw"] > 0); Assert.Equal(1, a["rws"]);
        var c3 = rows.Single(r => (string)r["c"] == "TKC1");
        Assert.Equal(3, c3["rws"]); Assert.True((long)c3["ar"] > 0);                    // giờ hàng về shop
        Assert.False(rows.Single(r => (string)r["c"] == "SPXE1").ContainsKey("rw"));    // chưa vào bảng
        Assert.False(rows.Single(r => (string)r["c"] == "SPXH1").ContainsKey("rw"));    // đã bị bỏ
        Assert.False(rows.Single(r => (string)r["c"] == "SPXD1").ContainsKey("rw"));    // đã nhận trước khi vào bảng
    }

    [Fact]
    public void Schema_is_upgraded_in_place_and_old_databases_without_the_table_still_load()
    {
        using (var c = new SqliteConnection($"Data Source={Db};Pooling=False"))
        {
            c.Open();
            foreach (var sql in new[] { "DROP TABLE return_watch", "PRAGMA user_version = 4" }) { var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        }
        Assert.Empty(ReturnWatchService.ActiveMarks(Db, OrderRepository.Load(Db)));     // chưa có bảng: không ném lỗi
        AppDatabase.Initialize(Db);
        Assert.Equal(AppDatabase.SchemaVersion, Count("PRAGMA user_version"));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM sqlite_master WHERE name='return_watch'"));
        Assert.NotEmpty(ReturnWatchService.Load(Db, Now));
    }
}
