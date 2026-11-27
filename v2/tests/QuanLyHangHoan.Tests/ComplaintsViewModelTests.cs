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
        public Task<EditResult> SetComplaintAsync(string orderId, string status, string reason, string? expectedOld = null, CancellationToken ct = default)
        { Calls.Add($"{orderId}:ComplaintStatus:{status}|{reason}"); return Task.FromResult(new EditResult(EditKind.Saved, "ok")); }
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
        Assert.True(await vm.SetStatusAsync(row, "Thành công"));
        await vm.EditNoteAsync(row);
        await vm.RemoveComplaintAsync(row);
        Assert.Equal(new[] { "a:ComplaintStatus:Thành công|đã gửi Shopee", "a:ComplaintNote:đã gửi Shopee", "a:ComplaintStatus:" }, edit.Calls);
    }

    /// <summary>Chủ dự án 27/11/2026: đổi trạng thái khiếu nại phải ghi lý do; bỏ trống / bấm Hủy thì KHÔNG đổi.</summary>
    [Fact]
    public async Task Changing_status_requires_a_reason()
    {
        var edit = new RecordingEdit();
        var dialogs = new Dialogs { Text = "   " };
        var notes = new List<string>();
        var vm = new ComplaintsViewModel(edit, dialogs, () => Task.CompletedTask, (m, _) => notes.Add(m));
        vm.LoadOrders(new[] { O("a", "Đang xử lý") }, Array.Empty<Order>(), Now);
        Assert.False(await vm.SetStatusAsync(vm.VisibleRows[0], "Thất bại"));
        dialogs.Text = null;
        Assert.False(await vm.SetStatusAsync(vm.VisibleRows[0], "Thất bại"));
        Assert.Empty(edit.Calls);
        Assert.Contains(notes, n => n.Contains("lý do"));
    }

    [Fact]
    public void Complaint_note_log_shows_on_one_line_in_the_table()
    {
        var o = O("a", "Đang xử lý");
        o.ComplaintNote = "27/11/2026 09:00 · Đang xử lý: thiếu áo\n27/11/2026 10:00 · Thất bại: sàn từ chối";
        var row = new ComplaintRowViewModel(o, 1, false);
        Assert.DoesNotContain("\n", row.ComplaintNoteOneLine);
        Assert.Equal("27/11/2026 10:00 · Thất bại: sàn từ chối", row.LastReason);
    }

    [Fact]
    public async Task Complain_button_in_order_tables_starts_a_complaint_for_the_selected_rows()
    {
        var edit = new RecordingEdit();
        var vm = new ReturnOrdersViewModel(new FakeDataService(), editService: edit, dialogService: new Dialogs(), onDataChangedAsync: () => Task.CompletedTask, loadInitialData: false);
        vm.LoadOrders(new[] { O("a"), O("b", "Đang xử lý"), O("c") }, Now);
        vm.UpdateSelectedRows(vm.VisibleOrders);
        await vm.BulkComplainAsync();
        // đơn đã khiếu nại thì không đặt lại (không mất trạng thái Thành công/Thất bại); lý do nhập một lần cho các đơn đã chọn
        Assert.Equal(new[] { "a:ComplaintStatus:Đang xử lý|đã gửi Shopee", "c:ComplaintStatus:Đang xử lý|đã gửi Shopee" }, edit.Calls.OrderBy(x => x));
        var r = vm.VisibleOrders.First(x => x.Source.Id == "b");
        Assert.Contains("⚖️", r.WatchMark + r.ComplaintMark);
    }

    /// <summary>Chủ dự án 27/11/2026: bấm Khiếu nại ở bảng đơn phải nhập lý do; không nhập thì không khiếu nại.</summary>
    [Fact]
    public async Task Complain_button_requires_a_reason()
    {
        var edit = new RecordingEdit();
        var vm = new ReturnOrdersViewModel(new FakeDataService(), editService: edit, dialogService: new Dialogs { Text = "" }, onDataChangedAsync: () => Task.CompletedTask, loadInitialData: false);
        vm.LoadOrders(new[] { O("a") }, Now);
        await vm.ComplainRowAsync(vm.VisibleOrders[0]);
        Assert.Empty(edit.Calls);
    }
}

/// <summary>Trang chủ: 3 khiếu nại gần nhất (chủ dự án 27/11/2026). Dữ liệu GIẢ.</summary>
public sealed class HomeRecentComplaintsTests
{
    private static QuanLyHangHoan.Core.Models.Order O(string id, string st, string at) => new()
    {
        Id = id, OrderId = "O-" + id, Track = "T-" + id, Platform = "Shopee", Status = "Chưa trả", ComplaintStatus = st, ComplaintAt = at,
    };

    [Fact]
    public void Home_shows_the_three_newest_complaints_and_opens_detail_or_table()
    {
        var complaints = new QuanLyHangHoan.App.ViewModels.ComplaintsViewModel();
        var home = new QuanLyHangHoan.App.ViewModels.HomeViewModel(new QuanLyHangHoan.App.Services.FakeDataService());
        complaints.CountsUpdated += () => home.SetRecentComplaints(complaints.Recent(3), complaints.ProcessingCount, complaints.TotalCount);
        complaints.LoadOrders(new[] { O("a", "Đang xử lý", "20/11/2026 10:00:00"), O("b", "Thất bại", "25/11/2026 10:00:00"), O("c", "", ""),
            O("d", "Thành công", "26/11/2026 10:00:00") }, new[] { O("e", "Đang xử lý", "27/11/2026 08:00:00") }, new DateTime(2026, 11, 27));
        Assert.Equal(new[] { "e", "d", "b" }, home.RecentComplaints.Select(r => r.Source.Id));
        Assert.Equal("2 đang xử lý / 4 đơn", home.ComplaintSummaryText);
        string? opened = null; var table = false;
        home.OpenOrderDetail = o => opened = o.Id;
        home.OpenComplaintsTable = () => table = true;
        home.OpenComplaintCommand.Execute(home.RecentComplaints[1]);
        home.OpenComplaintsCommand.Execute(null);
        Assert.Equal("d", opened);
        Assert.True(table);
    }
}
