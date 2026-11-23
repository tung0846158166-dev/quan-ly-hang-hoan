using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.App.Views;

public partial class ChangeStatusWindow : Window
{
    private static readonly string[] AllStatuses =
    [
        ReceiveStatus.Received,
        ReceiveStatus.NotReturned,
        ReceiveStatus.NotNeeded,
        ReceiveStatus.Lost,
        ReceiveStatus.NotYetArrived
    ];

    private readonly List<RadioButton> _radios = new();

    public ChangeStatusWindow(string orderId, string tracking, string currentStatus)
    {
        InitializeComponent();
        OrderInfoText.Text = $"Mã đơn: {orderId}  •  MVĐ: {tracking}";
        CurrentStatusText.Text = $"Trạng thái hiện tại: {(string.IsNullOrWhiteSpace(currentStatus) ? "(trống)" : currentStatus)}";
        SelectedStatus = currentStatus;

        foreach (var status in AllStatuses)
        {
            var rb = new RadioButton
            {
                Content = status,
                Tag = status,
                GroupName = "StatusChoice",
                IsChecked = string.Equals(status, currentStatus, StringComparison.Ordinal),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand
            };
            rb.SetResourceReference(ForegroundProperty, "InkPrimaryBrush");
            _radios.Add(rb);
            StatusOptionsPanel.Children.Add(rb);
        }
    }

    public string SelectedStatus { get; private set; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var chosen = _radios.FirstOrDefault(r => r.IsChecked == true)?.Tag as string;
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            SelectedStatus = chosen;
        }
        DialogResult = true;
    }

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
