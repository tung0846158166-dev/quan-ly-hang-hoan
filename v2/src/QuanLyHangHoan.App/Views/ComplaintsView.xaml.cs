using System.Windows.Controls;
using System.Windows.Input;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.App.Views;

public partial class ComplaintsView : UserControl
{
    public ComplaintsView()
    {
        InitializeComponent();
        ClickToCopy.Attach(ComplaintsGrid);
    }

    /// <summary>Người dùng chọn trạng thái ở ô Khiếu nại (đóng hộp chọn): lưu ngay. Không lưu khi bảng tự dựng lại.</summary>
    private async void StatusCombo_DropDownClosed(object? sender, EventArgs e)
    {
        if (sender is not ComboBox cb || cb.DataContext is not ComplaintRowViewModel row || DataContext is not ComplaintsViewModel vm) return;
        if (cb.SelectedItem is not string status || status == row.ComplaintStatus) return;
        // phải ghi lý do (27/11/2026): Hủy / bỏ trống thì ô chọn quay về trạng thái cũ
        if (!await vm.SetStatusAsync(row, status)) cb.SelectedItem = row.ComplaintStatus;
    }

    private void ComplaintsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ComplaintsViewModel vm) return;
        var item = DataGridRowHelper.GetDoubleClickedItem<ComplaintRowViewModel>(e);
        if (item is not null) vm.OpenDetailCommand.Execute(item);
    }
}
