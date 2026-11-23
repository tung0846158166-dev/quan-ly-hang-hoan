using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

public interface IOrderActionDialogService
{
    string? PromptStatus(string orderId, string tracking, string currentStatus);

    string? PromptMultilineText(string title, string subtitle, string fieldLabel, string initialValue);

    (bool IsWatchlist, string Reason)? PromptWatchlist(string orderId, string tracking, bool currentWatchlist, string currentReason);

    bool ConfirmDeleteSingle(string tracking, string orderId, string fromTable);

    bool ConfirmBulkAction(string title, int count, string detail);

    void ShowHistory(string orderId, string tracking, IReadOnlyList<HistoryEntry> entries);
}
