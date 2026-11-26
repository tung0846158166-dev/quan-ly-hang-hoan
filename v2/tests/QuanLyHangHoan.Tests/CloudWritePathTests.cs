using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Cloud;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Tests;

/// <summary>
/// Chế độ cloud (chủ dự án 26/11/2026: "triển khai hết"): tạo đơn mới và tự lưu trữ đơn cũ đều ĐI QUA CLOUD trước (không ghi riêng
/// vào máy), mất mạng thì từ chối, không hàng đợi. Dữ liệu GIẢ.
/// </summary>
public sealed class CloudWritePathTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-cwp-" + Guid.NewGuid().ToString("N"));
    public CloudWritePathTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static CloudOpResult Ok(JsonObject result) => new("x", true, 1, result, null, null);

    // ---------------------------------------------------------------- tạo đơn mới
    [Fact]
    public async Task New_order_is_created_on_the_cloud_with_the_form_fields()
    {
        JsonObject? sent = null;
        var c = new CloudNewOrderCreator(() => true, (op, _) =>
        {
            sent = op;
            return Task.FromResult(Ok(new JsonObject { ["outcome"] = "success", ["message"] = "ĐÃ TẠO ĐƠN MỚI Ở BẢNG CHÍNH (Shopee)", ["code"] = "SPXVN01", ["platform"] = "Shopee", ["orderId"] = "SPXVN01" }));
        });
        var r = await c.CreateAsync(new QuanLyHangHoan.Data.NewOrderInput("spxvn01", Product: "Áo", Qty: 2, Note: "khách gửi"));
        Assert.True(r.Ok);
        Assert.Equal("success", r.Outcome);
        Assert.Equal("Shopee", r.Platform);
        Assert.Equal("newOrder", sent!["type"]!.GetValue<string>());
        Assert.Equal("spxvn01", sent["code"]!.GetValue<string>());
        Assert.Equal("manual", sent["origin"]!.GetValue<string>());
        Assert.Equal(2, sent["qty"]!.GetValue<int>());
    }

    [Fact]
    public async Task Duplicate_and_offline_are_refused_without_creating_anything()
    {
        var dup = new CloudNewOrderCreator(() => true, (_, _) => Task.FromResult(Ok(new JsonObject
        { ["outcome"] = "duplicate", ["message"] = "ĐƠN ĐÃ CÓ Ở KHO LƯU TRỮ, KHÔNG TẠO LẠI", ["code"] = "A1", ["orderId"] = "O1" })));
        var r = await dup.CreateAsync(new QuanLyHangHoan.Data.NewOrderInput("A1"));
        Assert.False(r.Ok);
        Assert.Equal("duplicate", r.Outcome);
        Assert.Contains("KHO LƯU TRỮ", r.Message);

        var called = false;
        var off = new CloudNewOrderCreator(() => false, (_, _) => { called = true; return Task.FromResult(Ok(new JsonObject())); });
        var o = await off.CreateAsync(new QuanLyHangHoan.Data.NewOrderInput("A2"));
        Assert.False(o.Ok);
        Assert.Contains("Mất kết nối", o.Message);
        Assert.False(called);
    }

    // ---------------------------------------------------------------- tự lưu trữ
    private static object Row(string oid, string track, string status, string recv) => new
    {
        platform = "Shopee", orderType = "x", orderId = oid, track, status, recvDate = recv, price = 1, items = Array.Empty<object>()
    };

    [Fact]
    public async Task Archive_sweep_sends_archive_ops_to_the_cloud_and_never_writes_the_local_copy()
    {
        File.WriteAllText(Path.Combine(_dir, "du_lieu_hang_hoan.json"), JsonSerializer.Serialize(new[]
        {
            Row("O1", "A", "Hàng đã trả", "18/11/2026 10:00:00"),  // đủ điều kiện
            Row("O2", "B", "Hàng đã trả", "23/11/2026 09:00:00"),  // chưa đủ 24h
            Row("O3", "C", "Chưa trả", "18/11/2026 10:00:00"),
        }));
        var db = Path.Combine(_dir, "t.db");
        LegacyImporter.Import(_dir, db);
        var idA = OrderRepository.Load(db).Single(x => x.Order.Track == "A").Order.Id;

        Assert.Equal(new[] { idA }, ArchiveSweep.DueIds(db, Now));

        var batches = new List<List<JsonObject>>();
        var sweeper = new CloudArchiveSweeper(db, () => true, (ops, _) =>
        {
            var list = ops.ToList();
            batches.Add(list);
            return Task.FromResult<IReadOnlyList<CloudOpResult>>(list.Select(o => new CloudOpResult(o["opId"]!.GetValue<string>(), true, 1, null, null, null)).ToList());
        });
        Assert.Equal(1, await sweeper.SweepAsync(Now));
        var op = Assert.Single(Assert.Single(batches));
        Assert.Equal("archive", op["type"]!.GetValue<string>());
        Assert.Equal(idA, op["id"]!.GetValue<string>());
        Assert.False(OrderRepository.Load(db).Single(x => x.Order.Track == "A").IsArchived);   // bản sao chỉ đổi khi cloud gửi về

        // cùng ngày gửi lại: cùng opId (cloud trả kết quả cũ, không làm hai lần)
        await sweeper.SweepAsync(Now);
        Assert.Equal(batches[0][0]["opId"]!.GetValue<string>(), batches[1][0]["opId"]!.GetValue<string>());

        var offline = new CloudArchiveSweeper(db, () => false, (_, _) => throw new InvalidOperationException("không được gọi mạng"));
        Assert.Equal(0, await offline.SweepAsync(Now));
    }
}
