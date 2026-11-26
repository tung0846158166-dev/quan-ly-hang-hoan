using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.Tests;

/// <summary>Bảng Khiếu nại + nút Khiếu nại ở bảng đơn (chủ dự án 26/11/2026). Dữ liệu GIẢ.</summary>
public sealed class ComplaintsViewModelTests
{
    private sealed class RecordingEdit : IOrderEditService
    {
        public readonly List<string> Calls = new();
        public Task<EditResult> SetFieldAsync(string orderId, EditField field, string newValue, string? expectedOld = null, CancellationToken ct = default)
        { Calls.Add($"{orderId}:{field}:{newValue}"); return Task.FromResult(new EditResult(EditKind.Saved, "ok")); }
        public Task<EditResult> DeleteAsync(string orderId, string from, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<EditResult> RestoreAsync(string orderId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<EditResult> ArchiveAsync(string orderId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<EditResult> UnarchiveAsync(string orderId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(string orderId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Dialogs : IOrderActionDialogService
    {
        public string? Text = "đã gửi Shopee";
        public string? PromptStatus(string orderId, string tracking, string currentStatus) => null;
        public string? PromptMultilineText(string title, string subtitle, string label, string initialValue) => Text;
        public (bool IsWatchlist, string Reason)? PromptWatchlist(string orderId, string tracking, bool isWatchlist, string currentReason) => null;
        public bool ConfirmDeleteSingle(string tracking, string orderId, string sourceTable) => true;
        public bool ConfirmBulkAction(string title, int count, string detail) => true;
        public void ShowHistory(string orderId, string tracking, IReadOnlyList<HistoryEntry> entries) { }
    }

    private static Order O(string id, string complaint = "") => new()
    {
        Id = id, OrderId = "O-" + id, Track = "T-" + id, Platform = "Shopee", OrderType = "Trả hàng/Hoàn tiền", Status = "Chưa trả",
        ComplaintStatus = complaint, ComplaintAt = complaint.Length > 0 ? "26/11/2026 10:00:00" : "", Items = { new OrderItem { Product = "Áo", Qty = 1 } },
    };

    private static readonly DateTime Now = new(2026, 11, 26, 12, 0, 0);

    [Fact]
    public void Only_complained_orders_appear_and_the_default_filter_is_in_progress()
    {
        var vm = new ComplaintsViewModel();
        vm.LoadOrders(new[] { O("a", "Đang xử lý"), O("b", "Thành công"), O("c"), O("d", "Thất bại") }, new[] { O("e", "Đang xử lý") }, Now);
        Assert.Equal(2, vm.ProcessingCount);
        Assert.Equal(1, vm.SuccessCount);
        Assert.Equal(1, vm.FailedCount);
        Assert.Equal(new[] { "a", "e" }, vm.VisibleRows.Select(r => r.Source.Id).OrderBy(x => x));
        vm.StatusFilter = "all";
        Assert.Equal(4, vm.VisibleRows.Count);
        vm.StatusFilter = "failed";
        Assert.Equal("d", Assert.Single(vm.VisibleRows).Source.Id);
        vm.StatusFilter = "all";
        vm.SearchText = "o-b";
        Assert.Equal("b", Assert.Single(vm.VisibleRows).Source.Id);
    }

    [Fact]
    public async Task Status_can_be_changed_note_edited_and_complaint_removed()
    {
        var edit = new RecordingEdit();
        var vm = new ComplaintsViewModel(edit, new Dialogs(), () => Task.CompletedTask);
        vm.LoadOrders(new[] { O("a", "Đang xử lý") }, Array.Empty<Order>(), Now);
        var row = vm.VisibleRows[0];
        await vm.SetStatusAsync(row, "Thành công");
        await vm.EditNoteAsync(row);
        await vm.RemoveComplaintAsync(row);
        Assert.Equal(new[] { "a:ComplaintStatus:Thành công", "a:ComplaintNote:đã gửi Shopee", "a:ComplaintStatus:" }, edit.Calls);
    }

    [Fact]
    public async Task Complain_button_in_order_tables_starts_a_complaint_for_the_selected_rows()
    {
        var edit = new RecordingEdit();
        var vm = new ReturnOrdersViewModel(new FakeDataService(), editService: edit, dialogService: new Dialogs(), onDataChangedAsync: () => Task.CompletedTask, loadInitialData: false);
        vm.LoadOrders(new[] { O("a"), O("b", "Đang xử lý"), O("c") }, Now);
        vm.UpdateSelectedRows(vm.VisibleOrders);
        await vm.BulkComplainAsync();
        // đơn đã khiếu nại thì không đặt lại (không mất trạng thái Thành công/Thất bại)
        Assert.Equal(new[] { "a:ComplaintStatus:Đang xử lý", "c:ComplaintStatus:Đang xử lý" }, edit.Calls.OrderBy(x => x));
        var r = vm.VisibleOrders.First(x => x.Source.Id == "b");
        Assert.Contains("⚖️", r.WatchMark + r.ComplaintMark);
    }
}
