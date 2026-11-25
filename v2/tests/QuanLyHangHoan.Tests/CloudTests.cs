using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Cloud;
using QuanLyHangHoan.Data.Sheet;
using Xunit.Abstractions;

namespace QuanLyHangHoan.Tests;

/// <summary>Máy khách cloud phía Windows: ánh xạ kiện, bản sao SQLite, API, phiên ghép, quét/sửa khi mất mạng. Dữ liệu GIẢ.</summary>
public sealed class CloudTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-cloud-" + Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_dir, "t.db");

    public CloudTests() { Directory.CreateDirectory(_dir); AppDatabase.Initialize(Db); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class FakeProtector : ISecretProtector
    {
        public string Protect(string plain) => "P:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
        public string? Unprotect(string t) => t.StartsWith("P:") ? Encoding.UTF8.GetString(Convert.FromBase64String(t[2..])) : null;
    }

    private sealed class Handler(Func<HttpRequestMessage, (HttpStatusCode, string)> reply) : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string Body)> Calls = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls.Add((r.Method.Method, r.RequestUri!.ToString(), r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct)));
            var (code, body) = reply(r);
            return new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static JsonObject Parcel(string id, string status = "Chưa trả", string track = "") => new()
    {
        ["id"] = id, ["orderId"] = "O-" + id, ["track"] = track.Length > 0 ? track : "TRK-" + id, ["platform"] = "Shopee", ["orderType"] = "Trả hàng",
        ["status"] = status, ["price"] = 125000, ["userNote"] = "ghi chú", ["isWatchlist"] = true, ["watchlistReason"] = "lý do",
        ["trackingCodes"] = new JsonArray("OLD-1"), ["photoId"] = "ph1",
        ["items"] = new JsonArray(new JsonObject { ["product"] = "Áo", ["color"] = "Đen", ["size"] = "M", ["qty"] = 2, ["returnedQty"] = 1 }),
        ["extra"] = new JsonObject { ["returnSuccessTime"] = "24/11/2026 10:00", ["returnReason"] = "Sai mô tả", ["forwardCarrier"] = "SPX" },
        ["isArchived"] = false, ["isDeleted"] = false,
    };

    // ------------------------------------------------------------ ánh xạ
    [Fact]
    public void Parcel_maps_to_order_and_back_without_forbidden_data()
    {
        var s = ParcelMapper.ToStored(Parcel("a"));
        Assert.Equal("O-a", s.Order.OrderId);
        Assert.Equal(125000m, s.Order.Price);
        Assert.Equal(new[] { "OLD-1" }, s.Order.TrackingCodes);
        Assert.Equal(1, s.Order.Items[0].ReturnedQty);
        Assert.Equal("24/11/2026 10:00", s.Order.Import.RawDetails["returnSuccessTime"]);
        Assert.Equal("SPX", s.Order.Import.ForwardCarrier);

        s.Order.BuyerNote = "tên khách 0912345678";
        var back = ParcelMapper.ToParcel(s);
        var text = back.ToJsonString();
        Assert.DoesNotContain("buyerNote", text);
        Assert.DoesNotContain("0912345678", text);
        Assert.DoesNotContain("rawDetails", text);
        Assert.Equal("Sai mô tả", back["extra"]!["returnReason"]!.GetValue<string>());
    }

    [Fact]
    public void Extra_merge_updates_whitelisted_keys_and_keeps_every_other_legacy_key()
    {
        var existing = """{"rawDetails":{"deliveryAddress":"giữ nguyên","returnStatus":"cũ"},"priceModes":{"unit":5},"photoHistory":[1,2]}""";
        var merged = JsonNode.Parse(ParcelMapper.ExtraFrom(new JsonObject { ["returnStatus"] = "mới", ["forwardCarrier"] = "GHN" }, existing))!;
        Assert.Equal("mới", merged["rawDetails"]!["returnStatus"]!.GetValue<string>());
        Assert.Equal("giữ nguyên", merged["rawDetails"]!["deliveryAddress"]!.GetValue<string>());
        Assert.Equal(5, merged["priceModes"]!["unit"]!.GetValue<int>());
        Assert.Equal(2, merged["photoHistory"]!.AsArray().Count);
        Assert.Equal("GHN", merged["forwardCarrier"]!.GetValue<string>());
    }

    // ------------------------------------------------------------ bản sao SQLite
    [Fact]
    public void Replica_inserts_updates_and_keeps_local_only_fields_and_never_deletes()
    {
        Assert.Equal(1, CloudReplica.Apply(Db, new[] { Parcel("a"), }, 5));
        Assert.Equal(5, CloudReplica.GetRev(Db));
        var o = OrderRepository.Load(Db).Single();
        Assert.Equal("a", o.Order.Id);
        Assert.Equal("Chưa trả", o.Order.Status);
        Assert.Equal("Áo", o.Order.Items.Single().Product);

        // cột chỉ có ở máy (ghi chú người mua, dữ liệu thô) được giữ khi cloud cập nhật
        using (var c = new SqliteConnection($"Data Source={Db};Pooling=False"))
        {
            c.Open();
            using var u = c.CreateCommand();
            u.CommandText = "UPDATE orders SET buyer_note='ghi chú khách', raw_json='RAW', extra_json='{\"photoHistory\":[1],\"rawDetails\":{\"deliveryAddress\":\"x\"}}' WHERE id='a'";
            u.ExecuteNonQuery();
        }
        var p2 = Parcel("a", "Hàng đã trả"); p2["items"] = new JsonArray();
        CloudReplica.Apply(Db, new[] { p2, Parcel("b") }, 9);
        var all = OrderRepository.Load(Db);
        Assert.Equal(2, all.Count);
        var a = all.Single(x => x.Order.Id == "a").Order;
        Assert.Equal("Hàng đã trả", a.Status);
        Assert.Empty(a.Items);
        Assert.Equal("ghi chú khách", a.BuyerNote);
        Assert.Contains("photoHistory", a.ExtraJson);
        Assert.Contains("deliveryAddress", a.ExtraJson);
        Assert.Contains("returnSuccessTime", a.ExtraJson);
        Assert.Equal(9, CloudReplica.GetRev(Db));

        // cờ lưu trữ / xóa mềm đi theo cloud, dòng không bị xóa
        var arch = Parcel("b"); arch["isArchived"] = true; arch["archivedAt"] = "25/11/2026 10:00:00";
        CloudReplica.Apply(Db, new[] { arch }, null);
        Assert.True(OrderRepository.Load(Db).Single(x => x.Order.Id == "b").IsArchived);
        Assert.Equal(9, CloudReplica.GetRev(Db));   // áp kiện lẻ không đổi mốc đồng bộ

        CloudReplica.ResetRev(Db);
        Assert.Equal(0, CloudReplica.GetRev(Db));
        Assert.Equal(2, OrderRepository.Load(Db).Count);
    }

    // ------------------------------------------------------------ API
    [Fact]
    public async Task Api_maps_http_errors_and_network_failures_to_vietnamese_messages_without_leaking_the_token()
    {
        var h = new Handler(_ => (HttpStatusCode.Unauthorized, """{"error":{"code":"unauthorized","message":"Thiết bị đã bị thu hồi."}}"""));
        var api = new CloudApi(new HttpClient(h), "https://x.workers.dev/", "SECRET-TOKEN");
        var ex = await Assert.ThrowsAsync<CloudApiException>(() => api.MeAsync());
        Assert.True(ex.IsUnauthorized);
        Assert.Equal("Thiết bị đã bị thu hồi.", ex.Message);
        Assert.DoesNotContain("SECRET-TOKEN", ex.Message);
        Assert.Equal("https://x.workers.dev/v1/me", h.Calls[0].Url);

        var net = new CloudApi(new HttpClient(new Handler(_ => throw new HttpRequestException("dns"))), "https://x.workers.dev", "t");
        var n = await Assert.ThrowsAsync<CloudApiException>(() => net.SyncAsync(0));
        Assert.True(n.IsNetwork);
        Assert.Contains("kết nối", n.Message);

        var q = new CloudApi(new HttpClient(new Handler(_ => (HttpStatusCode.TooManyRequests, "{}"))), "https://x.workers.dev", "t");
        Assert.True((await Assert.ThrowsAsync<CloudApiException>(() => q.SyncAsync(0))).IsQuota);
    }

    [Theory]
    [InlineData("https://a.workers.dev", "https://a.workers.dev/v1")]
    [InlineData("https://a.workers.dev/", "https://a.workers.dev/v1")]
    [InlineData("https://a.workers.dev/v1", "https://a.workers.dev/v1")]
    [InlineData(" http://127.0.0.1:8799 ", "http://127.0.0.1:8799/v1")]
    public void Base_url_is_normalised(string input, string expected) => Assert.Equal(expected, CloudApi.NormalizeBaseUrl(input));

    [Theory]
    [InlineData("")]
    [InlineData("http://example.com")]
    [InlineData("ftp://a.b")]
    [InlineData("khong-phai-url")]
    public void Insecure_or_bad_base_url_is_rejected(string input) => Assert.Throws<CloudApiException>(() => CloudApi.NormalizeBaseUrl(input));

    [Fact]
    public async Task Sync_ops_and_quota_are_parsed()
    {
        var h = new Handler(r => r.RequestUri!.AbsolutePath switch
        {
            "/v1/sync" => (HttpStatusCode.OK, """{"rev":7,"more":true,"changes":[{"type":"parcel","rev":7,"parcel":{"id":"a"}},{"type":"returnWatch","rev":6}]}"""),
            "/v1/ops" => (HttpStatusCode.OK, """{"results":[{"opId":"x","ok":false,"error":{"code":"conflict","message":"Xung đột"}}]}"""),
            _ => (HttpStatusCode.OK, """{"day":"2026-11-25","requests":{"used":10,"limit":100000},"rowsWritten":{"used":2,"limit":100000},"rowsRead":{"used":3,"limit":5000000},"level":"warn"}"""),
        });
        var api = new CloudApi(new HttpClient(h), "https://x.workers.dev", "t");
        var page = await api.SyncAsync(3, 500);
        Assert.Equal(7, page.Rev); Assert.True(page.More);
        Assert.Single(page.Parcels);
        Assert.Contains("since=3", h.Calls[0].Url);
        var res = await api.OpAsync(new JsonObject { ["type"] = "setStatus", ["id"] = "a" });
        Assert.False(res.Ok);
        Assert.Equal("conflict", res.ErrorCode);
        Assert.Matches("\"opId\":\"[0-9a-f-]{36}\"", h.Calls[1].Body);   // tự sinh opId
        var q = await api.QuotaAsync();
        Assert.Equal("warn", q.Level); Assert.Equal(100000, q.RequestsLimit);
    }

    // ------------------------------------------------------------ phiên ghép
    [Fact]
    public void Session_is_saved_atomically_with_the_token_protected_and_survives_corruption()
    {
        var path = Path.Combine(_dir, "cloud.json");
        var store = new CloudSessionStore(path, new FakeProtector());
        Assert.Null(store.Load());
        store.Save(new CloudSession("https://a.workers.dev", "TOKEN-123", "dev1", "Máy 1", "staff"));
        Assert.DoesNotContain("TOKEN-123", File.ReadAllText(path));
        var s = store.Load()!;
        Assert.Equal("TOKEN-123", s.Token);
        Assert.Equal("Máy 1", s.DeviceName);
        File.WriteAllText(path, "{hỏng");
        Assert.Null(store.Load());
        store.Save(s); store.Clear();
        Assert.False(File.Exists(path));
    }

    // ------------------------------------------------------------ không mạng = khóa ghi
    [Fact]
    public async Task Without_a_connection_scan_and_edit_are_refused_and_never_acked()
    {
        var link = new CloudLinkService(new HttpClient(new Handler(_ => throw new InvalidOperationException("không được gọi mạng"))), Db,
            new CloudSessionStore(Path.Combine(_dir, "c.json"), new FakeProtector()));
        Assert.Equal(CloudState.Unpaired, link.State);
        Assert.False(link.CanWrite);

        var scan = await new CloudScanService(link).ReceiveAsync("TRK", DateTime.Now);
        Assert.Equal(ScanOutcome.Error, scan.Outcome);
        Assert.False(scan.Ack);
        Assert.Contains("Mất kết nối", scan.Message);

        var edit = new CloudOrderEditService(link);
        Assert.Equal(EditKind.Error, (await edit.SetFieldAsync("a", EditField.Note, "x")).Kind);
        Assert.Equal(EditKind.Error, (await edit.DeleteAsync("a", "Bảng chính")).Kind);
        Assert.Equal(EditKind.Error, (await edit.ArchiveAsync("a")).Kind);
        Assert.Equal(EditKind.Invalid, (await edit.SetFieldAsync("a", EditField.BuyerNote, "x")).Kind);
        Assert.Empty(await edit.GetHistoryAsync("a"));
    }

    [Fact]
    public async Task Pairing_validates_input_before_any_network_call()
    {
        var h = new Handler(_ => (HttpStatusCode.OK, "{}"));
        var link = new CloudLinkService(new HttpClient(h), Db, new CloudSessionStore(Path.Combine(_dir, "c.json"), new FakeProtector()));
        Assert.Contains("6 chữ số", await link.PairAsync("https://a.workers.dev", "12ab", "Máy"));
        Assert.Contains("tên", (await link.PairAsync("https://a.workers.dev", "123456", "  "))!);
        Assert.Contains("không hợp lệ", (await link.PairAsync("http://khong-an-toan.com", "123456", "Máy"))!);
        Assert.Empty(h.Calls);
        var start = await link.StartPairingAsync();
        Assert.NotNull(start.Error);
    }
}

