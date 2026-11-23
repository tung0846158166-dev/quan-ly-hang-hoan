using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.App.Views;

public partial class DeletedOrdersView : UserControl
{
    public DeletedOrdersView()
    {
        InitializeComponent();
    }

    private void DeletedGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is DeletedOrdersViewModel vm && vm.SelectedRow is not null)
        {
            e.Handled = true;
            vm.OpenDetailCommand.Execute(vm.SelectedRow);
        }
    }

    private void DeletedGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not DeletedOrdersViewModel vm) return;
        var item = DataGridRowHelper.GetDoubleClickedItem<DeletedOrderRowViewModel>(e);
        if (item is not null)
        {
            vm.OpenDetailCommand.Execute(item);
        }
    }

    private void RowActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (DeletedGrid.Resources["DeletedRowContextMenu"] is not ContextMenu menu) return;

        menu.DataContext = btn.DataContext;
        menu.PlacementTarget = btn;
        menu.IsOpen = true;
    }
}
