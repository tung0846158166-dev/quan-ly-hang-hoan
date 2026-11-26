using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.Views;
using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.ViewModels;

public sealed record OrderCodeRow(string Label, string DisplayValue, string RawValue, bool CanCopy);

public sealed record OrderHistoryCodeChip(string Label, string Code);

public sealed record OrderDetailItemRow(
    int Stt,
    string Product,
    string Color,
    string Size,
    int Qty,
    string Sku,
    string SkuId,
    string ImportPricesText,
    int ReturnedQty = 0,
    string ReturnTag = "—",
    ItemLineKind Kind = ItemLineKind.Neutral);

public sealed record RawDetailRow(string Column, string Value);

public sealed record PriceModeRow(string Mode, string PriceFormatted);

/// <summary>
/// ViewModel CHỈ XEM cho màn hình Chi tiết đơn. Không sửa đổi dữ liệu hay DB.
/// </summary>
public sealed partial class OrderDetailViewModel : ObservableObject
{
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

    private readonly Order _order;
    private readonly IPhotoProvider? _photoProvider;
    private readonly IPhotoViewerService? _photoViewerService;
    private readonly IOrderEditService? _orderEditService;
    private readonly IClipboardService? _clipboardService;
    private readonly IReadOnlyList<RawDetailRow> _allRawDetails;

