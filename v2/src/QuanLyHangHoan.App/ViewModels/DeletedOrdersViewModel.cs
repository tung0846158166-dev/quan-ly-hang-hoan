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

public sealed partial class DeletedOrdersViewModel : ObservableObject, IPageVisibilityAware
{
    private readonly PageGate<DeletedOrderRowViewModel> _pageGate;
    public void SetPageVisible(bool visible) => _pageGate.SetVisible(visible);

    private readonly IDeletedOrderProvider _provider;
    private readonly IOrderEditService? _editService;
    private readonly IOrderActionDialogService? _dialogService;
    private readonly Func<Task>? _onDataChangedAsync;
    private readonly Action<string, bool>? _onNotify;
    private readonly Action<Order>? _onOpenDetail;
    private readonly WriteGate _writeGate;
    private List<DeletedOrderRowViewModel> _allRows = [];
    private bool _suppressRefresh;

    public DeletedOrdersViewModel(
        IDeletedOrderProvider provider,
        IOrderEditService? editService = null,
        IOrderActionDialogService? dialogService = null,
        Func<Task>? onDataChangedAsync = null,
        Action<string, bool>? onNotify = null,
        Action<Order>? onOpenDetail = null,
        bool loadInitialData = true,
        WriteGate? writeGate = null)
    {
        _provider = provider;
        _editService = editService;
        _dialogService = dialogService;
        _onDataChangedAsync = onDataChangedAsync;
        _onNotify = onNotify;
        _onOpenDetail = onOpenDetail;
        _writeGate = writeGate ?? WriteGate.AlwaysOpen;
        if (!ReferenceEquals(_writeGate, WriteGate.AlwaysOpen))
        {
            _writeGate.Changed += OnWriteGateChanged;
        }
        SortOptions =
        [
            new SortOptionItem("default", "Mặc định (STT)"),
            new SortOptionItem("deleted_desc", "Thời điểm xóa (Mới → Cũ)"),
            new SortOptionItem("deleted_asc", "Thời điểm xóa (Cũ → Mới)"),
            new SortOptionItem("source_asc", "Nguồn xóa (A → Z)")
        ];
        _selectedSortOption = SortOptions[0];
        _pageGate = new PageGate<DeletedOrderRowViewModel>(p => VisibleOrders = p);   // trang ẩn: giữ danh sách, mở ra mới đưa lên bảng (27/11/2026)
        Pager = new Pager<DeletedOrderRowViewModel>(_pageGate.Push);
        Filters.Changed += () => { if (!_suppressRefresh) ApplyFiltersAndSort(); };

        if (loadInitialData) LoadOrders(_provider.GetDeletedOrders());
    }

    public TableFilterState Filters { get; } = new("Ngày xóa");

    public IReadOnlyList<SortOptionItem> SortOptions { get; }

    /// <summary>Chia trang 100 đơn/trang.</summary>
    public Pager<DeletedOrderRowViewModel> Pager { get; private set; } = null!;

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

        ApplyFiltersAndSort(previousId, resetPage: false);
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

    public void ApplyFiltersAndSort(string? preserveOrderId = null, bool resetPage = true)
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
        Pager.SetItems(filtered, resetPage);
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

        StatusBarText = $"Đang hiển thị {Pager.RangeText} đơn đã xóa (chặn nạp lại)  •  Lọc: {sw.ElapsedMilliseconds} ms";
    }

    public bool SelectOrder(string orderId, string track)
    {
        ResetAllFilters();

        var match = Pager.All.FirstOrDefault(r =>
            r.OrderId.Equals(orderId, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(track)
                || r.Source.Track.Equals(track, StringComparison.OrdinalIgnoreCase)
                || r.Source.ReturnTrack.Equals(track, StringComparison.OrdinalIgnoreCase)));

        if (match is null) return false;
        Pager.GoToItem(match);   // nhảy tới đúng trang chứa đơn
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

    public bool CanWrite => _writeGate.CanWrite;
    public string? WriteDisabledTooltip => _writeGate.TooltipReason;

    private void OnWriteGateChanged()
    {
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(WriteDisabledTooltip));
        RestoreRowCommand.NotifyCanExecuteChanged();
        PurgeRowCommand.NotifyCanExecuteChanged();
        PurgeAllShownCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// XÓA VĨNH VIỄN một đơn (chủ dự án 28/11/2026): không còn hiện ở đâu, không khôi phục từ phần mềm, mã quét / nạp lại như chưa từng có.
    /// Chỉ máy chủ shop (cloud kiểm). Dữ liệu vẫn nằm trong bản sao lưu cloud hằng ngày.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    public async Task PurgeRowAsync(DeletedOrderRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (!CanWrite || target is null || _editService is null) return;
        if (_dialogService is not null && !_dialogService.ConfirmBulkAction("Xóa vĩnh viễn", 1,
                $"Xóa vĩnh viễn đơn {target.DisplayTracking} (mã đơn {target.OrderId}). Đơn sẽ không còn hiện ở đâu và KHÔNG khôi phục được từ phần mềm."))
            return;
        var r = await _editService.PurgeAsync(target.Source.Id);
        if (r.Kind == QuanLyHangHoan.Data.EditKind.Saved) _onNotify?.Invoke($"Đã xóa vĩnh viễn đơn {target.DisplayTracking}.", false);
        await OrderEditHelper.HandleResultAsync(r, _onDataChangedAsync, _onNotify);
    }

    /// <summary>Xóa vĩnh viễn MỌI đơn đang hiển thị theo bộ lọc hiện tại (hỏi xác nhận, có số lượng).</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    public async Task PurgeAllShownAsync()
    {
        var targets = Pager.All.ToList();
        if (!CanWrite || targets.Count == 0 || _editService is null) return;
        if (_dialogService is not null && !_dialogService.ConfirmBulkAction("Xóa vĩnh viễn", targets.Count,
                $"Xóa vĩnh viễn {targets.Count:N0} đơn đang hiển thị ở mục Đã xóa. Các đơn này sẽ không còn hiện ở đâu và KHÔNG khôi phục được từ phần mềm."))
            return;
        int ok = 0, fail = 0;
        string? lastError = null;
        foreach (var t in targets)
        {
            var r = await _editService.PurgeAsync(t.Source.Id);
            if (r.Kind == QuanLyHangHoan.Data.EditKind.Saved) ok++; else { fail++; lastError = r.Message; }
        }
        _onNotify?.Invoke(fail == 0 ? $"Đã xóa vĩnh viễn {ok:N0} đơn." : $"Đã xóa vĩnh viễn {ok:N0} đơn, {fail:N0} đơn không xóa được: {lastError}", fail > 0);
        if (_onDataChangedAsync is not null) await _onDataChangedAsync();
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    public async Task RestoreRowAsync(DeletedOrderRowViewModel? row = null)
    {
        if (!CanWrite) return;
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
