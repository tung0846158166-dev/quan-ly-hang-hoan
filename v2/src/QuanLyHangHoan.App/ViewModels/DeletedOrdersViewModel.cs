using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.App.ViewModels;

public sealed class DeletedOrderRowViewModel
{
    public DeletedOrderRowViewModel(DeletedOrderRecord source, int stt)
    {
        Source = source;
        Stt = stt;
        DeletedAtParsed = AlertThresholds.TryParseVietnamDateTime(source.DeletedAt);
        DisplayTracking = !string.IsNullOrWhiteSpace(source.ReturnTrack) ? source.ReturnTrack : source.Track;
        ItemsSummary = BuildItemsSummary(source.Items);
        SearchBlob = AlertThresholds.NormalizeForSearch(
            $"{source.OrderId} {source.Track} {source.ReturnTrack} {source.Platform} {source.DeletedFrom} {source.DeletedAt} {ItemsSummary} {source.DeleteReason}");
    }

    public DeletedOrderRecord Source { get; }
    public int Stt { get; }
    public string OrderId => Source.OrderId;
    public string Platform => Source.Platform;
    public string DisplayTracking { get; }
    public string ItemsSummary { get; }
    public string DeletedFrom => Source.DeletedFrom;
    public string DeletedAt => Source.DeletedAt;
    public DateTime? DeletedAtParsed { get; }
    public string DeleteReason => Source.DeleteReason;
    public string SearchBlob { get; }
    public string ItemsBlob => AlertThresholds.NormalizeForSearch(ItemsSummary);

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

public sealed partial class DeletedOrdersViewModel : ObservableObject
{
    private readonly IDeletedOrderProvider _provider;
    private readonly IOrderEditService? _editService;
    private readonly IOrderActionDialogService? _dialogService;
    private readonly Func<Task>? _onDataChangedAsync;
    private readonly Action<string, bool>? _onNotify;
    private readonly Action<Order>? _onOpenDetail;
    private List<DeletedOrderRowViewModel> _allRows = [];
    private bool _suppressRefresh;

    public DeletedOrdersViewModel(
        IDeletedOrderProvider provider,
        IOrderEditService? editService = null,
        IOrderActionDialogService? dialogService = null,
        Func<Task>? onDataChangedAsync = null,
        Action<string, bool>? onNotify = null,
        Action<Order>? onOpenDetail = null)
    {
        _provider = provider;
        _editService = editService;
        _dialogService = dialogService;
        _onDataChangedAsync = onDataChangedAsync;
        _onNotify = onNotify;
        _onOpenDetail = onOpenDetail;
        SortOptions =
        [
            new SortOptionItem("default", "Mặc định (STT)"),
            new SortOptionItem("deleted_desc", "Thời điểm xóa (Mới → Cũ)"),
            new SortOptionItem("deleted_asc", "Thời điểm xóa (Cũ → Mới)"),
            new SortOptionItem("source_asc", "Nguồn xóa (A → Z)")
        ];
        _selectedSortOption = SortOptions[0];
        Filters.Changed += () => { if (!_suppressRefresh) ApplyFiltersAndSort(); };

        LoadOrders(_provider.GetDeletedOrders());
    }

    public TableFilterState Filters { get; } = new("Ngày xóa");

    public IReadOnlyList<SortOptionItem> SortOptions { get; }

    [ObservableProperty]
    private bool _isDemoMode;

    [ObservableProperty]
    private IReadOnlyList<DeletedOrderRowViewModel> _visibleOrders = Array.Empty<DeletedOrderRowViewModel>();

    [ObservableProperty]
    private DeletedOrderRowViewModel? _selectedRow;

    [ObservableProperty]
    private string _sourceFilter = "all"; // "all" | "Bảng chính" | "Kho lưu trữ"

    [ObservableProperty]
    private string _platformFilter = "all"; // "all" | PlatformNames.Shopee | PlatformNames.TikTok

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
    private int _mainTableCount;

    [ObservableProperty]
    private int _archiveTableCount;

    [ObservableProperty]
    private int _shopeeCount;

    [ObservableProperty]
    private int _tikTokCount;

    [ObservableProperty]
    private string _statusBarText = string.Empty;

    public bool IsSourceAll => SourceFilter == "all";
    public bool IsSourceMain => SourceFilter == "Bảng chính";
    public bool IsSourceArchive => SourceFilter == "Kho lưu trữ";

    public bool IsPlatformAll => PlatformFilter == "all";
    public bool IsPlatformShopee => PlatformFilter == PlatformNames.Shopee;
    public bool IsPlatformTikTok => PlatformFilter == PlatformNames.TikTok;

