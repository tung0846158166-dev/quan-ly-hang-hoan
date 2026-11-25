using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _saveDebounceTimer;
    private bool _placementApplied;
    private bool _isExplicitExit;
    private bool _hasShownTrayMinimizeHint;

    public bool IsHiddenToTray { get; private set; }

    public void RequestExplicitExit()
    {
        _isExplicitExit = true;
        Close();
    }

    public void RestoreFromTray()
    {
        IsHiddenToTray = false;
        if (!IsVisible)
        {
            Show();
        }
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public MainWindow()
    {
        InitializeComponent();
        Views.ClickToCopy.Notify = m => (DataContext as ViewModels.MainViewModel)?.ShowToast(m, false);   // bấm mã đơn/mã vận đơn trong bảng: báo "Đã sao chép"
        _saveDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _saveDebounceTimer.Tick += (_, _) =>
        {
            _saveDebounceTimer.Stop();
            PersistPlacement();
        };

        SourceInitialized += OnSourceInitialized;
        LocationChanged += OnPlacementChanged;
        SizeChanged += OnPlacementChanged;
        StateChanged += OnPlacementChanged;
        Closing += OnClosing;
        PreviewKeyDown += OnMainWindowPreviewKeyDown;
    }

    private void Sidebar_MouseEnter(object sender, MouseEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.IsSidebarExpanded = true;
    }

    private void Sidebar_MouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.IsSidebarExpanded = false;
    }

    private void OnMainWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FocusGlobalSearchBox();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.N && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            vm.NotificationsVm.ToggleCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && vm.CloudVm.IsPopupOpen)
        {
            vm.CloudVm.ClosePopup();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && vm.NotificationsVm.IsOpen)
        {
            vm.NotificationsVm.CloseCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!IsTextInputFocused())
            {
                FocusGlobalSearchBox();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Escape && (GlobalSearchBox.IsKeyboardFocusWithin || vm.IsGlobalSearchOpen || !string.IsNullOrEmpty(vm.GlobalSearchText)))
        {
            vm.ClearGlobalSearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    public void FocusGlobalSearchBox()
    {
        GlobalSearchBox.Focus();
        GlobalSearchBox.SelectAll();
    }

    private static bool IsTextInputFocused()
    {
        var focused = Keyboard.FocusedElement;
        if (focused is TextBoxBase or PasswordBox) return true;
        if (focused is ComboBox cb && cb.IsEditable) return true;
        return false;
    }

    private void GlobalSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        switch (e.Key)
        {
            case Key.Down:
                vm.MoveGlobalSearchSelection(1);
                if (vm.SelectedGlobalSearchResult is not null)
                {
                    GlobalSearchResultsList.ScrollIntoView(vm.SelectedGlobalSearchResult);
                }
                e.Handled = true;
                break;

            case Key.Up:
                vm.MoveGlobalSearchSelection(-1);
                if (vm.SelectedGlobalSearchResult is not null)
                {
                    GlobalSearchResultsList.ScrollIntoView(vm.SelectedGlobalSearchResult);
                }
                e.Handled = true;
                break;

            case Key.Enter:
                vm.OpenGlobalSearchResultCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void GlobalSearchResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (GlobalSearchResultsList.SelectedItem is GlobalSearchResultItem item)
        {
            vm.OpenGlobalSearchResultCommand.Execute(item);
        }
    }

    private void GlobalSearchResultsList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (e.Key == Key.Enter && GlobalSearchResultsList.SelectedItem is GlobalSearchResultItem item)
        {
            e.Handled = true;
            vm.OpenGlobalSearchResultCommand.Execute(item);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (_placementApplied) return;
        _placementApplied = true;

        if (DataContext is MainViewModel vm)
        {
            WindowPlacementService.ApplyToWindow(this, vm.LoadWindowPlacement());
        }
    }

    private void OnPlacementChanged(object? sender, EventArgs e)
    {
        if (!_placementApplied || WindowState == WindowState.Minimized) return;
        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Start();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _saveDebounceTimer.Stop();
        PersistPlacement();

        if (!_isExplicitExit && DataContext is MainViewModel vm && vm.MinimizeToTray)
        {
            e.Cancel = true;
            IsHiddenToTray = true;
            Hide();
            TrimMemoryLater();   // ẩn xuống khay: trả lại RAM cho hệ thống
            if (!_hasShownTrayMinimizeHint)
            {
                _hasShownTrayMinimizeHint = true;
                vm.NotificationService.ShowInfo(
                    "Quản Lý Hàng Hoàn",
                    "App vẫn chạy ở khay để nhận đơn từ điện thoại");
            }
        }
    }

    /// <summary>Khi app ẩn xuống khay: sau vài giây dọn rác bộ nhớ và trả bộ nhớ chưa dùng về cho Windows (app chạy cả ngày nên nên giữ nhẹ).</summary>
    private void TrimMemoryLater()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!IsHiddenToTray) return;
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
            }
            catch { /* không quan trọng */ }
        };
        timer.Start();
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimumWorkingSetSize, IntPtr maximumWorkingSetSize);

    private void PersistPlacement()
    {
        if (DataContext is not MainViewModel vm) return;
        var placement = WindowPlacementService.CapturePlacement(
            WindowState,
            Left,
            Top,
            Width,
            Height,
            RestoreBounds);
        vm.SaveWindowPlacement(placement);
    }
}
