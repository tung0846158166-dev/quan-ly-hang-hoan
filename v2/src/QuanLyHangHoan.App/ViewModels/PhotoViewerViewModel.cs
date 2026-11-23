using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;

namespace QuanLyHangHoan.App.ViewModels;

public sealed partial class PhotoViewerViewModel : ObservableObject
{
    private const double MinZoom = 0.25;
    private const double MaxZoom = 4.0;
    private const double ZoomStep = 0.25;

    private readonly IPhotoProvider _photoProvider;

    public PhotoViewerViewModel(IPhotoProvider photoProvider)
    {
        _photoProvider = photoProvider;
    }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _title = "Xem ảnh đơn hàng";

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private BitmapImage? _imageSource;

    [ObservableProperty]
    private bool _isMissing;

    [ObservableProperty]
    private string _missingMessage = string.Empty;

    [ObservableProperty]
    private bool _showInlineDiffersNote;

    [ObservableProperty]
    private string _inlineDiffersNote = string.Empty;

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    public bool HasImage => ImageSource is not null;
    public string ZoomPercentText => $"{ZoomLevel * 100:0}%";

    partial void OnImageSourceChanged(BitmapImage? value)
    {
        OnPropertyChanged(nameof(HasImage));
    }

    partial void OnZoomLevelChanged(double value)
    {
        OnPropertyChanged(nameof(ZoomPercentText));
    }

    [RelayCommand]
    public void ZoomIn()
    {
        ZoomLevel = Math.Min(MaxZoom, Math.Round(ZoomLevel + ZoomStep, 2));
    }

    [RelayCommand]
    public void ZoomOut()
    {
        ZoomLevel = Math.Max(MinZoom, Math.Round(ZoomLevel - ZoomStep, 2));
    }

    [RelayCommand]
    public void ResetZoom()
    {
        ZoomLevel = 1.0;
    }

    public async Task LoadAsync(
        string photoId,
        string? orderId = null,
        string? tracking = null,
        CancellationToken cancellationToken = default)
    {
        Title = string.IsNullOrWhiteSpace(orderId) ? "Xem ảnh đơn hàng" : $"Xem ảnh — Đơn {orderId}";
        Subtitle = string.IsNullOrWhiteSpace(tracking)
            ? $"Mã ảnh: {photoId}"
            : $"MVĐ: {tracking} • Mã ảnh: {photoId}";

        IsLoading = true;
        ImageSource = null;
        IsMissing = false;
        MissingMessage = string.Empty;
        ShowInlineDiffersNote = false;
        InlineDiffersNote = string.Empty;
        ZoomLevel = 1.0;

        try
        {
            var data = await _photoProvider.GetPhotoAsync(photoId, cancellationToken);
            if (data is null || data.Bytes is null || data.Bytes.Length == 0)
            {
                IsMissing = true;
                MissingMessage = "Ảnh bị mất khỏi dữ liệu cũ";
                return;
            }

            var bitmap = await Task.Run(() => DecodeFrozenBitmap(data.Bytes), cancellationToken);
            if (bitmap is null)
            {
                IsMissing = true;
                MissingMessage = "Ảnh bị mất khỏi dữ liệu cũ";
                return;
            }

            ImageSource = bitmap;
            if (data.InlineDiffers)
            {
                ShowInlineDiffersNote = true;
                InlineDiffersNote = "Ảnh nhúng khác tệp ảnh, đang hiện tệp ảnh";
            }
        }
        catch (OperationCanceledException)
        {
            // Bỏ qua khi người dùng đóng cửa sổ trước khi tải xong
        }
        catch
        {
            IsMissing = true;
            MissingMessage = "Ảnh bị mất khỏi dữ liệu cũ";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static BitmapImage? DecodeFrozenBitmap(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
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
