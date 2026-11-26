using System.Windows.Controls;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.App.Views;

public partial class CloudSettingsView : UserControl
{
    public CloudSettingsView()
    {
        InitializeComponent();
        // chủ cửa hàng mở màn Cloud: tự tải danh sách thiết bị đã ghép
        Loaded += (_, _) => { if (DataContext is CloudStatusViewModel { IsOwner: true } vm) _ = vm.RefreshDevicesAsync(); };
    }
}
