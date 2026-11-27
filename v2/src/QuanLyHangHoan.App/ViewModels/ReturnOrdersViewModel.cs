using System.Collections.ObjectModel;
using System.ComponentModel;
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
    NewCreated,
    /// <summary>Đơn ngoài sàn (Facebook, Zalo, tại shop...): chủ shop tự tạo bằng form tạo nhanh (27/11/2026).</summary>
    OffPlatform
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

public sealed partial class ReturnOrdersViewModel : ObservableObject, IPageVisibilityAware
{
    // trang ẩn: giữ trang đơn mới nhất, mở ra mới đưa lên bảng (27/11/2026: hết lag khi cập nhật)
    private bool _pageVisible = true;
    private IReadOnlyList<ReturnOrderRowViewModel>? _pendingVisible;
    public void SetPageVisible(bool visible)
    {
        _pageVisible = visible;
        if (visible && _pendingVisible is { } p) { _pendingVisible = null; VisibleOrders = p; }
    }

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
    private readonly WriteGate _writeGate;
    private readonly Func<Core.Stats.ProductCatalog>? _productCatalog;
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
        Services.INewOrderDialogService? newOrderDialog = null,
        bool loadInitialData = true,
        WriteGate? writeGate = null,
        Func<Core.Stats.ProductCatalog>? productCatalog = null)
    {
        _provider = provider;
        _productCatalog = productCatalog;
        _newOrderCreator = newOrderCreator;
        _newOrderDialog = newOrderDialog;
        _exportService = exportService;
        _writeGate = writeGate ?? WriteGate.AlwaysOpen;
        if (!ReferenceEquals(_writeGate, WriteGate.AlwaysOpen))
        {
            _writeGate.Changed += OnWriteGateChanged;
        }
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
            new SortOptionItem("import_desc", "Mới nạp (Mới → Cũ)"),   // mặc định: đơn mới nạp nhất lên đầu (chủ dự án 26/11/2026)
            new SortOptionItem("default", "Theo STT"),
            new SortOptionItem("alert_desc", "Cảnh báo: Đỏ → Vàng → An toàn"),
            new SortOptionItem("return_desc", "Ngày hoàn (Mới → Cũ)"),
            new SortOptionItem("return_asc", "Ngày hoàn (Cũ → Mới)"),
            new SortOptionItem("ship_desc", "Ngày giao (Mới → Cũ)"),
            new SortOptionItem("price_desc", "Giá trị (Cao → Thấp)")
        ];
        _selectedSortOption = SortOptions[0];

        OrderTypeChips = BuildOrderTypeChips(scope);
        UpdateOrderTypeChipSelection();

        if (loadInitialData) LoadOrders(_provider.GetReturnOrders(40));   // app thật: bỏ qua, MainViewModel nạp một lần duy nhất (tránh tải cả CSDL nhiều lần lúc khởi động)
    }

    public ReturnTableScope Scope { get; }
    public IReadOnlyList<SortOptionItem> SortOptions { get; }
    public IReadOnlyList<OrderTypeChipItem> OrderTypeChips { get; }
    public bool CanWrite => _writeGate.CanWrite;
    public string? WriteDisabledTooltip => _writeGate.TooltipReason;

    private void OnWriteGateChanged()
    {
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(WriteDisabledTooltip));
        CreateNewOrderCommand.NotifyCanExecuteChanged();
        ChangeStatusCommand.NotifyCanExecuteChanged();
        EditUserNoteCommand.NotifyCanExecuteChanged();
        EditNoteCommand.NotifyCanExecuteChanged();
        ToggleWatchlistCommand.NotifyCanExecuteChanged();
        ArchiveRowCommand.NotifyCanExecuteChanged();
        DeleteRowCommand.NotifyCanExecuteChanged();
        BulkMarkReceivedCommand.NotifyCanExecuteChanged();
        BulkArchiveCommand.NotifyCanExecuteChanged();
        BulkWatchlistCommand.NotifyCanExecuteChanged();
        BulkDeleteCommand.NotifyCanExecuteChanged();
        HandleDeleteKeyCommand.NotifyCanExecuteChanged();
        BulkComplainCommand.NotifyCanExecuteChanged();
        ComplainRowCommand.NotifyCanExecuteChanged();
    }


    /// <summary>Khiếu nại một đơn: trạng thái "Đang xử lý", đơn hiện ở bảng Khiếu nại (chủ dự án 26/11/2026).</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    public async Task ComplainRowAsync(ReturnOrderRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is null) return;
        await ComplainAsync([target]);
    }

    /// <summary>Khiếu nại TẤT CẢ các dòng đang chọn (đơn đã khiếu nại thì giữ nguyên trạng thái).</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    public Task BulkComplainAsync() => ComplainAsync(GetBulkTargets());

    private async Task ComplainAsync(IReadOnlyList<ReturnOrderRowViewModel> picked)
    {
        if (!CanWrite || _editService is null) return;
        var targets = picked.Where(r => r.ComplaintStatus.Length == 0).ToList();
        if (targets.Count == 0)
        {
            _onNotify?.Invoke(picked.Count == 0 ? "Chưa chọn đơn nào." : "Các đơn đã chọn đều đang ở bảng Khiếu nại.", false);
            return;
        }
        // chủ dự án 27/11/2026: khiếu nại phải có lý do (nhiều đơn: một lý do cho tất cả)
        var first = targets[0];
        var reason = ComplaintsViewModel.AskReason(_dialogService, "⚖️ Lý do khiếu nại",
            targets.Count == 1 ? $"Mã đơn: {first.Source.OrderId}  •  MVĐ: {first.Source.Track}" : $"{targets.Count:N0} đơn đang chọn (cùng một lý do) • trạng thái \"Đang xử lý\"",
            "Lý do khiếu nại (thiếu hàng, sai hàng, hàng hỏng, mất kiện...):", _onNotify);
        if (reason is null) return;
        var (saved, _, _) = await OrderEditHelper.RunBulkAsync(
            targets,
            r => _editService.SetComplaintAsync(r.Source.Id, ComplaintStatuses.Processing, reason, expectedOld: ""),
            _onDataChangedAsync,
            _onNotify);
        if (saved > 0) _onNotify?.Invoke($"Đã chuyển {saved:N0} đơn sang bảng ⚖️ Khiếu nại (thanh bên trái).", false);
    }

    [ObservableProperty]
    private bool _isDemoMode;

    public bool ShowPlatformFilter => Scope is ReturnTableScope.All or ReturnTableScope.NewCreated;
    public bool ShowCreateButton => Scope is ReturnTableScope.NewCreated or ReturnTableScope.OffPlatform;
    public string CreateButtonText => Scope == ReturnTableScope.OffPlatform ? "➕ Tạo đơn ngoài sàn (Ctrl+N)" : "➕ Tạo đơn mới";
    public bool ShowOrderTypeFilter => Scope is ReturnTableScope.TikTokReturn or ReturnTableScope.ShopeeReturn;

    public string ScreenTitle => Scope switch
    {
        ReturnTableScope.ShopeeReturn => "🟠 Shopee — Hoàn hàng",
        ReturnTableScope.ShopeeCancelled => "🚫 Shopee — Đơn hủy",
        ReturnTableScope.TikTokReturn => "⚫ TikTok — Hoàn hàng",
        ReturnTableScope.NewCreated => "📥 Bảng đơn mới",
        ReturnTableScope.OffPlatform => "🏪 Đơn ngoài sàn",
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
        ReturnTableScope.OffPlatform =>
            $"Đơn bán qua Facebook, Zalo, tại shop... (không có trên sàn). Tạo nhanh từ dữ liệu sản phẩm sẵn có; không mã vận đơn thì tự sinh mã NS... để quét nhận • {AlertRuleDescription}",
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
    private Task ExportVisibleAsync(string? format) => ExportAsync(_filtered.Select(r => r.Source).ToList(), format);   // xuất tất cả đơn đã lọc, không chỉ trang đang xem

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
        ReturnTableScope.OffPlatform => "Đơn ngoài sàn",
        _ => "Bảng chính"
    };

    /// <summary>"➕ Tạo đơn mới": hỏi thông tin, tạo thẳng vào bảng chính, tải lại và chọn đúng đơn vừa tạo.</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    public async Task CreateNewOrderAsync()
    {
        if (!CanWrite) return;
        if (_newOrderCreator is null || _newOrderDialog is null) { _onNotify?.Invoke("Chưa sẵn sàng tạo đơn.", true); return; }
        if (Scope == ReturnTableScope.OffPlatform) { await CreateOffPlatformAsync(); return; }
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

    /// <summary>
    /// Đơn ngoài sàn: mở form tạo nhanh (gợi ý sản phẩm từ dữ liệu sẵn có). Form tự gửi tạo từng đơn (có "Tạo & nhập đơn tiếp");
    /// đóng form thì tải lại một lần và chọn đơn vừa tạo cuối cùng.
    /// </summary>
    private async Task CreateOffPlatformAsync()
    {
        Core.Stats.ProductCatalog catalog;
        try { catalog = _productCatalog?.Invoke() ?? Core.Stats.ProductCatalog.Build(_allRows.Select(r => r.Source)); }
        catch { catalog = Core.Stats.ProductCatalog.Empty; }
        var form = new OffPlatformOrderFormViewModel(catalog) { Create = _newOrderCreator!.CreateAsync };
        await _newOrderDialog!.RunOffPlatformAsync(form);
        if (form.Created.Count == 0) return;
        if (_onDataChangedAsync is not null) await _onDataChangedAsync();
        var last = form.Created[^1];
        SelectOrder(last.OrderId, last.Code);
        _onNotify?.Invoke(form.Created.Count == 1 ? $"✅ Đã tạo đơn ngoài sàn {last.Code}" : $"✅ Đã tạo {form.Created.Count:N0} đơn ngoài sàn", false);
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

    // ---- Bộ lọc nâng cao (theo cột) ----
    [ObservableProperty] private bool _showAdvancedFilters;
    [ObservableProperty] private string _productFilter = string.Empty;
    [ObservableProperty] private string _carrierFilter = AllCarriers;
    [ObservableProperty] private string _photoFilter = "all"; // all | with | without
    [ObservableProperty] private DateTime? _dateFrom;
    [ObservableProperty] private DateTime? _dateTo;
    // lọc theo ngày nhận tại kho (chủ dự án 27/11/2026)
    [ObservableProperty] private DateTime? _recvDateFrom;
    [ObservableProperty] private DateTime? _recvDateTo;
    [ObservableProperty] private ObservableCollection<string> _carrierOptions = [AllCarriers];

    public const string AllCarriers = "Tất cả ĐVVC";

    // ---- Lọc theo cột "Trạng thái kiện" (trạng thái trên sàn), chủ dự án 26/11/2026 ----
    public const string AllPlatformStatuses = "Tất cả trạng thái kiện";
    public const string EmptyPlatformStatus = "(Trống)";
    [ObservableProperty] private string _platformStatusFilter = AllPlatformStatuses;
    [ObservableProperty] private ObservableCollection<string> _platformStatusOptions = [AllPlatformStatuses];
    partial void OnPlatformStatusFilterChanged(string value) => AdvancedFilterChanged();

    private void RebuildPlatformStatusOptions()
    {
        var all = _allRows.Select(r => r.PlatformStatus?.Trim() ?? string.Empty).ToList();
        var set = all.Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (all.Any(c => c.Length == 0)) set.Add(EmptyPlatformStatus);
        var keep = PlatformStatusFilter;
        if (PlatformStatusOptions.SequenceEqual([AllPlatformStatuses, .. set])) return;
        PlatformStatusOptions = new ObservableCollection<string>([AllPlatformStatuses, .. set]);
        PlatformStatusFilter = PlatformStatusOptions.Contains(keep) ? keep : AllPlatformStatuses;
    }
    public IReadOnlyList<string> PhotoOptions { get; } = ["Ảnh: tất cả", "Có ảnh", "Chưa có ảnh"];

    [ObservableProperty] private string _photoOption = "Ảnh: tất cả";

    /// <summary>Số bộ lọc nâng cao đang bật (hiện trên nút Bộ lọc).</summary>
    public int AdvancedFilterCount =>
        (ProductFilter.Trim().Length > 0 ? 1 : 0)
        + (CarrierFilter != AllCarriers ? 1 : 0)
        + (PlatformStatusFilter != AllPlatformStatuses ? 1 : 0)
        + (PhotoFilter != "all" ? 1 : 0)
        + (DateFrom.HasValue || DateTo.HasValue ? 1 : 0)
        + (RecvDateFrom.HasValue || RecvDateTo.HasValue ? 1 : 0);

    public string AdvancedFilterButtonText => AdvancedFilterCount == 0 ? "🔎 Bộ lọc" : $"🔎 Bộ lọc ({AdvancedFilterCount})";

    private void AdvancedFilterChanged()
    {
        OnPropertyChanged(nameof(AdvancedFilterCount));
        OnPropertyChanged(nameof(AdvancedFilterButtonText));
        if (!_suppressFilterRefresh) ApplyFiltersAndSort();
    }

    partial void OnProductFilterChanged(string value) => AdvancedFilterChanged();
    partial void OnCarrierFilterChanged(string value) => AdvancedFilterChanged();
    partial void OnPhotoFilterChanged(string value) => AdvancedFilterChanged();
    partial void OnDateFromChanged(DateTime? value) => AdvancedFilterChanged();
    partial void OnDateToChanged(DateTime? value) => AdvancedFilterChanged();
    partial void OnRecvDateFromChanged(DateTime? value) => AdvancedFilterChanged();
    partial void OnRecvDateToChanged(DateTime? value) => AdvancedFilterChanged();
    partial void OnPhotoOptionChanged(string value)
        => PhotoFilter = value == "Có ảnh" ? "with" : value == "Chưa có ảnh" ? "without" : "all";

    [RelayCommand]
    private void ToggleAdvancedFilters() => ShowAdvancedFilters = !ShowAdvancedFilters;

    private bool MatchesAdvanced(ReturnOrderRowViewModel row)
    {
        if (CarrierFilter != AllCarriers && !row.Carrier.Equals(CarrierFilter, StringComparison.OrdinalIgnoreCase)) return false;
        if (PlatformStatusFilter != AllPlatformStatuses)
        {
            var ps = row.PlatformStatus?.Trim() ?? string.Empty;
            if (PlatformStatusFilter == EmptyPlatformStatus ? ps.Length > 0 : !ps.Equals(PlatformStatusFilter, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (PhotoFilter == "with" && !row.HasPhoto) return false;
        if (PhotoFilter == "without" && row.HasPhoto) return false;
        if (_productKw.Length > 0 && !row.ItemsBlob.Contains(_productKw, StringComparison.Ordinal)) return false;
        if (DateFrom.HasValue || DateTo.HasValue)
        {
            var d = row.ReturnDateParsed ?? row.ArrivedAtDate ?? row.ShipDateParsed;
            if (d is null) return false;
            if (DateFrom.HasValue && d.Value.Date < DateFrom.Value.Date) return false;
            if (DateTo.HasValue && d.Value.Date > DateTo.Value.Date) return false;
        }
        if (RecvDateFrom.HasValue || RecvDateTo.HasValue)
        {
            var r = row.RecvDateParsed;
            if (r is null) return false;
            if (RecvDateFrom.HasValue && r.Value.Date < RecvDateFrom.Value.Date) return false;
            if (RecvDateTo.HasValue && r.Value.Date > RecvDateTo.Value.Date) return false;
        }
        return true;
    }

    private string _productKw = string.Empty;

    private void RebuildCarrierOptions()
    {
        var set = _allRows.Select(r => r.Carrier?.Trim() ?? string.Empty)
            .Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList();
        var keep = CarrierFilter;
        if (CarrierOptions.SequenceEqual([AllCarriers, .. set])) return;
        CarrierOptions = new ObservableCollection<string>([AllCarriers, .. set]);
        CarrierFilter = CarrierOptions.Contains(keep) ? keep : AllCarriers;
    }

    // ---- Bấm tiêu đề cột để sắp xếp: tự sắp bằng so sánh trực tiếp (nhanh với 50.000 dòng) ----
    private string? _colSortMember;
    private bool _colSortDesc;

    public bool SortByColumn(string? member, out ListSortDirection direction)
    {
        direction = ListSortDirection.Ascending;
        if (string.IsNullOrWhiteSpace(member) || ColumnComparison(member, false) is null) return false;
        if (_colSortMember == member) _colSortDesc = !_colSortDesc; else { _colSortMember = member; _colSortDesc = false; }
        direction = _colSortDesc ? ListSortDirection.Descending : ListSortDirection.Ascending;
        ApplyFiltersAndSort();
        return true;
    }

    private static Comparison<ReturnOrderRowViewModel>? ColumnComparison(string member, bool desc)
    {
        static int S(string? a, string? b) => string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
        static int D(DateTime? a, DateTime? b) => a.HasValue && b.HasValue ? a.Value.CompareTo(b.Value) : a.HasValue ? -1 : b.HasValue ? 1 : 0;
        Comparison<ReturnOrderRowViewModel>? c = member switch
        {
            "Stt" => (a, b) => a.Stt.CompareTo(b.Stt),
            "AlertPriority" => (a, b) => { var p = a.AlertPriority.CompareTo(b.AlertPriority); return p != 0 ? p : a.ElapsedHours.CompareTo(b.ElapsedHours); },
            "Platform" => (a, b) => S(a.Platform, b.Platform),
            "OrderType" => (a, b) => S(a.OrderType, b.OrderType),
            "OrderId" => (a, b) => S(a.OrderId, b.OrderId),
            "DisplayTracking" => (a, b) => S(a.DisplayTracking, b.DisplayTracking),
            "Status" => (a, b) => S(a.Status, b.Status),
            "PlatformStatus" => (a, b) => S(a.PlatformStatus, b.PlatformStatus),
            "ShipDateParsed" => (a, b) => D(a.ShipDateParsed, b.ShipDateParsed),
            "ReturnDateParsed" => (a, b) => D(a.ReturnDateParsed, b.ReturnDateParsed),
            "ArrivedAtDate" => (a, b) => D(a.ArrivedAtDate, b.ArrivedAtDate),
            "RecvDate" => (a, b) => S(a.RecvDate, b.RecvDate),
            "Carrier" => (a, b) => S(a.Carrier, b.Carrier),
            "ItemsSummary" => (a, b) => S(a.ItemsSummary, b.ItemsSummary),
            "TotalQty" => (a, b) => a.TotalQty.CompareTo(b.TotalQty),
            "Price" => (a, b) => a.Price.CompareTo(b.Price),
            "HasPhoto" => (a, b) => a.HasPhoto.CompareTo(b.HasPhoto),
            "NoteDisplay" => (a, b) => S(a.NoteDisplay, b.NoteDisplay),
            _ => null
        };
        if (c is null || !desc) return c;
        var asc = c;
        return (a, b) => asc(b, a);
    }

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
            ReturnTableScope.OffPlatform => Core.Models.OffPlatform.Is(order),
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
        _colSortMember = null;
        if (!_suppressFilterRefresh) ApplyFiltersAndSort();
    }

    /// <summary>Dựng các dòng hiển thị (phần nặng: tính ngày, chuỗi tìm kiếm...). KHÔNG đụng giao diện nên gọi được từ luồng nền; áp kết quả bằng <see cref="LoadRows"/>.</summary>
    /// <summary>Dòng dùng lại tối đa chừng này rồi dựng mới (cảnh báo vàng/đỏ tính theo giờ).</summary>
    public static readonly TimeSpan RowReuseWindow = TimeSpan.FromMinutes(5);

    public List<ReturnOrderRowViewModel> BuildRows(IReadOnlyList<Order> orders, DateTime now, AlertPolicy? policyOverride = null)
    {
        var policy = policyOverride ?? _alertPolicy;
        var scope = Scope;
        var rows = new List<ReturnOrderRowViewModel>(orders.Count);
        // dòng của lần nạp trước (đọc bản chụp; lần nạp mới chỉ thay ở luồng giao diện)
        var previous = new Dictionary<string, ReturnOrderRowViewModel>(StringComparer.Ordinal);
        if (policyOverride is null) foreach (var r in _allRows) previous.TryAdd(r.Source.Id, r);
        var stt = 0;
        for (var i = 0; i < orders.Count; i++)
        {
            if (!MatchesScope(orders[i], scope)) continue;
            stt++;
            var fp = ReturnOrderRowViewModel.FingerprintOf(orders[i]);
            // đơn không đổi, cùng số thứ tự, dựng chưa quá 5 phút (cảnh báo theo giờ vẫn đúng): dùng lại dòng cũ -> bảng không phải vẽ lại
            if (previous.TryGetValue(orders[i].Id, out var old) && old.Stt == stt && old.Fingerprint == fp
                && now >= old.BuiltAt && now - old.BuiltAt < RowReuseWindow)
                rows.Add(old);
            else
                rows.Add(new ReturnOrderRowViewModel(orders[i], stt, now, policy, fp));
        }
        return rows;
    }

    public void LoadOrders(IReadOnlyList<Order> orders, DateTime? nowOverride = null, AlertPolicy? policyOverride = null)
    {
        var sw = Stopwatch.StartNew();
        var now = nowOverride ?? _provider.GetCurrentTime();
        LoadRows(BuildRows(orders, now, policyOverride), sw);
    }

    /// <summary>Áp danh sách dòng đã dựng lên bảng (nhanh: đếm, lọc, sắp xếp, dựng trang đầu). Chạy ở luồng giao diện.</summary>
    public void LoadRows(List<ReturnOrderRowViewModel> rows, Stopwatch? sw = null)
    {
        sw ??= Stopwatch.StartNew();
        var previousId = SelectedRow?.Source.Id;
        _allRows = rows;
        RebuildCarrierOptions();
        RebuildPlatformStatusOptions();
        sw.Stop();
        _lastLoadMs = sw.ElapsedMilliseconds;

        RecalculatePlatformAndOrderTypeCounts();
        RecalculateStatusCounts();
        ApplyFiltersAndSort(previousId, resetPage: false);   // nạp lại dữ liệu: giữ nguyên trang đang xem
        CountsUpdated?.Invoke();
    }

    /// <summary>Tìm dòng theo mã đơn + mã vận đơn (hoặc mã nội bộ); nếu mã vận đơn lệch thì nhận khi mã đơn chỉ có một dòng.</summary>
    public static ReturnOrderRowViewModel? FindRow(IEnumerable<ReturnOrderRowViewModel> rows, string orderId, string track)
    {
        var list = rows as IReadOnlyList<ReturnOrderRowViewModel> ?? rows.ToList();
        bool TrackMatches(ReturnOrderRowViewModel r) =>
            string.IsNullOrWhiteSpace(track)
            || r.Source.Track.Equals(track, StringComparison.OrdinalIgnoreCase)
            || r.Source.ReturnTrack.Equals(track, StringComparison.OrdinalIgnoreCase)
            || r.Source.ForwardTrack.Equals(track, StringComparison.OrdinalIgnoreCase)
            || r.Source.AltTrack.Equals(track, StringComparison.OrdinalIgnoreCase);
        var sameOrder = list.Where(r => r.OrderId.Equals(orderId, StringComparison.OrdinalIgnoreCase)).ToList();
        return sameOrder.FirstOrDefault(TrackMatches)
               ?? list.FirstOrDefault(r => r.Source.Id.Equals(orderId, StringComparison.OrdinalIgnoreCase))
               ?? (sameOrder.Count == 1 ? sameOrder[0] : null);
    }

    /// <summary>Bảng này có chứa đơn đó không (không đổi bộ lọc, không chọn).</summary>
    public bool HasRow(string orderId, string track) => FindRow(_allRows, orderId, track) is not null;

    public bool SelectOrder(string orderId, string track)
    {
        ResetAllFilters();

        var match = FindRow(_filtered, orderId, track);

        if (match is null) return false;
        GoToPageNumber(_filtered.IndexOf(match) / PageSize + 1);   // nhảy tới đúng trang chứa đơn
        SelectedRow = match;
        PendingReveal = match;
        StatusBarText = $"📍 Đang chọn đơn {match.OrderId} • {match.DisplayTracking} — dòng này được tô nổi bật trong bảng";
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
            ProductFilter = string.Empty;
            CarrierFilter = AllCarriers;
            PlatformStatusFilter = AllPlatformStatuses;
            PhotoOption = "Ảnh: tất cả";
            DateFrom = null;
            DateTo = null;
            RecvDateFrom = null;
            RecvDateTo = null;
            _colSortMember = null;
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
            RebuildCarrierOptions();
            RebuildPlatformStatusOptions();

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

    public void ApplyFiltersAndSort(string? preserveOrderId = null, bool resetPage = true)
    {
        var sw = Stopwatch.StartNew();
        var kw = AlertThresholds.NormalizeForSearch(SearchText).Trim();
        var hasKw = kw.Length > 0;
        _productKw = AlertThresholds.NormalizeForSearch(ProductFilter).Trim();

        var filtered = new List<ReturnOrderRowViewModel>(_allRows.Count);
        foreach (var row in _allRows)
        {
            if (!MatchesPlatform(row)) continue;
            if (!MatchesOrderType(row)) continue;
            if (!MatchesStatus(row)) continue;
            if (!MatchesAdvanced(row)) continue;
            if (hasKw && !row.SearchBlob.Contains(kw, StringComparison.Ordinal)) continue;
            filtered.Add(row);
        }

        var sortKey = SelectedSortOption?.Key ?? "default";
        var colCmp = _colSortMember is null ? null : ColumnComparison(_colSortMember, _colSortDesc);
        if (colCmp is not null)
        {
            filtered.Sort((a, b) => { var r = colCmp(a, b); return r != 0 ? r : a.Stt.CompareTo(b.Stt); });
            sortKey = "column";
        }
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

            case "import_desc":
                // cùng mốc thời gian: dòng nạp sau (STT lớn hơn) lên trước
                filtered.Sort((a, b) => CompareNullableDateDesc(a.NewestKey, b.NewestKey, b.Stt, a.Stt));
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
        _filtered = filtered;
        TotalPages = Math.Max(1, (filtered.Count + PageSize - 1) / PageSize);
        if (resetPage) _currentPage = 1;
        RefreshPage(scrollTop: resetPage);
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
        _filterMs = sw.ElapsedMilliseconds;
        UpdateStatusText();
    }

    // ---------------- Phân trang: mỗi trang 100 đơn cho nhẹ ----------------

    /// <summary>Số đơn mỗi trang. Bảng chỉ dựng đúng chừng này dòng nên 50.000 đơn vẫn mượt.</summary>
    public const int PageSize = 100;

    private List<ReturnOrderRowViewModel> _filtered = [];
    private int _currentPage = 1;
    private long _filterMs;

    /// <summary>Tất cả đơn sau khi lọc (mọi trang), dùng để xuất file và tìm đơn.</summary>
    public IReadOnlyList<ReturnOrderRowViewModel> FilteredOrders => _filtered;

    /// <summary>Đặt khi chuyển trang: giao diện cuộn bảng về đầu thay vì giữ vị trí cũ.</summary>
    public bool ResetScrollOnNextVisibleChange { get; set; }

    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private string _pageInput = "1";
    [ObservableProperty] private string _pageInfoText = "";

    public int CurrentPage
    {
        get => _currentPage;
        set => GoToPageNumber(value);
    }

    public bool ShowPager => _filtered.Count > PageSize;
    public bool CanGoPrev => _currentPage > 1;
    public bool CanGoNext => _currentPage < TotalPages;

    /// <summary>Chuyển tới trang n (tự cắt về 1..TotalPages). Trả về trang thực sự đã chuyển tới.</summary>
    public int GoToPageNumber(int page)
    {
        var p = Math.Clamp(page, 1, TotalPages);
        if (p != _currentPage)
        {
            _currentPage = p;
            RefreshPage(scrollTop: true);
            UpdateStatusText();
        }
        else
        {
            PageInput = p.ToString();
        }
        return p;
    }

    private void RefreshPage(bool scrollTop)
    {
        _currentPage = Math.Clamp(_currentPage, 1, TotalPages);
        ResetScrollOnNextVisibleChange = scrollTop;
        var skip = (_currentPage - 1) * PageSize;
        var page = _filtered.Count <= PageSize ? _filtered : _filtered.Skip(skip).Take(PageSize).ToList();
        if (!SameRows(page, _pendingVisible ?? VisibleOrders))
        {
            if (_pageVisible) { _pendingVisible = null; VisibleOrders = page; }
            else _pendingVisible = page;
        }
        else ResetScrollOnNextVisibleChange = false;
        PageInput = _currentPage.ToString();
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(ShowPager));
        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
        FirstPageCommand.NotifyCanExecuteChanged();
        PrevPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        LastPageCommand.NotifyCanExecuteChanged();
    }

    private static bool SameRows(IReadOnlyList<ReturnOrderRowViewModel> a, IReadOnlyList<ReturnOrderRowViewModel> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++) if (!ReferenceEquals(a[i], b[i])) return false;
        return true;
    }

    private void UpdateStatusText()
    {
        var from = _filtered.Count == 0 ? 0 : (_currentPage - 1) * PageSize + 1;
        var to = Math.Min(_currentPage * PageSize, _filtered.Count);
        PageInfoText = $"Trang {_currentPage:N0}/{TotalPages:N0}";
        StatusBarText =
            $"Đang hiển thị đơn {from:N0}–{to:N0} / {_filtered.Count:N0} đơn đã lọc (tổng {_allRows.Count:N0})  •  Nạp: {_lastLoadMs} ms  •  Lọc/Sắp xếp: {_filterMs} ms";
    }

    [RelayCommand(CanExecute = nameof(CanGoPrev))]
    private void FirstPage() => GoToPageNumber(1);

    [RelayCommand(CanExecute = nameof(CanGoPrev))]
    private void PrevPage() => GoToPageNumber(_currentPage - 1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextPage() => GoToPageNumber(_currentPage + 1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void LastPage() => GoToPageNumber(TotalPages);

    /// <summary>Nhảy tới trang gõ trong ô số trang (nhấn Enter). Nhập sai thì trả ô về trang hiện tại.</summary>
    [RelayCommand]
    private void GoToPageInput()
    {
        if (int.TryParse(PageInput?.Trim().Replace(".", "").Replace(",", ""), out var n)) GoToPageNumber(n);
        else PageInput = _currentPage.ToString();
    }

    // ---------------- Thao tác thủ công trên đơn (T013) ----------------

    [RelayCommand(CanExecute = nameof(CanWrite))]
    public async Task ChangeStatusAsync(ReturnOrderRowViewModel? row = null)
    {
        if (!CanWrite) return;
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

    [RelayCommand(CanExecute = nameof(CanWrite))]
    public async Task EditUserNoteAsync(ReturnOrderRowViewModel? row = null)
    {
        if (!CanWrite) return;
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

    [RelayCommand(CanExecute = nameof(CanWrite))]
    public async Task EditNoteAsync(ReturnOrderRowViewModel? row = null)
    {
        if (!CanWrite) return;
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

    [RelayCommand(CanExecute = nameof(CanWrite))]
    public async Task ToggleWatchlistAsync(ReturnOrderRowViewModel? row = null)
    {
        if (!CanWrite) return;
        var target = row ?? SelectedRow;
        if (target is null || _editService is null || _dialogService is null) return;

        // Đang chọn nhiều dòng và bấm "Theo dõi" trên một dòng trong số đó: áp dụng cho TẤT CẢ dòng đang chọn
        // (trước đây chỉ dòng được bấm chuột phải được thêm, các dòng còn lại bị bỏ sót).
        if (HasMultipleSelected && _selectedRows.Contains(target))
        {
            await BulkWatchlistAsync();
            return;
        }

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

    [RelayCommand(CanExecute = nameof(CanWrite))]
    public async Task ArchiveRowAsync(ReturnOrderRowViewModel? row = null)
    {
        if (!CanWrite) return;
        var target = row ?? SelectedRow;
        if (target is null || _editService is null) return;

        var result = await _editService.ArchiveAsync(target.Source.Id);
        await OrderEditHelper.HandleResultAsync(result, _onDataChangedAsync, _onNotify);
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    public async Task DeleteRowAsync(ReturnOrderRowViewModel? row = null)
    {
        if (!CanWrite) return;
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

    [RelayCommand(CanExecute = nameof(CanWrite))]
    public async Task BulkMarkReceivedAsync()
    {
        if (!CanWrite) return;
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

    [RelayCommand(CanExecute = nameof(CanWrite))]
    public async Task BulkArchiveAsync()
    {
        if (!CanWrite) return;
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

    /// <summary>Thêm (hoặc bỏ) theo dõi cho TẤT CẢ các dòng đang chọn, hỏi lý do một lần.</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    public async Task BulkWatchlistAsync()
    {
        if (!CanWrite) return;
        var targets = GetBulkTargets();
        if (targets.Count == 0 || _editService is null || _dialogService is null) return;

        var edited = _dialogService.PromptWatchlist($"{targets.Count:N0} đơn đang chọn", "", true, "");
        if (edited is null) return;
        var newFlag = edited.Value.IsWatchlist ? "1" : "0";
        var reason = edited.Value.Reason;

        var (saved, _, _) = await OrderEditHelper.RunBulkAsync(
            targets,
            async r =>
            {
                var f = await _editService.SetFieldAsync(r.Source.Id, EditField.IsWatchlist, newFlag, expectedOld: r.IsWatchlist ? "1" : "0");
                if (f.Kind is not (EditKind.Saved or EditKind.NoChange)) return f;
                var rr = await _editService.SetFieldAsync(r.Source.Id, EditField.WatchlistReason, reason, expectedOld: r.Source.WatchlistReason);
                return rr.Kind == EditKind.NoChange ? f : rr;
            },
            _onDataChangedAsync,
            _onNotify);

        if (saved > 0)
            _onNotify?.Invoke(edited.Value.IsWatchlist
                ? $"Đã thêm {saved:N0} đơn vào mục ⭐ Đang theo dõi (thanh bên trái)."
                : $"Đã bỏ theo dõi {saved:N0} đơn.", false);
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    public async Task BulkDeleteAsync()
    {
        if (!CanWrite) return;
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

    [RelayCommand(CanExecute = nameof(CanWrite))]
    public Task HandleDeleteKeyAsync()
    {
        if (!CanWrite) return Task.CompletedTask;
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
