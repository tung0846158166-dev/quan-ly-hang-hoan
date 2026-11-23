using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.ViewModels;

public sealed class WatchlistRowViewModel
{
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

    public WatchlistRowViewModel(Order source, int stt, DateTime now, bool isArchived, AlertPolicy? policy = null)
    {
        Source = source;
        Stt = stt;
        IsArchived = isArchived;
        (LocationLabel, _) = GlobalSearchService.ResolveLocation(source, isArchived, isDeleted: false);

        ArrivedAtDate = AlertThresholds.TryParseVietnamDateTime(source.ReturnedAt);
        AlertLevel = AlertThresholds.EvaluateOrder(source, now, policy);

        if (ArrivedAtDate is not null && AlertLevel is AlertLevel.Safe or AlertLevel.Yellow or AlertLevel.Red)
        {
            var elapsed = now - ArrivedAtDate.Value;
            ElapsedHours = Math.Max(0, elapsed.TotalHours);
            ElapsedText = FormatElapsed(elapsed);
        }
        else
        {
            ElapsedHours = 0;
            ElapsedText = string.Empty;
        }

        AlertBadgeText = AlertLevel switch
        {
            AlertLevel.Red => $"🚨 Quá {AlertThresholds.RedHours:0}h ({ElapsedText})",
            AlertLevel.Yellow => $"⚠️ Quá {AlertThresholds.YellowHours:0}h ({ElapsedText})",
            AlertLevel.Safe => $"⏳ An toàn ({ElapsedText})",
            AlertLevel.Done => "✅ Đã xử lý",
            _ => "—"
        };

        AlertPriority = AlertLevel switch
        {
            AlertLevel.Red => 4,
            AlertLevel.Yellow => 3,
            AlertLevel.Safe => 2,
            AlertLevel.None => 1,
            AlertLevel.Done => 0,
            _ => 0
        };

        DisplayTracking = BuildTrackingDisplay(source);
        ItemsSummary = BuildItemsSummary(source.Items, source.IsPartialReturn);
        ItemLines = ItemDisplay.Build(source.Items);
        PartialChip = ItemDisplay.Chip(source.Items, source.IsPartialReturn);
        ItemsTooltip = ItemDisplay.Tooltip(source.Items, source.IsPartialReturn);
        TotalQty = source.Items.Count > 0 ? source.Items.Sum(i => Math.Max(i.Qty, 0)) : 0;
        PriceFormatted = string.Format(ViCulture, "{0:N0} đ", source.Price);

        SearchBlob = AlertThresholds.NormalizeForSearch(
            $"{source.OrderId} {source.ReturnOrderId} {source.Track} {source.ReturnTrack} {source.AltTrack} {source.ForwardTrack} " +
            $"{source.Platform} {source.Carrier} {source.Status} {LocationLabel} {ItemsSummary} {source.WatchlistReason} {source.Note} {source.UserNote}");
    }

    public Order Source { get; }
    public int Stt { get; }
    public bool IsArchived { get; }
    public string LocationLabel { get; }
    public string Platform => Source.Platform;
    public string OrderId => Source.OrderId;
    public string DisplayTracking { get; }
    public string ItemsSummary { get; }
    public IReadOnlyList<ItemLine> ItemLines { get; }
    public string PartialChip { get; }
    public string ItemsTooltip { get; }
    public int TotalQty { get; }
    public string PriceFormatted { get; }
    public string Status => Source.Status;
    public string WatchlistReason => Source.WatchlistReason;
    public DateTime? ArrivedAtDate { get; }
    public AlertLevel AlertLevel { get; }
    public int AlertPriority { get; }
    public double ElapsedHours { get; }
    public string ElapsedText { get; }
    public string AlertBadgeText { get; }
    public string SearchBlob { get; }

    private static string BuildTrackingDisplay(Order o)
    {
        var ret = !string.IsNullOrWhiteSpace(o.ReturnTrack) ? o.ReturnTrack : o.Track;
        if (!string.IsNullOrWhiteSpace(ret) && !string.IsNullOrWhiteSpace(o.ForwardTrack)
            && !ret.Equals(o.ForwardTrack, StringComparison.OrdinalIgnoreCase))
        {
            return $"{ret}  (Giao: {o.ForwardTrack})";
        }

        if (!string.IsNullOrWhiteSpace(ret)) return ret;
        if (!string.IsNullOrWhiteSpace(o.ForwardTrack)) return $"Giao: {o.ForwardTrack}";
        return "Chưa có MVĐ";
    }

