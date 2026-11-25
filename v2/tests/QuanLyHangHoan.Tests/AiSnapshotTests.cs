using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Ai;

namespace QuanLyHangHoan.Tests;

/// <summary>Số liệu tính sẵn gắn vào lời nhắc hệ thống + chống vòng gọi công cụ lặp (tiết kiệm token). Máy chủ GIẢ, dữ liệu GIẢ.</summary>
public sealed class AiSnapshotTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 14, 0, 0);

    private static StoredOrder S(Order o) => new(o, false, "", false, "", "");

    private static Order O(string id, string status, string recv, string product = "Áo thun", int qty = 1) => new()
    {
        Id = id, OrderId = "SHP" + id, Track = "VN00" + id, Status = status, RecvDate = recv, Platform = "Shopee", Carrier = "SPX",
        BuyerNote = "Khách Nguyễn Văn A 0912345678 123 Đường X",
        ExtraJson = "{\"rawDetails\":{\"Recipient\":\"Nguyễn Văn A\",\"Phone\":\"0912345678\"}}",
        Items = new() { new OrderItem { Product = product, Qty = qty } },
    };

    private static AiOrderTools Tools() => new(() => new[]
    {
        S(O("1", ReceiveStatus.Received, "25/11/2026 09:00:00", "Áo thun", 2)),
        S(O("2", ReceiveStatus.Received, "25/11/2026 11:30")),
        S(O("3", ReceiveStatus.Received, "24/11/2026 10:00")),
        S(O("4", ReceiveStatus.NotReturned, "", "Quần jean", 3)),
    }, () => Now);

    private sealed class Capture(Func<int, string> reply) : HttpMessageHandler
    {
        public readonly List<JsonObject> Bodies = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add((JsonObject)JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply(Bodies.Count), Encoding.UTF8, "application/json") };
        }
    }

    private static string Text(string t) => new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = t } }),
    }.ToJsonString();

    private static JsonObject Call(string id, string name, string args) => new()
    {
        ["id"] = id, ["type"] = "function",
        ["function"] = new JsonObject { ["name"] = name, ["arguments"] = args },
    };

    private static string Calls(params JsonObject[] calls) => new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject
        {
            ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = "", ["tool_calls"] = new JsonArray(calls.Select(c => (JsonNode)c).ToArray()) },
        }),
    }.ToJsonString();

    private static AiChatService Svc(Capture f) =>
        new(new HttpClient(f), new List<AiEndpoint> { new("groq", AiChatService.GroqBaseUrl, "k", "m") }, Tools(), () => Now);

    [Fact]
    public void Snapshot_has_current_counts_and_no_personal_data()
    {
        var snap = Tools().Snapshot();
        var j = JsonNode.Parse(snap)!;
        Assert.Equal(4, j["activeOrders"]!.GetValue<int>());
        Assert.Equal(1, j["waiting"]!["count"]!.GetValue<int>());
        Assert.Equal(2, j["receivedToday"]!["count"]!.GetValue<int>());
        Assert.Equal(1, j["receivedYesterday"]!["count"]!.GetValue<int>());
        Assert.Equal(3, j["received7days"]!["count"]!.GetValue<int>());
        Assert.Equal("Quần jean", j["topWaitingProducts"]![0]!["name"]!.GetValue<string>());
        Assert.DoesNotContain("0912345678", snap);
        Assert.DoesNotContain("Nguyễn Văn A", snap);
        Assert.True(snap.Length < 1500, "ảnh chụp phải nhỏ: " + snap.Length);
    }

    [Fact]
    public void Snapshot_failure_returns_empty_instead_of_breaking_the_question()
    {
        var t = new AiOrderTools(() => throw new InvalidOperationException("hỏng"), () => Now);
        Assert.Equal("", t.Snapshot());
    }

    [Fact]
    public async Task System_prompt_carries_the_snapshot_so_simple_questions_need_no_tool()
    {
        var f = new Capture(_ => Text("Đang chờ 1 kiện."));
        var a = await Svc(f).AskAsync("đang chờ bao nhiêu đơn?");

        Assert.Equal("Đang chờ 1 kiện.", a.Text);
        Assert.Single(f.Bodies);   // 1 yêu cầu, không vòng công cụ
        var sys = f.Bodies[0]["messages"]![0]!["content"]!.GetValue<string>();
        Assert.Contains("SỐ LIỆU HIỆN TẠI", sys);
        Assert.Contains("\"receivedToday\"", sys);
        Assert.DoesNotContain("0912345678", sys);
    }

    [Theory]
    [InlineData("man_hinh", "Kho lưu trữ")]
    [InlineData("y_nghia_du_lieu", "Đang chờ")]
    [InlineData("cau_hoi_mau", "list_orders")]
    public void App_guide_has_screen_map_data_dictionary_and_sample_questions(string topic, string mustContain)
    {
        var r = Tools().Execute("app_guide", "{\"topic\":\"" + topic + "\"}").Json;
        Assert.Contains(mustContain, r);
    }

    [Fact]
    public async Task System_prompt_lists_app_screens_and_examples_and_software_questions_get_the_guide_tool()
    {
        var f = new Capture(_ => Text("ok"));
        await Svc(f).AskAsync("nút sao lưu ở màn hình nào?");
        var sys = f.Bodies[0]["messages"]![0]!["content"]!.GetValue<string>();
        Assert.Contains("CÓ TRONG PHẦN MỀM", sys);
        Assert.Contains("Kho lưu trữ", sys);
        Assert.Contains("VÍ DỤ", sys);
        var names = ((JsonArray)f.Bodies[0]["tools"]!).Select(t => t!["function"]!["name"]!.GetValue<string>()).ToList();
        Assert.Contains("app_guide", names);
    }

    [Fact]
    public async Task Last_rounds_forbid_more_tool_calls_and_loop_ends_with_a_clear_error()
    {
        var f = new Capture(n => Calls(Call("c" + n, "count_orders", "{\"from\":\"" + n + "\"}")));
        await Assert.ThrowsAsync<AiException>(() => Svc(f).AskAsync("đếm đơn"));

        Assert.Equal("auto", f.Bodies[0]["tool_choice"]!.GetValue<string>());
        Assert.Equal("none", f.Bodies[^1]["tool_choice"]!.GetValue<string>());
        Assert.Equal("none", f.Bodies[^2]["tool_choice"]!.GetValue<string>());
    }

    [Fact]
    public async Task Identical_repeated_tool_call_is_not_executed_or_sent_again()
    {
        var f = new Capture(n => n == 1
            ? Calls(Call("a", "count_orders", "{}"), Call("b", "count_orders", "{}"))
            : Text("xong"));
        var a = await Svc(f).AskAsync("đếm đơn");

        Assert.Equal("xong", a.Text);
        Assert.Single(a.Trace.Where(t => t.StartsWith("count_orders")));
        var tools = ((JsonArray)f.Bodies[1]["messages"]!).Where(m => m!["role"]!.GetValue<string>() == "tool").ToList();
        Assert.Equal(2, tools.Count);
        Assert.Contains("total", tools[0]!["content"]!.GetValue<string>());
        Assert.Contains("Đã gọi công cụ này", tools[1]!["content"]!.GetValue<string>());
    }
}
