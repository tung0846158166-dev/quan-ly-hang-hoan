using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Input;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.App.Views;

public partial class AiChatView : UserControl
{
    public AiChatView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is AiChatViewModel old)
            {
                old.Messages.CollectionChanged -= OnMessages;
                old.PropertyChanged -= OnVmChanged;
            }
            if (e.NewValue is AiChatViewModel vm)
            {
                vm.Messages.CollectionChanged += OnMessages;
                vm.PropertyChanged += OnVmChanged;
            }
        };
    }

    private void OnMessages(object? s, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => Scroller.ScrollToEnd()), System.Windows.Threading.DispatcherPriority.Background);

    // Mở cửa sổ chat thì đưa con trỏ vào ô nhập để gõ ngay.
    private void OnVmChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiChatViewModel.IsPanelOpen) && s is AiChatViewModel { IsPanelOpen: true })
            Dispatcher.BeginInvoke(new Action(() => InputBox.Focus()), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is AiChatViewModel vm && vm.SendCommand.CanExecute(null))
        {
            vm.SendCommand.Execute(null);
            e.Handled = true;
        }
    }
}