    public OrderDetailViewModel(
        Order order,
        DateTime now,
        IPhotoProvider? photoProvider = null,
        IPhotoViewerService? photoViewerService = null,
        IOrderEditService? orderEditService = null,
        IClipboardService? clipboardService = null)
    {
        _order = order ?? throw new ArgumentNullException(nameof(order));
        _photoProvider = photoProvider;
        _photoViewerService = photoViewerService;
        _orderEditService = orderEditService;
        _clipboardService = clipboardService;

        PrimaryTracking = !string.IsNullOrWhiteSpace(order.ReturnTrack)
            ? order.ReturnTrack
            : !string.IsNullOrWhiteSpace(order.Track)
                ? order.Track
                : order.ForwardTrack;

        WindowTitle = string.IsNullOrWhiteSpace(order.OrderId) && string.IsNullOrWhiteSpace(PrimaryTracking)
            ? "Chi tiết đơn hàng"
            : $"Chi tiết đơn — {Dash(order.OrderId)} ({Dash(PrimaryTracking)})";

        HeaderOrderId = Dash(order.OrderId);
        HeaderTracking = Dash(PrimaryTracking);
        HeaderPlatform = Dash(order.Platform);
        HeaderStatus = Dash(order.Status);

        // 1. Tổng quan
        Platform = Dash(order.Platform);
        OrderType = Dash(order.OrderType);
        PlatformStatus = Dash(order.PlatformStatus);
        Status = Dash(order.Status);
        PreOrder = Dash(order.PreOrder);
        PriceFormatted = order.Price > 0 ? string.Format(ViCulture, "{0:N0} đ", order.Price) : "—";

        var arrivedAt = AlertThresholds.TryParseVietnamDateTime(order.ReturnedAt);
        AlertLevel = AlertThresholds.EvaluateOrder(order, now, AlertThresholds.MainPolicy);
        var elapsedText = arrivedAt is not null ? FormatElapsed(now - arrivedAt.Value) : string.Empty;
        AlertText = AlertLevel switch
        {
            AlertLevel.Red => string.IsNullOrEmpty(elapsedText)
                ? $"🚨 Báo Đỏ (>{AlertThresholds.RedHours:0}h)"
                : $"🚨 Báo Đỏ (>{AlertThresholds.RedHours:0}h) — đã về {elapsedText}",
            AlertLevel.Yellow => string.IsNullOrEmpty(elapsedText)
                ? $"⚠️ Báo Vàng (>{AlertThresholds.YellowHours:0}h)"
                : $"⚠️ Báo Vàng (>{AlertThresholds.YellowHours:0}h) — đã về {elapsedText}",
            AlertLevel.Safe => string.IsNullOrEmpty(elapsedText)
                ? "⏳ An toàn"
                : $"⏳ An toàn — đã về {elapsedText}",
            AlertLevel.Done => "✅ Đã xử lý",
            _ => "—"
        };

        var import = order.Import;
        Carrier = Dash(order.Carrier);
        ForwardCarrier = Dash(import?.ForwardCarrier);
        ReturnCarrier = Dash(import?.ReturnCarrier);
        IsWatchlist = order.IsWatchlist;
        ComplaintBanner = order.ComplaintStatus.Length == 0 ? ""
            : $"⚖️ KHIẾU NẠI: {order.ComplaintStatus}" + (order.ComplaintAt.Length > 0 ? $" (từ {order.ComplaintAt})" : "")
              + (order.ComplaintNote.Length > 0 ? " — " + order.ComplaintNote : "");
        WatchlistBanner = !order.IsWatchlist ? ""
            : "⭐ ĐANG THEO DÕI" + (string.IsNullOrWhiteSpace(order.WatchlistReason) ? " (chưa ghi lý do)" : " — " + order.WatchlistReason.Trim());
        WatchlistText = order.IsWatchlist
            ? (string.IsNullOrWhiteSpace(order.WatchlistReason) ? "⭐ Có theo dõi" : $"⭐ Có — {order.WatchlistReason.Trim()}")
            : "Không";

        HasOverviewGroup =
            !string.IsNullOrWhiteSpace(order.Platform) ||
            !string.IsNullOrWhiteSpace(order.OrderType) ||
            !string.IsNullOrWhiteSpace(order.PlatformStatus) ||
            !string.IsNullOrWhiteSpace(order.Status) ||
            !string.IsNullOrWhiteSpace(order.PreOrder) ||
            order.Price > 0 ||
            AlertLevel != AlertLevel.None ||
            !string.IsNullOrWhiteSpace(order.Carrier) ||
            !string.IsNullOrWhiteSpace(import?.ForwardCarrier) ||
            !string.IsNullOrWhiteSpace(import?.ReturnCarrier) ||
            order.IsWatchlist ||
            !string.IsNullOrWhiteSpace(order.WatchlistReason);

        // 2. Mã
        CodeRows =
        [
            MakeCodeRow("Mã đơn", order.OrderId),
            MakeCodeRow("Mã yêu cầu hoàn", order.ReturnOrderId),
            MakeCodeRow("Mã vận đơn", order.Track),
            MakeCodeRow("Mã vận đơn hoàn", order.ReturnTrack),
            MakeCodeRow("Mã giao đi", order.ForwardTrack),
            MakeCodeRow("Mã phụ (AltTrack)", order.AltTrack)
        ];

        var historyCodes = new List<OrderHistoryCodeChip>();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (order.TrackingCodes is { Count: > 0 })
        {
            foreach (var code in order.TrackingCodes)
            {
                var trimmed = code?.Trim();
                if (!string.IsNullOrEmpty(trimmed) && seenCodes.Add("tc:" + trimmed))
                {
                    historyCodes.Add(new OrderHistoryCodeChip("Tracking Code", trimmed));
                }
            }
        }
        if (order.ScanAliases is { Count: > 0 })
        {
            foreach (var alias in order.ScanAliases)
            {
                var trimmed = alias?.Trim();
                if (!string.IsNullOrEmpty(trimmed) && seenCodes.Add("sa:" + trimmed))
                {
                    historyCodes.Add(new OrderHistoryCodeChip("Alias quét", trimmed));
                }
            }
        }

        HistoryCodes = historyCodes;
        HasHistoryCodes = historyCodes.Count > 0;
        HasCodesGroup = CodeRows.Any(c => c.CanCopy) || HasHistoryCodes;

        // 3. Mốc thời gian
        ShipDate = Dash(order.ShipDate);
        ReturnDate = Dash(order.ReturnDate);
        ReturnedAt = Dash(order.ReturnedAt);
        RecvDate = Dash(order.RecvDate);
        Deadline = Dash(order.Deadline);
        ImportWeek = Dash(order.ImportWeek);
        ImportedAt = Dash(order.ImportedAt);

        HasTimelineGroup =
            !string.IsNullOrWhiteSpace(order.ShipDate) ||
            !string.IsNullOrWhiteSpace(order.ReturnDate) ||
            !string.IsNullOrWhiteSpace(order.ReturnedAt) ||
            !string.IsNullOrWhiteSpace(order.RecvDate) ||
            !string.IsNullOrWhiteSpace(order.Deadline) ||
            !string.IsNullOrWhiteSpace(order.ImportWeek) ||
            !string.IsNullOrWhiteSpace(order.ImportedAt);

        // 4. Sản phẩm
        var sourceItems = order.Items ?? [];
        var itemRows = new List<OrderDetailItemRow>(sourceItems.Count);
        var mixedItems = ItemDisplay.IsMixed(sourceItems);
        var totalQty = 0;
        for (var i = 0; i < sourceItems.Count; i++)
        {
            var item = sourceItems[i];
            totalQty += Math.Max(0, item.Qty);
            var pricesText = item.ImportPrices is { Count: > 0 }
                ? string.Join(", ", item.ImportPrices.Select(kv => $"{kv.Key}: {string.Format(ViCulture, "{0:N0} đ", kv.Value)}"))
                : "—";
            var kind = !mixedItems ? ItemLineKind.Neutral : item.ReturnedQty > 0 ? ItemLineKind.Returned : ItemLineKind.NotReturned;
            itemRows.Add(new OrderDetailItemRow(
                i + 1,
                Dash(item.Product),
                Dash(item.Color),
                Dash(item.Size),
                item.Qty,
                Dash(item.Sku),
                Dash(item.SkuId),
                pricesText,
                item.ReturnedQty,
                kind == ItemLineKind.Returned ? $"Hoàn {item.ReturnedQty}" : kind == ItemLineKind.NotReturned ? "Không hoàn" : "—",
                kind));
        }

        Items = itemRows;
        TotalQty = totalQty;
        TotalQtyText = $"Tổng SL: {totalQty:N0}";
        IsPartialReturn = order.IsPartialReturn;
        var chipText = ItemDisplay.Chip(sourceItems, order.IsPartialReturn);
        ReturnSummary = mixedItems
            ? $"{chipText.Replace(" SP", " sản phẩm")}: {string.Join(", ", sourceItems.Where(x => x.ReturnedQty > 0).Select(x => $"{x.Product} x{x.ReturnedQty}"))}"
            : string.Empty;
        HasReturnSummary = ReturnSummary.Length > 0;
        HasItemsGroup = itemRows.Count > 0 || IsPartialReturn;

        // 5. Ghi chú
        Note = Dash(order.Note);
        BuyerNote = Dash(order.BuyerNote);
        UserNote = Dash(order.UserNote);
        HasNotesGroup =
            !string.IsNullOrWhiteSpace(order.Note) ||
            !string.IsNullOrWhiteSpace(order.BuyerNote) ||
            !string.IsNullOrWhiteSpace(order.UserNote);

        // 6. Ảnh
        PhotoId = order.PhotoId?.Trim() ?? string.Empty;
        _mainPhotoId = PhotoId;
        PhotoStatusText = "Đang tải ảnh...";

        // 7. Dữ liệu từ file sàn
        ImportPriceBasis = Dash(import?.ImportPriceBasis);
        ImportSelectedPriceMode = Dash(import?.ImportSelectedPriceMode);
        PriceModes = import?.PriceModes is { Count: > 0 }
            ? import.PriceModes
                .Select(kv => new PriceModeRow(kv.Key, string.Format(ViCulture, "{0:N0} đ", kv.Value)))
                .ToList()
            : Array.Empty<PriceModeRow>();
        HasPriceModes = PriceModes.Count > 0;

        _allRawDetails = import?.RawDetails is { Count: > 0 }
            ? import.RawDetails.Select(kv => new RawDetailRow(kv.Key, string.IsNullOrWhiteSpace(kv.Value) ? "—" : kv.Value)).ToList()
            : Array.Empty<RawDetailRow>();
        FilteredRawDetails = _allRawDetails;
        HasRawDetails = _allRawDetails.Count > 0;

        HasRawImportGroup =
            !string.IsNullOrWhiteSpace(import?.ImportPriceBasis) ||
            !string.IsNullOrWhiteSpace(import?.ImportSelectedPriceMode) ||
            HasPriceModes ||
            HasRawDetails;
    }

