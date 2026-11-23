using System.Windows;
using QuanLyHangHoan.App.Views;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

public sealed class WpfOrderActionDialogService : IOrderActionDialogService
{
    private static Window? GetOwner() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.MainWindow;

    public string? PromptStatus(string orderId, string tracking, string currentStatus)
    {
        var dlg = new ChangeStatusWindow(orderId, tracking, currentStatus) { Owner = GetOwner() };
        return dlg.ShowDialog() == true ? dlg.SelectedStatus : null;
    }

    public string? PromptMultilineText(string title, string subtitle, string fieldLabel, string initialValue)
    {
        var dlg = new EditNoteWindow(title, subtitle, fieldLabel, initialValue) { Owner = GetOwner() };
        return dlg.ShowDialog() == true ? dlg.EditedText : null;
    }

    public (bool IsWatchlist, string Reason)? PromptWatchlist(string orderId, string tracking, bool currentWatchlist, string currentReason)
    {
        var dlg = new WatchlistEditWindow(orderId, tracking, currentWatchlist, currentReason) { Owner = GetOwner() };
        return dlg.ShowDialog() == true ? (dlg.IsWatchlist, dlg.WatchlistReason) : null;
    }

    public bool ConfirmDeleteSingle(string tracking, string orderId, string fromTable)
    {
        var msg = $"Bạn có chắc muốn xóa đơn này khỏi \"{fromTable}\"?\n\n" +
                  $"• Mã vận đơn: {tracking}\n" +
                  $"• Mã đơn: {orderId}\n\n" +
                  "Lưu ý: Đơn vẫn khôi phục được ở màn Đã xóa.";
        var dlg = new ConfirmActionWindow("Xác nhận xóa đơn", msg, "Xóa đơn", isDanger: true) { Owner = GetOwner() };
        return dlg.ShowDialog() == true;
    }

    public bool ConfirmBulkAction(string title, int count, string detail)
    {
        var msg = $"Bạn sắp thực hiện \"{title}\" cho {count:N0} đơn đang chọn.\n\n{detail}";
        var dlg = new ConfirmActionWindow(title, msg, "Xác nhận", isDanger: title.Contains("Xóa", StringComparison.OrdinalIgnoreCase))
        {
            Owner = GetOwner()
        };
        return dlg.ShowDialog() == true;
    }

    public void ShowHistory(string orderId, string tracking, IReadOnlyList<HistoryEntry> entries)
    {
        var dlg = new OrderHistoryWindow(orderId, tracking, entries) { Owner = GetOwner() };
        dlg.ShowDialog();
    }
}
