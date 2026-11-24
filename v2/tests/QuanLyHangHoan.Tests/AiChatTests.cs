using System.IO;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Ai;

namespace QuanLyHangHoan.Tests;

/// <summary>Trợ lý AI: công cụ truy vấn, quyền riêng tư, vòng gọi công cụ với máy chủ Groq GIẢ (không gọi mạng thật, không dùng dữ liệu đơn thật).</summary>
public sealed class AiChatTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 14, 0, 0);

    private static StoredOrder S(Order o, bool archived = false, bool deleted = false) => new(o, archived, "", deleted, "", "");

    private static Order O(string id, string orderId, string track, string status, string recv, string product = "Áo thun", int qty = 1, string note = "") => new()
    {
        Id = id, OrderId = orderId, Track = track, Status = status, RecvDate = recv, Platform = "Shopee", Carrier = "SPX", Note = note,
        BuyerNote = "Khách Nguyễn Văn A 0912345678 123 Đường X",
        ExtraJson = "{\"rawDetails\":{\"Recipient\":\"Nguyễn Văn A\",\"Phone\":\"0912345678\",\"Address\":\"123 Đường X\"}}",
        Items = new() { new OrderItem { Product = product, Qty = qty } },
    };

    private static AiOrderTools Tools(params StoredOrder[] rows) => new(() => rows, () => Now);

    private static readonly StoredOrder[] Sample =
    {
        S(O("1", "SHP1", "VN001", ReceiveStatus.Received, "25/11/2026 09:00:00", "Áo thun", 2)),
        S(O("2", "SHP2", "VN002", ReceiveStatus.Received, "25/11/2026 11:30", "Quần jean")),
        S(O("3", "SHP3", "VN003", ReceiveStatus.Received, "24/11/2026 10:00", "Áo thun")),
        S(O("4", "SHP4", "VN004", ReceiveStatus.NotReturned, "")),
        S(O("5", "SHP5", "VN005", ReceiveStatus.Received, "25/11/2026 08:00"), archived: true),
    };

    [Fact]
    public void Count_today_received_counts_only_active_rows_received_today()
    {
        var r = Tools(Sample).Execute("count_orders", """{"status":"Hàng đã trả","date_field":"recv","from":"today","to":"today"}""");
        Assert.Equal(2, JsonNode.Parse(r.Json)!["total"]!.GetValue<int>());
    }

    [Fact]
    public void Count_without_filters_gives_overview_by_status()
    {
        var j = JsonNode.Parse(Tools(Sample).Execute("count_orders", "{}").Json)!;
        Assert.Equal(4, j["total"]!.GetValue<int>());
        Assert.Equal(3, j["byStatus"]![ReceiveStatus.Received]!.GetValue<int>());
        Assert.Equal(1, j["byStatus"]![ReceiveStatus.NotReturned]!.GetValue<int>());
    }

    [Fact]
    public void Scope_all_includes_archived()
    {
        var j = JsonNode.Parse(Tools(Sample).Execute("count_orders", """{"scope":"all"}""").Json)!;
        Assert.Equal(5, j["total"]!.GetValue<int>());
    }

    [Fact]
    public void List_returns_clickable_refs_and_never_leaks_recipient_data()
    {
        var tools = Tools(Sample);
        var r = tools.Execute("list_orders", """{"status":"Hàng đã trả","from":"today","to":"today"}""");
        Assert.Equal(2, r.Refs.Count);
        Assert.Equal("1", r.Refs[0].Id);
        Assert.Same(Sample[0].Order, tools.TryGetOrder("1"));
        foreach (var banned in new[] { "0912345678", "Nguyễn Văn A", "123 Đường X", "Recipient", "Address" })
            Assert.DoesNotContain(banned, r.Json);
    }

    [Fact]
    public void Phone_numbers_typed_in_notes_are_masked()
    {
        var tools = Tools(S(O("9", "SHP9", "VN009", ReceiveStatus.Received, "25/11/2026 09:00", note: "Gọi khách 0987 654 321 hoặc +84912345678 nhé; mã VN123456789012")));
        var j = tools.Execute("list_orders", "{}").Json;
        Assert.DoesNotContain("654 321", j);
        Assert.DoesNotContain("912345678", j);
        Assert.Contains("ẩn SĐT", j);
        Assert.Contains("VN123456789012", j);   // mã vận đơn có chữ không bị che
    }

    [Fact]
    public void Find_matches_partial_codes_across_archive_and_reports_short_codes()
    {
        var tools = Tools(Sample);
        Assert.Single(tools.Execute("find_order", """{"code":"vn005"}""").Refs);
        Assert.Contains("error", tools.Execute("find_order", """{"code":"v"}""").Json);
    }

    [Fact]
    public void Product_stats_sums_quantity_and_orders()
    {
        var j = JsonNode.Parse(Tools(Sample).Execute("product_stats", """{"status":"Hàng đã trả"}""").Json)!;
        var first = j["products"]![0]!;
        Assert.Equal("Áo thun", first["product"]!.GetValue<string>());
        Assert.Equal(3, first["qty"]!.GetValue<int>());
        Assert.Equal(2, first["orders"]!.GetValue<int>());
    }

    [Fact]
    public void Unknown_tool_and_bad_json_do_not_throw()
    {
        var t = Tools(Sample);
        Assert.Contains("error", t.Execute("delete_everything", "{}").Json);
        Assert.Contains("error", t.Execute("count_orders", "{not json").Json);
    }

    private sealed class FakeGroq(Func<JsonObject, (HttpStatusCode, string)> reply) : HttpMessageHandler
    {
        public readonly List<JsonObject> Requests = new();
        public string? AuthHeader;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            AuthHeader = request.Headers.Authorization?.ToString();
            var body = (JsonObject)JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            Requests.Add(body);
            var (code, text) = reply(body);
            return new HttpResponseMessage(code) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    private static string ToolCall(string name, string args) => new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject
        {
            ["message"] = new JsonObject
            {
                ["role"] = "assistant", ["content"] = null,
                ["tool_calls"] = new JsonArray(new JsonObject
                {
                    ["id"] = "call_1", ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = name, ["arguments"] = args },
                }),
            },
        }),
    }.ToJsonString();

    private static string Final(string text) => new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = text } }),
    }.ToJsonString();

    private static AiChatService Service(FakeGroq fake, AiOrderTools tools) =>
        new(new HttpClient(fake), "gsk_test_key", "", tools, () => Now);

    [Fact]
    public async Task Ask_runs_the_tool_the_model_requests_and_returns_final_text_with_refs()
    {
        var turn = 0;
        var fake = new FakeGroq(req =>
        {
            turn++;
            if (turn == 1) return (HttpStatusCode.OK, ToolCall("list_orders", """{"status":"Hàng đã trả","from":"today","to":"today"}"""));
            var toolMsg = req["messages"]!.AsArray().Last()!;
            Assert.Equal("tool", toolMsg["role"]!.GetValue<string>());
            Assert.DoesNotContain("0912345678", toolMsg["content"]!.GetValue<string>());
            return (HttpStatusCode.OK, Final("Hôm nay có 2 kiện về."));
        });
        var ans = await Service(fake, Tools(Sample)).AskAsync("hôm nay có bao nhiêu đơn về?");
        Assert.Equal("Hôm nay có 2 kiện về.", ans.Text);
        Assert.Equal(2, ans.Refs.Count);
        Assert.Single(ans.Trace);
        Assert.Equal("Bearer gsk_test_key", fake.AuthHeader);
        Assert.Equal(AiChatService.DefaultModel, fake.Requests[0]["model"]!.GetValue<string>());
        Assert.Contains("2026-11-25", fake.Requests[0]["messages"]![0]!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task History_is_kept_between_questions_but_tool_messages_are_not()
    {
        var fake = new FakeGroq(_ => (HttpStatusCode.OK, Final("ok")));
        var svc = Service(fake, Tools(Sample));
        await svc.AskAsync("câu 1");
        await svc.AskAsync("câu 2");
        var second = fake.Requests[1]["messages"]!.AsArray();
        Assert.Equal(new[] { "system", "user", "assistant", "user" }, second.Select(m => m!["role"]!.GetValue<string>()).ToArray());
        svc.Reset();
        await svc.AskAsync("câu 3");
        Assert.Equal(2, fake.Requests[2]["messages"]!.AsArray().Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Khóa API")]
    [InlineData((HttpStatusCode)429, "giới hạn")]
    [InlineData(HttpStatusCode.NotFound, "mô hình")]
    public async Task Http_errors_become_clear_vietnamese_messages(HttpStatusCode code, string expected)
    {
        var fake = new FakeGroq(_ => (code, """{"error":{"message":"x"}}"""));
        var ex = await Assert.ThrowsAsync<AiException>(() => Service(fake, Tools(Sample)).AskAsync("hi"));
        Assert.Contains(expected, ex.Message);
        Assert.DoesNotContain("gsk_test_key", ex.Message);
    }

    [Fact]
    public async Task Endless_tool_calls_are_stopped()
    {
        var fake = new FakeGroq(_ => (HttpStatusCode.OK, ToolCall("count_orders", "{}")));
        await Assert.ThrowsAsync<AiException>(() => Service(fake, Tools(Sample)).AskAsync("loop"));
        Assert.True(fake.Requests.Count <= 6);
    }

    [Fact]
    public void Api_key_is_encrypted_on_disk_and_round_trips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "qlhh-ai-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AiSettingsStore(dir);
            Assert.Null(store.LoadKey());
            store.SaveKey("gsk_secret_value_123");
            Assert.Equal("gsk_secret_value_123", store.LoadKey());
            var raw = File.ReadAllBytes(Path.Combine(dir, "ai-key.dat"));
            Assert.DoesNotContain("gsk_secret_value_123", Encoding.UTF8.GetString(raw));
            store.SaveModel("openai/gpt-oss-20b");
            Assert.Equal("openai/gpt-oss-20b", store.LoadModel());
            store.ClearKey();
            Assert.Null(store.LoadKey());
        }
        finally { try { Directory.Delete(dir, true); } catch { /* bỏ qua */ } }
    }
}
