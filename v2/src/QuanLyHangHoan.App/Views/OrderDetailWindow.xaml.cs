using System.Windows;
using System.Windows.Input;

namespace QuanLyHangHoan.App.Views;

public partial class OrderDetailWindow : Window
{
    public OrderDetailWindow()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
