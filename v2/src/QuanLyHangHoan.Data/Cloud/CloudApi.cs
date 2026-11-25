using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuanLyHangHoan.Data.Cloud;

/// <summary>Lỗi trả về từ cloud hoặc không kết nối được (Status = 0). Message là tiếng Việt cho người dùng.</summary>
public sealed class CloudApiException(string code, string message, int status) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public bool IsUnauthorized => Status == 401;
    public bool IsNetwork => Status == 0;
    public bool IsQuota => Status == 429;
}

public sealed record CloudPaired(string DeviceId, string Token, string Role);
public sealed record CloudSyncPage(long Rev, bool More, IReadOnlyList<JsonObject> Parcels);
public sealed record CloudQuotaInfo(string Day, long RequestsUsed, long RequestsLimit, long RowsWrittenUsed, long RowsWrittenLimit,
    long RowsReadUsed, long RowsReadLimit, string Level);
public sealed record CloudOpResult(string OpId, bool Ok, long Rev, JsonObject? Result, string? ErrorCode, string? ErrorMessage);

/// <summary>
/// Máy khách HTTP cho cloud (cloud/API.md). Không giữ trạng thái ngoài địa chỉ + token. Mọi lỗi mạng/HTTP đều thành
/// <see cref="CloudApiException"/> với thông báo tiếng Việt; không bao giờ ghi token ra thông báo lỗi.
/// </summary>
public sealed class CloudApi
{
    private readonly HttpClient _http;
    public string BaseUrl { get; }
    public string? Token { get; }

    public CloudApi(HttpClient http, string baseUrl, string? token)
    {
        _http = http;
        BaseUrl = NormalizeBaseUrl(baseUrl);
        Token = token;
    }

    /// <summary>"https://x.workers.dev" hoặc ".../" hoặc ".../v1" -> ".../v1". Chỉ nhận https (hoặc http cho máy cục bộ).</summary>
    public static string NormalizeBaseUrl(string url)
    {
        var u = (url ?? "").Trim().TrimEnd('/');
        if (u.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) u = u[..^3];
        if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            throw new CloudApiException("invalid", "Địa chỉ cloud không hợp lệ (cần bắt đầu bằng https://).", 0);
        return u + "/v1";
    }

