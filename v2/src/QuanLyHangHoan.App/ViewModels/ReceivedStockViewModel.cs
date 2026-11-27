using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Core.Stats;

namespace QuanLyHangHoan.App.ViewModels;

/// <summary>Một dòng của bảng Hàng hoàn sẵn kho.</summary>
public sealed class ReceivedStockRowViewModel
{
    public ReceivedStockRowViewModel(ReceivedStockRow row)
    {
        Source = row;
        Platform = string.Join(", ", row.Parcels.Select(p => p.Order.Platform).Where(p => p.Length > 0).Distinct());
        Codes = string.Join("  ·  ", row.Parcels.Take(4).Select(p => Code(p.Order) + (p.Units > 1 ? $" ×{p.Units}" : "")))
                + (row.Parcels.Count > 4 ? $"  · +{row.Parcels.Count - 4} kiện" : "");
        AllCodes = string.Join(Environment.NewLine, row.Parcels.Select(p => $"{p.ReceivedAt:dd/MM HH:mm}  {Code(p.Order)}  ({p.Order.Platform}) ×{p.Units}"));
    }

    public ReceivedStockRow Source { get; }
    public string Product => Source.Product;
    public string Color => Source.Color.Length == 0 ? "—" : Source.Color;
    public string Size => Source.Size.Length == 0 ? "—" : Source.Size;
    public int Units => Source.Units;
    public int ParcelCount => Source.Parcels.Count;
    public string LastReceived => Source.LastReceived.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
    public string Platform { get; }
    /// <summary>Mã vận đơn (hoặc mã đơn) của vài kiện mới nhận nhất: để tìm đúng kiện ngoài kho.</summary>
    public string Codes { get; }
    public string AllCodes { get; }

    private static string Code(Order o) =>
        !string.IsNullOrWhiteSpace(o.ReturnTrack) ? o.ReturnTrack : !string.IsNullOrWhiteSpace(o.Track) ? o.Track : o.OrderId;
}

/// <summary>
/// HÀNG HOÀN SẴN KHO (chủ dự án 27/11/2026): sản phẩm của các kiện hoàn ĐÃ NHẬN tại kho gần đây, gom theo sản phẩm + màu + size.
/// Có đơn mới trùng sản phẩm thì tra ở đây, lấy luôn hàng hoàn đóng đơn, khỏi cất lên kệ. Chỉ đọc dữ liệu.
/// </summary>
public sealed partial class ReceivedStockViewModel : ObservableObject
{
    private readonly Action<Order>? _onOpenDetail;
    private IReadOnlyList<Order> _orders = Array.Empty<Order>();
    private DateTime _now = DateTime.Now;

    public ReceivedStockViewModel(Action<Order>? onOpenDetail = null) => _onOpenDetail = onOpenDetail;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _rangeKey = "3d";   // today | 3d | 7d | 30d
    [ObservableProperty] private IReadOnlyList<ReceivedStockRowViewModel> _rows = Array.Empty<ReceivedStockRowViewModel>();
    [ObservableProperty] private ReceivedStockRowViewModel? _selectedRow;
    [ObservableProperty] private string _summaryText = "";

    public bool IsToday => RangeKey == "today";
    public bool Is3Days => RangeKey == "3d";
    public bool Is7Days => RangeKey == "7d";
    public bool Is30Days => RangeKey == "30d";
    public bool IsEmpty => Rows.Count == 0;

    partial void OnSearchTextChanged(string value) => Recompute();
    partial void OnRowsChanged(IReadOnlyList<ReceivedStockRowViewModel> value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnRangeKeyChanged(string value)
    {
        OnPropertyChanged(nameof(IsToday)); OnPropertyChanged(nameof(Is3Days)); OnPropertyChanged(nameof(Is7Days)); OnPropertyChanged(nameof(Is30Days));
        Recompute();
    }

    [RelayCommand] private void SetRange(string? key) => RangeKey = key is "today" or "3d" or "7d" or "30d" ? key : "3d";
    [RelayCommand] private void ClearSearch() => SearchText = "";

    [RelayCommand]
    private void OpenDetail(ReceivedStockRowViewModel? row)
    {
        var r = row ?? SelectedRow;
        if (r is not null && r.Source.Parcels.Count > 0) _onOpenDetail?.Invoke(r.Source.Parcels[0].Order);
    }

    /// <summary>Đơn bảng chính + kho lưu trữ (đơn đã nhận có thể tự vào lưu trữ).</summary>
    public void LoadOrders(IReadOnlyList<Order> active, IReadOnlyList<Order> archived, DateTime now)
    {
        _orders = active.Concat(archived).ToList();
        _now = now;
        Recompute();
    }

    public DateTime Since => RangeKey switch
    {
        "today" => _now.Date,
        "7d" => _now.Date.AddDays(-6),
        "30d" => _now.Date.AddDays(-29),
        _ => _now.Date.AddDays(-2),
    };

    private void Recompute()
    {
        var keep = SelectedRow?.Source.Key + "|" + SelectedRow?.Source.Color + "|" + SelectedRow?.Source.Size;
        var rows = ReceivedStock.Build(_orders, Since, SearchText).Select(r => new ReceivedStockRowViewModel(r)).ToList();
        Rows = rows;
        SelectedRow = rows.FirstOrDefault(r => r.Source.Key + "|" + r.Source.Color + "|" + r.Source.Size == keep);
        var range = RangeKey switch { "today" => "hôm nay", "7d" => "7 ngày qua", "30d" => "30 ngày qua", _ => "3 ngày qua" };
        SummaryText = rows.Count == 0
            ? (string.IsNullOrWhiteSpace(SearchText) ? $"Chưa có hàng hoàn nào nhận tại kho {range}." : $"Không có hàng hoàn khớp «{SearchText.Trim()}» nhận {range}.")
            : $"{rows.Count:N0} mẫu (sản phẩm · màu · size) • {rows.Sum(r => r.Units):N0} cái • nhận tại kho {range} (từ {Since:dd/MM})";
    }
}
