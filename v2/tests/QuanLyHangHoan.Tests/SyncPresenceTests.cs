using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;
using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.Tests;

/// <summary>Báo "PC đang online" cho điện thoại cũ: pc_hello khi khởi động / khi điện thoại vào phòng, pc_heartbeat định kỳ. Dữ liệu GIẢ.</summary>
public sealed class SyncPresenceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private const string Room = "ROOM-PRES";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-pres-" + Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_dir, "t.db");

    public SyncPresenceTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "du_lieu_hang_hoan.json"), "[]");
        LegacyImporter.Import(_dir, Db);
    }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static async Task<bool> WaitFor(Func<bool> cond, int ms = 3000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (cond()) return true; await Task.Delay(10); }
        return cond();
    }

    private static List<JsonElement> Of(List<string> inbox, string type)
    {
        lock (inbox) return inbox.Select(m => JsonDocument.Parse(m).RootElement.Clone())
            .Where(e => e.TryGetProperty("type", out var t) && t.GetString() == type).ToList();
    }

    private static List<string> Listen(InMemoryTransport t, string room = Room)
    {
        var inbox = new List<string>();
        t.Subscribe(room, (_, m) => { lock (inbox) inbox.Add(m); });
        return inbox;
    }

    [Fact]
    public void Start_announces_pc_hello_with_room()
    {
        var t = new InMemoryTransport();
        var inbox = Listen(t);
        using var svc = new SyncService(Db, Room, t, () => Now, heartbeatInterval: TimeSpan.Zero);
        svc.Start();

        var hello = Of(inbox, "pc_hello").Single();
        Assert.Equal(Room, hello.GetProperty("room").GetString());
    }

    [Fact]
    public async Task Phone_connected_gets_a_pc_hello_reply_only_for_same_room()
    {
        var t = new InMemoryTransport();
        var inbox = Listen(t);
        using var svc = new SyncService(Db, Room, t, () => Now, heartbeatInterval: TimeSpan.Zero);
        svc.Start();
        var before = Of(inbox, "pc_hello").Count;

        await t.PublishAsync(Room, JsonSerializer.Serialize(new { type = "phone_connected", room = Room, time = 1 }));
        Assert.Equal(before + 1, Of(inbox, "pc_hello").Count);

        await t.PublishAsync(Room, JsonSerializer.Serialize(new { type = "phone_connected", room = "PHONG-KHAC", time = 1 }));
        Assert.Equal(before + 1, Of(inbox, "pc_hello").Count); // khác phòng: im lặng
    }

    [Fact]
    public async Task Heartbeat_is_sent_periodically_and_stops_after_dispose()
    {
        var t = new InMemoryTransport();
        var inbox = Listen(t);
        var svc = new SyncService(Db, Room, t, () => Now, heartbeatInterval: TimeSpan.FromMilliseconds(30));
        svc.Start();

        Assert.True(await WaitFor(() => Of(inbox, "pc_heartbeat").Count >= 3));
        Assert.Equal(Room, Of(inbox, "pc_heartbeat")[0].GetProperty("room").GetString());

        svc.Dispose();
        await Task.Delay(100);
        var after = Of(inbox, "pc_heartbeat").Count;
        await Task.Delay(150);
        Assert.Equal(after, Of(inbox, "pc_heartbeat").Count);
    }

    [Fact]
    public async Task Zero_interval_means_no_heartbeat()
    {
        var t = new InMemoryTransport();
        var inbox = Listen(t);
        using var svc = new SyncService(Db, Room, t, () => Now, heartbeatInterval: TimeSpan.Zero);
        svc.Start();
        await Task.Delay(150);
        Assert.Empty(Of(inbox, "pc_heartbeat"));
    }

    [Fact]
    public async Task Presence_messages_never_raise_handled_or_loop_on_themselves()
    {
        var t = new InMemoryTransport();
        var inbox = Listen(t);
        using var svc = new SyncService(Db, Room, t, () => Now, heartbeatInterval: TimeSpan.FromMilliseconds(30));
        var handled = 0;
        svc.Handled += _ => handled++;
        svc.Start();

        await t.PublishAsync(Room, JsonSerializer.Serialize(new { type = "phone_connected", room = Room }));
        await Task.Delay(200);

        Assert.Equal(0, handled);
        // pc_hello do chính PC gửi ra không được kích hoạt thêm pc_hello nào nữa (không vòng lặp)
        Assert.True(Of(inbox, "pc_hello").Count <= 3);
    }

    [Fact]
    public async Task Scan_ack_still_works_alongside_presence()
    {
        File.WriteAllText(Path.Combine(_dir, "du_lieu_hang_hoan.json"), JsonSerializer.Serialize(new[]
            { new { platform = "Shopee", orderType = "x", orderId = "O1", track = "A", status = "Chưa trả", price = 1, items = Array.Empty<object>() } }));
        LegacyImporter.Import(_dir, Db);
        var t = new InMemoryTransport();
        var inbox = Listen(t);
        using var svc = new SyncService(Db, Room, t, () => Now, heartbeatInterval: TimeSpan.FromMilliseconds(30));
        svc.Start();

        await t.PublishAsync(Room, JsonSerializer.Serialize(new { type = "barcode", room = Room, requestId = "r1", code = "A" }));

        Assert.Equal("success", Of(inbox, "pc_ack").Single().GetProperty("outcome").GetString());
    }
}