    public Uri WebSocketUri()
    {
        var b = new UriBuilder(BaseUrl + "/ws") { Scheme = BaseUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws" };
        b.Query = "token=" + Uri.EscapeDataString(Token ?? "");
        return b.Uri;
    }

    private async Task<(HttpStatusCode Status, byte[] Body, HttpResponseHeaders Headers)> SendRawAsync(
        HttpMethod method, string path, HttpContent? content, IDictionary<string, string>? headers, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, BaseUrl + path) { Content = content };
        if (!string.IsNullOrEmpty(Token)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        if (headers is not null) foreach (var h in headers) req.Headers.TryAddWithoutValidation(h.Key, h.Value);
        try
        {
            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (res.IsSuccessStatusCode) return (res.StatusCode, body, res.Headers);
            throw ErrorFrom((int)res.StatusCode, body);
        }
        catch (CloudApiException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (TaskCanceledException) { throw new CloudApiException("timeout", "Cloud không phản hồi kịp (quá thời gian chờ).", 0); }
        catch (HttpRequestException) { throw new CloudApiException("network", "Không kết nối được tới cloud. Kiểm tra mạng.", 0); }
    }

    private static CloudApiException ErrorFrom(int status, byte[] body)
    {
        string code = status switch { 401 => "unauthorized", 403 => "forbidden", 404 => "not_found", 409 => "conflict", 429 => "quota_exceeded", _ => "http_" + status };
        string msg = status switch
        {
            401 => "Thiết bị chưa được ghép hoặc đã bị thu hồi.",
            403 => "Không đủ quyền thực hiện việc này.",
            429 => "Cloud tạm dừng vì hết hạn mức hôm nay.",
            >= 500 => "Cloud đang gặp sự cố, thử lại sau.",
            _ => "Cloud từ chối yêu cầu.",
        };
        try
        {
            var n = JsonNode.Parse(body);
            if (n?["error"] is JsonObject e)
            {
                if (e["code"]?.GetValue<string>() is { Length: > 0 } c) code = c;
                if (e["message"]?.GetValue<string>() is { Length: > 0 } m) msg = m;
            }
        }
        catch (JsonException) { /* thân không phải JSON: giữ thông báo mặc định */ }
        return new CloudApiException(code, msg, status);
    }

    private async Task<JsonNode> JsonAsync(HttpMethod method, string path, JsonNode? body, CancellationToken ct, IDictionary<string, string>? headers = null)
    {
        HttpContent? content = body is null ? null : new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        var (_, bytes, _) = await SendRawAsync(method, path, content, headers, ct).ConfigureAwait(false);
        if (bytes.Length == 0) return new JsonObject();
        try { return JsonNode.Parse(bytes) ?? new JsonObject(); }
        catch (JsonException) { throw new CloudApiException("invalid_response", "Cloud trả về dữ liệu không đúng định dạng.", 0); }
    }

    // ---- ghép thiết bị
    public async Task<CloudPaired> PairCompleteAsync(string code, string deviceName, string kind, CancellationToken ct = default)
    {
        var r = await JsonAsync(HttpMethod.Post, "/pair/complete", new JsonObject { ["code"] = code, ["deviceName"] = deviceName, ["kind"] = kind }, ct);
        return new(r["deviceId"]?.GetValue<string>() ?? "", r["token"]?.GetValue<string>() ?? "", r["role"]?.GetValue<string>() ?? "staff");
    }

    public async Task<(string Code, DateTime ExpiresAt)> PairStartAsync(CancellationToken ct = default)
    {
        var r = await JsonAsync(HttpMethod.Post, "/pair/start", new JsonObject(), ct);
        return (r["code"]?.GetValue<string>() ?? "", DateTimeOffset.FromUnixTimeMilliseconds(r["expiresAt"]?.GetValue<long>() ?? 0).LocalDateTime);
    }

    public async Task<(string DeviceId, string DeviceName, string Role)> MeAsync(CancellationToken ct = default)
    {
        var r = await JsonAsync(HttpMethod.Get, "/me", null, ct);
        return (r["deviceId"]?.GetValue<string>() ?? "", r["deviceName"]?.GetValue<string>() ?? "", r["role"]?.GetValue<string>() ?? "");
    }

    // ---- đồng bộ
    public async Task<CloudSyncPage> SyncAsync(long since, int limit = 500, CancellationToken ct = default)
    {
        var r = await JsonAsync(HttpMethod.Get, $"/sync?since={since}&limit={limit}", null, ct);
        var list = new List<JsonObject>();
        if (r["changes"] is JsonArray arr)
            foreach (var c in arr)
                if (c?["type"]?.GetValue<string>() == "parcel" && c["parcel"] is JsonObject p) list.Add((JsonObject)p.DeepClone());
        return new(r["rev"]?.GetValue<long>() ?? since, r["more"]?.GetValue<bool>() ?? false, list);
    }

    // ---- thao tác ghi
    public async Task<IReadOnlyList<CloudOpResult>> OpsAsync(IEnumerable<JsonObject> ops, CancellationToken ct = default)
    {
        var arr = new JsonArray();
        foreach (var o in ops) arr.Add(o);
        var r = await JsonAsync(HttpMethod.Post, "/ops", new JsonObject { ["ops"] = arr }, ct);
        var list = new List<CloudOpResult>();
        if (r["results"] is JsonArray res)
            foreach (var x in res)
            {
                if (x is not JsonObject o) continue;
                var ok = o["ok"]?.GetValue<bool>() ?? false;
                list.Add(new(o["opId"]?.GetValue<string>() ?? "", ok, o["rev"]?.GetValue<long>() ?? 0,
                    o["result"] as JsonObject, o["error"]?["code"]?.GetValue<string>(), o["error"]?["message"]?.GetValue<string>()));
            }
        return list;
    }

    /// <summary>Một thao tác, tự sinh opId nếu thiếu. Gửi lặp cùng opId là an toàn (idempotent).</summary>
    public async Task<CloudOpResult> OpAsync(JsonObject op, CancellationToken ct = default)
    {
        if (op["opId"] is null) op["opId"] = Guid.NewGuid().ToString();
        var res = await OpsAsync(new[] { op }, ct).ConfigureAwait(false);
        return res.Count > 0 ? res[0] : new(op["opId"]!.GetValue<string>(), false, 0, null, "invalid_response", "Cloud không trả kết quả.");
    }

    public async Task<JsonArray> HistoryAsync(string parcelId, CancellationToken ct = default) =>
        (await JsonAsync(HttpMethod.Get, $"/parcels/{Uri.EscapeDataString(parcelId)}/history", null, ct)) as JsonArray ?? new JsonArray();

    // ---- hạn mức, AI
    public async Task<CloudQuotaInfo> QuotaAsync(CancellationToken ct = default)
    {
        var r = await JsonAsync(HttpMethod.Get, "/quota", null, ct);
        long L(string a, string b) => r[a]?[b]?.GetValue<long>() ?? 0;
        return new(r["day"]?.GetValue<string>() ?? "", L("requests", "used"), L("requests", "limit"), L("rowsWritten", "used"), L("rowsWritten", "limit"),
            L("rowsRead", "used"), L("rowsRead", "limit"), r["level"]?.GetValue<string>() ?? "ok");
    }

    // ---- ảnh
    public async Task PutPhotoAsync(string photoId, string parcelId, string mime, bool thumb, byte[] bytes, CancellationToken ct = default)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(mime);
        var q = $"parcelId={Uri.EscapeDataString(parcelId)}&mime={Uri.EscapeDataString(mime)}" + (thumb ? "&kind=thumb" : "");
        await SendRawAsync(HttpMethod.Put, $"/photos/{Uri.EscapeDataString(photoId)}?{q}", content, null, ct).ConfigureAwait(false);
    }

    public async Task<byte[]> GetPhotoAsync(string photoId, bool thumb, CancellationToken ct = default)
    {
        var (_, body, _) = await SendRawAsync(HttpMethod.Get, $"/photos/{Uri.EscapeDataString(photoId)}" + (thumb ? "?size=thumb" : ""), null, null, ct).ConfigureAwait(false);
        return body;
    }

    // ---- nhập đơn
    public async Task<(int Added, int Updated, int Unchanged, int Rejected)> ImportAsync(string source, string signature, IEnumerable<JsonObject> parcels, CancellationToken ct = default)
    {
        var arr = new JsonArray();
        foreach (var p in parcels) arr.Add(p);
        var r = await JsonAsync(HttpMethod.Post, "/import", new JsonObject { ["source"] = source, ["signature"] = signature, ["parcels"] = arr }, ct);
        return (r["added"]?.GetValue<int>() ?? 0, r["updated"]?.GetValue<int>() ?? 0, r["unchanged"]?.GetValue<int>() ?? 0, (r["rejected"] as JsonArray)?.Count ?? 0);
    }
}
