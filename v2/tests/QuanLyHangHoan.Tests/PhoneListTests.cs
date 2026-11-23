using System.IO;
using System.Text.Json;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;
using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.Tests;

/// <summary>Danh sách đơn gửi app iPhone (snapshot / delta / watch_sync) và các thao tác từ iPhone. Dữ liệu GIẢ, DB tạm.</summary>
public sealed class PhoneListTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private const string Room = "ROOM-PHONE";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-pl-" + Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_dir, "t.db");

    public PhoneListTests()
    {
        Directory.CreateDirectory(_dir);
        object Item(string p) => new { product = p, color = "Đen", size = "M", qty = 2 };
        File.WriteAllText(Path.Combine(_dir, "du_lieu_hang_hoan.json"), JsonSerializer.Serialize(new object[]
        {
            // đã về 13 giờ trước, chưa nhận -> vàng
            new { platform = "Shopee", orderType = "Trả hàng/Hoàn tiền", orderId = "O1", track = "SPXA1", returnTrack = "RTA1", status = "Chưa trả", returnedAt = "22/11/2026 23:00:00", note = "lý do", items = new[] { Item("Áo A") } },
            // cùng đơn O1, kiện B riêng
            new { platform = "Shopee", orderType = "Trả hàng/Hoàn tiền", orderId = "O1", track = "SPXB1", status = "Chưa trả", returnedAt = "", items = new[] { Item("Áo B") } },
            // đã về 30 giờ trước -> đỏ
            new { platform = "TikTok", orderType = "Trả hàng/Hoàn tiền", orderId = "T1", track = "TKR1", status = "Chưa về", returnedAt = "22/11/2026 06:00:00", items = new[] { Item("Quần") } },
            // đã nhận
            new { platform = "Shopee", orderType = "Trả hàng/Hoàn tiền", orderId = "O2", track = "SPXC1", status = "Hàng đã trả", recvDate = "23/11/2026 08:00:00", returnedAt = "21/11/2026 10:00:00", items = new[] { Item("Váy") } },
            // đơn hủy chưa nhận
            new { platform = "Shopee", orderType = "Đơn hủy", orderId = "C1", track = "SPXD1", status = "Chưa trả", returnedAt = "", items = new[] { Item("Mũ") } },
        }));
        LegacyImporter.Import(_dir, Db);
    }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private List<Dictionary<string, object>> Rows() => PhoneSnapshot.BuildRows(OrderRepository.Load(Db), Now);
    private static Dictionary<string, object> Row(List<Dictionary<string, object>> rows, string c) => rows.Single(r => (string)r["c"] == c);
    private string IdOf(string track) => OrderRepository.Load(Db).Single(o => o.Order.Track == track).Order.Id;

    // ---------------- nội dung dòng ----------------
    [Fact]
    public void Rows_carry_alert_level_due_and_kind_like_the_old_pc()
    {
        var rows = Rows();
        var a = Row(rows, "SPXA1");
        Assert.Equal("m", a["k"]); Assert.Equal("Shopee", a["p"]); Assert.Equal("O1", a["o"]);
        Assert.Equal(1, a["lv"]);                                   // 13h: vàng
        Assert.True((long)a["due"] > 0);                            // hạn = giờ về + 24h
        Assert.Equal(2, a["q"]);
        Assert.Contains("RTA1", (string)a["x"]);                    // mã hoàn của chính kiện
        Assert.False(a.ContainsKey("pd"));                          // còn chờ nhận (mặc định)

        var t = Row(rows, "TKR1");
        Assert.Equal(2, t["lv"]);                                   // 30h: đỏ
        Assert.Equal("TikTok Shop", t["p"]);                        // điện thoại hiểu cả "TikTok" lẫn "TikTok Shop"

        var done = Row(rows, "SPXC1");
        Assert.Equal(0, done["pd"]); Assert.Equal(1, done["rv"]); Assert.Equal(0, done["lv"]); Assert.Equal(0L, done["due"]);
        Assert.Equal("23/11/2026 08:00:00", done["rcd"]);

        var c = Row(rows, "SPXD1");
        Assert.Equal("c", c["k"]); Assert.Equal("Đơn hủy", c["st"]); Assert.Equal(IdOf("SPXD1"), c["ci"]);
    }

    [Fact]
    public void Same_order_two_parcels_stay_two_separate_rows()
    {
        var rows = Rows().Where(r => (string)r["o"] == "O1").ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "SPXA1", "SPXB1" }, rows.Select(r => (string)r["c"]).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Pending_rows_come_first_nearest_deadline_first_and_deleted_orders_are_not_sent()
    {
        OrderEditor.Delete(Db, IdOf("SPXB1"), "Shopee - Hoàn hàng", Now);
        var rows = Rows();
        Assert.DoesNotContain(rows, r => (string)r["c"] == "SPXB1");
        var order = rows.Select(r => (string)r["c"]).ToList();
        Assert.True(order.IndexOf("TKR1") < order.IndexOf("SPXA1"));   // hạn gần hơn lên trước
        Assert.Equal("SPXC1", order.Last());                           // đã nhận xuống cuối
    }

    [Fact]
    public void Stats_count_the_source_data_and_dropped_is_zero()
    {
        var all = OrderRepository.Load(Db);
        var rows = PhoneSnapshot.BuildRows(all, Now);
        var st = PhoneSnapshot.BuildStats(all, rows.Count, Now);
        Assert.Equal(4, st["m_total"]); Assert.Equal(1, st["m_recv"]); Assert.Equal(3, st["m_pend"]);
        Assert.Equal(1, st["m_danger"]); Assert.Equal(1, st["m_warn"]); Assert.Equal(1, st["c_total"]);
        Assert.Equal(0, st["dropped"]);
    }

    [Fact]
    public void No_personal_data_fields_exist_in_rows()
    {
        var json = PhoneSnapshot.ToJson(Rows());
        foreach (var key in new[] { "phone", "address", "buyer", "sdt" })
            Assert.DoesNotContain("\"" + key, json, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------- gói tin ----------------
    [Fact]
    public void Full_snapshot_round_trips_through_deflate_base64_and_chunks()
    {
        var rows = Rows();
        var json = PhoneSnapshot.FullPayloadJson(rows, PhoneSnapshot.BuildStats(OrderRepository.Load(Db), rows.Count, Now), "snap_1", 1);
        var b64 = PhoneSnapshot.DeflateBase64(json);
        var chunks = PhoneSnapshot.Chunks("snapshot_chunk", "snapshotId", "snap_1", Room, b64, 1, rows.Count);
        var parts = chunks.Select(c => JsonDocument.Parse(c).RootElement).ToList();
        Assert.All(parts, p => { Assert.Equal(Room, p.GetProperty("room").GetString()); Assert.True(p.GetProperty("chunk").GetString()!.Length <= PhoneSnapshot.ChunkSize); });
        var joined = string.Concat(parts.OrderBy(p => p.GetProperty("idx").GetInt32()).Select(p => p.GetProperty("chunk").GetString()));
        using var back = JsonDocument.Parse(PhoneSnapshot.InflateBase64(joined));
        Assert.Equal(rows.Count, back.RootElement.GetProperty("rows").GetArrayLength());
        Assert.Equal("snap_1", back.RootElement.GetProperty("sid").GetString());
        Assert.All(chunks, c => Assert.True(System.Text.Encoding.UTF8.GetByteCount(c) < 4096)); // vừa giới hạn ntfy
    }

    [Fact]
    public void Delta_contains_only_changed_and_removed_rows_with_phone_compatible_keys()
    {
        var before = Rows();
        var b = PhoneSnapshot.MakeBase("snap_1", before);
        OrderEditor.SetField(Db, IdOf("SPXA1"), EditField.Status, ReceiveStatus.Received, Now);
        OrderEditor.Delete(Db, IdOf("SPXC1"), "Shopee - Hoàn hàng", Now);

        var d = PhoneSnapshot.ComputeDelta(b, Rows());
        Assert.Single(d.Up);
        Assert.Equal("m|SPXA1|O1|", d.Up[0]["_k"]);
        Assert.Equal(1, d.Up[0]["rv"]);
        Assert.Equal(new[] { "m|SPXC1|O2|" }, d.Rm.ToArray());
    }

    [Fact]
    public void Duplicate_row_keys_are_numbered_like_the_phone_does()
    {
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["k"] = "m", ["c"] = "X", ["o"] = "O" }, new() { ["k"] = "m", ["c"] = "X", ["o"] = "O" }, new() { ["k"] = "c", ["c"] = "X", ["o"] = "O", ["ci"] = "id1" },
        };
        Assert.Equal(new[] { "m|X|O|", "m|X|O|#2", "c|X|O|id1" }, PhoneSnapshot.RowKeys(rows).ToArray());
    }

    // ---------------- dịch vụ gửi ----------------
    private static List<JsonElement> Of(List<string> inbox, string type)
    {
        lock (inbox) return inbox.Select(m => JsonDocument.Parse(m).RootElement.Clone())
            .Where(e => e.TryGetProperty("type", out var t) && t.GetString() == type).ToList();
    }
    private static List<string> Listen(InMemoryTransport t) { var inbox = new List<string>(); t.Subscribe(Room, (_, m) => { lock (inbox) inbox.Add(m); }); return inbox; }
    private static async Task<bool> WaitFor(Func<bool> cond, int ms = 4000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (cond()) return true; await Task.Delay(10); }
        return cond();
    }
    private SyncService NewService(InMemoryTransport t) =>
        new(Db, Room, t, () => Now, heartbeatInterval: TimeSpan.Zero, snapshotPoll: TimeSpan.Zero, chunkDelay: TimeSpan.Zero);

    [Fact]
    public async Task Snapshot_request_sends_full_list_and_watch_sync_to_same_room_only()
    {
        var t = new InMemoryTransport();
        var inbox = Listen(t);
        using var svc = NewService(t);
        svc.Start();

        await t.PublishAsync(Room, JsonSerializer.Serialize(new { type = "snapshot_request", room = "PHONG-KHAC", time = 1 }));
        await Task.Delay(150);
        Assert.Empty(Of(inbox, "snapshot_chunk"));                                   // khác phòng: im lặng

        await t.PublishAsync(Room, JsonSerializer.Serialize(new { type = "snapshot_request", room = Room, time = 1 }));
        Assert.True(await WaitFor(() => Of(inbox, "snapshot_chunk").Count > 0 && Of(inbox, "watch_sync").Count > 0));
        var chunks = Of(inbox, "snapshot_chunk");
        Assert.Single(chunks.Select(c => c.GetProperty("snapshotId").GetString()).Distinct());
        Assert.Equal(2, chunks.Count / chunks.Select(c => c.GetProperty("idx").GetInt32()).Distinct().Count()); // gửi 2 lượt
    }

    [Fact]
    public async Task Old_scanner_page_never_asked_so_no_list_is_pushed_after_a_scan()
    {
        var t = new InMemoryTransport();
        var inbox = Listen(t);
        using var svc = NewService(t);
        svc.Start();
        await t.PublishAsync(Room, JsonSerializer.Serialize(new { type = "barcode", room = Room, requestId = "r1", code = "SPXA1", captureId = "c1", time = 1 }));
        await Task.Delay(700);
        Assert.Single(Of(inbox, "pc_ack"));
        Assert.Empty(Of(inbox, "snapshot_chunk"));
        Assert.Empty(Of(inbox, "snapshot_delta"));
    }

    [Fact]
    public async Task After_a_full_list_a_scan_pushes_a_small_delta_and_only_after_the_ack_is_stored()
    {
        var t = new InMemoryTransport();
        var inbox = Listen(t);
        using var svc = NewService(t);
        svc.Start();
        await t.PublishAsync(Room, JsonSerializer.Serialize(new { type = "snapshot_request", room = Room, time = 1 }));
        Assert.True(await WaitFor(() => Of(inbox, "snapshot_chunk").Count > 0));
        await Task.Delay(100);

        await t.PublishAsync(Room, JsonSerializer.Serialize(new { type = "barcode", room = Room, requestId = "r1", code = "SPXA1", captureId = "c1", time = 2 }));
        Assert.True(await WaitFor(() => Of(inbox, "snapshot_delta").Count > 0));
        Assert.Equal("success", Of(inbox, "pc_ack").Single().GetProperty("outcome").GetString());
        Assert.Equal(ReceiveStatus.Received, OrderRepository.Load(Db).Single(o => o.Order.Track == "SPXA1").Order.Status);

        var parts = Of(inbox, "snapshot_delta");
        var b64 = string.Concat(parts.GroupBy(p => p.GetProperty("idx").GetInt32()).OrderBy(g => g.Key).Select(g => g.First().GetProperty("chunk").GetString()));
        using var doc = JsonDocument.Parse(PhoneSnapshot.InflateBase64(b64));
        Assert.Equal(1, doc.RootElement.GetProperty("up").GetArrayLength());
        Assert.Equal(Of(inbox, "snapshot_chunk")[0].GetProperty("snapshotId").GetString(), doc.RootElement.GetProperty("base").GetString());
    }

    // ---------------- thao tác từ iPhone ----------------
    private PhoneActionResult Act(object msg) => PhoneActions.Handle(Db, Room, JsonSerializer.Serialize(msg), Now)!;
    private static string Outcome(PhoneActionResult r) => r.Outcome;

    [Fact]
    public void Note_marks_watchlist_with_reason_and_acks_after_saving()
    {
        var r = Act(new { type = "order_note", room = Room, requestId = "n1", code = "SPXA1", orderId = "O1", note = "gọi khách", watch = true });
        Assert.Equal("success", r.Outcome); Assert.Equal("note", r.Stage); Assert.True(r.Changed);
        var o = OrderRepository.Load(Db).Single(x => x.Order.Track == "SPXA1").Order;
        Assert.True(o.IsWatchlist); Assert.Equal("gọi khách", o.WatchlistReason);
        Assert.False(OrderRepository.Load(Db).Single(x => x.Order.Track == "SPXB1").Order.IsWatchlist); // kiện B không bị ảnh hưởng
    }

    [Fact]
    public void Order_id_shared_by_two_parcels_is_ambiguous_and_changes_nothing()
    {
        var r = Act(new { type = "order_note", room = Room, requestId = "n2", code = "KHONG-CO", orderId = "O1", note = "x", watch = true });
        Assert.Equal("ambiguous", r.Outcome); Assert.False(r.Changed);
        Assert.DoesNotContain(OrderRepository.Load(Db), o => o.Order.IsWatchlist);
    }

    [Fact]
    public void Edit_status_goes_through_the_editor_and_only_status_is_accepted()
    {
        var ok = Act(new { type = "order_edit", room = Room, requestId = "e1", code = "SPXA1", fields = new { st = "Hàng đã trả" } });
        Assert.Equal("success", ok.Outcome);
        Assert.Equal(ReceiveStatus.Received, OrderRepository.Load(Db).Single(x => x.Order.Track == "SPXA1").Order.Status);

        var bad = Act(new { type = "order_edit", room = Room, requestId = "e2", code = "SPXB1", fields = new { n = "Tên mới", st = "Hàng đã trả" } });
        Assert.Equal("invalid", bad.Outcome);
        Assert.Equal("Chưa trả", OrderRepository.Load(Db).Single(x => x.Order.Track == "SPXB1").Order.Status);   // không lưu một nửa

        var hu = Act(new { type = "order_edit", room = Room, requestId = "e3", code = "SPXB1", fields = new { st = "Hư hỏng" } });
        Assert.Equal("invalid", hu.Outcome);
    }

    [Fact]
    public void Delete_is_soft_and_can_be_restored()
    {
        var r = Act(new { type = "order_delete", room = Room, requestId = "d1", code = "SPXB1" });
        Assert.Equal("success", r.Outcome); Assert.Equal("delete", r.Stage);
        var stored = OrderRepository.Load(Db).Single(x => x.Order.Track == "SPXB1");
        Assert.True(stored.IsDeleted);
        Assert.True(OrderEditor.Restore(Db, stored.Order.Id, Now).Ok);
    }

    [Fact]
    public void Resending_the_same_request_returns_the_same_ack_without_redoing()
    {
        var first = Act(new { type = "order_delete", room = Room, requestId = "d2", code = "SPXB1" });
        var again = Act(new { type = "order_delete", room = Room, requestId = "d2", code = "SPXB1" });
        Assert.Equal("success", first.Outcome);
        Assert.Equal("success", again.Outcome);   // không thành not_found dù đơn đã xóa
        Assert.Equal(first.Ack, again.Ack);
    }

    [Fact]
    public void Cancel_receive_marks_only_cancelled_orders_and_can_be_undone()
    {
        var id = IdOf("SPXD1");
        var on = Act(new { type = "cancel_receive", room = Room, requestId = "c1", ids = new[] { id, IdOf("SPXA1") }, received = true });
        Assert.Equal("success", on.Outcome);
        Assert.Equal(ReceiveStatus.Received, OrderRepository.Load(Db).Single(x => x.Order.Track == "SPXD1").Order.Status);
        Assert.Equal("Chưa trả", OrderRepository.Load(Db).Single(x => x.Order.Track == "SPXA1").Order.Status);   // đơn không phải hủy: không đổi

        var same = Act(new { type = "cancel_receive", room = Room, requestId = "c2", ids = new[] { id }, received = true });
        Assert.Equal("duplicate", same.Outcome);
        var off = Act(new { type = "cancel_receive", room = Room, requestId = "c3", ids = new[] { id }, received = false });
        Assert.Equal("success", off.Outcome);
        Assert.Equal("Chưa trả", OrderRepository.Load(Db).Single(x => x.Order.Track == "SPXD1").Order.Status);
    }

    [Fact]
    public void Phone_new_order_goes_straight_into_the_main_table_with_auto_platform()
    {
        var before = OrderRepository.Load(Db).Count;
        var r = Act(new { type = "new_order_scan", room = Room, requestId = "s1", code = "SPXVN0999", captureId = "cap1", cancelled = false, platform = "auto" });
        Assert.Equal("success", r.Outcome); Assert.Equal("scan", r.Stage); Assert.Equal("new_order", r.Kind); Assert.True(r.Changed);
        Assert.Contains("\"captureId\":\"cap1\"", r.Ack);
        var all = OrderRepository.Load(Db);
        Assert.Equal(before + 1, all.Count);
        var s = all.Single(x => x.Order.Track == "SPXVN0999");
        Assert.False(s.IsArchived); Assert.False(s.IsDeleted);
        Assert.Equal("Shopee", s.Order.Platform); Assert.Equal("SPX Express", s.Order.Carrier);
        Assert.Equal(ReceiveStatus.NotReturned, s.Order.Status);                 // chưa nhận: phải quét nhận kiện sau
        Assert.Equal("SPXVN0999", s.Order.OrderId);
        Assert.Equal("Đơn mới tạo từ máy quét điện thoại", s.Order.Note);
        Assert.Single(s.Order.Items);
        Assert.Contains(PhoneSnapshot.BuildRows(all, Now), row => (string)row["c"] == "SPXVN0999"); // điện thoại thấy ngay ở danh sách
    }

    [Fact]
    public void Phone_new_order_tiktok_code_detected_and_resend_does_not_create_twice()
    {
        var msg = new { type = "new_order_scan", room = Room, requestId = "s9", code = "803040506070", captureId = "c9", cancelled = false };
        var first = Act(msg);
        var again = Act(msg);
        Assert.Equal("success", first.Outcome);
        Assert.Equal(first.Ack, again.Ack);
        var rows = OrderRepository.Load(Db).Where(x => x.Order.Track == "803040506070").ToList();
        Assert.Single(rows);
        Assert.Equal("TikTok", rows[0].Order.Platform);
    }

    [Fact]
    public void Phone_new_order_for_an_existing_or_deleted_code_is_duplicate_and_creates_nothing()
    {
        var before = OrderRepository.Load(Db).Count;
        var dup = Act(new { type = "new_order_scan", room = Room, requestId = "s2", code = "SPXA1", captureId = "c2" });
        Assert.Equal("duplicate", dup.Outcome); Assert.False(dup.Changed);
        var viaReturnCode = Act(new { type = "new_order_scan", room = Room, requestId = "s3", code = "RTA1", captureId = "c3" });
        Assert.Equal("duplicate", viaReturnCode.Outcome);

        OrderEditor.Delete(Db, IdOf("SPXB1"), "Shopee - Hoàn hàng", Now);
        var deleted = Act(new { type = "new_order_scan", room = Room, requestId = "s4", code = "SPXB1", captureId = "c4" });
        Assert.Equal("duplicate", deleted.Outcome);
        Assert.Contains("ĐÃ XÓA", deleted.Message);
        Assert.True(OrderRepository.Load(Db).Single(x => x.Order.Track == "SPXB1").IsDeleted);   // không hồi sinh đơn đã xóa
        Assert.Equal(before, OrderRepository.Load(Db).Count);
    }

    [Fact]
    public void Phone_new_order_then_scan_receives_it_and_photo_can_attach()
    {
        Act(new { type = "new_order_scan", room = Room, requestId = "s5", code = "SPXNEW5", captureId = "c5" });
        var scan = QuanLyHangHoan.Data.Sync.SyncReceiver.Handle(Db, Room, JsonSerializer.Serialize(new { type = "barcode", room = Room, requestId = "b5", code = "SPXNEW5", captureId = "c5b", time = 1 }), Now);
        Assert.Contains("\"outcome\":\"success\"", scan);
        Assert.Equal(ReceiveStatus.Received, OrderRepository.Load(Db).Single(x => x.Order.Track == "SPXNEW5").Order.Status);
    }

    [Fact]
    public void Phone_new_order_without_a_code_is_invalid_and_saves_nothing()
    {
        var before = OrderRepository.Load(Db).Count;
        var r = Act(new { type = "new_order_scan", room = Room, requestId = "s6", code = "  ", captureId = "c6" });
        Assert.Equal("invalid", r.Outcome);
        Assert.Equal(before, OrderRepository.Load(Db).Count);
    }

    [Theory]
    [InlineData("SPXVN123", "Shopee")]
    [InlineData("VN268ABCDEFGH", "Shopee")]
    [InlineData("803040506070", "TikTok")]
    [InlineData("581234567890123456", "TikTok")]
    [InlineData("ZZ", "TikTok")]
    public void Platform_is_detected_from_the_code_like_the_old_pc(string code, string platform) =>
        Assert.Equal(platform, PhoneActions.DetectPlatform(code));

    [Fact]
    public void Creating_cancelled_orders_from_the_phone_is_dropped_and_saves_nothing()
    {
        var before = OrderRepository.Load(Db).Count;
        var r = Act(new { type = "new_order_scan", room = Room, requestId = "s2", code = "SPXCXL1", captureId = "cap2", cancelled = true });
        Assert.Equal("error", r.Outcome);
        Assert.Contains("đơn hủy", r.Message);
        Assert.Empty(r.Kind);
        Assert.Equal(before, OrderRepository.Load(Db).Count);
    }

    [Fact]
    public void Other_room_or_missing_request_id_is_ignored()
    {
        Assert.Null(PhoneActions.Handle(Db, Room, JsonSerializer.Serialize(new { type = "order_delete", room = "KHAC", requestId = "x", code = "SPXB1" }), Now));
        Assert.Null(PhoneActions.Handle(Db, Room, JsonSerializer.Serialize(new { type = "order_delete", room = Room, code = "SPXB1" }), Now));
        Assert.False(OrderRepository.Load(Db).Single(x => x.Order.Track == "SPXB1").IsDeleted);
    }
}