    public string OrderInternalId => _order.Id;
    public string PrimaryTracking { get; }
    public string WindowTitle { get; }
    public string HeaderOrderId { get; }
    public string HeaderTracking { get; }
    public string HeaderPlatform { get; }
    public string HeaderStatus { get; }

    // Tổng quan
    public bool HasOverviewGroup { get; }
    public string Platform { get; }
    public string OrderType { get; }
    public string PlatformStatus { get; }
    public string Status { get; }
    public string PreOrder { get; }
    public AlertLevel AlertLevel { get; }
    public string AlertText { get; }
    public string PriceFormatted { get; }
    public string Carrier { get; }
    public string ForwardCarrier { get; }
    public string ReturnCarrier { get; }
    public bool IsWatchlist { get; }
    public string WatchlistText { get; }
    /// <summary>Dải nổi bật đầu chi tiết khi đơn đang được theo dõi (lý do theo dõi).</summary>
    public string WatchlistBanner { get; private set; } = "";
    /// <summary>Khiếu nại (26/11/2026): trạng thái + ngày + ghi chú; rỗng nếu không khiếu nại.</summary>
    public string ComplaintBanner { get; private set; } = "";
    public bool HasComplaint => ComplaintBanner.Length > 0;

    // Mã
    public bool HasCodesGroup { get; }
    public IReadOnlyList<OrderCodeRow> CodeRows { get; }
    public IReadOnlyList<OrderHistoryCodeChip> HistoryCodes { get; }
    public bool HasHistoryCodes { get; }
    public string ForwardTrackWarning => "Mã giao đi dùng chung không đủ để nhận kiện";

