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
    }

    private void WatchlistGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not WatchlistViewModel vm) return;

        if (e.Key == Key.Enter && vm.SelectedRow is not null)
        {
            e.Handled = true;
            vm.OpenDetailCommand.Execute(vm.SelectedRow);
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
