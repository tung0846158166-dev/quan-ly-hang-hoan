using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.ViewModels;

public sealed class ReturnWatchRowViewModel
{
    public ReturnWatchRowViewModel(ReturnWatchEntry entry, int stt, DateTime now, AlertPolicy? policy = null)
    {
        Entry = entry;
        Source = entry.Stored.Order;
        Stt = stt;
        Since = entry.Since;
        Stage = entry.Stage;

        SinceFormatted = DateTimeOffset.FromUnixTimeMilliseconds(entry.Since)
            .ToLocalTime()
            .ToString("dd/MM/yyyy HH:mm");

        StageLabel = Stage >= 1 && Stage < ReturnWatchService.StageNames.Length
            ? ReturnWatchService.StageNames[Stage]
            : "—";

        PlatformStatusLine = string.IsNullOrWhiteSpace(Source.PlatformStatus)
            ? "Sàn: —"
            : $"Sàn: {Source.PlatformStatus}";

        DisplayTracking = !string.IsNullOrWhiteSpace(Source.ReturnTrack)
            ? Source.ReturnTrack
            : (!string.IsNullOrWhiteSpace(Source.Track) ? Source.Track : "Chưa có MVĐ");

        ItemsSummary = BuildItemsSummary(Source.Items, Source.IsPartialReturn);
        ItemLines = ItemDisplay.Build(Source.Items);
        PartialChip = ItemDisplay.Chip(Source.Items, Source.IsPartialReturn);
        ItemsTooltip = ItemDisplay.Tooltip(Source.Items, Source.IsPartialReturn);
        ArrivedAtDate = AlertThresholds.TryParseVietnamDateTime(Source.ReturnedAt);

        var activePolicy = policy ?? AlertThresholds.MainPolicy;
        if (Stage == 3)
        {
            AlertLevel = activePolicy.Evaluate(ArrivedAtDate, received: false, now);
            if (ArrivedAtDate is not null && AlertLevel is AlertLevel.Safe or AlertLevel.Yellow or AlertLevel.Red)
            {
                ElapsedText = FormatElapsed(now - ArrivedAtDate.Value);
            }
            else
            {
                ElapsedText = string.Empty;
            }
        }
        else if (Stage == 4)
        {
            AlertLevel = AlertLevel.Done;
            ElapsedText = string.Empty;
        }
        else
        {
            AlertLevel = AlertLevel.None;
            ElapsedText = string.Empty;
        }

        AlertBadgeText = AlertLevel switch
        {
            AlertLevel.Red => $"🚨 Quá {AlertThresholds.RedHours:0}h ({ElapsedText})",
            AlertLevel.Yellow => $"⚠️ Quá {AlertThresholds.YellowHours:0}h ({ElapsedText})",
            AlertLevel.Safe => $"⏳ Chờ nhận ({ElapsedText})",
            AlertLevel.Done => "✅ Đã nhận kho",
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

        var allProducts = Source.Items.Count > 0
            ? string.Join(" ", Source.Items.Select(i => $"{i.Product} {i.Color} {i.Size}"))
            : string.Empty;

        SearchBlob = AlertThresholds.NormalizeForSearch(
            $"{Source.OrderId} {Source.ReturnOrderId} {Source.Track} {Source.ReturnTrack} {Source.AltTrack} {Source.ForwardTrack} {allProducts}");
    }

    public ReturnWatchEntry Entry { get; }
    public Order Source { get; }
    public string OrderIdKey => Source.Id;
    public int Stt { get; set; }
    public string OrderId => Source.OrderId;
    public string Platform => Source.Platform;
    public string DisplayTracking { get; }
    public string ItemsSummary { get; }
    public IReadOnlyList<ItemLine> ItemLines { get; }
    public string PartialChip { get; }
    public string ItemsTooltip { get; }
    public long Since { get; }
    public string SinceFormatted { get; }
    public int Stage { get; }
    public string StageLabel { get; }
    public string PlatformStatusLine { get; }
    public string ReturnedAt => Source.ReturnedAt;
    public DateTime? ArrivedAtDate { get; }
    public string RecvDate => Source.RecvDate;
    public AlertLevel AlertLevel { get; }
    public int AlertPriority { get; }
    public string ElapsedText { get; }
    public string AlertBadgeText { get; }
    public string SearchBlob { get; }

    public bool IsStage1Done => Stage >= 1;
    public bool IsStage2Done => Stage >= 2;
    public bool IsStage3Done => Stage >= 3;
    public bool IsStage4Done => Stage >= 4;

    public bool IsStage1Current => Stage == 1;
    public bool IsStage2Current => Stage == 2;
    public bool IsStage3Current => Stage == 3;
    public bool IsStage4Current => Stage == 4;

    private static string BuildItemsSummary(IReadOnlyList<OrderItem> items, bool isPartialReturn)
    {
        if (items.Count == 0) return "Chưa có sản phẩm";
        var first = items[0];
        var variant = string.Join("/", new[] { first.Color, first.Size }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var firstText = string.IsNullOrWhiteSpace(variant)
            ? $"{first.Product} (x{first.Qty})"
            : $"{first.Product} ({variant} x{first.Qty})";
        var prefix = isPartialReturn ? "[1 phần] " : string.Empty;
        return items.Count == 1
            ? $"{prefix}{firstText}"
            : $"{prefix}{firstText} +{items.Count - 1} món";
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

public sealed partial class ReturnWatchViewModel : ObservableObject, IPageVisibilityAware
{
    private readonly PageGate<ReturnWatchRowViewModel> _pageGate;
    public void SetPageVisible(bool visible) => _pageGate.SetVisible(visible);

    public const int MaxDisplayRows = 1500;

    private readonly IReturnWatchService _service;
    private readonly Func<DateTime> _clock;
    private readonly Func<string, string, bool> _confirmDialog;
    private readonly Func<Task>? _onDataChangedAsync;
    private readonly Action<IReadOnlyList<(string OrderId, string Tracking)>, string?>? _onOrdersRemoved;
    private readonly Action<string, bool>? _onNotify;
    private readonly Action<Order>? _onOpenDetail;
    private readonly Action<Order>? _onGoToMainTable;
    private readonly AlertPolicy _alertPolicy;
    private readonly WriteGate _writeGate;

    private IReadOnlyList<ReturnWatchEntry> _entries = Array.Empty<ReturnWatchEntry>();
    private List<ReturnWatchRowViewModel> _allRows = [];
    private List<ReturnWatchRowViewModel> _selectedRows = [];
    private DateTime _lastNow;

    public ReturnWatchViewModel(
        IReturnWatchService service,
        Func<DateTime>? clock = null,
        Func<string, string, bool>? confirmDialog = null,
        Func<Task>? onDataChangedAsync = null,
        Action<IReadOnlyList<(string OrderId, string Tracking)>, string?>? onOrdersRemoved = null,
        Action<string, bool>? onNotify = null,
        Action<Order>? onOpenDetail = null,
        Action<Order>? onGoToMainTable = null,
        AlertPolicy? alertPolicy = null,
        WriteGate? writeGate = null)
    {
        _service = service;
        _clock = clock ?? (() => DateTime.Now);
        _confirmDialog = confirmDialog ?? ((title, msg) => new WindowsConfirmationService().Confirm(title, msg));
        _onDataChangedAsync = onDataChangedAsync;
        _onOrdersRemoved = onOrdersRemoved;
        _onNotify = onNotify;
        _onOpenDetail = onOpenDetail;
        _onGoToMainTable = onGoToMainTable;
        _alertPolicy = alertPolicy ?? AlertThresholds.MainPolicy;
        _writeGate = writeGate ?? WriteGate.AlwaysOpen;
        if (!ReferenceEquals(_writeGate, WriteGate.AlwaysOpen))
        {
            _writeGate.Changed += OnWriteGateChanged;
        }
        _lastNow = _clock();
        _pageGate = new PageGate<ReturnWatchRowViewModel>(p => VisibleOrders = p);   // trang ẩn: giữ danh sách, mở ra mới đưa lên bảng (27/11/2026)
        Pager = new Pager<ReturnWatchRowViewModel>(_pageGate.Push);
    }

    public bool CanWrite => _writeGate.CanWrite;
    public string? WriteDisabledTooltip => _writeGate.TooltipReason;

    private void OnWriteGateChanged()
    {
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(WriteDisabledTooltip));
        RemoveFromBoardCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Chia trang 100 kiện/trang (thay cho giới hạn 1.500 dòng trước đây).</summary>
    public Pager<ReturnWatchRowViewModel> Pager { get; private set; } = null!;

    public event Action? CountsUpdated;

    [ObservableProperty]
    private bool _isDemoMode;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private IReadOnlyList<ReturnWatchRowViewModel> _visibleOrders = Array.Empty<ReturnWatchRowViewModel>();

    [ObservableProperty]
    private ReturnWatchRowViewModel? _selectedRow;

    [ObservableProperty]
    private int _selectedRowCount;

    [ObservableProperty]
    private string _stageFilter = "tracking"; // tracking (1-3) | stage1 | stage2 | stage3 | stage4 | all

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private bool _hasOverflow;

    [ObservableProperty]
    private string _overflowMessage = string.Empty;

    [ObservableProperty]
    private int _trackingCount;

    [ObservableProperty]
    private int _stage1Count;

    [ObservableProperty]
    private int _stage2Count;

    [ObservableProperty]
    private int _stage3Count;

    [ObservableProperty]
    private int _stage4Count;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _yellowAlertCount;

    [ObservableProperty]
    private int _redAlertCount;

    [ObservableProperty]
    private string _statusBarText = string.Empty;

    public bool IsFilterTracking => StageFilter == "tracking";
    public bool IsFilterStage1 => StageFilter == "stage1";
    public bool IsFilterStage2 => StageFilter == "stage2";
    public bool IsFilterStage3 => StageFilter == "stage3";
    public bool IsFilterStage4 => StageFilter == "stage4";
    public bool IsFilterAll => StageFilter == "all";
    public bool HasMultiSelection => SelectedRowCount > 1;

    partial void OnStageFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsFilterTracking));
        OnPropertyChanged(nameof(IsFilterStage1));
        OnPropertyChanged(nameof(IsFilterStage2));
        OnPropertyChanged(nameof(IsFilterStage3));
        OnPropertyChanged(nameof(IsFilterStage4));
        OnPropertyChanged(nameof(IsFilterAll));
        ApplyFiltersAndSort();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFiltersAndSort();
    }

    partial void OnSelectedRowCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasMultiSelection));
    }

    private DateTime _lastLoadedUtc = DateTime.MinValue;

    /// <summary>Mở tab: chỉ đọc lại khi dữ liệu đã cũ hơn <paramref name="maxAge"/> (dữ liệu vẫn được nạp lại sau mỗi thay đổi; 27/11/2026: đỡ lag khi đổi tab).</summary>
    public Task RefreshIfStaleAsync(TimeSpan maxAge) =>
        DateTime.UtcNow - _lastLoadedUtc < maxAge ? Task.CompletedTask : RefreshAsync();

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var now = _clock();
            var entries = await _service.LoadAsync(now, cancellationToken);
            LoadEntries(entries, now);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void LoadEntries(IReadOnlyList<ReturnWatchEntry> entries, DateTime? nowOverride = null)
    {
        _lastLoadedUtc = DateTime.UtcNow;
        _entries = entries ?? Array.Empty<ReturnWatchEntry>();
        _lastNow = nowOverride ?? _clock();
        RebuildRows();
    }

    public void UpdateSelectedRows(IEnumerable<ReturnWatchRowViewModel> rows)
    {
        _selectedRows = rows.ToList();
        SelectedRowCount = _selectedRows.Count;
    }

    [RelayCommand]
    public void SetStageFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return;
        StageFilter = filter;
    }

    [RelayCommand]
    public void ClearSearch()
    {
        SearchText = string.Empty;
    }

    [RelayCommand]
    public Task RefreshDataAsync() => RefreshAsync();

    [RelayCommand]
    public void OpenDetail(ReturnWatchRowViewModel? row)
    {
        var target = row ?? SelectedRow;
        if (target is null) return;
        _onOpenDetail?.Invoke(target.Source);
    }

    [RelayCommand]
    public void GoToMainTable(ReturnWatchRowViewModel? row)
    {
        var target = row ?? SelectedRow;
        if (target is null) return;
        if (_onGoToMainTable is not null)
        {
            _onGoToMainTable(target.Source);
        }
        else
        {
            _onOpenDetail?.Invoke(target.Source);
        }
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    public async Task RemoveFromBoardAsync(ReturnWatchRowViewModel? row)
    {
        if (!CanWrite) return;
        var targets = ResolveTargets(row);
        if (targets.Count == 0) return;

        if (targets.Count == 1)
        {
            var item = targets[0];
            var msg = $"Bạn có chắc muốn bỏ kiện này khỏi bảng Hàng hoàn về?\n\n" +
                      $"• Mã vận đơn: {item.DisplayTracking}\n" +
                      $"• Mã đơn: {item.OrderId}\n\n" +
                      "Chỉ bỏ khỏi bảng theo dõi, đơn vẫn còn ở Bảng chính.";
            if (!_confirmDialog("Bỏ khỏi bảng Hàng hoàn về", msg)) return;

            var now = _clock();
            var res = await _service.RemoveAsync(item.OrderIdKey, now);
            if (res.Kind == EditKind.Saved)
            {
                await NotifyDataChangedAsync();
                _onOrdersRemoved?.Invoke(
                    new[] { (item.OrderIdKey, item.DisplayTracking) },
                    $"Đã bỏ {item.DisplayTracking} khỏi bảng Hàng hoàn về");
            }
            else
            {
                _onNotify?.Invoke(res.Message, res.Kind == EditKind.Error);
            }
            return;
        }

        var bulkMsg = $"Bạn sắp bỏ {targets.Count:N0} kiện đang chọn khỏi bảng Hàng hoàn về.\n\n" +
                      "Chỉ bỏ khỏi bảng theo dõi, đơn vẫn còn ở Bảng chính.";
        if (!_confirmDialog("Bỏ nhiều đơn khỏi bảng", bulkMsg)) return;

        var okList = new List<(string OrderId, string Tracking)>();
        var errCount = 0;
        var bulkNow = _clock();

        foreach (var item in targets)
        {
            var res = await _service.RemoveAsync(item.OrderIdKey, bulkNow);
            if (res.Kind == EditKind.Saved)
            {
                okList.Add((item.OrderIdKey, item.DisplayTracking));
            }
            else
            {
                errCount++;
            }
        }

        if (okList.Count > 0)
        {
            await NotifyDataChangedAsync();
        }

        var summary = $"{okList.Count} thành công, {errCount} lỗi";
        if (okList.Count > 0 && _onOrdersRemoved is not null)
        {
            _onOrdersRemoved(okList, summary);
        }
        else
        {
            _onNotify?.Invoke(summary, errCount > 0);
        }
    }

    public Task HandleDeleteKeyAsync() => RemoveFromBoardAsync(null);

    private List<ReturnWatchRowViewModel> ResolveTargets(ReturnWatchRowViewModel? explicitRow)
    {
        if (explicitRow is not null)
        {
            if (_selectedRows.Count > 1 && _selectedRows.Any(r => r.OrderIdKey == explicitRow.OrderIdKey))
            {
                return _selectedRows.ToList();
            }
            return [explicitRow];
        }

        if (_selectedRows.Count > 0) return _selectedRows.ToList();
        if (SelectedRow is not null) return [SelectedRow];
        return [];
    }

    private async Task NotifyDataChangedAsync()
    {
        if (_onDataChangedAsync is not null)
        {
            await _onDataChangedAsync();
        }
        else
        {
            await RefreshAsync();
        }
    }

    private void RebuildRows()
    {
        var previousId = SelectedRow?.OrderIdKey;
        var ordered = _entries
            .OrderBy(e => e.Stage)
            .ThenBy(e => e.Since)
            .ToList();

        var rows = new List<ReturnWatchRowViewModel>(ordered.Count);
        int s1 = 0, s2 = 0, s3 = 0, s4 = 0, yellow = 0, red = 0;

        for (var i = 0; i < ordered.Count; i++)
        {
            var row = new ReturnWatchRowViewModel(ordered[i], i + 1, _lastNow, _alertPolicy);
            rows.Add(row);

            switch (row.Stage)
            {
                case 1: s1++; break;
                case 2: s2++; break;
                case 3:
                    s3++;
                    if (row.AlertLevel == AlertLevel.Yellow) yellow++;
                    else if (row.AlertLevel == AlertLevel.Red) red++;
                    break;
                case 4: s4++; break;
            }
        }

        _allRows = rows;
        Stage1Count = s1;
        Stage2Count = s2;
        Stage3Count = s3;
        Stage4Count = s4;
        TrackingCount = s1 + s2 + s3;
        TotalCount = rows.Count;
        YellowAlertCount = yellow;
        RedAlertCount = red;

        ApplyFiltersAndSort(previousId, resetPage: false);
        CountsUpdated?.Invoke();
    }

    private void ApplyFiltersAndSort(string? preferredOrderId = null, bool resetPage = true)
    {
        var prevId = preferredOrderId ?? SelectedRow?.OrderIdKey;
        var normQuery = AlertThresholds.NormalizeForSearch(SearchText);

        IEnumerable<ReturnWatchRowViewModel> query = _allRows;

        query = StageFilter switch
        {
            "tracking" => query.Where(r => r.Stage is >= 1 and <= 3),
            "stage1" => query.Where(r => r.Stage == 1),
            "stage2" => query.Where(r => r.Stage == 2),
            "stage3" => query.Where(r => r.Stage == 3),
            "stage4" => query.Where(r => r.Stage == 4),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(normQuery))
        {
            query = query.Where(r => r.SearchBlob.Contains(normQuery, StringComparison.Ordinal));
        }

        var filtered = query
            .OrderBy(r => r.Stage)
            .ThenBy(r => r.Since)
            .ToList();

        var totalMatched = filtered.Count;
        HasOverflow = false;            // không còn cắt bớt dòng: chia trang thay thế
        OverflowMessage = string.Empty;

        for (var i = 0; i < filtered.Count; i++)
        {
            filtered[i].Stt = i + 1;
        }

        Pager.SetItems(filtered, resetPage);
        IsEmpty = filtered.Count == 0;

        if (!string.IsNullOrEmpty(prevId))
        {
            SelectedRow = filtered.FirstOrDefault(r => r.OrderIdKey == prevId);
        }
        else if (SelectedRow is not null && !filtered.Contains(SelectedRow))
        {
            SelectedRow = null;
        }

        StatusBarText = $"Hiển thị {Pager.RangeText} kiện (tổng {TotalCount:N0}) • Đang theo dõi (1-3): {TrackingCount:N0}";
    }
}
