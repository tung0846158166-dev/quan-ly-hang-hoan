using System.IO;
using System.Text.Json.Nodes;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Cloud;

namespace QuanLyHangHoan.Tests;

/// <summary>Khiếu nại (chủ dự án 26/11/2026): trạng thái Đang xử lý / Thành công / Thất bại + ngày + ghi chú; lưu trên cloud. Dữ liệu GIẢ.</summary>
public sealed class ComplaintDataTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-kn-" + Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_dir, "t.db");
    public ComplaintDataTests() { Directory.CreateDirectory(_dir); AppDatabase.Initialize(Db); }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static JsonObject Parcel(string id, string? complaint = null)
    {
        var p = new JsonObject
        {
            ["id"] = id, ["orderId"] = "O-" + id, ["track"] = "T-" + id, ["platform"] = "Shopee", ["orderType"] = "Trả hàng", ["status"] = "Chưa trả",
            ["price"] = 100000, ["isWatchlist"] = false, ["isArchived"] = false, ["isDeleted"] = false,
            ["items"] = new JsonArray(new JsonObject { ["product"] = "Áo", ["qty"] = 1 }),
        };
        if (complaint is not null) { p["complaintStatus"] = complaint; p["complaintAt"] = "26/11/2026 10:00:00"; p["complaintNote"] = "gửi Shopee"; }
        return p;
    }

    [Fact]
    public void Cloud_complaint_fields_reach_the_local_copy_and_go_back_only_when_set()
    {
        CloudReplica.Apply(Db, new[] { Parcel("a", "Đang xử lý"), Parcel("b") }, 3);
        var all = OrderRepository.Load(Db).ToDictionary(s => s.Order.Id);
        Assert.Equal("Đang xử lý", all["a"].Order.ComplaintStatus);
        Assert.Equal("26/11/2026 10:00:00", all["a"].Order.ComplaintAt);
        Assert.Equal("gửi Shopee", all["a"].Order.ComplaintNote);
        Assert.Equal("", all["b"].Order.ComplaintStatus);

        var back = ParcelMapper.ToParcel(all["a"]);
        Assert.Equal("Đang xử lý", (string?)back["complaintStatus"]);
        // đơn không khiếu nại: KHÔNG có khóa (băm cũ giữ nguyên, không đẩy lại hàng nghìn đơn)
        Assert.False(ParcelMapper.ToParcel(all["b"]).ContainsKey("complaintStatus"));

        // cloud bỏ khiếu nại -> máy xóa theo
        CloudReplica.Apply(Db, new[] { Parcel("a") }, 4);
        Assert.Equal("", OrderRepository.Load(Db).Single(s => s.Order.Id == "a").Order.ComplaintStatus);
    }

    [Fact]
    public void Local_mode_complaint_edit_stamps_the_date_and_clearing_removes_everything()
    {
        CloudReplica.Apply(Db, new[] { Parcel("a") }, 1);
        var now = new DateTime(2026, 11, 26, 15, 30, 0);
        Assert.Equal(EditKind.Invalid, OrderEditor.SetField(Db, "a", EditField.ComplaintStatus, "Bậy", now).Kind);
        Assert.Equal(EditKind.Saved, OrderEditor.SetField(Db, "a", EditField.ComplaintStatus, "Đang xử lý", now).Kind);
        var o = OrderRepository.Load(Db).Single().Order;
        Assert.Equal("Đang xử lý", o.ComplaintStatus);
        Assert.Equal("26/11/2026 15:30:00", o.ComplaintAt);
        Assert.Equal(EditKind.Saved, OrderEditor.SetField(Db, "a", EditField.ComplaintNote, "đã gửi", now).Kind);
        Assert.Equal(EditKind.Saved, OrderEditor.SetField(Db, "a", EditField.ComplaintStatus, "Thành công", now.AddDays(2)).Kind);
        o = OrderRepository.Load(Db).Single().Order;
        Assert.Equal("26/11/2026 15:30:00", o.ComplaintAt);    // ngày khiếu nại giữ nguyên
        Assert.Equal(EditKind.Saved, OrderEditor.SetField(Db, "a", EditField.ComplaintStatus, "", now).Kind);
        o = OrderRepository.Load(Db).Single().Order;
        Assert.Equal(("", "", ""), (o.ComplaintStatus, o.ComplaintAt, o.ComplaintNote));
    }
}
