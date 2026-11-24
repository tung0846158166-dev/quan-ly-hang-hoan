using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuanLyHangHoan.Data.Ai;

public class AiException(string message) : Exception(message);

/// <summary>Lỗi HTTP từ nhà cung cấp AI (để quyết định có chuyển sang mô hình dự phòng không).</summary>
public sealed class AiHttpException(string message, HttpStatusCode code, TimeSpan? retryAfter, string body) : AiException(message)
{
    public HttpStatusCode Code { get; } = code;
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public string Body { get; } = body;
}

public sealed record AiAnswer(string Text, IReadOnlyList<AiOrderRef> Refs, IReadOnlyList<string> Trace);

/// <summary>Một mô hình cụ thể ở một nhà cung cấp (API tương thích OpenAI).</summary>
public sealed record AiEndpoint(string Provider, string BaseUrl, string ApiKey, string Model)
{
    public string Label => Provider + "/" + Model;
}

public interface IAiChat
{
    Task<AiAnswer> AskAsync(string question, CancellationToken ct = default);
    void Reset();
    QuanLyHangHoan.Core.Models.Order? TryGetOrder(string id);
}

/// <summary>
/// Trò chuyện với AI (Groq, Google Gemini qua đường dẫn tương thích OpenAI) và cho AI gọi công cụ chỉ-đọc của <see cref="AiOrderTools"/>.
/// Chỉ gửi: câu hỏi, lịch sử chữ của phiên, và KẾT QUẢ công cụ (trường trong danh sách trắng, đã che số điện thoại).
/// Chuyển dự phòng: thử mô hình đầu danh sách; gặp 429 (hết hạn mức) hoặc lỗi riêng của mô hình thì nhảy sang mô hình kế tiếp
/// và nhớ mô hình đó đang "nghỉ" một lúc để các câu hỏi sau không thử lại vô ích.
/// </summary>
public sealed class AiChatService : IAiChat
{
    public const string GroqBaseUrl = "https://api.groq.com/openai/v1";
    public const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai";
    public const string DefaultBaseUrl = GroqBaseUrl;
    public const string DefaultModel = "openai/gpt-oss-120b";
    public const string DefaultGroqModels = "openai/gpt-oss-120b, qwen/qwen3.8-27b, openai/gpt-oss-20b";
    public const string DefaultGeminiModels = "gemini-2.5-flash, gemini-2.5-flash-lite";
    private const int MaxRounds = 6;
    private const int MaxToolChars = 14000;
    private const int MaxHistoryMessages = 12;

    private readonly HttpClient _http;
    private readonly IReadOnlyList<AiEndpoint> _endpoints;
    private readonly AiOrderTools _tools;
    private readonly Func<DateTime> _clock;
    private readonly List<JsonObject> _history = new();
    private readonly Dictionary<string, DateTime> _restUntil = new();

    public AiChatService(HttpClient http, IReadOnlyList<AiEndpoint> endpoints, AiOrderTools tools, Func<DateTime>? clock = null)
    {
        if (endpoints.Count == 0) throw new ArgumentException("Cần ít nhất một mô hình.", nameof(endpoints));
        _http = http;
        _endpoints = endpoints;
        _tools = tools;
        _clock = clock ?? (() => DateTime.Now);
    }

    /// <summary>Một nhà cung cấp (Groq), có thể liệt kê nhiều mô hình cách nhau bằng dấu phẩy/xuống dòng (theo thứ tự ưu tiên).</summary>
    public AiChatService(HttpClient http, string apiKey, string models, AiOrderTools tools, Func<DateTime>? clock = null, string? baseUrl = null)
        : this(http, BuildEndpoints("groq", baseUrl ?? GroqBaseUrl, apiKey, models, DefaultModel), tools, clock) { }

