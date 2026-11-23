using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.ViewModels;

public sealed record ActivityRow(long Seq, string Time, string Type, string TypeLabel, string Icon, string Detail);
public sealed record ActivityTypeOption(string Key, string Label);

/// <summary>
/// Màn "Nhật ký hoạt động": xem ai/cái gì đã đổi dữ liệu (nạp đơn, sửa, quét, xóa, đơn từ điện thoại, Salework...). CHỈ XEM.
/// Mới nhất trước; tải 200 dòng một lần; lọc theo loại và theo chữ (chờ ~300 ms sau lần gõ cuối).
/// </summary>
public sealed partial class ActivityLogViewModel : ObservableObject
{
    public const int PageSize = 200;
    private readonly IActivityLogProvider _provider;
    private readonly IClipboardService? _clipboard;
    private readonly TimeSpan _debounce;
    private CancellationTokenSource? _debounceCts;
    private DateTime _lastLoad = DateTime.MinValue;
    private int _offset;

    public ActivityLogViewModel(IActivityLogProvider provider, IClipboardService? clipboard = null, TimeSpan? debounce = null)
    {
        _provider = provider;
        _clipboard = clipboard;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(300);
        TypeOptions = new ObservableCollection<ActivityTypeOption> { new("", "Tất cả") };
        SelectedType = TypeOptions[0];
    }

    public bool IsDemoMode { get; set; }
    public ObservableCollection<ActivityRow> Entries { get; } = new();
    public ObservableCollection<ActivityTypeOption> TypeOptions { get; }

    [ObservableProperty] private ActivityTypeOption? _selectedType;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _shownText = "";
    [ObservableProperty] private bool _hasMore;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _copyStatus = "";

    public bool IsEmpty => Entries.Count == 0 && !IsLoading;
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    private bool _syncingTypes;
    partial void OnSelectedTypeChanged(ActivityTypeOption? value)
    {
        if (_syncingTypes || value is null) return;   // ComboBox có thể tạm đặt null khi danh sách loại được thay
        _ = ReloadAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        _debounceCts?.Cancel();
        var cts = _debounceCts = new CancellationTokenSource();
        _ = DebouncedReloadAsync(cts.Token);
    }

    private async Task DebouncedReloadAsync(CancellationToken ct)
    {
        try
        {
            if (_debounce > TimeSpan.Zero) await Task.Delay(_debounce, ct);
            if (!ct.IsCancellationRequested) await ReloadAsync();
        }
        catch (OperationCanceledException) { /* gõ tiếp: bỏ lần này */ }
    }

    private static string IconOf(string type) => type switch
    {
        "import" => "📥", "edit" => "✏️", "scan" => "📷", "salework" => "🛒", "phone_new_order" => "📱",
        "delete" => "🗑️", "restore" => "↩️", "archive" => "📦", "return_watch" => "🚚", _ => "•",
    };

    /// <summary>Tải lại từ đầu (đầu danh sách), giữ bộ lọc hiện tại.</summary>
    public Task ReloadAsync()
    {
        // Đang tải: không bỏ qua âm thầm (dữ liệu có thể đã đổi sau khi lượt đó đọc). Đánh dấu "tải lại thêm một lượt" rồi trả lại lượt đang chạy.
        if (_reloadTask is { IsCompleted: false }) { _reloadAgain = true; return _reloadTask; }
        return _reloadTask = ReloadLoopAsync();
    }

    private Task? _reloadTask;
    private bool _reloadAgain;

    private async Task ReloadLoopAsync()
    {
        do
        {
            _reloadAgain = false;
            await ReloadOnceAsync();
        } while (_reloadAgain);
    }

    private async Task ReloadOnceAsync()
    {
        IsLoading = true;
        try
        {
            var types = await _provider.TypesAsync();
            SyncTypeOptions(types);
            _offset = 0;
            var page = await _provider.ReadAsync(PageSize, 0, (SelectedType?.Key ?? ""), SearchText?.Trim() ?? "");
            Entries.Clear();
            foreach (var e in page) Entries.Add(ToRow(e));
            _offset = page.Count;
            HasMore = page.Count == PageSize;
            await UpdateShownAsync();
            _lastLoad = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            ShownText = "Không đọc được nhật ký: " + ex.Message;
        }
        finally { IsLoading = false; }
    }

    /// <summary>Làm mới nhẹ khi có hoạt động mới: chỉ khi đang ở đầu danh sách và đã quá 2 giây từ lần tải trước (không nhảy vị trí người đang đọc).</summary>
    public async Task RefreshQuietlyAsync()
    {
        if (_offset > PageSize) return;
        if (DateTime.UtcNow - _lastLoad < TimeSpan.FromSeconds(2)) return;
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (IsLoading || !HasMore) return;
        IsLoading = true;
        try
        {
            var page = await _provider.ReadAsync(PageSize, _offset, (SelectedType?.Key ?? ""), SearchText?.Trim() ?? "");
            foreach (var e in page) Entries.Add(ToRow(e));
            _offset += page.Count;
            HasMore = page.Count == PageSize;
            await UpdateShownAsync();
        }
        catch (Exception ex) { ShownText = "Không đọc được nhật ký: " + ex.Message; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private Task RefreshAsync() => ReloadAsync();

    [RelayCommand]
    private void CopyDetail(ActivityRow? row)
    {
        if (row is null) return;
        _clipboard?.SetText($"{row.Time}  {row.TypeLabel}: {row.Detail}");
        CopyStatus = "Đã sao chép dòng nhật ký.";
    }

    private async Task UpdateShownAsync()
    {
        var total = await _provider.CountAsync();
        ShownText = $"Hiển thị {Entries.Count:N0} / tổng {total:N0}" + (HasMore ? " (còn nữa: bấm \"Tải thêm\")" : "");
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void SyncTypeOptions(IReadOnlyList<string> types)
    {
        var selectedKey = SelectedType?.Key ?? "";
        var wanted = new List<ActivityTypeOption> { new("", "Tất cả") };
        wanted.AddRange(types.Where(t => t.Length > 0).Select(t => new ActivityTypeOption(t, AuditLogReader.LabelOf(t))));
        if (wanted.Select(w => w.Key).SequenceEqual(TypeOptions.Select(o => o.Key))) return;
        _syncingTypes = true;
        try
        {
            TypeOptions.Clear();
            foreach (var w in wanted) TypeOptions.Add(w);
            SelectedType = TypeOptions.FirstOrDefault(o => o.Key == selectedKey) ?? TypeOptions[0];
        }
        finally { _syncingTypes = false; }
    }

    private static ActivityRow ToRow(AuditEntry e) => new(e.Seq, e.Time, e.Type, e.Label, IconOf(e.Type), e.Detail);
}
