using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.ViewModels;

/// <summary>Một dòng ở bảng Khiếu nại.</summary>
public sealed class ComplaintRowViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public ComplaintRowViewModel(Order source, int stt, bool isArchived)
    {
        Source = source;
        Stt = stt;
        IsArchived = isArchived;
        var ret = source.ReturnTrack.Trim();
        var fwd = (source.Track.Length > 0 ? source.Track : source.ForwardTrack).Trim();
        DisplayTracking = ret.Length > 0 && fwd.Length > 0 && !ret.Equals(fwd, StringComparison.OrdinalIgnoreCase) ? $"{ret} / {fwd}" : (ret.Length > 0 ? ret : fwd);
        ItemsSummary = string.Join("; ", source.Items.Where(i => i.Product.Length > 0)
            .Select(i => i.Product + (i.Color.Length + i.Size.Length > 0 ? $" ({string.Join(", ", new[] { i.Color, i.Size }.Where(x => x.Length > 0))})" : "") + $" × {i.Qty}"));
        SearchBlob = AlertThresholds.NormalizeForSearch(string.Join(" ", source.OrderId, source.ReturnOrderId, source.Track, source.ReturnTrack,
            source.ForwardTrack, source.AltTrack, ItemsSummary, source.ComplaintNote, source.UserNote, source.Note));
    }

    public Order Source { get; }
    public int Stt { get; }
    public bool IsArchived { get; }
    public string Location => IsArchived ? "Kho lưu trữ" : "Bảng chính";
    public string Platform => Source.Platform;
    public string OrderId => Source.OrderId;
    public string DisplayTracking { get; }
    public string Status => Source.Status;
    public string PlatformStatus => Source.PlatformStatus;
    public string PriceFormatted => Source.Price > 0 ? Source.Price.ToString("N0", Vi) + " đ" : "";
    public string ItemsSummary { get; }
    public string ComplaintStatus => Source.ComplaintStatus;
    public string ComplaintAt => Source.ComplaintAt;
    public string ComplaintNote => Source.ComplaintNote;
    public string ComplaintDoneAt => Source.ComplaintDoneAt;
    /// <summary>Khiếu nại Thành công: ngày sẽ tự vào Kho lưu trữ (30 ngày sau khi xong).</summary>
    public string AutoArchiveText
    {
        get
        {
            if (IsArchived || Source.ComplaintStatus != ComplaintStatuses.Success) return "";
            var d = QuanLyHangHoan.Core.Alerts.ArchivePolicy.ParseRecvDate(Source.ComplaintDoneAt);
            return d is null ? "" : "Tự lưu trữ " + (d.Value + QuanLyHangHoan.Core.Alerts.ArchivePolicy.ComplaintSuccessAfter).ToString("dd/MM/yyyy");
        }
    }
    public string SearchBlob { get; }
}

/// <summary>
/// Bảng Khiếu nại (chủ dự án 26/11/2026): đơn đã bấm "Khiếu nại" ở bảng đơn. Mỗi đơn chọn trạng thái Đang xử lý / Thành công / Thất bại,
/// ghi chú khiếu nại, bỏ khiếu nại. Lọc theo trạng thái (mặc định: đang khiếu nại) + tìm kiếm. Dữ liệu lưu trên cloud (mọi máy thấy như nhau).
/// </summary>
public sealed partial class ComplaintsViewModel : ObservableObject
{
    private readonly IOrderEditService? _editService;
    private readonly IOrderActionDialogService? _dialogService;
    private readonly Func<Task>? _onDataChangedAsync;
    private readonly Action<string, bool>? _onNotify;
    private readonly Action<Order>? _onOpenDetail;
    private readonly WriteGate _writeGate;
    private List<ComplaintRowViewModel> _allRows = [];

    public ComplaintsViewModel(
        IOrderEditService? editService = null,
        IOrderActionDialogService? dialogService = null,
        Func<Task>? onDataChangedAsync = null,
        Action<string, bool>? onNotify = null,
        Action<Order>? onOpenDetail = null,
        WriteGate? writeGate = null)
    {
        _editService = editService;
        _dialogService = dialogService;
        _onDataChangedAsync = onDataChangedAsync;
        _onNotify = onNotify;
        _onOpenDetail = onOpenDetail;
        _writeGate = writeGate ?? WriteGate.AlwaysOpen;
        if (!ReferenceEquals(_writeGate, WriteGate.AlwaysOpen)) _writeGate.Changed += () => OnPropertyChanged(nameof(CanWrite));
    }

    public bool CanWrite => _writeGate.CanWrite;
    public string? WriteDisabledTooltip => _writeGate.TooltipReason;

    /// <summary>Trạng thái khiếu nại để chọn ở từng dòng.</summary>
    public IReadOnlyList<string> StatusChoices { get; } = ComplaintStatuses.All;

    public event Action? CountsUpdated;

    [ObservableProperty] private IReadOnlyList<ComplaintRowViewModel> _visibleRows = Array.Empty<ComplaintRowViewModel>();
    [ObservableProperty] private ComplaintRowViewModel? _selectedRow;
    /// <summary>"processing" (mặc định: đang khiếu nại) | "success" | "failed" | "all".</summary>
    [ObservableProperty] private string _statusFilter = "processing";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private int _processingCount;
    [ObservableProperty] private int _successCount;
    [ObservableProperty] private int _failedCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string _statusBarText = "";

