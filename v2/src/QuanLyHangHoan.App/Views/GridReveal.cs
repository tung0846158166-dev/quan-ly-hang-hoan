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
        // 28/11/2026: trang vừa hiện ra (khung giữ sẵn trang) có thể chưa nhận tiêu điểm ngay -> giành lại sau khi dựng xong
        grid.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (!grid.IsKeyboardFocusWithin) { grid.Focus(); System.Windows.Input.Keyboard.Focus(grid); } }));
        // Dòng chỉ có container sau khi bảng dựng xong; thử lại một nhịp nếu chưa có.
        if (!Flash(grid, row))
            grid.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Flash(grid, row)));
    }

    private static bool Flash(DataGrid grid, object row)
    {
        if (grid.ItemContainerGenerator.ContainerFromItem(row) is not DataGridRow container) return false;
        // Các ô của dòng tự tô nền (màu "đang chọn") đè lên nền dòng, nên phải nháy ngay trên từng ô.
        var cells = new List<DataGridCell>();
        CollectCells(container, cells);
        if (cells.Count == 0) return false;

        var strong = Color.FromRgb(0xFA, 0xCC, 0x15);   // vàng đậm
        var soft = Color.FromRgb(0xFE, 0xF3, 0xC7);     // vàng nhạt
        var brush = new SolidColorBrush(strong);
        // Nháy vàng đậm ↔ nhạt 3 lần (khoảng 2,4 giây) rồi mờ dần về màu "đang chọn".
        var anim = new ColorAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(3.4) };
        double t = 0;
        anim.KeyFrames.Add(new DiscreteColorKeyFrame(strong, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
        for (var i = 0; i < 3; i++)
        {
            t += 0.4; anim.KeyFrames.Add(new LinearColorKeyFrame(soft, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
            t += 0.4; anim.KeyFrames.Add(new LinearColorKeyFrame(strong, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
        }
        anim.KeyFrames.Add(new LinearColorKeyFrame(Color.FromArgb(0, 0xFA, 0xCC, 0x15), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.4))));
        anim.Completed += (_, _) =>
        {
            foreach (var c in cells) c.ClearValue(Control.BackgroundProperty);
        };
        foreach (var c in cells) c.Background = brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        container.BringIntoView();
        return true;
    }

    private static void CollectCells(DependencyObject root, List<DataGridCell> cells)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is DataGridCell cell) cells.Add(cell);
            else CollectCells(child, cells);
        }
    }
}
