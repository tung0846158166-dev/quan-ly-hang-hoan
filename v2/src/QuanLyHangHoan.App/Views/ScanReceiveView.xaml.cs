using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.App.Views;

public partial class ScanReceiveView : UserControl
{
    public ScanReceiveView()
    {
        InitializeComponent();
        Loaded += (_, _) => FocusScanBox();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) FocusScanBox();
        };
    }

    private void OnScanCodeBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Return && e.Key != Key.Enter) return;

        e.Handled = true;
        if (DataContext is ScanReceiveViewModel vm && vm.SubmitScanCommand.CanExecute(null))
        {
            vm.SubmitScanCommand.Execute(null);
        }
        FocusScanBox();
    }

    private void OnControlClickedRefocus(object sender, RoutedEventArgs e)
    {
        FocusScanBox();
    }

    private void FocusScanBox()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            ScanCodeBox.Focus();
            Keyboard.Focus(ScanCodeBox);
        });
    }
}
