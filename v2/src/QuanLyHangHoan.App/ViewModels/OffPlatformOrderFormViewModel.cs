using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Core.Stats;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.ViewModels;

/// <summary>Một dòng sản phẩm trong form tạo đơn ngoài sàn; màu / size gợi ý theo dữ liệu sản phẩm sẵn có.</summary>
public sealed partial class OffPlatformItemRow : ObservableObject
{
    private readonly ProductCatalog _catalog;
    public OffPlatformItemRow(ProductCatalog catalog) => _catalog = catalog;

    [ObservableProperty] private string _product = "";
    [ObservableProperty] private string _color = "";
    [ObservableProperty] private string _size = "";
    [ObservableProperty] private string _qtyText = "1";
    [ObservableProperty] private IReadOnlyList<string> _colorOptions = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<string> _sizeOptions = Array.Empty<string>();

    /// <summary>Sản phẩm đã có trong dữ liệu (có gợi ý màu/size).</summary>
    public bool IsKnown => _catalog.Find(Product) is not null;

    partial void OnProductChanged(string value)
    {
        var p = _catalog.Find(value);
        ColorOptions = p?.Colors ?? Array.Empty<string>();
        SizeOptions = p?.Sizes ?? Array.Empty<string>();
        OnPropertyChanged(nameof(IsKnown));
    }

    public bool IsBlank => string.IsNullOrWhiteSpace(Product) && string.IsNullOrWhiteSpace(Color) && string.IsNullOrWhiteSpace(Size);

    public int Qty => int.TryParse((QtyText ?? "").Trim(), out var q) ? q : 0;

    [RelayCommand] private void Increase() => QtyText = Math.Min(9999, Math.Max(0, Qty) + 1).ToString(CultureInfo.InvariantCulture);
    [RelayCommand] private void Decrease() => QtyText = Math.Max(1, Qty - 1).ToString(CultureInfo.InvariantCulture);
}

/// <summary>Kết quả hộp thoại: đầu vào đơn + có mở tiếp form để nhập đơn sau không.</summary>
public sealed record OffPlatformDialogResult(NewOrderInput Input, bool CreateAnother);

/// <summary>
/// Form tạo nhanh đơn NGOÀI SÀN (chủ dự án 27/11/2026): tìm sản phẩm từ dữ liệu sẵn có (gõ không dấu cũng được), bấm là thêm dòng,
/// màu / size gợi ý theo sản phẩm, nhiều sản phẩm một đơn, kênh bán, loại đơn, giá trị; để trống mã thì tự sinh mã "NS...".
/// Không có ô thông tin người mua (quy tắc: không đưa dữ liệu cá nhân người mua lên cloud).
/// </summary>
public sealed partial class OffPlatformOrderFormViewModel : ObservableObject
{
    private readonly ProductCatalog _catalog;

