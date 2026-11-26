using System.Globalization;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.App.ViewModels;

/// <summary>
/// Bản chiếu đọc nhanh cho từng dòng trên DataGrid (tính sẵn AlertLevel, chuỗi tìm kiếm không dấu, định dạng hiển thị)
/// giúp lọc & cuộn 50.000 dòng mượt mà không giật UI.
/// </summary>
public sealed class ReturnOrderRowViewModel
{
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

    /// <summary>Dấu nội dung đơn lúc dựng dòng: nạp lại mà đơn không đổi thì DÙNG LẠI dòng cũ (bảng không vẽ lại, giữ chỗ cuộn/dòng chọn).</summary>
    public string Fingerprint { get; }
    /// <summary>Lúc dựng (cảnh báo vàng/đỏ tính theo giờ: dòng dùng lại tối đa vài phút rồi dựng mới).</summary>
    public DateTime BuiltAt { get; }

    /// <summary>Dấu nội dung đơn (mọi trường bảng hiển thị/lọc).</summary>
    public static string FingerprintOf(Order o)
    {
        var sb = new System.Text.StringBuilder(256);
        void A(string? v) { sb.Append(v).Append('\u001f'); }
        A(o.Id); A(o.OrderId); A(o.ReturnOrderId); A(o.Track); A(o.ReturnTrack); A(o.AltTrack); A(o.ForwardTrack); A(string.Join(",", o.TrackingCodes));
        A(o.Platform); A(o.OrderType); A(o.PlatformStatus); A(o.PreOrder); A(o.Status); A(o.Carrier); A(o.ShipDate); A(o.ReturnDate); A(o.RecvDate);
        A(o.ReturnedAt); A(o.Deadline); A(o.Price.ToString(CultureInfo.InvariantCulture)); A(o.Note); A(o.BuyerNote); A(o.UserNote); A(o.ImportWeek);
        A(o.ImportedAt); A(o.IsWatchlist ? "1" : "0"); A(o.WatchlistReason); A(o.IsPartialReturn ? "1" : "0"); A(o.PhotoId);
        foreach (var i in o.Items) { A(i.Product); A(i.Color); A(i.Size); A(i.Qty.ToString(CultureInfo.InvariantCulture)); A(i.ReturnedQty.ToString(CultureInfo.InvariantCulture)); A(i.Sku); }
        return sb.ToString();
    }

