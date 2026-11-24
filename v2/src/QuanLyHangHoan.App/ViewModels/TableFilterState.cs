using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;

namespace QuanLyHangHoan.App.ViewModels;

/// <summary>Bộ lọc nâng cao dùng chung cho bảng Kho lưu trữ và Đã xóa: sản phẩm + khoảng ngày.</summary>
public sealed partial class TableFilterState : ObservableObject
{
    private readonly string _dateLabel;

    public TableFilterState(string dateLabel) { _dateLabel = dateLabel; }

    public string DateLabel => _dateLabel;
    public event Action? Changed;

    [ObservableProperty] private bool _showPanel;
    [ObservableProperty] private string _productFilter = string.Empty;
    [ObservableProperty] private DateTime? _dateFrom;
    [ObservableProperty] private DateTime? _dateTo;

    public int Count => (ProductFilter.Trim().Length > 0 ? 1 : 0) + (DateFrom.HasValue || DateTo.HasValue ? 1 : 0);
    public string ButtonText => Count == 0 ? "🔎 Bộ lọc" : $"🔎 Bộ lọc ({Count})";

    private string _kw = string.Empty;

    private void Raise()
    {
        _kw = AlertThresholds.NormalizeForSearch(ProductFilter).Trim();
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(ButtonText));
        Changed?.Invoke();
    }

    partial void OnProductFilterChanged(string value) => Raise();
    partial void OnDateFromChanged(DateTime? value) => Raise();
    partial void OnDateToChanged(DateTime? value) => Raise();

    [RelayCommand]
    private void TogglePanel() => ShowPanel = !ShowPanel;

    /// <summary>Xóa bộ lọc mà không bắn Changed từng cái (người gọi tự áp dụng một lần).</summary>
    public void ResetSilently()
    {
        var h = Changed;
        Changed = null;
        ProductFilter = string.Empty;
        DateFrom = null;
        DateTo = null;
        Changed = h;
        _kw = string.Empty;
    }

    public bool Matches(string itemsBlob, DateTime? date)
    {
        if (_kw.Length > 0 && !itemsBlob.Contains(_kw, StringComparison.Ordinal)) return false;
        if (DateFrom.HasValue || DateTo.HasValue)
        {
            if (date is null) return false;
            if (DateFrom.HasValue && date.Value.Date < DateFrom.Value.Date) return false;
            if (DateTo.HasValue && date.Value.Date > DateTo.Value.Date) return false;
        }
        return true;
    }
}
