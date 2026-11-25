using System.Text.Json;

namespace QuanLyHangHoan.Data.Sync;

/// <summary>
/// Cổng truyền tin (MQTT / ntfy / relay riêng...). Lõi chỉ biết "gửi chuỗi JSON vào phòng" và "nghe chuỗi JSON của phòng".
/// Bản thật (MQTT, ntfy) cài giao diện này ở bước sau; bản trong bộ nhớ dùng cho test và chạy thử cục bộ.
/// </summary>
public interface ISyncTransport
{
    Task PublishAsync(string room, string message);
    IDisposable Subscribe(string room, Action<string, string> onMessage);

    /// <summary>
    /// Gói lớn gồm nhiều mảnh (danh sách đơn gửi điện thoại). Mặc định giống PublishAsync.
    /// Bộ gộp nhiều đường chỉ gửi qua đường nhanh (MQTT) trừ khi <paramref name="includeSlowChannels"/> = true,
    /// vì ntfy.sh giới hạn số tin và sẽ từ chối cả tin xác nhận quét nếu bị dồn hàng trăm mảnh.
    /// </summary>
    Task PublishBulkAsync(string room, string message, bool includeSlowChannels) => PublishAsync(room, message);
}

/// <summary>Truyền tin trong bộ nhớ: gửi tới mọi người đang nghe cùng phòng (đồng bộ, theo thứ tự).</summary>
public sealed class InMemoryTransport : ISyncTransport
{
    private readonly object _lock = new();
    private readonly List<(string Room, Action<string, string> Handler)> _subs = new();

    public Task PublishAsync(string room, string message)
    {
        List<Action<string, string>> targets;
        lock (_lock) targets = _subs.Where(s => s.Room == room).Select(s => s.Handler).ToList();
        foreach (var h in targets) h(room, message);
        return Task.CompletedTask;
    }

    public IDisposable Subscribe(string room, Action<string, string> onMessage)
    {
        var entry = (room, onMessage);
        lock (_lock) _subs.Add(entry);
        return new Unsub(() => { lock (_lock) _subs.Remove(entry); });
    }

    private sealed class Unsub(Action a) : IDisposable { public void Dispose() => a(); }
}

/// <param name="Kind">"new_order" = điện thoại vừa tạo đơn mới ở bảng chính (giao diện nên tải lại bảng + báo); "" = thao tác thường.</param>
public sealed record SyncHandled(string RequestId, string Code, string Outcome, string Message, DateTime At, string Stage = "scan", string Kind = "");

/// <summary>
/// Nghe một phòng, xử lý bản tin quét/ảnh bằng SyncReceiver rồi gửi xác nhận lại cùng phòng.
/// Đồng thời báo "PC đang online" cho điện thoại cũ (nó coi PC offline nếu 45 giây không thấy gì):
/// gửi pc_hello khi khởi động và khi điện thoại vào phòng (phone_connected), pc_heartbeat định kỳ (mặc định 15 giây).
/// Không gửi danh sách mã vận đơn (manifest_sync): sẽ lộ dữ liệu qua broker công cộng.
/// </summary>
public sealed class SyncService : IDisposable
{
    public static readonly TimeSpan DefaultHeartbeat = TimeSpan.FromSeconds(15);

    private readonly string _db, _room;
    private readonly ISyncTransport _transport;
    private readonly Func<DateTime> _clock;
    private readonly TimeSpan _heartbeat;
    private IDisposable? _sub;
    private CancellationTokenSource? _cts;

    // ---- danh sách đơn gửi app iPhone (xem PhoneSnapshot) ----
    public static readonly TimeSpan DefaultSnapshotPoll = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultChunkDelay = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan PhoneActiveWindow = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FullEvery = TimeSpan.FromMinutes(10);
    private readonly TimeSpan _snapPoll, _chunkDelay;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private PhoneSnapshot.Base? _base;
    private int _seq;
    private string _lastDeltaSig = "", _lastWatchSig = "", _lastDbSig = "";
    private DateTime _lastActivityUtc = DateTime.MinValue, _lastFullUtc = DateTime.MinValue;

