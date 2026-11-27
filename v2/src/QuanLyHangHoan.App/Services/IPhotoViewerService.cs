using System.Windows;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.App.Views;

namespace QuanLyHangHoan.App.Services;

public interface IPhotoViewerService
{
    void ShowPhoto(string photoId, string? orderId = null, string? tracking = null);
}

public sealed class WindowsPhotoViewerService : IPhotoViewerService
{
    private readonly IPhotoProvider _photoProvider;

    public WindowsPhotoViewerService(IPhotoProvider photoProvider)
    {
        _photoProvider = photoProvider;
    }

    public void ShowPhoto(string photoId, string? orderId = null, string? tracking = null)
    {
        var vm = new PhotoViewerViewModel(_photoProvider);
        var window = new PhotoViewerWindow
        {
            DataContext = vm,
            // gắn vào cửa sổ đang mở (thường là Chi tiết đơn): đóng ảnh thì quay lại đúng cửa sổ đó
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible) ?? Application.Current?.MainWindow
        };
        window.Show();
        _ = vm.LoadAsync(photoId, orderId, tracking);
    }
}