    partial void OnStatusFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsFilterProcessing)); OnPropertyChanged(nameof(IsFilterSuccess));
        OnPropertyChanged(nameof(IsFilterFailed)); OnPropertyChanged(nameof(IsFilterAll));
        ApplyFilters();
    }

    public bool IsFilterProcessing => StatusFilter is not ("success" or "failed" or "all");
    public bool IsFilterSuccess => StatusFilter == "success";
    public bool IsFilterFailed => StatusFilter == "failed";
    public bool IsFilterAll => StatusFilter == "all";
    partial void OnSearchTextChanged(string value) => ApplyFilters();

    [RelayCommand]
    private void SetStatusFilter(string? filter) => StatusFilter = string.IsNullOrWhiteSpace(filter) ? "processing" : filter;

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    public void LoadOrders(IReadOnlyList<Order> activeOrders, IReadOnlyList<Order> archivedOrders, DateTime now)
    {
        var rows = new List<ComplaintRowViewModel>();
        var stt = 0;
        foreach (var o in activeOrders) if (o.ComplaintStatus.Length > 0) rows.Add(new ComplaintRowViewModel(o, ++stt, false));
        foreach (var o in archivedOrders) if (o.ComplaintStatus.Length > 0) rows.Add(new ComplaintRowViewModel(o, ++stt, true));
        // mới khiếu nại trước
        rows.Sort((a, b) => Compare(b.ComplaintAt, a.ComplaintAt));
        _allRows = rows;
        ProcessingCount = rows.Count(r => r.ComplaintStatus == ComplaintStatuses.Processing);
        SuccessCount = rows.Count(r => r.ComplaintStatus == ComplaintStatuses.Success);
        FailedCount = rows.Count(r => r.ComplaintStatus == ComplaintStatuses.Failed);
        TotalCount = rows.Count;
        ApplyFilters();
        CountsUpdated?.Invoke();
    }

    private static int Compare(string a, string b)
    {
        var da = AlertThresholds.TryParseVietnamDateTime(a);
        var db = AlertThresholds.TryParseVietnamDateTime(b);
        return Nullable.Compare(da, db);
    }

    private void ApplyFilters()
    {
        var kw = AlertThresholds.NormalizeForSearch(SearchText).Trim();
        var want = StatusFilter switch
        {
            "success" => ComplaintStatuses.Success,
            "failed" => ComplaintStatuses.Failed,
            "all" => null,
            _ => ComplaintStatuses.Processing,
        };
        var previous = SelectedRow?.Source.Id;
        VisibleRows = _allRows.Where(r => (want is null || r.ComplaintStatus == want) && (kw.Length == 0 || r.SearchBlob.Contains(kw, StringComparison.Ordinal))).ToList();
        SelectedRow = previous is null ? null : VisibleRows.FirstOrDefault(r => r.Source.Id == previous);
        IsEmpty = VisibleRows.Count == 0;
        StatusBarText = $"Đang hiển thị {VisibleRows.Count:N0} / {TotalCount:N0} đơn khiếu nại  •  Đang xử lý {ProcessingCount:N0} • Thành công {SuccessCount:N0} • Thất bại {FailedCount:N0}";
    }

    /// <summary>Đổi trạng thái khiếu nại của một đơn (chọn ở ô trạng thái).</summary>
    public async Task SetStatusAsync(ComplaintRowViewModel row, string status)
    {
        if (!CanWrite || _editService is null || status == row.ComplaintStatus || !ComplaintStatuses.All.Contains(status)) return;
        var r = await _editService.SetFieldAsync(row.Source.Id, EditField.ComplaintStatus, status, expectedOld: row.ComplaintStatus);
        await OrderEditHelper.HandleResultAsync(r, _onDataChangedAsync, _onNotify);
    }

    [RelayCommand]
    private Task SetStatusFromMenuAsync(object? arg) =>
        arg is object[] { Length: 2 } a && a[0] is ComplaintRowViewModel row && a[1] is string s ? SetStatusAsync(row, s) : Task.CompletedTask;

    [RelayCommand]
    public async Task EditNoteAsync(ComplaintRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (!CanWrite || target is null || _editService is null || _dialogService is null) return;
        var text = _dialogService.PromptMultilineText("Ghi chú khiếu nại", $"Mã đơn: {target.OrderId}  •  MVĐ: {target.DisplayTracking}",
            "Ghi chú (đã gửi gì, sàn trả lời gì, được đền bao nhiêu...):", target.ComplaintNote);
        if (text is null) return;
        var r = await _editService.SetFieldAsync(target.Source.Id, EditField.ComplaintNote, text, expectedOld: target.ComplaintNote);
        await OrderEditHelper.HandleResultAsync(r, _onDataChangedAsync, _onNotify);
    }

    [RelayCommand]
    public async Task RemoveComplaintAsync(ComplaintRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (!CanWrite || target is null || _editService is null || _dialogService is null) return;
        if (!_dialogService.ConfirmBulkAction("Bỏ khiếu nại", 1, $"Bỏ đơn {target.OrderId} khỏi bảng Khiếu nại (xóa trạng thái, ngày và ghi chú khiếu nại).")) return;
        var r = await _editService.SetFieldAsync(target.Source.Id, EditField.ComplaintStatus, "", expectedOld: target.ComplaintStatus);
        await OrderEditHelper.HandleResultAsync(r, _onDataChangedAsync, _onNotify);
    }

    [RelayCommand]
    public void OpenDetail(ComplaintRowViewModel? row = null)
    {
        var target = row ?? SelectedRow;
        if (target is not null) _onOpenDetail?.Invoke(target.Source);
    }
}
