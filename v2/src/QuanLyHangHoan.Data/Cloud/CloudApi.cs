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
/// <summary>Tư cách thành viên Bảng Hàng Hoàn Về: giờ vào bảng (ms) và cờ đã bị bỏ khỏi bảng.</summary>
public sealed record CloudWatchChange(string Key, long Since, bool Off);
public sealed record CloudSyncPage(long Rev, bool More, IReadOnlyList<JsonObject> Parcels, IReadOnlyList<CloudWatchChange>? Watch = null);
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

    /// <summary>Chủ cửa hàng: các thiết bị đã ghép (không bao giờ kèm token).</summary>
    public async Task<IReadOnlyList<CloudDeviceInfo>> DevicesAsync(CancellationToken ct = default)
    {
        var list = new List<CloudDeviceInfo>();
        if (await JsonAsync(HttpMethod.Get, "/devices", null, ct) is not JsonArray arr) return list;
        static long L(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? (long)d : 0;
        foreach (var x in arr.OfType<JsonObject>())
            list.Add(new CloudDeviceInfo(x["deviceId"]?.GetValue<string>() ?? "", x["deviceName"]?.GetValue<string>() ?? "", x["kind"]?.GetValue<string>() ?? "",
                x["role"]?.GetValue<string>() ?? "", L(x["lastSeenAt"]), x["revoked"] is JsonValue rv && rv.TryGetValue<bool>(out var b) && b));
        return list;
    }

    /// <summary>Chủ cửa hàng thu hồi một thiết bị (máy mất / nhân viên nghỉ): token của máy đó hết dùng được ngay.</summary>
    public async Task RevokeDeviceAsync(string deviceId, CancellationToken ct = default) =>
        await JsonAsync(HttpMethod.Delete, "/devices/" + Uri.EscapeDataString(deviceId), null, ct);

    public async Task<(string DeviceId, string DeviceName, string Role)> MeAsync(CancellationToken ct = default)
    {
        var r = await JsonAsync(HttpMethod.Get, "/me", null, ct);
        return (r["deviceId"]?.GetValue<string>() ?? "", r["deviceName"]?.GetValue<string>() ?? "", r["role"]?.GetValue<string>() ?? "");
    }

    /// <summary>Cổng AI: nhà cung cấp nào đã có khóa trên cloud.</summary>
    public async Task<CloudAiStatus> AiStatusAsync(CancellationToken ct = default) => ParseAiStatus(await JsonAsync(HttpMethod.Get, "/ai/status", null, ct));

    public static CloudAiStatus ParseAiStatus(JsonNode r)
    {
        static bool B(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;
        var order = r["order"] is JsonArray a ? a.Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0).ToList() : new List<string>();
        var models = new Dictionary<string, string>();
        if (r["models"] is JsonObject m)
            foreach (var kv in m) if (kv.Value is JsonValue v && v.TryGetValue<string>(out var str)) models[kv.Key] = str;
        var t = r["timeoutSec"] is JsonValue tv && tv.TryGetValue<double>(out var d) ? (int)d : 0;
        return new CloudAiStatus(B(r["groq"]), B(r["gemini"]), B(r["nvidia"]), B(r["cloudflare"]), order, models, t);
    }

    // ---- đồng bộ
    public async Task<CloudSyncPage> SyncAsync(long since, int limit = 500, CancellationToken ct = default)
    {
        var r = await JsonAsync(HttpMethod.Get, $"/sync?since={since}&limit={limit}", null, ct);
        var list = new List<JsonObject>();
        var watch = new List<CloudWatchChange>();
        if (r["changes"] is JsonArray arr)
            foreach (var c in arr)
            {
                var type = c?["type"]?.GetValue<string>();
                if (type == "parcel" && c!["parcel"] is JsonObject p) list.Add((JsonObject)p.DeepClone());
                else if (type == "returnWatch" && c!["key"]?.GetValue<string>() is { Length: > 0 } key)
                    watch.Add(new(key, (long)ParcelMapper.Num(c["since"]), c["off"] is JsonValue o && o.TryGetValue<bool>(out var off) && off));
            }
        return new(r["rev"]?.GetValue<long>() ?? since, r["more"]?.GetValue<bool>() ?? false, list, watch);
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

    /// <summary>Gợi ý AI theo khung giờ (mới nhất trước).</summary>
    public async Task<IReadOnlyList<CloudAiDigest>> AiDigestsAsync(int limit = 12, CancellationToken ct = default) =>
        ParseDigests(await JsonAsync(HttpMethod.Get, "/ai/digests?limit=" + limit, null, ct));

    public static IReadOnlyList<CloudAiDigest> ParseDigests(JsonNode node)
    {
        var list = new List<CloudAiDigest>();
        if (node is not JsonArray arr) return list;
        static long L(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? (long)d : 0;
        foreach (var x in arr.OfType<JsonObject>())
            list.Add(new CloudAiDigest(L(x["id"]), L(x["at"]), x["slot"]?.GetValue<string>() ?? "", x["text"]?.GetValue<string>() ?? "",
                x["byAi"] is JsonValue b && b.TryGetValue<bool>(out var bb) && bb));
        return list;
    }

    /// <summary>Lịch sử đồng bộ: các lượt nhập đơn cloud đã nhận (Google Sheet, nạp từ máy tính), mới nhất trước.</summary>
    public async Task<IReadOnlyList<CloudImportRun>> ImportLogAsync(CancellationToken ct = default) =>
        ParseImportLog(await JsonAsync(HttpMethod.Get, "/import/log", null, ct));

    public static IReadOnlyList<CloudImportRun> ParseImportLog(JsonNode node)
    {
        var list = new List<CloudImportRun>();
        if (node is not JsonArray arr) return list;
        static long L(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? (long)d : 0;
        foreach (var x in arr.OfType<JsonObject>())
            list.Add(new CloudImportRun(x["source"]?.GetValue<string>() ?? "", L(x["start"]), L(x["end"]), (int)L(x["batches"]), (int)L(x["sent"]),
                (int)L(x["added"]), (int)L(x["updated"]), (int)L(x["skipped"]), (int)L(x["ambiguous"])));
        return list;
    }

    // ---- hạn mức, AI
    public async Task<CloudQuotaInfo> QuotaAsync(CancellationToken ct = default)
    {
        var r = await JsonAsync(HttpMethod.Get, "/quota", null, ct);
        long L(string a, string b) => r[a]?[b]?.GetValue<long>() ?? 0;
        return new(r["day"]?.GetValue<string>() ?? "", L("requests", "used"), L("requests", "limit"), L("rowsWritten", "used"), L("rowsWritten", "limit"),
            L("rowsRead", "used"), L("rowsRead", "limit"), r["level"]?.GetValue<string>() ?? "ok");
    }

    // ---- ảnh
    /// <param name="legacy">Chuyển ảnh CŨ (chỉ chủ cửa hàng): không cần vừa quét.</param>
    /// <param name="takenAtMs">Giờ chụp gốc (ms); chỉ dùng với legacy, quá hạn lưu thì cloud từ chối.</param>
    public async Task PutPhotoAsync(string photoId, string parcelId, string mime, bool thumb, byte[] bytes, CancellationToken ct = default, bool legacy = false, long? takenAtMs = null)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(mime);
        var q = $"parcelId={Uri.EscapeDataString(parcelId)}&mime={Uri.EscapeDataString(mime)}" + (thumb ? "&kind=thumb" : "");
        if (legacy) q += "&legacy=1" + (takenAtMs is { } t ? "&takenAt=" + t : "");
        await SendRawAsync(HttpMethod.Put, $"/photos/{Uri.EscapeDataString(photoId)}?{q}", content, null, ct).ConfigureAwait(false);
    }

    /// <summary>Mã các ảnh của một kiện trên cloud (kể cả ảnh điện thoại chụp), mới nhất trước; bỏ ảnh đã hết hạn lưu.</summary>
    public async Task<IReadOnlyList<string>> ParcelPhotoIdsAsync(string parcelId, CancellationToken ct = default)
    {
        if (await JsonAsync(HttpMethod.Get, $"/parcels/{Uri.EscapeDataString(parcelId)}/photos", null, ct) is not JsonArray arr) return Array.Empty<string>();
        static long L(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? (long)d : 0;
        return arr.OfType<JsonObject>()
            .Where(x => !(x["expired"] is JsonValue e && e.TryGetValue<bool>(out var b) && b))
            .Select(x => (Id: x["photoId"]?.GetValue<string>() ?? "", At: L(x["takenAt"])))
            .Where(x => x.Id.Length > 0).OrderByDescending(x => x.At).Select(x => x.Id).ToList();
    }

    public async Task<byte[]> GetPhotoAsync(string photoId, bool thumb, CancellationToken ct = default)
    {
        var (_, body, _) = await SendRawAsync(HttpMethod.Get, $"/photos/{Uri.EscapeDataString(photoId)}" + (thumb ? "?size=thumb" : ""), null, null, ct).ConfigureAwait(false);
        return body;
    }

    // ---- nhập đơn
    /// <summary>Một lô kiện đã chuyển sẵn trên máy (không có dữ liệu cá nhân) để cloud gộp; <paramref name="orderIds"/>: mã đơn để thay dòng chờ cũ (Salework).</summary>
    public async Task<CloudMappedResult> ImportMappedAsync(string source, string kind, string mode, IEnumerable<JsonObject> parcels, IEnumerable<string> orderIds,
        string signature, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["source"] = source, ["kind"] = kind, ["mode"] = mode, ["signature"] = signature,
            ["parcels"] = new JsonArray(parcels.Select(p => (JsonNode)p.DeepClone()).ToArray()),
            ["orderIds"] = new JsonArray(orderIds.Select(i => (JsonNode?)i).ToArray()),
        };
        var r = await JsonAsync(HttpMethod.Post, "/import/mapped", body, ct);
        static int I(JsonNode? n) => n is JsonValue v && v.TryGetValue<int>(out var x) ? x : 0;
        static string S(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var x) ? x : "";
        var amb = new List<QuanLyHangHoan.Core.Import.AmbiguousParcel>();
        if (r["ambiguous"] is JsonArray a)
            foreach (var x in a)
                amb.Add(new(S(x?["platform"]), S(x?["orderId"]), S(x?["track"]), S(x?["returnOrderId"]), I(x?["candidates"]),
                    x?["orderConflict"] is JsonValue oc && oc.TryGetValue<bool>(out var b) && b));
        return new(I(r["added"]), I(r["updated"]), I(r["enrichedArchived"]), I(r["skippedDeleted"]), I(r["superseded"]), amb);
    }

    public async Task<CloudImportResult> ImportAsync(string source, string signature, IEnumerable<JsonObject> parcels, IEnumerable<JsonObject>? returnWatch = null, CancellationToken ct = default)
    {
        var arr = new JsonArray();
        foreach (var p in parcels) arr.Add(p);
        var body = new JsonObject { ["source"] = source, ["signature"] = signature, ["parcels"] = arr };
        if (returnWatch is not null)
        {
            var w = new JsonArray();
            foreach (var x in returnWatch) w.Add(x);
            body["returnWatch"] = w;
        }
        var r = await JsonAsync(HttpMethod.Post, "/import", body, ct);
        var rejected = new List<(string Id, string Reason)>();
        if (r["rejected"] is JsonArray rj)
            foreach (var x in rj) rejected.Add((x?["id"]?.GetValue<string>() ?? "", x?["reason"]?.GetValue<string>() ?? ""));
        return new(r["added"]?.GetValue<int>() ?? 0, r["updated"]?.GetValue<int>() ?? 0, r["unchanged"]?.GetValue<int>() ?? 0, rejected, r["returnWatchAdded"]?.GetValue<int>() ?? 0);
    }
}

public sealed record CloudImportResult(int Added, int Updated, int Unchanged, IReadOnlyList<(string Id, string Reason)> Rejected, int ReturnWatchAdded);
/// <summary>Kết quả một lô <c>/import/mapped</c> (cloud gộp kiện đã chuyển sẵn).</summary>
public sealed record CloudMappedResult(int Added, int Updated, int EnrichedArchived, int SkippedDeleted, int Superseded, IReadOnlyList<QuanLyHangHoan.Core.Import.AmbiguousParcel> Ambiguous);
/// <summary>Một lượt đồng bộ cloud đã nhận (các lô cùng nguồn gửi liền nhau gộp lại). Thời gian: mili giây Unix.</summary>
public sealed record CloudImportRun(string Source, long Start, long End, int Batches, int Sent, int Added, int Updated, int Skipped, int Ambiguous);

/// <summary>Một thiết bị đã ghép với cửa hàng. LastSeenAt: mili giây Unix (0 = chưa thấy).</summary>
public sealed record CloudDeviceInfo(string DeviceId, string DeviceName, string Kind, string Role, long LastSeenAt, bool Revoked);

/// <summary>Một bản gợi ý AI theo khung giờ. At: mili giây Unix; ByAi=false: bản viết theo quy tắc (AI lỗi/hết lượt).</summary>
public sealed record CloudAiDigest(long Id, long At, string Slot, string Text, bool ByAi);

/// <summary>Trạng thái + cấu hình AI chung trên cloud (thứ tự dự phòng, mô hình, thời gian chờ; cloud cũ không có thì rỗng/0).</summary>
public sealed record CloudAiStatus(bool Groq, bool Gemini, bool Nvidia, bool Cloudflare,
    IReadOnlyList<string> Order, IReadOnlyDictionary<string, string> Models, int TimeoutSec);
