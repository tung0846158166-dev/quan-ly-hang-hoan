using System.Windows;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Sao chép chữ vào bộ nhớ tạm trên MỘT LUỒNG RIÊNG. Clipboard.SetText chờ (Flush, thử lại nhiều lần) khi chương trình khác đang giữ
/// bộ nhớ tạm, có thể treo luồng giao diện tới ~3 giây mỗi lần bấm; chạy ngoài luồng giao diện thì giao diện không bao giờ phải chờ.
/// </summary>
public static class ClipboardHelper
{
    /// <summary>Chạy nền, trả về ngay. <paramref name="done"/> (nếu có) được gọi trên luồng giao diện khi xong (true = đã chép được).</summary>
    public static void SetTextInBackground(string text, Action<bool>? done = null)
    {
        if (string.IsNullOrEmpty(text)) { done?.Invoke(false); return; }
        var ui = Application.Current?.Dispatcher;
        var t = new Thread(() =>
        {
            var ok = false;
            for (var attempt = 0; attempt < 3 && !ok; attempt++)
            {
                try { Clipboard.SetText(text); ok = true; }
                catch (System.Runtime.InteropServices.ExternalException) { Thread.Sleep(50); }
            }
            if (done is null) return;
            if (ui is null) done(ok); else ui.BeginInvoke(done, ok);
        }) { IsBackground = true, Name = "ClipboardCopy" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }
}
