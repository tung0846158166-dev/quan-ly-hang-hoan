using System.Windows;
using System.Windows.Input;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Views;

public sealed class HistoryRowItem
{
    public HistoryRowItem(HistoryEntry entry)
    {
        Time = entry.Time;
        Label = string.IsNullOrWhiteSpace(entry.Label) ? entry.Field : entry.Label;
        var oldText = string.IsNullOrEmpty(entry.OldValue) ? "(trống)" : entry.OldValue;
        var newText = string.IsNullOrEmpty(entry.NewValue) ? "(trống)" : entry.NewValue;
        ChangeDisplay = $"{oldText} → {newText}";
        EditType = entry.EditType;
    }

    public string Time { get; }
    public string Label { get; }
    public string ChangeDisplay { get; }
    public string EditType { get; }
}

public partial class OrderHistoryWindow : Window
{
    public OrderHistoryWindow(string orderId, string tracking, IReadOnlyList<HistoryEntry> entries)
    {
        InitializeComponent();
        OrderSubtitleText.Text = $"Mã đơn: {orderId}  •  MVĐ: {tracking}  •  {entries.Count:N0} mục (mới nhất trên cùng)";

        if (entries.Count == 0)
        {
            EmptyStatePanel.Visibility = Visibility.Visible;
        }
        else
        {
            HistoryGrid.ItemsSource = entries.Select(e => new HistoryRowItem(e)).ToList();
        }
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
