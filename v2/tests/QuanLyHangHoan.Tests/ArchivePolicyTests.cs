using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Tests;

/// <summary>Đơn đã nhận về kho >= 24h tự dời sang kho lưu trữ (cờ, không xóa). Dữ liệu GIẢ.</summary>
public sealed class ArchivePolicyTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-arch-" + Guid.NewGuid().ToString("N"));
    public ArchivePolicyTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static Order O(string status, string recv) => new() { Status = status, RecvDate = recv };

    [Theory]
    [InlineData("Hàng đã trả", "22/11/2026 12:00:00", true)]   // đúng 24h
    [InlineData("Hàng đã trả", "22/11/2026 12:00:01", false)]  // thiếu 1 giây
    [InlineData("Hàng đã trả", "18/11/2026 20:09:43", true)]
    [InlineData("Hàng đã trả", "22/11/2026 11:59", true)]      // không có giây
    [InlineData("Hàng đã trả", "22/11/2026", false)]           // chỉ có ngày: coi là cuối ngày, chưa đủ 24h
    [InlineData("Hàng đã trả", "21/11/2026", true)]
    [InlineData("Hàng đã trả", "", false)]                      // không rõ ngày nhận: KHÔNG đoán
    [InlineData("Hàng đã trả", "không phải ngày", false)]
    [InlineData("Chưa trả", "10/11/2026 10:00:00", false)]
    [InlineData("Chưa về", "10/11/2026 10:00:00", false)]
    [InlineData("Bị mất", "10/11/2026 10:00:00", false)]
    public void ShouldArchive_only_received_and_24h_passed(string status, string recv, bool expected) =>
        Assert.Equal(expected, ArchivePolicy.ShouldArchive(O(status, recv), Now));

    [Fact]
    public void Threshold_is_configurable() =>
        Assert.True(new ArchivePolicy(TimeSpan.FromHours(1)).Check(O("Hàng đã trả", "23/11/2026 10:00:00"), Now));

    private static object Row(string oid, string track, string status, string recv) => new
    {
        platform = "Shopee", orderType = "x", orderId = oid, track, status, recvDate = recv, price = 1, items = Array.Empty<object>()
    };

    [Fact]
    public void Sweep_flags_eligible_only_idempotent_never_touches_deleted_and_never_deletes()
    {
        File.WriteAllText(Path.Combine(_dir, "du_lieu_hang_hoan.json"), JsonSerializer.Serialize(new[]
        {
            Row("O1", "A", "Hàng đã trả", "18/11/2026 10:00:00"),  // đủ điều kiện
            Row("O2", "B", "Hàng đã trả", "23/11/2026 09:00:00"),  // chưa đủ 24h
            Row("O3", "C", "Chưa trả", "18/11/2026 10:00:00"),     // chưa nhận
        }));
        File.WriteAllText(Path.Combine(_dir, "phu_shopee_deleted_orders_v1.json"), JsonSerializer.Serialize(new
        { v = JsonSerializer.Serialize(new[] { Row("O4", "D", "Hàng đã trả", "01/11/2026 10:00:00") }) }));
        var db = Path.Combine(_dir, "t.db");
        LegacyImporter.Import(_dir, db);

        var first = ArchiveSweep.Run(db, Now);
        var second = ArchiveSweep.Run(db, Now);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        var all = OrderRepository.Load(db);
        Assert.Equal(4, all.Count); // không mất dòng nào
        Assert.True(all.Single(x => x.Order.Track == "A").IsArchived);
        Assert.Equal("23/11/2026 12:00:00", all.Single(x => x.Order.Track == "A").ArchivedAt);
        Assert.False(all.Single(x => x.Order.Track == "B").IsArchived);
        Assert.False(all.Single(x => x.Order.Track == "C").IsArchived);
        Assert.False(all.Single(x => x.Order.Track == "D").IsArchived); // đã xóa: không đụng
    }
}
