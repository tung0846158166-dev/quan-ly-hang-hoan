using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.ViewModels;

public sealed partial class ScanReceiveViewModel : ObservableObject
{
    private readonly IScanService _scanService;
    private readonly IScanSoundPlayer _soundPlayer;
    private readonly IImageFilePickerService? _imagePicker;
    private readonly Func<DateTime> _debounceClock;

    private string? _lastScannedCode;
    private DateTime _lastScannedAt = DateTime.MinValue;
    private byte[]? _pendingPhotoBytes;
    private string _pendingPhotoMime = "image/jpeg";

    public ScanReceiveViewModel(
        IScanService scanService,
        IScanSoundPlayer? soundPlayer = null,
        IImageFilePickerService? imagePicker = null,
        Func<DateTime>? debounceClock = null,
        bool isDemoMode = false)
    {
        _scanService = scanService;
        _soundPlayer = soundPlayer ?? new NoOpScanSoundPlayer();
        _imagePicker = imagePicker;
        _debounceClock = debounceClock ?? (() => DateTime.UtcNow);
        IsDemoMode = isDemoMode;
    }

    public event Func<Task>? ScanReceivedSucceeded;

    public bool IsDemoMode { get; }

    public ObservableCollection<ScanSessionItem> SessionHistory { get; } = new();

    [ObservableProperty]
    private string _scanInput = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isSoundEnabled = true;

    [ObservableProperty]
    private string _pendingPhotoName = string.Empty;

    [ObservableProperty]
    private bool _hasPendingPhoto;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private ScanOutcome _resultOutcome;

    [ObservableProperty]
    private bool _resultAck;

    [ObservableProperty]
    private string _resultHeadline = string.Empty;

    [ObservableProperty]
    private string _resultReason = string.Empty;

    [ObservableProperty]
    private string _resultAckText = string.Empty;

    [ObservableProperty]
    private string _resultBgHex = "#FFFFFF";

    [ObservableProperty]
    private string _resultBorderHex = "#CBD5E1";

    [ObservableProperty]
    private string _resultFgHex = "#0F172A";

    [ObservableProperty]
    private bool _hasOrderDetails;

    [ObservableProperty]
    private string _resultOrderId = string.Empty;

    [ObservableProperty]
    private string _resultTracking = string.Empty;

    [ObservableProperty]
    private string _resultPlatform = string.Empty;

    [ObservableProperty]
    private string _resultOrderType = string.Empty;

    [ObservableProperty]
    private string _resultRecvDate = string.Empty;

    [ObservableProperty]
    private string _resultItemsText = string.Empty;

    [ObservableProperty]
    private int _receivedCount;

    [ObservableProperty]
    private int _alreadyReceivedCount;

    [ObservableProperty]
    private int _errorCount;

    public bool HasSessionHistory => SessionHistory.Count > 0;

