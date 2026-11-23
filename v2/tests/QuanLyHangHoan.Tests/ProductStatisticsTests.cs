using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Core.Stats;

namespace QuanLyHangHoan.Tests;

/// <summary>Thống kê sản phẩm: dữ liệu GIẢ.</summary>
public sealed class ProductStatisticsTests
{
    private static Order O(string id, string platform, string note, params OrderItem[] items)
    {
        var o = new Order { Id = id, OrderId = id, Platform = platform, Note = note };
        o.Items.AddRange(items);
        return o;
    }
    private static OrderItem I(string product, string color, string size, int qty, int returned = 0) =>
        new() { Product = product, Color = color, Size = size, Qty = qty, ReturnedQty = returned };

    [Fact]
    public void Units_orders_share_and_ranking_are_computed_per_product()
    {
        var r = ProductStatistics.Compute(new[]
        {
            O("1", "Shopee", "Sai size", I("Áo Polo", "Đen", "L", 2)),
            O("2", "TikTok Shop", "Sai size", I("Áo Polo", "Trắng", "M", 1), I("Quần Jean", "Xanh", "31", 1)),
            O("3", "Shopee", "Đổi ý", I("Quần Jean", "Xanh", "31", 1)),
            O("4", "Shopee", "Sai size", I("Áo Polo", "Đen", "L", 3)),
        });
        Assert.Equal(new[] { "Áo Polo", "Quần Jean" }, r.Products.Select(p => p.Name).ToArray());
        var polo = r.Products[0];
        Assert.Equal((6, 3), (polo.Units, polo.Orders));
        Assert.Equal((5, 1), (polo.ShopeeUnits, polo.TikTokUnits));
        Assert.Equal("Sai size", polo.TopReason);
        Assert.Equal(8, r.TotalUnits);
        Assert.Equal(0.75, polo.Share, 3);
        Assert.Equal(new[] { ("Đen", "L", 5), ("Trắng", "M", 1) }, polo.Variants.Select(v => (v.Color, v.Size, v.Units)).ToArray());
        Assert.Equal(4, r.OrdersWithItems);
    }

    [Fact]
    public void Spelling_and_spacing_variants_of_the_same_product_are_merged_keeping_the_most_common_spelling()
    {
        var r = ProductStatistics.Compute(new[]
        {
            O("1", "Shopee", "", I("Áo  Polo ", "", "", 1)),
            O("2", "Shopee", "", I("áo polo", "", "", 1)),
            O("3", "Shopee", "", I("Áo Polo", "", "", 1)),
        });
        var only = Assert.Single(r.Products);
        Assert.Equal(3, only.Units);
        Assert.Equal("Áo Polo", only.Name);
    }

    [Fact]
    public void Partial_return_counts_only_the_returned_products_by_returned_quantity()
    {
        var r = ProductStatistics.Compute(new[]
        {
            O("1", "TikTok Shop", "", I("Quần Jeans", "Xanh", "31", 2, returned: 1), I("Sơ Mi", "Trắng", "L", 1, returned: 0), I("Nón", "Đen", "Free", 1, returned: 0)),
        });
        var only = Assert.Single(r.Products);
        Assert.Equal(("Quần Jeans", 1), (only.Name, only.Units));     // không đếm Sơ Mi/Nón (không hoàn)
        Assert.Equal(1, r.TotalUnits);
    }

    [Fact]
    public void Fully_returned_orders_count_every_product_and_placeholders_and_empty_names_are_reported_separately()
    {
        var r = ProductStatistics.Compute(new[]
        {
            O("1", "Shopee", "", I("Áo A", "", "", 2, returned: 2), I("Áo B", "", "", 1, returned: 1)),
            O("2", "Shopee", "", I("Chưa rõ sản phẩm (hàng đã về, chưa có đơn)", "", "", 1)),
            O("3", "Shopee", "", I("   ", "", "", 3)),
            O("4", "Shopee", ""),                                     // không có sản phẩm
        });
        Assert.Equal(2, r.Products.Count);
        Assert.Equal(3, r.TotalUnits);
        Assert.Equal(4, r.UnknownUnits);
        Assert.Equal(1, r.OrdersWithItems);
    }

    [Fact]
    public void Empty_input_gives_an_empty_result_without_dividing_by_zero_and_zero_qty_counts_as_one()
    {
        var empty = ProductStatistics.Compute(Array.Empty<Order>());
        Assert.Empty(empty.Products); Assert.Equal(0, empty.TotalUnits);
        var one = ProductStatistics.Compute(new[] { O("1", "Shopee", "", I("Áo", "", "", 0)) });
        Assert.Equal(1, one.Products[0].Units);
        Assert.Equal(1.0, one.Products[0].Share, 3);
    }

    [Fact]
    public void Does_not_modify_the_orders()
    {
        var o = O("1", "Shopee", "x", I("Áo", "Đen", "L", 2, returned: 1), I("Quần", "Xanh", "30", 1));
        var before = (o.Items.Count, o.Items[0].Qty, o.Items[0].ReturnedQty, o.Note);
        ProductStatistics.Compute(new[] { o });
        Assert.Equal(before, (o.Items.Count, o.Items[0].Qty, o.Items[0].ReturnedQty, o.Note));
    }
}
