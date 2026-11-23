using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.Tests;

/// <summary>ntfy qua máy chủ GIẢ (không mạng thật). SSE: dòng `data: {json}`; chỉ event "message" là bản tin.</summary>
public sealed class NtfyTransportTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string Body)> Requests { get; } = new();
        private int _n;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            int n;
            lock (Requests) { Requests.Add((request.Method, request.RequestUri!.ToString(), body)); n = ++_n; }
            return respond(request, n);
        }
    }

    private static HttpResponseMessage Sse(params string[] lines) => new(HttpStatusCode.OK)
    {
        Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n"))),
    };

    private static string Msg(string text) => "data: " + System.Text.Json.JsonSerializer.Serialize(new { id = "id" + text.GetHashCode(), time = 1, @event = "message", topic = "t", message = text });

    private static async Task<bool> WaitFor(Func<bool> cond, int ms = 3000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (cond()) return true; await Task.Delay(10); }
        return cond();
    }

    [Fact]
    public async Task Publish_posts_body_to_legacy_topic_url()
    {
        var h = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var t = new NtfyTransport(new HttpClient(h), "https://ntfy.example");

        await t.PublishAsync("SHP-2026", "{\"type\":\"pc_ack\"}");

        var r = h.Requests.Single();
        Assert.Equal(HttpMethod.Post, r.Method);
        Assert.Equal("https://ntfy.example/agy_shopee_shp-2026", r.Url);
        Assert.Equal("{\"type\":\"pc_ack\"}", r.Body);
        Assert.Equal("", t.LastError);
    }

    [Fact]
    public async Task Publish_over_4096_bytes_is_not_sent_and_reports_error()
    {
        var h = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var t = new NtfyTransport(new HttpClient(h), "https://ntfy.example");

        await t.PublishAsync("room", new string('x', 5000));

        Assert.Empty(h.Requests);
        Assert.Contains("4096", t.LastError);
    }

    [Fact]
    public async Task Publish_failure_is_recorded_not_thrown()
    {
        var h = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var t = new NtfyTransport(new HttpClient(h), "https://ntfy.example");

        await t.PublishAsync("room", "x");

        Assert.False(string.IsNullOrEmpty(t.LastError));
    }

    [Fact]
    public async Task Subscribe_delivers_only_message_events_with_room()
    {
        var h = new FakeHandler((_, n) => n == 1
            ? Sse("data: {\"event\":\"open\",\"topic\":\"t\"}", "", Msg("một"), "", "data: {\"event\":\"keepalive\"}", "", Msg("hai"), "")
            : Sse());
        using var t = new NtfyTransport(new HttpClient(h), "https://ntfy.example") { ReconnectDelay = TimeSpan.FromMilliseconds(10) };
        var got = new List<(string Room, string Msg)>();
        t.Subscribe("SHP-2026", (r, m) => { lock (got) got.Add((r, m)); });
        t.Start();

        Assert.True(await WaitFor(() => { lock (got) return got.Count >= 2; }));
        lock (got) Assert.Equal(new[] { ("SHP-2026", "một"), ("SHP-2026", "hai") }, got.Take(2).ToArray());
        Assert.Equal("https://ntfy.example/agy_shopee_shp-2026/sse", h.Requests[0].Url);
    }

    [Fact]
    public async Task Stream_end_reconnects_and_resumes_after_last_event_id()
    {
        var h = new FakeHandler((_, n) => n switch
        {
            1 => Sse("data: {\"id\":\"AAA\",\"event\":\"message\",\"message\":\"m1\"}", ""),
            2 => Sse("data: {\"id\":\"BBB\",\"event\":\"message\",\"message\":\"m2\"}", ""),
            _ => Sse(),
        });
        using var t = new NtfyTransport(new HttpClient(h), "https://ntfy.example") { ReconnectDelay = TimeSpan.FromMilliseconds(10) };
        var got = new List<string>();
        t.Subscribe("room", (_, m) => { lock (got) got.Add(m); });
        t.Start();

        Assert.True(await WaitFor(() => { lock (got) return got.Count >= 2; }));
        Assert.EndsWith("/sse", h.Requests[0].Url);              // lần đầu: chỉ nghe trực tiếp, không phát lại cũ
        Assert.Contains("since=AAA", h.Requests[1].Url);          // nối lại: lấy phần bị lỡ trong lúc mất kết nối
    }

    [Fact]
    public async Task Bad_json_and_handler_exceptions_do_not_stop_the_stream()
    {
        var h = new FakeHandler((_, n) => n == 1
            ? Sse("data: không phải json", "", Msg("x1"), "", Msg("x2"), "")
            : Sse());
        using var t = new NtfyTransport(new HttpClient(h), "https://ntfy.example") { ReconnectDelay = TimeSpan.FromMilliseconds(10) };
        var got = new List<string>();
        t.Subscribe("room", (_, m) => { lock (got) got.Add(m); if (m == "x1") throw new InvalidOperationException("lỗi người nghe"); });
        t.Start();

        Assert.True(await WaitFor(() => { lock (got) return got.Count >= 2; }));
    }

    [Fact]
    public async Task Unsubscribed_handler_receives_nothing_and_no_request_for_unused_room()
    {
        var h = new FakeHandler((_, _) => Sse());
        using var t = new NtfyTransport(new HttpClient(h), "https://ntfy.example") { ReconnectDelay = TimeSpan.FromMilliseconds(10) };
        t.Start();
        await Task.Delay(100);
        Assert.Empty(h.Requests); // chưa có ai đăng ký phòng nào: không mở kết nối
    }

    [Fact]
    public async Task Composite_publishes_to_all_and_delivers_from_all()
    {
        var a = new InMemoryTransport();
        var b = new InMemoryTransport();
        using var comp = new CompositeTransport(a, b);
        var got = new List<string>();
        comp.Subscribe("r", (_, m) => got.Add(m));

        await a.PublishAsync("r", "từ-a");
        await b.PublishAsync("r", "từ-b");
        Assert.Equal(new[] { "từ-a", "từ-b" }, got);

        var seenA = new List<string>(); var seenB = new List<string>();
        a.Subscribe("r", (_, m) => seenA.Add(m)); b.Subscribe("r", (_, m) => seenB.Add(m));
        await comp.PublishAsync("r", "gửi-đi");
        Assert.Contains("gửi-đi", seenA);
        Assert.Contains("gửi-đi", seenB);
    }

    [Fact]
    public async Task Composite_one_failing_transport_does_not_block_the_other()
    {
        var ok = new InMemoryTransport();
        using var comp = new CompositeTransport(new ThrowingTransport(), ok);
        var got = new List<string>();
        ok.Subscribe("r", (_, m) => got.Add(m));

        await comp.PublishAsync("r", "x");

        Assert.Equal(new[] { "x" }, got);
    }

    private sealed class ThrowingTransport : ISyncTransport
    {
        public Task PublishAsync(string room, string message) => throw new IOException("mất mạng");
        public IDisposable Subscribe(string room, Action<string, string> onMessage) => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}
