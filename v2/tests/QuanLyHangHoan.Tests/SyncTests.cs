using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;
using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.Tests;

/// <summary>Đồng bộ điện thoại: định dạng bản tin tương thích app cũ, chống xử lý lặp, chỉ ACK sau khi lưu bền. Dữ liệu GIẢ.</summary>
public sealed class SyncTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private const string Room = "ROOM-TEST";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-sync-" + Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_dir, "t.db");

    public SyncTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private void Seed(params (string oid, string track, string fwd)[] rows)
    {
        var data = rows.Select(r => new
        {
            platform = "Shopee", orderType = "Trả hàng", orderId = r.oid, track = r.track, forwardTrack = r.fwd,
            status = "Chưa trả", price = 1, items = Array.Empty<object>()
        });
        File.WriteAllText(Path.Combine(_dir, "du_lieu_hang_hoan.json"), JsonSerializer.Serialize(data));
        LegacyImporter.Import(_dir, Db);
    }

    private static string Scan(string requestId, string code, string room = Room, string type = "barcode") =>
        JsonSerializer.Serialize(new { type, room, requestId, code, time = "12:00:00" });

    private static JsonElement Ack(string? json)
    {
        Assert.NotNull(json);
        return JsonDocument.Parse(json!).RootElement.Clone();
    }

    [Fact]
    public void Scan_message_is_parsed_with_legacy_field_names()
    {
        Assert.True(SyncProtocol.TryParseScan(Scan("r1", " trk1 "), out var req));
        Assert.Equal("r1", req.RequestId);
        Assert.Equal("TRK1", req.Code); // chuẩn hóa như TrackingResolver
        Assert.Equal(Room, req.Room);
    }

    [Theory]
    [InlineData("không phải json")]
    [InlineData("{\"type\":\"latency_ping\",\"room\":\"R\"}")]
    [InlineData("{\"type\":\"barcode\",\"room\":\"R\",\"requestId\":\"\",\"code\":\"A\"}")]
    [InlineData("{\"type\":\"barcode\",\"room\":\"R\",\"requestId\":\"x\",\"code\":\"\"}")]
    public void Unrelated_or_incomplete_messages_are_ignored(string json) =>
        Assert.False(SyncProtocol.TryParseScan(json, out _));

    [Fact]
    public void Success_ack_has_legacy_shape_and_only_after_commit()
    {
        Seed(("O1", "A", ""));
        var ack = Ack(SyncReceiver.Handle(Db, Room, Scan("r1", "A"), Now));

        Assert.Equal("pc_ack", ack.GetProperty("type").GetString());
        Assert.Equal(Room, ack.GetProperty("room").GetString());
        Assert.Equal("r1", ack.GetProperty("requestId").GetString());
        Assert.Equal("A", ack.GetProperty("code").GetString());
        Assert.Equal("success", ack.GetProperty("outcome").GetString());
        Assert.Equal("Hàng đã trả", OrderRepository.Load(Db).Single().Order.Status); // đã lưu trước khi có ack
    }

    [Fact]
    public void Redelivery_of_same_request_returns_same_ack_and_does_not_reprocess()
    {
        Seed(("O1", "A", ""));
        var first = SyncReceiver.Handle(Db, Room, Scan("r1", "A"), Now);
        var second = SyncReceiver.Handle(Db, Room, Scan("r1", "A"), Now.AddHours(3));

        Assert.Equal(first, second);
        Assert.Equal("23/11/2026 12:00:00", OrderRepository.Load(Db).Single().Order.RecvDate);
    }

    [Fact]
    public void Same_message_arriving_concurrently_from_several_brokers_gets_one_identical_ack()
    {
        Seed(("O1", "A", ""));
        var acks = new System.Collections.Concurrent.ConcurrentBag<string?>();

        Parallel.For(0, 12, new ParallelOptions { MaxDegreeOfParallelism = 12 },
            _ => acks.Add(SyncReceiver.Handle(Db, Room, Scan("r1", "A"), Now)));

        Assert.Single(acks.Distinct());                                  // mọi bản sao nhận cùng MỘT xác nhận
        Assert.Equal("success", Ack(acks.First()).GetProperty("outcome").GetString());
        Assert.Equal("Hàng đã trả", OrderRepository.Load(Db).Single().Order.Status);
        using var c = new SqliteConnection($"Data Source={Db};Pooling=False"); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM sync_inbox";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void Same_photo_arriving_concurrently_is_stored_once()
    {
        Seed(("O1", "A", ""));
        var photo = JsonSerializer.Serialize(new
        {
            type = "photo", room = Room, requestId = "r1", captureId = "cap1", code = "A",
            photo = "data:image/jpeg;base64," + Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 1, 2, 3 }),
        });
        var acks = new System.Collections.Concurrent.ConcurrentBag<string?>();

        Parallel.For(0, 12, new ParallelOptions { MaxDegreeOfParallelism = 12 },
            _ => acks.Add(SyncReceiver.Handle(Db, Room, photo, Now)));

        Assert.Single(acks.Distinct());
        using var c = new SqliteConnection($"Data Source={Db};Pooling=False"); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM photos WHERE source='phone'";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void Same_code_new_request_is_reported_duplicate_keeping_original_time()
    {
        Seed(("O1", "A", ""));
        SyncReceiver.Handle(Db, Room, Scan("r1", "A"), Now);
        var ack = Ack(SyncReceiver.Handle(Db, Room, Scan("r2", "A"), Now.AddHours(2)));

        Assert.Equal("duplicate", ack.GetProperty("outcome").GetString());
        Assert.Equal("23/11/2026 12:00:00", OrderRepository.Load(Db).Single().Order.RecvDate);
    }

    [Fact]
    public void Ambiguous_and_not_found_are_reported_and_change_nothing()
    {
        Seed(("O1", "A", "S"), ("O1", "B", "S"));
        Assert.Equal("ambiguous", Ack(SyncReceiver.Handle(Db, Room, Scan("r1", "S"), Now)).GetProperty("outcome").GetString());
        Assert.Equal("not_found", Ack(SyncReceiver.Handle(Db, Room, Scan("r2", "XYZ"), Now)).GetProperty("outcome").GetString());
        Assert.All(OrderRepository.Load(Db), o => Assert.Equal("Chưa trả", o.Order.Status));
    }

    [Fact]
    public void Message_for_another_room_is_ignored_no_ack_no_change()
    {
        Seed(("O1", "A", ""));
        Assert.Null(SyncReceiver.Handle(Db, Room, Scan("r1", "A", room: "ROOM-KHAC"), Now));
        Assert.Equal("Chưa trả", OrderRepository.Load(Db).Single().Order.Status);
    }

    [Fact]
    public void Failure_to_persist_gives_error_ack_never_success()
    {
        // DB không có bảng orders (file rỗng/hỏng) => không được ACK success
        File.WriteAllText(Db, "không phải sqlite");
        var ack = SyncReceiver.Handle(Db, Room, Scan("r1", "A"), Now);
        if (ack is not null) Assert.NotEqual("success", Ack(ack).GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Service_receives_over_transport_and_publishes_ack_to_same_room()
    {
        Seed(("O1", "A", ""));
        var transport = new InMemoryTransport();
        var phoneInbox = new List<string>();
        transport.Subscribe(Room, (_, msg) => phoneInbox.Add(msg));

        using var svc = new SyncService(Db, Room, transport, () => Now);
        svc.Start();
        await transport.PublishAsync(Room, Scan("r1", "A"));

        var acks = phoneInbox.Where(m => m.Contains("pc_ack")).ToList();
        Assert.Single(acks);
        Assert.Equal("success", Ack(acks[0]).GetProperty("outcome").GetString());
        Assert.Equal("Hàng đã trả", OrderRepository.Load(Db).Single().Order.Status);
    }

    [Fact]
    public async Task Service_raises_event_after_ack_published_with_request_and_outcome()
    {
        Seed(("O1", "A", ""));
        var transport = new InMemoryTransport();
        using var svc = new SyncService(Db, Room, transport, () => Now);
        var events = new List<SyncHandled>();
        svc.Handled += e => events.Add(e);
        svc.Start();

        await transport.PublishAsync(Room, Scan("r1", "A"));
        await transport.PublishAsync(Room, Scan("r1", "A")); // gửi lại: vẫn báo sự kiện nhưng không đổi dữ liệu

        Assert.Equal(2, events.Count);
        Assert.Equal("r1", events[0].RequestId);
        Assert.Equal("A", events[0].Code);
        Assert.Equal("success", events[0].Outcome);
        Assert.Equal(Now, events[0].At);
    }

    [Fact]
    public async Task Service_stop_means_no_more_processing()
    {
        Seed(("O1", "A", ""));
        var transport = new InMemoryTransport();
        var svc = new SyncService(Db, Room, transport, () => Now);
        svc.Start();
        svc.Dispose();
        await transport.PublishAsync(Room, Scan("r1", "A"));
        Assert.Equal("Chưa trả", OrderRepository.Load(Db).Single().Order.Status);
    }
}
