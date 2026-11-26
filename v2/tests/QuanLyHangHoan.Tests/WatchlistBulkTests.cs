using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.Tests;

/// <summary>Chủ dự án 26/11/2026: bảng "Đang theo dõi" chọn nhiều đơn -> bỏ theo dõi / xóa các đơn đã chọn. Dữ liệu GIẢ.</summary>
public sealed class WatchlistBulkTests
{
    private sealed class RecordingEdit : IOrderEditService
    {
        public readonly List<string> Calls = new();
        public Task<EditResult> SetFieldAsync(string orderId, EditField field, string newValue, string? expectedOld = null, CancellationToken ct = default)
        { Calls.Add($"set:{orderId}:{field}:{newValue}"); return Task.FromResult(new EditResult(EditKind.Saved, "ok")); }
        public Task<EditResult> DeleteAsync(string orderId, string from, CancellationToken ct = default)
        { Calls.Add($"del:{orderId}:{from}"); return Task.FromResult(new EditResult(EditKind.Saved, "ok")); }
        public Task<EditResult> RestoreAsync(string orderId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<EditResult> ArchiveAsync(string orderId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<EditResult> UnarchiveAsync(string orderId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(string orderId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Dialogs : IOrderActionDialogService
    {
        public int Confirms;
        public bool Answer = true;
        public string? PromptStatus(string orderId, string tracking, string currentStatus) => null;
        public string? PromptMultilineText(string title, string subtitle, string label, string initialValue) => null;
        public (bool IsWatchlist, string Reason)? PromptWatchlist(string orderId, string tracking, bool isWatchlist, string currentReason) => null;
        public bool ConfirmDeleteSingle(string tracking, string orderId, string sourceTable) => Answer;
        public bool ConfirmBulkAction(string title, int count, string detail) { Confirms++; return Answer; }
        public void ShowHistory(string orderId, string tracking, IReadOnlyList<HistoryEntry> entries) { }
    }

    private static Order W(string id) => new()
    {
        Id = id, OrderId = "O-" + id, Track = "T-" + id, Platform = "Shopee", OrderType = "Trả hàng/Hoàn tiền", Status = "Chưa trả",
        IsWatchlist = true, WatchlistReason = "kiểm", Items = { new OrderItem { Product = "Áo", Qty = 1 } },
    };

    private static (WatchlistViewModel Vm, RecordingEdit Edit, Dialogs Dlg) Make()
    {
        var edit = new RecordingEdit();
        var dlg = new Dialogs();
        var vm = new WatchlistViewModel(edit, dlg, () => Task.CompletedTask);
        var archived = W("c"); archived.Status = "Hàng đã trả";
        vm.IncludeArchived = true;
        vm.LoadOrders(new[] { W("a"), W("b") }, new[] { archived }, new DateTime(2026, 11, 26, 10, 0, 0));
        return (vm, edit, dlg);
    }

    [Fact]
    public async Task Selected_rows_can_be_removed_from_the_watchlist_in_one_go()
    {
        var (vm, edit, dlg) = Make();
        vm.UpdateSelectedRows(vm.VisibleOrders.Take(2));
        Assert.Equal(2, vm.SelectedCount);
        Assert.True(vm.HasMultipleSelected);
        await vm.BulkRemoveFromWatchlistAsync();
        Assert.Equal(1, dlg.Confirms);
        Assert.Equal(2, edit.Calls.Count(c => c.Contains(":IsWatchlist:0")));
    }

    [Fact]
    public async Task Selected_rows_can_be_deleted_with_their_own_source_table()
    {
        var (vm, edit, _) = Make();
        vm.UpdateSelectedRows(vm.VisibleOrders);
        await vm.BulkDeleteAsync();
        Assert.Equal(3, edit.Calls.Count(c => c.StartsWith("del:")));
        Assert.Contains("del:c:Kho lưu trữ", edit.Calls);
        Assert.Contains("del:a:Bảng chính", edit.Calls);
    }

    [Fact]
    public void Main_table_rows_show_a_star_with_the_watch_reason()
    {
        var w = new ReturnOrderRowViewModel(W("a"), 1, new DateTime(2026, 11, 26));
        Assert.Equal("⭐", w.WatchMark);
        Assert.Equal("⭐ Đang theo dõi: kiểm", w.WatchTooltip);
        var plain = W("b"); plain.IsWatchlist = false;
        var p = new ReturnOrderRowViewModel(plain, 2, new DateTime(2026, 11, 26));
        Assert.Equal("", p.WatchMark);
        Assert.Null(p.WatchTooltip);
    }

    [Fact]
    public async Task Nothing_happens_when_the_user_cancels()
    {
        var (vm, edit, dlg) = Make();
        dlg.Answer = false;
        vm.UpdateSelectedRows(vm.VisibleOrders);
        await vm.BulkDeleteAsync();
        await vm.BulkRemoveFromWatchlistAsync();
        Assert.Empty(edit.Calls);
    }
}
