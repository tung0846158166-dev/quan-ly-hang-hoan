using System.Windows.Controls;
using System.Windows.Input;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.App.Views;

public partial class ReceivedStockView : UserControl
{
    public ReceivedStockView()
    {
        InitializeComponent();
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ReceivedStockViewModel vm) vm.OpenDetailCommand.Execute(vm.SelectedRow);
    }
}
