using System.Windows;

namespace QuanLyHangHoan.App.Views;

/// <summary>
/// Cửa sổ phụ không chặn (chi tiết đơn, xem ảnh): khi đóng thì trả quyền điều khiển về cửa sổ cha của phần mềm.
/// Chủ dự án 27/11/2026: mở chi tiết đơn -> xem ảnh -> đóng ảnh -> đóng chi tiết thì Windows nhảy sang phần mềm mở trước đó,
/// vì Windows tự chọn cửa sổ kế tiếp theo thứ tự trên màn hình (có thể là phần mềm khác).
/// </summary>
public static class OwnerFocus
{
    public static void ReturnToOwnerOnClose(Window window)
    {
        window.Closing += (_, e) =>
        {
            if (e.Cancel) return;
            var owner = window.Owner ?? Application.Current?.MainWindow;
            if (owner is null || ReferenceEquals(owner, window) || !owner.IsVisible) return;
            // chỉ kéo về khi phần mềm đang được dùng (cửa sổ này đang hoạt động, hoặc không cửa sổ nào khác của phần mềm đang hoạt động)
            var otherActive = Application.Current?.Windows.OfType<Window>().Any(w => !ReferenceEquals(w, window) && w.IsActive) == true;
            if (window.IsActive || !otherActive)
            {
                if (owner.WindowState == WindowState.Minimized) owner.WindowState = WindowState.Normal;
                owner.Activate();
            }
        };
    }
}
