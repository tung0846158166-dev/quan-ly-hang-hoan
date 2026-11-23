using System.Windows;
using System.Windows.Input;

namespace QuanLyHangHoan.App.Views;

public partial class WatchlistEditWindow : Window
{
    public WatchlistEditWindow(string orderId, string tracking, bool currentWatchlist, string currentReason)
    {
        InitializeComponent();
        OrderInfoText.Text = $"Mã đơn: {orderId}  •  MVĐ: {tracking}";
        WatchlistCheck.IsChecked = currentWatchlist;
        ReasonInput.Text = currentReason ?? string.Empty;
    }

    public bool IsWatchlist => WatchlistCheck.IsChecked == true;
    public string WatchlistReason => ReasonInput.Text ?? string.Empty;

    private void SaveButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

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
