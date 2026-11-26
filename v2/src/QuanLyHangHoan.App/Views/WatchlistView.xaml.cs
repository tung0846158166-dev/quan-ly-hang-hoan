using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.App.Views;

public partial class WatchlistView : UserControl
{
    public WatchlistView()
    {
        InitializeComponent();
        ClickToCopy.Attach(WatchlistGrid);
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is WatchlistViewModel o) o.ClearSelectionRequested -= OnClearSelectionRequested;
            if (e.NewValue is WatchlistViewModel n) n.ClearSelectionRequested += OnClearSelectionRequested;
        };
    }

    private void OnClearSelectionRequested() => WatchlistGrid.UnselectAll();

    private void WatchlistGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is WatchlistViewModel vm) vm.UpdateSelectedRows(WatchlistGrid.SelectedItems.OfType<WatchlistRowViewModel>());
    }

    private void WatchlistGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not WatchlistViewModel vm) return;

        if (e.Key == Key.Enter && vm.SelectedRow is not null)
        {
            e.Handled = true;
            vm.OpenDetailCommand.Execute(vm.SelectedRow);
        }
        else if (e.Key == Key.Delete && vm.SelectedRows.Count > 0)
        {
            e.Handled = true;
            if (vm.HandleDeleteKeyCommand.CanExecute(null)) vm.HandleDeleteKeyCommand.Execute(null);
        }
        else if (e.Key == Key.Escape && vm.SelectedRows.Count > 1)
        {
            e.Handled = true;
            vm.ClearSelectionCommand.Execute(null);
        }
    }

    private void WatchlistGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not WatchlistViewModel vm) return;
        var item = DataGridRowHelper.GetDoubleClickedItem<WatchlistRowViewModel>(e);
        if (item is not null)
        {
            vm.OpenDetailCommand.Execute(item);
        }
    }

    private void RowActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (WatchlistGrid.Resources["WatchlistRowMenu"] is not ContextMenu menu) return;

        menu.DataContext = btn.DataContext;
        menu.PlacementTarget = btn;
        menu.IsOpen = true;
    }
}
