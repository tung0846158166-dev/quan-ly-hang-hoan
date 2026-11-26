using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Tests;

/// <summary>
/// Quyết định của chủ dự án (26/11/2026): kiện Shopee chưa có mã vận đơn (vd. từ tab "Trả hàng", đang xử lý) mà sau đó Sheet có CÙNG mã đơn
/// kèm mã vận đơn (cả hai không có mã yêu cầu trả) thì ĐIỀN mã vào kiện cũ, chỉ khi mã đơn đó có ĐÚNG MỘT kiện như vậy; từ hai trở lên -> nhập nhằng.
/// </summary>
public sealed class ShopeeTracklessFillTests
{
    private static readonly DateTime Today = new(2026, 11, 26);

    private static Order Old(string id, string orderId, string track = "", string returnOrderId = "", string platform = "Shopee") => new()
    {
        Id = id, OrderId = orderId, Track = track, ReturnOrderId = returnOrderId, Platform = platform, OrderType = "Trả hàng/Hoàn tiền",
        PlatformStatus = "Đang xử lý", Status = ReceiveStatus.NotReturned,
        Items = new() { new OrderItem { Product = "Áo thun", Color = "Đen", Size = "M", Qty = 1 } },
    };

    private static ImportedParcel Incoming(string orderId, string track, string returnOrderId = "") => new()
    {
        Platform = "Shopee", OrderId = orderId, Track = track, ReturnTrack = track, ReturnOrderId = returnOrderId, OrderType = "Trả hàng/Hoàn tiền",
        PlatformStatus = "Đang về", Carrier = "SPX Express", TrackingCodes = new() { track },
        Items = new() { new ImportedItem { Product = "Áo thun", Color = "Đen", Size = "M", Qty = 1, ReturnedQty = 1 } },
    };

    private static MergeOutcome Run(List<Order> main, params ImportedParcel[] parcels) =>
        TikTokMerger.Apply(main, new List<OtherEntry>(), parcels, ImportProductMode.Supplement, Today);

    [Fact]
    public void One_trackless_shopee_parcel_with_the_same_order_gets_the_tracking_code_instead_of_a_new_row()
    {
        var main = new List<Order> { Old("a", "SP1") };
        var r = Run(main, Incoming("SP1", "SPXVN000000101"));
        Assert.Single(main);
        Assert.Empty(r.Added);
        Assert.Empty(r.Ambiguous);
        Assert.Equal("SPXVN000000101", main[0].Track);
        Assert.Equal("Đang về", main[0].PlatformStatus);
        Assert.Contains("SPXVN000000101", main[0].TrackingCodes);
    }

    [Fact]
    public void Two_trackless_shopee_parcels_with_the_same_order_are_ambiguous_and_nothing_changes()
    {
        var main = new List<Order> { Old("a", "SP1"), Old("b", "SP1") };
        var r = Run(main, Incoming("SP1", "SPXVN000000101"));
        Assert.Equal(2, main.Count);
        Assert.Empty(r.Added);
        Assert.Equal(2, Assert.Single(r.Ambiguous).Candidates);
        Assert.All(main, o => Assert.Equal("", o.Track));
    }

    [Fact]
    public void No_fill_when_a_request_code_is_involved_or_the_old_parcel_already_has_a_tracking_code()
    {
        // kiện cũ có mã yêu cầu trả RR1, kiện mới không có: có thể là hai yêu cầu khác nhau -> không gộp
        var m1 = new List<Order> { Old("a", "SP1", returnOrderId: "RR1") };
        Run(m1, Incoming("SP1", "SPXVN000000101"));
        Assert.Equal(2, m1.Count);
        Assert.Equal("", m1[0].Track);

        // kiện mới có mã yêu cầu trả: đi theo quy tắc so khớp thường, không điền theo mã đơn
        var m2 = new List<Order> { Old("a", "SP1") };
        Run(m2, Incoming("SP1", "SPXVN000000101", returnOrderId: "RR9"));
        Assert.Equal(2, m2.Count);

        // kiện cũ đã có mã vận đơn khác (kiện A) -> kiện B là dòng mới (quy tắc 1-2)
        var m3 = new List<Order> { Old("a", "SP1", track: "SPXVN000000999") };
        Run(m3, Incoming("SP1", "SPXVN000000101"));
        Assert.Equal(2, m3.Count);
        Assert.Equal("SPXVN000000999", m3[0].Track);
    }

    // Quyết định chủ dự án 26/11/2026: đơn huỷ "Huỷ sau chuẩn bị" CÓ hàng về. App đặt "Không cần nhận" chỉ vì lúc nhập chưa có mã;
    // khi được điền mã thì chuyển sang chờ nhận để kho quét nhận + tính cảnh báo 12h/24h.
    private static Order OldCancel(string id, string orderId, string platform = "Shopee") => new()
    {
        Id = id, OrderId = orderId, Platform = platform, OrderType = "Đơn hủy", PlatformStatus = "Huỷ sau chuẩn bị · Đã huỷ",
        Status = ReceiveStatus.NotNeeded, Items = new() { new OrderItem { Product = "Áo thun", Qty = 1 } },
    };

    [Fact]
    public void Cancelled_order_marked_not_needed_only_for_lack_of_a_code_becomes_awaiting_receipt_when_the_code_is_filled()
    {
        var main = new List<Order> { OldCancel("a", "SP1") };
        var inc = Incoming("SP1", "SPXVN000000101"); inc.OrderType = "Đơn hủy"; inc.ReturnTrack = ""; inc.PlatformStatus = "Huỷ sau chuẩn bị · Đã huỷ";
        Run(main, inc);
        Assert.Single(main);
        Assert.Equal("SPXVN000000101", main[0].Track);
        Assert.Equal(ReceiveStatus.NotReturned, main[0].Status);
    }

    [Fact]
    public void Filling_a_code_never_touches_other_receive_statuses()
    {
        foreach (var st in new[] { ReceiveStatus.Received, ReceiveStatus.Lost, ReceiveStatus.NotReturned })
        {
            var o = OldCancel("a", "SP1"); o.Status = st;
            var main = new List<Order> { o };
            var inc = Incoming("SP1", "SPXVN000000101"); inc.OrderType = "Đơn hủy";
            Run(main, inc);
            Assert.Equal(st, main[0].Status);
        }
        // đơn KHÔNG phải đơn huỷ mà "Không cần nhận" (chủ shop tự đặt): giữ nguyên
        var r = Old("b", "SP2"); r.Status = ReceiveStatus.NotNeeded;
        var m2 = new List<Order> { r };
        Run(m2, Incoming("SP2", "SPXVN000000102"));
        Assert.Equal(ReceiveStatus.NotNeeded, m2[0].Status);
    }
}
