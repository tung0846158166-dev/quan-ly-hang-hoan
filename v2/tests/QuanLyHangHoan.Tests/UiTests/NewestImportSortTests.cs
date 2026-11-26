using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Tests.UiTests;

/// <summary>Chủ dự án 26/11/2026: các bảng đơn mặc định để đơn MỚI NẠP NHẤT lên đầu. Dữ liệu GIẢ.</summary>
public class NewestImportSortTests
{
    private static readonly DateTime Now = new(2026, 11, 26, 12, 0, 0);

    private static Order O(string id, string importedAt, string returnDate = "") => new()
    {
        OrderId = id, Track = "T" + id, Platform = "Shopee", Status = ReceiveStatus.NotReturned,
        ImportedAt = importedAt, ReturnDate = returnDate, Items = [new OrderItem { Product = "Áo", Qty = 1 }],
    };

    [Fact]
    public void Default_sort_puts_the_most_recently_imported_first_whatever_the_time_format()
    {
        var vm = new ReturnOrdersViewModel(new FakeDataService(() => Now));
        vm.LoadOrders(
        [
            O("CU", "2026-11-10T09:56:44.160Z"),            // ISO (dữ liệu cũ)
            O("MOI", "26/11/2026 10:15:00"),                 // giờ Việt Nam (đơn tạo mới)
            O("GIUA", "2026-11-20T03:00:00Z"),
            O("KHONG_GIO", "", "25/11/2026 08:00"),          // không có giờ nạp: dùng ngày hoàn
            O("KHONG_GI", ""),
        ], Now);

        Assert.Equal("import_desc", vm.SelectedSortOption.Key);
        Assert.Equal(new[] { "MOI", "KHONG_GIO", "GIUA", "CU", "KHONG_GI" }, vm.VisibleOrders.Select(o => o.OrderId));
    }

    [Fact]
    public void Reset_returns_to_newest_import_first()
    {
        var vm = new ReturnOrdersViewModel(new FakeDataService(() => Now));
        vm.LoadOrders([O("A", "2026-11-10T00:00:00Z"), O("B", "2026-11-20T00:00:00Z")], Now);
        vm.SelectedSortOption = vm.SortOptions.Single(o => o.Key == "price_desc");
        vm.ResetAllFiltersCommand.Execute(null);
        Assert.Equal("import_desc", vm.SelectedSortOption.Key);
        Assert.Equal("B", vm.VisibleOrders[0].OrderId);
    }
}