    private static string BuildItemsSummary(IReadOnlyList<OrderItem> items, bool isPartialReturn)
    {
        if (items.Count == 0) return "Chưa có sản phẩm";
        var first = items[0];
        var variant = string.Join("/", new[] { first.Color, first.Size }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var firstText = string.IsNullOrWhiteSpace(variant)
            ? $"{first.Product} (x{first.Qty})"
            : $"{first.Product} ({variant} x{first.Qty})";
        var partialPrefix = isPartialReturn ? "[1 phần] " : string.Empty;
        return items.Count == 1
            ? $"{partialPrefix}{firstText}"
            : $"{partialPrefix}{firstText} +{items.Count - 1} món";
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalMinutes < 1) return "vừa về";
        var totalHours = (int)elapsed.TotalHours;
        var days = totalHours / 24;
        var hours = totalHours % 24;
        var mins = elapsed.Minutes;
        if (days > 0) return $"{days}n {hours}h";
        if (totalHours > 0) return $"{totalHours}h {mins}p";
        return $"{mins}p";
    }
}

public sealed partial class WatchlistViewModel : ObservableObject
{
    private readonly IOrderEditService? _editService;
    private readonly IOrderActionDialogService? _dialogService;
    private readonly Func<Task>? _onDataChangedAsync;
    private readonly Action<string, bool>? _onNotify;
    private readonly Action<Order>? _onOpenDetail;
    private readonly AlertPolicy _alertPolicy;
    private IReadOnlyList<Order> _activeOrders = Array.Empty<Order>();
    private IReadOnlyList<Order> _archivedOrders = Array.Empty<Order>();
    private DateTime _lastNow = DateTime.Now;
    private List<WatchlistRowViewModel> _allRows = [];
    private bool _suppressRefresh;

    public WatchlistViewModel(
        IOrderEditService? editService = null,
        IOrderActionDialogService? dialogService = null,
        Func<Task>? onDataChangedAsync = null,
        Action<string, bool>? onNotify = null,
        Action<Order>? onOpenDetail = null,
        AlertPolicy? alertPolicy = null)
    {
        _editService = editService;
        _dialogService = dialogService;
        _onDataChangedAsync = onDataChangedAsync;
        _onNotify = onNotify;
        _onOpenDetail = onOpenDetail;
        _alertPolicy = alertPolicy ?? AlertThresholds.MainPolicy;
    }

    public event Action? CountsUpdated;

    [ObservableProperty]
    private bool _isDemoMode;

    [ObservableProperty]
    private IReadOnlyList<WatchlistRowViewModel> _visibleOrders = Array.Empty<WatchlistRowViewModel>();

    [ObservableProperty]
    private WatchlistRowViewModel? _selectedRow;

    [ObservableProperty]
    private string _platformFilter = "all"; // "all" | PlatformNames.Shopee | PlatformNames.TikTok

    [ObservableProperty]
    private bool _includeArchived;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private int _activeWatchlistCount;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _visibleCount;

    [ObservableProperty]
    private int _shopeeCount;

    [ObservableProperty]
    private int _tikTokCount;

    [ObservableProperty]
    private string _statusBarText = string.Empty;

    public bool IsPlatformAll => PlatformFilter == "all";
    public bool IsPlatformShopee => PlatformFilter == PlatformNames.Shopee;
    public bool IsPlatformTikTok => PlatformFilter == PlatformNames.TikTok;

