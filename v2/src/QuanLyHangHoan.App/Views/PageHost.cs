using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.App.Views;

/// <summary>
/// Khung hiển thị trang (thay cho một ContentControl duy nhất). Chủ dự án 27/11/2026: đổi tab bị lag vì mỗi lần mở tab WPF dựng lại
/// TOÀN BỘ giao diện bảng (đo trên dữ liệu thật: 1–1,4 giây mỗi lần mở bảng Shopee/TikTok). Ở đây:
/// <list type="bullet">
/// <item>Trang NẶNG (bảng đơn, lưu trữ, thống kê...) dựng MỘT lần rồi giữ lại, đổi tab chỉ ẩn/hiện (đo lại: ~10 ms).</item>
/// <item>Trang đang ẩn không vẽ lại khi có dữ liệu mới (<see cref="IPageVisibilityAware"/>): giữ bản mới nhất, mở ra mới đưa lên bảng.</item>
/// <item>Lúc người dùng đang rảnh tay, dựng sẵn ngầm các bảng chính để lần đầu mở cũng nhanh (<see cref="PrewarmEnabled"/>).</item>
/// </list>
/// Trang nhẹ hoặc cần làm mới mỗi lần mở (Cài đặt, quét, nhật ký...) vẫn dựng lại như cũ.
/// </summary>
public sealed class PageHost : Grid
{
    /// <summary>Bật dựng sẵn ngầm (app thật bật lúc khởi động; test tắt để cây giao diện chỉ có trang đang mở).</summary>
    public static bool PrewarmEnabled { get; set; }

    /// <summary>Test: chọn trang được giữ lại (null = mặc định).</summary>
    public static Func<object?, bool>? TestFilter { get; set; }

    private readonly Dictionary<object, ContentControl> _cached = new(ReferenceEqualityComparer.Instance);
    private ContentControl? _transient;
    private ContentControl? _active;
    private MainViewModel? _vm;
    private DispatcherTimer? _prewarmTimer;
    private DateTime _attachedAt;

    public PageHost()
    {
        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
    }

    public static bool IsCacheable(object? page) => TestFilter is { } tf ? tf(page) : page is ReturnOrdersViewModel or ArchivedOrdersViewModel or DeletedOrdersViewModel
        or WatchlistViewModel or ComplaintsViewModel or ReturnWatchViewModel or StatisticsViewModel or HomeViewModel;

    /// <summary>Giao diện đang hiện.</summary>
    public FrameworkElement? ActiveView => _active;

    private void Attach(MainViewModel? vm)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _prewarmTimer?.Stop();
        _vm = vm;
        Children.Clear();
        _cached.Clear();
        _transient = null;
        _active = null;
        if (vm is null) return;
        vm.PropertyChanged += OnVmPropertyChanged;
        Show(vm.CurrentPage);
        _attachedAt = DateTime.UtcNow;
        if (PrewarmEnabled)
        {
            _prewarmTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(1.5) };
            _prewarmTimer.Tick += (_, _) => PrewarmNext();
            _prewarmTimer.Start();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPage)) Show(_vm?.CurrentPage);
    }

    private void Show(object? page)
    {
        ContentControl target;
        if (page is not null && IsCacheable(page))
        {
            if (!_cached.TryGetValue(page, out var cached))
            {
                cached = NewHost(page);
                _cached[page] = cached;
                Children.Add(cached);
            }
            target = cached;
            if (_transient is not null) { Children.Remove(_transient); _transient = null; }
        }
        else
        {
            if (_transient is null) { _transient = NewHost(page); Children.Add(_transient); }
            else _transient.Content = page;
            target = _transient;
        }
        _active = target;
        // báo trang đang hiện TRƯỚC khi hiện ra (đưa danh sách mới nhất lên bảng); các trang khác ẩn và giữ danh sách chờ
        foreach (var (pg, host) in _cached)
            if (pg is IPageVisibilityAware aware) aware.SetPageVisible(ReferenceEquals(host, target));
        foreach (UIElement c in Children) c.Visibility = ReferenceEquals(c, target) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Dựng sẵn MỘT bảng chính chưa từng mở, chỉ khi cửa sổ đang hiện, đã có dữ liệu và người dùng rảnh tay ≥ 2 giây.</summary>
    private void PrewarmNext()
    {
        if (_vm is not { } vm || !IsVisible) return;
        if (UserActivity.SinceLast < TimeSpan.FromSeconds(2)) return;
        var hasData = vm.ShopeeReturnVm.TotalCount + vm.TikTokReturnVm.TotalCount + vm.ArchivedOrdersVm.TotalCount > 0;
        if (!hasData && DateTime.UtcNow - _attachedAt < TimeSpan.FromSeconds(30)) return;
        object? next = new object[] { vm.ShopeeReturnVm, vm.TikTokReturnVm, vm.ShopeeCancelledVm, vm.ArchivedOrdersVm, vm.StatisticsVm, vm.OffPlatformVm, vm.WatchlistVm, vm.ComplaintsVm }
            .FirstOrDefault(p => IsCacheable(p) && !_cached.ContainsKey(p));
        if (next is null) { vm.PrepareStatistics(); return; }   // đã dựng hết: lúc rảnh tay tính sẵn Thống kê sau mỗi lần dữ liệu đổi
        if (ReferenceEquals(next, vm.StatisticsVm)) vm.PrepareStatistics();   // tính số liệu trước, rồi mới dựng giao diện
        var host = NewHost(next);
        host.Visibility = Visibility.Hidden;        // Hidden: được đo và dựng dòng nhưng không vẽ, không nhận chuột
        host.IsHitTestVisible = false;
        _cached[next] = host;
        Children.Insert(0, host);
        if (next is IPageVisibilityAware aware) aware.SetPageVisible(true);
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            host.IsHitTestVisible = true;
            if (ReferenceEquals(host, _active)) return;            // người dùng vừa mở đúng trang này
            host.Visibility = Visibility.Collapsed;
            if (next is IPageVisibilityAware a) a.SetPageVisible(false);
        });
    }

    private static ContentControl NewHost(object? page) => new()
    {
        Content = page,
        Focusable = false,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
    };
}