    public static IReadOnlyList<string> SplitModels(string? text, string fallback)
    {
        var list = (text ?? "").Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).ToList();
        if (list.Count == 0 && !string.IsNullOrWhiteSpace(fallback)) list.Add(fallback.Trim());
        return list;
    }

    public static List<AiEndpoint> BuildEndpoints(string provider, string baseUrl, string apiKey, string? models, string fallbackModel) =>
        SplitModels(models, fallbackModel).Select(m => new AiEndpoint(provider, baseUrl.TrimEnd('/'), apiKey, m)).ToList();

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
        var usedModels = new List<string>();

        try
        {
            for (var round = 0; round < MaxRounds; round++)
            {
                var body = new JsonObject
                {
                    ["messages"] = new JsonArray(msgs.Select(m => (JsonNode)m.DeepClone()).ToArray()),
                    ["tools"] = tools.DeepClone(),
                    ["tool_choice"] = "auto",
                    ["temperature"] = 0.1,
                };
                var (message, used) = await PostWithFallbackAsync(body, ct).ConfigureAwait(false);
                if (!usedModels.Contains(used.Label)) usedModels.Add(used.Label);
                var calls = message["tool_calls"] as JsonArray;
                if (calls is null || calls.Count == 0)
                {
                    var text = (message["content"]?.GetValue<string>() ?? "").Trim();
                    if (text.Length == 0) text = "(Không có câu trả lời.)";
                    Remember(question, text);
                    var distinct = refs.GroupBy(r => r.Id).Select(g => g.First()).Take(50).ToList();
                    trace.Insert(0, "Mô hình: " + string.Join(" → ", usedModels));
                    return new AiAnswer(text, distinct, trace);
                }

                // Lưu lượt trả lời có gọi công cụ rồi chạy từng công cụ.
                msgs.Add(new JsonObject { ["role"] = "assistant", ["content"] = message["content"]?.GetValue<string>() ?? "", ["tool_calls"] = calls.DeepClone() });
                foreach (var call in calls)
                {
                    var id = call?["id"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N");
                    var fn = call?["function"];
                    var name = fn?["name"]?.GetValue<string>() ?? "";
                    var args = fn?["arguments"]?.GetValue<string>() ?? "{}";
                    var result = _tools.Execute(name, args);
                    refs.AddRange(result.Refs);
                    trace.Add($"{name}({args})");
                    var json = result.Json.Length > MaxToolChars ? result.Json[..MaxToolChars] + "…(đã cắt bớt)" : result.Json;
                    msgs.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = id, ["name"] = name, ["content"] = json });
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

    /// <summary>Thử kết nối TỪNG mô hình (một yêu cầu rất nhỏ). Trả về danh sách (nhãn, lỗi hoặc null nếu dùng được).</summary>
    public async Task<IReadOnlyList<(string Label, string? Error)>> TestAllAsync(CancellationToken ct = default)
    {
        var res = new List<(string, string?)>();
        foreach (var ep in _endpoints)
        {
            var body = new JsonObject
            {
                ["model"] = ep.Model,
                ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "Trả lời đúng một từ: ok" }),
                ["max_tokens"] = 16,
            };
            try { await PostAsync(ep, body, ct).ConfigureAwait(false); res.Add((ep.Label, null)); }
            catch (OperationCanceledException) { throw; }
            catch (AiException ex) { res.Add((ep.Label, ex.Message)); }
        }
        return res;
    }

    private DateTime Now() => _clock();

    private async Task<(JsonObject Message, AiEndpoint Used)> PostWithFallbackAsync(JsonObject body, CancellationToken ct)
    {
        var errors = new List<(AiEndpoint Ep, AiException Ex)>();
        foreach (var ep in _endpoints)
        {
            if (_restUntil.TryGetValue(ep.Label, out var until) && until > Now()) continue;
            var attempt = (JsonObject)body.DeepClone();
            attempt["model"] = ep.Model;
            try
            {
                var msg = await PostAsync(ep, attempt, ct).ConfigureAwait(false);
                return (msg, ep);
            }
            catch (OperationCanceledException) { throw; }
            catch (AiHttpException ex) when (ShouldFallback(ex))
            {
                _restUntil[ep.Label] = Now() + RestTime(ex);
                errors.Add((ep, ex));
            }
            catch (AiException ex) when (ex is not AiHttpException)
            {
                // Lỗi mạng/định dạng: thử mô hình kế tiếp nhưng không bắt nghỉ.
                errors.Add((ep, ex));
            }
        }

        if (errors.Count == 0)
        {
            var soonest = _restUntil.Values.Min();
            var wait = Math.Max(1, (int)Math.Ceiling((soonest - Now()).TotalMinutes));
            throw new AiException($"Tất cả mô hình đang nghỉ vì hết hạn mức. Thử lại sau khoảng {wait} phút.");
        }
        if (_endpoints.Count == 1) throw errors[0].Ex;
        var sb = new StringBuilder($"Cả {_endpoints.Count} mô hình đều không dùng được lúc này:");
        foreach (var (ep, ex) in errors) sb.Append("\n• ").Append(ep.Label).Append(": ").Append(ex.Message);
        throw new AiException(sb.ToString());
    }

    private static bool ShouldFallback(AiHttpException ex) =>
        ex.Code is HttpStatusCode.TooManyRequests or HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            or HttpStatusCode.BadRequest or HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.InternalServerError
            or HttpStatusCode.RequestEntityTooLarge;

    private static TimeSpan RestTime(AiHttpException ex)
    {
        if (ex.RetryAfter is { } ra) return ra < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : (ra > TimeSpan.FromHours(2) ? TimeSpan.FromHours(2) : ra);
        var b = ex.Body.ToLowerInvariant();
        return ex.Code switch
        {
            HttpStatusCode.TooManyRequests => b.Contains("per day") || b.Contains("tpd") || b.Contains("rpd") || b.Contains("daily") ? TimeSpan.FromMinutes(30) : TimeSpan.FromSeconds(60),
            HttpStatusCode.NotFound => TimeSpan.FromHours(6),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => TimeSpan.FromMinutes(10),
            HttpStatusCode.BadRequest => TimeSpan.FromMinutes(2),
            _ => TimeSpan.FromSeconds(30),
        };
    }

    private async Task<JsonObject> PostAsync(AiEndpoint ep, JsonObject body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, ep.BaseUrl + "/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(AiOrderTools.Json), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ep.ApiKey);
        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(req, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw new AiException($"Không kết nối được tới {ep.Provider} (kiểm tra mạng): " + ex.Message); }

        using (resp)
        {
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                TimeSpan? ra = resp.Headers.RetryAfter?.Delta;
                throw new AiHttpException(Describe(ep, resp.StatusCode, text), resp.StatusCode, ra, text);
            }
            try
            {
                var root = JsonNode.Parse(text);
                return root?["choices"]?[0]?["message"] as JsonObject
                       ?? throw new AiException($"{ep.Provider} trả về dữ liệu không đúng định dạng.");
            }
            catch (JsonException) { throw new AiException($"{ep.Provider} trả về dữ liệu không đọc được."); }
        }
    }

    private static string Describe(AiEndpoint ep, HttpStatusCode code, string body)
    {
        string detail = "";
        try
        {
            var root = JsonNode.Parse(body);
            detail = root?["error"]?["message"]?.GetValue<string>() ?? (root as JsonArray)?[0]?["error"]?["message"]?.GetValue<string>() ?? "";
        }
        catch { /* bỏ qua */ }
        var who = ep.Provider;
        var head = code switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"Khóa API {who} không đúng hoặc đã bị thu hồi.",
            HttpStatusCode.NotFound => $"Không tìm thấy mô hình hoặc địa chỉ ({who}): kiểm tra tên mô hình.",
            HttpStatusCode.TooManyRequests => $"Đã chạm giới hạn của {who} (hết hạn mức hoặc gọi quá nhanh).",
            HttpStatusCode.BadRequest => $"{who} từ chối yêu cầu (có thể mô hình không hỗ trợ gọi công cụ).",
            _ => $"{who} báo lỗi {(int)code}.",
        };
        return string.IsNullOrWhiteSpace(detail) ? head : head + " Chi tiết: " + (detail.Length > 300 ? detail[..300] + "…" : detail);
    }
}
