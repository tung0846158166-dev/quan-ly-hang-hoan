using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.App.ViewModels;

public sealed class ArchivedOrderRowViewModel
{
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

    public ArchivedOrderRowViewModel(Order source, int stt)
    {
        Source = source;
        Stt = stt;
        RecvDateParsed = AlertThresholds.TryParseVietnamDateTime(source.RecvDate);
        DisplayTracking = !string.IsNullOrWhiteSpace(source.ReturnTrack) ? source.ReturnTrack : source.Track;
        ItemsSummary = BuildItemsSummary(source.Items);
        TotalQty = source.Items.Count > 0 ? source.Items.Sum(i => Math.Max(i.Qty, 0)) : 0;
        PriceFormatted = string.Format(ViCulture, "{0:N0} đ", source.Price);
        PhotoIndicator = string.IsNullOrWhiteSpace(source.PhotoId) ? "—" : "📷 Có ảnh";
        NoteDisplay = !string.IsNullOrWhiteSpace(source.UserNote)
            ? source.UserNote
            : source.Note;

        SearchBlob = AlertThresholds.NormalizeForSearch(
            $"{source.OrderId} {source.Track} {source.ReturnTrack} {source.Platform} {source.Carrier} {ItemsSummary} {NoteDisplay} {source.RecvDate}");
    }

    public Order Source { get; }
    public int Stt { get; }
    public string RecvDate => Source.RecvDate;
    public DateTime? RecvDateParsed { get; }
    public string Platform => Source.Platform;
    public string OrderId => Source.OrderId;
    public string DisplayTracking { get; }
    public string ItemsSummary { get; }
    public int TotalQty { get; }
    public string Carrier => Source.Carrier;
    public decimal Price => Source.Price;
    public string PriceFormatted { get; }
    public string NoteDisplay { get; }
    public string Status => Source.Status;
    public string PhotoId => Source.PhotoId;
    public bool HasPhoto => !string.IsNullOrWhiteSpace(Source.PhotoId);
    public string PhotoIndicator { get; }
    public string SearchBlob { get; }

    private static string BuildItemsSummary(IReadOnlyList<OrderItem> items)
    {
        if (items.Count == 0) return "Chưa có sản phẩm";
        var first = items[0];
        var variant = string.Join("/", new[] { first.Color, first.Size }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var firstText = string.IsNullOrWhiteSpace(variant)
            ? $"{first.Product} (x{first.Qty})"
            : $"{first.Product} ({variant} x{first.Qty})";
        return items.Count == 1 ? firstText : $"{firstText} +{items.Count - 1} món";
    }
}

public sealed partial class ArchivedOrdersViewModel : ObservableObject
{
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");
    private readonly IArchivedOrderProvider _provider;
    private readonly Action<string, string, string>? _onOpenPhoto;
    private readonly IOrderEditService? _editService;
    private readonly IOrderActionDialogService? _dialogService;
    private readonly Func<Task>? _onDataChangedAsync;
    private readonly Action<IReadOnlyList<(string OrderId, string Tracking)>>? _onOrdersDeleted;
    private readonly Action<string, bool>? _onNotify;
    private readonly Action<Order>? _onOpenDetail;
    private List<ArchivedOrderRowViewModel> _allRows = [];
    private bool _suppressRefresh;

    public ArchivedOrdersViewModel(
        IArchivedOrderProvider provider,
        Action<string, string, string>? onOpenPhoto = null,
        IOrderEditService? editService = null,
        IOrderActionDialogService? dialogService = null,
        Func<Task>? onDataChangedAsync = null,
        Action<IReadOnlyList<(string OrderId, string Tracking)>>? onOrdersDeleted = null,
        Action<string, bool>? onNotify = null,
        Action<Order>? onOpenDetail = null)
    {
        _provider = provider;
        _onOpenPhoto = onOpenPhoto;
        _editService = editService;
        _dialogService = dialogService;
        _onDataChangedAsync = onDataChangedAsync;
        _onOrdersDeleted = onOrdersDeleted;
        _onNotify = onNotify;
        _onOpenDetail = onOpenDetail;
        SortOptions =
        [
            new SortOptionItem("default", "Mặc định (STT)"),
            new SortOptionItem("recv_desc", "Ngày về kho (Mới → Cũ)"),
            new SortOptionItem("recv_asc", "Ngày về kho (Cũ → Mới)"),
            new SortOptionItem("price_desc", "Giá trị (Cao → Thấp)")
        ];
        _selectedSortOption = SortOptions[0];

        LoadOrders(_provider.GetArchivedOrders());
    }

    public IReadOnlyList<SortOptionItem> SortOptions { get; }

    [ObservableProperty]
    private bool _isDemoMode;

    [ObservableProperty]
    private IReadOnlyList<ArchivedOrderRowViewModel> _visibleOrders = Array.Empty<ArchivedOrderRowViewModel>();

    [ObservableProperty]
    private ArchivedOrderRowViewModel? _selectedRow;

    [ObservableProperty]
    private string _platformFilter = "all";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private SortOptionItem _selectedSortOption;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _visibleCount;

    [ObservableProperty]
    private int _shopeeCount;

    [ObservableProperty]
    private int _tikTokCount;

    [ObservableProperty]
    private string _visibleTotalValueFormatted = "0 đ";

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

    partial void OnSearchTextChanged(string value)
    {
        if (!_suppressRefresh) ApplyFiltersAndSort();
    }

    partial void OnSelectedSortOptionChanged(SortOptionItem value)
    {
        if (!_suppressRefresh) ApplyFiltersAndSort();
    }

