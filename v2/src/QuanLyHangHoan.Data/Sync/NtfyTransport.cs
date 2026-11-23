using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace QuanLyHangHoan.Data.Sync;

/// <summary>
/// Bộ truyền tin ntfy (HTTPS) tương thích app cũ: topic `agy_shopee_&lt;phòng&gt;`; gửi = POST nội dung bản tin vào topic;
/// nghe = GET `/&lt;topic&gt;/sse` (luồng dòng `data: {json}`, chỉ event "message" là bản tin).
/// ntfy giới hạn 4096 byte/bản tin (ảnh phải chia mảnh phía điện thoại). Tự nối lại; nối lại thì xin phần bị lỡ bằng `since=&lt;id cuối&gt;`
/// (phát lại bản tin cũ là an toàn vì SyncReceiver chống lặp). Chỉ mở kết nối cho phòng đã có người đăng ký.
/// CẢNH BÁO: ntfy.sh công cộng, không mã hóa: xem docs/sync-design.md.
/// </summary>
public sealed class NtfyTransport : ISyncTransport, IDisposable
{
    public const string DefaultBaseUrl = "https://ntfy.sh";
    public const int MaxMessageBytes = 4096;

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Action<string, string>>> _handlers = new();
    private readonly ConcurrentDictionary<string, bool> _listening = new();
    private bool _started;

    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(3);
    public string LastError { get; private set; } = "";
    public bool Connected { get; private set; }
    public event Action? StatusChanged;

    public NtfyTransport(HttpClient? http = null, string baseUrl = DefaultBaseUrl)
    {
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; // luồng SSE sống lâu: hạn giờ đặt theo từng yêu cầu gửi
        _baseUrl = baseUrl.TrimEnd('/');
    }

    private string TopicUrl(string room) => $"{_baseUrl}/{SyncRoom.NtfyTopic(room)}";

    public void Start()
    {
        _started = true;
        foreach (var room in _handlers.Where(kv => !kv.Value.IsEmpty).Select(kv => kv.Key)) Listen(room);
    }

    public IDisposable Subscribe(string room, Action<string, string> onMessage)
    {
        var id = Guid.NewGuid();
        var set = _handlers.GetOrAdd(room, _ => new());
        set[id] = onMessage;
        if (_started) Listen(room);
        return new Unsub(() => set.TryRemove(id, out _));
    }

    private void Listen(string room)
    {
        if (_listening.TryAdd(room, true)) _ = Task.Run(() => SseLoop(room));
    }

    private async Task SseLoop(string room)
    {
        string? lastId = null;
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var url = TopicUrl(room) + "/sse" + (lastId is null ? "" : "?since=" + Uri.EscapeDataString(lastId));
                using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, _cts.Token);
                resp.EnsureSuccessStatusCode();
                SetConnected(true);

                using var stream = await resp.Content.ReadAsStreamAsync(_cts.Token);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                string? line;
                while ((line = await reader.ReadLineAsync(_cts.Token)) is not null)
                {
                    if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                    var id = Dispatch(room, line[5..].Trim());
                    if (id is not null) lastId = id;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { LastError = ex.Message; }

            SetConnected(false);
            try { await Task.Delay(ReconnectDelay, _cts.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <returns>id của bản tin nếu là event "message", ngược lại null.</returns>
    private string? Dispatch(string room, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            if (!r.TryGetProperty("event", out var ev) || ev.GetString() != "message") return null;
            var text = r.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
            var id = r.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;

            if (_handlers.TryGetValue(room, out var set))
                foreach (var h in set.Values.ToList())
                    try { h(room, text); } catch (Exception ex) { LastError = ex.Message; }
            return id;
        }
        catch (JsonException) { return null; }
    }

    public async Task PublishAsync(string room, string message)
    {
        if (Encoding.UTF8.GetByteCount(message) > MaxMessageBytes)
        {
            LastError = $"Bản tin vượt {MaxMessageBytes} byte, ntfy không nhận.";
            return;
        }
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var resp = await _http.PostAsync(TopicUrl(room), new StringContent(message, Encoding.UTF8, "text/plain"), timeout.Token);
            resp.EnsureSuccessStatusCode();
            LastError = "";
        }
        catch (Exception ex) { LastError = "ntfy: " + ex.Message; }
    }

    private void SetConnected(bool value)
    {
        if (Connected == value) return;
        Connected = value;
        StatusChanged?.Invoke();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _http.Dispose();
        _cts.Dispose();
    }

    private sealed class Unsub(Action a) : IDisposable { public void Dispose() => a(); }
}

/// <summary>Gộp nhiều đường truyền (MQTT + ntfy): gửi tới tất cả, nghe từ tất cả. Một đường lỗi không chặn đường khác.</summary>
public sealed class CompositeTransport : ISyncTransport, IDisposable
{
    private readonly ISyncTransport[] _parts;
    public CompositeTransport(params ISyncTransport[] parts) => _parts = parts;

    public async Task PublishAsync(string room, string message)
    {
        foreach (var p in _parts)
            try { await p.PublishAsync(room, message); } catch (Exception) { /* đường này lỗi: các đường khác vẫn gửi */ }
    }

    public async Task PublishBulkAsync(string room, string message, bool includeSlowChannels)
    {
        foreach (var p in _parts)
        {
            if (!includeSlowChannels && p is NtfyTransport) continue;
            try { await p.PublishAsync(room, message); } catch (Exception) { /* đường này lỗi: các đường khác vẫn gửi */ }
        }
    }

    public IDisposable Subscribe(string room, Action<string, string> onMessage)
    {
        var subs = _parts.Select(p => p.Subscribe(room, onMessage)).ToList();
        return new Multi(subs);
    }

    public void Dispose()
    {
        foreach (var p in _parts) (p as IDisposable)?.Dispose();
    }

    private sealed class Multi(List<IDisposable> subs) : IDisposable
    {
        public void Dispose() { foreach (var s in subs) s.Dispose(); }
    }
}
