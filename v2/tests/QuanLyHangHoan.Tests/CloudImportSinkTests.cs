using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Cloud;

namespace QuanLyHangHoan.Tests;

/// <summary>
/// Chủ dự án 26/11/2026: máy tính nạp file / thư mục / Salework theo CÙNG đường với điện thoại: chuyển đơn trên máy rồi gửi lên cloud gộp
/// (/import/mapped), không gộp vào máy nữa -> hai máy nạp cùng lúc không tạo đơn trùng. Dữ liệu GIẢ.
/// </summary>
public sealed class CloudImportSinkTests
{
    private sealed class Handler(Func<HttpRequestMessage, string, (HttpStatusCode, string)> reply) : HttpMessageHandler
    {
        public readonly List<JsonObject> Bodies = new();
        public readonly List<string> Urls = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct);
            Urls.Add(r.RequestUri!.ToString());
            if (body.Length > 0) Bodies.Add((JsonObject)JsonNode.Parse(body)!);
            var (code, text) = reply(r, body);
            return new HttpResponseMessage(code) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    private static readonly DateTime Today = new(2026, 11, 26);

    private static Dictionary<string, string> Row(int i) => new()
    {
        ["Order ID"] = "O" + i, ["Return Order ID"] = "C" + i, ["Return Logistics Tracking ID"] = "RT" + i, ["Product Name"] = "Shirt",
        ["Return Status"] = "In Process", ["Quantity"] = "1",
    };

    private static List<IReadOnlyList<Dictionary<string, string>>> Groups(int n) => new() { Enumerable.Range(0, n).Select(Row).ToList() };

    private static (CloudImportSink Sink, Handler H) Make(bool online = true, int? failAt = null)
    {
        var h = new Handler((_, body) =>
        {
            var o = JsonNode.Parse(body)!.AsObject();
            var n = o["parcels"]!.AsArray().Count;
            return (HttpStatusCode.OK, $"{{\"added\":{n},\"updated\":0,\"superseded\":{(n == 0 ? 1 : 0)},\"ambiguous\":[],\"written\":{n}}}");
        });
        var calls = 0;
        var h2 = failAt is null ? h : new Handler((r, body) => ++calls == failAt ? (HttpStatusCode.ServiceUnavailable, "{\"error\":{\"code\":\"unavailable\",\"message\":\"Cloud tạm lỗi\"}}") : (HttpStatusCode.OK,
            $"{{\"added\":{JsonNode.Parse(body)!["parcels"]!.AsArray().Count},\"updated\":0,\"ambiguous\":[]}}"));
        var api = new CloudApi(new HttpClient(h2), "https://x.workers.dev", "t");
        return (new CloudImportSink(() => api, () => online), h2);
    }

    [Fact]
    public void Tiktok_file_rows_are_mapped_on_the_pc_and_sent_in_batches_of_100()
    {
        var (sink, h) = Make();
        var r = sink.Apply(Groups(230), new TikTokMapOptions { Today = Today }, ImportProductMode.Supplement, ImportSource.TikTok, "file");
        Assert.True(r.Import.Ok, r.Import.Message);
        Assert.Equal(230, r.Import.Added);
        Assert.Equal(new[] { 100, 100, 30 }, h.Bodies.Select(b => b["parcels"]!.AsArray().Count));
        Assert.All(h.Bodies, b => { Assert.Equal("file", (string?)b["source"]); Assert.Equal("tiktok", (string?)b["kind"]); Assert.Equal("supplement", (string?)b["mode"]); });
        Assert.All(h.Urls, u => Assert.EndsWith("/v1/import/mapped", u));
        var p = h.Bodies[0]["parcels"]![0]!.AsObject();
        Assert.Equal("O0", (string?)p["orderId"]);
        Assert.Equal("", (string?)p["buyerNote"]);      // không gửi ghi chú người mua lên cloud
    }

