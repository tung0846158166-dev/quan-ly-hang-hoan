using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuanLyHangHoan.Data.Ai;

public sealed class AiException(string message) : Exception(message);

public sealed record AiAnswer(string Text, IReadOnlyList<AiOrderRef> Refs, IReadOnlyList<string> Trace);

public interface IAiChat
{
    Task<AiAnswer> AskAsync(string question, CancellationToken ct = default);
    void Reset();
    QuanLyHangHoan.Core.Models.Order? TryGetOrder(string id);
}

/// <summary>
/// Trò chuyện với Groq (API tương thích OpenAI) và cho AI gọi công cụ chỉ-đọc của <see cref="AiOrderTools"/>.
/// Chỉ gửi: câu hỏi, lịch sử chữ của phiên, và KẾT QUẢ công cụ (trường trong danh sách trắng, đã che số điện thoại).
/// </summary>
public sealed class AiChatService : IAiChat
{
    public const string DefaultBaseUrl = "https://api.groq.com/openai/v1";
    public const string DefaultModel = "llama-3.3-70b-versatile";
    private const int MaxRounds = 6;
    private const int MaxToolChars = 14000;
    private const int MaxHistoryMessages = 12;

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _baseUrl;
    private readonly AiOrderTools _tools;
    private readonly Func<DateTime> _clock;
    private readonly List<JsonObject> _history = new();

    public AiChatService(HttpClient http, string apiKey, string model, AiOrderTools tools, Func<DateTime>? clock = null, string? baseUrl = null)
    {
        _http = http;
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        _tools = tools;
        _clock = clock ?? (() => DateTime.Now);
        _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
    }

    public void Reset() => _history.Clear();

    public QuanLyHangHoan.Core.Models.Order? TryGetOrder(string id) => _tools.TryGetOrder(id);

    private string SystemPrompt()
    {
        var now = _clock();
        return "Bạn là trợ lý của phần mềm quản lý hàng hoàn (Shopee, TikTok Shop, Lazada) của một shop. Trả lời bằng tiếng Việt, ngắn gọn.\n" +
               $"Bây giờ là {now:dd/MM/yyyy HH:mm} ({new[] { "Chủ nhật", "Thứ hai", "Thứ ba", "Thứ tư", "Thứ năm", "Thứ sáu", "Thứ bảy" }[(int)now.DayOfWeek]}), hôm nay là {now:yyyy-MM-dd}.\n" +
               "QUY TẮC:\n" +
               "- Mọi con số và danh sách đơn PHẢI lấy từ công cụ (count_orders, list_orders, find_order, product_stats). Tuyệt đối không bịa, không đoán, không tự tính nhẩm khi công cụ đã cho số.\n" +
               "- Một mã đơn có thể có nhiều kiện hoàn (A/B/C), mỗi kiện một dòng riêng với mã vận đơn riêng. Đếm theo KIỆN (dòng), nói rõ 'kiện' hoặc 'dòng'.\n" +
               "- 'Đơn về hôm nay' nghĩa là trạng thái 'Hàng đã trả' (đã nhận tại kho) có ngày nhận tại kho là hôm nay.\n" +
               "- Khi liệt kê đơn, ghi mã đơn và mã vận đơn; người dùng có thể bấm vào đơn bên dưới để xem chi tiết. Nếu kết quả bị cắt (truncated) hãy nói rõ còn bao nhiêu.\n" +
               "- Câu hỏi mơ hồ (ví dụ không rõ ngày nào): chọn cách hiểu hợp lý nhất, nói rõ bạn đã hiểu thế nào.\n" +
               "- Bạn chỉ ĐỌC dữ liệu. Không thể sửa, xóa, nhận hàng hay gửi gì ra ngoài. Yêu cầu thay đổi dữ liệu thì hướng dẫn người dùng tự làm trong phần mềm.\n" +
               "- Dữ liệu không có tên, số điện thoại, địa chỉ người nhận (đã được ẩn). Nếu được hỏi, nói rằng thông tin đó đã bị ẩn.\n" +
               "- Không có công cụ cho điều được hỏi thì nói thẳng là chưa trả lời được.";
    }

