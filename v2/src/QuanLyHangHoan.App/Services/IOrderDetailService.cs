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

        // Chỉ MỘT cửa sổ chi tiết: bấm đơn khác thì đóng cửa sổ cũ rồi mở cửa sổ mới (không chồng nhiều bảng lên nhau).
        CloseCurrent();

        var window = new OrderDetailWindow
        {
            DataContext = vm,
            Owner = owner
        };
        window.Closed += (_, _) => { if (ReferenceEquals(_current, window)) _current = null; };
        _current = window;
        window.Show();
        _ = vm.LoadAsync();
    }

    private OrderDetailWindow? _current;

    private void CloseCurrent()
    {
        var old = _current;
        _current = null;
        if (old is null) return;
        try { old.Close(); } catch (InvalidOperationException) { /* đang đóng sẵn */ }
    }
}
