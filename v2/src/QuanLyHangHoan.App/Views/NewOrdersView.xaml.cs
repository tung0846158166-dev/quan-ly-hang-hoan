using System.Windows.Controls;
using System.Windows.Input;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.App.Views;

public partial class NewOrdersView : UserControl
{
    public NewOrdersView()
    {
        InitializeComponent();
    }

    private void NewOrdersGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is NewOrdersViewModel vm && vm.SelectedRow is not null)
        {
            e.Handled = true;
            vm.OpenDetailCommand.Execute(vm.SelectedRow);
        }
    }

    private void NewOrdersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not NewOrdersViewModel vm) return;
        var item = DataGridRowHelper.GetDoubleClickedItem<NewOrderRowViewModel>(e);
        if (item is not null)
        {
            vm.OpenDetailCommand.Execute(item);
        }
    }
}