    public async Task<AiAnswer> AskAsync(string question, CancellationToken ct = default)
    {
        var msgs = new List<JsonObject> { new() { ["role"] = "system", ["content"] = SystemPrompt() } };
        msgs.AddRange(_history.Select(m => (JsonObject)m.DeepClone()));
        msgs.Add(new JsonObject { ["role"] = "user", ["content"] = question });

        var refs = new List<AiOrderRef>();
        var trace = new List<string>();
        var tools = JsonNode.Parse(AiOrderTools.DefinitionsJson)!;

        try
        {
        for (var round = 0; round < MaxRounds; round++)
        {
            var body = new JsonObject
            {
                ["model"] = _model,
                ["messages"] = new JsonArray(msgs.Select(m => (JsonNode)m.DeepClone()).ToArray()),
                ["tools"] = tools.DeepClone(),
                ["tool_choice"] = "auto",
                ["temperature"] = 0.1,
            };
            var message = await PostAsync(body, ct).ConfigureAwait(false);
            var calls = message["tool_calls"] as JsonArray;
            if (calls is null || calls.Count == 0)
            {
                var text = (message["content"]?.GetValue<string>() ?? "").Trim();
                if (text.Length == 0) text = "(Không có câu trả lời.)";
                Remember(question, text);
                var distinct = refs.GroupBy(r => r.Id).Select(g => g.First()).Take(50).ToList();
                return new AiAnswer(text, distinct, trace);
            }

            // Lưu lượt trả lời có gọi công cụ rồi chạy từng công cụ.
            var assistant = new JsonObject { ["role"] = "assistant", ["content"] = message["content"]?.GetValue<string>() ?? "", ["tool_calls"] = calls.DeepClone() };
            msgs.Add(assistant);
            foreach (var call in calls)
            {
                var id = call?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
                var fn = call?["function"];
                var name = fn?["name"]?.GetValue<string>() ?? "";
                var args = fn?["arguments"]?.GetValue<string>() ?? "{}";
                var result = _tools.Execute(name, args);
                refs.AddRange(result.Refs);
                trace.Add($"{name}({args})");
                var json = result.Json.Length > MaxToolChars ? result.Json[..MaxToolChars] + "…(đã cắt bớt)" : result.Json;
                msgs.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = id, ["content"] = json });
            }
        }
        throw new AiException("Trợ lý gọi công cụ quá nhiều lần mà chưa ra kết quả. Hãy hỏi cụ thể hơn.");
        }
        finally { _tools.ReleaseCache(); }
    }

    private void Remember(string question, string answer)
    {
        _history.Add(new JsonObject { ["role"] = "user", ["content"] = question });
        _history.Add(new JsonObject { ["role"] = "assistant", ["content"] = answer });
        while (_history.Count > MaxHistoryMessages) _history.RemoveAt(0);
    }

    /// <summary>Gửi một yêu cầu nhỏ để kiểm tra khóa và tên mô hình. Ném AiException kèm lý do rõ ràng nếu lỗi.</summary>
    public async Task TestConnectionAsync(CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = _model,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "Trả lời đúng một từ: ok" }),
            ["max_tokens"] = 5,
        };
        await PostAsync(body, ct).ConfigureAwait(false);
    }

    private async Task<JsonObject> PostAsync(JsonObject body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(AiOrderTools.Json), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(req, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw new AiException("Không kết nối được tới Groq (kiểm tra mạng): " + ex.Message); }

        using (resp)
        {
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) throw new AiException(Describe(resp.StatusCode, text));
            try
            {
                var root = JsonNode.Parse(text);
                return root?["choices"]?[0]?["message"] as JsonObject
                       ?? throw new AiException("Groq trả về dữ liệu không đúng định dạng.");
            }
            catch (JsonException) { throw new AiException("Groq trả về dữ liệu không đọc được."); }
        }
    }

    private static string Describe(HttpStatusCode code, string body)
    {
        string detail = "";
        try { detail = JsonNode.Parse(body)?["error"]?["message"]?.GetValue<string>() ?? ""; } catch { /* bỏ qua */ }
        var head = code switch
        {
            HttpStatusCode.Unauthorized => "Khóa API không đúng hoặc đã bị thu hồi.",
            HttpStatusCode.NotFound => "Không tìm thấy mô hình hoặc địa chỉ (kiểm tra tên mô hình).",
            (HttpStatusCode)429 => "Đã chạm giới hạn của Groq (hết hạn mức hoặc gọi quá nhanh). Chờ một lúc rồi thử lại.",
            HttpStatusCode.BadRequest => "Groq từ chối yêu cầu (có thể mô hình không hỗ trợ gọi công cụ).",
            _ => $"Groq báo lỗi {(int)code}.",
        };
        return string.IsNullOrWhiteSpace(detail) ? head : head + " Chi tiết: " + detail;
    }
}
