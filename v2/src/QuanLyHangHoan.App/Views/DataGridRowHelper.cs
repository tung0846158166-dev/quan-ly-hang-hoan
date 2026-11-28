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
            // 28/11/2026: nhấp đúp trúng chữ dạng Run (không phải Visual) làm app tắt -> đi theo cây logic cho phần tử không phải Visual
            current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }
}