    partial void OnPlatformFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsPlatformAll));
        OnPropertyChanged(nameof(IsPlatformShopee));
        OnPropertyChanged(nameof(IsPlatformTikTok));
        if (!_suppressRefresh) ApplyFiltersAndSort();
    }

    partial void OnIncludeArchivedChanged(bool value)
    {
        if (!_suppressRefresh) RebuildRows();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!_suppressRefresh) ApplyFiltersAndSort();
    }

    public void LoadOrders(IReadOnlyList<Order> activeOrders, IReadOnlyList<Order> archivedOrders, DateTime? nowOverride = null)
    {
        _activeOrders = activeOrders ?? Array.Empty<Order>();
        _archivedOrders = archivedOrders ?? Array.Empty<Order>();
        _lastNow = nowOverride ?? DateTime.Now;
        RebuildRows();
    }

    private void RebuildRows()
    {
        var previousId = SelectedRow?.Source.Id;
        var rows = new List<WatchlistRowViewModel>();
        int activeWatchCount = 0;
        int shopee = 0;
        int tiktok = 0;
        int stt = 0;

        foreach (var o in _activeOrders)
        {
            if (!o.IsWatchlist) continue;
            activeWatchCount++;
            stt++;
            var row = new WatchlistRowViewModel(o, stt, _lastNow, isArchived: false, _alertPolicy);
            rows.Add(row);
            if (row.Platform.Equals(PlatformNames.Shopee, StringComparison.OrdinalIgnoreCase)) shopee++;
            else if (row.Platform.Equals(PlatformNames.TikTok, StringComparison.OrdinalIgnoreCase)) tiktok++;
        }

        if (IncludeArchived)
        {
            foreach (var o in _archivedOrders)
            {
                if (!o.IsWatchlist) continue;
                stt++;
                var row = new WatchlistRowViewModel(o, stt, _lastNow, isArchived: true, _alertPolicy);
                rows.Add(row);
                if (row.Platform.Equals(PlatformNames.Shopee, StringComparison.OrdinalIgnoreCase)) shopee++;
                else if (row.Platform.Equals(PlatformNames.TikTok, StringComparison.OrdinalIgnoreCase)) tiktok++;
            }
        }

        _allRows = rows;
        ActiveWatchlistCount = activeWatchCount;
        TotalCount = rows.Count;
        ShopeeCount = shopee;
        TikTokCount = tiktok;

        ApplyFiltersAndSort(previousId);
        CountsUpdated?.Invoke();
    }

    public void ApplyFiltersAndSort(string? preserveOrderId = null)
    {
        var sw = Stopwatch.StartNew();
        var kw = AlertThresholds.NormalizeForSearch(SearchText).Trim();
        var hasKw = kw.Length > 0;

        var filtered = new List<WatchlistRowViewModel>(_allRows.Count);
        foreach (var row in _allRows)
        {
            if (PlatformFilter != "all" && !row.Platform.Equals(PlatformFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (hasKw && !row.SearchBlob.Contains(kw, StringComparison.Ordinal))
            {
                continue;
            }
            filtered.Add(row);
        }

        filtered.Sort((a, b) =>
        {
            var p = b.AlertPriority.CompareTo(a.AlertPriority);
            if (p != 0) return p;
            var h = b.ElapsedHours.CompareTo(a.ElapsedHours);
            return h != 0 ? h : a.Stt.CompareTo(b.Stt);
        });

        sw.Stop();
        VisibleOrders = filtered;
        VisibleCount = filtered.Count;
        IsEmpty = filtered.Count == 0;

        var targetId = preserveOrderId ?? SelectedRow?.Source.Id;
        if (!string.IsNullOrWhiteSpace(targetId))
        {
            SelectedRow = filtered.FirstOrDefault(r => r.Source.Id == targetId);
        }
        else if (SelectedRow is not null && !filtered.Contains(SelectedRow))
        {
            SelectedRow = null;
        }

        StatusBarText = $"Đang theo dõi {filtered.Count:N0} / {_allRows.Count:N0} kiện  •  Lọc: {sw.ElapsedMilliseconds} ms";
    }

    [RelayCommand]
    private void SetPlatformFilter(string platform)
    {
        PlatformFilter = string.IsNullOrWhiteSpace(platform) ? "all" : platform;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    [RelayCommand]
    private void ResetAllFilters()
    {
        _suppressRefresh = true;
        try
        {
            PlatformFilter = "all";
            SearchText = string.Empty;
        }
        finally
        {
            _suppressRefresh = false;
        }
        ApplyFiltersAndSort();
    }

    [RelayCommand]
    public void OpenDetail(WatchlistRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is null) return;
        _onOpenDetail?.Invoke(target.Source);
    }

    [RelayCommand]
    public async Task ChangeStatusAsync(WatchlistRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is null || _editService is null || _dialogService is null) return;

        var newStatus = _dialogService.PromptStatus(target.OrderId, target.DisplayTracking, target.Status);
        if (newStatus is null) return;

        var result = await _editService.SetFieldAsync(
            target.Source.Id,
            EditField.Status,
            newStatus,
            expectedOld: target.Status);
        await OrderEditHelper.HandleResultAsync(result, _onDataChangedAsync, _onNotify);
    }

    [RelayCommand]
    public async Task EditWatchlistReasonAsync(WatchlistRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is null || _editService is null || _dialogService is null) return;

        var newReason = _dialogService.PromptMultilineText(
            "Sửa lý do theo dõi",
            $"Mã đơn: {target.OrderId}  •  MVĐ: {target.DisplayTracking}",
            "Lý do theo dõi:",
            target.WatchlistReason);
        if (newReason is null) return;

        var result = await _editService.SetFieldAsync(
            target.Source.Id,
            EditField.WatchlistReason,
            newReason,
            expectedOld: target.WatchlistReason);
        await OrderEditHelper.HandleResultAsync(result, _onDataChangedAsync, _onNotify);
    }

    [RelayCommand]
    public async Task RemoveFromWatchlistAsync(WatchlistRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is null || _editService is null) return;

        var result = await _editService.SetFieldAsync(
            target.Source.Id,
            EditField.IsWatchlist,
            "0",
            expectedOld: "1");
        await OrderEditHelper.HandleResultAsync(result, _onDataChangedAsync, _onNotify);
    }

    [RelayCommand]
    public async Task ShowHistoryAsync(WatchlistRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is null || _editService is null || _dialogService is null) return;

        var entries = await _editService.GetHistoryAsync(target.Source.Id);
        _dialogService.ShowHistory(target.OrderId, target.DisplayTracking, entries);
    }
}
