using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Core.Stats;

namespace QuanLyHangHoan.App.ViewModels;

public sealed record PlatformStatRow(
    string Platform,
    int Total,
    int Received,
    int Pending,
    int Other,
    int Yellow,
    int Red,
    string TotalValueFormatted);

public sealed record StatusStatRow(
    string StatusLabel,
    int Count,
    string PercentText,
    string TotalValueFormatted);

public sealed record OrderTypeStatRow(
    string Platform,
    string OrderType,
    int Count,
    string PercentText,
    string TotalValueFormatted);

public sealed record ProductRowVm(int Rank, string Name, int Units, int Orders, string ShareText, double SharePercent, string TopReason, string PlatformText, string VariantText, string PremiumText, ProductStat Source);

public sealed record VariantRowVm(string Variant, int Units, string ShareText, double SharePercent);

public sealed partial class StatisticsViewModel : ObservableObject
{
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IReturnOrderProvider _returnProvider;
    private readonly IArchivedOrderProvider _archivedProvider;
    private readonly IFileSaveService? _fileSaveService;
    private readonly AlertPolicy _mainPolicy;

    private IReadOnlyList<Order> _mainOrders = [];
    private IReadOnlyList<Order> _archivedOrders = [];
    private IReadOnlyList<Order> _lastFilteredOrders = [];
    private DateTime _snapshotTime;

    public StatisticsViewModel(
        IReturnOrderProvider returnProvider,
        IArchivedOrderProvider archivedProvider,
        IFileSaveService? fileSaveService = null,
        AlertPolicy? mainPolicy = null,
        bool loadInitialData = true)
    {
        _returnProvider = returnProvider;
        _archivedProvider = archivedProvider;
        _fileSaveService = fileSaveService;
        _mainPolicy = mainPolicy ?? AlertThresholds.MainPolicy;

        OrderTypeOptions =
        [
            "Tất cả loại",
            OrderTypeNames.ReturnRefund,
            OrderTypeNames.Cancelled,
            OrderTypeNames.Lost,
            OrderTypeNames.FailedDelivery,
            OrderTypeNames.RefundOnly
        ];
        _selectedOrderType = OrderTypeOptions[0];

        if (loadInitialData) Refresh();
    }

    public IReadOnlyList<string> OrderTypeOptions { get; }

    public string AlertRuleNote =>
        $"Ngưỡng cảnh báo: Vàng > {AlertThresholds.YellowHours:0}h • Đỏ > {AlertThresholds.RedHours:0}h kể từ lúc hàng về chưa xử lý (tính thống nhất trên bảng đơn chung)";

    [ObservableProperty]
    private bool _isDemoMode;

    [ObservableProperty]
    private string _scopeFilter = "include_archive"; // "main_only" | "include_archive"

    [ObservableProperty]
    private string _platformFilter = "all"; // "all" | PlatformNames.Shopee | PlatformNames.TikTok

    [ObservableProperty]
    private string _selectedOrderType;

    [ObservableProperty]
    private DateTime? _fromDate;

    [ObservableProperty]
    private DateTime? _toDate;

    [ObservableProperty]
    private int _totalOrders;

    [ObservableProperty]
    private int _receivedCount;

    [ObservableProperty]
    private int _pendingCount;

    [ObservableProperty]
    private int _yellowAlertCount;

    [ObservableProperty]
    private int _redAlertCount;

    [ObservableProperty]
    private int _lostCount;

    [ObservableProperty]
    private int _unneededCount;

    [ObservableProperty]
    private decimal _totalValue;

    [ObservableProperty]
    private string _totalValueFormatted = "0 đ";

    [ObservableProperty]
    private IReadOnlyList<PlatformStatRow> _platformBreakdown = Array.Empty<PlatformStatRow>();

    [ObservableProperty]
    private IReadOnlyList<StatusStatRow> _statusBreakdown = Array.Empty<StatusStatRow>();

    [ObservableProperty]
    private IReadOnlyList<OrderTypeStatRow> _orderTypeBreakdown = Array.Empty<OrderTypeStatRow>();

