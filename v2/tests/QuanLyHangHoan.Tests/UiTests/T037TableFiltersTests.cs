using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Tests.UiTests;

public class T037TableFiltersTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);

    private static Order O(string id, string carrier, string product, string photo, string returnDate, decimal price) => new()
    {
        OrderId = id, Track = "T" + id, Platform = "Shopee", Status = ReceiveStatus.NotReturned,
        Carrier = carrier, PhotoId = photo, ReturnDate = returnDate, Price = price,
        Items = [new OrderItem { Product = product, Qty = 1 }]
    };

    private static ReturnOrdersViewModel Make()
    {
        var vm = new ReturnOrdersViewModel(new FakeDataService(() => Now));
        vm.LoadOrders(
        [
            O("A1", "SPX", "Áo Thun Premium", "p1", "20/11/2026 10:00", 100),
            O("A2", "GHTK", "Quần Jeans", "", "21/11/2026 10:00", 300),
            O("A3", "SPX", "Áo Khoác", "", "22/11/2026 10:00", 200)
        ], Now);
        return vm;
    }

    [Fact]
    public void ProductFilter_IgnoresAccents()
    {
        var vm = Make();
        vm.ProductFilter = "ao";
        Assert.Equal(2, vm.VisibleOrders.Count);
        Assert.Equal(1, vm.AdvancedFilterCount);
    }

    [Fact]
    public void CarrierPhotoAndDateFilters_Combine()
    {
        var vm = Make();
        Assert.Contains("SPX", vm.CarrierOptions);
        vm.CarrierFilter = "SPX";
        Assert.Equal(2, vm.VisibleOrders.Count);
        vm.PhotoOption = "Có ảnh";
        Assert.Single(vm.VisibleOrders);
        vm.PhotoOption = "Ảnh: tất cả";
        vm.DateFrom = new DateTime(2026, 11, 22);
        Assert.Equal("A3", vm.VisibleOrders.Single().OrderId);
        Assert.Equal(2, vm.AdvancedFilterCount);
    }

    [Fact]
    public void ResetAllFilters_ClearsAdvanced()
    {
        var vm = Make();
        vm.ProductFilter = "quan";
        vm.CarrierFilter = "GHTK";
        vm.ResetAllFiltersCommand.Execute(null);
        Assert.Equal(3, vm.VisibleOrders.Count);
        Assert.Equal(0, vm.AdvancedFilterCount);
    }

    [Fact]
    public void SortByColumn_TogglesDirection()
    {
        var vm = Make();
        Assert.True(vm.SortByColumn("Price", out var d1));
        Assert.Equal(System.ComponentModel.ListSortDirection.Ascending, d1);
        Assert.Equal(["A1", "A3", "A2"], vm.VisibleOrders.Select(r => r.OrderId).ToArray());
        Assert.True(vm.SortByColumn("Price", out var d2));
        Assert.Equal(System.ComponentModel.ListSortDirection.Descending, d2);
        Assert.Equal("A2", vm.VisibleOrders[0].OrderId);
        Assert.False(vm.SortByColumn("Khong-co", out _));
    }

    [Fact]
    public void SortByColumn_50kRows_IsFast()
    {
        var vm = new ReturnOrdersViewModel(new FakeDataService(() => Now));
        vm.LoadFakeBatchAsync(50_000).GetAwaiter().GetResult();
        Assert.True(vm.VisibleOrders.Count > 1000);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        vm.SortByColumn("Price", out _);
        vm.SortByColumn("OrderId", out _);
        vm.ProductFilter = "ao";
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 3000, $"quá chậm: {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void ArchivedAndDeleted_AdvancedFilters_ProductAndDate()
    {
        var provider = new FakeDataService(() => Now);
        var arc = new ArchivedOrdersViewModel(provider);
        arc.LoadOrders(
        [
            new Order { OrderId = "R1", Track = "T1", Platform = "Shopee", RecvDate = "20/11/2026 10:00", Items = [new OrderItem { Product = "Áo Len", Qty = 1 }] },
            new Order { OrderId = "R2", Track = "T2", Platform = "Shopee", RecvDate = "22/11/2026 10:00", Items = [new OrderItem { Product = "Quần Kaki", Qty = 1 }] }
        ]);
        arc.Filters.ProductFilter = "ao len";
        Assert.Equal("R1", arc.VisibleOrders.Single().OrderId);
        arc.Filters.ProductFilter = "";
        arc.Filters.DateFrom = new DateTime(2026, 11, 21);
        Assert.Equal("R2", arc.VisibleOrders.Single().OrderId);
        arc.ResetAllFiltersCommand.Execute(null);
        Assert.Equal(2, arc.VisibleOrders.Count);
        Assert.Equal(0, arc.Filters.Count);

        var del = new DeletedOrdersViewModel(provider);
        del.LoadOrders(
        [
            new DeletedOrderRecord { OrderId = "D1", Track = "T1", Platform = "Shopee", DeletedFrom = "Bảng chính", DeletedAt = "20/11/2026 09:00", Items = [new OrderItem { Product = "Váy Midi", Qty = 1 }] },
            new DeletedOrderRecord { OrderId = "D2", Track = "T2", Platform = "Shopee", DeletedFrom = "Bảng chính", DeletedAt = "23/11/2026 09:00", Items = [new OrderItem { Product = "Áo Khoác", Qty = 1 }] }
        ]);
        del.Filters.DateTo = new DateTime(2026, 11, 21);
        Assert.Equal("D1", del.VisibleOrders.Single().OrderId);
        del.Filters.DateTo = null;
        del.Filters.ProductFilter = "khoac";
        Assert.Equal("D2", del.VisibleOrders.Single().OrderId);
    }

    [Fact]
    public void HomeProductPeriods_FilterByDayWeekMonth()
    {
        var now = new DateTime(2026, 11, 25, 12, 0, 0); // thứ Tư
        Order Mk(string id, string d) => new() { OrderId = id, Track = id, Platform = "Shopee", ReturnDate = d, Items = [new OrderItem { Product = "Áo", Qty = 1 }] };
        IReadOnlyList<Order> all = [Mk("a", "25/11/2026 08:00"), Mk("b", "23/11/2026 08:00"), Mk("c", "10/11/2026 08:00"), Mk("d", "31/10/2026 08:00")];
        Assert.Equal(["a"], HomeViewModel.FilterByPeriod(all, "day", now).Select(o => o.OrderId));
        Assert.Equal(["a", "b"], HomeViewModel.FilterByPeriod(all, "week", now).Select(o => o.OrderId)); // tuần bắt đầu thứ Hai 23/11
        Assert.Equal(["a", "b", "c"], HomeViewModel.FilterByPeriod(all, "month", now).Select(o => o.OrderId));
        Assert.Equal(["a", "b"], HomeViewModel.FilterByPeriod(all, "7d", now).Select(o => o.OrderId));
        Assert.Equal(["a", "b", "c", "d"], HomeViewModel.FilterByPeriod(all, "all", now).Select(o => o.OrderId));
    }
}