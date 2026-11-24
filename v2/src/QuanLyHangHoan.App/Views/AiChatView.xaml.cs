using System.Collections.Specialized;
using System.Windows;
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
            if (e.OldValue is AiChatViewModel old) old.Messages.CollectionChanged -= OnMessages;
            if (e.NewValue is AiChatViewModel vm) vm.Messages.CollectionChanged += OnMessages;
        };
        Loaded += (_, _) => InputBox.Focus();
    }

    private void OnMessages(object? s, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => Scroller.ScrollToEnd()), System.Windows.Threading.DispatcherPriority.Background);

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is AiChatViewModel vm && vm.SendCommand.CanExecute(null))
        {
            vm.SendCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AiChatViewModel vm) return;
        var key = KeyBox.Password;
        var gKey = GeminiKeyBox.Password;
        GeminiKeyBox.Clear();
        KeyBox.Clear();
        await vm.SaveSettingsAsync(key, gKey);
    }

    private void ForgetKey_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AiChatViewModel vm) vm.ForgetKeys();
    }
}
