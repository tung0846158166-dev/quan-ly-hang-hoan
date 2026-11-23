using System.Windows;
using System.Windows.Input;

namespace QuanLyHangHoan.App.Views;

public partial class PhotoViewerWindow : Window
{
    public PhotoViewerWindow()
    {
        InitializeComponent();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
