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
        BuildTopProducts(_orders);
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
        var orders = FilterByPeriod(allOrders, ProductPeriod, _snapshotTime == default ? DateTime.Now : _snapshotTime);
        var r = QuanLyHangHoan.Core.Stats.ProductStatistics.Compute(orders);
        if (r.Products.Count == 0) { TopProducts = Array.Empty<HomeProductItem>(); ProductSummaryText = $"Chưa có sản phẩm hoàn nào ({ProductPeriodLabel.ToLowerInvariant()})."; return; }
        var top = r.Products.Take(6).ToList();
        var max = Math.Max(1, top[0].Units);
        TopProducts = top.Select((p, i) => new HomeProductItem(
            i + 1, p.Name, p.Units, p.Orders, p.Share.ToString("P1", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")),
            Math.Max(8, p.Units * 150.0 / max),
            p.PremiumUnits > 0 ? $"Premium {p.PremiumUnits:N0} • Thường {p.StandardUnits:N0}" : "")).ToList();
        ProductSummaryText = $"{ProductPeriodLabel}: {r.Products.Count:N0} sản phẩm • {r.TotalUnits:N0} cái bị hoàn trong {r.OrdersWithItems:N0} đơn";
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
            BuildTopProducts(orders);
            return;
        }

        OverallKpi = ComputeKpi("Tổng toàn shop", orders, now);
        ShopeeKpi = ComputeKpi(PlatformNames.Shopee, orders.Where(o => o.Platform.Equals(PlatformNames.Shopee, StringComparison.OrdinalIgnoreCase)).ToList(), now);
        TikTokKpi = ComputeKpi(PlatformNames.TikTok, orders.Where(o => o.Platform.Equals(PlatformNames.TikTok, StringComparison.OrdinalIgnoreCase)).ToList(), now);

        DailyTrendPoints = BuildDailyTrend(orders, now, days: 7);
        OrderTypeBars = BuildOrderTypeBars(orders);
        TopReasons = BuildTopReasons(orders, topN: 5);
        UrgentItems = BuildUrgentItems(orders, now);
        BuildTopProducts(orders);
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
