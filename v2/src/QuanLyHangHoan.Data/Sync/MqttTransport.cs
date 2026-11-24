using System.Collections.Concurrent;
using System.Text;
using MQTTnet;
using MQTTnet.Client;

namespace QuanLyHangHoan.Data.Sync;

/// <summary>
/// Bộ truyền tin MQTT qua WebSocket (cổng TLS), tương thích app cũ: cùng topic `agy_shopee_return_scanner_2026/&lt;phòng&gt;`
/// và cùng 3 broker công cộng (gửi/nghe đồng thời để dự phòng; bản tin trùng đã được SyncReceiver chống lặp theo requestId).
/// Tự kết nối lại. KHÔNG ném lỗi khi chưa kết nối: ghi LastError, gọi Start() trước.
/// CẢNH BÁO: broker công cộng, bản tin không mã hóa: xem docs/sync-design.md (đổi mã phòng sang chuỗi mạnh).
/// </summary>
public sealed class MqttTransport : ISyncTransport, IDisposable
{
    public const string TopicPrefix = "agy_shopee_return_scanner_2026/";
    public static readonly string[] DefaultBrokers =
    {
        "wss://broker.emqx.io:8084/mqtt", "wss://broker.hivemq.com:8884/mqtt", "wss://test.mosquitto.org:8081/",
    };

    /// <summary>Tách "wss://user:pass@host/mqtt" thành địa chỉ không chứa mật khẩu + tên người dùng + mật khẩu (broker riêng).</summary>
    public static (string Uri, string? User, string? Pass) SplitCredentials(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || string.IsNullOrEmpty(u.UserInfo)) return (uri, null, null);
        var parts = u.UserInfo.Split(':', 2);
        var clean = new UriBuilder(u) { UserName = "", Password = "" }.Uri.ToString();
        return (clean, Uri.UnescapeDataString(parts[0]), parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "");
    }

    /// <summary>Che mật khẩu khi hiển thị lỗi/nhật ký.</summary>
    public static string MaskUri(string uri)
    {
        var (clean, user, _) = SplitCredentials(uri);
        return user is null ? uri : clean;
    }

    public static string TopicFor(string room) => TopicPrefix + room;

    private sealed class Link
    {
        public required string Uri { get; init; }
        public IMqttClient Client { get; set; } = null!;
    }

    private readonly List<Link> _links = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Action<string, string>>> _handlers = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _clientId = "qlhh-" + Guid.NewGuid().ToString("N")[..12];
    private bool _started;

    public MqttTransport(IEnumerable<string> brokerUris)
    {
        foreach (var uri in brokerUris) _links.Add(new Link { Uri = uri });
    }

    public int ConnectedBrokers => _links.Count(l => l.Client?.IsConnected == true);
    public string LastError { get; private set; } = "";
    public event Action? StatusChanged;

    public void Start()
    {
        if (_started) return;
        _started = true;
        var factory = new MqttFactory();
        foreach (var link in _links)
        {
            link.Client = factory.CreateMqttClient();
            link.Client.ApplicationMessageReceivedAsync += e =>
            {
                var topic = e.ApplicationMessage.Topic ?? "";
                if (topic.StartsWith(TopicPrefix, StringComparison.Ordinal))
                {
                    var room = topic[TopicPrefix.Length..];
                    var text = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
                    if (_handlers.TryGetValue(room, out var set))
                        foreach (var h in set.Values.ToList())
                            try { h(room, text); } catch (Exception ex) { LastError = ex.Message; }
                }
                return Task.CompletedTask;
            };
            link.Client.DisconnectedAsync += async _ =>
            {
                StatusChanged?.Invoke();
                await ReconnectLoop(link);
            };
            _ = ReconnectLoop(link);
        }
    }

    private async Task ReconnectLoop(Link link)
    {
        while (!_cts.IsCancellationRequested && !link.Client.IsConnected)
        {
            try
            {
                var (cleanUri, user, pass) = SplitCredentials(link.Uri);
                var builder = new MqttClientOptionsBuilder()
                    .WithClientId(_clientId)
                    .WithWebSocketServer(o => o.WithUri(cleanUri))
                    .WithTlsOptions(o => o.UseTls())
                    .WithCleanSession()
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                    .WithTimeout(TimeSpan.FromSeconds(8));
                if (user is not null) builder = builder.WithCredentials(user, pass ?? "");
                var options = builder.Build();
                await link.Client.ConnectAsync(options, _cts.Token);
                foreach (var room in _handlers.Where(kv => !kv.Value.IsEmpty).Select(kv => kv.Key))
                    await SubscribeTopic(link, room);
                StatusChanged?.Invoke();
                return;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { LastError = $"{MaskUri(link.Uri)}: {ex.Message}"; }

            try { await Task.Delay(TimeSpan.FromSeconds(3), _cts.Token); }
            catch (OperationCanceledException) { return; }
        }
    }

    private static async Task SubscribeTopic(Link link, string room)
    {
        if (link.Client.IsConnected)
            await link.Client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(TopicFor(room)).Build());
    }

    public async Task PublishAsync(string room, string message)
    {
        var sent = 0;
        foreach (var link in _links.Where(l => l.Client?.IsConnected == true))
        {
            try
            {
                await link.Client.PublishAsync(new MqttApplicationMessageBuilder()
                    .WithTopic(TopicFor(room)).WithPayload(message).Build(), _cts.Token);
                sent++;
            }
            catch (Exception ex) { LastError = $"{MaskUri(link.Uri)}: {ex.Message}"; }
        }
        if (sent == 0 && string.IsNullOrEmpty(LastError)) LastError = "Chưa kết nối broker nào.";
        else if (sent == 0 && ConnectedBrokers == 0) LastError = "Chưa kết nối broker nào. " + LastError;
    }

    public IDisposable Subscribe(string room, Action<string, string> onMessage)
    {
        var id = Guid.NewGuid();
        var set = _handlers.GetOrAdd(room, _ => new());
        set[id] = onMessage;
        foreach (var link in _links.Where(l => l.Client?.IsConnected == true))
            _ = SubscribeTopic(link, room);
        return new Unsub(() => set.TryRemove(id, out _));
    }

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var l in _links)
            try { l.Client?.Dispose(); } catch { /* bỏ qua khi đóng */ }
        _cts.Dispose();
    }

    private sealed class Unsub(Action a) : IDisposable { public void Dispose() => a(); }
}