    // Mốc thời gian
    public bool HasTimelineGroup { get; }
    public string ShipDate { get; }
    public string ReturnDate { get; }
    public string ReturnedAt { get; }
    public string RecvDate { get; }
    public string Deadline { get; }
    public string ImportWeek { get; }
    public string ImportedAt { get; }

    // Sản phẩm
    public bool HasItemsGroup { get; }
    public IReadOnlyList<OrderDetailItemRow> Items { get; }
    public int TotalQty { get; }
    public string TotalQtyText { get; }
    public bool IsPartialReturn { get; }
    /// <summary>Hoàn một phần có biết sản phẩm nào hoàn: "Hoàn 1/3 sản phẩm: Áo x1".</summary>
    public string ReturnSummary { get; }
    public bool HasReturnSummary { get; }

    // Ghi chú
    public bool HasNotesGroup { get; }
    public string Note { get; }
    public string BuyerNote { get; }
    public string UserNote { get; }

    // Ảnh
    /// <summary>Ảnh gắn sẵn trong đơn (có thể trống).</summary>
    public string PhotoId { get; }
    /// <summary>Ảnh đang hiện to: ảnh của đơn, không có thì ảnh mới nhất trên cloud (cả ảnh điện thoại chụp).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhotoId))]
    private string _mainPhotoId = string.Empty;
    public bool HasPhotoId => !string.IsNullOrWhiteSpace(MainPhotoId);
    /// <summary>Các ảnh còn lại (ảnh nhỏ bên dưới, bấm để xem to).</summary>
    public System.Collections.ObjectModel.ObservableCollection<DetailPhotoThumb> ExtraPhotos { get; } = new();
    [ObservableProperty]
    private string _photoCountText = string.Empty;

    [ObservableProperty]
    private BitmapImage? _thumbnailImage;

    [ObservableProperty]
    private bool _isPhotoLoading;

    [ObservableProperty]
    private bool _hasPhotoLoaded;

    [ObservableProperty]
    private string _photoStatusText = "Chưa có ảnh";

    // Dữ liệu từ file sàn
    public bool HasRawImportGroup { get; }
    public string ImportPriceBasis { get; }
    public string ImportSelectedPriceMode { get; }
    public IReadOnlyList<PriceModeRow> PriceModes { get; }
    public bool HasPriceModes { get; }
    public bool HasRawDetails { get; }

    [ObservableProperty]
    private bool _isRawImportExpanded;

    [ObservableProperty]
    private string _rawDetailsFilter = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<RawDetailRow> _filteredRawDetails = Array.Empty<RawDetailRow>();

    // Lịch sử sửa
    [ObservableProperty]
    private bool _isHistoryLoading;

    [ObservableProperty]
    private IReadOnlyList<HistoryRowItem> _historyEntries = Array.Empty<HistoryRowItem>();

    [ObservableProperty]
    private bool _isHistoryEmpty = true;

    [ObservableProperty]
    private string _copiedToastText = string.Empty;

