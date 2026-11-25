using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Tests.UiTests;

/// <summary>Pháº§n náº·ng (dá»±ng dÃ²ng báº£ng, sá»‘ liá»‡u trang chá»§) tÃ¡ch ra cháº¡y Ä‘Æ°á»£c á»Ÿ luá»“ng ná»n; káº¿t quáº£ pháº£i GIá»NG Há»†T cÃ¡ch náº¡p trá»±c tiáº¿p.</summary>
public class BackgroundBuildTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 12, 0, 0);

    [Fact]
    public async Task Rows_built_on_a_background_thread_give_the_same_table_as_loading_directly()
    {
        var d = new FakeDataService(() => Now);
        var orders = d.GetReturnOrders(400);
        var direct = new ReturnOrdersViewModel(d, ReturnTableScope.ShopeeReturn);
        direct.LoadOrders(orders, Now);

        var viaBackground = new ReturnOrdersViewModel(d, ReturnTableScope.ShopeeReturn);
        var rows = await Task.Run(() => viaBackground.BuildRows(orders, Now));
        viaBackground.LoadRows(rows);

        Assert.Equal(direct.TotalCount, viaBackground.TotalCount);
        Assert.Equal(direct.FilteredOrders.Count, viaBackground.FilteredOrders.Count);
        Assert.Equal(direct.PendingCount, viaBackground.PendingCount);
        Assert.Equal(direct.RedAlertCount, viaBackground.RedAlertCount);
        Assert.Equal(direct.FilteredOrders.Select(r => r.Source.Id), viaBackground.FilteredOrders.Select(r => r.Source.Id));
        Assert.True(viaBackground.TotalCount > 0);
    }

    [Fact]
    public async Task Several_tables_can_build_rows_at_the_same_time()
    {
        var d = new FakeDataService(() => Now);
        var orders = d.GetReturnOrders(2000);
        var vms = new[] { ReturnTableScope.ShopeeReturn, ReturnTableScope.ShopeeCancelled, ReturnTableScope.TikTokReturn }
            .Select(s => new ReturnOrdersViewModel(d, s)).ToArray();
        var built = await Task.WhenAll(vms.Select(v => Task.Run(() => v.BuildRows(orders, Now))));
        for (var i = 0; i < vms.Length; i++)
        {
            var seq = vms[i].BuildRows(orders, Now);
            Assert.Equal(seq.Count, built[i].Count);
            Assert.Equal(seq.Select(r => r.Source.Id), built[i].Select(r => r.Source.Id));
            Assert.Equal(seq.Select(r => r.SearchBlob), built[i].Select(r => r.SearchBlob));
        }
    }

    [Fact]
    public async Task Home_numbers_computed_in_the_background_equal_direct_loading()
    {
        var d = new FakeDataService(() => Now);
        var orders = d.GetReturnOrders(500);
        var direct = new HomeViewModel(d);
        direct.LoadData(orders, Now);

        var bg = new HomeViewModel(d);
        var computed = await Task.Run(() => bg.Compute(orders, Now));
        bg.ApplyComputed(computed, orders, Now);

        Assert.Equal(direct.TotalOrders, bg.TotalOrders);
        Assert.Equal(direct.OverallKpi, bg.OverallKpi);
        Assert.Equal(direct.ShopeeKpi, bg.ShopeeKpi);
        Assert.Equal(direct.TikTokKpi, bg.TikTokKpi);
        Assert.Equal(direct.ProductSummaryText, bg.ProductSummaryText);
        Assert.Equal(direct.UrgentItems.Count, bg.UrgentItems.Count);
        Assert.Equal(direct.DailyTrendPoints.Select(p => (p.ShopeeCount, p.TikTokCount)), bg.DailyTrendPoints.Select(p => (p.ShopeeCount, p.TikTokCount)));
    }

    [Fact]
    public void Empty_list_falls_back_to_the_normal_empty_state()
    {
        var d = new FakeDataService(() => Now);
        var home = new HomeViewModel(d);
        Assert.Null(home.Compute(Array.Empty<Order>(), Now));
        home.ApplyComputed(null, Array.Empty<Order>(), Now);
        Assert.True(home.IsEmpty);
    }

    [Fact]
    public async Task Archived_rows_built_in_the_background_load_the_same()
    {
        var d = new FakeDataService(() => Now);
        var orders = d.GetReturnOrders(300);
        var direct = new ArchivedOrdersViewModel(d);
        direct.LoadOrders(orders);
        var bg = new ArchivedOrdersViewModel(d);
        bg.LoadRows(await Task.Run(() => bg.BuildRows(orders)));
        Assert.Equal(direct.TotalCount, bg.TotalCount);
        Assert.Equal(direct.ShopeeCount, bg.ShopeeCount);
        Assert.Equal(direct.TikTokCount, bg.TikTokCount);
    }
}

