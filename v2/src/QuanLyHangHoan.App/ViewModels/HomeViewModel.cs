using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.App.ViewModels;

public sealed record PlatformKpiSummary(
    string PlatformName,
    int TotalCount,
    int PendingCount,
    int ReceivedTodayCount,
    int YellowAlertCount,
    int RedAlertCount,
    decimal PendingTotalValue,
    string PendingTotalValueFormatted);

public sealed record HomeProductItem(int Rank, string Name, int Units, int Orders, string ShareText, double BarWidth, string PremiumText);

public sealed record DailyTrendPoint(
    DateTime Date,
    string DateLabel,
    int ShopeeCount,
    int TikTokCount,
    double ShopeeBarHeight,
    double TikTokBarHeight)
{
    public int TotalCount => ShopeeCount + TikTokCount;
}

public sealed record OrderTypeBarItem(
    string Platform,
    string OrderType,
    int Count,
    string PercentText,
    double BarWidth,
    string AccentHex);

public sealed record ReasonBarItem(
    string Reason,
    int Count,
    string PercentText,
    double BarWidth);

public sealed record UrgentOrderItem(
    string OrderId,
    string Track,
    string Platform,
    string OrderType,
    string TargetNavKey,
    AlertLevel AlertLevel,
    string AlertBadgeText,
    string PriceFormatted,
    string Note,
    double ElapsedHours);

public sealed partial class HomeViewModel : ObservableObject
{
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IReturnOrderProvider _provider;
    private readonly IFileSaveService? _fileSaveService;
    private readonly AlertPolicy _alertPolicy;
    private readonly Action<UrgentOrderItem>? _onOpenOrder;
    private readonly Action? _onNavigateLegacyImport;

    private IReadOnlyList<Order> _orders = [];
    // "Sản phẩm hoàn nhiều nhất" tính trên bảng chính + KHO LƯU TRỮ, giống màn Thống kê (chủ dự án 28/11/2026: hai nơi lệch nhau
    // vì đơn đã nhận tự vào lưu trữ). Danh sách lưu trữ do MainViewModel đưa vào.
    private IReadOnlyList<Order> _archivedForProducts = [];
    public void SetArchivedForProducts(IReadOnlyList<Order> archived) => _archivedForProducts = archived;
    private IReadOnlyList<Order> ProductSource(IReadOnlyList<Order> active, IReadOnlyList<Order>? archived = null)
    {
        var a = archived ?? _archivedForProducts;
        return a.Count == 0 ? active : active.Concat(a).ToList();
    }
    private DateTime _snapshotTime;

    public HomeViewModel(
        IReturnOrderProvider provider,
        IFileSaveService? fileSaveService = null,
        AlertPolicy? alertPolicy = null,
        Action<UrgentOrderItem>? onOpenOrder = null,
        Action? onNavigateLegacyImport = null)
    {
        _provider = provider;
        _fileSaveService = fileSaveService;
        _alertPolicy = alertPolicy ?? AlertThresholds.MainPolicy;
        _onOpenOrder = onOpenOrder;
        _onNavigateLegacyImport = onNavigateLegacyImport;

        var now = _provider.GetCurrentTime();
        _snapshotTime = now;
        _reportFromDate = now.Date.AddDays(-7);
        _reportToDate = now.Date;
    }

    [ObservableProperty]
    private bool _isDemoMode;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private int _totalOrders;

    [ObservableProperty]
    private PlatformKpiSummary _overallKpi = EmptyKpi("Tổng toàn shop");

    [ObservableProperty]
    private PlatformKpiSummary _shopeeKpi = EmptyKpi(PlatformNames.Shopee);

    [ObservableProperty]
    private PlatformKpiSummary _tikTokKpi = EmptyKpi(PlatformNames.TikTok);

    [ObservableProperty]
    private IReadOnlyList<DailyTrendPoint> _dailyTrendPoints = Array.Empty<DailyTrendPoint>();

    [ObservableProperty]
    private IReadOnlyList<OrderTypeBarItem> _orderTypeBars = Array.Empty<OrderTypeBarItem>();

    [ObservableProperty]
    private IReadOnlyList<ReasonBarItem> _topReasons = Array.Empty<ReasonBarItem>();

