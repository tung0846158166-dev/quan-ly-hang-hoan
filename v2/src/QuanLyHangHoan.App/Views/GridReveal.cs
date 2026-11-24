using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace QuanLyHangHoan.App.Views;

/// <summary>Cuộn bảng tới một dòng, chọn đúng dòng đó, đưa tiêu điểm vào bảng và nháy vàng dòng đó vài giây để người dùng thấy ngay (Trang chủ "Cần xử lý ngay", tìm kiếm tổng).</summary>
public static class GridReveal
{
    public static void Perform(DataGrid grid, object row)
    {
        grid.UnselectAll();
        grid.SelectedItem = row;
        grid.UpdateLayout();
        grid.ScrollIntoView(row);
        grid.UpdateLayout();
        if (grid.Columns.Count > 0) grid.CurrentCell = new DataGridCellInfo(row, grid.Columns[0]);
        grid.Focus();
        // Dòng chỉ có container sau khi bảng dựng xong; thử lại một nhịp nếu chưa có.
        if (!Flash(grid, row))
            grid.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Flash(grid, row)));
    }

    private static bool Flash(DataGrid grid, object row)
    {
        if (grid.ItemContainerGenerator.ContainerFromItem(row) is not DataGridRow container) return false;
        var brush = new SolidColorBrush(Color.FromRgb(0xFA, 0xCC, 0x15)); // vàng đậm
        container.Background = brush;
        var anim = new ColorAnimation(Color.FromRgb(0xFA, 0xCC, 0x15), Color.FromArgb(0, 0xFA, 0xCC, 0x15), new Duration(TimeSpan.FromSeconds(3.5)))
        {
            BeginTime = TimeSpan.FromSeconds(1.2) // giữ vàng đậm 1,2 giây rồi mờ dần
        };
        anim.Completed += (_, _) => container.ClearValue(Control.BackgroundProperty);
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        container.BringIntoView();
        return true;
    }
}
