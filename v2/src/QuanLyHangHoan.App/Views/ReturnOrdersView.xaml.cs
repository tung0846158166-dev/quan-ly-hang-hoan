using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.App.Views;

public partial class ReturnOrdersView : UserControl
{
    private double? _savedScrollOffset;

    public ReturnOrdersView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => TryReveal();
    }

    private void OnRevealRequested(ReturnOrderRowViewModel row)
    {
        // Gọi trễ một nhịp để bảng kịp dựng xong (trang vừa được mở) rồi mới cuộn tới.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(TryReveal));
    }

    /// <summary>Cuộn tới dòng được chọn bằng lệnh, chọn đúng dòng đó và đưa tiêu điểm vào bảng để thấy rõ nó được tô chọn.</summary>
    private void TryReveal()
    {
        if (DataContext is not ReturnOrdersViewModel vm || vm.PendingReveal is not { } row || !OrdersGrid.IsLoaded) return;
        if (!vm.VisibleOrders.Contains(row)) { vm.PendingReveal = null; return; }
        vm.PendingReveal = null;
        GridReveal.Perform(OrdersGrid, row);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ReturnOrdersViewModel oldVm) { oldVm.ClearSelectionRequested -= OnClearSelectionRequested; oldVm.RevealRequested -= OnRevealRequested; }
        if (e.NewValue is ReturnOrdersViewModel newVm)
        {
            newVm.ClearSelectionRequested += OnClearSelectionRequested;
            newVm.RevealRequested += OnRevealRequested;
            if (newVm.PendingReveal is not null) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(TryReveal));
        }

        if (e.OldValue is INotifyPropertyChanging oldChanging)
            oldChanging.PropertyChanging -= OnVmPropertyChanging;
        if (e.OldValue is INotifyPropertyChanged oldChanged)
            oldChanged.PropertyChanged -= OnVmPropertyChanged;

        if (e.NewValue is INotifyPropertyChanging newChanging)
            newChanging.PropertyChanging += OnVmPropertyChanging;
        if (e.NewValue is INotifyPropertyChanged newChanged)
            newChanged.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnClearSelectionRequested() => OrdersGrid.UnselectAll();

    private void OnVmPropertyChanging(object? sender, PropertyChangingEventArgs e)
    {
        if (e.PropertyName == nameof(ReturnOrdersViewModel.VisibleOrders))
        {
            _savedScrollOffset = FindScrollViewer(OrdersGrid)?.VerticalOffset;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReturnOrdersViewModel.VisibleOrders) && _savedScrollOffset is double offset && offset > 0)
        {
            _savedScrollOffset = null;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                FindScrollViewer(OrdersGrid)?.ScrollToVerticalOffset(offset);
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

    private void OrdersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ReturnOrdersViewModel vm)
        {
            vm.UpdateSelectedRows(OrdersGrid.SelectedItems.OfType<ReturnOrderRowViewModel>());
        }
    }

    /// <summary>Tự sắp xếp trong ViewModel (so sánh trực tiếp) thay vì để WPF sắp 50.000 dòng bằng reflection.</summary>
    private void OrdersGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not ReturnOrdersViewModel vm) return;
        var member = string.IsNullOrWhiteSpace(e.Column.SortMemberPath) ? null : e.Column.SortMemberPath;
        if (!vm.SortByColumn(member, out var dir)) return;
        foreach (var c in OrdersGrid.Columns) c.SortDirection = null;
        e.Column.SortDirection = dir;
    }

    private async void OrdersGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ReturnOrdersViewModel vm) return;

        if (e.Key == Key.Enter && vm.SelectedRow is not null)
        {
            e.Handled = true;
            vm.OpenDetailCommand.Execute(vm.SelectedRow);
            return;
        }

        if (e.Key == Key.Escape && vm.HasAnySelected)
        {
            e.Handled = true;
            vm.ClearSelectionCommand.Execute(null);
            return;
        }

        if (e.Key == Key.Delete)
        {
            e.Handled = true;
            await vm.HandleDeleteKeyAsync();
        }
    }

    private void OrdersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ReturnOrdersViewModel vm) return;
        var item = DataGridRowHelper.GetDoubleClickedItem<ReturnOrderRowViewModel>(e);
        if (item is not null)
        {
            vm.OpenDetailCommand.Execute(item);
        }
    }

    private void RowActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (OrdersGrid.Resources["RowActionContextMenu"] is not ContextMenu menu) return;

        menu.DataContext = btn.DataContext;
        menu.PlacementTarget = btn;
        menu.IsOpen = true;
    }

    private void ExportButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button b && b.ContextMenu is { } menu)
        {
            menu.PlacementTarget = b;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }
}
