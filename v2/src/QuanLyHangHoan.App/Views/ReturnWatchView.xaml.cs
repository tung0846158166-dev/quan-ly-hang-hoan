using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.App.Views;

public partial class ReturnWatchView : UserControl
{
    private double? _savedScrollOffset;

    public ReturnWatchView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanging oldChanging)
            oldChanging.PropertyChanging -= OnVmPropertyChanging;
        if (e.OldValue is INotifyPropertyChanged oldChanged)
            oldChanged.PropertyChanged -= OnVmPropertyChanged;

        if (e.NewValue is INotifyPropertyChanging newChanging)
            newChanging.PropertyChanging += OnVmPropertyChanging;
        if (e.NewValue is INotifyPropertyChanged newChanged)
            newChanged.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanging(object? sender, PropertyChangingEventArgs e)
    {
        if (e.PropertyName == nameof(ReturnWatchViewModel.VisibleOrders))
        {
            _savedScrollOffset = FindScrollViewer(ReturnWatchGrid)?.VerticalOffset;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReturnWatchViewModel.VisibleOrders) && _savedScrollOffset is double offset && offset > 0)
        {
            _savedScrollOffset = null;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                FindScrollViewer(ReturnWatchGrid)?.ScrollToVerticalOffset(offset);
            });
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            var nested = FindScrollViewer(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private void ReturnWatchGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ReturnWatchViewModel vm)
        {
            vm.UpdateSelectedRows(ReturnWatchGrid.SelectedItems.OfType<ReturnWatchRowViewModel>());
        }
    }

    private async void ReturnWatchGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ReturnWatchViewModel vm) return;

        if (e.Key == Key.Enter && vm.SelectedRow is not null)
        {
            e.Handled = true;
            vm.OpenDetailCommand.Execute(vm.SelectedRow);
            return;
        }

        if (e.Key == Key.Delete)
        {
            e.Handled = true;
            await vm.HandleDeleteKeyAsync();
        }
    }

    private void ReturnWatchGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ReturnWatchViewModel vm) return;
        var item = DataGridRowHelper.GetDoubleClickedItem<ReturnWatchRowViewModel>(e);
        if (item is not null)
        {
            vm.OpenDetailCommand.Execute(item);
        }
    }

    private void RowActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (ReturnWatchGrid.Resources["RowActionContextMenu"] is not ContextMenu menu) return;

        menu.DataContext = btn.DataContext;
        menu.PlacementTarget = btn;
        menu.IsOpen = true;
    }
}
