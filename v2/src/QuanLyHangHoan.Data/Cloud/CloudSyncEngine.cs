using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace QuanLyHangHoan.Data.Cloud;

public enum CloudConnState { Unpaired, Connecting, Syncing, Connected, Disconnected, Revoked }

/// <summary>
/// Giữ bản sao cục bộ đồng bộ với cloud: nối WebSocket, nhận thông báo thay đổi (`rev`), lấy phần mới (`/sync`) rồi áp vào SQLite.
/// Mất WebSocket (hoặc im lặng quá <see cref="SilenceTimeout"/>) = MẤT KẾT NỐI: giao diện khóa ghi cho tới khi nối lại và bắt kịp.
/// Không có hàng đợi ngoại tuyến (quyết định của chủ dự án).
/// </summary>
public sealed class CloudSyncEngine : IDisposable
{
    private readonly CloudApi _api;
    private readonly string _dbPath;
    private readonly SemaphoreSlim _pullLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private Task? _run;

    public CloudSyncEngine(CloudApi api, string dbPath) { _api = api; _dbPath = dbPath; }

    public CloudConnState State { get; private set; } = CloudConnState.Connecting;
    public string? LastError { get; private set; }
    public DateTime? LastSyncAt { get; private set; }
    public CloudQuotaInfo? Quota { get; private set; }
    public TimeSpan PingEvery { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan SilenceTimeout { get; set; } = TimeSpan.FromSeconds(25);
    public TimeSpan QuotaEvery { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Phát khi trạng thái/hạn mức/lỗi đổi (có thể từ luồng nền).</summary>
    public event Action? Changed;
    /// <summary>Phát sau khi áp thay đổi mới từ cloud vào SQLite: giao diện nên nạp lại dữ liệu (có thể từ luồng nền).</summary>
    public event Action? DataChanged;

    private void Set(CloudConnState s, string? error = null)
    {
        State = s;
        LastError = error;
        Changed?.Invoke();
    }

    public void Start() => _run ??= Task.Run(() => RunAsync(_cts.Token));

    public void Dispose()
    {
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        try { _run?.Wait(TimeSpan.FromSeconds(3)); } catch (Exception) { /* đang tắt */ }
        _cts.Dispose();
    }

    /// <summary>Lấy toàn bộ phần thay đổi từ mốc đã lưu cho tới khi bắt kịp. An toàn khi gọi đồng thời (tuần tự hóa).</summary>
    public async Task<int> PullAsync(CancellationToken ct = default)
    {
        await _pullLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var total = 0;
            while (true)
            {
                var since = CloudReplica.GetRev(_dbPath);
                var page = await _api.SyncAsync(since, 500, ct).ConfigureAwait(false);
                if (page.Parcels.Count > 0 || (page.Watch?.Count ?? 0) > 0 || page.Rev != since)
                    total += CloudReplica.Apply(_dbPath, page.Parcels, page.Rev, page.Watch);
                if (!page.More) break;
            }
            LastSyncAt = DateTime.Now;
            if (total > 0) DataChanged?.Invoke();
            Changed?.Invoke();
            return total;
        }
        finally { _pullLock.Release(); }
    }

    /// <summary>Áp ngay kiện trả về từ một thao tác ghi (không đổi mốc đồng bộ; lần `/sync` kế tiếp áp lại cũng không sao).</summary>
    public void ApplyLocal(JsonObject parcel)
    {
        CloudReplica.Apply(_dbPath, new[] { parcel }, null);
        DataChanged?.Invoke();
    }

    /// <summary>Áp ngay thay đổi Bảng Hàng Hoàn Về vừa gửi lên cloud thành công.</summary>
    public void ApplyWatchLocal(string orderId, long since, bool off)
    {
        CloudReplica.Apply(_dbPath, Array.Empty<JsonObject>(), null, new[] { new CloudWatchChange(orderId, since, off) });
        DataChanged?.Invoke();
    }

    public async Task RefreshQuotaAsync(CancellationToken ct = default)
    {
        try { Quota = await _api.QuotaAsync(ct).ConfigureAwait(false); Changed?.Invoke(); }
        catch (CloudApiException) { /* hạn mức chỉ để hiển thị */ }
    }

    // ===================================================== vòng chạy chính
    private async Task RunAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(2);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Set(CloudConnState.Connecting);
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(_api.WebSocketUri(), ct).ConfigureAwait(false);
                Set(CloudConnState.Syncing);
                await PullAsync(ct).ConfigureAwait(false);
                _ = RefreshQuotaAsync(ct);
                Set(CloudConnState.Connected);
                backoff = TimeSpan.FromSeconds(2);
                await ReceiveLoopAsync(ws, ct).ConfigureAwait(false);
                if (!ct.IsCancellationRequested) Set(CloudConnState.Disconnected, "Mất kết nối cloud. Đang thử nối lại...");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (CloudApiException ex) when (ex.IsUnauthorized) { Set(CloudConnState.Revoked, ex.Message); return; }
            catch (Exception ex)
            {
                // WebSocket báo lỗi chung chung khi token sai: hỏi /me để phân biệt "bị thu hồi" với "mất mạng".
                var revoked = await IsRevokedAsync(ct).ConfigureAwait(false);
                if (revoked) { Set(CloudConnState.Revoked, "Thiết bị đã bị thu hồi khỏi cloud."); return; }
                Set(CloudConnState.Disconnected, ex is CloudApiException ce ? ce.Message : "Mất kết nối cloud. Đang thử nối lại...");
            }
            try { await Task.Delay(backoff, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
            backoff = TimeSpan.FromSeconds(Math.Min(30, backoff.TotalSeconds * 2));
        }
    }

    private async Task<bool> IsRevokedAsync(CancellationToken ct)
    {
        try { await _api.MeAsync(ct).ConfigureAwait(false); return false; }
        catch (CloudApiException ex) { return ex.IsUnauthorized; }
        catch (OperationCanceledException) { return false; }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        using var link = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var lastSeen = DateTime.UtcNow;
        var buf = new byte[4096];
        var ping = Task.Run(async () =>
        {
            var lastQuota = DateTime.UtcNow;
            var ping = Encoding.UTF8.GetBytes("{\"t\":\"ping\"}");
            while (!link.IsCancellationRequested)
            {
                await Task.Delay(PingEvery, link.Token).ConfigureAwait(false);
                if (DateTime.UtcNow - lastSeen > SilenceTimeout) { link.Cancel(); return; }
                await ws.SendAsync(ping, WebSocketMessageType.Text, true, link.Token).ConfigureAwait(false);
                if (DateTime.UtcNow - lastQuota > QuotaEvery) { lastQuota = DateTime.UtcNow; await RefreshQuotaAsync(link.Token).ConfigureAwait(false); }
            }
        }, link.Token);

        try
        {
            while (ws.State == WebSocketState.Open && !link.IsCancellationRequested)
            {
                var sb = new StringBuilder();
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(buf, link.Token).ConfigureAwait(false);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                } while (!r.EndOfMessage);

                lastSeen = DateTime.UtcNow;
                JsonNode? msg;
                try { msg = JsonNode.Parse(sb.ToString()); } catch (System.Text.Json.JsonException) { continue; }
                var t = msg?["t"]?.GetValue<string>();
                if (t == "rev" && (msg!["rev"]?.GetValue<long>() ?? 0) > CloudReplica.GetRev(_dbPath))
                {
                    Set(CloudConnState.Syncing);
                    await PullAsync(link.Token).ConfigureAwait(false);
                    Set(CloudConnState.Connected);
                }
                else if (t == "quota") _ = RefreshQuotaAsync(link.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* im lặng quá lâu: coi là mất kết nối */ }
        finally
        {
            link.Cancel();
            try { await ping.ConfigureAwait(false); } catch (Exception) { /* đã dừng */ }
            try { if (ws.State == WebSocketState.Open) await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false); } catch (Exception) { }
        }
    }
}