    [Fact]
    public void Shopee_files_use_the_shopee_branch_and_replace_mode_is_passed()
    {
        var (sink, h) = Make();
        var rows = new List<IReadOnlyList<Dictionary<string, string>>>
        {
            new List<Dictionary<string, string>> { new() { ["Mã đơn hàng"] = "SP1", ["Mã vận đơn"] = "SPXVN000000001", ["Trạng thái trả hàng"] = "Đang xử lý", ["Tên sản phẩm"] = "Áo", ["Số lượng"] = "1" } },
        };
        var r = sink.Apply(rows, new TikTokMapOptions { Today = Today, OnlyReturns = false }, ImportProductMode.Replace, ImportSource.Shopee, "file");
        Assert.True(r.Import.Ok, r.Import.Message);
        Assert.All(h.Bodies, b => { Assert.Equal("shopee", (string?)b["kind"]); Assert.Equal("replace", (string?)b["mode"]); });
    }

    [Fact]
    public void Salework_sends_order_ids_last_for_replacing_old_waiting_rows()
    {
        var (sink, h) = Make();
        var r = sink.Apply(Groups(3), new TikTokMapOptions { Today = Today }, ImportProductMode.Supplement, ImportSource.TikTok, "salework",
            supersedeOrderIds: Enumerable.Range(0, 600).Select(i => "O" + i).ToList());
        Assert.True(r.Import.Ok, r.Import.Message);
        Assert.Equal(new[] { 3, 0, 0 }, h.Bodies.Select(b => b["parcels"]!.AsArray().Count));
        Assert.Equal(new[] { 0, 500, 100 }, h.Bodies.Select(b => b["orderIds"]!.AsArray().Count));
        Assert.Equal(2, r.Superseded);
    }

    [Fact]
    public void Salework_and_file_import_in_cloud_mode_send_to_cloud_and_never_touch_the_local_database()
    {
        var dir = Path.Combine(Path.GetTempPath(), "qlhh-sink-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var db = Path.Combine(dir, "data.db");
            AppDatabase.Initialize(db);
            var (sink, h) = Make();
            var sw = SaleworkImportService.Apply(db, Enumerable.Range(0, 4).Select(Row).ToList(), new TikTokMapOptions { Today = Today },
                ImportProductMode.Supplement, Today, sink);
            Assert.True(sw.Ok, sw.Message);
            Assert.Equal(4, sw.Import!.Added);
            Assert.Equal("salework", (string?)h.Bodies[0]["source"]);
            Assert.Equal(4, h.Bodies.Last()["orderIds"]!.AsArray().Count);
            var csv = System.Text.Encoding.UTF8.GetBytes("Order ID,Return Order ID,Return Logistics Tracking ID,Product Name,Return Status,Quantity\nO9,C9,RT9,Shirt,In Process,1\n");
            var fi = FileImportService.Import(db, new[] { ("a.csv", csv) }, new TikTokMapOptions { Today = Today }, ImportProductMode.Supplement, Today, sink);
            Assert.True(fi.Ok, fi.Message);
            Assert.Equal(1, fi.Added);
            Assert.Equal("file", (string?)h.Bodies.Last()["source"]);
            Assert.Empty(OrderRepository.Load(db));     // máy tính không tự gộp: nhận về từ cloud như mọi thay đổi khác
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Offline_does_not_import_anything_and_says_so()
    {
        var (sink, h) = Make(online: false);
        var r = sink.Apply(Groups(5), new TikTokMapOptions { Today = Today }, ImportProductMode.Supplement, ImportSource.TikTok, "file");
        Assert.False(r.Import.Ok);
        Assert.Contains("Chưa kết nối cloud", r.Import.Message);
        Assert.Empty(h.Bodies);
    }

    [Fact]
    public void A_failed_batch_stops_and_reports_how_many_were_sent()
    {
        var (sink, _) = Make(failAt: 2);
        var r = sink.Apply(Groups(250), new TikTokMapOptions { Today = Today }, ImportProductMode.Supplement, ImportSource.TikTok, "file");
        Assert.False(r.Import.Ok);
        Assert.Contains("100/250", r.Import.Message);
        Assert.Contains("Cloud tạm lỗi", r.Import.Message);
    }
}
