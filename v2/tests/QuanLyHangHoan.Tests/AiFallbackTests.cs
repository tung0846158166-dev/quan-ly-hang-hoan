using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Ai;

namespace QuanLyHangHoan.Tests;

/// <summary>Chuyển dự phòng giữa nhiều mô hình (Groq rồi Google): máy chủ GIẢ, không gọi mạng thật.</summary>
public sealed class AiFallbackTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 14, 0, 0);

    private sealed class Fake(Func<string, string, (HttpStatusCode, string)> reply) : HttpMessageHandler
    {
        public readonly List<string> Calls = new();   // "host|model"
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = (JsonObject)JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            var model = body["model"]!.GetValue<string>();
            Calls.Add(request.RequestUri!.Host + "|" + model);
            var (code, text) = reply(request.RequestUri!.Host, model);
            return new HttpResponseMessage(code) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    private static string Ok(string text) => new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = text } }),
    }.ToJsonString();

    private const string Limit = "{\"error\":{\"message\":\"Rate limit reached for model (TPM)\"}}";

    private static List<AiEndpoint> Chain() => new()
    {
        new("groq", AiChatService.GroqBaseUrl, "k1", "openai/gpt-oss-120b"),
        new("groq", AiChatService.GroqBaseUrl, "k1", "qwen/qwen3.8-27b"),
        new("groq", AiChatService.GroqBaseUrl, "k1", "openai/gpt-oss-20b"),
        new("gemini", AiChatService.GeminiBaseUrl, "k2", "gemini-2.5-flash"),
    };

    private static AiChatService Svc(Fake f, List<AiEndpoint>? chain = null) =>
        new(new HttpClient(f), chain ?? Chain(), new AiOrderTools(() => Array.Empty<StoredOrder>(), () => Now), () => Now);

    [Fact]
    public async Task First_model_rate_limited_falls_back_to_next_and_remembers_the_rest()
    {
        var f = new Fake((_, m) => m == "openai/gpt-oss-120b" ? ((HttpStatusCode)429, Limit) : (HttpStatusCode.OK, Ok("trả lời bởi " + m)));
        var svc = Svc(f);
        var a1 = await svc.AskAsync("câu 1");
        Assert.Equal("trả lời bởi qwen/qwen3.8-27b", a1.Text);
        Assert.Contains("groq/qwen/qwen3.8-27b", a1.Trace[0]);
        var a2 = await svc.AskAsync("câu 2");
        Assert.Equal("trả lời bởi qwen/qwen3.8-27b", a2.Text);
        // câu 2 không thử lại mô hình đang nghỉ
        Assert.Equal(new[] { "api.groq.com|openai/gpt-oss-120b", "api.groq.com|qwen/qwen3.8-27b", "api.groq.com|qwen/qwen3.8-27b" }, f.Calls.ToArray());
    }

    [Fact]
    public async Task All_groq_models_limited_then_google_answers()
    {
        var f = new Fake((host, m) => host.Contains("groq") ? ((HttpStatusCode)429, Limit) : (HttpStatusCode.OK, Ok("từ " + m)));
        var a = await Svc(f).AskAsync("hi");
        Assert.Equal("từ gemini-2.5-flash", a.Text);
        Assert.Equal(4, f.Calls.Count);
        Assert.StartsWith("Mô hình: ", a.Trace[0]);
    }

    [Fact]
    public async Task Missing_model_404_is_skipped_to_the_next_one()
    {
        var f = new Fake((_, m) => m == "openai/gpt-oss-120b" ? (HttpStatusCode.NotFound, "{\"error\":{\"message\":\"model not found\"}}") : (HttpStatusCode.OK, Ok("ok " + m)));
        var a = await Svc(f).AskAsync("hi");
        Assert.Equal("ok qwen/qwen3.8-27b", a.Text);
    }

    [Fact]
    public async Task All_models_failing_gives_one_message_listing_each_and_later_asks_report_resting()
    {
        var f = new Fake((_, _) => ((HttpStatusCode)429, Limit));
        var svc = Svc(f);
        var ex = await Assert.ThrowsAnyAsync<AiException>(() => svc.AskAsync("hi"));
        Assert.Contains("openai/gpt-oss-120b", ex.Message);
        Assert.Contains("gemini/gemini-2.5-flash", ex.Message);
        Assert.Contains("giới hạn", ex.Message);
        Assert.DoesNotContain("k1", ex.Message);
        var callsBefore = f.Calls.Count;
        var ex2 = await Assert.ThrowsAnyAsync<AiException>(() => svc.AskAsync("hi"));
        Assert.Contains("nghỉ", ex2.Message);
        Assert.Equal(callsBefore, f.Calls.Count);   // không gọi lại vô ích
    }

    private sealed class Capturing(Func<JsonObject, string> reply) : HttpMessageHandler
    {
        public string? Host, Auth;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Host = request.RequestUri!.Host;
            Auth = request.Headers.Authorization?.ToString();
            var body = (JsonObject)JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply(body), Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task Gemini_goes_to_google_with_its_own_key_and_tool_messages_carry_names()
    {
        var calls = 0;
        string? toolName = null;
        var h = new Capturing(req =>
        {
            calls++;
            var msgs = (JsonArray)req["messages"]!;
            toolName = msgs.LastOrDefault(m => m!["role"]!.GetValue<string>() == "tool")?["name"]?.GetValue<string>();
            return calls == 1
                ? "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"\",\"type\":\"function\",\"function\":{\"name\":\"count_orders\",\"arguments\":\"{}\"}}]}}]}"
                : Ok("xong");
        });
        var chain = new List<AiEndpoint> { new("gemini", AiChatService.GeminiBaseUrl, "gem-key", "gemini-2.5-flash") };
        var svc = new AiChatService(new HttpClient(h), chain, new AiOrderTools(() => Array.Empty<StoredOrder>(), () => Now), () => Now);
        var a = await svc.AskAsync("đếm");
        Assert.Equal("xong", a.Text);
        Assert.Equal("generativelanguage.googleapis.com", h.Host);
        Assert.Equal("Bearer gem-key", h.Auth);
        Assert.Equal("count_orders", toolName);
    }

    [Fact]
    public void Store_builds_the_chain_groq_first_then_google_and_keeps_keys_separate()
    {
        var dir = Path.Combine(Path.GetTempPath(), "qlhh-aifb-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s = new AiSettingsStore(dir);
            Assert.Empty(s.BuildEndpoints());
            s.SaveKey(AiSettingsStore.Gemini, "gem-secret");
            Assert.Equal(new[] { "gemini/gemini-2.5-flash", "gemini/gemini-2.5-flash-lite" }, s.BuildEndpoints().Select(e => e.Label).ToArray());
            s.SaveKey(AiSettingsStore.Groq, "groq-secret");
            var labels = s.BuildEndpoints().Select(e => e.Label).ToArray();
            Assert.Equal("groq/openai/gpt-oss-120b", labels[0]);
            Assert.Equal("groq/qwen/qwen3.8-27b", labels[1]);
            Assert.Equal("groq/openai/gpt-oss-20b", labels[2]);
            Assert.Equal("gemini/gemini-2.5-flash", labels[3]);
            Assert.Equal("groq-secret", s.LoadKey(AiSettingsStore.Groq));
            Assert.Equal("gem-secret", s.LoadKey(AiSettingsStore.Gemini));
            s.ClearKey(AiSettingsStore.Groq);
            Assert.Null(s.LoadKey(AiSettingsStore.Groq));
            Assert.Equal("gem-secret", s.LoadKey(AiSettingsStore.Gemini));
            Assert.Equal(new[] { "a", "b" }, AiChatService.SplitModels("a, b\nb", "x").ToArray());
            Assert.Equal(new[] { "x" }, AiChatService.SplitModels("  ", "x").ToArray());
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
