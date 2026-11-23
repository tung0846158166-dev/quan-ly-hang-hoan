using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Dịch vụ sinh dữ liệu GIẢ 100% để dựng &amp; kiểm thử giao diện WPF (không đọc dữ liệu thật).
/// Hỗ trợ sinh nhanh 50.000+ dòng để kiểm tra virtualization của DataGrid.
/// </summary>
public sealed class FakeDataService :
    IReturnOrderProvider,
    INewOrderProvider,
    IArchivedOrderProvider,
    IDeletedOrderProvider
{
    private readonly Func<DateTime> _clock;
    private readonly bool _enableMutableDemoStore;
    private readonly object _storeLock = new();
    private List<DemoOrderEntry>? _demoStore;

    internal sealed class DemoOrderEntry
    {
        public required Order Order { get; set; }
        public bool IsArchived { get; set; }
        public bool IsDeleted { get; set; }
        public string DeletedFrom { get; set; } = string.Empty;
        public string DeletedAt { get; set; } = string.Empty;
        public List<QuanLyHangHoan.Data.HistoryEntry> History { get; } = new();
    }

    private static readonly string[] Products =
    [
        "Áo Khoác Gió 2 Lớp Demo",
        "Áo Thun Cotton Cổ Tròn Demo",
        "Quần Jeans Ống Suông Demo",
        "Sơ Mi Oxford Dài Tay Demo",
        "Áo Len Cổ Lọ Mùa Đông Demo",
        "Quần Kaki Túi Hộp Demo",
        "Áo Nỉ Hoodie Nỉ Bông Demo",
        "Váy Midi Xếp Ly Demo"
    ];

    private static readonly string[] Colors = ["Đen", "Trắng", "Xám", "Xanh Đen", "Be", "Nâu"];
    private static readonly string[] Sizes = ["S", "M", "L", "XL", "2XL", "29", "30", "31"];
    private static readonly string[] ShopeeCarriers = ["SPX Express", "GHN", "GHTK", "Viettel Post"];
    private static readonly string[] TikTokCarriers = ["J&T Express", "SPX Express", "Best Express", "Ninja Van"];
    private static readonly string[] Reasons =
    [
        "Khách đổi ý / không có nhu cầu",
        "Giao hàng không thành công - khách không nghe máy",
        "Chọn nhầm size, muốn đổi size khác",
        "Hủy sau khi chuẩn bị hàng",
        "Kiện hoàn một phần theo yêu cầu khách"
    ];

    public FakeDataService(Func<DateTime>? clock = null, bool enableMutableDemoStore = false)
    {
        _clock = clock ?? (() => DateTime.Now);
        _enableMutableDemoStore = enableMutableDemoStore;
    }

    public DateTime GetCurrentTime() => _clock();

    internal object StoreLock => _storeLock;

    internal List<DemoOrderEntry> EnsureDemoStore()
    {
        if (_demoStore is not null) return _demoStore;

        var list = new List<DemoOrderEntry>();
        foreach (var o in GenerateReturnOrdersRaw(40))
        {
            list.Add(new DemoOrderEntry { Order = o });
        }
        foreach (var o in GenerateArchivedOrdersRaw())
        {
            list.Add(new DemoOrderEntry { Order = o, IsArchived = true });
        }
        foreach (var d in GenerateDeletedOrdersRaw())
        {
            list.Add(new DemoOrderEntry
            {
                Order = new Order
                {
                    Id = d.Id,
                    OrderId = d.OrderId,
                    Track = d.Track,
                    ReturnTrack = d.ReturnTrack,
                    Platform = d.Platform,
                    Note = d.DeleteReason,
                    Items = d.Items.ToList(),
                    Status = ReceiveStatus.NotReturned
                },
                IsDeleted = true,
                DeletedFrom = d.DeletedFrom,
                DeletedAt = d.DeletedAt
            });
        }

        _demoStore = list;
        return _demoStore;
    }

    public IReadOnlyList<Order> GetReturnOrders(int count = 40)
    {
        if (_enableMutableDemoStore && count == 40)
        {
            lock (_storeLock)
            {
                return EnsureDemoStore()
                    .Where(e => !e.IsArchived && !e.IsDeleted)
                    .Select(e => CloneOrder(e.Order))
                    .ToList();
            }
        }

        return GenerateReturnOrdersRaw(count);
    }

    private IReadOnlyList<Order> GenerateReturnOrdersRaw(int count)
    {
        var now = _clock();
        var list = new List<Order>(Math.Max(count, 0));
        if (count <= 0) return list;

        // 10 bản ghi đầu cố định đại diện đầy đủ 7 cặp (Sàn, Loại đơn) & tình huống nghiệp vụ (kể cả kiện A/B cùng mã đơn gốc)
        var featured = CreateFeaturedFakeOrders(now);
        var featuredCount = Math.Min(count, featured.Count);
        for (var i = 0; i < featuredCount; i++)
        {
            list.Add(featured[i]);
        }

        // Các bản ghi còn lại sinh tất định theo tỉ lệ thực tế, không cấp phát thừa để đạt tốc độ cao với 50.000 dòng
        for (var i = featuredCount; i < count; i++)
        {
            list.Add(CreateGeneratedFakeOrder(i, now));
        }

        return list;
    }

    public Task<IReadOnlyList<Order>> GetReturnOrdersAsync(int count = 40, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GetReturnOrders(count);
        }, cancellationToken);
    }

    public IReadOnlyList<NewOrderRecord> GetNewOrders()
    {
        var now = _clock();
        static string Fmt(DateTime dt) => dt.ToString("dd/MM/yyyy HH:mm:ss");

        return
        [
            new NewOrderRecord
            {
                Id = "new_001",
                ScannedAt = Fmt(now.AddMinutes(-12)),
                Platform = PlatformNames.Shopee,
                Track = "SPXNEW000901",
                Carrier = "SPX Express",
                Note = "Kiện mới quét ở cổng kho - vỏ nguyên vẹn, có ảnh tem",
                PhotoId = "demo_new_photo_01",
                IsSelectedForImport = true
            },
            new NewOrderRecord
            {
                Id = "new_002",
                ScannedAt = Fmt(now.AddMinutes(-28)),
                Platform = PlatformNames.TikTok,
                Track = "TTVNNEW00902",
                Carrier = "J&T Express",
                Note = "Chờ đối chiếu file Salework buổi chiều",
                PhotoId = "demo_new_photo_02",
                IsSelectedForImport = true
            },
            new NewOrderRecord
            {
                Id = "new_003",
                ScannedAt = Fmt(now.AddMinutes(-45)),
                Platform = PlatformNames.Shopee,
                Track = "GHNNEW000903",
                Carrier = "GHN",
                Note = "Vỏ bọc rách nhẹ góc trái, đã chụp ảnh đối chứng",
                PhotoId = "demo_new_photo_03",
                IsSelectedForImport = false
            },
            new NewOrderRecord
            {
                Id = "new_004",
                ScannedAt = Fmt(now.AddHours(-2)),
                Platform = PlatformNames.TikTok,
                Track = "BESTNEW00904",
                Carrier = "Best Express",
                Note = "Áo Khoác Gió 2 Lớp Demo - khách trả không rõ mã đơn",
                PhotoId = "",
                IsSelectedForImport = true
            },
            new NewOrderRecord
            {
                Id = "new_005",
                ScannedAt = Fmt(now.AddHours(-3).AddMinutes(-15)),
                Platform = PlatformNames.Shopee,
                Track = "GHTKNEW00905",
                Carrier = "GHTK",
                Note = "Kiện nhẹ, nghi hoàn 1 phần (áo thun trắng)",
                PhotoId = "demo_new_photo_05",
                IsSelectedForImport = false
            }
        ];
    }

    public IReadOnlyList<Order> GetArchivedOrders()
    {
        if (_enableMutableDemoStore)
        {
            lock (_storeLock)
            {
                return EnsureDemoStore()
                    .Where(e => e.IsArchived && !e.IsDeleted)
                    .Select(e => CloneOrder(e.Order))
                    .ToList();
            }
        }

        return GenerateArchivedOrdersRaw();
    }

    private IReadOnlyList<Order> GenerateArchivedOrdersRaw()
    {
        var now = _clock();
        static string Fmt(DateTime dt) => dt.ToString("dd/MM/yyyy HH:mm");

        var list = new List<Order>(18);
        for (var i = 0; i < 18; i++)
        {
            var num = i + 1;
            var isShopee = i % 2 == 0;
            var platform = isShopee ? PlatformNames.Shopee : PlatformNames.TikTok;
            var carrier = isShopee
                ? ShopeeCarriers[i % ShopeeCarriers.Length]
                : TikTokCarriers[i % TikTokCarriers.Length];
            var recvDt = now.AddHours(-(28 + i * 9));
            var retDt = recvDt.AddDays(-2);
            var shipDt = retDt.AddDays(-3);
            var prod = Products[i % Products.Length];
            var color = Colors[i % Colors.Length];
            var size = Sizes[i % Sizes.Length];
            var qty = 1 + (i % 2);
            var track = isShopee ? $"SPXARC{num:D6}" : $"TTVNARC{num:D6}";
            var orderType = isShopee
                ? (i % 4 == 0 ? OrderTypeNames.Cancelled : OrderTypeNames.ReturnRefund)
                : (i % 3 == 0 ? OrderTypeNames.Lost : OrderTypeNames.ReturnRefund);

            list.Add(new Order
            {
                Id = $"arc_{num:D4}",
                OrderId = isShopee ? $"2611ARC{num:D5}" : $"5789ARC{num:D5}",
                ReturnOrderId = $"ARC-RET-{num:D4}",
                Track = track,
                ReturnTrack = track,
                ForwardTrack = isShopee ? $"SPXFWDARC{num:D4}" : $"JTFWDARC{num:D4}",
                Platform = platform,
                OrderType = orderType,
                PlatformStatus = "Đã hoàn tiền",
                Status = ReceiveStatus.Received,
                Carrier = carrier,
                ShipDate = shipDt.ToString("dd/MM/yyyy"),
                ReturnDate = Fmt(retDt),
                ReturnedAt = Fmt(recvDt.AddHours(-4)),
                RecvDate = Fmt(recvDt),
                Price = 155000m + (i % 8) * 45000m,
                Note = Reasons[i % Reasons.Length],
                UserNote = i % 3 == 0 ? $"Đã đối soát và nhập kệ khu B{(i % 4) + 1}" : "Đã lưu trữ tự động sau 24h",
                PhotoId = i % 2 == 0 ? $"demo_arc_photo_{num}" : "",
                Items = [new OrderItem { Product = prod, Color = color, Size = size, Qty = qty }]
            });
        }

        return list;
    }

    public IReadOnlyList<DeletedOrderRecord> GetDeletedOrders()
    {
        if (_enableMutableDemoStore)
        {
            lock (_storeLock)
            {
                return EnsureDemoStore()
                    .Where(e => e.IsDeleted)
                    .Select(e => new DeletedOrderRecord
                    {
                        Id = e.Order.Id,
                        OrderId = e.Order.OrderId,
                        Track = e.Order.Track,
                        ReturnTrack = e.Order.ReturnTrack,
                        Platform = e.Order.Platform,
                        DeletedFrom = string.IsNullOrWhiteSpace(e.DeletedFrom) ? "Bảng chính" : e.DeletedFrom,
                        DeletedAt = e.DeletedAt,
                        DeleteReason = !string.IsNullOrWhiteSpace(e.Order.UserNote) ? e.Order.UserNote : e.Order.Note,
                        Items = e.Order.Items,
                        FullOrder = CloneOrder(e.Order)
                    })
                    .ToList();
            }
        }

        return GenerateDeletedOrdersRaw();
    }

    private IReadOnlyList<DeletedOrderRecord> GenerateDeletedOrdersRaw()
    {
        var now = _clock();
        static string Fmt(DateTime dt) => dt.ToString("dd/MM/yyyy HH:mm");

        return
        [
            new DeletedOrderRecord
            {
                Id = "del_001",
                OrderId = "2611DEL0001",
                Track = "SPXDEL00011",
                ReturnTrack = "SPXDEL00011",
                Platform = PlatformNames.Shopee,
                DeletedFrom = "Bảng chính",
                DeletedAt = Fmt(now.AddHours(-6)),
                DeleteReason = "Đơn tạo trùng khi nhập tay, giữ ở danh sách chặn nạp lại",
                Items = [new OrderItem { Product = "Áo Thun Cotton Cổ Tròn Demo", Color = "Trắng", Size = "L", Qty = 1 }],
                FullOrder = new Order
                {
                    Id = "del_001",
                    OrderId = "2611DEL0001",
                    Track = "SPXDEL00011",
                    ReturnTrack = "SPXDEL00011",
                    Platform = PlatformNames.Shopee,
                    OrderType = OrderTypeNames.ReturnRefund,
                    Status = ReceiveStatus.NotReturned,
                    Carrier = "SPX Express",
                    Note = "Đơn tạo trùng khi nhập tay, giữ ở danh sách chặn nạp lại",
                    Price = 185000,
                    Items = [new OrderItem { Product = "Áo Thun Cotton Cổ Tròn Demo", Color = "Trắng", Size = "L", Qty = 1 }]
                }
            },
            new DeletedOrderRecord
            {
                Id = "del_002",
                OrderId = "5789DEL0002",
                Track = "TTVNDEL00022",
                ReturnTrack = "TTVNDEL00022",
                Platform = PlatformNames.TikTok,
                DeletedFrom = "Kho lưu trữ",
                DeletedAt = Fmt(now.AddDays(-1).AddHours(-3)),
                DeleteReason = "Đã xử lý ngoài hệ thống, không cần đối soát tiếp",
                Items = [new OrderItem { Product = "Quần Kaki Túi Hộp Demo", Color = "Nâu", Size = "30", Qty = 1 }]
            },
            new DeletedOrderRecord
            {
                Id = "del_003",
                OrderId = "2611DEL0003",
                Track = "GHNDEL00033",
                ReturnTrack = "GHNDEL00033",
                Platform = PlatformNames.Shopee,
                DeletedFrom = "Bảng chính",
                DeletedAt = Fmt(now.AddDays(-2).AddHours(-5)),
                DeleteReason = "Đơn hủy trước khi đóng gói, chưa bàn giao ĐVVC",
                Items = [new OrderItem { Product = "Sơ Mi Oxford Dài Tay Demo", Color = "Xanh Đen", Size = "M", Qty = 2 }]
            },
            new DeletedOrderRecord
            {
                Id = "del_004",
                OrderId = "5789DEL0004",
                Track = "JTDEL00044",
                ReturnTrack = "JTDEL00044",
                Platform = PlatformNames.TikTok,
                DeletedFrom = "Kho lưu trữ",
                DeletedAt = Fmt(now.AddDays(-3)),
                DeleteReason = "Test quét mã thử nghiệm ở kho",
                Items = [new OrderItem { Product = "Áo Len Cổ Lọ Mùa Đông Demo", Color = "Be", Size = "XL", Qty = 1 }]
            }
        ];
    }

    private static List<Order> CreateFeaturedFakeOrders(DateTime now)
    {
        static string Fmt(DateTime dt) => dt.ToString("dd/MM/yyyy HH:mm");

        return
        [
            new Order
            {
                Id = "fake_001",
                OrderId = "2611DEMO0001A",
                ReturnOrderId = "RET-DEMO-1001",
                Track = "SPXFAKE000101",
                ReturnTrack = "SPXFAKE000101",
                ForwardTrack = "SPXFWD900001",
                Platform = PlatformNames.Shopee,
                OrderType = OrderTypeNames.ReturnRefund,
                PlatformStatus = "Đã về shop",
                Status = ReceiveStatus.NotReturned,
                Carrier = "SPX Express",
                ShipDate = Fmt(now.AddDays(-5)),
                ReturnDate = Fmt(now.AddDays(-1)),
                ReturnedAt = Fmt(now.AddHours(-30)), // Đỏ (> 24h)
                RecvDate = "",
                Price = 385000m,
                Note = "Hàng đã về bưu cục shop quá 24h chưa kiểm tra",
                UserNote = "Ưu tiên bóc kiểm quay video",
                PhotoId = "demo_photo_01",
                IsWatchlist = true,
                WatchlistReason = "Đơn giá trị cao, quá 24h",
                Items = [new OrderItem { Product = "Áo Khoác Gió 2 Lớp Demo", Color = "Đen", Size = "XL", Qty = 1 }]
            },
            new Order
            {
                Id = "fake_002",
                OrderId = "2611DEMO0001A", // Kiện B cùng mã đơn gốc -> KHÔNG gộp với kiện A
                ReturnOrderId = "RET-DEMO-1002",
                Track = "SPXFAKE000102",
                ReturnTrack = "SPXFAKE000102",
                ForwardTrack = "SPXFWD900001",
                Platform = PlatformNames.Shopee,
                OrderType = OrderTypeNames.ReturnRefund,
                PlatformStatus = "Đã về shop",
                Status = ReceiveStatus.NotReturned,
                Carrier = "SPX Express",
                ShipDate = Fmt(now.AddDays(-5)),
                ReturnDate = Fmt(now.AddDays(-1)),
                ReturnedAt = Fmt(now.AddHours(-16)), // Vàng (12h–24h)
                RecvDate = "",
                Price = 249000m,
                Note = "Kiện hoàn thứ 2 của cùng đơn gốc (kiện B tách dòng riêng)",
                UserNote = "Nhận kiện A không tự nhận kiện B",
                IsPartialReturn = true,
                // Hoàn một phần: 2 sản phẩm, hoàn 1
                Items =
                [
                    new OrderItem { Product = "Áo Thun Cotton Cổ Tròn Demo", Color = "Trắng", Size = "L", Qty = 2, ReturnedQty = 2 },
                    new OrderItem { Product = "Thắt Lưng Da Demo", Color = "Nâu", Size = "100", Qty = 1 }
                ]
            },
            new Order
            {
                Id = "fake_003",
                OrderId = "5789FAKE000201",
                ReturnOrderId = "TT-RET-2001",
                Track = "TTVNFAKE00201",
                ReturnTrack = "TTVNFAKE00201",
                ForwardTrack = "JT88FAKE00201",
                Platform = PlatformNames.TikTok,
                OrderType = OrderTypeNames.Lost,
                PlatformStatus = "Đã về",
                Status = ReceiveStatus.NotReturned,
                Carrier = "J&T Express",
                ShipDate = Fmt(now.AddDays(-6)),
                ReturnDate = Fmt(now.AddDays(-2)),
                ReturnedAt = Fmt(now.AddHours(-42)), // Đỏ (> 24h)
                RecvDate = "",
                Price = 520000m,
                Note = "Giao thất bại nhiều lần - chuyển hoàn về kho",
                UserNote = "Kiểm tra nguyên niêm phong",
                PhotoId = "demo_photo_03",
                Items =
                [
                    // Hoàn MỘT PHẦN: gửi 3 sản phẩm, khách chỉ hoàn 1 (giữ đủ cả 3, sản phẩm hoàn khác màu)
                    new OrderItem { Product = "Quần Jeans Ống Suông Demo", Color = "Xanh Đen", Size = "31", Qty = 1, ReturnedQty = 1 },
                    new OrderItem { Product = "Sơ Mi Oxford Dài Tay Demo", Color = "Trắng", Size = "L", Qty = 1 },
                    new OrderItem { Product = "Nón Lưỡi Trai Demo", Color = "Đen", Size = "Free", Qty = 1 }
                ],
                IsPartialReturn = true
            },
            new Order
            {
                Id = "fake_004",
                OrderId = "5789FAKE000202",
                ReturnOrderId = "TT-RET-2002",
                Track = "TTVNFAKE00202",
                ReturnTrack = "TTVNFAKE00202",
                ForwardTrack = "JT88FAKE00202",
                Platform = PlatformNames.TikTok,
                OrderType = OrderTypeNames.ReturnRefund,
                PlatformStatus = "Đã về",
                Status = ReceiveStatus.NotReturned,
                Carrier = "J&T Express",
                ShipDate = Fmt(now.AddDays(-4)),
                ReturnDate = Fmt(now.AddDays(-1)),
                ReturnedAt = Fmt(now.AddHours(-14)), // Vàng (12h–24h)
                RecvDate = "",
                Price = 299000m,
                Note = "Chọn nhầm size, muốn đổi size khác",
                UserNote = "",
                Items = [new OrderItem { Product = "Áo Nỉ Hoodie Nỉ Bông Demo", Color = "Xám", Size = "M", Qty = 1 }]
            },
            new Order
            {
                Id = "fake_005",
                OrderId = "2611DEMO0005",
                ReturnOrderId = "RET-DEMO-1005",
                Track = "SPXFAKE000105",
                ReturnTrack = "SPXFAKE000105",
                ForwardTrack = "SPXFWD900005",
                Platform = PlatformNames.Shopee,
                OrderType = OrderTypeNames.FailedDelivery,
                PlatformStatus = "Đã về shop",
                Status = ReceiveStatus.NotReturned,
                Carrier = "GHN",
                ShipDate = Fmt(now.AddDays(-3)),
                ReturnDate = Fmt(now),
                ReturnedAt = Fmt(now.AddHours(-5)), // An toàn (< 12h)
                RecvDate = "",
                Price = 195000m,
                Note = "Giao hàng không thành công - khách không nghe máy",
                UserNote = "",
                Items = [new OrderItem { Product = "Áo Thun Cotton Cổ Tròn Demo", Color = "Đen", Size = "M", Qty = 1 }]
            },
            new Order
            {
                Id = "fake_006",
                OrderId = "2611DEMO0006",
                ReturnOrderId = "RET-DEMO-1006",
                Track = "SPXFAKE000106",
                ReturnTrack = "SPXFAKE000106",
                ForwardTrack = "SPXFWD900006",
                Platform = PlatformNames.Shopee,
                OrderType = OrderTypeNames.ReturnRefund,
                PlatformStatus = "Đã hoàn tiền",
                Status = ReceiveStatus.Received,
                Carrier = "SPX Express",
                ShipDate = Fmt(now.AddDays(-7)),
                ReturnDate = Fmt(now.AddDays(-2)),
                ReturnedAt = Fmt(now.AddHours(-10)),
                RecvDate = Fmt(now.AddHours(-2)), // Đã nhận hôm nay
                Price = 440000m,
                Note = "Khách đổi ý / không có nhu cầu",
                UserNote = "Đã nhập kệ khu A2",
                PhotoId = "demo_photo_06",
                Items = [new OrderItem { Product = "Áo Len Cổ Lọ Mùa Đông Demo", Color = "Be", Size = "L", Qty = 2 }]
            },
            new Order
            {
                Id = "fake_007",
                OrderId = "5789FAKE000207",
                ReturnOrderId = "TT-RET-2007",
                Track = "TTVNFAKE00207",
                ReturnTrack = "TTVNFAKE00207",
                ForwardTrack = "JT88FAKE00207",
                Platform = PlatformNames.TikTok,
                OrderType = OrderTypeNames.FailedDelivery,
                PlatformStatus = "Đang chuyển hoàn",
                Status = ReceiveStatus.NotReturned,
                Carrier = "Best Express",
                ShipDate = Fmt(now.AddDays(-4)),
                ReturnDate = Fmt(now.AddDays(-2)),
                ReturnedAt = "", // Chưa về -> None
                RecvDate = "",
                Price = 310000m,
                Note = "Giao hàng không thành công - khách không nghe máy",
                UserNote = "",
                Items = [new OrderItem { Product = "Quần Kaki Túi Hộp Demo", Color = "Nâu", Size = "30", Qty = 1 }]
            },
            new Order
            {
                Id = "fake_008",
                OrderId = "5789FAKE000208",
                ReturnOrderId = "TT-RET-2008",
                Track = "TTVNFAKE00208",
                ReturnTrack = "TTVNFAKE00208",
                ForwardTrack = "JT88FAKE00208",
                Platform = PlatformNames.TikTok,
                OrderType = OrderTypeNames.RefundOnly,
                PlatformStatus = "Đã hoàn tiền",
                Status = ReceiveStatus.NotNeeded,
                Carrier = "J&T Express",
                ShipDate = Fmt(now.AddDays(-5)),
                ReturnDate = Fmt(now.AddDays(-3)),
                ReturnedAt = "",
                RecvDate = "",
                Price = 175000m,
                Note = "Chỉ hoàn tiền một phần theo thỏa thuận",
                UserNote = "Không cần nhận hàng về kho",
                Items = [new OrderItem { Product = "Váy Midi Xếp Ly Demo", Color = "Đen", Size = "M", Qty = 1 }]
            },
            new Order
            {
                Id = "fake_009",
                OrderId = "2611CXL0009",
                ReturnOrderId = "",
                Track = "SPXCXL000109",
                ReturnTrack = "SPXCXL000109",
                ForwardTrack = "SPXFWD900009",
                Platform = PlatformNames.Shopee,
                OrderType = OrderTypeNames.Cancelled,
                PlatformStatus = "Đã hủy",
                Status = ReceiveStatus.NotReturned,
                Carrier = "SPX Express",
                ShipDate = Fmt(now.AddDays(-4)),
                ReturnDate = Fmt(now.AddDays(-2)),
                ReturnedAt = Fmt(now.AddHours(-28)), // Đơn hủy Shopee quá 24h (Đỏ)
                RecvDate = "",
                Price = 345000m,
                Note = "Hủy sau khi chuẩn bị hàng",
                UserNote = "Chờ shipper hoàn trả về kho",
                Items = [new OrderItem { Product = "Áo Khoác Gió 2 Lớp Demo", Color = "Xanh Đen", Size = "L", Qty = 1 }]
            },
            new Order
            {
                Id = "fake_010",
                OrderId = "2611CXL0010",
                ReturnOrderId = "",
                Track = "GHNCXL000110",
                ReturnTrack = "GHNCXL000110",
                ForwardTrack = "GHNFWD900010",
                Platform = PlatformNames.Shopee,
                OrderType = OrderTypeNames.Cancelled,
                PlatformStatus = "Đã hủy",
                Status = ReceiveStatus.Received,
                Carrier = "GHN",
                ShipDate = Fmt(now.AddDays(-3)),
                ReturnDate = Fmt(now.AddDays(-1)),
                ReturnedAt = Fmt(now.AddHours(-9)),
                RecvDate = Fmt(now.AddHours(-1)), // Đã nhận hôm nay
                Price = 260000m,
                Note = "Hủy sau khi chuẩn bị hàng",
                UserNote = "Đã kiểm nhận đủ hàng hủy",
                Items = [new OrderItem { Product = "Sơ Mi Oxford Dài Tay Demo", Color = "Trắng", Size = "L", Qty = 1 }]
            }
        ];
    }

    private static Order CreateGeneratedFakeOrder(int index, DateTime now)
    {
        // Tỉ lệ mô phỏng theo phân bố thực tế:
        // Shopee Đơn hủy (~45%), Shopee Trả hàng/Hoàn tiền (~22%), Shopee Giao không thành công (~1%),
        // TikTok THẤT THOÁT (~19%), TikTok Trả hàng/Hoàn tiền (~11%), TikTok Giao không thành công (~1%), TikTok Chỉ hoàn tiền (~1%)
        var slot = index % 100;
        string platform;
        string orderType;

        if (slot < 45)
        {
            platform = PlatformNames.Shopee;
            orderType = OrderTypeNames.Cancelled;
        }
        else if (slot < 67)
        {
            platform = PlatformNames.Shopee;
            orderType = OrderTypeNames.ReturnRefund;
        }
        else if (slot == 67)
        {
            platform = PlatformNames.Shopee;
            orderType = OrderTypeNames.FailedDelivery;
        }
        else if (slot < 87)
        {
            platform = PlatformNames.TikTok;
            orderType = OrderTypeNames.Lost;
        }
        else if (slot < 98)
        {
            platform = PlatformNames.TikTok;
            orderType = OrderTypeNames.ReturnRefund;
        }
        else if (slot == 98)
        {
            platform = PlatformNames.TikTok;
            orderType = OrderTypeNames.FailedDelivery;
        }
        else
        {
            platform = PlatformNames.TikTok;
            orderType = OrderTypeNames.RefundOnly;
        }

        var isShopee = platform == PlatformNames.Shopee;
        var carrier = isShopee
            ? ShopeeCarriers[index % ShopeeCarriers.Length]
            : TikTokCarriers[index % TikTokCarriers.Length];

        var num = index + 1;
        var orderId = isShopee ? $"2611DEMO{num:D6}" : $"5789FAKE{num:D6}";
        var returnTrack = isShopee ? $"SPXFAKE{num:D7}" : $"TTVNFAKE{num:D6}";
        var forwardTrack = isShopee ? $"SPXFWD{num:D7}" : $"JT88FAKE{num:D6}";

        var bucket = index % 20;
        string status;
        string platformStatus;
        string returnedAt = "";
        string recvDate = "";

        if (orderType == OrderTypeNames.RefundOnly)
        {
            status = ReceiveStatus.NotNeeded;
            platformStatus = "Chỉ hoàn tiền";
        }
        else if (bucket < 3)
        {
            status = ReceiveStatus.NotReturned;
            platformStatus = isShopee ? "Đã về shop" : "Đã về";
            returnedAt = now.AddHours(-(26 + (index % 48))).ToString("dd/MM/yyyy HH:mm");
        }
        else if (bucket < 6)
        {
            status = ReceiveStatus.NotReturned;
            platformStatus = isShopee ? "Đã về shop" : "Đã về";
            returnedAt = now.AddHours(-(13 + (index % 10))).ToString("dd/MM/yyyy HH:mm");
        }
        else if (bucket < 10)
        {
            status = ReceiveStatus.NotReturned;
            platformStatus = isShopee ? "Đã về shop" : "Đã về";
            returnedAt = now.AddHours(-(1 + (index % 10))).ToString("dd/MM/yyyy HH:mm");
        }
        else if (bucket < 15)
        {
            status = ReceiveStatus.Received;
            platformStatus = "Đã hoàn tiền";
            var recShiftHours = index % 3 == 0 ? 1 + (index % 4) : 26 + (index % 48);
            returnedAt = now.AddHours(-(recShiftHours + 6)).ToString("dd/MM/yyyy HH:mm");
            recvDate = now.AddHours(-recShiftHours).ToString("dd/MM/yyyy HH:mm");
        }
        else if (bucket < 18)
        {
            status = ReceiveStatus.NotReturned;
            platformStatus = "Đang chuyển hoàn";
        }
        else if (bucket == 18)
        {
            status = ReceiveStatus.Lost;
            platformStatus = "Khiếu nại ĐVVC";
            returnedAt = now.AddDays(-4).ToString("dd/MM/yyyy HH:mm");
        }
        else
        {
            status = ReceiveStatus.NotNeeded;
            platformStatus = "Đã hủy trước gửi";
        }

        var dayOffset = index % 7;
        var returnDt = now.AddDays(-dayOffset).AddHours(-(index % 8));
        var shipDt = returnDt.AddDays(-(2 + (index % 3)));
        var prod = Products[index % Products.Length];
        var color = Colors[index % Colors.Length];
        var size = Sizes[index % Sizes.Length];
        var qty = 1 + (index % 3);

        return new Order
        {
            Id = $"fake_{num:D6}",
            OrderId = orderId,
            ReturnOrderId = orderType == OrderTypeNames.Cancelled ? "" : $"RET-{num:D6}",
            Track = returnTrack,
            ReturnTrack = bucket == 19 ? "" : returnTrack,
            ForwardTrack = forwardTrack,
            Platform = platform,
            OrderType = orderType,
            PlatformStatus = platformStatus,
            Status = status,
            Carrier = carrier,
            ShipDate = shipDt.ToString("dd/MM/yyyy"),
            ReturnDate = returnDt.ToString("dd/MM/yyyy HH:mm"),
            ReturnedAt = returnedAt,
            RecvDate = recvDate,
            Price = 120000m + (index % 15) * 35000m,
            Note = Reasons[index % Reasons.Length],
            UserNote = index % 5 == 0 ? $"Ghi chú giả #{num}" : "",
            PhotoId = index % 4 == 0 ? $"demo_photo_{num}" : "",
            IsPartialReturn = index % 9 == 0,
            IsWatchlist = index % 11 == 0,
            Items =
            [
                new OrderItem { Product = prod, Color = color, Size = size, Qty = qty }
            ]
        };
    }

    public PlaceholderScreenData GetPlaceholderScreenData(string screenKey)
    {
        return screenKey switch
        {
            "quick_create" => new PlaceholderScreenData(
                "Tạo đơn nhanh hàng loạt",
                "Dán danh sách mã đơn / mã vận đơn mỗi dòng 1 mã, tự lọc trùng với bảng chính rồi đẩy ra ngoài.",
                "Khung dữ liệu giả",
                ["STT", "Mã đơn / MVĐ", "Sàn TMĐT", "Loại đơn", "Chống trùng"],
                [
                    new("1", "2611QUICK0001", PlatformNames.Shopee, OrderTypeNames.ReturnRefund, "Đơn mới (Chờ về)"),
                    new("2", "TTVNQUICK0002", PlatformNames.TikTok, OrderTypeNames.FailedDelivery, "Đơn mới (Chờ về)"),
                    new("3", "SPXFAKE000101", PlatformNames.Shopee, OrderTypeNames.ReturnRefund, "Đã có trên bảng (Tự bỏ tích)")
                ],
                "Sẽ bổ sung ô dán nhiều dòng và kiểm tra trùng trực tiếp qua TrackingResolver của Core."),

            "scan_session" => new PlaceholderScreenData(
                "Ca quét nhận kiện (Biên bản Shipper)",
                "Tích lũy các mã vận đơn đã quét trong ca giao nhận với shipper để lập biên bản nhanh.",
                "Ca giả: 3 kiện",
                ["#", "Mã vận đơn đã quét", "Giờ quét", "Sản phẩm khớp", "Kết quả"],
                [
                    new("3", "SPXFAKE000106", "09:24:11", "Áo Len Cổ Lọ Mùa Đông Demo (Be/L)", "Hợp lệ"),
                    new("2", "TTVNFAKE00201", "09:23:42", "Quần Jeans Ống Suông Demo (Xanh Đen/31)", "Hợp lệ"),
                    new("1", "SPXFAKE000106", "09:23:59", "Áo Len Cổ Lọ Mùa Đông Demo (Be/L)", "Trùng lặp (không cộng)")
                ],
                "Sẽ nối súng quét mã vạch USB & luồng SyncTransport từ điện thoại, hỗ trợ sao chép biên bản."),

            "catalog" => new PlaceholderScreenData(
                "Danh mục sản phẩm & Size của shop",
                "Khai báo sẵn Tên SP, Màu sắc và Size riêng cho từng mẫu để gợi ý nhanh và đối soát tồn kho.",
                "4 mẫu SP giả",
                ["STT", "Tên sản phẩm", "Các màu sắc", "Các size", "Ghi chú"],
                [
                    new("1", "Áo Khoác Gió 2 Lớp Demo", "Đen, Xanh Đen, Xám", "M, L, XL, 2XL", "Áo khoác"),
                    new("2", "Áo Thun Cotton Cổ Tròn Demo", "Đen, Trắng, Be", "S, M, L, XL", "Áo thun"),
                    new("3", "Quần Jeans Ống Suông Demo", "Xanh Đen, Xanh Nhạt, Đen", "28, 29, 30, 31, 32", "Quần size số"),
                    new("4", "Sơ Mi Oxford Dài Tay Demo", "Trắng, Xanh Nhạt", "M, L, XL", "Sơ mi")
                ],
                "Sẽ nối bảng Catalog trong SQLite + nút tự gom sản phẩm từ danh sách đơn."),

            _ => new PlaceholderScreenData(
                "Cài đặt hệ thống",
                "Cấu hình ngưỡng cảnh báo, mã phòng đồng bộ điện thoại, nhập dữ liệu cũ và sao lưu ổ cứng.",
                "Bản khung v2 (WPF)",
                ["Hạng mục", "Giá trị hiện tại (Giả)", "Nguồn cấu hình", "Trạng thái", "Ghi chú"],
                [
                    new("Cảnh báo Vàng", $"{AlertThresholds.YellowHours:0} giờ kể từ lúc hàng về", "AlertThresholds.YellowHours", "Đang áp dụng", "Đặt tại một nơi duy nhất, dễ đổi"),
                    new("Cảnh báo Đỏ", $"{AlertThresholds.RedHours:0} giờ kể từ lúc hàng về", "AlertThresholds.RedHours", "Đang áp dụng", "Đặt tại một nơi duy nhất, dễ đổi"),
                    new("Đồng bộ điện thoại", "Mã phòng giả: SHP-DEMO26", "SyncTransport (Phương án A)", "Chờ nối ở Giai đoạn 4", "MQTT + ntfy, persistence-before-ACK"),
                    new("Nhập dữ liệu app cũ", "Chưa chạy (sẽ có chế độ Dry-run)", "QuanLyHangHoan.Data Importer", "Chờ nối ở Giai đoạn 2", "Chỉ đọc bản sao, không đụng thư mục gốc")
                ],
                "Cài đặt sẽ lưu vào SQLite/cấu hình cục bộ và nối nút Nhập dữ liệu cũ (Dry-run -> Xác nhận -> Nhập).")
        };
    }

    internal static Order CloneOrder(Order o) => new()
    {
        Id = o.Id,
        OrderId = o.OrderId,
        ReturnOrderId = o.ReturnOrderId,
        Track = o.Track,
        ReturnTrack = o.ReturnTrack,
        ForwardTrack = o.ForwardTrack,
        AltTrack = o.AltTrack,
        TrackingCodes = o.TrackingCodes,
        Platform = o.Platform,
        OrderType = o.OrderType,
        PlatformStatus = o.PlatformStatus,
        Status = o.Status,
        Carrier = o.Carrier,
        ShipDate = o.ShipDate,
        ReturnDate = o.ReturnDate,
        ReturnedAt = o.ReturnedAt,
        RecvDate = o.RecvDate,
        Price = o.Price,
        Note = o.Note,
        BuyerNote = o.BuyerNote,
        UserNote = o.UserNote,
        Deadline = o.Deadline,
        PhotoId = o.PhotoId,
        IsWatchlist = o.IsWatchlist,
        WatchlistReason = o.WatchlistReason,
        IsPartialReturn = o.IsPartialReturn,
        Items = o.Items.ToList()
    };
}
