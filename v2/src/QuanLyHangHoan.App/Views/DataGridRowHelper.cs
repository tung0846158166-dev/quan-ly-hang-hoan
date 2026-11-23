using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace QuanLyHangHoan.App.Views;

internal static class DataGridRowHelper
{
    public static T? GetDoubleClickedItem<T>(MouseButtonEventArgs e) where T : class
    {
        var current = e.OriginalSource as DependencyObject;
        while (current is not null)
        {
            if (current is Button)
            {
                return null;
            }
            if (current is DataGridRow row)
            {
                return row.Item as T;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
