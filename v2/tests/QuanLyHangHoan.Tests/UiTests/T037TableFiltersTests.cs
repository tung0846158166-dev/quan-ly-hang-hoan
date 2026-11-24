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
        Assert.True(vm.FilteredOrders.Count > 1000);
        Assert.Equal(ReturnOrdersViewModel.PageSize, vm.VisibleOrders.Count);
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

    [Fact]
    public void FindRow_MatchesByOrderIdTrackOrInternalId_AndHasRowReportsPresence()
    {
        var vm = new ReturnOrdersViewModel(new FakeDataService(() => Now));
        vm.LoadOrders(
        [
            new Order { Id = "guid-1", OrderId = "OD1", Track = "TA", ReturnTrack = "RA", Platform = "Shopee", OrderType = "Đơn hủy" },
            new Order { Id = "guid-2", OrderId = "OD2", Track = "TB", Platform = "Shopee", OrderType = "Đơn hủy" },
            new Order { Id = "guid-3", OrderId = "OD2", Track = "TC", Platform = "Shopee", OrderType = "Đơn hủy" }
        ], Now);
        Assert.True(vm.HasRow("OD1", "RA"));
        Assert.True(vm.HasRow("OD1", "SAI-MA"));          // mã vận đơn lệch nhưng mã đơn chỉ có một dòng
        Assert.False(vm.HasRow("OD2", "SAI-MA"));         // nhiều dòng cùng mã đơn: không đoán (quy tắc bất biến)
        Assert.True(vm.HasRow("guid-2", ""));             // mã nội bộ (từ tìm kiếm tổng)
        Assert.False(vm.HasRow("KHONG-CO", "TA"));
        Assert.True(vm.SelectOrder("OD2", "TC"));
        Assert.Equal("guid-3", vm.SelectedRow!.Source.Id);
    }

    [Fact]
    public void Paging_100PerPage_NavigationAndQuickJump()
    {
        var vm = new ReturnOrdersViewModel(new FakeDataService(() => Now));
        vm.LoadOrders(Enumerable.Range(1, 250).Select(i => new Order { OrderId = "P" + i, Track = "TP" + i, Platform = "Shopee", OrderType = "Đơn hủy" }).ToList(), Now);
        Assert.Equal(3, vm.TotalPages);
        Assert.Equal(100, vm.VisibleOrders.Count);
        Assert.Equal(250, vm.VisibleCount);
        Assert.True(vm.ShowPager);
        Assert.False(vm.CanGoPrev);
        vm.NextPageCommand.Execute(null);
        Assert.Equal(2, vm.CurrentPage);
        Assert.Equal("P101", vm.VisibleOrders[0].OrderId);
        vm.LastPageCommand.Execute(null);
        Assert.Equal(50, vm.VisibleOrders.Count);
        Assert.Equal("P201", vm.VisibleOrders[0].OrderId);
        Assert.False(vm.CanGoNext);
        vm.PageInput = "1"; vm.GoToPageInputCommand.Execute(null);
        Assert.Equal("P1", vm.VisibleOrders[0].OrderId);
        vm.PageInput = "99"; vm.GoToPageInputCommand.Execute(null);     // vượt quá: về trang cuối
        Assert.Equal(3, vm.CurrentPage);
        vm.PageInput = "abc"; vm.GoToPageInputCommand.Execute(null);    // nhập sai: giữ nguyên, ô trả về trang hiện tại
        Assert.Equal(3, vm.CurrentPage);
        Assert.Equal("3", vm.PageInput);
        // Lọc → về trang 1; xuất file lấy cả các trang.
        vm.SearchText = "P2";
        Assert.Equal(1, vm.CurrentPage);
        Assert.Equal(vm.VisibleCount, vm.FilteredOrders.Count);
        // Chọn đơn bằng lệnh (từ Trang chủ / tìm kiếm) tự nhảy tới đúng trang.
        vm.ResetAllFiltersCommand.Execute(null);
        Assert.True(vm.SelectOrder("P230", "TP230"));
        Assert.Equal(3, vm.CurrentPage);
        Assert.Equal("P230", vm.SelectedRow!.OrderId);
        Assert.Contains(vm.SelectedRow, vm.VisibleOrders);
        // Ít hơn một trang: không hiện thanh chuyển trang.
        vm.SearchText = "P7";
        Assert.True(vm.VisibleCount <= 100);
        Assert.False(vm.ShowPager);
    }

    [Fact]
    public void ArchivedAndDeleted_PageBy100_WithQuickJumpAndRevealOnRightPage()
    {
        var provider = new FakeDataService(() => Now);
        var arc = new ArchivedOrdersViewModel(provider);
        arc.LoadOrders(Enumerable.Range(1, 230).Select(i => new Order { OrderId = "A" + i, Track = "TA" + i, Platform = "Shopee", RecvDate = "20/11/2026 10:00" }).ToList());
        Assert.Equal(3, arc.Pager.TotalPages);
        Assert.Equal(100, arc.VisibleOrders.Count);
        Assert.Equal(230, arc.VisibleCount);
        arc.Pager.NextPageCommand.Execute(null);
        Assert.Equal("A101", arc.VisibleOrders[0].OrderId);
        arc.Pager.PageInput = "3"; arc.Pager.GoToPageInputCommand.Execute(null);
        Assert.Equal(30, arc.VisibleOrders.Count);
        Assert.Equal(230, arc.Pager.All.Count);
        Assert.True(arc.SelectOrder("A150", "TA150"));
        Assert.Equal(2, arc.Pager.CurrentPage);
        Assert.Contains(arc.SelectedRow, arc.VisibleOrders);
        arc.SearchText = "A7";
        Assert.Equal(1, arc.Pager.CurrentPage);

        var del = new DeletedOrdersViewModel(provider);
        del.LoadOrders(Enumerable.Range(1, 120).Select(i => new DeletedOrderRecord { OrderId = "D" + i, Track = "TD" + i, Platform = "Shopee", DeletedFrom = "Bảng chính", DeletedAt = "20/11/2026 09:00" }).ToList());
        Assert.Equal(2, del.Pager.TotalPages);
        Assert.Equal(100, del.VisibleOrders.Count);
        Assert.True(del.SelectOrder("D110", "TD110"));
        Assert.Equal(2, del.Pager.CurrentPage);
        Assert.Equal("D110", del.SelectedRow!.OrderId);
        del.Pager.LastPageCommand.Execute(null);
        Assert.Equal(20, del.VisibleOrders.Count);
    }

    [Fact]
    public void Watchlist_PagesBy100()
    {
        var vm = new WatchlistViewModel();
        var many = Enumerable.Range(1, 140).Select(i => new Order { Id = "w" + i, OrderId = "W" + i, Track = "TW" + i, Platform = "Shopee", IsWatchlist = true, ReturnedAt = Now.AddHours(-i).ToString("dd/MM/yyyy HH:mm") }).ToList();
        vm.LoadOrders(many, [], Now);
        Assert.Equal(2, vm.Pager.TotalPages);
        Assert.Equal(100, vm.VisibleOrders.Count);
        Assert.Equal(140, vm.Pager.All.Count);
        vm.Pager.LastPageCommand.Execute(null);
        Assert.Equal(40, vm.VisibleOrders.Count);
        vm.SearchText = "W13";
        Assert.Equal(1, vm.Pager.CurrentPage);
        Assert.False(vm.Pager.ShowPager);
    }

    [Fact]
    public async Task FileImport_BacksUpTheDatabaseBeforeImporting()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "QLHH_T038_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var db = System.IO.Path.Combine(dir, "data.db");
            QuanLyHangHoan.Data.AppDatabase.Initialize(db);
            var csv = System.IO.Path.Combine(dir, "khong-phai-file-don.csv");
            System.IO.File.WriteAllText(csv, "a,b\n1,2\n");
            var ctl = new QuanLyHangHoan.App.Services.RealFileImportController(db, () => new DateTime(2026, 11, 24, 12, 0, 0));
            await ctl.ImportAsync([csv], new QuanLyHangHoan.Core.Import.TikTokMapOptions(), QuanLyHangHoan.Core.Import.ImportProductMode.Supplement);
            var backups = System.IO.Directory.GetFiles(System.IO.Path.Combine(dir, "backups"), "data-*.db");
            Assert.Single(backups);                                   // đã có bản sao lưu ngay trước khi nạp
            Assert.True(new System.IO.FileInfo(backups[0]).Length > 0);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
    }
}