    /// <param name="heartbeatInterval">null = mặc định 15 giây; TimeSpan.Zero = không gửi heartbeat.</param>
    /// <param name="snapshotPoll">null = 5 giây; TimeSpan.Zero = không tự theo dõi thay đổi để gửi phần thay đổi (vẫn trả lời snapshot_request).</param>
    /// <param name="chunkDelay">null = 40 ms giữa các mảnh (tránh dồn broker); test đặt TimeSpan.Zero.</param>
    public SyncService(string dbPath, string room, ISyncTransport transport, Func<DateTime> clock, TimeSpan? heartbeatInterval = null,
        TimeSpan? snapshotPoll = null, TimeSpan? chunkDelay = null)
    {
        _db = dbPath; _room = room; _transport = transport; _clock = clock;
        _heartbeat = heartbeatInterval ?? DefaultHeartbeat;
        _snapPoll = snapshotPoll ?? DefaultSnapshotPoll;
        _chunkDelay = chunkDelay ?? DefaultChunkDelay;
    }

    public void Start()
    {
        if (_sub is not null) return;
        _sub = _transport.Subscribe(_room, OnMessage);
        Announce("pc_hello");

        if (_snapPoll > TimeSpan.Zero)
        {
            _pollCts = new CancellationTokenSource();
            var pt = _pollCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var timer = new PeriodicTimer(_snapPoll);
                    while (await timer.WaitForNextTickAsync(pt)) await PollAsync();
                }
                catch (OperationCanceledException) { /* dừng bình thường */ }
                catch (Exception) { /* theo dõi thay đổi lỗi không được làm hỏng dịch vụ */ }
            });
        }

        if (_heartbeat > TimeSpan.Zero)
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var timer = new PeriodicTimer(_heartbeat);
                    while (await timer.WaitForNextTickAsync(token)) Announce("pc_heartbeat");
                }
                catch (OperationCanceledException) { /* dừng bình thường */ }
                catch (Exception) { /* nhịp báo lỗi không được làm hỏng dịch vụ */ }
            });
        }
    }

    private void Announce(string type)
    {
        var msg = JsonSerializer.Serialize(new { type, room = _room, time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        _ = _transport.PublishAsync(_room, msg);
    }

    /// <summary>Phát SAU khi đã lưu và đã gửi xác nhận; giao diện dùng để tải lại màn hình / ghi nhật ký.</summary>
    public event Action<SyncHandled>? Handled;

    private CancellationTokenSource? _pollCts;

    private void OnMessage(string room, string message)
    {
        var type = TypeOf(message, out var sameRoom);
        // Chỉ gửi danh sách khi một app điện thoại ĐÃ xin (phone_connected / snapshot_request): trang quét cũ không cần và không nhận.
        if (sameRoom && (type == "phone_connected" || type == "snapshot_request")) _phoneWantsList = true;
        if (sameRoom && _phoneWantsList && type.Length > 0 && !type.StartsWith("pc_", StringComparison.Ordinal) && type != "latency_pong"
            && (type == "snapshot_request" || !type.StartsWith("snapshot_", StringComparison.Ordinal)) && type != "watch_sync" && type != "manifest_sync")
            _lastActivityUtc = DateTime.UtcNow; // điện thoại đang dùng: bật gửi phần thay đổi định kỳ

        if (type == "phone_connected" && sameRoom)
        {
            Announce("pc_hello"); // điện thoại vừa vào phòng: báo ngay PC đang online
            _ = Task.Run(async () => { await Task.Delay(900); await SendSnapshotAsync(force: false); });
            return;
        }

        if (type == "latency_ping" && sameRoom)
        {
            // Đo độ trễ: trả lại đúng mã điện thoại gửi (không đụng dữ liệu).
            try
            {
                using var d = JsonDocument.Parse(message);
                var id = d.RootElement.TryGetProperty("id", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                _ = _transport.PublishAsync(_room, JsonSerializer.Serialize(new { type = "pc_pong", room = _room, id }));
            }
            catch (Exception) { /* bản tin lỗi: bỏ qua */ }
            return;
        }

        if (type == "snapshot_request" && sameRoom)
        {
            _ = Task.Run(() => SendSnapshotAsync(force: true));
            return;
        }

        var now = _clock();

        var action = PhoneActions.Handle(_db, _room, message, now);
        if (action is not null)
        {
            _ = _transport.PublishAsync(_room, action.Ack);
            Raise(new SyncHandled(action.RequestId, action.Code, action.Outcome, action.Message, now, action.Stage, action.Kind));
            if (action.Changed) ScheduleDelta();
            return;
        }

        var ack = SyncReceiver.Handle(_db, _room, message, now);
        if (ack is null) return;

        _ = _transport.PublishAsync(_room, ack);
        try
        {
            using var doc = JsonDocument.Parse(ack);
            var r = doc.RootElement;
            string S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            Raise(new SyncHandled(S("requestId"), S("code"), S("outcome"), S("extra"), now, S("ackStage") is { Length: > 0 } st ? st : "scan"));
            if (S("outcome") == "success" && S("ackStage") is "scan" or "") ScheduleDelta(); // kiện vừa nhận: điện thoại thấy ngay
        }
        catch (Exception) { /* người nghe sự kiện lỗi không được làm hỏng luồng nhận */ }
    }

    private void Raise(SyncHandled h)
    {
        try { Handled?.Invoke(h); }
        catch (Exception) { /* người nghe sự kiện lỗi không được làm hỏng luồng nhận */ }
    }

    private string TypeOf(string message, out bool sameRoom)
    {
        sameRoom = false;
        try
        {
            using var doc = JsonDocument.Parse(message);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return "";
            sameRoom = r.TryGetProperty("room", out var rm) && rm.ValueKind == JsonValueKind.String && rm.GetString() == _room;
            return r.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
        }
        catch (JsonException) { return ""; }
    }

    // ================= gửi danh sách đơn cho app iPhone =================
    private int _deltaScheduled;
    private volatile bool _phoneWantsList;

    /// <summary>Hẹn gửi phần thay đổi sau ~0,3 giây (gộp nhiều thay đổi liên tiếp).</summary>
    /// <summary>Báo dữ liệu vừa đổi từ bên ngoài (nạp Sheet/Excel/thư mục/Salework): gửi phần thay đổi cho điện thoại ngay (~0,3 giây) thay vì đợi lần thăm dò kế tiếp.
    /// Vẫn chỉ gửi khi điện thoại đang hoạt động (đã xin danh sách gần đây).</summary>
    public void NotifyDataChanged() => ScheduleDelta();

    private void ScheduleDelta()
    {
        if (!_phoneWantsList || DateTime.UtcNow - _lastActivityUtc > PhoneActiveWindow) return;
        if (Interlocked.Exchange(ref _deltaScheduled, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(300); } finally { Interlocked.Exchange(ref _deltaScheduled, 0); }
            try { await SendSnapshotAsync(force: false); } catch (Exception) { /* lần thăm dò sau sẽ gửi lại */ }
        });
    }

    private string DbSignature()
    {
        try
        {
            string One(string p) => File.Exists(p) ? $"{File.GetLastWriteTimeUtc(p).Ticks}:{new FileInfo(p).Length}" : "-";
            return One(_db) + "|" + One(_db + "-wal");
        }
        catch (Exception) { return ""; }
    }

    private async Task PollAsync()
    {
        if (DateTime.UtcNow - _lastActivityUtc > PhoneActiveWindow) return; // điện thoại không hoạt động: không gửi
        var sig = DbSignature();
        var fullDue = DateTime.UtcNow - _lastFullUtc >= FullEvery;
        if (sig == _lastDbSig && !fullDue) return;
        if (await SendSnapshotAsync(force: false, full: fullDue)) _lastDbSig = sig;
    }

    /// <summary>
    /// Gửi danh sách cho điện thoại. `force` (điện thoại xin) hoặc chưa có bản gốc hoặc `full` => gửi ĐẦY ĐỦ (và cả ntfy khi force);
    /// ngược lại chỉ gửi phần thay đổi (nếu quá nhiều thay đổi thì gửi đầy đủ). Trả true nếu đã gửi hoặc không có gì mới.
    /// Đang gửi dở thì bỏ qua (lần thăm dò sau sẽ gửi lại), giống PC cũ.
    /// </summary>
    public async Task<bool> SendSnapshotAsync(bool force, bool full = false)
    {
        if (!await _sendGate.WaitAsync(0)) return false;
        try
        {
            var now = _clock();
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            try { ReturnWatchService.Sync(_db, now); } catch (Exception) { /* bảng theo dõi lỗi không được chặn việc gửi danh sách */ }
            var all = OrderRepository.Load(_db);
            var rows = PhoneSnapshot.BuildRows(all, now, ReturnWatchService.ActiveMarks(_db, all));
            var stats = PhoneSnapshot.BuildStats(all, rows.Count, now);

            var sendFull = force || full || _base is null;
            if (!sendFull)
            {
                var d = PhoneSnapshot.ComputeDelta(_base!, rows);
                if (d.Up.Count + d.Rm.Count > PhoneSnapshot.DeltaMaxRows) sendFull = true;
                else
                {
                    var sig = PhoneSnapshot.ToJson(new object[] { _base!.Sid, d.Up, d.Rm, stats });
                    if (sig != _lastDeltaSig) { _seq++; _lastDeltaSig = sig; }
                    else { await SendWatchAsync(all, nowMs); return true; } // không có gì mới
                    var b64 = PhoneSnapshot.DeflateBase64(PhoneSnapshot.DeltaPayloadJson(_base.Sid, _seq, rows.Count, d, stats, nowMs));
                    var msgs = PhoneSnapshot.Chunks("snapshot_delta", "deltaId", "delta_" + nowMs, _room, b64, nowMs);
                    await PublishPassesAsync(msgs, includeSlow: msgs.Count <= 3);
                    await SendWatchAsync(all, nowMs);
                    return true;
                }
            }

            var sid = "snap_" + nowMs;
            var full64 = PhoneSnapshot.DeflateBase64(PhoneSnapshot.FullPayloadJson(rows, stats, sid, nowMs));
            var fullMsgs = PhoneSnapshot.Chunks("snapshot_chunk", "snapshotId", sid, _room, full64, nowMs, rows.Count);
            await PublishPassesAsync(fullMsgs, includeSlow: force);
            _base = PhoneSnapshot.MakeBase(sid, rows);
            _seq = 0; _lastDeltaSig = ""; _lastFullUtc = DateTime.UtcNow;
            await SendWatchAsync(all, nowMs, forceSend: true);
            return true;
        }
        catch (Exception) { return false; }
        finally { _sendGate.Release(); }
    }

    // Gửi 2 lượt để bù mảnh rớt trên broker công cộng; điện thoại tự bỏ mảnh trùng.
    private async Task PublishPassesAsync(List<string> msgs, bool includeSlow)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var m in msgs)
            {
                await _transport.PublishBulkAsync(_room, m, includeSlow && pass == 0);
                if (_chunkDelay > TimeSpan.Zero) await Task.Delay(_chunkDelay);
            }
            if (pass == 0 && _chunkDelay > TimeSpan.Zero) await Task.Delay(TimeSpan.FromMilliseconds(300));
        }
    }

    private async Task SendWatchAsync(IReadOnlyList<StoredOrder> all, long nowMs, bool forceSend = false)
    {
        var list = PhoneSnapshot.BuildWatchList(all);
        var sig = PhoneSnapshot.ToJson(list);
        if (!forceSend && sig == _lastWatchSig) return;
        _lastWatchSig = sig;
        var msg = PhoneSnapshot.WatchSyncJson(_room, nowMs, list);
        await _transport.PublishBulkAsync(_room, msg, includeSlowChannels: System.Text.Encoding.UTF8.GetByteCount(msg) < 3500);
    }

    public void Dispose()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _sub?.Dispose();
        _sub = null;
    }
}