    public ReturnOrderRowViewModel(Order source, int stt, DateTime now, AlertPolicy? policy = null, string? fingerprint = null)
    {
        Source = source;
        Stt = stt;
        Fingerprint = fingerprint ?? FingerprintOf(source);
        BuiltAt = now;
        ArrivedAtDate = AlertThresholds.TryParseVietnamDateTime(source.ReturnedAt);
        ShipDateParsed = AlertThresholds.TryParseVietnamDateTime(source.ShipDate);
        ReturnDateParsed = AlertThresholds.TryParseVietnamDateTime(source.ReturnDate);
        ImportedAtParsed = AlertThresholds.TryParseVietnamDateTime(source.ImportedAt);
        AlertLevel = AlertThresholds.EvaluateOrder(source, now, policy);

        if (ArrivedAtDate is not null && AlertLevel is AlertLevel.Safe or AlertLevel.Yellow or AlertLevel.Red)
        {
            var elapsed = now - ArrivedAtDate.Value;
            ElapsedHours = Math.Max(0, elapsed.TotalHours);
            ElapsedText = FormatElapsed(elapsed);
        }
        else
        {
            ElapsedHours = 0;
            ElapsedText = string.Empty;
        }

        AlertBadgeText = AlertLevel switch
        {
            AlertLevel.Red => $"🚨 Quá {AlertThresholds.RedHours:0}h ({ElapsedText})",
            AlertLevel.Yellow => $"⚠️ Quá {AlertThresholds.YellowHours:0}h ({ElapsedText})",
            AlertLevel.Safe => $"⏳ An toàn ({ElapsedText})",
            AlertLevel.Done => "✅ Đã xử lý",
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

        DisplayTracking = BuildTrackingDisplay(source);
        ItemsSummary = BuildItemsSummary(source);
        ItemLines = ItemDisplay.Build(source.Items);
        PartialChip = ItemDisplay.Chip(source.Items, source.IsPartialReturn);
        ItemsTooltip = ItemDisplay.Tooltip(source.Items, source.IsPartialReturn);
        TotalQty = source.Items.Count > 0 ? source.Items.Sum(i => Math.Max(i.Qty, 0)) : 0;
        PriceFormatted = string.Format(ViCulture, "{0:N0} đ", source.Price);
        PhotoIndicator = string.IsNullOrWhiteSpace(source.PhotoId) ? "—" : "📷 Có ảnh";
        NoteDisplay = !string.IsNullOrWhiteSpace(source.UserNote)
            ? $"{source.UserNote} • {source.Note}".Trim(' ', '•')
            : source.Note;

        ItemsBlob = AlertThresholds.NormalizeForSearch(ItemsSummary);
        SearchBlob = AlertThresholds.NormalizeForSearch(
            $"{source.OrderId} {source.ReturnOrderId} {source.Track} {source.ReturnTrack} {source.ForwardTrack} {source.AltTrack} " +
            $"{source.Platform} {source.Carrier} {source.Status} {source.PlatformStatus} {source.OrderType} " +
            $"{ItemsSummary} {source.Note} {source.BuyerNote} {source.UserNote}");
    }

    public Order Source { get; }
    public int Stt { get; }

    public string OrderId => Source.OrderId;
    public string ReturnOrderId => Source.ReturnOrderId;
    public string Platform => Source.Platform;
    public string OrderType => Source.OrderType;
    public string PlatformStatus => Source.PlatformStatus;
    public string Status => Source.Status;
    public string Carrier => Source.Carrier;
    public string ShipDate => Source.ShipDate;
    public string ReturnDate => Source.ReturnDate;
    public string ReturnedAt => Source.ReturnedAt;
    public string RecvDate => Source.RecvDate;
    public decimal Price => Source.Price;
    public bool IsWatchlist => Source.IsWatchlist;
    /// <summary>Nhãn cột ⭐ (trống nếu không theo dõi) và lời nhắc khi rê chuột: lý do theo dõi.</summary>
    public string WatchMark => Source.IsWatchlist ? "⭐" : "";
    public string? WatchTooltip => Source.IsWatchlist
        ? "Đang theo dõi" + (string.IsNullOrWhiteSpace(Source.WatchlistReason) ? "" : ": " + Source.WatchlistReason.Trim())
        : null;
    public bool IsPartialReturn => Source.IsPartialReturn;

    public string DisplayTracking { get; }
    public string ItemsSummary { get; }
    public IReadOnlyList<ItemLine> ItemLines { get; }
    public string PartialChip { get; }
    public string ItemsTooltip { get; }
    public int TotalQty { get; }
    public string PriceFormatted { get; }
    public string PhotoId => Source.PhotoId;
    public bool HasPhoto => !string.IsNullOrWhiteSpace(Source.PhotoId);
    public string PhotoIndicator { get; }
    public string NoteDisplay { get; }

    public DateTime? ArrivedAtDate { get; }
    public DateTime? ShipDateParsed { get; }
    public DateTime? ReturnDateParsed { get; }
    /// <summary>Giờ nạp đơn (ISO của dữ liệu cũ hoặc giờ Việt Nam của đơn tạo mới).</summary>
    public DateTime? ImportedAtParsed { get; }
    /// <summary>Khóa "mới nạp": giờ nạp; đơn cũ không có giờ nạp thì dùng ngày hoàn, rồi ngày giao.</summary>
    public DateTime? NewestKey => ImportedAtParsed ?? ReturnDateParsed ?? ShipDateParsed;

    public AlertLevel AlertLevel { get; }
    public int AlertPriority { get; }
    public double ElapsedHours { get; }
    public string ElapsedText { get; }
    public string AlertBadgeText { get; }

    /// <summary>Chuỗi đã bỏ dấu tiếng Việt & viết thường để tìm kiếm siêu nhanh.</summary>
    public string SearchBlob { get; }

    /// <summary>Tên sản phẩm đã bỏ dấu, dùng cho bộ lọc "Sản phẩm".</summary>
    public string ItemsBlob { get; }

    private static string BuildTrackingDisplay(Order o)
    {
        var ret = string.IsNullOrWhiteSpace(o.ReturnTrack) ? o.Track : o.ReturnTrack;
        if (!string.IsNullOrWhiteSpace(ret) && !string.IsNullOrWhiteSpace(o.ForwardTrack)
            && !ret.Equals(o.ForwardTrack, StringComparison.OrdinalIgnoreCase))
        {
            return $"{ret}  (Giao: {o.ForwardTrack})";
        }

        if (!string.IsNullOrWhiteSpace(ret)) return ret;
        if (!string.IsNullOrWhiteSpace(o.ForwardTrack)) return $"Giao: {o.ForwardTrack}";
        return "Chưa có MVĐ";
    }

    private static string BuildItemsSummary(Order o)
    {
        if (o.Items.Count == 0) return "Chưa có sản phẩm";
        var first = o.Items[0];
        var variant = string.Join("/", new[] { first.Color, first.Size }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var firstText = string.IsNullOrWhiteSpace(variant)
            ? $"{first.Product} (x{first.Qty})"
            : $"{first.Product} ({variant} x{first.Qty})";
        var partialPrefix = o.IsPartialReturn ? "[1 phần] " : string.Empty;
        return o.Items.Count == 1
            ? $"{partialPrefix}{firstText}"
            : $"{partialPrefix}{firstText} +{o.Items.Count - 1} món";
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
