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
}
