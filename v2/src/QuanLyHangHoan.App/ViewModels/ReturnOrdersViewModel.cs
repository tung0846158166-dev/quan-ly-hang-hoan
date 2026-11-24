using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.ViewModels;

public sealed record SortOptionItem(string Key, string Label);

public enum ReturnTableScope
{
    All,
    ShopeeReturn,
    ShopeeCancelled,
    TikTokReturn,
    /// <summary>Bảng đơn mới: các đơn tạo tay / quét tạo từ điện thoại (đánh dấu NewOrderMarker), nằm sẵn ở bảng chính.</summary>
    NewCreated
}

public sealed partial class OrderTypeChipItem : ObservableObject
{
    public OrderTypeChipItem(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }
    public string Label { get; }

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _isSelected;

    public string DisplayText => $"{Label} ({Count:N0})";

    partial void OnCountChanged(int value)
    {
        OnPropertyChanged(nameof(DisplayText));
    }
}

public sealed partial class ReturnOrdersViewModel : ObservableObject
{
    private readonly IReturnOrderProvider _provider;
    private readonly AlertPolicy _alertPolicy;
    private readonly Action<string, string, string>? _onOpenPhoto;
    private readonly IOrderEditService? _editService;
    private readonly IOrderActionDialogService? _dialogService;
    private readonly Func<Task>? _onDataChangedAsync;
    private readonly Action<IReadOnlyList<(string OrderId, string Tracking)>>? _onOrdersDeleted;
    private readonly Action<string, bool>? _onNotify;
    private readonly Action<Order>? _onOpenDetail;
    private readonly Services.IOrderExportService? _exportService;
    private readonly Services.INewOrderCreator? _newOrderCreator;
    private readonly Services.INewOrderDialogService? _newOrderDialog;
    private List<ReturnOrderRowViewModel> _allRows = [];
    private IReadOnlyList<ReturnOrderRowViewModel> _selectedRows = Array.Empty<ReturnOrderRowViewModel>();
    private long _lastLoadMs;
    private bool _suppressFilterRefresh;

    public ReturnOrdersViewModel(
        IReturnOrderProvider provider,
        ReturnTableScope scope = ReturnTableScope.All,
        AlertPolicy? alertPolicy = null,
        Action<string, string, string>? onOpenPhoto = null,
        IOrderEditService? editService = null,
        IOrderActionDialogService? dialogService = null,
        Func<Task>? onDataChangedAsync = null,
        Action<IReadOnlyList<(string OrderId, string Tracking)>>? onOrdersDeleted = null,
        Action<string, bool>? onNotify = null,
        Action<Order>? onOpenDetail = null,
        Services.IOrderExportService? exportService = null,
        Services.INewOrderCreator? newOrderCreator = null,
        Services.INewOrderDialogService? newOrderDialog = null)
    {
        _provider = provider;
        _newOrderCreator = newOrderCreator;
        _newOrderDialog = newOrderDialog;
        _exportService = exportService;
        Scope = scope;
        _alertPolicy = alertPolicy ?? AlertThresholds.MainPolicy;
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
            new SortOptionItem("alert_desc", "Cảnh báo: Đỏ → Vàng → An toàn"),
            new SortOptionItem("return_desc", "Ngày hoàn (Mới → Cũ)"),
            new SortOptionItem("return_asc", "Ngày hoàn (Cũ → Mới)"),
            new SortOptionItem("ship_desc", "Ngày giao (Mới → Cũ)"),
            new SortOptionItem("price_desc", "Giá trị (Cao → Thấp)")
        ];
        _selectedSortOption = SortOptions[0];

        OrderTypeChips = BuildOrderTypeChips(scope);
        UpdateOrderTypeChipSelection();

