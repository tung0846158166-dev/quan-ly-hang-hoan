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

    // ------------------------------------------------------------------ form tạo nhanh
    private static ProductCatalog Catalog() => ProductCatalog.Build(new[]
    {
        O("1", "Áo thun basic", 3, "Đen", "L"), O("2", "Áo thun basic", 1, "Đen", "M"), O("3", "Váy hoa nhí", 1, "Hồng", "S"),
    });

    [Fact]
    public void Form_adds_products_from_search_prefills_single_options_and_builds_the_input()
    {
        var f = new OffPlatformOrderFormViewModel(Catalog());
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
        f.Channel = "Zalo"; f.OrderType = "Đổi hàng"; f.PriceText = "1,2tr"; f.Code = " ns-abc ";
        var input = f.Build()!;
        Assert.Equal(("ns-abc", OffPlatform.Name, 1_200_000m, "Đổi hàng", "Zalo"), (input.Track, input.Platform, input.Price, input.OrderType, input.Channel));
        Assert.Equal(3, input.Items.Count);
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
