using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace QuanLyHangHoan.App.Views;

public partial class ConfirmActionWindow : Window
{
    public ConfirmActionWindow(string title, string message, string confirmLabel = "Xác nhận", bool isDanger = true)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmLabel;
        ConfirmButton.Background = new SolidColorBrush(
            isDanger ? Color.FromRgb(0xDC, 0x26, 0x26) : Color.FromRgb(0x25, 0x63, 0xEB));
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }
}
