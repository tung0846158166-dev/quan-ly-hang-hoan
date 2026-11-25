using System.IO;
using System.Runtime.CompilerServices;

namespace QuanLyHangHoan.Tests;

/// <summary>Popup (chi tiết đơn, hộp thoại...) là cửa sổ phụ của cửa sổ chính: không được hiện thành mục riêng trên taskbar.</summary>
public sealed class PopupWindowTaskbarTests
{
    private static string ViewsDir([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "QuanLyHangHoan.App", "Views"));

    [Fact]
    public void Every_secondary_window_hides_its_taskbar_button()
    {
        var files = Directory.GetFiles(ViewsDir(), "*Window.xaml");
        Assert.NotEmpty(files);
        var missing = files
            .Where(f => File.ReadAllText(f).TrimStart().StartsWith("<Window", StringComparison.Ordinal))
            .Where(f => !File.ReadAllText(f).Contains("ShowInTaskbar=\"False\"", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        Assert.True(missing.Count == 0, "Thiếu ShowInTaskbar=\"False\": " + string.Join(", ", missing));
    }
}
