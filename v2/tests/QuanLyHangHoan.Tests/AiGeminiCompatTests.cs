using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Ai;

namespace QuanLyHangHoan.Tests;

/// <summary>Gemini kén lược đồ công cụ; Groq báo "thử lại sau vài trăm ms"; lỗi 400 phải hiện được lý do. Máy chủ GIẢ.</summary>
public sealed class AiGeminiCompatTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 14, 0, 0);

    private sealed class Capture(Func<string, int, (HttpStatusCode, string)> reply) : HttpMessageHandler
    {
        public readonly List<(string Host, string Model, JsonObject Body)> Requests = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = (JsonObject)JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            Requests.Add((request.RequestUri!.Host, body["model"]!.GetValue<string>(), body));
            var (code, text) = reply(request.RequestUri!.Host, Requests.Count);
            return new HttpResponseMessage(code) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    private static string Ok(string text) => new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = text } }),
    }.ToJsonString();

    private static AiChatService Svc(Capture f, params AiEndpoint[] chain) =>
        new(new HttpClient(f), chain.ToList(), new AiOrderTools(() => Array.Empty<StoredOrder>(), () => Now), () => Now);

    private static AiEndpoint Groq(string m) => new("groq", AiChatService.GroqBaseUrl, "k1", m);
    private static AiEndpoint Gem(string m) => new("gemini", AiChatService.GeminiBaseUrl, "k2", m);

    private static List<JsonObject> Functions(JsonObject body) =>
        (body["tools"] as JsonArray ?? new JsonArray()).Select(t => (JsonObject)t!["function"]!).ToList();

    // ------------------------------------------------------------ lược đồ công cụ
    [Fact]
    public async Task Gemini_gets_tools_without_empty_properties_or_unsupported_keys_while_groq_is_unchanged()
    {
        // hỏi về "tình trạng hệ thống" để công cụ không tham số (system_status) có trong danh sách
        var gem = new Capture((_, _) => (HttpStatusCode.OK, Ok("ok")));
        await Svc(gem, Gem("gemini-x")).AskAsync("tình trạng hệ thống đang kết nối điện thoại không");
        var gFns = Functions(gem.Requests[0].Body);
        Assert.NotEmpty(gFns);
        foreach (var fn in gFns)
        {
            if (fn["parameters"] is JsonObject prm)
            {
                Assert.NotNull(prm["properties"]);
                Assert.True(((JsonObject)prm["properties"]!).Count > 0, fn["name"] + " có properties rỗng");
                Assert.False(prm.ToJsonString().Contains("additionalProperties"), fn["name"] + " còn additionalProperties");
            }
        }

        var groq = new Capture((_, _) => (HttpStatusCode.OK, Ok("ok")));
        await Svc(groq, Groq("m")).AskAsync("tình trạng hệ thống đang kết nối điện thoại không");
        var noParamTools = Functions(groq.Requests[0].Body).Where(fn => fn["parameters"]?["properties"] is JsonObject { Count: 0 }).ToList();
        Assert.NotEmpty(noParamTools);   // Groq vẫn nhận bản gốc (có công cụ không tham số)
    }

    [Fact]
    public void Sanitize_removes_empty_parameters_and_nested_unsupported_keys()
    {
        var body = JsonNode.Parse("""
            {"tools":[
              {"type":"function","function":{"name":"a","parameters":{"type":"object","properties":{}}}},
              {"type":"function","function":{"name":"b","parameters":{"type":"object","additionalProperties":false,
                 "properties":{"x":{"type":"string","default":"1"},"y":{"type":"object","additionalProperties":true,"properties":{"z":{"type":"integer"}}}}}}}
            ]}
            """)!.AsObject();

        AiChatService.SanitizeToolsForGemini(body);

        var a = (JsonObject)body["tools"]![0]!["function"]!;
        Assert.False(a.ContainsKey("parameters"));
        var b = body["tools"]![1]!["function"]!["parameters"]!.ToJsonString();
        Assert.DoesNotContain("additionalProperties", b);
        Assert.DoesNotContain("default", b);
        Assert.Contains("\"z\"", b);
    }

    // ------------------------------------------------------------ chờ ngắn rồi thử lại
    [Fact]
    public async Task Short_per_minute_limit_waits_and_retries_the_same_model_instead_of_falling_back()
    {
        var limit = "{\"error\":{\"message\":\"Rate limit reached ... on tokens per minute (TPM). Please try again in 300ms.\"}}";
        var f = new Capture((_, n) => n == 1 ? ((HttpStatusCode)429, limit) : (HttpStatusCode.OK, Ok("xong")));
        var a = await Svc(f, Groq("m1"), Groq("m2")).AskAsync("đếm đơn");

        Assert.Equal("xong", a.Text);
        Assert.Equal(new[] { "m1", "m1" }, f.Requests.Select(r => r.Model).ToArray());   // thử lại m1, không nhảy sang m2
    }

    [Fact]
    public async Task Long_wait_limit_still_falls_back_to_the_next_model()
    {
        var limit = "{\"error\":{\"message\":\"Rate limit. Please try again in 45s.\"}}";
        var f = new Capture((_, n) => n == 1 ? ((HttpStatusCode)429, limit) : (HttpStatusCode.OK, Ok("từ m2")));
        var a = await Svc(f, Groq("m1"), Groq("m2")).AskAsync("đếm đơn");

        Assert.Equal("từ m2", a.Text);
        Assert.Equal(new[] { "m1", "m2" }, f.Requests.Select(r => r.Model).ToArray());
    }

    [Fact]
    public async Task Waits_only_once_per_model_when_the_limit_persists()
    {
        var limit = "{\"error\":{\"message\":\"Please try again in 200ms.\"}}";
        var f = new Capture((_, n) => n <= 2 ? ((HttpStatusCode)429, limit) : (HttpStatusCode.OK, Ok("từ m2")));
        var a = await Svc(f, Groq("m1"), Groq("m2")).AskAsync("đếm đơn");

        Assert.Equal("từ m2", a.Text);
        Assert.Equal(new[] { "m1", "m1", "m2" }, f.Requests.Select(r => r.Model).ToArray());
    }

    // ------------------------------------------------------------ lý do lỗi
    [Fact]
    public async Task Bad_request_without_a_readable_reason_shows_the_raw_body_and_hides_org_ids()
    {
        var f = new Capture((_, _) => (HttpStatusCode.BadRequest, "Unknown field at tools[3] org_01abcDEF123 oops"));
        var ex = await Assert.ThrowsAnyAsync<AiException>(() => Svc(f, Gem("g")).AskAsync("đếm đơn"));

        Assert.Contains("Unknown field at tools[3]", ex.Message);
        Assert.DoesNotContain("org_01abcDEF123", ex.Message);
        Assert.Contains("org_…", ex.Message);
    }

    [Fact]
    public async Task Gemini_array_error_body_still_shows_its_message()
    {
        var f = new Capture((_, _) => (HttpStatusCode.BadRequest, "[{\"error\":{\"code\":400,\"message\":\"Tool schema invalid\",\"status\":\"INVALID_ARGUMENT\"}}]"));
        var ex = await Assert.ThrowsAnyAsync<AiException>(() => Svc(f, Gem("g")).AskAsync("đếm đơn"));
        Assert.Contains("Tool schema invalid", ex.Message);
    }
}