        LoadOrders(_provider.GetReturnOrders(40));
    }

    public ReturnTableScope Scope { get; }
    public IReadOnlyList<SortOptionItem> SortOptions { get; }
    public IReadOnlyList<OrderTypeChipItem> OrderTypeChips { get; }

    [ObservableProperty]
    private bool _isDemoMode;

    public bool ShowPlatformFilter => Scope is ReturnTableScope.All or ReturnTableScope.NewCreated;
    public bool ShowCreateButton => Scope == ReturnTableScope.NewCreated;
    public bool ShowOrderTypeFilter => Scope is ReturnTableScope.TikTokReturn or ReturnTableScope.ShopeeReturn;

    public string ScreenTitle => Scope switch
    {
        ReturnTableScope.ShopeeReturn => "🟠 Shopee — Hoàn hàng",
        ReturnTableScope.ShopeeCancelled => "🚫 Shopee — Đơn hủy",
        ReturnTableScope.TikTokReturn => "⚫ TikTok — Hoàn hàng",
        ReturnTableScope.NewCreated => "📥 Bảng đơn mới",
        _ => "🚚 Bảng Hàng Hoàn Về"
    };

    public string ScreenSubtitle => Scope switch
    {
        ReturnTableScope.ShopeeReturn =>
            $"Bao gồm mọi đơn Shopee không phải \"{OrderTypeNames.Cancelled}\" (Trả hàng/Hoàn tiền & Giao không thành công) • {AlertRuleDescription}",
        ReturnTableScope.ShopeeCancelled =>
            $"Bộ lọc đơn Shopee có loại \"{OrderTypeNames.Cancelled}\" trên bảng đơn chung • {AlertRuleDescription}",
        ReturnTableScope.TikTokReturn =>
            $"Hiển thị tất cả loại đơn TikTok (THẤT THOÁT, Trả hàng/Hoàn tiền, Giao không thành công, Chỉ hoàn tiền) • {AlertRuleDescription}",
        ReturnTableScope.NewCreated =>
            $"Đơn tạo tay hoặc quét tạo từ điện thoại, nằm sẵn ở bảng chính. Bấm \"➕ Tạo đơn mới\" để thêm • {AlertRuleDescription}",
        _ => AlertRuleDescription
    };

    public string AlertRuleDescription =>
        $"Vàng > {AlertThresholds.YellowHours:0}h • Đỏ > {AlertThresholds.RedHours:0}h kể từ lúc hàng về chưa xử lý";

    [ObservableProperty]
    private IReadOnlyList<ReturnOrderRowViewModel> _visibleOrders = Array.Empty<ReturnOrderRowViewModel>();

    [ObservableProperty]
    private ReturnOrderRowViewModel? _selectedRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultipleSelected))]
    [NotifyPropertyChangedFor(nameof(HasAnySelected))]
    private int _selectedCount;

    public bool HasMultipleSelected => SelectedCount > 1;
    public bool HasAnySelected => SelectedCount > 0;
    public IReadOnlyList<ReturnOrderRowViewModel> SelectedRows => _selectedRows;

    // ---- Xuất Excel/CSV (chỉ đọc dữ liệu đơn; xem OrderExportService) ----
    [ObservableProperty] private string _exportStatus = "";
    [ObservableProperty] private bool _exportStatusIsError;
    [ObservableProperty] private bool _isExporting;

    [RelayCommand]
    private Task ExportVisibleAsync(string? format) => ExportAsync(VisibleOrders.Select(r => r.Source).ToList(), format);

    [RelayCommand]
    private Task ExportSelectedAsync(string? format) => ExportAsync(SelectedRows.Select(r => r.Source).ToList(), format);

    private async Task ExportAsync(IReadOnlyList<Order> orders, string? format)
    {
        if (_exportService is null || IsExporting) return;
        IsExporting = true;
        ExportStatus = "Đang xuất...";
        ExportStatusIsError = false;
        try
        {
            var fmt = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase) ? Services.ExportFormat.Csv : Services.ExportFormat.Xlsx;
            var r = await _exportService.ExportAsync(orders, SourceTableName, fmt);
            ExportStatus = r.Message;
            ExportStatusIsError = !r.Ok && !r.Cancelled;
            if (!r.Cancelled) _onNotify?.Invoke(r.Message, !r.Ok);
        }
        finally { IsExporting = false; }
    }

    public string SourceTableName => Scope switch
    {
        ReturnTableScope.ShopeeReturn => "Shopee - Hoàn hàng",
        ReturnTableScope.ShopeeCancelled => "Shopee - Đơn hủy",
        ReturnTableScope.TikTokReturn => "TikTok - Hoàn hàng",
        ReturnTableScope.NewCreated => "Bảng đơn mới",
        _ => "Bảng chính"
    };

    /// <summary>"➕ Tạo đơn mới": hỏi thông tin, tạo thẳng vào bảng chính, tải lại và chọn đúng đơn vừa tạo.</summary>
    [RelayCommand]
    public async Task CreateNewOrderAsync()
    {
        if (_newOrderCreator is null || _newOrderDialog is null) { _onNotify?.Invoke("Chưa sẵn sàng tạo đơn.", true); return; }
        var input = _newOrderDialog.Ask();
        if (input is null) return;                                   // hủy: không làm gì
        Data.NewOrderResult result;
        try { result = await _newOrderCreator.CreateAsync(input); }
        catch (Exception ex) { _onNotify?.Invoke("Không tạo được đơn: " + ex.Message, true); return; }
        if (!result.Ok)
        {
            _onNotify?.Invoke(result.Outcome == "duplicate" ? "⚠️ " + result.Message : "Không tạo được đơn: " + result.Message, true);
            return;
        }
        if (_onDataChangedAsync is not null) await _onDataChangedAsync();
        SelectOrder(result.OrderId, result.Code);
        _onNotify?.Invoke($"✅ Đã tạo đơn {result.Code} ({result.Platform}) ở Bảng đơn mới", false);
    }

    /// <summary>Giao diện nghe sự kiện này để bỏ chọn mọi dòng trên lưới (nút "Bỏ chọn", phím Esc).</summary>
    public event Action? ClearSelectionRequested;

    [RelayCommand]
    private void ClearSelection()
    {
        ClearSelectionRequested?.Invoke();
        // Không có giao diện (test/demo không hiển thị lưới): vẫn đặt lại trạng thái chọn.
        if (ClearSelectionRequested is null) UpdateSelectedRows(null);
    }

    public void UpdateSelectedRows(IEnumerable<ReturnOrderRowViewModel>? rows)
    {
        _selectedRows = rows?.ToList() ?? [];
        SelectedCount = _selectedRows.Count;
    }

    [ObservableProperty]
    private string _platformFilter = "all"; // "all" | PlatformNames.Shopee | PlatformNames.TikTok

    [ObservableProperty]
    private string _orderTypeFilter = "all";

    [ObservableProperty]
    private string _statusFilter = "all"; // "all" | "pending" | "received" | "yellow" | "red" | "lost" | "unneeded"

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private SortOptionItem _selectedSortOption;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string _statusBarText = string.Empty;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _visibleCount;

    [ObservableProperty]
    private int _shopeeCount;

    [ObservableProperty]
    private int _tikTokCount;

    [ObservableProperty]
    private int _pendingCount;

    [ObservableProperty]
    private int _receivedCount;

    [ObservableProperty]
    private int _yellowAlertCount;

    [ObservableProperty]
    private int _redAlertCount;

    [ObservableProperty]
    private int _lostCount;

    [ObservableProperty]
    private int _unneededCount;

    public bool IsPlatformAll => PlatformFilter == "all";
    public bool IsPlatformShopee => PlatformFilter == PlatformNames.Shopee;
    public bool IsPlatformTikTok => PlatformFilter == PlatformNames.TikTok;

    public bool IsStatusAll => StatusFilter == "all";
    public bool IsStatusPending => StatusFilter == "pending";
    public bool IsStatusReceived => StatusFilter == "received";
    public bool IsStatusYellow => StatusFilter == "yellow";
    public bool IsStatusRed => StatusFilter == "red";
    public bool IsStatusLost => StatusFilter == "lost";
    public bool IsStatusUnneeded => StatusFilter == "unneeded";

    public event Action? CountsUpdated;

    public static bool MatchesScope(Order order, ReturnTableScope scope)
    {
        return scope switch
        {
            ReturnTableScope.ShopeeReturn =>
                order.Platform.Equals(PlatformNames.Shopee, StringComparison.OrdinalIgnoreCase)
                && OrderTypeNames.IsShopeeReturn(order.OrderType),
            ReturnTableScope.ShopeeCancelled =>
                order.Platform.Equals(PlatformNames.Shopee, StringComparison.OrdinalIgnoreCase)
                && OrderTypeNames.IsCancelled(order.OrderType),
            ReturnTableScope.TikTokReturn =>
                order.Platform.Equals(PlatformNames.TikTok, StringComparison.OrdinalIgnoreCase),
            ReturnTableScope.NewCreated => NewOrderMarker.IsNewlyCreated(order),
            _ => true
        };
    }

    partial void OnPlatformFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsPlatformAll));
        OnPropertyChanged(nameof(IsPlatformShopee));
        OnPropertyChanged(nameof(IsPlatformTikTok));
        if (_suppressFilterRefresh) return;
        RecalculateStatusCounts();
        ApplyFiltersAndSort();
    }

    partial void OnOrderTypeFilterChanged(string value)
    {
        UpdateOrderTypeChipSelection();
        if (_suppressFilterRefresh) return;
        RecalculateStatusCounts();
        ApplyFiltersAndSort();
    }

    partial void OnStatusFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsStatusAll));
        OnPropertyChanged(nameof(IsStatusPending));
        OnPropertyChanged(nameof(IsStatusReceived));
        OnPropertyChanged(nameof(IsStatusYellow));
        OnPropertyChanged(nameof(IsStatusRed));
        OnPropertyChanged(nameof(IsStatusLost));
        OnPropertyChanged(nameof(IsStatusUnneeded));
        if (_suppressFilterRefresh) return;
        ApplyFiltersAndSort();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!_suppressFilterRefresh) ApplyFiltersAndSort();
    }

    partial void OnSelectedSortOptionChanged(SortOptionItem value)
    {
        if (!_suppressFilterRefresh) ApplyFiltersAndSort();
    }

    public void LoadOrders(IReadOnlyList<Order> orders, DateTime? nowOverride = null, AlertPolicy? policyOverride = null)
    {
        var sw = Stopwatch.StartNew();
        var now = nowOverride ?? _provider.GetCurrentTime();
        var policy = policyOverride ?? _alertPolicy;

        var rows = new List<ReturnOrderRowViewModel>(orders.Count);
        var stt = 0;
        for (var i = 0; i < orders.Count; i++)
        {
            if (!MatchesScope(orders[i], Scope)) continue;
            stt++;
            rows.Add(new ReturnOrderRowViewModel(orders[i], stt, now, policy));
        }

        var previousId = SelectedRow?.Source.Id;
        _allRows = rows;
        sw.Stop();
        _lastLoadMs = sw.ElapsedMilliseconds;

        RecalculatePlatformAndOrderTypeCounts();
        RecalculateStatusCounts();
        ApplyFiltersAndSort(previousId);
        CountsUpdated?.Invoke();
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
        RevealRequested?.Invoke(match);
        return true;
    }

    /// <summary>Dòng vừa được chọn bằng lệnh (từ Trang chủ/tìm kiếm) mà giao diện chưa cuộn tới. View xử lý rồi xóa.</summary>
    public ReturnOrderRowViewModel? PendingReveal { get; set; }

    /// <summary>Yêu cầu giao diện cuộn tới, đưa tiêu điểm và làm nổi bật dòng này.</summary>
    public event Action<ReturnOrderRowViewModel>? RevealRequested;

    [RelayCommand]
    private void SetPlatformFilter(string platform)
    {
        PlatformFilter = string.IsNullOrWhiteSpace(platform) ? "all" : platform;
    }

    [RelayCommand]
    private void SetOrderTypeFilter(string orderType)
    {
        OrderTypeFilter = string.IsNullOrWhiteSpace(orderType) ? "all" : orderType;
    }

    [RelayCommand]
    private void SetStatusFilter(string status)
    {
        StatusFilter = string.IsNullOrWhiteSpace(status) ? "all" : status;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    [RelayCommand]
    private void ResetAllFilters()
    {
        _suppressFilterRefresh = true;
        try
        {
            PlatformFilter = "all";
            OrderTypeFilter = "all";
            StatusFilter = "all";
            SearchText = string.Empty;
            SelectedSortOption = SortOptions[0];
        }
        finally
        {
            _suppressFilterRefresh = false;
        }

        RecalculateStatusCounts();
        ApplyFiltersAndSort();
    }

    [RelayCommand]
    private void OpenPhoto(ReturnOrderRowViewModel? row)
    {
        if (row is null || !row.HasPhoto) return;
        _onOpenPhoto?.Invoke(row.PhotoId, row.OrderId, row.DisplayTracking);
    }

    [RelayCommand]
    private Task LoadSample40Async() => LoadFakeBatchAsync(40);

    [RelayCommand]
    private Task LoadStress50kAsync() => LoadFakeBatchAsync(50_000);

    public async Task LoadFakeBatchAsync(int count, CancellationToken cancellationToken = default)
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusBarText = $"Đang sinh {count:N0} dòng dữ liệu giả...";
        try
        {
            var sw = Stopwatch.StartNew();
            var now = _provider.GetCurrentTime();
            var orders = await _provider.GetReturnOrdersAsync(count, cancellationToken);
            var rows = await Task.Run(() =>
            {
                var built = new List<ReturnOrderRowViewModel>(orders.Count);
                var stt = 0;
                for (var i = 0; i < orders.Count; i++)
                {
                    if (!MatchesScope(orders[i], Scope)) continue;
                    stt++;
                    built.Add(new ReturnOrderRowViewModel(orders[i], stt, now, _alertPolicy));
                }
                return built;
            }, cancellationToken);

            sw.Stop();
            _lastLoadMs = sw.ElapsedMilliseconds;
            _allRows = rows;

            RecalculatePlatformAndOrderTypeCounts();
            RecalculateStatusCounts();
            ApplyFiltersAndSort();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RecalculatePlatformAndOrderTypeCounts()
    {
        int shopee = 0, tiktok = 0;
        var typeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in _allRows)
        {
            if (row.Platform.Equals(PlatformNames.Shopee, StringComparison.OrdinalIgnoreCase)) shopee++;
            else if (row.Platform.Equals(PlatformNames.TikTok, StringComparison.OrdinalIgnoreCase)) tiktok++;

            var key = row.OrderType?.Trim() ?? string.Empty;
            if (key.Length > 0)
            {
                typeCounts[key] = typeCounts.TryGetValue(key, out var c) ? c + 1 : 1;
            }
        }

        TotalCount = _allRows.Count;
        ShopeeCount = shopee;
        TikTokCount = tiktok;

        foreach (var chip in OrderTypeChips)
        {
            chip.Count = chip.Key == "all"
                ? _allRows.Count
                : (typeCounts.TryGetValue(chip.Key, out var c) ? c : 0);
        }
    }

    private void RecalculateStatusCounts()
    {
        int pending = 0, received = 0, yellow = 0, red = 0, lost = 0, unneeded = 0;

        foreach (var row in _allRows)
        {
            if (!MatchesPlatform(row)) continue;
            if (!MatchesOrderType(row)) continue;

            if (row.Status.Equals(ReceiveStatus.Received, StringComparison.OrdinalIgnoreCase)
                || row.Status.Equals("Đã nhận", StringComparison.OrdinalIgnoreCase))
            {
                received++;
            }
            else if (row.Status.Equals(ReceiveStatus.NotNeeded, StringComparison.OrdinalIgnoreCase))
            {
                unneeded++;
            }
            else if (row.Status.Equals(ReceiveStatus.Lost, StringComparison.OrdinalIgnoreCase)
                || row.Status.Equals("Hư hỏng", StringComparison.OrdinalIgnoreCase))
            {
                lost++;
            }
            else
            {
                pending++;
                if (row.AlertLevel == AlertLevel.Yellow) yellow++;
                else if (row.AlertLevel == AlertLevel.Red) red++;
            }
        }

        PendingCount = pending;
        ReceivedCount = received;
        YellowAlertCount = yellow;
        RedAlertCount = red;
        LostCount = lost;
        UnneededCount = unneeded;

        CountsUpdated?.Invoke();
    }

    public void ApplyFiltersAndSort(string? preserveOrderId = null)
    {
        var sw = Stopwatch.StartNew();
        var kw = AlertThresholds.NormalizeForSearch(SearchText).Trim();
        var hasKw = kw.Length > 0;

        var filtered = new List<ReturnOrderRowViewModel>(_allRows.Count);
        foreach (var row in _allRows)
        {
            if (!MatchesPlatform(row)) continue;
            if (!MatchesOrderType(row)) continue;
            if (!MatchesStatus(row)) continue;
            if (hasKw && !row.SearchBlob.Contains(kw, StringComparison.Ordinal)) continue;
            filtered.Add(row);
        }

        var sortKey = SelectedSortOption?.Key ?? "default";
        switch (sortKey)
        {
            case "alert_desc":
                filtered.Sort((a, b) =>
                {
                    var p = b.AlertPriority.CompareTo(a.AlertPriority);
                    if (p != 0) return p;
                    var h = b.ElapsedHours.CompareTo(a.ElapsedHours);
                    return h != 0 ? h : a.Stt.CompareTo(b.Stt);
                });
                break;

            case "return_desc":
                filtered.Sort((a, b) => CompareNullableDateDesc(a.ReturnDateParsed, b.ReturnDateParsed, a.Stt, b.Stt));
                break;

            case "return_asc":
                filtered.Sort((a, b) => CompareNullableDateAsc(a.ReturnDateParsed, b.ReturnDateParsed, a.Stt, b.Stt));
                break;

            case "ship_desc":
                filtered.Sort((a, b) => CompareNullableDateDesc(a.ShipDateParsed, b.ShipDateParsed, a.Stt, b.Stt));
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
        StatusBarText =
            $"Đang hiển thị {filtered.Count:N0} / {_allRows.Count:N0} đơn  •  Nạp: {_lastLoadMs} ms  •  Lọc/Sắp xếp: {sw.ElapsedMilliseconds} ms  •  Virtualization: BẬT (Recycling)";
    }

    // ---------------- Thao tác thủ công trên đơn (T013) ----------------

    [RelayCommand]
    public async Task ChangeStatusAsync(ReturnOrderRowViewModel? row = null)
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
    public async Task EditUserNoteAsync(ReturnOrderRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is null || _editService is null || _dialogService is null) return;

        var newText = _dialogService.PromptMultilineText(
            "Ghi chú của tôi",
            $"Mã đơn: {target.OrderId}  •  MVĐ: {target.DisplayTracking}",
            "Nội dung ghi chú của tôi:",
            target.Source.UserNote);
        if (newText is null) return;

        var result = await _editService.SetFieldAsync(
            target.Source.Id,
            EditField.UserNote,
            newText,
            expectedOld: target.Source.UserNote);
        await OrderEditHelper.HandleResultAsync(result, _onDataChangedAsync, _onNotify);
    }

    [RelayCommand]
    public async Task EditNoteAsync(ReturnOrderRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is null || _editService is null || _dialogService is null) return;

        var newText = _dialogService.PromptMultilineText(
            "Sửa lý do",
            $"Mã đơn: {target.OrderId}  •  MVĐ: {target.DisplayTracking}",
            "Lý do hoàn / hủy:",
            target.Source.Note);
        if (newText is null) return;

        var result = await _editService.SetFieldAsync(
            target.Source.Id,
            EditField.Note,
            newText,
            expectedOld: target.Source.Note);
        await OrderEditHelper.HandleResultAsync(result, _onDataChangedAsync, _onNotify);
    }

    [RelayCommand]
    public async Task ToggleWatchlistAsync(ReturnOrderRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is null || _editService is null || _dialogService is null) return;

        var edited = _dialogService.PromptWatchlist(
            target.OrderId,
            target.DisplayTracking,
            target.IsWatchlist,
            target.Source.WatchlistReason);
        if (edited is null) return;

        var oldFlag = target.IsWatchlist ? "1" : "0";
        var newFlag = edited.Value.IsWatchlist ? "1" : "0";
        var flagResult = await _editService.SetFieldAsync(
            target.Source.Id,
            EditField.IsWatchlist,
            newFlag,
            expectedOld: oldFlag);

        if (flagResult.Kind is not (EditKind.Saved or EditKind.NoChange))
        {
            await OrderEditHelper.HandleResultAsync(flagResult, _onDataChangedAsync, _onNotify);
            return;
        }

        var reasonResult = await _editService.SetFieldAsync(
            target.Source.Id,
            EditField.WatchlistReason,
            edited.Value.Reason,
            expectedOld: target.Source.WatchlistReason);

        if (reasonResult.Kind == EditKind.NoChange && flagResult.Kind == EditKind.Saved)
        {
            if (_onDataChangedAsync is not null) await _onDataChangedAsync();
            return;
        }

        await OrderEditHelper.HandleResultAsync(reasonResult, _onDataChangedAsync, _onNotify);
    }

    [RelayCommand]
    public async Task ArchiveRowAsync(ReturnOrderRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is null || _editService is null) return;

        var result = await _editService.ArchiveAsync(target.Source.Id);
        await OrderEditHelper.HandleResultAsync(result, _onDataChangedAsync, _onNotify);
    }

    [RelayCommand]
    public async Task DeleteRowAsync(ReturnOrderRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is null || _editService is null || _dialogService is null) return;

        if (!_dialogService.ConfirmDeleteSingle(target.DisplayTracking, target.OrderId, SourceTableName))
            return;

        var result = await _editService.DeleteAsync(target.Source.Id, SourceTableName);
        if (result.Kind == EditKind.Saved)
        {
            _onOrdersDeleted?.Invoke([(target.Source.Id, target.DisplayTracking)]);
        }
        await OrderEditHelper.HandleResultAsync(result, _onDataChangedAsync, _onNotify);
    }

    [RelayCommand]
    public async Task ShowHistoryAsync(ReturnOrderRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is null || _editService is null || _dialogService is null) return;

        var entries = await _editService.GetHistoryAsync(target.Source.Id);
        _dialogService.ShowHistory(target.OrderId, target.DisplayTracking, entries);
    }

    [RelayCommand]
    public async Task BulkMarkReceivedAsync()
    {
        var targets = GetBulkTargets();
        if (targets.Count == 0 || _editService is null || _dialogService is null) return;

        if (!_dialogService.ConfirmBulkAction(
                "Đánh dấu đã nhận",
                targets.Count,
                $"Chuyển trạng thái {targets.Count:N0} kiện đang chọn sang \"{ReceiveStatus.Received}\"."))
            return;

        await OrderEditHelper.RunBulkAsync(
            targets,
            r => _editService.SetFieldAsync(r.Source.Id, EditField.Status, ReceiveStatus.Received, expectedOld: r.Status),
            _onDataChangedAsync,
            _onNotify);
    }

    [RelayCommand]
    public async Task BulkArchiveAsync()
    {
        var targets = GetBulkTargets();
        if (targets.Count == 0 || _editService is null || _dialogService is null) return;

        if (!_dialogService.ConfirmBulkAction(
                "Dời sang kho lưu trữ",
                targets.Count,
                $"Dời {targets.Count:N0} kiện đang chọn sang Kho lưu trữ."))
            return;

        await OrderEditHelper.RunBulkAsync(
            targets,
            r => _editService.ArchiveAsync(r.Source.Id),
            _onDataChangedAsync,
            _onNotify);
    }

    [RelayCommand]
    public async Task BulkDeleteAsync()
    {
        var targets = GetBulkTargets();
        if (targets.Count == 0 || _editService is null || _dialogService is null) return;

        if (!_dialogService.ConfirmBulkAction(
                "Xóa đơn hàng",
                targets.Count,
                $"Xóa {targets.Count:N0} kiện đang chọn khỏi \"{SourceTableName}\". Lưu ý: đơn vẫn khôi phục được ở màn Đã xóa."))
            return;

        await OrderEditHelper.RunBulkAsync(
            targets,
            r => _editService.DeleteAsync(r.Source.Id, SourceTableName),
            _onDataChangedAsync,
            _onNotify,
            savedRows => _onOrdersDeleted?.Invoke(savedRows.Select(r => (r.Source.Id, r.DisplayTracking)).ToList()));
    }

    [RelayCommand]
    public void OpenDetail(ReturnOrderRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is null) return;
        _onOpenDetail?.Invoke(target.Source);
    }

    [RelayCommand]
    public Task HandleDeleteKeyAsync()
    {
        if (SelectedRows.Count > 1)
        {
            return BulkDeleteAsync();
        }
        return DeleteRowAsync(SelectedRows.FirstOrDefault() ?? SelectedRow);
    }

    private IReadOnlyList<ReturnOrderRowViewModel> GetBulkTargets()
    {
        if (SelectedRows.Count > 0) return SelectedRows;
        return SelectedRow is not null ? [SelectedRow] : Array.Empty<ReturnOrderRowViewModel>();
    }

    private static IReadOnlyList<OrderTypeChipItem> BuildOrderTypeChips(ReturnTableScope scope)
    {
        return scope switch
        {
            ReturnTableScope.TikTokReturn =>
            [
                new OrderTypeChipItem("all", "Tất cả loại"),
                .. OrderTypeNames.TikTokTypes.Select(t => new OrderTypeChipItem(t, t))
            ],
            ReturnTableScope.ShopeeReturn =>
            [
                new OrderTypeChipItem("all", "Tất cả loại"),
                .. OrderTypeNames.ShopeeReturnTypes.Select(t => new OrderTypeChipItem(t, t))
            ],
            _ => []
        };
    }

    private void UpdateOrderTypeChipSelection()
    {
        foreach (var chip in OrderTypeChips)
        {
            chip.IsSelected = chip.Key.Equals(OrderTypeFilter, StringComparison.OrdinalIgnoreCase);
        }
    }

    private bool MatchesPlatform(ReturnOrderRowViewModel row)
    {
        if (PlatformFilter == "all") return true;
        return row.Platform.Equals(PlatformFilter, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesOrderType(ReturnOrderRowViewModel row)
    {
        if (OrderTypeFilter == "all") return true;
        return row.OrderType.Equals(OrderTypeFilter, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesStatus(ReturnOrderRowViewModel row)
    {
        return StatusFilter switch
        {
            "all" => true,
            "pending" => AlertThresholds.IsOrderPending(row.Status),
            "received" => row.Status.Equals(ReceiveStatus.Received, StringComparison.OrdinalIgnoreCase)
                       || row.Status.Equals("Đã nhận", StringComparison.OrdinalIgnoreCase),
            "yellow" => row.AlertLevel == AlertLevel.Yellow,
            "red" => row.AlertLevel == AlertLevel.Red,
            "lost" => row.Status.Equals(ReceiveStatus.Lost, StringComparison.OrdinalIgnoreCase)
                   || row.Status.Equals("Hư hỏng", StringComparison.OrdinalIgnoreCase),
            "unneeded" => row.Status.Equals(ReceiveStatus.NotNeeded, StringComparison.OrdinalIgnoreCase),
            _ => true
        };
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
