using System.Windows;
using System.Windows.Controls;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    /// <summary>Bấm một dòng ở "Cần xử lý ngay": tự sao chép mã đơn hàng (đồng thời mở bảng đúng sàn như trước).</summary>
    private void UrgentRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is UrgentOrderItem item) ClickToCopy.CopyText(item.OrderId);
    }
}
