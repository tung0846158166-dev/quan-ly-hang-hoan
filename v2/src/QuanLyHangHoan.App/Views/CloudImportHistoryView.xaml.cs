using System.Windows.Controls;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.App.Views;

/// <summary>Chế độ cloud: lịch sử đồng bộ Google Sheet (tải lại mỗi lần mở màn).</summary>
public partial class CloudImportHistoryView : UserControl
{
    public CloudImportHistoryView()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (DataContext is CloudImportHistoryViewModel vm) _ = vm.RefreshAsync(); };
    }
}