    [RelayCommand]
    private void OpenPhoto(ArchivedOrderRowViewModel? row)
    {
        if (row is null || !row.HasPhoto) return;
        _onOpenPhoto?.Invoke(row.PhotoId, row.OrderId, row.DisplayTracking);
    }

    public void LoadOrders(IReadOnlyList<Order> orders)
    {
        var rows = new List<ArchivedOrderRowViewModel>(orders.Count);
        int shopee = 0, tiktok = 0;

        for (var i = 0; i < orders.Count; i++)
        {
            var row = new ArchivedOrderRowViewModel(orders[i], i + 1);
            rows.Add(row);
            if (row.Platform.Equals(PlatformNames.Shopee, StringComparison.OrdinalIgnoreCase)) shopee++;
            else if (row.Platform.Equals(PlatformNames.TikTok, StringComparison.OrdinalIgnoreCase)) tiktok++;
        }

        var previousId = SelectedRow?.Source.Id;
        _allRows = rows;
        TotalCount = rows.Count;
        ShopeeCount = shopee;
        TikTokCount = tiktok;

        ApplyFiltersAndSort(previousId);
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
            SelectedSortOption = SortOptions[0];
        }
        finally
        {
            _suppressRefresh = false;
        }

        ApplyFiltersAndSort();
    }

    public void ApplyFiltersAndSort(string? preserveOrderId = null)
    {
        var sw = Stopwatch.StartNew();
        var kw = AlertThresholds.NormalizeForSearch(SearchText).Trim();
        var hasKw = kw.Length > 0;

        var filtered = new List<ArchivedOrderRowViewModel>(_allRows.Count);
        decimal totalVal = 0m;

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
            totalVal += row.Price;
        }

        var sortKey = SelectedSortOption?.Key ?? "default";
        switch (sortKey)
        {
            case "recv_desc":
                filtered.Sort((a, b) => CompareNullableDateDesc(a.RecvDateParsed, b.RecvDateParsed, a.Stt, b.Stt));
                break;
            case "recv_asc":
                filtered.Sort((a, b) => CompareNullableDateAsc(a.RecvDateParsed, b.RecvDateParsed, a.Stt, b.Stt));
                break;
            case "price_desc":
                filtered.Sort((a, b) =>
                {
                    var p = b.Price.CompareTo(a.Price);
                    return p != 0 ? p : a.Stt.CompareTo(b.Stt);
                });
                break;
        }

        sw.Stop();
        VisibleOrders = filtered;
        VisibleCount = filtered.Count;
        VisibleTotalValueFormatted = string.Format(ViCulture, "{0:N0} đ", totalVal);
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

        StatusBarText = $"Đang hiển thị {filtered.Count:N0} / {_allRows.Count:N0} đơn lưu trữ  •  Tổng giá trị: {VisibleTotalValueFormatted}  •  Lọc: {sw.ElapsedMilliseconds} ms";
    }

    public bool SelectOrder(string orderId, string track)
    {
        ResetAllFilters();

        var match = VisibleOrders.FirstOrDefault(r =>
            r.OrderId.Equals(orderId, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(track)
                || r.Source.Track.Equals(track, StringComparison.OrdinalIgnoreCase)
                || r.Source.ReturnTrack.Equals(track, StringComparison.OrdinalIgnoreCase)));

        if (match is null) return false;
        SelectedRow = match;
        return true;
    }

    [RelayCommand]
    public async Task UnarchiveRowAsync(ArchivedOrderRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is null || _editService is null) return;

        var result = await _editService.UnarchiveAsync(target.Source.Id);
        await OrderEditHelper.HandleResultAsync(result, _onDataChangedAsync, _onNotify);
    }

    [RelayCommand]
    public async Task DeleteRowAsync(ArchivedOrderRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is null || _editService is null || _dialogService is null) return;

        if (!_dialogService.ConfirmDeleteSingle(target.DisplayTracking, target.OrderId, "Kho lưu trữ"))
            return;

        var result = await _editService.DeleteAsync(target.Source.Id, "Kho lưu trữ");
        if (result.Kind == QuanLyHangHoan.Data.EditKind.Saved)
        {
            _onOrdersDeleted?.Invoke([(target.Source.Id, target.DisplayTracking)]);
        }
        await OrderEditHelper.HandleResultAsync(result, _onDataChangedAsync, _onNotify);
    }

    [RelayCommand]
    public async Task ShowHistoryAsync(ArchivedOrderRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is null || _editService is null || _dialogService is null) return;

        var entries = await _editService.GetHistoryAsync(target.Source.Id);
        _dialogService.ShowHistory(target.OrderId, target.DisplayTracking, entries);
    }

    [RelayCommand]
    public void OpenDetail(ArchivedOrderRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is null) return;
        _onOpenDetail?.Invoke(target.Source);
    }

    [RelayCommand]
    public Task HandleDeleteKeyAsync() => DeleteRowAsync(SelectedRow);

    private static int CompareNullableDateDesc(DateTime? a, DateTime? b, int sttA, int sttB)
    {
        if (a.HasValue && b.HasValue)
        {
            var cmp = b.Value.CompareTo(a.Value);
            return cmp != 0 ? cmp : sttA.CompareTo(sttB);
        }
        if (a.HasValue) return -1;
        if (b.HasValue) return 1;
        return sttA.CompareTo(sttB);
    }

    private static int CompareNullableDateAsc(DateTime? a, DateTime? b, int sttA, int sttB)
    {
        if (a.HasValue && b.HasValue)
        {
            var cmp = a.Value.CompareTo(b.Value);
            return cmp != 0 ? cmp : sttA.CompareTo(sttB);
        }
        if (a.HasValue) return -1;
        if (b.HasValue) return 1;
        return sttA.CompareTo(sttB);
    }
}
