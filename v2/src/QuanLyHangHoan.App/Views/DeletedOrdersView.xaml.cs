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
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is DeletedOrdersViewModel o) o.RevealRequested -= OnRevealRequested;
            if (e.NewValue is DeletedOrdersViewModel n)
            {
                n.RevealRequested += OnRevealRequested;
                if (n.PendingReveal is not null) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(TryReveal));
            }
        };
        Loaded += (_, _) => TryReveal();
    }

    private void OnRevealRequested(DeletedOrderRowViewModel row) =>
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(TryReveal));

    private void TryReveal()
    {
        if (DataContext is not DeletedOrdersViewModel vm || vm.PendingReveal is not { } row || !DeletedGrid.IsLoaded) return;
        vm.PendingReveal = null;
        if (!vm.VisibleOrders.Contains(row)) return;
        GridReveal.Perform(DeletedGrid, row);
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
