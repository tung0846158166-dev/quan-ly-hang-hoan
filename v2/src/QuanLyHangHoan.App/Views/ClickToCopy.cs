using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace QuanLyHangHoan.App.Views;

/// <summary>
/// Bấm một lần vào ô "Mã đơn hàng" / "Mã vận đơn" trong bảng là tự sao chép mã đó vào bộ nhớ tạm và báo "Đã sao chép".
/// Không sao chép khi đang giữ Ctrl/Shift (đang chọn nhiều dòng) để không làm phiền thao tác chọn.
/// </summary>
public static class ClickToCopy
{
    /// <summary>Hiện thông báo (MainWindow gắn vào thanh thông báo của app).</summary>
    public static Action<string>? Notify { get; set; }

    public static void Attach(DataGrid grid) => grid.PreviewMouseLeftButtonUp += OnGridClick;

    private static void OnGridClick(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.OriginalSource is not DependencyObject src) return;
        var cell = FindParent<DataGridCell>(src);
        if (cell is null || !IsCodeColumn(cell.Column?.Header?.ToString())) return;
        var text = ExtractCode(FindText(cell));
        if (text.Length == 0) return;
        CopyText(text);
    }

    /// <summary>Cột chứa mã cần sao chép: bắt đầu bằng "Mã đơn" hoặc "Mã vận đơn".</summary>
    public static bool IsCodeColumn(string? header)
        => !string.IsNullOrWhiteSpace(header)
           && (header.StartsWith("Mã đơn", StringComparison.OrdinalIgnoreCase)
               || header.StartsWith("Mã vận đơn", StringComparison.OrdinalIgnoreCase));

    /// <summary>Lấy mã từ chữ trong ô: "SPX123  (Giao: SPX456)" → "SPX123" (mã hoàn, đứng đầu).</summary>
    public static string ExtractCode(string? cellText)
    {
        if (string.IsNullOrWhiteSpace(cellText)) return string.Empty;
        var first = cellText.Trim().Split([' ', '\t', '\r', '\n', '('], StringSplitOptions.RemoveEmptyEntries);
        return first.Length == 0 ? string.Empty : first[0].Trim();
    }

    public static void CopyText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                Notify?.Invoke($"📋 Đã sao chép: {text}");
                return;
            }
            catch (System.Runtime.InteropServices.COMException) { System.Threading.Thread.Sleep(30); }   // bộ nhớ tạm đang bị app khác giữ
            catch (System.Runtime.InteropServices.ExternalException) { System.Threading.Thread.Sleep(30); }
        }
        Notify?.Invoke("Không sao chép được (bộ nhớ tạm đang bận), thử lại.");
    }

    private static string? FindText(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text)) return tb.Text;
            var n = FindText(c);
            if (n is not null) return n;
        }
        return null;
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var p = child;
        while (p is not null)
        {
            if (p is T t) return t;
            p = p is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p);
        }
        return null;
    }
}