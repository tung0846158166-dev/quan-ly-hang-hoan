using System.Windows;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.App.Views;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.App.Services;

public interface IOrderDetailService
{
    void ShowOrderDetail(Order order, DateTime? now = null);
}

public sealed class WindowsOrderDetailService : IOrderDetailService
{
    private readonly IPhotoProvider? _photoProvider;
    private readonly IPhotoViewerService? _photoViewerService;
    private readonly IOrderEditService? _orderEditService;
    private readonly IClipboardService? _clipboardService;
    private readonly Func<DateTime> _clock;

    public WindowsOrderDetailService(
        IPhotoProvider? photoProvider = null,
        IPhotoViewerService? photoViewerService = null,
        IOrderEditService? orderEditService = null,
        IClipboardService? clipboardService = null,
        Func<DateTime>? clock = null)
    {
        _photoProvider = photoProvider;
        _photoViewerService = photoViewerService;
        _orderEditService = orderEditService;
        _clipboardService = clipboardService ?? new WindowsClipboardService();
        _clock = clock ?? (() => DateTime.Now);
    }

    public void ShowOrderDetail(Order order, DateTime? now = null)
    {
        ArgumentNullException.ThrowIfNull(order);

        var vm = new OrderDetailViewModel(
            order,
            now ?? _clock(),
            _photoProvider,
            _photoViewerService,
            _orderEditService,
            _clipboardService);

        var owner = Application.Current?.Windows
                        .OfType<Window>()
                        .FirstOrDefault(w => w.IsActive && w is not OrderDetailWindow)
                    ?? (Application.Current?.MainWindow is not OrderDetailWindow ? Application.Current?.MainWindow : null);

        var window = new OrderDetailWindow
        {
            DataContext = vm,
            Owner = owner
        };
        window.Show();
        _ = vm.LoadAsync();
    }
}
