using System.IO;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Import;

namespace QuanLyHangHoan.Tests.UiTests;

/// <summary>Xuất Excel/CSV từ các bảng đơn. Thư mục TẠM, bản giả hộp thoại lưu; không ghi vào Documents/%LocalAppData% thật.</summary>
public sealed class T024ExportUiTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-t024-" + Guid.NewGuid().ToString("N"));
    public T024ExportUiTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    /// <summary>Hộp thoại lưu giả: trả đường dẫn đã định (null = người dùng hủy); ghi thật bằng cùng cách ghi tạm rồi đổi tên.</summary>
    private sealed class FakeSave : IFileSaveService
    {
        public string? Path;
        public bool FailWrite;
        public List<string> Suggested { get; } = new();
        public string? PickSavePath(string title, string defaultFileName, string filter) { Suggested.Add(defaultFileName); return Path; }
        public Task WriteTextAsync(string filePath, string content, CancellationToken cancellationToken = default) { File.WriteAllText(filePath, content); return Task.CompletedTask; }
        public async Task WriteBytesAsync(string filePath, byte[] content, CancellationToken cancellationToken = default)
        {
            if (FailWrite) throw new IOException("tệp đang được mở bởi chương trình khác");
            await new WindowsFileSaveService().WriteBytesAsync(filePath, content, cancellationToken);
        }
    }

    private static Order Make(string id, string track, params (string Name, int Qty, int Returned)[] items) => new()
    {
        Id = id, OrderId = "O-" + id, Track = track, Platform = "Shopee", OrderType = "Trả hàng", Status = ReceiveStatus.NotReturned,
        Items = items.Select(i => new OrderItem { Product = i.Name, Qty = i.Qty, ReturnedQty = i.Returned }).ToList(),
    };

    private static List<Order> Three() => new()
    {
        Make("1", "TRK1", ("Áo", 1, 1), ("Quần", 1, 0)),
        Make("2", "TRK2", ("Váy", 1, 0)),
        Make("3", "TRK3", ("Nón", 2, 0)),
    };

    // ---------------- dịch vụ xuất ----------------
    [Fact]
    public async Task Xlsx_and_csv_have_one_row_per_parcel_in_the_given_order_and_xlsx_round_trips()
    {
        var path = Path.Combine(_dir, "ra.xlsx");
        var save = new FakeSave { Path = path };
        var svc = new OrderExportService(save, () => Now);
        var r = await svc.ExportAsync(Three(), "Shopee-Hoan-hang", ExportFormat.Xlsx);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(3, r.Rows);
        var rows = SpreadsheetReader.Read("ra.xlsx", File.ReadAllBytes(path));
        Assert.Equal(new[] { "TRK1", "TRK2", "TRK3" }, rows.Select(x => x["Mã vận đơn"]).ToArray());
        Assert.Equal("Áo x1", rows[0]["Sản phẩm đã hoàn"]);            // hoàn một phần được đánh dấu

        var csvPath = Path.Combine(_dir, "ra.csv");
        save.Path = csvPath;
        var c = await svc.ExportAsync(Three(), "Shopee-Hoan-hang", ExportFormat.Csv);
        Assert.True(c.Ok);
        var lines = File.ReadAllText(csvPath).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1 + 3, lines.Length);
    }

    [Fact]
    public async Task Suggested_file_name_has_table_and_time_and_no_file_is_created_when_cancelled_or_empty()
    {
        var save = new FakeSave { Path = null };                       // người dùng hủy hộp thoại lưu
        var svc = new OrderExportService(save, () => Now);
        var cancelled = await svc.ExportAsync(Three(), "TikTok Hoàn hàng", ExportFormat.Xlsx);
        Assert.True(cancelled.Cancelled);
        Assert.False(cancelled.Ok);
        Assert.Equal("QuanLyHangHoan-TikTok-Hoàn-hàng-20261123-1200.xlsx", save.Suggested.Single());
        Assert.Empty(Directory.GetFiles(_dir));

        var empty = await svc.ExportAsync(new List<Order>(), "X", ExportFormat.Csv);
        Assert.False(empty.Ok);
        Assert.False(empty.Cancelled);
        Assert.Contains("Không có dòng nào", empty.Message);
        Assert.Single(save.Suggested);                                  // không hỏi nơi lưu khi không có dòng
    }

    [Fact]
    public async Task Write_error_is_reported_clearly_and_leaves_no_partial_file()
    {
        var path = Path.Combine(_dir, "loi.xlsx");
        var svc = new OrderExportService(new FakeSave { Path = path, FailWrite = true }, () => Now);
        var r = await svc.ExportAsync(Three(), "X", ExportFormat.Xlsx);
        Assert.False(r.Ok);
        Assert.False(r.Cancelled);
        Assert.Contains("Không ghi được tệp", r.Message);
        Assert.Empty(Directory.GetFiles(_dir));                         // không có tệp dở, kể cả .tmp
    }

    [Fact]
    public async Task Real_writer_replaces_the_target_atomically_and_cleans_the_temp_file()
    {
        var path = Path.Combine(_dir, "co-san.xlsx");
        File.WriteAllText(path, "cũ");
        await new WindowsFileSaveService().WriteBytesAsync(path, new byte[] { 1, 2, 3 });
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public async Task Export_never_touches_order_data_or_the_database()
    {
        var path = Path.Combine(_dir, "ra.xlsx");
        var orders = Three();
        var before = orders.Select(o => (o.Id, o.Track, o.Status, o.Items.Count)).ToList();
        await new OrderExportService(new FakeSave { Path = path }, () => Now).ExportAsync(orders, "X", ExportFormat.Xlsx);
        Assert.Equal(before, orders.Select(o => (o.Id, o.Track, o.Status, o.Items.Count)).ToList());
    }

    [Fact]
    public async Task Archived_export_marks_the_location_column()
    {
        var path = Path.Combine(_dir, "luu-tru.xlsx");
        await new OrderExportService(new FakeSave { Path = path }, () => Now).ExportAsync(Three(), "Kho-luu-tru", ExportFormat.Xlsx, archived: true);
        var rows = SpreadsheetReader.Read("x.xlsx", File.ReadAllBytes(path));
        Assert.All(rows, r => Assert.Equal("Kho lưu trữ", r["Vị trí"]));
    }

    // ---------------- view model ----------------
    private static ReturnOrdersViewModel NewVm(IOrderExportService export, FakeDataService data, List<(string, bool)>? notes = null) =>
        new(data, ReturnTableScope.All, onNotify: (m, err) => notes?.Add((m, err)), exportService: export);

    [Fact]
    public async Task View_model_exports_visible_rows_and_selected_rows_with_the_right_counts()
    {
        var data = new FakeDataService(enableMutableDemoStore: true);
        var export = new FakeOrderExportService();
        var notes = new List<(string, bool)>();
        var vm = NewVm(export, data, notes);
        vm.LoadOrders(data.GetReturnOrders(40), Now);
        var visible = vm.VisibleOrders.Count;
        Assert.True(visible > 3);

        await vm.ExportVisibleCommand.ExecuteAsync("csv");
        Assert.Equal((visible, ExportFormat.Csv), (export.Calls[0].Rows, export.Calls[0].Format));
        Assert.Contains("Đã xuất", vm.ExportStatus);
        Assert.False(vm.ExportStatusIsError);
        Assert.False(vm.IsExporting);

        vm.UpdateSelectedRows(vm.VisibleOrders.Take(2).ToList());
        Assert.True(vm.HasAnySelected);
        await vm.ExportSelectedCommand.ExecuteAsync("xlsx");
        Assert.Equal((2, ExportFormat.Xlsx), (export.Calls[1].Rows, export.Calls[1].Format));
        Assert.Equal(2, notes.Count);
    }

    [Fact]
    public async Task View_model_shows_errors_in_red_and_stays_quiet_when_the_user_cancels()
    {
        var data = new FakeDataService(enableMutableDemoStore: true);
        var export = new FakeOrderExportService();
        var notes = new List<(string, bool)>();
        var vm = NewVm(export, data, notes);
        vm.LoadOrders(data.GetReturnOrders(40), Now);

        export.NextResult = new ExportResult(false, false, "Không ghi được tệp: ...", null, 0);
        await vm.ExportVisibleCommand.ExecuteAsync("xlsx");
        Assert.True(vm.ExportStatusIsError);
        Assert.Contains("Không ghi được", vm.ExportStatus);
        Assert.Single(notes);
        Assert.True(notes[0].Item2);

        export.NextResult = new ExportResult(false, true, "Đã hủy xuất file.", null, 0);
        await vm.ExportVisibleCommand.ExecuteAsync("xlsx");
        Assert.False(vm.ExportStatusIsError);
        Assert.Single(notes);                                           // hủy: không thêm thông báo
    }

    [Fact]
    public async Task Selected_export_with_no_selection_does_nothing_harmful()
    {
        var data = new FakeDataService(enableMutableDemoStore: true);
        var export = new FakeOrderExportService();
        var vm = NewVm(export, data);
        vm.LoadOrders(data.GetReturnOrders(40), Now);
        await vm.ExportSelectedCommand.ExecuteAsync("xlsx");
        Assert.Empty(export.Calls);                                     // không dòng nào: không gọi xuất tệp
        Assert.Contains("Không có dòng nào", vm.ExportStatus);
    }
}
