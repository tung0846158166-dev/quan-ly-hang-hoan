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

    /// <summary>
    /// Lỗi 27/11/2026 (thấy trên điện thoại): đơn chép về từ bản app CHƯA biết trường mới không bao giờ được tải lại, vì đồng bộ theo mốc.
    /// Bản sao ghi bằng phiên bản trường cũ (hoặc không ghi phiên bản) phải trả mốc 0 để tải lại toàn bộ một lần; ghi đúng phiên bản thì giữ mốc.
    /// </summary>
    [Fact]
    public void Replica_written_by_older_field_version_resyncs_from_zero_once()
    {
        CloudReplica.Apply(Db, new[] { Parcel("a") }, 50);
        Assert.Equal(50, CloudReplica.GetRev(Db));
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Db}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM cloud_meta WHERE k = 'fields'";   // giống bản sao do app cũ ghi
            cmd.ExecuteNonQuery();
        }
        Assert.Equal(0, CloudReplica.GetRev(Db));
        CloudReplica.Apply(Db, new[] { Parcel("a", "Đang xử lý") }, 60);
        Assert.Equal(60, CloudReplica.GetRev(Db));
    }

    /// <summary>Chủ dự án 27/11/2026: lý do khiếu nại / đổi trạng thái thêm một dòng có ngày giờ vào ghi chú khiếu nại (giống cloud).</summary>
    // 28/11/2026 (đơn 2610300U6NUPAA): đổi về "Chưa trả" thì bỏ ngày nhận kho
    [Fact]
    public void Setting_status_back_to_not_returned_clears_the_receive_date()
    {
        CloudReplica.Apply(Db, new[] { Parcel("a") }, 1);
        var now = new DateTime(2026, 11, 27, 13, 34, 32);
        Assert.Equal(EditKind.Saved, OrderEditor.SetField(Db, "a", EditField.Status, ReceiveStatus.Received, now).Kind);
        Assert.Equal("27/11/2026 13:34:32", OrderRepository.Load(Db).Single().Order.RecvDate);
        Assert.Equal(EditKind.Saved, OrderEditor.SetField(Db, "a", EditField.Status, ReceiveStatus.NotReturned, now.AddMinutes(1)).Kind);
        Assert.Equal("", OrderRepository.Load(Db).Single().Order.RecvDate);
    }

    [Fact]
    public void Local_mode_reason_appends_a_dated_line_to_the_complaint_note()
    {
        CloudReplica.Apply(Db, new[] { Parcel("a") }, 1);
        var now = new DateTime(2026, 11, 27, 9, 5, 0);
        Assert.Equal(EditKind.Saved, OrderEditor.SetField(Db, "a", EditField.ComplaintStatus, "Đang xử lý", now, reason: " thiếu  áo ").Kind);
        Assert.Equal(EditKind.Saved, OrderEditor.SetField(Db, "a", EditField.ComplaintStatus, "Thất bại", now.AddHours(1), reason: "sàn từ chối").Kind);
        Assert.Equal("27/11/2026 09:05 · Đang xử lý: thiếu áo\n27/11/2026 10:05 · Thất bại: sàn từ chối", OrderRepository.Load(Db).Single().Order.ComplaintNote);
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
