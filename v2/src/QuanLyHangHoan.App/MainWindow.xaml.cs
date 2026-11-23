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

    private void OnMainWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FocusGlobalSearchBox();
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
            if (!_hasShownTrayMinimizeHint)
            {
                _hasShownTrayMinimizeHint = true;
                vm.NotificationService.ShowInfo(
                    "Quản Lý Hàng Hoàn",
                    "App vẫn chạy ở khay để nhận đơn từ điện thoại");
            }
        }
    }

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
