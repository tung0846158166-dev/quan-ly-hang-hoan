using System.Windows;

namespace QuanLyHangHoan.App.Services;

public interface IClipboardService
{
    void SetText(string text);
}

public sealed class WindowsClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        ClipboardHelper.SetTextInBackground(text);   // ngoài luồng giao diện; bỏ qua nếu clipboard đang bị tiến trình khác khóa
    }
}