    partial void OnSourceFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsSourceAll));
        OnPropertyChanged(nameof(IsSourceMain));
        OnPropertyChanged(nameof(IsSourceArchive));
        if (!_suppressRefresh) ApplyFiltersAndSort();
    }

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

    public void LoadOrders(IReadOnlyList<DeletedOrderRecord> records)
    {
        var rows = new List<DeletedOrderRowViewModel>(records.Count);
        int mainCount = 0, archiveCount = 0, shopeeCount = 0, tikTokCount = 0;

        for (var i = 0; i < records.Count; i++)
        {
            var row = new DeletedOrderRowViewModel(records[i], i + 1);
            rows.Add(row);

            if (row.DeletedFrom.Equals("Bảng chính", StringComparison.OrdinalIgnoreCase)) mainCount++;
            else if (row.DeletedFrom.Equals("Kho lưu trữ", StringComparison.OrdinalIgnoreCase)) archiveCount++;

            if (row.Platform.Equals(PlatformNames.Shopee, StringComparison.OrdinalIgnoreCase)) shopeeCount++;
            else if (row.Platform.Equals(PlatformNames.TikTok, StringComparison.OrdinalIgnoreCase)) tikTokCount++;
        }

        var previousId = SelectedRow?.Source.Id;
        _allRows = rows;
        TotalCount = rows.Count;
        MainTableCount = mainCount;
        ArchiveTableCount = archiveCount;
        ShopeeCount = shopeeCount;
        TikTokCount = tikTokCount;

        ApplyFiltersAndSort(previousId);
    }

    [RelayCommand]
    private void SetSourceFilter(string source)
    {
        SourceFilter = string.IsNullOrWhiteSpace(source) ? "all" : source;
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
            SourceFilter = "all";
            PlatformFilter = "all";
            SearchText = string.Empty;
            Filters.ResetSilently();
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

        var filtered = new List<DeletedOrderRowViewModel>(_allRows.Count);
        foreach (var row in _allRows)
        {
            if (SourceFilter != "all" && !row.DeletedFrom.Equals(SourceFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (PlatformFilter != "all" && !row.Platform.Equals(PlatformFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!Filters.Matches(row.ItemsBlob, row.DeletedAtParsed)) continue;
            if (hasKw && !row.SearchBlob.Contains(kw, StringComparison.Ordinal))
            {
                continue;
            }
            filtered.Add(row);
        }

        var sortKey = SelectedSortOption?.Key ?? "default";
        switch (sortKey)
        {
            case "deleted_desc":
                filtered.Sort((a, b) => CompareNullableDateDesc(a.DeletedAtParsed, b.DeletedAtParsed, a.Stt, b.Stt));
                break;
            case "deleted_asc":
                filtered.Sort((a, b) => CompareNullableDateAsc(a.DeletedAtParsed, b.DeletedAtParsed, a.Stt, b.Stt));
                break;
            case "source_asc":
                filtered.Sort((a, b) =>
                {
                    var cmp = string.Compare(a.DeletedFrom, b.DeletedFrom, StringComparison.OrdinalIgnoreCase);
                    return cmp != 0 ? cmp : a.Stt.CompareTo(b.Stt);
                });
                break;
        }

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

        StatusBarText = $"Đang hiển thị {filtered.Count:N0} / {_allRows.Count:N0} đơn đã xóa (chặn nạp lại)  •  Lọc: {sw.ElapsedMilliseconds} ms";
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
        PendingReveal = match;
        StatusBarText = $"📍 Đang chọn đơn {match.OrderId} • {match.DisplayTracking} — dòng này được tô nổi bật trong bảng";
        RevealRequested?.Invoke(match);
        return true;
    }

    /// <summary>Dòng vừa được chọn bằng lệnh mà giao diện chưa cuộn tới (View xử lý rồi xóa).</summary>
    public DeletedOrderRowViewModel? PendingReveal { get; set; }

    /// <summary>Yêu cầu giao diện cuộn tới và làm nổi bật dòng này.</summary>
    public event Action<DeletedOrderRowViewModel>? RevealRequested;

    [RelayCommand]
    public void OpenDetail(DeletedOrderRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is null) return;

        var order = target.Source.FullOrder ?? new Order
        {
            Id = target.Source.Id,
            OrderId = target.Source.OrderId,
            Track = target.Source.Track,
            ReturnTrack = target.Source.ReturnTrack,
            Platform = target.Source.Platform,
            Note = target.Source.DeleteReason,
            Items = target.Source.Items
        };
        _onOpenDetail?.Invoke(order);
    }

    [RelayCommand]
    public async Task RestoreRowAsync(DeletedOrderRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is null || _editService is null) return;

        var result = await _editService.RestoreAsync(target.Source.Id);
        if (result.Kind == QuanLyHangHoan.Data.EditKind.Saved)
        {
            _onNotify?.Invoke($"Đã khôi phục đơn {target.DisplayTracking}.", false);
        }
        await OrderEditHelper.HandleResultAsync(result, _onDataChangedAsync, _onNotify);
    }

    [RelayCommand]
    public async Task ShowHistoryAsync(DeletedOrderRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is null || _editService is null || _dialogService is null) return;

        var entries = await _editService.GetHistoryAsync(target.Source.Id);
        _dialogService.ShowHistory(target.OrderId, target.DisplayTracking, entries);
    }

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