    [ObservableProperty]
    private IReadOnlyList<UrgentOrderItem> _urgentItems = Array.Empty<UrgentOrderItem>();

    [ObservableProperty]
    private IReadOnlyList<HomeProductItem> _topProducts = Array.Empty<HomeProductItem>();

    [ObservableProperty]
    private string _productSummaryText = "Chưa có sản phẩm hoàn nào.";

    public bool HasTopProducts => TopProducts.Count > 0;
    partial void OnTopProductsChanged(IReadOnlyList<HomeProductItem> value) => OnPropertyChanged(nameof(HasTopProducts));

    public IReadOnlyList<(string Key, string Label)> ProductPeriods { get; } =
        [("day", "Hôm nay"), ("week", "Tuần này"), ("month", "Tháng này"), ("7d", "7 ngày"), ("30d", "30 ngày"), ("all", "Tất cả")];

    [ObservableProperty]
    private string _productPeriod = "all";

    public string ProductPeriodLabel => ProductPeriods.First(p => p.Key == ProductPeriod).Label;

    [RelayCommand]
    private void SetProductPeriod(string? key)
    {
        ProductPeriod = string.IsNullOrWhiteSpace(key) ? "all" : key;
    }

    partial void OnProductPeriodChanged(string value)
    {
        OnPropertyChanged(nameof(ProductPeriodLabel));
        OnPropertyChanged(nameof(IsPeriodDay)); OnPropertyChanged(nameof(IsPeriodWeek)); OnPropertyChanged(nameof(IsPeriodMonth));
        OnPropertyChanged(nameof(IsPeriod7)); OnPropertyChanged(nameof(IsPeriod30)); OnPropertyChanged(nameof(IsPeriodAll));
        BuildTopProducts(ProductSource(_orders));
    }

    public bool IsPeriodDay => ProductPeriod == "day";
    public bool IsPeriodWeek => ProductPeriod == "week";
    public bool IsPeriodMonth => ProductPeriod == "month";
    public bool IsPeriod7 => ProductPeriod == "7d";
    public bool IsPeriod30 => ProductPeriod == "30d";
    public bool IsPeriodAll => ProductPeriod == "all";

    /// <summary>Khoảng ngày [từ, đến] của kỳ đã chọn (theo ngày hoàn; thiếu thì ngày hàng về, rồi ngày giao).</summary>
    public static (DateTime From, DateTime To)? PeriodRange(string key, DateTime now)
    {
        var today = now.Date;
        return key switch
        {
            "day" => (today, today),
            "week" => (today.AddDays(-(((int)today.DayOfWeek + 6) % 7)), today),
            "month" => (new DateTime(today.Year, today.Month, 1), today),
            "7d" => (today.AddDays(-6), today),
            "30d" => (today.AddDays(-29), today),
            _ => null
        };
    }

    public static IReadOnlyList<Order> FilterByPeriod(IReadOnlyList<Order> orders, string key, DateTime now)
    {
        var range = PeriodRange(key, now);
        if (range is null) return orders;
        return orders.Where(o =>
        {
            var d = AlertThresholds.TryParseVietnamDateTime(o.ReturnDate)
                    ?? AlertThresholds.TryParseVietnamDateTime(o.ReturnedAt)
                    ?? AlertThresholds.TryParseVietnamDateTime(o.ShipDate);
            return d is not null && d.Value.Date >= range.Value.From && d.Value.Date <= range.Value.To;
        }).ToList();
    }

    private void BuildTopProducts(IReadOnlyList<Order> allOrders)
    {
        var (items, text) = ComputeTopProducts(allOrders, _snapshotTime == default ? DateTime.Now : _snapshotTime);
        TopProducts = items;
        ProductSummaryText = text;
    }

