using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using QuanLyHangHoan.Core.Models;
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
            Assert.Equal(new[] { "gemini/gemini-4-hydrogen", "gemini/gemini-3.8-flash", "gemini/gemini-3.5-flash-lite" }, s.BuildEndpoints().Select(e => e.Label).ToArray());
            s.SaveKey(AiSettingsStore.Groq, "groq-secret");
            var labels = s.BuildEndpoints().Select(e => e.Label).ToArray();
            Assert.Equal("groq/openai/gpt-oss-120b", labels[0]);
            Assert.Equal("groq/qwen/qwen3.8-27b", labels[1]);
            Assert.Equal("groq/openai/gpt-oss-20b", labels[2]);
            Assert.Equal("gemini/gemini-4-hydrogen", labels[3]);
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

    // ---------------- mô hình treo (không phản hồi) ----------------

    private sealed class Hanging(string hangModel) : HttpMessageHandler
    {
        public readonly List<string> Calls = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = (JsonObject)JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            var model = body["model"]!.GetValue<string>();
            Calls.Add(model);
            if (model == hangModel) await Task.Delay(Timeout.Infinite, ct);   // treo tới khi bị hủy
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Ok("bởi " + model), Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task A_hanging_model_times_out_then_falls_back_and_is_skipped_afterwards()
    {
        var h = new Hanging("openai/gpt-oss-120b");
        var svc = new AiChatService(new HttpClient(h), Chain(), new AiOrderTools(() => Array.Empty<StoredOrder>(), () => Now), () => Now)
        { RequestTimeout = TimeSpan.FromMilliseconds(300) };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var a1 = await svc.AskAsync("câu 1");
        Assert.Equal("bởi qwen/qwen3.8-27b", a1.Text);
        Assert.True(sw.ElapsedMilliseconds < 5000, "không được chờ quá lâu khi một mô hình treo");
        var a2 = await svc.AskAsync("câu 2");
        Assert.Equal("bởi qwen/qwen3.8-27b", a2.Text);
        Assert.Equal(new[] { "openai/gpt-oss-120b", "qwen/qwen3.8-27b", "qwen/qwen3.8-27b" }, h.Calls.ToArray());   // câu 2 bỏ qua mô hình đang treo
    }

    [Fact]
    public async Task All_models_hanging_gives_a_clear_timeout_message()
    {
        var h = new Hanging("only");
        var one = new List<AiEndpoint> { new("groq", AiChatService.GroqBaseUrl, "k", "only") };
        var svc = new AiChatService(new HttpClient(h), one, new AiOrderTools(() => Array.Empty<StoredOrder>(), () => Now), () => Now)
        { RequestTimeout = TimeSpan.FromMilliseconds(200) };
        var ex = await Assert.ThrowsAnyAsync<AiException>(() => svc.AskAsync("hi"));
        Assert.Contains("không phản hồi", ex.Message);
    }

    [Fact]
    public async Task Test_connection_runs_all_models_in_parallel_so_one_hanging_model_does_not_block_the_rest()
    {
        var h = new Hanging("qwen/qwen3.8-27b");
        var svc = new AiChatService(new HttpClient(h), Chain(), new AiOrderTools(() => Array.Empty<StoredOrder>(), () => Now), () => Now)
        { RequestTimeout = TimeSpan.FromMilliseconds(300) };
        var seen = new List<string>();
        var progress = new SyncProgress<(string Label, string? Error)>(r => { lock (seen) seen.Add(r.Label); });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var res = await svc.TestAllAsync(default, progress);
        Assert.True(sw.ElapsedMilliseconds < 3000);
        Assert.Equal(4, res.Count);
        Assert.Equal(4, seen.Count);                                          // mỗi mô hình báo kết quả một lần
        Assert.Null(res[0].Error);
        Assert.Contains("không phản hồi", res[1].Error);                      // mô hình treo báo quá thời gian
        Assert.Null(res[2].Error);
        Assert.Null(res[3].Error);
        Assert.Equal(new[] { "groq/openai/gpt-oss-120b", "groq/qwen/qwen3.8-27b", "groq/openai/gpt-oss-20b", "gemini/gemini-2.5-flash" }, res.Select(r => r.Label).ToArray());
    }

    private sealed class SyncProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    // ---------------- gói miễn phí giới hạn token: yêu cầu phải nhỏ ----------------

    private static string[] ToolNames(string json) =>
        JsonNode.Parse(json)!.AsArray().Select(t => t!["function"]!["name"]!.GetValue<string>()).ToArray();

    [Fact]
    public void Only_tools_relevant_to_the_question_are_sent_and_the_payload_stays_small()
    {
        var today = AiOrderTools.DefinitionsFor("hôm nay có bao nhiêu đơn về?");
        Assert.Contains("count_orders", ToolNames(today));
        Assert.Contains("overview", ToolNames(today));
        Assert.True(today.Length < 7600, "định nghĩa gửi đi quá dài: " + today.Length);   // ~2.700 token
        Assert.True(AiOrderTools.DefinitionsFor("giá trị chưa về và sản phẩm nào nhiều nhất").Length < 8200);

        Assert.Contains("compare_periods", ToolNames(AiOrderTools.DefinitionsFor("so với hôm qua thì sao")));
        Assert.Contains("app_guide", ToolNames(AiOrderTools.DefinitionsFor("làm sao để nạp đơn")));
        Assert.Contains("turnaround_stats", ToolNames(AiOrderTools.DefinitionsFor("kho xử lý trung bình bao lâu")));
        Assert.Contains("find_order", ToolNames(AiOrderTools.DefinitionsFor("tìm đơn 2611ABC123XYZ")));      // mã dạng chữ+số
        Assert.Contains("duplicates", ToolNames(AiOrderTools.DefinitionsFor("có mã nào bị trùng không")));
        Assert.DoesNotContain("app_guide", ToolNames(AiOrderTools.DefinitionsFor("bao nhiêu đơn shopee hôm nay")));
        // luôn có 3 công cụ cốt lõi, kể cả câu hỏi lạ
        var odd = ToolNames(AiOrderTools.DefinitionsFor("xyz"));
        Assert.Contains("count_orders", odd);
        Assert.Contains("aggregate", odd);
        Assert.Contains("list_orders", odd);
        // bản đầy đủ vẫn có đủ công cụ (để kiểm thử/điều chỉnh)
        Assert.True(ToolNames(AiOrderTools.DefinitionsJson).Length >= 19);
    }

    private sealed class Recorder(Func<JsonObject, (HttpStatusCode, string)> reply) : HttpMessageHandler
    {
        public readonly List<JsonObject> Bodies = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = (JsonObject)JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            Bodies.Add(body);
            var (code, text) = reply(body);
            return new HttpResponseMessage(code) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>Cách app thật tuần tự hóa yêu cầu: không thoát chữ Việt thành \uXXXX.</summary>
    private static readonly System.Text.Json.JsonSerializerOptions WireJson =
        new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [Fact]
    public async Task A_typical_request_is_well_under_the_free_tier_limit()
    {
        var r = new Recorder(_ => (HttpStatusCode.OK, Ok("xong")));
        var svc = new AiChatService(new HttpClient(r), new List<AiEndpoint> { new("groq", AiChatService.GroqBaseUrl, "k", "m") },
            new AiOrderTools(() => Array.Empty<StoredOrder>(), () => Now), () => Now);
        await svc.AskAsync("giá trị chưa về đang là bao nhiêu tiền và sản phẩm nào chưa về nhiều nhất");
        var chars = r.Bodies[0].ToJsonString(WireJson).Length;
        Assert.True(chars / 2.8 < 4500, $"yêu cầu ước {chars / 2.8:0} token, vượt ngân sách 4500");
    }

    [Fact]
    public async Task Long_history_is_trimmed_so_requests_do_not_grow_without_bound()
    {
        var longAnswer = new string('x', 3000);
        var r = new Recorder(_ => (HttpStatusCode.OK, Ok(longAnswer)));
        var svc = new AiChatService(new HttpClient(r), new List<AiEndpoint> { new("groq", AiChatService.GroqBaseUrl, "k", "m") },
            new AiOrderTools(() => Array.Empty<StoredOrder>(), () => Now), () => Now);
        for (var i = 0; i < 10; i++) await svc.AskAsync("câu hỏi số " + i);
        var last = r.Bodies[^1];
        Assert.True(last["messages"]!.AsArray().Count <= 8);                       // system + tối đa 6 tin lịch sử + câu hỏi
        // đo đúng cách gửi thật (không thoát ký tự Việt thành \uXXXX)
        Assert.True(last.ToJsonString(WireJson).Length / 2.8 < 4500);
    }

    [Fact]
    public async Task Http_413_retries_the_same_model_with_core_tools_only_before_giving_up()
    {
        var r = new Recorder(b => b["tools"]!.AsArray().Count > 3
            ? ((HttpStatusCode)413, "{\"error\":{\"message\":\"Request too large for model\"}}")
            : (HttpStatusCode.OK, Ok("trả lời gọn")));
        var chain = new List<AiEndpoint> { new("groq", AiChatService.GroqBaseUrl, "k", "openai/gpt-oss-120b") };
        var svc = new AiChatService(new HttpClient(r), chain, new AiOrderTools(() => Array.Empty<StoredOrder>(), () => Now), () => Now);
        var a = await svc.AskAsync("so với hôm qua tình hình hôm nay thế nào");      // chọn nhiều hơn 3 công cụ
        Assert.Equal("trả lời gọn", a.Text);
        Assert.Equal(2, r.Bodies.Count);
        Assert.True(r.Bodies[0]["tools"]!.AsArray().Count > 3);
        Assert.Equal(3, r.Bodies[1]["tools"]!.AsArray().Count);
        Assert.Equal(r.Bodies[0]["model"]!.GetValue<string>(), r.Bodies[1]["model"]!.GetValue<string>());
    }

    [Fact]
    public void List_results_are_compact_no_empty_fields_and_at_most_three_items()
    {
        var o = new Order { Id = "1", OrderId = "O1", Track = "T1", Status = "Chưa trả", Price = 1000, Platform = "Shopee", Note = "ghi chú 0987654321" };
        o.Items = Enumerable.Range(1, 5).Select(i => new OrderItem { Product = "SP" + i, Qty = 1 }).ToList();
        var t = new AiOrderTools(() => new[] { new StoredOrder(o, false, "", false, "", "") }, () => Now);
        var json = t.Execute("list_orders", "{}").Json;
        var row = JsonNode.Parse(json)!["orders"]![0]!.AsObject();
        Assert.False(row.ContainsKey("carrier"));            // trường rỗng bị bỏ
        Assert.False(row.ContainsKey("watching"));           // false bị bỏ
        Assert.Equal(3, row["items"]!.AsArray().Count);
        Assert.Equal(2, row["moreItems"]!.GetValue<int>());
        Assert.DoesNotContain("0987654321", json);
    }
}
