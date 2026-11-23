using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.App.Views;

public partial class ArchivedOrdersView : UserControl
{
    public ArchivedOrdersView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ArchivedOrdersViewModel o) o.RevealRequested -= OnRevealRequested;
            if (e.NewValue is ArchivedOrdersViewModel n)
            {
                n.RevealRequested += OnRevealRequested;
                if (n.PendingReveal is not null) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(TryReveal));
            }
        };
        Loaded += (_, _) => TryReveal();
    }

    private void OnRevealRequested(ArchivedOrderRowViewModel row) =>
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(TryReveal));

    private void TryReveal()
    {
        if (DataContext is not ArchivedOrdersViewModel vm || vm.PendingReveal is not { } row || !ArchivedGrid.IsLoaded) return;
        vm.PendingReveal = null;
        if (!vm.VisibleOrders.Contains(row)) return;
        GridReveal.Perform(ArchivedGrid, row);
    }

    private async void ArchivedGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ArchivedOrdersViewModel vm) return;

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

    private void ArchivedGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ArchivedOrdersViewModel vm) return;
        var item = DataGridRowHelper.GetDoubleClickedItem<ArchivedOrderRowViewModel>(e);
        if (item is not null)
        {
            vm.OpenDetailCommand.Execute(item);
        }
    }

    private void RowActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (ArchivedGrid.Resources["ArchivedRowContextMenu"] is not ContextMenu menu) return;

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