    [ObservableProperty]
    private string _statusBarText = string.Empty;

    [ObservableProperty]
    private int _missingPhotoCount;

    public bool HasMissingPhotos => MissingPhotoCount > 0;
    public string MissingPhotoText => $"Ảnh mất: {MissingPhotoCount:N0}";

    partial void OnMissingPhotoCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasMissingPhotos));
        OnPropertyChanged(nameof(MissingPhotoText));
    }

    public bool IsScopeMainOnly => ScopeFilter == "main_only";
    public bool IsScopeIncludeArchive => ScopeFilter == "include_archive";

    public bool IsPlatformAll => PlatformFilter == "all";
    public bool IsPlatformShopee => PlatformFilter == PlatformNames.Shopee;
    public bool IsPlatformTikTok => PlatformFilter == PlatformNames.TikTok;
    public bool IsPlatformOff => PlatformFilter == PlatformNames.OffPlatform;

    partial void OnScopeFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsScopeMainOnly));
        OnPropertyChanged(nameof(IsScopeIncludeArchive));
        Recalculate();
    }

    partial void OnPlatformFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsPlatformAll));
        OnPropertyChanged(nameof(IsPlatformShopee));
        OnPropertyChanged(nameof(IsPlatformTikTok));
        OnPropertyChanged(nameof(IsPlatformOff));
        Recalculate();
    }

    partial void OnSelectedOrderTypeChanged(string value) => Recalculate();
    partial void OnFromDateChanged(DateTime? value) => Recalculate();
    partial void OnToDateChanged(DateTime? value) => Recalculate();

    [RelayCommand]
    public void Refresh()
    {
        LoadData(
            _returnProvider.GetReturnOrders(40),
            _archivedProvider.GetArchivedOrders(),
            _returnProvider.GetCurrentTime());
    }

    [RelayCommand]
    private void SetScopeFilter(string scope)
    {
        ScopeFilter = string.IsNullOrWhiteSpace(scope) ? "include_archive" : scope;
    }

    [RelayCommand]
    private void SetPlatformFilter(string platform)
    {
        PlatformFilter = string.IsNullOrWhiteSpace(platform) ? "all" : platform;
    }

    [RelayCommand]
    private void ClearDateFilters()
    {
        FromDate = null;
        ToDate = null;
        SelectedOrderType = OrderTypeOptions[0];
        PlatformFilter = "all";
    }

    [RelayCommand]
    public async Task ExportCsvAsync()
    {
        if (_fileSaveService is null) return;
        var defaultName = $"BaoCao_HangHoan_{_snapshotTime:yyyyMMdd_HHmm}.csv";
        var path = _fileSaveService.PickSavePath("Xuất báo cáo thống kê (CSV)", defaultName, "CSV (*.csv)|*.csv");
        if (string.IsNullOrWhiteSpace(path)) return;

        var csv = BuildCsvSummary();
        await _fileSaveService.WriteTextAsync(path, csv);
        StatusBarText = $"Đã xuất báo cáo CSV: {path}";
    }

    public void LoadData(
        IReadOnlyList<Order> mainOrders,
        IReadOnlyList<Order> archivedOrders,
        DateTime now)
    {
        _mainOrders = mainOrders;
        _archivedOrders = archivedOrders;
        _snapshotTime = now;
        Recalculate();
        RunProductLookup();
    }

    // ---- Tra nhanh sản phẩm (chủ dự án 27/11/2026): gõ tên -> số cái về kho / bị hoàn hôm nay, tuần này, tháng này.
    // Luôn tính trên bảng chính + kho lưu trữ, mọi sàn (không theo bộ lọc phía trên) để con số là "toàn bộ".
    [ObservableProperty] private string _productLookupText = "";
    [ObservableProperty] private IReadOnlyList<ProductLookupRow> _productLookupRows = Array.Empty<ProductLookupRow>();
    [ObservableProperty] private string _productLookupHint = "Gõ tên sản phẩm (không dấu cũng được), ví dụ: «ao thun den».";
    public bool HasProductLookupRows => ProductLookupRows.Count > 0;
    public string WeekLabel => $"Tuần này (từ {ProductLookup.WeekStart(_snapshotTime == default ? DateTime.Now : _snapshotTime):dd/MM})";
    public string MonthLabel => $"Tháng {(_snapshotTime == default ? DateTime.Now : _snapshotTime):MM/yyyy}";
    partial void OnProductLookupRowsChanged(IReadOnlyList<ProductLookupRow> value) => OnPropertyChanged(nameof(HasProductLookupRows));
    partial void OnProductLookupTextChanged(string value) => RunProductLookup();

    [RelayCommand]
    private void ClearProductLookup() => ProductLookupText = "";

    private void RunProductLookup()
    {
        var now = _snapshotTime == default ? DateTime.Now : _snapshotTime;
        OnPropertyChanged(nameof(WeekLabel));
        OnPropertyChanged(nameof(MonthLabel));
        if (ProductLookup.Terms(ProductLookupText).Length == 0)
        {
            ProductLookupRows = Array.Empty<ProductLookupRow>();
            ProductLookupHint = "Gõ tên sản phẩm (không dấu cũng được), ví dụ: «ao thun den».";
            return;
        }
        ProductLookupRows = ProductLookup.Search(_mainOrders.Concat(_archivedOrders), ProductLookupText, now, limit: 8);
        ProductLookupHint = ProductLookupRows.Count == 0
            ? $"Không có sản phẩm nào khớp «{ProductLookupText.Trim()}»."
            : $"{ProductLookupRows.Count:N0} sản phẩm khớp (tối đa 8, nhiều nhất tháng này trước) • bảng chính + kho lưu trữ, mọi sàn";
    }

    // ---- Thống kê sản phẩm (cùng bộ lọc: phạm vi, sàn, loại đơn, ngày) ----
    [ObservableProperty] private IReadOnlyList<ProductRowVm> _productRows = Array.Empty<ProductRowVm>();
    [ObservableProperty] private ProductRowVm? _selectedProduct;
    [ObservableProperty] private IReadOnlyList<VariantRowVm> _variantRows = Array.Empty<VariantRowVm>();
    [ObservableProperty] private string _productSummaryText = "";
    [ObservableProperty] private string _variantTitle = "";
    public bool HasProducts => ProductRows.Count > 0;
    public bool HasSelectedProduct => SelectedProduct is not null;

    partial void OnProductRowsChanged(IReadOnlyList<ProductRowVm> value) => OnPropertyChanged(nameof(HasProducts));

    partial void OnSelectedProductChanged(ProductRowVm? value)
    {
        OnPropertyChanged(nameof(HasSelectedProduct));
        if (value is null) { VariantRows = Array.Empty<VariantRowVm>(); VariantTitle = ""; return; }
        VariantTitle = value.PremiumText.Length == 0 ? $"Màu / size của: {value.Name}" : $"Màu / size của: {value.Name}{Environment.NewLine}{value.PremiumText}";
        VariantRows = value.Source.Variants.Select(v => new VariantRowVm(
            VariantLabel(v), v.Units,
            (value.Units == 0 ? 0 : (double)v.Units / value.Units).ToString("P0", ViCulture),
            value.Units == 0 ? 0 : 100.0 * v.Units / value.Units)).ToList();
    }

    /// <summary>Các sàn có sản phẩm này: "Shopee 3 • TikTok 2 • Khác 1" (Khác = ngoài sàn / sàn khác); chỉ một nơi thì ghi tên nơi đó.</summary>
    private static string PlatformLabel(ProductStat p)
    {
        var other = p.Units - p.ShopeeUnits - p.TikTokUnits;
        var parts = new List<string>();
        if (p.ShopeeUnits > 0) parts.Add($"Shopee {p.ShopeeUnits:N0}");
        if (p.TikTokUnits > 0) parts.Add($"TikTok {p.TikTokUnits:N0}");
        if (other > 0) parts.Add($"Khác {other:N0}");
        if (parts.Count == 0) return "—";
        if (parts.Count == 1) return p.ShopeeUnits > 0 ? "Shopee" : p.TikTokUnits > 0 ? "TikTok" : "Ngoài sàn / khác";
        return string.Join(" • ", parts);
    }

    /// <summary>"Premium 76 • Thường 76"; để trống khi sản phẩm không có bản Premium nào.</summary>
    private static string PremiumLabel(ProductStat p) => p.PremiumUnits == 0 ? "" : $"Premium {p.PremiumUnits:N0} • Thường {p.StandardUnits:N0}";

    private static string VariantLabel(VariantStat v)
    {
        var parts = new[] { v.Color, v.Size }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        return parts.Length == 0 ? "(không ghi màu/size)" : string.Join(" / ", parts);
    }

    private void BuildProductStatistics(IReadOnlyList<Order> filtered)
    {
        var result = ProductStatistics.Compute(filtered);
        var keep = SelectedProduct?.Name;
        var rows = result.Products.Take(100).Select((p, i) => new ProductRowVm(
            i + 1, p.Name, p.Units, p.Orders,
            p.Share.ToString("P1", ViCulture), p.Share * 100, string.IsNullOrWhiteSpace(p.TopReason) ? "—" : p.TopReason,
            PlatformLabel(p),
            string.Join(" · ", p.Variants.Take(3).Select(v => $"{VariantLabel(v)} ×{v.Units:N0}")),
            PremiumLabel(p),
            p)).ToList();
        ProductRows = rows;
        ProductSummaryText = result.TotalUnits == 0
            ? "Chưa có sản phẩm nào trong bộ lọc hiện tại."
            : $"{result.Products.Count:N0} sản phẩm • {result.TotalUnits:N0} cái bị hoàn trong {result.OrdersWithItems:N0} đơn" + (result.UnknownUnits > 0 ? $" • {result.UnknownUnits:N0} cái chưa rõ sản phẩm (không tính)" : "") + (result.Products.Count > rows.Count ? $" • đang hiện {rows.Count} sản phẩm đầu" : "");
        SelectedProduct = keep is null ? null : rows.FirstOrDefault(r => r.Name == keep);
    }

    public void Recalculate()
    {
        var active = new List<Order>(_mainOrders.Count + (IsScopeIncludeArchive ? _archivedOrders.Count : 0));
        active.AddRange(_mainOrders);
        if (IsScopeIncludeArchive)
        {
            active.AddRange(_archivedOrders);
        }

        var filtered = new List<Order>(active.Count);
        foreach (var order in active)
        {
            if (PlatformFilter != "all" && !order.Platform.Equals(PlatformFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(SelectedOrderType)
                && SelectedOrderType != OrderTypeOptions[0]
                && !order.OrderType.Equals(SelectedOrderType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (FromDate.HasValue || ToDate.HasValue)
            {
                var dt = AlertThresholds.TryParseVietnamDateTime(order.ReturnDate)
                      ?? AlertThresholds.TryParseVietnamDateTime(order.ReturnedAt)
                      ?? AlertThresholds.TryParseVietnamDateTime(order.RecvDate);
                if (dt is null) continue;
                if (FromDate.HasValue && dt.Value.Date < FromDate.Value.Date) continue;
                if (ToDate.HasValue && dt.Value.Date > ToDate.Value.Date) continue;
            }

            filtered.Add(order);
        }

        _lastFilteredOrders = filtered;

        int received = 0, pending = 0, yellow = 0, red = 0, lost = 0, unneeded = 0, pendingSafeOrTransit = 0;
        decimal totalVal = 0m;
        decimal receivedVal = 0m, redVal = 0m, yellowVal = 0m, safeVal = 0m, lostVal = 0m, unneededVal = 0m;

        foreach (var order in filtered)
        {
            totalVal += order.Price;

            if (IsReceived(order.Status))
            {
                received++;
                receivedVal += order.Price;
            }
            else if (IsNotNeeded(order.Status))
            {
                unneeded++;
                unneededVal += order.Price;
            }
            else if (IsLost(order.Status))
            {
                lost++;
                lostVal += order.Price;
            }
            else
            {
                pending++;
                var alert = AlertThresholds.EvaluateOrder(order, _snapshotTime, _mainPolicy);
                if (alert == AlertLevel.Red)
                {
                    red++;
                    redVal += order.Price;
                }
                else if (alert == AlertLevel.Yellow)
                {
                    yellow++;
                    yellowVal += order.Price;
                }
                else
                {
                    pendingSafeOrTransit++;
                    safeVal += order.Price;
                }
            }
        }

        TotalOrders = filtered.Count;
        ReceivedCount = received;
        PendingCount = pending;
        YellowAlertCount = yellow;
        RedAlertCount = red;
        LostCount = lost;
        UnneededCount = unneeded;
        TotalValue = totalVal;
        TotalValueFormatted = FormatMoney(totalVal);

        PlatformBreakdown = BuildPlatformBreakdown(filtered);
        OrderTypeBreakdown = BuildOrderTypeBreakdown(filtered);
        StatusBreakdown = BuildStatusBreakdown(
            filtered.Count,
            received, receivedVal,
            red, redVal,
            yellow, yellowVal,
            pendingSafeOrTransit, safeVal,
            lost, lostVal,
            unneeded, unneededVal);

        BuildProductStatistics(filtered);

        var scopeLabel = IsScopeIncludeArchive ? "Bảng chính + Kho lưu trữ" : "Chỉ bảng chính";
        var platLabel = PlatformFilter == "all" ? "Tất cả sàn" : PlatformFilter;
        StatusBarText = $"Phạm vi: {scopeLabel} • {platLabel} • Tổng đơn: {TotalOrders:N0} • Giá trị: {TotalValueFormatted} • Cập nhật lúc {_snapshotTime:dd/MM/yyyy HH:mm}";
    }

    public string BuildCsvSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("San,LoaiDon,MaDon,MaVanDon,TrangThai,CanhBao,NgayHoan,NgayHangVe,GiaTri");
        foreach (var o in _lastFilteredOrders)
        {
            var alert = AlertThresholds.EvaluateOrder(o, _snapshotTime, _mainPolicy);
            var track = string.IsNullOrWhiteSpace(o.ReturnTrack) ? o.Track : o.ReturnTrack;
            sb.AppendLine(string.Join(",",
                CsvEscape(o.Platform),
                CsvEscape(o.OrderType),
                CsvEscape(o.OrderId),
                CsvEscape(track),
                CsvEscape(o.Status),
                CsvEscape(alert.ToString()),
                CsvEscape(o.ReturnDate),
                CsvEscape(o.ReturnedAt),
                o.Price.ToString("0", CultureInfo.InvariantCulture)));
        }
        return sb.ToString();
    }

    private static string CsvEscape(string? val)
    {
        if (string.IsNullOrEmpty(val)) return "\"\"";
        return $"\"{val.Replace("\"", "\"\"")}\"";
    }

    private IReadOnlyList<PlatformStatRow> BuildPlatformBreakdown(IReadOnlyList<Order> orders)
    {
        var platformNames = orders.Select(o => NormalizePlatform(o.Platform))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = new List<PlatformStatRow>(platformNames.Count + 1);
        foreach (var plat in platformNames)
        {
            var platOrders = orders.Where(o => NormalizePlatform(o.Platform).Equals(plat, StringComparison.OrdinalIgnoreCase)).ToList();

            int rRec = 0, rPend = 0, rOther = 0, rYel = 0, rRed = 0;
            decimal rVal = 0m;
            foreach (var o in platOrders)
            {
                rVal += o.Price;
                if (IsReceived(o.Status)) rRec++;
                else if (IsLost(o.Status) || IsNotNeeded(o.Status)) rOther++;
                else
                {
                    rPend++;
                    var level = AlertThresholds.EvaluateOrder(o, _snapshotTime, _mainPolicy);
                    if (level == AlertLevel.Red) rRed++;
                    else if (level == AlertLevel.Yellow) rYel++;
                }
            }

            rows.Add(new PlatformStatRow(
                Platform: plat,
                Total: platOrders.Count,
                Received: rRec,
                Pending: rPend,
                Other: rOther,
                Yellow: rYel,
                Red: rRed,
                TotalValueFormatted: FormatMoney(rVal)));
        }

        if (rows.Count > 1)
        {
            rows.Add(new PlatformStatRow(
                Platform: "TỔNG CỘNG",
                Total: rows.Sum(r => r.Total),
                Received: rows.Sum(r => r.Received),
                Pending: rows.Sum(r => r.Pending),
                Other: rows.Sum(r => r.Other),
                Yellow: rows.Sum(r => r.Yellow),
                Red: rows.Sum(r => r.Red),
                TotalValueFormatted: FormatMoney(orders.Sum(o => o.Price))));
        }

        return rows;
    }

    private static IReadOnlyList<OrderTypeStatRow> BuildOrderTypeBreakdown(IReadOnlyList<Order> orders)
    {
        var total = orders.Count;
        if (total == 0) return [];

        return orders
            .GroupBy(o => (Platform: NormalizePlatform(o.Platform), OrderType: string.IsNullOrWhiteSpace(o.OrderType) ? "Khác" : o.OrderType.Trim()))
            .OrderBy(g => g.Key.Platform, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(g => g.Count())
            .Select(g =>
            {
                var count = g.Count();
                var val = g.Sum(o => o.Price);
                return new OrderTypeStatRow(
                    g.Key.Platform,
                    g.Key.OrderType,
                    count,
                    $"{(count * 100.0 / total):0.0}%",
                    FormatMoney(val));
            })
            .ToList();
    }

    private static IReadOnlyList<StatusStatRow> BuildStatusBreakdown(
        int total,
        int received, decimal receivedVal,
        int red, decimal redVal,
        int yellow, decimal yellowVal,
        int safe, decimal safeVal,
        int lost, decimal lostVal,
        int unneeded, decimal unneededVal)
    {
        string Pct(int count) => total == 0 ? "0.0%" : $"{(count * 100.0 / total):0.0}%";

        return
        [
            new StatusStatRow("🟢 Đã nhận tại kho", received, Pct(received), FormatMoney(receivedVal)),
            new StatusStatRow($"🚨 Chưa nhận — Báo Đỏ (> {AlertThresholds.RedHours:0}h)", red, Pct(red), FormatMoney(redVal)),
            new StatusStatRow($"⚠️ Chưa nhận — Báo Vàng (> {AlertThresholds.YellowHours:0}h)", yellow, Pct(yellow), FormatMoney(yellowVal)),
            new StatusStatRow("⏳ Chưa nhận — An toàn / Đang chuyển hoàn", safe, Pct(safe), FormatMoney(safeVal)),
            new StatusStatRow("🔴 Bị mất / Hư hỏng", lost, Pct(lost), FormatMoney(lostVal)),
            new StatusStatRow("⚪ Không cần nhận", unneeded, Pct(unneeded), FormatMoney(unneededVal))
        ];
    }

    private static bool IsReceived(string? status) => AlertThresholds.IsOrderReceivedOrDone(status);
    private static bool IsNotNeeded(string? status) =>
        !string.IsNullOrWhiteSpace(status) && status.Equals(ReceiveStatus.NotNeeded, StringComparison.OrdinalIgnoreCase);
    private static bool IsLost(string? status) =>
        !string.IsNullOrWhiteSpace(status)
        && (status.Equals(ReceiveStatus.Lost, StringComparison.OrdinalIgnoreCase)
            || status.Equals("Hư hỏng", StringComparison.OrdinalIgnoreCase));

    private static string NormalizePlatform(string? platform) =>
        string.IsNullOrWhiteSpace(platform) ? "Khác" : platform.Trim();

    private static string FormatMoney(decimal value) =>
        string.Format(ViCulture, "{0:N0} đ", value);
}