    [RelayCommand]
    private void PickPhoto()
    {
        var path = _imagePicker?.PickImageFile("Chọn ảnh đính kèm cho lần quét kế tiếp");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        try
        {
            _pendingPhotoBytes = File.ReadAllBytes(path);
            _pendingPhotoMime = Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)
                ? "image/png"
                : "image/jpeg";
            PendingPhotoName = Path.GetFileName(path);
            HasPendingPhoto = true;
        }
        catch (Exception ex)
        {
            ClearPhoto();
            HasResult = true;
            ResultOutcome = ScanOutcome.Error;
            ResultAck = false;
            ResultHeadline = "CẦN KIỂM TRA";
            ResultReason = $"Không đọc được tệp ảnh: {ex.Message}";
            ResultAckText = "CHƯA nhận — không thay đổi dữ liệu (NO-ACK)";
            ApplyColors("#FFEDD5", "#EA580C", "#C2410C");
            HasOrderDetails = false;
        }
    }

    [RelayCommand]
    private void ClearPhoto()
    {
        _pendingPhotoBytes = null;
        _pendingPhotoMime = "image/jpeg";
        PendingPhotoName = string.Empty;
        HasPendingPhoto = false;
    }

    [RelayCommand]
    public async Task SubmitScanAsync()
    {
        var code = (ScanInput ?? string.Empty).Trim();
        ScanInput = string.Empty;
        if (string.IsNullOrEmpty(code) || IsBusy) return;

        var debounceNow = _debounceClock();
        if (string.Equals(_lastScannedCode, code, StringComparison.OrdinalIgnoreCase) &&
            (debounceNow - _lastScannedAt) < TimeSpan.FromSeconds(1))
        {
            return;
        }

        _lastScannedCode = code;
        _lastScannedAt = debounceNow;

        var photoBytes = _pendingPhotoBytes;
        var photoMime = _pendingPhotoMime;
        ClearPhoto();

        IsBusy = true;
        try
        {
            var now = _scanService.GetCurrentTime();
            var result = await _scanService.ReceiveAsync(code, now, photoBytes, photoMime);
            ApplyScanResult(code, now, result);

            if (result.Outcome == ScanOutcome.Received && result.Ack && ScanReceivedSucceeded is not null)
            {
                await ScanReceivedSucceeded.Invoke();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyScanResult(string code, DateTime now, ScanResult result)
    {
        HasResult = true;
        ResultOutcome = result.Outcome;
        ResultAck = result.Ack;

        var (headline, bg, border, fg) = result.Outcome switch
        {
            ScanOutcome.Received => ("ĐÃ NHẬN", "#DCFCE7", "#16A34A", "#15803D"),
            ScanOutcome.AlreadyReceived => ("ĐÃ NHẬN TRƯỚC ĐÓ", "#FEF3C7", "#D97706", "#B45309"),
            ScanOutcome.Ambiguous => ("NHIỀU KIỆN KHỚP - KIỂM TRA THỦ CÔNG", "#FEE2E2", "#DC2626", "#B91C1C"),
            ScanOutcome.NotFound => ("KHÔNG TÌM THẤY", "#FEE2E2", "#DC2626", "#B91C1C"),
            _ => ("CẦN KIỂM TRA", "#FFEDD5", "#EA580C", "#C2410C")
        };

        ResultHeadline = headline;
        ApplyColors(bg, border, fg);

        ResultReason = result.Outcome == ScanOutcome.AlreadyReceived && !string.IsNullOrWhiteSpace(result.Order?.RecvDate)
            ? $"Giờ nhận trước đó: {result.Order.RecvDate}"
            : result.Message;

        // Quy tắc 6: CHỈ hiện "đã lưu" khi Ack == true.
        ResultAckText = result.Ack
            ? "✓ Đã lưu bền vào cơ sở dữ liệu (ACK)"
            : result.Outcome == ScanOutcome.AlreadyReceived
                ? "CHƯA nhận mới — kiện đã được nhận trước đó (NO-ACK)"
                : "CHƯA nhận — không thay đổi dữ liệu (NO-ACK)";

        PopulateOrderDetails(result.Order);

        switch (result.Outcome)
        {
            case ScanOutcome.Received:
                ReceivedCount++;
                if (IsSoundEnabled) _soundPlayer.PlaySuccess();
                break;
            case ScanOutcome.AlreadyReceived:
                AlreadyReceivedCount++;
                if (IsSoundEnabled) _soundPlayer.PlayError();
                break;
            default:
                ErrorCount++;
                if (IsSoundEnabled) _soundPlayer.PlayError();
                break;
        }

        SessionHistory.Insert(0, new ScanSessionItem(
            now.ToString("HH:mm:ss"),
            code,
            result.Outcome,
            headline,
            ResultReason,
            result.Ack,
            bg,
            border,
            fg));
        OnPropertyChanged(nameof(HasSessionHistory));
    }

    private void ApplyColors(string bg, string border, string fg)
    {
        ResultBgHex = bg;
        ResultBorderHex = border;
        ResultFgHex = fg;
    }

    private void PopulateOrderDetails(Order? order)
    {
        if (order is null)
        {
            HasOrderDetails = false;
            ResultOrderId = string.Empty;
            ResultTracking = string.Empty;
            ResultPlatform = string.Empty;
            ResultOrderType = string.Empty;
            ResultRecvDate = string.Empty;
            ResultItemsText = string.Empty;
            return;
        }

        HasOrderDetails = true;
        ResultOrderId = string.IsNullOrWhiteSpace(order.OrderId) ? "(Không có)" : order.OrderId;
        ResultTracking = !string.IsNullOrWhiteSpace(order.ReturnTrack) &&
                         !string.Equals(order.ReturnTrack, order.Track, StringComparison.OrdinalIgnoreCase)
            ? $"{order.Track} (Hoàn: {order.ReturnTrack})"
            : (!string.IsNullOrWhiteSpace(order.Track) ? order.Track : order.ReturnTrack);
        ResultPlatform = string.IsNullOrWhiteSpace(order.Platform) ? "-" : order.Platform;
        ResultOrderType = string.IsNullOrWhiteSpace(order.OrderType) ? "-" : order.OrderType;
        ResultRecvDate = string.IsNullOrWhiteSpace(order.RecvDate) ? "-" : order.RecvDate;
        ResultItemsText = order.Items.Count > 0
            ? string.Join(" | ", order.Items.Select(FormatItem))
            : "(Chưa có thông tin sản phẩm)";
    }

    private static string FormatItem(OrderItem item)
    {
        var variant = string.Join(" ", new[] { item.Color, item.Size }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return string.IsNullOrWhiteSpace(variant)
            ? $"{item.Product} x{item.Qty}"
            : $"{item.Product} ({variant}) x{item.Qty}";
    }
}

public sealed record ScanSessionItem(
    string ScannedAtText,
    string Code,
    ScanOutcome Outcome,
    string OutcomeLabel,
    string Message,
    bool Ack,
    string BgHex,
    string BorderHex,
    string FgHex);
