using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Tests;

/// <summary>Quy tắc 5: nhập lại cùng kiện thì cập nhật, không cộng lặp sản phẩm. Dữ liệu giả.</summary>
public sealed class ParcelUpsertTests
{
    private static Order P(string orderId, string track, int qty = 1, string platform = "Shopee") => new()
    {
        Platform = platform, OrderId = orderId, Track = track, Price = 100,
        Items = { new OrderItem { Product = "Áo giả", Color = "Đen", Size = "M", Qty = qty } },
    };

    [Fact]
    public void New_parcel_is_inserted()
    {
        var list = new List<Order>();
        var r = ParcelUpsert.Apply(list, P("O1", "A"));
        Assert.Equal(UpsertKind.Inserted, r.Kind);
        Assert.Single(list);
    }

    [Fact]
    public void Reimport_same_parcel_updates_and_does_not_double_items()
    {
        var list = new List<Order> { P("O1", "A", qty: 2) };
        var again = P("O1", "A", qty: 2);
        again.Price = 150;

        var r = ParcelUpsert.Apply(list, again);

        Assert.Equal(UpsertKind.Updated, r.Kind);
        Assert.Single(list);
        Assert.Equal(150, list[0].Price);
        Assert.Single(list[0].Items);
        Assert.Equal(2, list[0].Items[0].Qty); // không thành 4
    }

    [Fact]
    public void Reimport_keeps_local_warehouse_fields()
    {
        var existing = P("O1", "A");
        existing.Status = ReceiveStatus.Received; existing.RecvDate = "10/11/2026";
        existing.PhotoId = "p_X"; existing.UserNote = "đã kiểm";
        var list = new List<Order> { existing };

        ParcelUpsert.Apply(list, P("O1", "A")); // dữ liệu nhập có Status rỗng

        Assert.Equal(ReceiveStatus.Received, list[0].Status);
        Assert.Equal("10/11/2026", list[0].RecvDate);
        Assert.Equal("p_X", list[0].PhotoId);
        Assert.Equal("đã kiểm", list[0].UserNote);
    }

    [Fact]
    public void Sibling_parcels_same_order_id_stay_separate()
    {
        var list = new List<Order> { P("O1", "A") };
        var r = ParcelUpsert.Apply(list, P("O1", "B"));
        Assert.Equal(UpsertKind.Inserted, r.Kind);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void Explicit_tracking_code_link_updates_and_keeps_history_code()
    {
        var existing = P("O1", "A");
        var list = new List<Order> { existing };
        var incoming = P("O1", "A2");
        incoming.TrackingCodes.Add("A"); // liên kết tường minh tới mã cũ

        var r = ParcelUpsert.Apply(list, incoming);

        Assert.Equal(UpsertKind.Updated, r.Kind);
        Assert.Single(list);
        Assert.Contains("A", list[0].TrackingCodes.Concat(new[] { list[0].Track }));
        Assert.Contains("A2", list[0].TrackingCodes.Concat(new[] { list[0].Track }));
    }

    [Fact]
    public void Multiple_candidates_is_ambiguous_and_changes_nothing()
    {
        var a = P("O1", "A"); a.TrackingCodes.Add("SHARED");
        var b = P("O1", "B"); b.TrackingCodes.Add("SHARED");
        var list = new List<Order> { a, b };
        var incoming = P("O1", "SHARED");

        var r = ParcelUpsert.Apply(list, incoming);

        Assert.Equal(UpsertKind.Ambiguous, r.Kind);
        Assert.Equal(2, list.Count);
        Assert.Equal(100, a.Price);
    }

    [Fact]
    public void Different_platform_is_never_merged()
    {
        var list = new List<Order> { P("O1", "A", platform: "Shopee") };
        var r = ParcelUpsert.Apply(list, P("O1", "A", platform: "TikTok"));
        Assert.Equal(UpsertKind.Inserted, r.Kind);
    }
}
