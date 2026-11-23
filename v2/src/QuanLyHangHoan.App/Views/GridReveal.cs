using System.Windows.Controls;

namespace QuanLyHangHoan.App.Views;

/// <summary>Cuộn bảng tới một dòng, chọn đúng dòng đó và đưa tiêu điểm vào bảng để dòng hiện rõ là đang được chọn (Trang chủ "Cần xử lý ngay", tìm kiếm tổng).</summary>
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
    }
}
