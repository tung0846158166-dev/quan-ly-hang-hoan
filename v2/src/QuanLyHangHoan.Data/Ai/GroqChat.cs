using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuanLyHangHoan.Data.Ai;

public class AiException(string message) : Exception(message);

/// <summary>Mô hình không phản hồi trong thời gian cho phép (treo/chậm): chuyển sang mô hình kế tiếp thay vì chờ mãi.</summary>
public sealed class AiTimeoutException(string message) : AiException(message);

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

    /// <summary>Thời gian chờ tối đa cho MỘT yêu cầu tới MỘT mô hình. Quá hạn thì bỏ qua mô hình đó (cho nghỉ 2 phút) và thử mô hình kế tiếp.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(45);

    public void Reset() => _history.Clear();

    public QuanLyHangHoan.Core.Models.Order? TryGetOrder(string id) => _tools.TryGetOrder(id);

    private string SystemPrompt()
    {
        var now = _clock();
        return "Bạn là trợ lý AI tích hợp trong phần mềm 'Quản lý hàng hoàn' của một shop bán hàng trên Shopee, TikTok Shop, Lazada. " +
               "Khi được hỏi bạn là ai, hãy nói bạn là trợ lý AI của phần mềm này (đừng nhận là ChatGPT, Llama hay tên mô hình nào). Trả lời bằng tiếng Việt, ngắn gọn, rõ ràng.\n" +
               $"Bây giờ là {now:dd/MM/yyyy HH:mm} ({new[] { "Chủ nhật", "Thứ hai", "Thứ ba", "Thứ tư", "Thứ năm", "Thứ sáu", "Thứ bảy" }[(int)now.DayOfWeek]}), hôm nay là {now:yyyy-MM-dd}.\n" +
               "CÁCH LÀM VIỆC:\n" +
               "- Bạn có công cụ truy vấn dữ liệu thật của phần mềm: overview (tổng quan), count_orders (đếm + tổng tiền), aggregate (gom nhóm theo trạng thái/sàn/đơn vị vận chuyển/loại đơn/sản phẩm/ngày/tuần/tháng/mức cảnh báo/lý do/thời gian chờ/khoảng giá..., theo số kiện, tổng tiền hoặc số lượng), compare_periods (so sánh hai kỳ), list_orders (danh sách), find_order (tìm theo mã), order_detail (chi tiết một đơn), order_groups (mã đơn nhiều kiện A/B/C), return_watch (bảng Hàng hoàn về theo 4 chặng), product_stats (sản phẩm), recent_activity (nhật ký hoạt động), system_status (kết nối điện thoại, sao lưu, số đơn từng nơi), search_orders (tìm tự do nhiều từ khóa), turnaround_stats (thời gian xử lý: trung bình, p90, đơn chậm nhất, theo đơn vị vận chuyển/sàn), data_health (kiểm tra dữ liệu thiếu/sai), duplicates (mã vận đơn trùng), activity_stats (thống kê hoạt động theo ngày/loại), notifications (thông báo), app_guide (hướng dẫn dùng phần mềm).\n" +
               "- Khi người dùng hỏi cách dùng phần mềm ('làm sao', 'ở đâu', 'có tính năng gì') thì gọi app_guide và chỉ nói những gì hướng dẫn có, không bịa tính năng.\n" +
               "- Câu hỏi cần số liệu thì GỌI CÔNG CỤ NGAY, không hỏi lại 'bạn có muốn tôi làm không'. Câu hỏi nhiều phần (ví dụ 'giá trị chưa về và sản phẩm nào nhiều nhất') thì gọi nhiều công cụ liên tiếp rồi trả lời đủ mọi phần trong MỘT câu trả lời.\n" +
               "- Mọi con số và danh sách đơn PHẢI lấy từ công cụ. Tuyệt đối không bịa, không đoán, không tự tính nhẩm khi công cụ đã cho số.\n" +
               "- Từ ngữ: 'chưa về', 'chưa nhận', 'đang chờ' đều là đơn chưa nhận hàng về kho (status 'Đang chờ'). 'Đã về', 'đã nhận' là 'Đã nhận'. 'Giá trị', 'tiền', 'doanh thu hoàn' là giá trị đơn (đồng), trường 'value'/'totalValue'. 'Quá hạn' / 'báo đỏ' là chờ quá 24 giờ kể từ lúc hàng về, 'báo vàng' là quá 12 giờ.\n" +
               "- Tiền viết dạng 1.234.567 đ. Số kiện/số lượng viết rõ đơn vị.\n" +
               "- Một mã đơn có thể có nhiều kiện hoàn (A/B/C), mỗi kiện một dòng riêng với mã vận đơn riêng. Đếm theo KIỆN (dòng), nói rõ 'kiện' hoặc 'dòng'.\n" +
               "- 'Hôm nay có bao nhiêu đơn về' nghĩa là status 'Đã nhận' với ngày nhận tại kho là hôm nay (date_field recv).\n" +
               "- Khi liệt kê đơn, ghi mã đơn và mã vận đơn; người dùng có thể bấm vào đơn bên dưới để xem chi tiết. Nếu kết quả bị cắt (truncated) hãy nói rõ còn bao nhiêu.\n" +
               "- Câu hỏi mơ hồ (ví dụ không rõ ngày nào): chọn cách hiểu hợp lý nhất và làm luôn, nói rõ bạn đã hiểu thế nào. Chỉ hỏi lại khi thật sự không thể đoán.\n" +
               "- Bạn chỉ ĐỌC dữ liệu. Không thể sửa, xóa, nhận hàng hay gửi gì ra ngoài. Yêu cầu thay đổi dữ liệu thì hướng dẫn người dùng tự làm trong phần mềm.\n" +
               "- Dữ liệu không có tên, số điện thoại, địa chỉ người nhận (đã được ẩn). Nếu được hỏi, nói rằng thông tin đó đã bị ẩn.\n" +
               "- Nếu công cụ báo lỗi hoặc không có dữ liệu cho câu hỏi, nói thẳng điều đó và gợi ý cách hỏi khác dùng được.";
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

    /// <summary>
    /// Thử kết nối TẤT CẢ mô hình CÙNG LÚC (một yêu cầu rất nhỏ cho mỗi mô hình, mỗi mô hình tối đa <see cref="RequestTimeout"/>), nên một mô hình treo không làm cả bước thử bị đứng.
    /// Báo từng kết quả ngay khi có qua <paramref name="progress"/>. Trả về danh sách theo thứ tự mô hình: (nhãn, lỗi hoặc null nếu dùng được).
    /// </summary>
    public async Task<IReadOnlyList<(string Label, string? Error)>> TestAllAsync(CancellationToken ct = default, IProgress<(string Label, string? Error)>? progress = null)
    {
        async Task<(string Label, string? Error)> One(AiEndpoint ep)
        {
            var body = new JsonObject
            {
                ["model"] = ep.Model,
                ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "Trả lời đúng một từ: ok" }),
                ["max_tokens"] = 16,
            };
            (string, string?) r;
            try { await PostAsync(ep, body, ct).ConfigureAwait(false); r = (ep.Label, null); }
            catch (OperationCanceledException) { throw; }
            catch (AiException ex) { r = (ep.Label, ex.Message); }
            progress?.Report(r);
            return r;
        }
        var results = await Task.WhenAll(_endpoints.Select(One)).ConfigureAwait(false);
        return results;
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
            catch (AiTimeoutException ex)
            {
                _restUntil[ep.Label] = Now() + TimeSpan.FromMinutes(2);   // treo/chậm: cho nghỉ để câu hỏi sau không chờ lại vô ích
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
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RequestTimeout);
        var token = timeoutCts.Token;
        AiTimeoutException TimedOut() => new($"{ep.Label} không phản hồi sau {RequestTimeout.TotalSeconds:0} giây.");
        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(req, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw TimedOut(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw new AiException($"Không kết nối được tới {ep.Provider} (kiểm tra mạng): " + ex.Message); }

        using (resp)
        {
            string text;
            try { text = await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw TimedOut(); }
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
