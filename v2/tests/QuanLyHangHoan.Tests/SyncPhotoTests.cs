using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;
using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.Tests;

/// <summary>Ảnh từ điện thoại (gói trọn `photo` và mảnh `photo_chunk`). Bản tin từ mạng công cộng => kiểm tra chặt. Dữ liệu GIẢ.</summary>
public sealed class SyncPhotoTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private const string Room = "ROOM-PHOTO";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-sph-" + Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_dir, "t.db");

    public SyncPhotoTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private void Seed(params (string oid, string track, string fwd)[] rows)
    {
        File.WriteAllText(Path.Combine(_dir, "du_lieu_hang_hoan.json"), JsonSerializer.Serialize(rows.Select(r => new
        {
            platform = "Shopee", orderType = "x", orderId = r.oid, track = r.track, forwardTrack = r.fwd,
            status = "Chưa trả", price = 1, items = Array.Empty<object>()
        })));
        LegacyImporter.Import(_dir, Db);
    }

    private static readonly byte[] Jpg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5 };
    private static string Uri(byte[] b, string mime = "image/jpeg") => $"data:{mime};base64,{Convert.ToBase64String(b)}";

    private static string Photo(string captureId, string code, string dataUri, string room = Room, string requestId = "r1") =>
        JsonSerializer.Serialize(new { type = "photo", room, requestId, captureId, code, photo = dataUri, photoOnly = true });

    private static string Chunk(string photoId, string captureId, string code, int idx, int total, string chunk, string room = Room) =>
        JsonSerializer.Serialize(new { type = "photo_chunk", room, requestId = "r1", captureId, photoId, code, idx, total, chunk });

    private static JsonElement J(string? s) { Assert.NotNull(s); return JsonDocument.Parse(s!).RootElement.Clone(); }
    private StoredOrder Order(string track) => OrderRepository.Load(Db).Single(o => o.Order.Track == track);
    private long PhotoRows()
    {
        using var c = new SqliteConnection($"Data Source={Db};Pooling=False"); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM photos WHERE source='phone'";
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public void Photo_attaches_to_parcel_acks_with_photo_stage_and_captureId_and_keeps_status()
    {
        Seed(("O1", "A", ""));
        var ack = J(SyncReceiver.Handle(Db, Room, Photo("cap1", "a", Uri(Jpg)), Now));

        Assert.Equal("pc_ack", ack.GetProperty("type").GetString());
        Assert.Equal("photo", ack.GetProperty("ackStage").GetString());
        Assert.Equal("cap1", ack.GetProperty("captureId").GetString());
        Assert.Equal("success", ack.GetProperty("outcome").GetString());
        Assert.True(ack.GetProperty("photoSaved").GetBoolean());        // điện thoại chỉ coi ảnh đã lưu khi thấy cờ này (quy tắc 6)
        var o = Order("A");
        Assert.Equal("Chưa trả", o.Order.Status);                 // ảnh KHÔNG tự đổi trạng thái nhận kiện
        Assert.Equal(Jpg, PhotoRepository.Get(Db, o.Order.PhotoId)!.Bytes);
    }

    [Fact]
    public void Photo_after_scan_keeps_received_time_and_old_photo()
    {
        Seed(("O1", "A", ""));
        ScanReceiver.Receive(Db, "A", Now, new byte[] { 9, 9 });
        var oldId = Order("A").Order.PhotoId;

        SyncReceiver.Handle(Db, Room, Photo("cap1", "A", Uri(Jpg)), Now.AddHours(1));

        Assert.Equal("23/11/2026 12:00:00", Order("A").Order.RecvDate);
        Assert.Equal(new byte[] { 9, 9 }, PhotoRepository.Get(Db, oldId)!.Bytes); // ảnh cũ còn nguyên
        Assert.Equal(Jpg, PhotoRepository.Get(Db, Order("A").Order.PhotoId)!.Bytes);
    }

    [Fact]
    public void Redelivery_of_same_captureId_returns_same_ack_and_stores_once()
    {
        Seed(("O1", "A", ""));
        var a = SyncReceiver.Handle(Db, Room, Photo("cap1", "A", Uri(Jpg)), Now);
        var b = SyncReceiver.Handle(Db, Room, Photo("cap1", "A", Uri(Jpg)), Now.AddMinutes(5));
        Assert.Equal(a, b);
        Assert.Equal(1, PhotoRows());
    }

    [Fact]
    public void Two_different_photos_in_same_second_do_not_collide()
    {
        Seed(("O1", "A", ""));
        SyncReceiver.Handle(Db, Room, Photo("cap1", "A", Uri(Jpg)), Now);
        SyncReceiver.Handle(Db, Room, Photo("cap2", "A", Uri(new byte[] { 0xFF, 0xD8, 7 })), Now);
        Assert.Equal(2, PhotoRows());
    }

    [Fact]
    public void Ambiguous_or_unknown_code_saves_nothing_and_reports()
    {
        Seed(("O1", "A", "S"), ("O1", "B", "S"));
        Assert.Equal("ambiguous", J(SyncReceiver.Handle(Db, Room, Photo("c1", "S", Uri(Jpg)), Now)).GetProperty("outcome").GetString());
        Assert.Equal("not_found", J(SyncReceiver.Handle(Db, Room, Photo("c2", "ZZZ", Uri(Jpg)), Now)).GetProperty("outcome").GetString());
        Assert.Equal(0, PhotoRows());
    }

    [Theory]
    [InlineData("data:text/html;base64,PGI+eDwvYj4=")]            // không phải ảnh
    [InlineData("data:image/jpeg;base64,@@@khong-phai-base64")]   // base64 hỏng
    [InlineData("không phải data uri")]
    [InlineData("data:image/jpeg;base64,")]                        // rỗng
    public void Invalid_image_payload_is_rejected_with_error_ack_and_not_stored(string badUri)
    {
        Seed(("O1", "A", ""));
        var ack = J(SyncReceiver.Handle(Db, Room, Photo("c1", "A", badUri), Now));
        Assert.Equal("error", ack.GetProperty("outcome").GetString());
        Assert.Equal(0, PhotoRows());
        Assert.Equal("", Order("A").Order.PhotoId);
    }

    [Fact]
    public void Oversized_photo_is_rejected()
    {
        Seed(("O1", "A", ""));
        var big = new byte[SyncProtocol.MaxPhotoBytes + 1];
        big[0] = 0xFF; big[1] = 0xD8;
        var ack = J(SyncReceiver.Handle(Db, Room, Photo("c1", "A", Uri(big)), Now));
        Assert.Equal("error", ack.GetProperty("outcome").GetString());
        Assert.Equal(0, PhotoRows());
    }

    [Fact]
    public void Photo_for_another_room_is_ignored()
    {
        Seed(("O1", "A", ""));
        Assert.Null(SyncReceiver.Handle(Db, Room, Photo("c1", "A", Uri(Jpg), room: "ROOM-KHAC"), Now));
        Assert.Equal(0, PhotoRows());
    }

    [Fact]
    public void Chunks_in_any_order_assemble_then_save_once_and_ack_only_when_complete()
    {
        Seed(("O1", "A", ""));
        var uri = Uri(Jpg);
        var parts = new[] { uri[..10], uri[10..20], uri[20..] };

        Assert.Null(SyncReceiver.Handle(Db, Room, Chunk("ph1", "cap1", "A", 2, 3, parts[2]), Now));   // chưa đủ: KHÔNG ack
        Assert.Null(SyncReceiver.Handle(Db, Room, Chunk("ph1", "cap1", "A", 0, 3, parts[0]), Now));
        Assert.Null(SyncReceiver.Handle(Db, Room, Chunk("ph1", "cap1", "A", 0, 3, parts[0]), Now));   // mảnh lặp: bỏ qua
        Assert.Equal(0, PhotoRows());

        var ack = J(SyncReceiver.Handle(Db, Room, Chunk("ph1", "cap1", "A", 1, 3, parts[1]), Now));
        Assert.Equal("photo", ack.GetProperty("ackStage").GetString());
        Assert.Equal("success", ack.GetProperty("outcome").GetString());
        Assert.True(ack.GetProperty("photoSaved").GetBoolean());
        Assert.Equal("cap1", ack.GetProperty("captureId").GetString());
        Assert.Equal(1, PhotoRows());
        Assert.Equal(Jpg, PhotoRepository.Get(Db, Order("A").Order.PhotoId)!.Bytes);
    }

    [Fact]
    public void Chunk_with_absurd_total_or_bad_index_is_ignored()
    {
        Seed(("O1", "A", ""));
        Assert.Null(SyncReceiver.Handle(Db, Room, Chunk("p", "c", "A", 0, 100000, "x"), Now));
        Assert.Null(SyncReceiver.Handle(Db, Room, Chunk("p", "c", "A", 5, 3, "x"), Now));
        Assert.Null(SyncReceiver.Handle(Db, Room, Chunk("p", "c", "A", -1, 3, "x"), Now));
        Assert.Equal(0, PhotoRows());
    }

    [Fact]
    public async Task Service_handles_photo_over_transport_and_raises_event_with_stage()
    {
        Seed(("O1", "A", ""));
        var transport = new InMemoryTransport();
        var inbox = new List<string>();
        transport.Subscribe(Room, (_, m) => inbox.Add(m));
        using var svc = new SyncService(Db, Room, transport, () => Now);
        var events = new List<SyncHandled>();
        svc.Handled += events.Add;
        svc.Start();

        await transport.PublishAsync(Room, Photo("cap1", "A", Uri(Jpg)));

        Assert.Contains(inbox, m => m.Contains("\"ackStage\":\"photo\""));
        Assert.Equal("photo", events.Single().Stage);
    }
    [Theory]
    [InlineData("success", true)]
    [InlineData("error", false)]
    [InlineData("not_found", false)]
    [InlineData("ambiguous", false)]
    public void PhotoAck_photoSaved_is_true_only_when_the_photo_was_really_saved(string outcome, bool expected)
    {
        var ack = JsonDocument.Parse(QuanLyHangHoan.Data.Sync.SyncProtocol.BuildPhotoAck("R", "rq", "cap", "CODE", outcome, "m")).RootElement;
        Assert.Equal(expected, ack.GetProperty("photoSaved").GetBoolean());
    }
}