    /// <summary>Phần tính toán thuần (không đụng giao diện) của "sản phẩm hoàn nhiều nhất": chạy được ở luồng nền.</summary>
    private (IReadOnlyList<HomeProductItem> Items, string Text) ComputeTopProducts(IReadOnlyList<Order> allOrders, DateTime now)
    {
        var orders = FilterByPeriod(allOrders, ProductPeriod, now);
        var r = QuanLyHangHoan.Core.Stats.ProductStatistics.Compute(orders);
        if (r.Products.Count == 0) return (Array.Empty<HomeProductItem>(), $"Chưa có sản phẩm hoàn nào ({ProductPeriodLabel.ToLowerInvariant()}).");
        var top = r.Products.Take(6).ToList();
        var max = Math.Max(1, top[0].Units);
        var items = top.Select((p, i) => new HomeProductItem(
            i + 1, p.Name, p.Units, p.Orders, p.Share.ToString("P1", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")),
            Math.Max(8, p.Units * 150.0 / max),
            p.PremiumUnits > 0 ? $"Premium {p.PremiumUnits:N0} • Thường {p.StandardUnits:N0}" : "")).ToList();
        return (items, $"{ProductPeriodLabel}: {r.Products.Count:N0} sản phẩm • {r.TotalUnits:N0} cái bị hoàn trong {r.OrdersWithItems:N0} đơn");
    }

    /// <summary>Kết quả tính sẵn của trang chủ (tính ở luồng nền, áp lên giao diện bằng <see cref="ApplyComputed"/>).</summary>
    public sealed record HomeComputed(
        IReadOnlyList<Order> Orders, DateTime Now,
        PlatformKpiSummary Overall, PlatformKpiSummary Shopee, PlatformKpiSummary TikTok,
        IReadOnlyList<DailyTrendPoint> Trend, IReadOnlyList<OrderTypeBarItem> Bars, IReadOnlyList<ReasonBarItem> Reasons,
        IReadOnlyList<UrgentOrderItem> Urgent, IReadOnlyList<HomeProductItem> Products, string ProductText);

    /// <summary>Tính mọi số liệu trang chủ KHÔNG đụng giao diện (gọi được từ luồng nền). Danh sách rỗng trả null (dùng <see cref="LoadData"/>).</summary>
    public HomeComputed? Compute(IReadOnlyList<Order> orders, DateTime now, IReadOnlyList<Order>? archived = null)
    {
        if (orders.Count == 0) return null;
        var (items, text) = ComputeTopProducts(ProductSource(orders, archived), now);
        return new HomeComputed(orders, now,
            ComputeKpi("Tổng toàn shop", orders, now),
            ComputeKpi(PlatformNames.Shopee, orders.Where(o => o.Platform.Equals(PlatformNames.Shopee, StringComparison.OrdinalIgnoreCase)).ToList(), now),
            ComputeKpi(PlatformNames.TikTok, orders.Where(o => o.Platform.Equals(PlatformNames.TikTok, StringComparison.OrdinalIgnoreCase)).ToList(), now),
            BuildDailyTrend(orders, now, days: 7), BuildOrderTypeBars(orders), BuildTopReasons(orders, topN: 5), BuildUrgentItems(orders, now),
            items, text);
    }

    /// <summary>Áp kết quả <see cref="Compute"/> lên giao diện (nhanh, chạy ở luồng giao diện). null = danh sách rỗng.</summary>
    public void ApplyComputed(HomeComputed? c, IReadOnlyList<Order> orders, DateTime now)
    {
        if (c is null) { LoadData(orders, now); return; }
        _orders = c.Orders;
        _snapshotTime = c.Now;
        TotalOrders = c.Orders.Count;
        IsEmpty = false;
        LastUpdatedText = $"Cập nhật lúc {c.Now:dd/MM/yyyy HH:mm} • Ngưỡng cảnh báo: Vàng > {AlertThresholds.YellowHours:0}h / Đỏ > {AlertThresholds.RedHours:0}h";
        OverallKpi = c.Overall; ShopeeKpi = c.Shopee; TikTokKpi = c.TikTok;
        DailyTrendPoints = c.Trend; OrderTypeBars = c.Bars; TopReasons = c.Reasons; UrgentItems = c.Urgent;
        TopProducts = c.Products; ProductSummaryText = c.ProductText;
    }

    [ObservableProperty]
    private DateTime? _reportFromDate;

    [ObservableProperty]
    private DateTime? _reportToDate;

    [ObservableProperty]
    private string _reportPlatformFilter = "all";

    [ObservableProperty]
    private string _exportStatusMessage = string.Empty;

    [ObservableProperty]
    private string _lastUpdatedText = string.Empty;

    [ObservableProperty]
    private int _missingPhotoCount;

    [ObservableProperty]
    private int _returnWatchOnTheWayCount;

    public bool HasMissingPhotos => MissingPhotoCount > 0;
    public string MissingPhotoText => $"Ảnh mất: {MissingPhotoCount:N0}";

    partial void OnMissingPhotoCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasMissingPhotos));
        OnPropertyChanged(nameof(MissingPhotoText));
    }

    public bool HasUrgentItems => UrgentItems.Count > 0;
    public bool IsReportPlatformAll => ReportPlatformFilter == "all";
    public bool IsReportPlatformShopee => ReportPlatformFilter == PlatformNames.Shopee;
    public bool IsReportPlatformTikTok => ReportPlatformFilter == PlatformNames.TikTok;

    partial void OnUrgentItemsChanged(IReadOnlyList<UrgentOrderItem> value)
    {
        OnPropertyChanged(nameof(HasUrgentItems));
    }

    partial void OnReportPlatformFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsReportPlatformAll));
        OnPropertyChanged(nameof(IsReportPlatformShopee));
        OnPropertyChanged(nameof(IsReportPlatformTikTok));
    }

    public async Task InitializeAsync(int count = 40, CancellationToken cancellationToken = default)
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            var now = _provider.GetCurrentTime();
            var orders = await _provider.GetReturnOrdersAsync(count, cancellationToken);
            LoadData(orders, now);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public Task RefreshAsync() => InitializeAsync();

    [RelayCommand]
    private void SetReportPlatformFilter(string platform)
    {
        ReportPlatformFilter = string.IsNullOrWhiteSpace(platform) ? "all" : platform;
    }

    [RelayCommand]
    private void OpenUrgentOrder(UrgentOrderItem? item)
    {
        if (item is null) return;
        _onOpenOrder?.Invoke(item);
    }

    // ---- Khiếu nại gần nhất (chủ dự án 27/11/2026): 3 đơn mới nhất + nút mở bảng Khiếu nại; bấm từng đơn để xem chi tiết
    [ObservableProperty]
    private IReadOnlyList<ComplaintRowViewModel> _recentComplaints = Array.Empty<ComplaintRowViewModel>();
    [ObservableProperty]
    private string _complaintSummaryText = "";
    public bool HasRecentComplaints => RecentComplaints.Count > 0;
    partial void OnRecentComplaintsChanged(IReadOnlyList<ComplaintRowViewModel> value) => OnPropertyChanged(nameof(HasRecentComplaints));

    /// <summary>Mở bảng Khiếu nại (MainViewModel gắn).</summary>
    public Action? OpenComplaintsTable { get; set; }
    /// <summary>Mở chi tiết một đơn (MainViewModel gắn).</summary>
    public Action<Order>? OpenOrderDetail { get; set; }

    public void SetRecentComplaints(IReadOnlyList<ComplaintRowViewModel> recent, int processing, int total)
    {
        RecentComplaints = recent;
        ComplaintSummaryText = total == 0 ? "" : $"{processing:N0} đang xử lý / {total:N0} đơn";
    }

    [RelayCommand]
    private void OpenComplaints() => OpenComplaintsTable?.Invoke();

    [RelayCommand]
    private void OpenComplaint(ComplaintRowViewModel? row)
    {
        if (row is not null) OpenOrderDetail?.Invoke(row.Source);
    }

    [RelayCommand]
    private void NavigateToLegacyImport()
    {
        _onNavigateLegacyImport?.Invoke();
    }

    [RelayCommand]
    private void ClearDataPreview()
    {
        LoadData(Array.Empty<Order>(), _provider.GetCurrentTime());
    }

    [RelayCommand]
    public async Task ExportCsvAsync()
    {
        var csv = BuildCsvSummary();
        if (_fileSaveService is null)
        {
            ExportStatusMessage = "Đã tạo dữ liệu CSV trong bộ nhớ.";
            return;
        }

        var defaultName = $"TomTat_HangHoan_{_snapshotTime:yyyyMMdd_HHmm}.csv";
        var path = _fileSaveService.PickSavePath("Xuất tóm tắt báo cáo (CSV)", defaultName, "CSV (*.csv)|*.csv");
        if (string.IsNullOrWhiteSpace(path)) return;

        await _fileSaveService.WriteTextAsync(path, csv);
        ExportStatusMessage = $"Đã xuất CSV: {path}";
    }

    public void LoadData(IReadOnlyList<Order> orders, DateTime now)
    {
        _orders = orders;
        _snapshotTime = now;
        TotalOrders = orders.Count;
        IsEmpty = orders.Count == 0;
        LastUpdatedText = $"Cập nhật lúc {now:dd/MM/yyyy HH:mm} • Ngưỡng cảnh báo: Vàng > {AlertThresholds.YellowHours:0}h / Đỏ > {AlertThresholds.RedHours:0}h";

        if (IsEmpty)
        {
            OverallKpi = EmptyKpi("Tổng toàn shop");
            ShopeeKpi = EmptyKpi(PlatformNames.Shopee);
            TikTokKpi = EmptyKpi(PlatformNames.TikTok);
            DailyTrendPoints = Array.Empty<DailyTrendPoint>();
            OrderTypeBars = Array.Empty<OrderTypeBarItem>();
            TopReasons = Array.Empty<ReasonBarItem>();
            UrgentItems = Array.Empty<UrgentOrderItem>();
            BuildTopProducts(ProductSource(orders));
            return;
        }

        OverallKpi = ComputeKpi("Tổng toàn shop", orders, now);
        ShopeeKpi = ComputeKpi(PlatformNames.Shopee, orders.Where(o => o.Platform.Equals(PlatformNames.Shopee, StringComparison.OrdinalIgnoreCase)).ToList(), now);
        TikTokKpi = ComputeKpi(PlatformNames.TikTok, orders.Where(o => o.Platform.Equals(PlatformNames.TikTok, StringComparison.OrdinalIgnoreCase)).ToList(), now);

        DailyTrendPoints = BuildDailyTrend(orders, now, days: 7);
        OrderTypeBars = BuildOrderTypeBars(orders);
        TopReasons = BuildTopReasons(orders, topN: 5);
        UrgentItems = BuildUrgentItems(orders, now);
        BuildTopProducts(ProductSource(orders));
    }

    public string BuildCsvSummary()
    {
        var filtered = FilterOrdersForReport(_orders, ReportFromDate, ReportToDate, ReportPlatformFilter);
        var kpi = ComputeKpi(ReportPlatformFilter == "all" ? "Tất cả sàn" : ReportPlatformFilter, filtered, _snapshotTime);

        var sb = new StringBuilder();
        sb.AppendLine("BaoCao,GiaTri");
        sb.AppendLine($"San,{CsvEscape(kpi.PlatformName)}");
        sb.AppendLine($"TuNgay,{CsvEscape(ReportFromDate?.ToString("dd/MM/yyyy") ?? "Tất cả")}");
        sb.AppendLine($"DenNgay,{CsvEscape(ReportToDate?.ToString("dd/MM/yyyy") ?? "Tất cả")}");
        sb.AppendLine($"TongDon,{kpi.TotalCount}");
        sb.AppendLine($"DangChoVe,{kpi.PendingCount}");
        sb.AppendLine($"DaNhanHomNay,{kpi.ReceivedTodayCount}");
        sb.AppendLine($"CanhBaoVang,{kpi.YellowAlertCount}");
        sb.AppendLine($"CanhBaoDo,{kpi.RedAlertCount}");
        sb.AppendLine($"GiaTriChuaVe,{kpi.PendingTotalValue.ToString("0", CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        sb.AppendLine("San,LoaiDon,MaDon,MaVanDon,TrangThai,CanhBao,NgayHoan,GiaTri");

        foreach (var o in filtered)
        {
            var alert = AlertThresholds.EvaluateOrder(o, _snapshotTime, _alertPolicy);
            var track = string.IsNullOrWhiteSpace(o.ReturnTrack) ? o.Track : o.ReturnTrack;
            sb.AppendLine(string.Join(",",
                CsvEscape(o.Platform),
                CsvEscape(o.OrderType),
                CsvEscape(o.OrderId),
                CsvEscape(track),
                CsvEscape(o.Status),
                CsvEscape(alert.ToString()),
                CsvEscape(o.ReturnDate),
                o.Price.ToString("0", CultureInfo.InvariantCulture)));
        }

        return sb.ToString();
    }

    private static IReadOnlyList<Order> FilterOrdersForReport(
        IReadOnlyList<Order> orders,
        DateTime? fromDate,
        DateTime? toDate,
        string platformFilter)
    {
        var list = new List<Order>(orders.Count);
        foreach (var o in orders)
        {
            if (platformFilter != "all" && !o.Platform.Equals(platformFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (fromDate.HasValue || toDate.HasValue)
            {
                var dt = AlertThresholds.TryParseVietnamDateTime(o.ReturnDate)
                      ?? AlertThresholds.TryParseVietnamDateTime(o.ReturnedAt)
                      ?? AlertThresholds.TryParseVietnamDateTime(o.RecvDate);
                if (dt is null) continue;
                if (fromDate.HasValue && dt.Value.Date < fromDate.Value.Date) continue;
                if (toDate.HasValue && dt.Value.Date > toDate.Value.Date) continue;
            }

            list.Add(o);
        }
        return list;
    }

    private PlatformKpiSummary ComputeKpi(string label, IReadOnlyList<Order> orders, DateTime now)
    {
        int pending = 0, receivedToday = 0, yellow = 0, red = 0;
        decimal pendingValue = 0m;

        foreach (var o in orders)
        {
            if (AlertThresholds.IsOrderReceivedOrDone(o.Status))
            {
                var recvDt = AlertThresholds.TryParseVietnamDateTime(o.RecvDate);
                if (recvDt.HasValue && recvDt.Value.Date == now.Date)
                {
                    receivedToday++;
                }
                continue;
            }

            if (!AlertThresholds.IsOrderPending(o.Status))
            {
                continue;
            }

            pending++;
            pendingValue += o.Price;

            var level = AlertThresholds.EvaluateOrder(o, now, _alertPolicy);
            if (level == AlertLevel.Red) red++;
            else if (level == AlertLevel.Yellow) yellow++;
        }

        return new PlatformKpiSummary(
            label,
            orders.Count,
            pending,
            receivedToday,
            yellow,
            red,
            pendingValue,
            FormatMoney(pendingValue));
    }

    private static IReadOnlyList<DailyTrendPoint> BuildDailyTrend(IReadOnlyList<Order> orders, DateTime now, int days)
    {
        var start = now.Date.AddDays(-(days - 1));
        var counts = new Dictionary<DateTime, (int Shopee, int TikTok)>();
        for (var i = 0; i < days; i++)
        {
            counts[start.AddDays(i)] = (0, 0);
        }

        foreach (var o in orders)
        {
            var dt = AlertThresholds.TryParseVietnamDateTime(o.ReturnDate)
                  ?? AlertThresholds.TryParseVietnamDateTime(o.ReturnedAt);
            if (dt is null) continue;
            var day = dt.Value.Date;
            if (!counts.TryGetValue(day, out var cur)) continue;

            if (o.Platform.Equals(PlatformNames.Shopee, StringComparison.OrdinalIgnoreCase))
            {
                counts[day] = (cur.Shopee + 1, cur.TikTok);
            }
            else if (o.Platform.Equals(PlatformNames.TikTok, StringComparison.OrdinalIgnoreCase))
            {
                counts[day] = (cur.Shopee, cur.TikTok + 1);
            }
        }

        var maxVal = Math.Max(1, counts.Values.Select(v => Math.Max(v.Shopee, v.TikTok)).DefaultIfEmpty(1).Max());
        const double maxBarHeight = 110.0;

        var result = new List<DailyTrendPoint>(days);
        for (var i = 0; i < days; i++)
        {
            var d = start.AddDays(i);
            var (shp, tt) = counts[d];
            result.Add(new DailyTrendPoint(
                d,
                d.ToString("dd/MM"),
                shp,
                tt,
                shp == 0 ? 2 : Math.Max(8, shp * maxBarHeight / maxVal),
                tt == 0 ? 2 : Math.Max(8, tt * maxBarHeight / maxVal)));
        }
        return result;
    }

    private static IReadOnlyList<OrderTypeBarItem> BuildOrderTypeBars(IReadOnlyList<Order> orders)
    {
        var total = orders.Count;
        if (total == 0) return [];

        const double maxWidth = 180.0;
        var groups = orders
            .GroupBy(o => (
                Platform: string.IsNullOrWhiteSpace(o.Platform) ? "Khác" : o.Platform.Trim(),
                OrderType: string.IsNullOrWhiteSpace(o.OrderType) ? "Khác" : o.OrderType.Trim()))
            .OrderByDescending(g => g.Count())
            .ToList();

        var maxCount = Math.Max(1, groups[0].Count());
        return groups.Select(g =>
        {
            var count = g.Count();
            var color = g.Key.Platform.Equals(PlatformNames.Shopee, StringComparison.OrdinalIgnoreCase)
                ? (OrderTypeNames.IsCancelled(g.Key.OrderType) ? "#F59E0B" : "#EE4D2D")
                : "#1E293B";
            return new OrderTypeBarItem(
                g.Key.Platform,
                g.Key.OrderType,
                count,
                $"{(count * 100.0 / total):0.0}%",
                Math.Max(10, count * maxWidth / maxCount),
                color);
        }).ToList();
    }

    private static IReadOnlyList<ReasonBarItem> BuildTopReasons(IReadOnlyList<Order> orders, int topN)
    {
        var total = orders.Count;
        if (total == 0) return [];

        const double maxWidth = 170.0;
        var groups = orders
            .Where(o => !string.IsNullOrWhiteSpace(o.Note))
            .GroupBy(o => o.Note.Trim())
            .OrderByDescending(g => g.Count())
            .Take(topN)
            .ToList();

        if (groups.Count == 0) return [];
        var maxCount = Math.Max(1, groups[0].Count());

        return groups.Select(g =>
        {
            var count = g.Count();
            return new ReasonBarItem(
                g.Key,
                count,
                $"{(count * 100.0 / total):0.0}%",
                Math.Max(10, count * maxWidth / maxCount));
        }).ToList();
    }

    private IReadOnlyList<UrgentOrderItem> BuildUrgentItems(IReadOnlyList<Order> orders, DateTime now)
    {
        var items = new List<UrgentOrderItem>();
        foreach (var o in orders)
        {
            var level = AlertThresholds.EvaluateOrder(o, now, _alertPolicy);
            if (level is not (AlertLevel.Red or AlertLevel.Yellow)) continue;

            var arrivedAt = AlertThresholds.TryParseVietnamDateTime(o.ReturnedAt);
            var elapsedHours = arrivedAt.HasValue ? Math.Max(0, (now - arrivedAt.Value).TotalHours) : 0;
            var track = string.IsNullOrWhiteSpace(o.ReturnTrack) ? o.Track : o.ReturnTrack;
            var targetKey = ResolveTargetNavigationKey(o);
            var badge = level == AlertLevel.Red
                ? $"🚨 Đỏ ({elapsedHours:0}h)"
                : $"⚠️ Vàng ({elapsedHours:0}h)";

            items.Add(new UrgentOrderItem(
                o.OrderId,
                track,
                o.Platform,
                o.OrderType,
                targetKey,
                level,
                badge,
                FormatMoney(o.Price),
                string.IsNullOrWhiteSpace(o.Note) ? "Chờ kiểm tra nhận kho" : o.Note,
                elapsedHours));
        }

        return items
            .OrderByDescending(i => i.AlertLevel == AlertLevel.Red ? 1 : 0)
            .ThenByDescending(i => i.ElapsedHours)
            .ToList();
    }

    public static string ResolveTargetNavigationKey(Order order)
    {
        if (order.Platform.Equals(PlatformNames.TikTok, StringComparison.OrdinalIgnoreCase))
        {
            return "tiktok_return";
        }

        return OrderTypeNames.IsCancelled(order.OrderType)
            ? "shopee_cancelled"
            : "shopee_return";
    }

    private static PlatformKpiSummary EmptyKpi(string name) =>
        new(name, 0, 0, 0, 0, 0, 0m, "0 đ");

    private static string FormatMoney(decimal value) =>
        string.Format(ViCulture, "{0:N0} đ", value);

    private static string CsvEscape(string? val)
    {
        if (string.IsNullOrEmpty(val)) return "\"\"";
        return $"\"{val.Replace("\"", "\"\"")}\"";
    }
}