/// <summary>
/// Kiểm thử NGUYÊN CHUỖI với Worker thật chạy cục bộ (wrangler dev): ghép thiết bị, WebSocket, đồng bộ vào SQLite, quét qua cloud.
/// Chỉ chạy khi đặt QLHH_CLOUD_TEST_URL (vd http://127.0.0.1:8799), QLHH_CLOUD_TEST_BOOTSTRAP, QLHH_CLOUD_TEST_IMPORT; bỏ qua nếu thiếu.
/// </summary>
public sealed class CloudLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-live-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class P : ISecretProtector
    {
        public string Protect(string plain) => "P:" + plain;
        public string? Unprotect(string t) => t.StartsWith("P:") ? t[2..] : null;
    }

    private static async Task<bool> WaitAsync(Func<bool> cond, int ms = 15000)
    {
        for (var i = 0; i < ms / 50; i++) { if (cond()) return true; await Task.Delay(50); }
        return cond();
    }

    [Fact]
    public async Task Full_flow_against_a_real_worker()
    {
        var url = Environment.GetEnvironmentVariable("QLHH_CLOUD_TEST_URL");
        var boot = Environment.GetEnvironmentVariable("QLHH_CLOUD_TEST_BOOTSTRAP");
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(boot)) { output.WriteLine("BỎ QUA: chưa đặt QLHH_CLOUD_TEST_URL/BOOTSTRAP"); return; }

        Directory.CreateDirectory(_dir);
        var db = Path.Combine(_dir, "t.db");
        AppDatabase.Initialize(db);
        using var http = new HttpClient();

        // máy chủ cửa hàng (chủ) được tạo bằng khóa khởi tạo, rồi ghép một máy tính thứ hai bằng mã 6 số
        var anon = new CloudApi(http, url, null);
        var owner = await http.PostAsync(CloudApi.NormalizeBaseUrl(url) + "/pair/bootstrap",
            new StringContent(new JsonObject { ["bootstrapKey"] = boot, ["deviceName"] = "Chủ (thử)", ["kind"] = "pc" }.ToJsonString(), Encoding.UTF8, "application/json"));
        string ownerToken;
        if (owner.IsSuccessStatusCode) ownerToken = JsonNode.Parse(await owner.Content.ReadAsStringAsync())!["token"]!.GetValue<string>();
        else { output.WriteLine("Máy chủ đã có chủ từ trước: dùng QLHH_CLOUD_TEST_OWNER"); ownerToken = Environment.GetEnvironmentVariable("QLHH_CLOUD_TEST_OWNER") ?? ""; }
        Assert.NotEmpty(ownerToken);
        var ownerApi = new CloudApi(http, url, ownerToken);
        var code = (await ownerApi.PairStartAsync()).Code;

        var store = new CloudSessionStore(Path.Combine(_dir, "cloud.json"), new P());
        using var link = new CloudLinkService(http, db, store);
        var changes = 0;
        link.DataChanged += () => Interlocked.Increment(ref changes);
        Assert.Null(await link.PairAsync(url, code, "Máy tính thử"));
        Assert.True(await WaitAsync(() => link.State == CloudState.Connected), "không nối được cloud: " + link.State + " " + link.LastError);
        Assert.True(link.CanWrite);
        Assert.Equal("staff", link.Role);
        Assert.NotNull(store.Load());

        // chủ nhập 2 kiện (như Apps Script/nạp file) -> máy tính nhận qua WebSocket và áp vào SQLite
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var a = Parcel("live-a-" + suffix); var b = Parcel("live-b-" + suffix);
        var res = await ownerApi.ImportAsync("file", "sig", new[] { a, b });
        Assert.Equal(2, res.Added);
        Assert.True(await WaitAsync(() => OrderRepository.Load(db).Count == 2), "bản sao không nhận được kiện qua WebSocket");
        Assert.True(changes > 0);

        // quét qua cloud: ACK, trạng thái cập nhật ngay trong SQLite; quét lại -> đã nhận
        var scan = new CloudScanService(link);
        var r1 = await scan.ReceiveAsync("trk-live-a-" + suffix, DateTime.Now);
        Assert.Equal(ScanOutcome.Received, r1.Outcome);
        Assert.True(r1.Ack);
        Assert.Equal("Hàng đã trả", OrderRepository.Load(db).Single(x => x.Order.Id == "live-a-" + suffix).Order.Status);
        var r2 = await scan.ReceiveAsync("TRK-live-a-" + suffix, DateTime.Now);
        Assert.Equal(ScanOutcome.AlreadyReceived, r2.Outcome);
        Assert.Contains("Máy tính thử", r2.Message);
        Assert.Equal(ScanOutcome.NotFound, (await scan.ReceiveAsync("KHONG-CO-" + suffix, DateTime.Now)).Outcome);

        // sửa qua cloud + lịch sử + hạn mức
        var edit = new CloudOrderEditService(link);
        Assert.True((await edit.SetFieldAsync("live-b-" + suffix, EditField.UserNote, "đã sửa qua cloud")).Ok);
        Assert.Equal("đã sửa qua cloud", OrderRepository.Load(db).Single(x => x.Order.Id == "live-b-" + suffix).Order.UserNote);
        Assert.NotEmpty(await edit.GetHistoryAsync("live-b-" + suffix));
        var conflict = await edit.SetFieldAsync("live-b-" + suffix, EditField.Status, "Bị mất", expectedOld: "Hàng đã trả");
        Assert.Equal(EditKind.Conflict, conflict.Kind);
        Assert.True(await WaitAsync(() => link.Quota is not null));
        Assert.True(link.Quota!.RequestsUsed > 0);

        // thu hồi thiết bị -> máy bị đẩy ra, trạng thái "đã thu hồi", khóa ghi
        var devices = await new CloudApi(http, url, ownerToken).MeAsync();
        var list = JsonNode.Parse(await http.SendAsync(new HttpRequestMessage(HttpMethod.Get, CloudApi.NormalizeBaseUrl(url) + "/devices") { Headers = { { "Authorization", "Bearer " + ownerToken } } }).ContinueWith(t => t.Result.Content.ReadAsStringAsync()).Unwrap())!.AsArray();
        var mine = list.First(d => d!["deviceName"]!.GetValue<string>() == "Máy tính thử")!["deviceId"]!.GetValue<string>();
        var del = new HttpRequestMessage(HttpMethod.Delete, CloudApi.NormalizeBaseUrl(url) + "/devices/" + mine);
        del.Headers.Add("Authorization", "Bearer " + ownerToken);
        Assert.True((await http.SendAsync(del)).IsSuccessStatusCode);
        Assert.True(await WaitAsync(() => link.State is CloudState.Revoked or CloudState.Disconnected, 40000), "trạng thái sau thu hồi: " + link.State);
        Assert.False(link.CanWrite);
        var refused = await scan.ReceiveAsync("TRK-live-b-" + suffix, DateTime.Now);
        Assert.False(refused.Ack);
        Assert.Equal("owner", devices.Role);
    }

    private static JsonObject Parcel(string id) => new()
    {
        ["id"] = id, ["orderId"] = "O-" + id, ["track"] = "TRK-" + id, ["platform"] = "Shopee", ["orderType"] = "Trả hàng", ["status"] = "Chưa trả", ["price"] = 1000,
        ["items"] = new JsonArray(new JsonObject { ["product"] = "Áo", ["color"] = "Đen", ["size"] = "M", ["qty"] = 1 }),
    };
}
