using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Tests;

/// <summary>Nhận kiện qua quét: chỉ ACK SAU KHI đã lưu bền (quy tắc 6); không nhận nhầm kiện A/B/C (quy tắc 2-3). Dữ liệu GIẢ.</summary>
public sealed class ScanReceiverTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-scan-" + Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_dir, "t.db");

    public ScanReceiverTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static object Row(string oid, string track, string status = "Chưa trả", string fwd = "") => new
    {
        platform = "Shopee", orderType = "Trả hàng", orderId = oid, track, forwardTrack = fwd, status, price = 1,
        items = Array.Empty<object>()
    };

    private void Seed(params object[] rows)
    {
        File.WriteAllText(Path.Combine(_dir, "du_lieu_hang_hoan.json"), JsonSerializer.Serialize(rows));
        LegacyImporter.Import(_dir, Db);
    }

    private StoredOrder Get(string track) => OrderRepository.Load(Db).Single(o => o.Order.Track == track);

    [Fact]
    public void Match_persists_status_and_time_then_acks()
    {
        Seed(Row("O1", "TRK1"));
        var r = ScanReceiver.Receive(Db, "  trk1 ", Now);

        Assert.Equal(ScanOutcome.Received, r.Outcome);
        Assert.True(r.Ack);
        var o = Get("TRK1").Order;
        Assert.Equal("Hàng đã trả", o.Status);
        Assert.Equal("23/11/2026 12:00:00", o.RecvDate);
    }

    [Fact]
    public void Receiving_A_never_receives_sibling_B()
    {
        Seed(Row("O1", "A"), Row("O1", "B"));
        ScanReceiver.Receive(Db, "A", Now);

        Assert.Equal("Hàng đã trả", Get("A").Order.Status);
        Assert.Equal("Chưa trả", Get("B").Order.Status);
    }

    [Fact]
    public void Shared_forward_track_or_order_id_is_ambiguous_nothing_changes_no_ack()
    {
        Seed(Row("O1", "A", fwd: "SHARED"), Row("O1", "B", fwd: "SHARED"));

        foreach (var code in new[] { "SHARED", "O1" })
        {
            var r = ScanReceiver.Receive(Db, code, Now);
            Assert.Equal(ScanOutcome.Ambiguous, r.Outcome);
            Assert.False(r.Ack);
        }
        Assert.Equal("Chưa trả", Get("A").Order.Status);
        Assert.Equal("Chưa trả", Get("B").Order.Status);
    }

    [Fact]
    public void Unknown_code_is_not_found_no_ack()
    {
        Seed(Row("O1", "A"));
        var r = ScanReceiver.Receive(Db, "XYZ", Now);
        Assert.Equal(ScanOutcome.NotFound, r.Outcome);
        Assert.False(r.Ack);
    }

    [Fact]
    public void Scanning_again_does_not_change_receive_time_and_is_reported()
    {
        Seed(Row("O1", "A"));
        ScanReceiver.Receive(Db, "A", Now);
        var again = ScanReceiver.Receive(Db, "A", Now.AddHours(5));

        Assert.Equal(ScanOutcome.AlreadyReceived, again.Outcome);
        Assert.Equal("23/11/2026 12:00:00", Get("A").Order.RecvDate);
    }

    [Fact]
    public void Not_needed_or_lost_status_is_blocked_for_manual_check()
    {
        Seed(Row("O1", "A", "Không cần nhận"), Row("O2", "B", "Bị mất"));
        Assert.Equal(ScanOutcome.NeedsManualCheck, ScanReceiver.Receive(Db, "A", Now).Outcome);
        Assert.Equal(ScanOutcome.NeedsManualCheck, ScanReceiver.Receive(Db, "B", Now).Outcome);
        Assert.Equal("Không cần nhận", Get("A").Order.Status);
        Assert.Equal("Bị mất", Get("B").Order.Status);
    }

    [Fact]
    public void Archived_and_deleted_orders_are_not_matched()
    {
        File.WriteAllText(Path.Combine(_dir, "du_lieu_hang_hoan.json"), "[]");
        File.WriteAllText(Path.Combine(_dir, "phu_shopee_deleted_orders_v1.json"),
            JsonSerializer.Serialize(new { v = JsonSerializer.Serialize(new[] { Row("O9", "DEL") }) }));
        LegacyImporter.Import(_dir, Db);

        Assert.Equal(ScanOutcome.NotFound, ScanReceiver.Receive(Db, "DEL", Now).Outcome);
    }

    [Fact]
    public void Photo_is_saved_in_same_commit_as_the_ack_and_old_photo_kept()
    {
        Seed(Row("O1", "A"));
        var jpg1 = new byte[] { 1, 2, 3 };
        var r1 = ScanReceiver.Receive(Db, "A", Now, jpg1, "image/jpeg");

        Assert.True(r1.Ack);
        var o = Get("A").Order;
        Assert.NotEmpty(o.PhotoId);
        Assert.Equal(jpg1, PhotoRepository.Get(Db, o.PhotoId)!.Bytes);

        // chụp lại (quét lần 2 kèm ảnh mới): ảnh cũ KHÔNG bị ghi đè
        var oldId = o.PhotoId;
        var jpg2 = new byte[] { 9, 9 };
        var r2 = ScanReceiver.Receive(Db, "A", Now.AddHours(1), jpg2, "image/jpeg");
        Assert.True(r2.Ack);                                   // ảnh mới đã lưu => ACK
        Assert.Equal(ScanOutcome.AlreadyReceived, r2.Outcome);
        Assert.Equal(jpg1, PhotoRepository.Get(Db, oldId)!.Bytes);
        Assert.Equal(jpg2, PhotoRepository.Get(Db, Get("A").Order.PhotoId)!.Bytes);
        Assert.Equal("23/11/2026 12:00:00", Get("A").Order.RecvDate);
    }

    [Fact]
    public void Photo_for_unmatched_or_ambiguous_scan_is_not_stored_and_not_acked()
    {
        Seed(Row("O1", "A", fwd: "S"), Row("O1", "B", fwd: "S"));
        var r = ScanReceiver.Receive(Db, "S", Now, new byte[] { 5 }, "image/jpeg");

        Assert.False(r.Ack);
        using var c = new SqliteConnection($"Data Source={Db};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM photos WHERE source='phone'";
        Assert.Equal(0L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void Missing_database_is_not_found_without_creating_file()
    {
        var r = ScanReceiver.Receive(Path.Combine(_dir, "none.db"), "A", Now);
        Assert.False(r.Ack);
        Assert.False(File.Exists(Path.Combine(_dir, "none.db")));
    }
}
