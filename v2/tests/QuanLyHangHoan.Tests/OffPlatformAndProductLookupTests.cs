using System.IO;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Core.Stats;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Cloud;

namespace QuanLyHangHoan.Tests;

/// <summary>Chủ dự án 27/11/2026: bảng Đơn ngoài sàn (tạo nhanh từ dữ liệu sản phẩm) + tra nhanh sản phẩm theo ngày/tuần/tháng. Dữ liệu GIẢ.</summary>
public sealed class OffPlatformAndProductLookupTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 27, 15, 0, 0);   // thứ Sáu; tuần bắt đầu thứ Hai 23/11
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-off-" + Guid.NewGuid().ToString("N"));
    public OffPlatformAndProductLookupTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private string NewDb() { var db = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".db"); AppDatabase.Initialize(db); return db; }

    private static Order O(string id, string product, int qty = 1, string color = "", string size = "", string status = ReceiveStatus.NotReturned,
        string recv = "", string ret = "", string platform = "Shopee") => new()
    {
        Id = id, OrderId = id, Track = "T" + id, Platform = platform, Status = status, RecvDate = recv, ReturnDate = ret,
        Items = { new OrderItem { Product = product, Color = color, Size = size, Qty = qty } },
    };

    // ------------------------------------------------------------------ tạo đơn (máy, chế độ không cloud)
    [Fact]
    public void Local_off_platform_order_gets_an_NS_code_many_items_price_channel_and_its_own_marker()
    {
        var db = NewDb();
        var input = new NewOrderInput("", Platform: OffPlatform.Name, Note: "")
        {
            Items = [new NewOrderItem("Áo thun", "Đen", "L", 2), new NewOrderItem("Quần jean", "", "30", 1), new NewOrderItem("", "", "", 1)],
            Price = 350000.4m, OrderType = "Đổi hàng", Channel = "Zalo",
        };
        var r = NewOrderService.Create(db, input, Now);
        Assert.True(r.Ok, r.Message);
        Assert.Matches("^NS261127[2-9A-HJ-NP-Z]{4}$", r.Code);
        var o = Assert.Single(OrderRepository.Load(db)).Order;
        Assert.Equal((OffPlatform.Name, "Đổi hàng", "Kênh: Zalo", 350000m, "", OffPlatform.Week, OffPlatform.DefaultNote),
            (o.Platform, o.OrderType, o.PlatformStatus, o.Price, o.Carrier, o.ImportWeek, o.Note));
        Assert.Equal(new[] { ("Áo thun", "Đen", "L", 2), ("Quần jean", "", "30", 1) }, o.Items.Select(i => (i.Product, i.Color, i.Size, i.Qty)));
        Assert.True(OffPlatform.Is(o));
        Assert.False(NewOrderMarker.IsNewlyCreated(o));                          // không lẫn vào "Bảng đơn mới"
        Assert.Equal("Zalo", OffPlatform.ChannelOf(o));
        Assert.True(ReturnOrdersViewModel.MatchesScope(o, ReturnTableScope.OffPlatform));
        Assert.False(ReturnOrdersViewModel.MatchesScope(o, ReturnTableScope.ShopeeReturn));
        Assert.False(ReturnOrdersViewModel.MatchesScope(o, ReturnTableScope.NewCreated));
        // mã đã có thì không tạo lại
        var dup = NewOrderService.Create(db, new NewOrderInput(r.Code, Platform: OffPlatform.Name) { Items = [new NewOrderItem("Váy")] }, Now);
        Assert.Equal("duplicate", dup.Outcome);
        // sàn thường vẫn bắt buộc mã
        Assert.Equal("invalid", NewOrderService.Create(db, new NewOrderInput("", Platform: "Shopee"), Now).Outcome);
    }

    [Fact]
    public async Task Cloud_off_platform_op_carries_items_price_type_and_channel()
    {
        JsonObject? sent = null;
        var c = new CloudNewOrderCreator(() => true, (op, _) =>
        {
            sent = op;
            return Task.FromResult(new CloudOpResult("x", true, 1, new JsonObject { ["outcome"] = "success", ["message"] = "ok", ["code"] = "NS261127ABCD", ["platform"] = "Ngoài sàn", ["orderId"] = "NS261127ABCD" }, null, null));
        });
        var r = await c.CreateAsync(new NewOrderInput("", Platform: OffPlatform.Name) { Items = [new NewOrderItem("Áo", "Đỏ", "M", 3)], Price = 120000, OrderType = "Đổi hàng", Channel = "Facebook" });
        Assert.True(r.Ok);
        Assert.Equal("NS261127ABCD", r.Code);                                      // mã cloud sinh ra
        Assert.Equal("Ngoài sàn", sent!["platform"]!.GetValue<string>());
        Assert.Equal("", sent["code"]!.GetValue<string>());
        var item = sent["items"]!.AsArray().Single()!.AsObject();
        Assert.Equal(("Áo", "Đỏ", "M", 3), (item["product"]!.GetValue<string>(), item["color"]!.GetValue<string>(), item["size"]!.GetValue<string>(), item["qty"]!.GetValue<int>()));
        Assert.Equal(120000m, sent["price"]!.GetValue<decimal>());
        Assert.Equal("Đổi hàng", sent["orderType"]!.GetValue<string>());
        Assert.Equal("Facebook", sent["channel"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------ danh mục sản phẩm
    [Fact]
    public void Catalog_merges_spellings_orders_colors_by_use_and_searches_without_tones()
    {
        var cat = ProductCatalog.Build(new[]
        {
            O("1", "Áo thun basic", 2, "Đen", "L"), O("2", "ao thun  basic", 1, "Trắng", "M"), O("3", "Áo thun basic", 3, "Đen", "M"),
            O("4", "Quần jean ống rộng", 1, "Xanh", "30"), O("5", "Chưa rõ sản phẩm (quét)", 1), O("6", "Sản phẩm mới (nhập tay)", 1),
        });
        Assert.Equal(2, cat.Products.Count);                                         // bỏ sản phẩm giữ chỗ
        var ao = cat.Products[0];
        Assert.Equal(("Áo thun basic", 6), (ao.Name, ao.Units));
        Assert.Equal(new[] { "Đen", "Trắng" }, ao.Colors);                           // Đen 5 cái > Trắng 1
        Assert.Equal(new[] { "M", "L" }, ao.Sizes);                                  // M 4 > L 2
        Assert.Equal("Quần jean ống rộng", Assert.Single(cat.Search("quan ong")).Name);
        Assert.Equal("Áo thun basic", cat.Search("ÁO")[0].Name);
        Assert.Empty(cat.Search("vay"));
        Assert.NotNull(cat.Find("áo thun BASIC"));
    }

    // ------------------------------------------------------------------ tra nhanh sản phẩm theo kỳ
    [Fact]
    public void Lookup_counts_units_received_and_returned_today_this_week_and_this_month()
    {
        var orders = new[]
        {
            O("a", "Áo thun đen", 2, status: ReceiveStatus.Received, recv: "27/11/2026 09:00:00", ret: "25/11/2026 10:00:00"),   // hôm nay
            O("b", "Áo thun đen", 1, status: ReceiveStatus.Received, recv: "23/11/2026 08:00:00", ret: "20/11/2026 10:00:00"),   // thứ Hai tuần này
            O("c", "ao thun den", 4, status: ReceiveStatus.Received, recv: "22/11/2026 08:00:00", ret: "22/11/2026 08:00:00"),   // Chủ nhật tuần trước, cùng tháng
            O("d", "Áo thun đen", 5, status: ReceiveStatus.Received, recv: "30/10/2026 08:00:00", ret: "28/10/2026 08:00:00"),   // tháng trước
            O("e", "Áo thun đen", 3, ret: "27/11/2026 07:00:00"),                                                              // hoàn hôm nay, CHƯA về
            O("f", "Áo thun đen", 7, status: ReceiveStatus.NotReturned, recv: "27/11/2026 07:00:00"),                         // chưa nhận: ngày nhận bỏ qua
            O("g", "Quần short", 9, status: ReceiveStatus.Received, recv: "27/11/2026 07:00:00"),
        };
        var row = Assert.Single(ProductLookup.Search(orders, "ao den", Now));
        Assert.Equal("Áo thun đen", row.Name);
        Assert.Equal(new PeriodUnits(2, 3, 7, 12), row.Received);
        Assert.Equal(new PeriodUnits(3, 5, 10, 22), row.Returned);
        Assert.Equal(6, row.Orders);
        Assert.Empty(ProductLookup.Search(orders, "   ", Now));
        Assert.Empty(ProductLookup.Search(orders, "", Now));                        // rỗng -> không tìm
    }

    [Fact]
    public void Lookup_follows_the_partial_return_rule_and_parses_time_first_dates()
    {
        var partial = new Order
        {
            Id = "p", Status = ReceiveStatus.Received, RecvDate = "10:15:22 26/11/2026",
            Items = { new OrderItem { Product = "Áo A", Qty = 3, ReturnedQty = 1 }, new OrderItem { Product = "Quần B", Qty = 2, ReturnedQty = 0 } },
        };
        Assert.Equal(new PeriodUnits(0, 1, 1, 1), Assert.Single(ProductLookup.Search(new[] { partial }, "ao a", Now)).Received);
        Assert.Empty(ProductLookup.Search(new[] { partial }, "quan b", Now));       // sản phẩm không hoàn của đơn hoàn một phần
    }

    [Fact]
    public void Statistics_screen_lookup_uses_main_and_archive_regardless_of_filters()
    {
        var vm = new StatisticsViewModel(new FakeDataService(), new FakeDataService(), loadInitialData: false);
        vm.LoadData(new[] { O("1", "Váy hoa", 2, status: ReceiveStatus.Received, recv: "27/11/2026 08:00:00", platform: "TikTok") },
                    new[] { O("2", "Váy hoa", 1, status: ReceiveStatus.Received, recv: "02/11/2026 08:00:00") }, Now);
        vm.PlatformFilter = "Shopee";
        vm.ProductLookupText = "vay";
        var row = Assert.Single(vm.ProductLookupRows);
        Assert.Equal(new PeriodUnits(2, 2, 3, 3), row.Received);
        Assert.True(vm.HasProductLookupRows);
        Assert.Equal("Tuần này (từ 23/11)", vm.WeekLabel);
        vm.ProductLookupText = "khong co";
        Assert.Empty(vm.ProductLookupRows);
        Assert.Contains("Không có sản phẩm", vm.ProductLookupHint);
    }

    // ------------------------------------------------------------------ hàng hoàn sẵn kho (27/11/2026)
    [Fact]
    public void Received_stock_groups_recently_received_items_by_product_color_size_newest_parcel_first()
    {
        var partial = new Order
        {
            Id = "p", OrderId = "p", Track = "TP", Platform = "TikTok", Status = ReceiveStatus.Received, RecvDate = "27/11/2026 10:00:00",
            Items = { new OrderItem { Product = "Áo thun", Color = "Đen", Size = "L", Qty = 2, ReturnedQty = 1 }, new OrderItem { Product = "Quần B", Qty = 1 } },
        };
        var orders = new[]
        {
            O("a", "Áo thun", 2, "Đen", "L", ReceiveStatus.Received, "27/11/2026 09:00:00"),
            O("b", "ao thun", 1, "đen", "L", ReceiveStatus.Received, "25/11/2026 09:00:00"),
            O("c", "Áo thun", 1, "Đen", "M", ReceiveStatus.Received, "26/11/2026 09:00:00"),
            O("d", "Áo thun", 4, "Đen", "L", ReceiveStatus.Received, "20/11/2026 09:00:00"),   // quá 3 ngày
            O("e", "Áo thun", 5, "Đen", "L", ReceiveStatus.NotReturned, "27/11/2026 09:00:00"), // chưa nhận
            partial,
        };
        var since = Now.Date.AddDays(-2);
        var rows = ReceivedStock.Build(orders, since);
        Assert.Equal(2, rows.Count);                                              // Áo thun Đen L, Áo thun Đen M (Quần B không hoàn)
        var l = rows.Single(r => r.Size == "L");
        Assert.Equal(4, l.Units);                                                 // a 2 + b 1 + p 1 (đã hoàn)
        Assert.Equal(new[] { "p", "a", "b" }, l.Parcels.Select(p => p.Order.Id));  // mới nhận trước
        Assert.Equal("L", Assert.Single(ReceivedStock.Build(orders, since, "ao den l")).Size);
        Assert.Empty(ReceivedStock.Build(orders, since, "quan"));
        var vm = new ReceivedStockViewModel();
        vm.LoadOrders(orders, Array.Empty<Order>(), Now);
        Assert.Equal(2, vm.Rows.Count);
        vm.SetRangeCommand.Execute("today");
        Assert.Equal(3, Assert.Single(vm.Rows).Units);
        Assert.Contains("TP", vm.Rows[0].Codes);
        vm.SearchText = "vay";
        Assert.True(vm.IsEmpty);
        Assert.Contains("Không có hàng hoàn khớp", vm.SummaryText);
    }

    // ------------------------------------------------------------------ gộp các kiểu tên của cùng một mẫu (28/11/2026)
    [Theory]
    [InlineData("Áo Polo LEN THỎ Local Brand Lavi Studio/ ALPHA Nam Nữ Unisex", "m:alpha:aopolo")]
    [InlineData("ALPHA", "m:alpha")]
    [InlineData("[ DEAL MỞ BÁN ] Áo Polo Len Lavi Studio / ALPHA Unisex", "m:alpha:aopolo")]
    [InlineData("Áo Polo Len Khóa Zip Hàn Quốc 𝐋𝐚𝐯𝐢 𝐒𝐭𝐮𝐝𝐢𝐨/ BETA Unisex Nam Nữ", "m:beta:aopolo")]
    [InlineData("Áo Polo Lavistudio/ GAMMA Unisex", "m:gamma:aopolo")]
    [InlineData("Quần Kaki Ống Suông Lavi Studio Phong Cách Trẻ Trung/ DELTA", "m:delta:quankaki")]
    [InlineData("Áo Cardigan Lavi Studio / Logo Thêu Unisex", "aocardiganlogotheu")]
    [InlineData("Áo Cardigan Len Dệt Kim Hàn Quốc Local Brand Lavi Studio / Logo Thêu Unisex Nam Nữ", "aocardiganlendetkimhanquoclogotheu")]
    [InlineData("Áo Cardigan Len Dệt Kim Hàn Quốc Lavi Studio / Logo Thêu Unisex Nam Nữ - Nhận may theo số đo/ thêu tên theo yêu cầu khách", "aocardiganlendetkimhanquoclogotheu")]
    [InlineData("Áo Cardigan Len Dệt Kim Hàn Quốc Lavi Studio/ Logo Thêu Unisex Nam Nữ", "aocardiganlendetkimhanquoclogotheu")]
    [InlineData("Áo Polo Lavi Studio / EPSI - Nhận may theo số đo/ thêu tên", "m:epsi:aopolo")]
    [InlineData("Áo thun basic", "aothunbasic")]
    [InlineData("Váy", "vay")]
    public void Family_key_merges_short_long_and_prefixed_names(string name, string key) => Assert.Equal(key, ProductNames.FamilyKey(name));

    [Fact]
    public void Received_stock_shows_one_row_per_model_and_keeps_premium_apart()
    {
        var orders = new[]
        {
            O("1", "Áo Polo LEN THỎ Local Brand Lavi Studio/ ALPHA Nam Nữ Unisex", 1, "Xanh Than", "L", ReceivedStatus, "27/11/2026 09:00:00"),
            O("2", "ALPHA", 2, "Xanh Than", "L", ReceivedStatus, "27/11/2026 10:00:00"),
            O("3", "[ DEAL MỞ BÁN ] Áo Polo LEN THỎ Local Brand Lavi Studio/ ALPHA Nam Nữ Unisex", 1, "Xanh Than", "L", ReceivedStatus, "27/11/2026 11:00:00"),
            O("4", "Áo Polo LEN THỎ Local Brand Lavi Studio/ ALPHA Nam Nữ Unisex - Premium Cao Cấp", 1, "Xanh Than", "L", ReceivedStatus, "27/11/2026 11:00:00"),
            O("5", "Áo Polo LEN THỎ Local Brand Lavi Studio/ ALPHA Nam Nữ Unisex", 1, "Xanh Than", "L - Premium Cao Cấp", ReceivedStatus, "27/11/2026 11:00:00"),
        };
        var rows = ReceivedStock.Build(orders, Now.Date);
        Assert.Equal(2, rows.Count);
        var normal = rows.Single(r => !r.Premium);
        Assert.Equal(("Áo Polo LEN THỎ Local Brand Lavi Studio/ ALPHA Nam Nữ Unisex", 4, "L"), (normal.Product, normal.Units, normal.Size));
        var premium = rows.Single(r => r.Premium);
        Assert.Equal((2, "L"), (premium.Units, premium.Size));
        // gõ tên dài vẫn ra cả đơn chỉ ghi tên ngắn
        Assert.Equal(6, ReceivedStock.Build(orders, Now.Date, "ao polo len tho").Sum(r => r.Units));
        Assert.Equal(2, ReceivedStock.Build(orders, Now.Date, "alpha premium").Sum(r => r.Units));
        // thống kê và tra nhanh cũng gộp
        Assert.Single(ProductStatistics.Compute(orders).Products);
        Assert.Equal(6, Assert.Single(ProductLookup.Search(orders, "polo len", Now)).Received.Total);
    }

    [Fact]
    public void Same_model_code_on_two_product_types_is_not_merged_and_short_name_stays_apart()
    {
        var orders = new[]
        {
            O("1", "Áo Sweater Nỉ Lavi Studio / ZETA", 1), O("2", "Áo Hoodie Zip Lavi Studio/ ZETA", 1), O("3", "ZETA", 1),
            O("4", "Áo Polo Len Lavi Studio/ OMEGA", 1), O("5", "OMEGA", 2), O("6", "Áo Polo Len Lavi Studio/ Omega", 1),
        };
        var f = ProductFamilies.Build(orders);
        Assert.NotEqual(f.Key("Áo Sweater Nỉ Lavi Studio / ZETA"), f.Key("Áo Hoodie Zip Lavi Studio/ ZETA"));
        Assert.Equal("m:zeta", f.Key("ZETA"));                                                  // không đoán gộp vào loại nào
        Assert.Equal(f.Key("Áo Polo Len Lavi Studio/ OMEGA"), f.Key("OMEGA"));                 // chỉ một loại: gộp
        Assert.Equal(f.Key("Áo Polo Len Lavi Studio/ OMEGA"), f.Key("Áo Polo Len Lavi Studio/ Omega"));   // viết thường (TikTok) cũng gộp
        Assert.Equal("Áo Polo Len Lavi Studio/ OMEGA", f.Display(f.Key("OMEGA")));             // hiện tên đầy đủ
        Assert.Equal(4, ProductStatistics.Compute(orders).Products.Count);
    }

    // chủ dự án 28/11/2026: lọc theo cột "Giờ hàng về shop"
    [Fact]
    public void Main_table_filters_by_arrival_at_shop()
    {
        Order A(string id, string arrived) { var o = O(id, "Áo", 1); o.ReturnedAt = arrived; o.ReturnDate = "20/11/2026 08:00:00"; return o; }
        var orders = new[] { A("a", "26/11/2026 09:00:00"), A("b", "10:15:22 27/11/2026"), A("c", ""), A("d", "20/11/2026 09:00:00") };
        var vm = new ReturnOrdersViewModel(new FakeDataService(() => Now), loadInitialData: false);
        vm.LoadOrders(orders, Now);
        Assert.Equal(4, vm.VisibleOrders.Count);
        vm.ArrivedOption = ReturnOrdersViewModel.ArrivedNo;
        Assert.Equal("c", Assert.Single(vm.VisibleOrders).Source.Id);
        vm.ArrivedOption = ReturnOrdersViewModel.ArrivedYes;
        Assert.Equal(3, vm.VisibleOrders.Count);
        vm.ArrivedFrom = new DateTime(2026, 11, 26);
        Assert.Equal(new[] { "a", "b" }, vm.VisibleOrders.Select(r => r.Source.Id).OrderBy(x => x));
        vm.ArrivedTo = new DateTime(2026, 11, 26);
        Assert.Equal("a", Assert.Single(vm.VisibleOrders).Source.Id);
        Assert.Contains("(1)", vm.AdvancedFilterButtonText);
    }

    // bộ lọc gọn (28/11/2026): một ô loại ngày + một ô lịch; "đã về / chưa về shop" nằm trong ô Trạng thái kiện
    [Fact]
    public void Compact_filter_one_date_box_and_arrival_inside_the_status_box()
    {
        Order A(string id, string arrived, string recv) { var o = O(id, "Áo", 1); o.ReturnedAt = arrived; o.RecvDate = recv; o.ReturnDate = "20/11/2026 08:00:00"; return o; }
        var orders = new[] { A("a", "26/11/2026 09:00:00", "27/11/2026 09:00:00"), A("b", "", ""), A("c", "21/11/2026 09:00:00", "") };
        var vm = new ReturnOrdersViewModel(new FakeDataService(() => Now), loadInitialData: false);
        vm.LoadOrders(orders, Now);
        Assert.Contains(ReturnOrdersViewModel.StatusArrivedYes, vm.PlatformStatusOptions);
        vm.PlatformStatusFilter = ReturnOrdersViewModel.StatusArrivedNo;
        Assert.Equal("b", Assert.Single(vm.VisibleOrders).Source.Id);
        vm.PlatformStatusFilter = ReturnOrdersViewModel.StatusArrivedYes;
        Assert.Equal(2, vm.VisibleOrders.Count);
        vm.PlatformStatusFilter = ReturnOrdersViewModel.AllPlatformStatuses;
        vm.DateField = ReturnOrdersViewModel.DateByArrived;
        vm.RangeFrom = new DateTime(2026, 11, 25); vm.RangeTo = new DateTime(2026, 11, 27);
        Assert.Equal("a", Assert.Single(vm.VisibleOrders).Source.Id);
        vm.DateField = ReturnOrdersViewModel.DateByReturn;                       // cùng khoảng ngày, đổi sang ngày hoàn (20/11): không đơn nào
        Assert.Empty(vm.VisibleOrders);
        Assert.Null(vm.ArrivedFrom);
        vm.DateField = ReturnOrdersViewModel.DateByRecv;
        Assert.Equal("a", Assert.Single(vm.VisibleOrders).Source.Id);
        Assert.Equal("📅 25/11 – 27/11/2026", QuanLyHangHoan.App.Views.DateRangePicker.Label(vm.RangeFrom, vm.RangeTo));
    }

    // chủ dự án 28/11/2026: "sản phẩm hoàn nhiều nhất" trang chủ = màn Thống kê (cả kho lưu trữ)
    [Fact]
    public void Home_top_products_count_archive_like_the_statistics_screen()
    {
        var active = new[] { O("1", "Áo A", 1) };
        var archived = new[] { O("2", "Áo B", 5), O("3", "Áo B", 1) };
        var home = new HomeViewModel(new FakeDataService(() => Now));
        var c = home.Compute(active, Now, archived)!;
        Assert.Equal("Áo B", c.Products[0].Name);
        Assert.Equal(6, c.Products[0].Units);
        var stats = new StatisticsViewModel(new FakeDataService(), new FakeDataService(), loadInitialData: false);
        stats.LoadData(active, archived, Now);
        Assert.Equal(stats.ProductRows[0].Name, c.Products[0].Name);
        Assert.Equal(stats.ProductRows[0].Units, c.Products[0].Units);
    }

    private const string ReceivedStatus = ReceiveStatus.Received;

    // ------------------------------------------------------------------ form tạo nhanh
    private static ProductCatalog Catalog() => ProductCatalog.Build(new[]
    {
        O("1", "Áo thun basic", 3, "Đen", "L"), O("2", "Áo thun basic", 1, "Đen", "M"), O("3", "Váy hoa nhí", 1, "Hồng", "S"),
    });

    [Fact]
    public void Form_adds_products_from_search_prefills_single_options_and_builds_the_input()
    {
        var f = new OffPlatformOrderFormViewModel(Catalog());
        Assert.Equal("Salework", f.Channel);                                          // kênh mặc định, đầu danh sách
        Assert.Equal("Salework", f.Channels[0]);
        Assert.Equal(2, f.QuickProducts.Count);
        f.SearchText = "vay";
        Assert.Equal("Váy hoa nhí", Assert.Single(f.Suggestions).Name);
        f.AddFromSearchCommand.Execute(null);
        Assert.Equal("", f.SearchText);
        var row = Assert.Single(f.Items);                                            // điền vào dòng trống đầu
        Assert.Equal(("Váy hoa nhí", "Hồng", "S"), (row.Product, row.Color, row.Size));   // chỉ một màu/size -> điền sẵn
        f.AddProduct(f.QuickProducts[0]);                                              // Áo: một màu (Đen), hai size
        Assert.Equal(2, f.Items.Count);
        Assert.Equal(("Đen", ""), (f.Items[1].Color, f.Items[1].Size));
        Assert.Equal(new[] { "L", "M" }, f.Items[1].SizeOptions);
        f.Items[1].IncreaseCommand.Execute(null);
        f.SearchText = "Mũ len mới";                                                   // sản phẩm chưa có: thêm đúng chữ đã gõ
        f.AddFromSearch();
        Assert.Equal("Mũ len mới", f.Items[2].Product);
        Assert.Equal("3 sản phẩm • 4 cái", f.ItemsSummary);
        f.Channel = "Zalo"; f.OrderType = "Đổi hàng"; f.PriceText = "1,2tr"; f.Code = " ns-abc "; f.OrderId = " SW-001 ";
        var input = f.Build()!;
        Assert.Equal(("ns-abc", OffPlatform.Name, 1_200_000m, "Đổi hàng", "Zalo"), (input.Track, input.Platform, input.Price, input.OrderType, input.Channel));
        Assert.Equal(3, input.Items.Count);
        Assert.Equal("SW-001", input.OrderId);
        Assert.Equal(2, input.Items[1].Qty);
    }

    [Theory]
    [InlineData("350000", 350000)]
    [InlineData("350.000", 350000)]
    [InlineData("350k", 350000)]
    [InlineData("1,5tr", 1500000)]
    [InlineData("", 0)]
    public void Price_text_is_read_in_shop_style(string text, int expected) => Assert.Equal((decimal?)expected, OffPlatformOrderFormViewModel.ParsePrice(text));

    [Fact]
    public void Form_rejects_empty_or_bad_input_with_a_clear_message()
    {
        var f = new OffPlatformOrderFormViewModel();
        Assert.Null(f.Build());
        Assert.Contains("ít nhất một sản phẩm", f.Error);
        f.Items[0].Product = "Áo"; f.Items[0].QtyText = "0";
        Assert.Null(f.Build());
        Assert.Contains("Số lượng", f.Error);
        f.Items[0].QtyText = "2"; f.PriceText = "abc";
        Assert.Null(f.Build());
        Assert.Contains("Giá trị", f.Error);
        f.PriceText = "";
        Assert.NotNull(f.Build());
        Assert.False(f.HasError);
        f.Items[0].Product = ""; f.Items[0].Color = "Đỏ";
        Assert.Null(f.Build());
        Assert.Contains("chưa ghi tên", f.Error);
    }

    [Fact]
    public async Task Table_create_button_opens_the_form_creates_several_orders_and_keeps_data_on_duplicate()
    {
        var data = new FakeDataService(() => Now, enableMutableDemoStore: true);
        var dialog = new FakeNewOrderDialogService();
        var notes = new List<string>();
        var reloads = 0;
        var vm = new ReturnOrdersViewModel(data, ReturnTableScope.OffPlatform, newOrderCreator: new FakeNewOrderCreator(data), newOrderDialog: dialog,
            onDataChangedAsync: () => { reloads++; return Task.CompletedTask; }, onNotify: (m, _) => notes.Add(m), loadInitialData: false,
            productCatalog: Catalog);
        Assert.True(vm.ShowCreateButton);
        Assert.Contains("ngoài sàn", vm.CreateButtonText);
        dialog.OffPlatformScript = async f =>
        {
            Assert.Equal(2, f.QuickProducts.Count);                                    // danh mục từ dữ liệu sẵn có
            f.AddProduct(f.QuickProducts[0]);
            f.Channel = "Tại shop";
            Assert.True(await f.SubmitAsync(another: true));                           // tạo & nhập tiếp: form làm trống, giữ kênh
            Assert.True(f.Items.Single().IsBlank);
            Assert.Equal("Tại shop", f.Channel);
            var firstCode = f.Created[0].Code;
            f.Items[0].Product = "Váy hoa nhí"; f.Code = firstCode;                    // trùng mã: báo lỗi, KHÔNG mất dữ liệu đã nhập
            Assert.False(await f.SubmitAsync(another: false));
            Assert.Contains("ĐÃ CÓ", f.Error);
            Assert.Equal("Váy hoa nhí", f.Items[0].Product);
            f.Code = "";
            Assert.True(await f.SubmitAsync(another: false));
        };
        await vm.CreateNewOrderCommand.ExecuteAsync(null);
        Assert.Equal(1, reloads);                                                       // tải lại một lần khi đóng form
        Assert.Contains("Đã tạo 2 đơn ngoài sàn", notes.Last());
        var created = data.GetReturnOrders(40).Where(OffPlatform.Is).ToList();
        Assert.Equal(2, created.Count);
        Assert.All(created, o => Assert.Equal("Kênh: Tại shop", o.PlatformStatus));
        vm.LoadOrders(data.GetReturnOrders(40), Now);
        Assert.Equal(2, vm.TotalCount);
    }
}