    public OffPlatformOrderFormViewModel(ProductCatalog? catalog = null)
    {
        _catalog = catalog ?? ProductCatalog.Empty;
        QuickProducts = _catalog.Products.Take(10).ToList();
        Items.Add(NewRow());
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ItemsSummary));
    }

    public IReadOnlyList<string> Channels => OffPlatform.Channels;
    public IReadOnlyList<string> OrderTypes => OffPlatform.OrderTypes;
    public IReadOnlyList<CatalogProduct> QuickProducts { get; }
    public bool HasQuickProducts => QuickProducts.Count > 0;
    public int CatalogCount => _catalog.Products.Count;
    public string CatalogHint => CatalogCount == 0
        ? "Chưa có dữ liệu sản phẩm: gõ tên sản phẩm rồi Enter để thêm."
        : $"Tìm trong {CatalogCount:N0} sản phẩm đã có (gõ không dấu cũng được) • Enter để thêm sản phẩm đầu tiên • ↓ để chọn";

    public ObservableCollection<OffPlatformItemRow> Items { get; } = new();

    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _orderId = "";
    [ObservableProperty] private string _channel = OffPlatform.Channels[0];
    [ObservableProperty] private string _orderType = OffPlatform.OrderTypes[0];
    [ObservableProperty] private string _priceText = "";
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private IReadOnlyList<CatalogProduct> _suggestions = Array.Empty<CatalogProduct>();
    [ObservableProperty] private string _error = "";

    public bool HasError => Error.Length > 0;
    public bool HasSuggestions => Suggestions.Count > 0;
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnSuggestionsChanged(IReadOnlyList<CatalogProduct> value) => OnPropertyChanged(nameof(HasSuggestions));

    partial void OnSearchTextChanged(string value) =>
        Suggestions = string.IsNullOrWhiteSpace(value) ? Array.Empty<CatalogProduct>() : _catalog.Search(value, 12);

    public string ItemsSummary
    {
        get
        {
            var filled = Items.Where(r => !r.IsBlank).ToList();
            return filled.Count == 0 ? "Chưa có sản phẩm" : $"{filled.Count:N0} sản phẩm • {filled.Sum(r => Math.Max(1, r.Qty)):N0} cái";
        }
    }

    public string PricePreview => ParsePrice(PriceText) is { } p && p > 0 ? p.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) + " đ" : "";
    partial void OnPriceTextChanged(string value) => OnPropertyChanged(nameof(PricePreview));

    private OffPlatformItemRow NewRow()
    {
        var r = new OffPlatformItemRow(_catalog);
        r.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ItemsSummary));
        return r;
    }

    /// <summary>Thêm một sản phẩm: điền vào dòng trống cuối nếu có, không thì thêm dòng. Chỉ có một màu / một size thì điền sẵn.</summary>
    [RelayCommand]
    public void AddProduct(CatalogProduct? p)
    {
        if (p is null) return;
        AddNamed(p.Name);
        var row = Items[^1];
        if (p.Colors.Count == 1) row.Color = p.Colors[0];
        if (p.Sizes.Count == 1) row.Size = p.Sizes[0];
        SearchText = "";
        Error = "";
    }

    private void AddNamed(string name)
    {
        var target = Items.Count > 0 && Items[^1].IsBlank ? Items[^1] : null;
        if (target is null) { target = NewRow(); Items.Add(target); }
        target.Product = name.Trim();
    }

    /// <summary>Enter ở ô tìm: thêm sản phẩm gợi ý đầu tiên; không có gợi ý thì thêm đúng chữ đã gõ (sản phẩm mới).</summary>
    [RelayCommand]
    public void AddFromSearch()
    {
        var text = (SearchText ?? "").Trim();
        if (text.Length == 0) return;
        if (Suggestions.Count > 0) { AddProduct(Suggestions[0]); return; }
        AddNamed(text);
        SearchText = "";
        Error = "";
    }

    [RelayCommand] public void AddEmptyRow() => Items.Add(NewRow());

    [RelayCommand]
    public void RemoveRow(OffPlatformItemRow? row)
    {
        if (row is null) return;
        Items.Remove(row);
        if (Items.Count == 0) Items.Add(NewRow());
    }

    [RelayCommand] private void SetChannel(string? channel) { if (!string.IsNullOrWhiteSpace(channel)) Channel = channel; }

    /// <summary>"350000", "350.000", "350k", "1,5tr" -> số đồng; null nếu không đọc được.</summary>
    public static decimal? ParsePrice(string? text)
    {
        var t = (text ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace("đ", "").Replace("vnd", "");
        if (t.Length == 0) return 0;
        decimal mult = 1;
        if (t.EndsWith("triệu")) { mult = 1_000_000; t = t[..^5]; }
        else if (t.EndsWith("tr")) { mult = 1_000_000; t = t[..^2]; }
        else if (t.EndsWith("k") || t.EndsWith("n")) { mult = 1_000; t = t[..^1]; }
        if (t.Length == 0) return null;
        if (mult > 1)
        {
            t = t.Replace(',', '.');
            return decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) && d >= 0 ? Math.Round(d * mult) : null;
        }
        t = t.Replace(".", "").Replace(",", "");
        return decimal.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>Kiểm tra và dựng đầu vào; lỗi ghi vào <see cref="Error"/>.</summary>
    public NewOrderInput? Build()
    {
        Error = "";
        var lines = Items.Where(r => !r.IsBlank).ToList();
        if (lines.Count == 0) { Error = "Hãy thêm ít nhất một sản phẩm (gõ tên ở ô tìm rồi Enter, hoặc bấm sản phẩm gợi ý)."; return null; }
        if (lines.Any(r => string.IsNullOrWhiteSpace(r.Product))) { Error = "Có dòng chưa ghi tên sản phẩm."; return null; }
        if (lines.Any(r => r.Qty < 1 || r.Qty > 9999)) { Error = "Số lượng phải là số nguyên từ 1 đến 9999."; return null; }
        var price = ParsePrice(PriceText);
        if (price is null) { Error = "Giá trị đơn không đọc được (ví dụ: 350000, 350.000, 350k, 1,2tr)."; return null; }
        var items = lines.Select(r => new NewOrderItem(r.Product.Trim(), (r.Color ?? "").Trim(), (r.Size ?? "").Trim(), r.Qty)).ToList();
        var first = items[0];
        return new NewOrderInput((Code ?? "").Trim(), (OrderId ?? "").Trim(), OffPlatform.Name, "", first.Product, first.Color, first.Size, first.Qty, (Note ?? "").Trim(), "manual")
        {
            Items = items,
            Price = price.Value,
            OrderType = OffPlatform.NormalizeOrderType(OrderType),
            Channel = string.IsNullOrWhiteSpace(Channel) ? "Khác" : Channel.Trim(),
        };
    }

    // ---- Gửi tạo đơn ngay trong form (lỗi như trùng mã hiện tại chỗ, không mất dữ liệu đã nhập)
    /// <summary>Hàm tạo đơn (cloud / máy) do bảng Đơn ngoài sàn gắn vào.</summary>
    public Func<NewOrderInput, Task<NewOrderResult>>? Create { get; set; }
    public List<NewOrderResult> Created { get; } = new();
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _lastCreatedText = "";
    public bool HasLastCreated => LastCreatedText.Length > 0;
    partial void OnLastCreatedTextChanged(string value) => OnPropertyChanged(nameof(HasLastCreated));

    /// <summary>
    /// Tạo đơn. Trả true khi đã tạo (cửa sổ đóng nếu không nhập tiếp). <paramref name="another"/> = "Tạo & nhập đơn tiếp":
    /// tạo xong làm trống form (giữ kênh + loại đơn) để nhập đơn sau.
    /// </summary>
    public async Task<bool> SubmitAsync(bool another)
    {
        if (IsBusy) return false;
        var input = Build();
        if (input is null) return false;
        if (Create is null) { Error = "Chưa sẵn sàng tạo đơn."; return false; }
        IsBusy = true;
        try
        {
            NewOrderResult r;
            try { r = await Create(input); }
            catch (Exception ex) { Error = "Không tạo được đơn: " + ex.Message; return false; }
            if (!r.Ok) { Error = r.Outcome == "duplicate" ? "⚠️ " + r.Message : "Không tạo được đơn: " + r.Message; return false; }
            Created.Add(r);
            LastCreatedText = $"✅ Đã tạo đơn {r.Code} ({Created.Count:N0} đơn trong lượt này)";
            if (another) ResetForNext();
            return true;
        }
        finally { IsBusy = false; }
    }

    /// <summary>"Tạo & nhập đơn tiếp": xóa phần riêng của đơn, giữ kênh bán và loại đơn.</summary>
    public void ResetForNext()
    {
        Code = ""; OrderId = ""; PriceText = ""; Note = ""; SearchText = ""; Error = "";
        Items.Clear();
        Items.Add(NewRow());
    }
}
