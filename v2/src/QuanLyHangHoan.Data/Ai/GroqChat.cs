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
    /// <summary>NVIDIA NIM (build.nvidia.com), chuẩn OpenAI: tuyến dự phòng cuối sau Groq và Google.</summary>
    public const string NvidiaBaseUrl = "https://integrate.api.nvidia.com/v1";
    /// <summary>Thử thật 26/11/2026: nemotron-3-ultra gọi công cụ + trả lời ~3 giây; meta/llama-3.3 đã bị NVIDIA ngừng (26/08/2026).</summary>
    public const string DefaultNvidiaModels = "nvidia/nemotron-3-ultra-550b-a55b, nvidia/nemotron-3.5-lightning-30b-a3b";
    /// <summary>Cloudflare Workers AI: chỉ dùng qua cổng AI của cloud (cần mã tài khoản Cloudflare). Tuyến dự phòng sau NVIDIA.</summary>
    /// <summary>Thử thật 26/11/2026: gpt-oss-120b và llama-4-scout gọi công cụ + trả lời đúng; llama-3.3 lặp gọi công cụ; qwen3-30b đã ngừng.</summary>
    public const string DefaultCloudflareModels = "@cf/openai/gpt-oss-120b, @cf/meta/llama-4-scout-17b-16e-instruct";
    public const string DefaultBaseUrl = GroqBaseUrl;
    public const string DefaultModel = "openai/gpt-oss-120b";
    public const string DefaultGroqModels = "openai/gpt-oss-120b, qwen/qwen3.8-27b, openai/gpt-oss-20b";
    public const string DefaultGeminiModels = "gemini-4-hydrogen, gemini-3.8-flash, gemini-3.5-flash-lite";
    /// <summary>Mô hình Google dự phòng cuối (gemini-2.x đã bị Google ngừng, thử thật 26/11/2026).</summary>
    public const string FallbackGeminiModel = "gemini-3.5-flash-lite";
    private const int MaxRounds = 6;
    private const int MaxToolChars = 6000;
    private const int MaxHistoryMessages = 6;
    private const int MaxHistoryChars = 500;
    /// <summary>Ngân sách ước lượng cho MỘT yêu cầu (token). Gói miễn phí Groq giới hạn 7.000-8.000 token mỗi yêu cầu/phút, nên giữ dưới mức này.</summary>
    private const int TokenBudget = 4500;

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
        return "Bạn là trợ lý AI của phần mềm 'Quản lý hàng hoàn' (Shopee, TikTok Shop, Lazada). Khi được hỏi bạn là ai: trợ lý AI của phần mềm này (đừng nhận là ChatGPT, Llama hay tên mô hình). Trả lời tiếng Việt, ngắn gọn.\n" +
               $"Bây giờ là {now:dd/MM/yyyy HH:mm} ({new[] { "Chủ nhật", "Thứ hai", "Thứ ba", "Thứ tư", "Thứ năm", "Thứ sáu", "Thứ bảy" }[(int)now.DayOfWeek]}), hôm nay là {now:yyyy-MM-dd}.\n" +
               "QUY TẮC:\n" +
               "- Số liệu và danh sách đơn PHẢI lấy từ công cụ, không bịa, không nhẩm. Cần số liệu thì gọi công cụ ngay, đừng hỏi lại; câu nhiều phần thì gọi nhiều công cụ rồi trả lời đủ trong MỘT lần.\n" +
               "- 'chưa về/chưa nhận/đang chờ' = status 'Đang chờ'; 'đã về/đã nhận' = 'Đã nhận'. 'tiền/giá trị' = value (đồng, viết 1.234.567 đ). 'quá hạn/báo đỏ' = chờ quá 24 giờ kể từ lúc hàng về, 'báo vàng' = quá 12 giờ.\n" +
               "- Đếm theo KIỆN (dòng); một mã đơn có thể có nhiều kiện A/B/C riêng. 'Về hôm nay' = Đã nhận có ngày nhận kho hôm nay (date_field recv).\n" +
               "- Liệt kê đơn: ghi mã đơn + mã vận đơn (người dùng bấm được bên dưới); kết quả bị cắt (truncated) thì nói rõ.\n" +
               "- Câu mơ hồ: chọn cách hiểu hợp lý, làm luôn, nói rõ đã hiểu thế nào.\n" +
               "- Hỏi cách dùng/ý nghĩa/màn hình nào: gọi app_guide (topic man_hinh, y_nghia_du_lieu, cau_hoi_mau, nap_don...), không bịa tính năng. Bạn chỉ ĐỌC dữ liệu, không sửa/xóa/nhận hàng.\n" +
               "CÓ TRONG PHẦN MỀM: Trang chủ; SHOPEE: Hoàn hàng, Đơn hủy; TIKTOK: Hoàn hàng; CHUNG: Quét nhận hàng, Đồng bộ điện thoại, Hàng hoàn về (4 chặng), Bảng đơn mới, Đang theo dõi, Kho lưu trữ, Đã xóa; NẠP ĐƠN: file Excel, thư mục, Google Sheet, Salework; Thống kê, Nhật ký, Thông báo (chuông), Cài đặt, ✨AI.\n" +
               "VÍ DỤ: 'còn bao nhiêu đơn chưa về/tổng tiền' → dùng số liệu có sẵn. 'đơn nào quá hạn/báo đỏ' → list_orders(status Đang chờ, alert red). 'hôm nay về những đơn nào' → list_orders(status Đã nhận, date_field recv, from=to=today). " +
               "'sản phẩm hoàn nhiều nhất tháng này' → product_stats(from đầu tháng). 'tuần này so với tuần trước' → compare_periods. 'sàn/hãng nào nhiều' → aggregate(group_by platform|carrier). 'tìm đơn SPXVN...' → find_order. 'đơn hủy' → count_orders/list_orders(order_type 'Hủy').\n" +
               "- Dữ liệu không có tên, SĐT, địa chỉ người nhận (đã ẩn). Công cụ lỗi hoặc không có dữ liệu: nói thẳng và gợi ý cách hỏi khác.\n" +
               SnapshotBlock();
    }

    /// <summary>Số liệu hiện tại đính sẵn: câu hỏi chỉ cần các số này thì trả lời luôn, KHÔNG gọi công cụ (đỡ tốn token/vòng).</summary>
    private string SnapshotBlock()
    {
        var snap = _tools.Snapshot();
        if (snap.Length == 0) return "";
        return "SỐ LIỆU HIỆN TẠI (tính sẵn, đồng, theo kiện): " + snap + "\n" +
               "- Chỉ cần các số này thì trả lời NGAY, không gọi công cụ. Cần danh sách/lọc khác/chi tiết/kỳ khác thì gọi công cụ ĐỦ trong một lượt rồi trả lời luôn.";
    }

    public async Task<AiAnswer> AskAsync(string question, CancellationToken ct = default)
    {
        var msgs = new List<JsonObject> { new() { ["role"] = "system", ["content"] = SystemPrompt() } };
        msgs.AddRange(_history.Select(m => (JsonObject)m.DeepClone()));
        msgs.Add(new JsonObject { ["role"] = "user", ["content"] = question });

        var refs = new List<AiOrderRef>();
        var trace = new List<string>();
        var tools = JsonNode.Parse(AiOrderTools.DefinitionsFor(question))!;
        var usedModels = new List<string>();
        var done = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            for (var round = 0; round < MaxRounds; round++)
            {
                var body = new JsonObject
                {
                    ["messages"] = new JsonArray(msgs.Select(m => (JsonNode)m.DeepClone()).ToArray()),
                    ["tools"] = tools.DeepClone(),
                    // 2 vòng cuối: cấm gọi thêm công cụ, buộc chốt đáp án từ dữ liệu đã có (tránh lòng vòng và "gọi công cụ quá nhiều lần").
                    ["tool_choice"] = round >= MaxRounds - 2 ? "none" : "auto",
                    ["temperature"] = 0.1,
                };
                ShrinkToBudget(body);
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
                    string json;
                    if (!done.Add(name + "|" + args))
                    {
                        // Gọi lại y hệt: không chạy lại, không gửi lại kết quả lớn (đã có ở trên).
                        json = "{\"note\":\"Đã gọi công cụ này với cùng tham số ở trên; dùng kết quả đó để trả lời.\"}";
                    }
                    else
                    {
                        var result = _tools.Execute(name, args);
                        refs.AddRange(result.Refs);
                        trace.Add($"{name}({args})");
                        json = result.Json.Length > MaxToolChars ? result.Json[..MaxToolChars] + "…(đã cắt bớt)" : result.Json;
                    }
                    msgs.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = id, ["name"] = name, ["content"] = json });
                }
            }
            throw new AiException("Trợ lý gọi công cụ quá nhiều lần mà chưa ra kết quả. Hãy hỏi cụ thể hơn.");
        }
        finally { _tools.ReleaseCache(); }
    }

    /// <summary>Ước lượng số token của yêu cầu (chữ Việt ~ 2,8 ký tự/token).</summary>
    internal static int EstimateTokens(JsonObject body) => (int)(body.ToJsonString(AiOrderTools.Json).Length / 2.8);

    /// <summary>Nếu yêu cầu quá lớn: bỏ bớt lịch sử cũ, rồi chỉ giữ 3 công cụ cốt lõi. Không bao giờ bỏ câu hỏi hiện tại.</summary>
    private static void ShrinkToBudget(JsonObject body)
    {
        if (EstimateTokens(body) <= TokenBudget) return;
        var msgs = (JsonArray)body["messages"]!;
        // 1. bỏ lịch sử cũ (giữ system + vài tin cuối, kể cả tin công cụ của lượt hiện tại)
        while (EstimateTokens(body) > TokenBudget && msgs.Count > 3 && msgs[1]!["role"]!.GetValue<string>() is "user" or "assistant")
            msgs.RemoveAt(1);
        if (EstimateTokens(body) <= TokenBudget) return;
        // 2. chỉ giữ công cụ cốt lõi
        if (body["tools"] is JsonArray tools)
        {
            for (var i = tools.Count - 1; i >= 0; i--)
                if (tools[i]!["function"]!["name"]!.GetValue<string>() is not ("count_orders" or "aggregate" or "list_orders")) tools.RemoveAt(i);
        }
    }

    private void Remember(string question, string answer)
    {
        if (answer.Length > MaxHistoryChars) answer = answer[..MaxHistoryChars] + "…";
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
            if (ep.Provider == "gemini") SanitizeToolsForGemini(attempt);
            var waited = false;
            for (var pass = 0; pass < 3; pass++)
            {
                try
                {
                    var msg = await PostAsync(ep, attempt, ct).ConfigureAwait(false);
                    return (msg, ep);
                }
                catch (OperationCanceledException) { throw; }
                catch (AiHttpException ex) when (ex.Code == HttpStatusCode.TooManyRequests && !waited && ShortRetryDelay(ex) is { } wait)
                {
                    // Chạm giới hạn theo PHÚT và máy chủ báo chờ chỉ vài giây: chờ rồi thử lại đúng mô hình này, đừng bỏ sang mô hình khác.
                    waited = true;
                    await Task.Delay(wait, ct).ConfigureAwait(false);
                    continue;
                }
                catch (AiHttpException ex) when (ex.Code == HttpStatusCode.RequestEntityTooLarge && pass == 0 && KeepCoreToolsOnly(attempt))
                {
                    // 413: yêu cầu vượt giới hạn kích thước của mô hình này: thử lại NGAY với chỉ các công cụ cốt lõi trước khi bỏ sang mô hình khác.
                    continue;
                }
                catch (AiHttpException ex) when (ShouldFallback(ex))
                {
                    _restUntil[ep.Label] = Now() + RestTime(ex);
                    errors.Add((ep, ex));
                    break;
                }
                catch (AiTimeoutException ex)
                {
                    _restUntil[ep.Label] = Now() + TimeSpan.FromMinutes(2);   // treo/chậm: cho nghỉ để câu hỏi sau không chờ lại vô ích
                    errors.Add((ep, ex));
                    break;
                }
                catch (AiException ex) when (ex is not AiHttpException)
                {
                    // Lỗi mạng/định dạng: thử mô hình kế tiếp nhưng không bắt nghỉ.
                    errors.Add((ep, ex));
                    break;
                }
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

    /// <summary>Thời gian chờ ngắn (tối đa <see cref="ShortRetryMax"/>) mà máy chủ báo trong 429 ("try again in 659ms" / "in 2.5s" / Retry-After); null nếu không có hoặc quá lâu.</summary>
    public static TimeSpan ShortRetryMax { get; set; } = TimeSpan.FromSeconds(8);

    private static TimeSpan? ShortRetryDelay(AiHttpException ex)
    {
        TimeSpan? d = ex.RetryAfter;
        if (d is null)
        {
            var m = System.Text.RegularExpressions.Regex.Match(ex.Body ?? "", @"try again in\s+([0-9]+(?:\.[0-9]+)?)\s*(ms|s|m)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                d = m.Groups[2].Value.ToLowerInvariant() switch { "ms" => TimeSpan.FromMilliseconds(v), "s" => TimeSpan.FromSeconds(v), _ => TimeSpan.FromMinutes(v) };
        }
        if (d is null || d.Value > ShortRetryMax) return null;
        return d.Value + TimeSpan.FromMilliseconds(250);
    }

    /// <summary>Gemini (đường OpenAI-compat) từ chối lược đồ công cụ có "properties" rỗng hoặc "additionalProperties": bỏ các phần đó cho yêu cầu gửi Gemini.</summary>
    public static void SanitizeToolsForGemini(JsonObject body)
    {
        if (body["tools"] is not JsonArray tools) return;
        foreach (var t in tools)
        {
            if (t?["function"] is not JsonObject fn || fn["parameters"] is not JsonObject prm) continue;
            StripUnsupported(prm);
            if (prm["properties"] is not JsonObject props || props.Count == 0) fn.Remove("parameters");
        }
    }

    private static void StripUnsupported(JsonNode? node)
    {
        if (node is JsonObject o)
        {
            o.Remove("additionalProperties");
            o.Remove("default");
            foreach (var kv in o.ToList()) StripUnsupported(kv.Value);
        }
        else if (node is JsonArray a) foreach (var x in a) StripUnsupported(x);
    }

    /// <summary>Chỉ giữ 3 công cụ cốt lõi. Trả true nếu đã bỏ bớt công cụ (đáng thử lại).</summary>
    private static bool KeepCoreToolsOnly(JsonObject body)
    {
        if (body["tools"] is not JsonArray tools) return false;
        var removed = false;
        for (var i = tools.Count - 1; i >= 0; i--)
            if (tools[i]!["function"]!["name"]!.GetValue<string>() is not ("count_orders" or "aggregate" or "list_orders")) { tools.RemoveAt(i); removed = true; }
        return removed;
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
        // Không có câu lý do đọc được (đa số do định dạng lạ): hiện nguyên văn đoạn đầu để biết máy chủ chê gì.
        if (string.IsNullOrWhiteSpace(detail) && !string.IsNullOrWhiteSpace(body))
            detail = System.Text.RegularExpressions.Regex.Replace(body.Trim(), @"\s+", " ");
        detail = System.Text.RegularExpressions.Regex.Replace(detail, @"org_[A-Za-z0-9]+", "org_…");   // không hiện mã tổ chức
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