    partial void OnRawDetailsFilterChanged(string value)
    {
        if (_allRawDetails.Count == 0)
        {
            FilteredRawDetails = Array.Empty<RawDetailRow>();
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            FilteredRawDetails = _allRawDetails;
            return;
        }

        var kw = value.Trim();
        FilteredRawDetails = _allRawDetails
            .Where(r => r.Column.Contains(kw, StringComparison.OrdinalIgnoreCase)
                     || r.Value.Contains(kw, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    [RelayCommand]
    public void CopyCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        _clipboardService?.SetText(code);
        CopiedToastText = $"Đã sao chép: {code}";
    }

    [RelayCommand]
    public void OpenPhotoViewer()
    {
        if (!HasPhotoId) return;
        _photoViewerService?.ShowPhoto(MainPhotoId, _order.OrderId, PrimaryTracking);
    }

    [RelayCommand]
    public void OpenExtraPhoto(DetailPhotoThumb? thumb)
    {
        if (thumb is null) return;
        _photoViewerService?.ShowPhoto(thumb.PhotoId, _order.OrderId, PrimaryTracking);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(
            LoadPhotoAsync(cancellationToken),
            LoadHistoryAsync(cancellationToken));
    }

    private async Task LoadPhotoAsync(CancellationToken cancellationToken)
    {
        if (_photoProvider is null)
        {
            HasPhotoLoaded = false;
            PhotoStatusText = "Chưa có ảnh";
            return;
        }

        // ảnh của đơn + ảnh trên cloud (kể cả điện thoại chụp), bỏ trùng; ảnh của đơn đứng đầu
        var ids = new List<string>();
        if (!string.IsNullOrWhiteSpace(PhotoId)) ids.Add(PhotoId);
        try
        {
            foreach (var id in await _photoProvider.ListPhotoIdsAsync(_order.Id, cancellationToken))
                if (!ids.Contains(id, StringComparer.Ordinal)) ids.Add(id);
        }
        catch (OperationCanceledException) { return; }
        catch { /* không lấy được danh sách: vẫn hiện ảnh của đơn nếu có */ }

        if (ids.Count == 0)
        {
            MainPhotoId = string.Empty;
            HasPhotoLoaded = false;
            PhotoStatusText = "Chưa có ảnh";
            return;
        }
        MainPhotoId = ids[0];
        PhotoCountText = ids.Count > 1 ? $"{ids.Count} ảnh (cả ảnh trên cloud)" : string.Empty;
        _ = LoadExtraPhotosAsync(ids.Skip(1).Take(12).ToList(), cancellationToken);

        IsPhotoLoading = true;
        try
        {
            var data = await _photoProvider.GetPhotoAsync(MainPhotoId, cancellationToken);
            if (data is null || data.Bytes is null || data.Bytes.Length == 0)
            {
                HasPhotoLoaded = false;
                PhotoStatusText = "Không tải được ảnh (ảnh bị mất)";
                return;
            }

            var bmp = await Task.Run(() => DecodeFrozenThumbnail(data.Bytes), cancellationToken);
            if (bmp is null)
            {
                HasPhotoLoaded = false;
                PhotoStatusText = "Không tải được ảnh (dữ liệu ảnh lỗi)";
                return;
            }

            ThumbnailImage = bmp;
            HasPhotoLoaded = true;
            PhotoStatusText = string.Empty;
        }
        catch (OperationCanceledException)
        {
            // Bỏ qua khi đóng cửa sổ
        }
        catch
        {
            HasPhotoLoaded = false;
            PhotoStatusText = "Không tải được ảnh";
        }
        finally
        {
            IsPhotoLoading = false;
        }
    }

    private async Task LoadHistoryAsync(CancellationToken cancellationToken)
    {
        if (_orderEditService is null || string.IsNullOrWhiteSpace(_order.Id))
        {
            HistoryEntries = Array.Empty<HistoryRowItem>();
            IsHistoryEmpty = true;
            return;
        }

        IsHistoryLoading = true;
        try
        {
            var entries = await _orderEditService.GetHistoryAsync(_order.Id, cancellationToken);
            var rows = entries.Select(e => new HistoryRowItem(e)).ToList();
            HistoryEntries = rows;
            IsHistoryEmpty = rows.Count == 0;
        }
        catch (OperationCanceledException)
        {
            // Bỏ qua khi đóng cửa sổ
        }
        catch
        {
            HistoryEntries = Array.Empty<HistoryRowItem>();
            IsHistoryEmpty = true;
        }
        finally
        {
            IsHistoryLoading = false;
        }
    }

    private static OrderCodeRow MakeCodeRow(string label, string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        var hasVal = !string.IsNullOrEmpty(trimmed);
        return new OrderCodeRow(label, hasVal ? trimmed : "—", trimmed, hasVal);
    }

    private static string Dash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero) return "0 phút";
        if (elapsed.TotalDays >= 1)
        {
            var days = (int)elapsed.TotalDays;
            return $"{days} ngày {elapsed.Hours} giờ";
        }
        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours} giờ {elapsed.Minutes} phút";
        }
        return $"{Math.Max(1, elapsed.Minutes)} phút";
    }

    private async Task LoadExtraPhotosAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        foreach (var id in ids)
        {
            try
            {
                var data = await _photoProvider!.GetPhotoAsync(id, cancellationToken);
                if (data?.Bytes is not { Length: > 0 } bytes) continue;
                var bmp = await Task.Run(() => DecodeFrozenThumbnail(bytes), cancellationToken);
                if (bmp is not null) ExtraPhotos.Add(new DetailPhotoThumb(id, bmp));
            }
            catch (OperationCanceledException) { return; }
            catch { /* bỏ ảnh lỗi */ }
        }
    }

    private static BitmapImage? DecodeFrozenThumbnail(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 360;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Một ảnh nhỏ trong chi tiết đơn.</summary>
public sealed record DetailPhotoThumb(string PhotoId, System.Windows.Media.ImageSource Image);
