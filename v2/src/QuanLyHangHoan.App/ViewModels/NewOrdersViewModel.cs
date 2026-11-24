using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.App.ViewModels;

public sealed class NewOrderRowViewModel
{
    public NewOrderRowViewModel(NewOrderRecord source, int stt)
    {
        Source = source;
        Stt = stt;
        ScannedAtParsed = AlertThresholds.TryParseVietnamDateTime(source.ScannedAt);
        PhotoIndicator = string.IsNullOrWhiteSpace(source.PhotoId) ? "—" : "📷 Có ảnh";
        SelectionStatusText = source.IsSelectedForImport ? "Đã chọn duyệt" : "Chờ duyệt";
        SearchBlob = AlertThresholds.NormalizeForSearch(
            $"{source.Track} {source.Platform} {source.Carrier} {source.Note} {source.ScannedAt} {SelectionStatusText}");
    }

    public NewOrderRecord Source { get; }
    public int Stt { get; }
    public string ScannedAt => Source.ScannedAt;
    public DateTime? ScannedAtParsed { get; }
    public string Platform => Source.Platform;
    public string Track => Source.Track;
    public string Carrier => Source.Carrier;
    public string Note => Source.Note;
    public string PhotoIndicator { get; }
    public bool IsSelectedForImport => Source.IsSelectedForImport;
    public bool CanOpenDetail => Source.FullOrder is not null;
    public string SelectionStatusText { get; }
    public string SearchBlob { get; }
}

public sealed partial class NewOrdersViewModel : ObservableObject
{
    private readonly INewOrderProvider _provider;
    private readonly Action<Order>? _onOpenDetail;
    private List<NewOrderRowViewModel> _allRows = [];
    private bool _suppressRefresh;

    public NewOrdersViewModel(INewOrderProvider provider, Action<Order>? onOpenDetail = null, bool loadInitialData = true)
    {
        _provider = provider;
        _onOpenDetail = onOpenDetail;
        SortOptions =
        [
            new SortOptionItem("default", "Mặc định (STT)"),
            new SortOptionItem("time_desc", "Thời gian quét (Mới → Cũ)"),
            new SortOptionItem("time_asc", "Thời gian quét (Cũ → Mới)"),
            new SortOptionItem("platform_asc", "Sàn TMĐT (A → Z)")
        ];
        _selectedSortOption = SortOptions[0];

        if (loadInitialData) LoadOrders(_provider.GetNewOrders());
    }

    public IReadOnlyList<SortOptionItem> SortOptions { get; }

    [ObservableProperty]
    private bool _isDemoMode;

    [ObservableProperty]
    private IReadOnlyList<NewOrderRowViewModel> _visibleOrders = Array.Empty<NewOrderRowViewModel>();

    [ObservableProperty]
    private NewOrderRowViewModel? _selectedRow;

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
    private int _selectedForImportCount;

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

    public void LoadOrders(IReadOnlyList<NewOrderRecord> records)
    {
        var rows = new List<NewOrderRowViewModel>(records.Count);
        int shopee = 0, tiktok = 0, selected = 0;

        for (var i = 0; i < records.Count; i++)
        {
            var row = new NewOrderRowViewModel(records[i], i + 1);
            rows.Add(row);
            if (row.Platform.Equals(PlatformNames.Shopee, StringComparison.OrdinalIgnoreCase)) shopee++;
            else if (row.Platform.Equals(PlatformNames.TikTok, StringComparison.OrdinalIgnoreCase)) tiktok++;
            if (row.IsSelectedForImport) selected++;
        }

        _allRows = rows;
        TotalCount = rows.Count;
        ShopeeCount = shopee;
        TikTokCount = tiktok;
        SelectedForImportCount = selected;

        ApplyFiltersAndSort();
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

    public void ApplyFiltersAndSort()
    {
        var sw = Stopwatch.StartNew();
        var kw = AlertThresholds.NormalizeForSearch(SearchText).Trim();
        var hasKw = kw.Length > 0;

        var filtered = new List<NewOrderRowViewModel>(_allRows.Count);
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

        var sortKey = SelectedSortOption?.Key ?? "default";
        switch (sortKey)
        {
            case "time_desc":
                filtered.Sort((a, b) => CompareNullableDateDesc(a.ScannedAtParsed, b.ScannedAtParsed, a.Stt, b.Stt));
                break;
            case "time_asc":
                filtered.Sort((a, b) => CompareNullableDateAsc(a.ScannedAtParsed, b.ScannedAtParsed, a.Stt, b.Stt));
                break;
            case "platform_asc":
                filtered.Sort((a, b) =>
                {
                    var cmp = string.Compare(a.Platform, b.Platform, StringComparison.OrdinalIgnoreCase);
                    return cmp != 0 ? cmp : a.Stt.CompareTo(b.Stt);
                });
                break;
        }

        sw.Stop();
        VisibleOrders = filtered;
        VisibleCount = filtered.Count;
        IsEmpty = filtered.Count == 0;
        if (SelectedRow is not null && !filtered.Contains(SelectedRow))
        {
            SelectedRow = null;
        }

        StatusBarText = $"Đang hiển thị {filtered.Count:N0} / {_allRows.Count:N0} đơn mới quét  •  Đã chọn duyệt: {SelectedForImportCount:N0}  •  Lọc: {sw.ElapsedMilliseconds} ms";
    }

    [RelayCommand]
    public void OpenDetail(NewOrderRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target?.Source.FullOrder is null) return;
        _onOpenDetail?.Invoke(target.Source.FullOrder);
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
