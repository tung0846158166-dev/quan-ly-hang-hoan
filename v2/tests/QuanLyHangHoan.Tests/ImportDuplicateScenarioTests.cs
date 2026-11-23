using System.IO;
using System.Text;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Sheet;

namespace QuanLyHangHoan.Tests;

/// <summary>
/// Trùng mã đơn hàng / mã vận đơn khi NẠP FILE và khi ĐỒNG BỘ GOOGLE SHEET. Bất biến cần giữ (AGENTS.md 1, 2, 3, 5, 8):
/// kiện khác nhau không bao giờ bị gộp chỉ vì cùng mã đơn/mã yêu cầu; mã dùng chung giữa nhiều đơn thì báo nhập nhằng, không đoán;
/// nhập lại không nhân đôi dòng hay sản phẩm. Dữ liệu GIẢ, DB tạm.
/// </summary>
public sealed class ImportDuplicateScenarioTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-dup-" + Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_dir, "t.db");
    public ImportDuplicateScenarioTests() { Directory.CreateDirectory(_dir); AppDatabase.Initialize(Db); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);
    private FileImportResult Import(params (string, string)[] files) =>
        FileImportService.Import(Db, files.Select(f => (f.Item1, B(f.Item2))).ToList(), new TikTokMapOptions { Today = Now }, ImportProductMode.Supplement, Now);

    private const string ShopeeHead = "Mã đơn hàng,Mã số khiếu nại,Mã vận đơn,Tên sản phẩm,Tên phân loại hàng,Số lượng,Trạng thái Trả hàng/Hoàn tiền,Tổng số tiền hoàn trả\n";
    private static string Sp(string order, string claim, string track, string product = "Ao", string qty = "1") =>
        $"{order},{claim},{track},{product},\"Den,M\",{qty},Chờ xử lý,100000\n";

    private const string TikHead = "Order ID,Return Order ID,Return Logistics Tracking ID,Product Name,Return Status,Quantity\n";
    private static string Tk(string order, string claim, string track, string product = "Shirt", string qty = "1") =>
        $"{order},{claim},{track},{product},In Process,{qty}\n";

    private List<StoredOrder> Rows() => OrderRepository.Load(Db).ToList();

    // ================= NẠP FILE: Shopee =================
    [Fact]
    public void Shopee_same_order_two_claims_with_two_tracking_codes_are_two_rows_and_stay_two_on_reimport()
    {
        var csv = ShopeeHead + Sp("SP1", "RR1", "SPXVN0000000001", "Ao A") + Sp("SP1", "RR2", "SPXVN0000000002", "Ao B");
        Import(("a.csv", csv));
        Assert.Equal(2, Rows().Count);
        Import(("a.csv", csv));
        var rows = Rows();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "SPXVN0000000001", "SPXVN0000000002" }, rows.Select(r => r.Order.Track).OrderBy(x => x).ToArray());
        Assert.All(rows, r => Assert.Single(r.Order.Items));            // không cộng lặp sản phẩm
    }

    [Fact]
    public void Shopee_exact_duplicate_row_in_one_file_does_not_create_two_parcels()
    {
        var row = Sp("SP1", "RR1", "SPXVN0000000001");
        Import(("a.csv", ShopeeHead + row + row));
        var rows = Rows();
        Assert.Single(rows);
        Assert.Equal("SPXVN0000000001", rows[0].Order.Track);
    }

    [Fact]
    public void Shopee_two_files_overlapping_in_one_batch_do_not_double_rows()
    {
        var csv = ShopeeHead + Sp("SP1", "RR1", "SPXVN0000000001") + Sp("SP2", "RR9", "SPXVN0000000009");
        var r = Import(("tuan1.csv", csv), ("tuan2.csv", csv));
        Assert.Equal(2, Rows().Count);
        Assert.Equal(2, r.Added);
    }

    [Fact]
    public void Shopee_two_different_orders_with_the_same_tracking_code_are_never_merged()
    {
        Import(("a.csv", ShopeeHead + Sp("SP1", "RR1", "SPXVN0000000777", "Ao A")));
        var second = Import(("b.csv", ShopeeHead + Sp("SP2", "RR2", "SPXVN0000000777", "Ao B")));
        var rows = Rows();
        // Đơn SP1 còn nguyên (không bị đổi mã đơn, không bị thêm sản phẩm của SP2); SP2 được BÁO để kiểm tra, không ghi đoán.
        var only = Assert.Single(rows);
        Assert.Equal("SP1", only.Order.OrderId);
        Assert.Equal("Ao A", only.Order.Items.Single().Product);
        Assert.Equal(1, second.Ambiguous);
        Assert.Equal(0, second.Added + second.Updated);
        Assert.True(second.Shopee!.Ambiguous.Single().OrderConflict);
    }

    [Fact]
    public void Placeholder_order_id_from_the_phone_is_upgraded_to_the_real_order_id_by_a_later_file()
    {
        // Điện thoại tạo đơn: mã đơn tạm = mã vận đơn. File sàn đến sau có mã đơn thật cho cùng mã vận đơn => ghép và nâng cấp, KHÔNG báo xung đột.
        var msg = System.Text.Json.JsonSerializer.Serialize(new { type = "new_order_scan", room = "R", requestId = "q1", code = "SPXVN0000000888", captureId = "c1" });
        Assert.NotNull(QuanLyHangHoan.Data.Sync.PhoneActions.Handle(Db, "R", msg, Now));
        Assert.Equal("SPXVN0000000888", Rows().Single().Order.OrderId);

        var r = Import(("a.csv", ShopeeHead + Sp("SP9", "RR9", "SPXVN0000000888", "Ao Z")));
        Assert.Equal(0, r.Ambiguous);
        var only = Assert.Single(Rows());
        Assert.Equal("SP9", only.Order.OrderId);
    }

    [Fact]
    public void Shopee_new_claim_with_a_new_code_for_an_order_that_already_has_a_parcel_is_a_sibling_not_an_overwrite()
    {
        Import(("a.csv", ShopeeHead + Sp("SP1", "RR1", "SPXVN0000000001", "Ao A")));
        Import(("b.csv", ShopeeHead + Sp("SP1", "RR2", "SPXVN0000000002", "Ao B")));
        var rows = Rows();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Ao A", rows.Single(r => r.Order.Track == "SPXVN0000000001").Order.Items.Single().Product);
        Assert.Equal("Ao B", rows.Single(r => r.Order.Track == "SPXVN0000000002").Order.Items.Single().Product);
    }

    // ================= NẠP FILE: TikTok =================
    [Fact]
    public void Tiktok_same_parcel_split_over_two_product_rows_is_one_parcel_with_two_items()
    {
        var csv = TikHead + Tk("T1", "C1", "RT111111", "Shirt") + Tk("T1", "C1", "RT111111", "Pants");
        Import(("t.csv", csv));
        var rows = Rows();
        Assert.Single(rows);
        Assert.Equal(2, rows[0].Order.Items.Count);
        Import(("t.csv", csv));
        Assert.Single(Rows());
        Assert.Equal(2, Rows()[0].Order.Items.Count);                  // nhập lại: không thành 4 sản phẩm
    }

    [Fact]
    public void Tiktok_same_order_with_two_different_claims_stay_separate_parcels()
    {
        Import(("t.csv", TikHead + Tk("T1", "C1", "RT111111", "Shirt") + Tk("T1", "C2", "RT222222", "Pants")));
        var rows = Rows();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Shirt", rows.Single(r => r.Order.Track == "RT111111" || r.Order.ReturnTrack == "RT111111").Order.Items.Single().Product);
    }

    [Fact]
    public void Tiktok_shared_tracking_code_across_two_orders_is_not_merged()
    {
        Import(("a.csv", TikHead + Tk("T1", "C1", "RT999999", "Shirt")));
        Import(("b.csv", TikHead + Tk("T2", "C2", "RT999999", "Pants")));
        var rows = Rows();
        Assert.Single(rows.Where(r => r.Order.OrderId == "T1"));
        Assert.Single(rows.Where(r => r.Order.OrderId == "T2"));
        Assert.Equal("Shirt", rows.Single(r => r.Order.OrderId == "T1").Order.Items.Single().Product);
    }

    // ================= HOÀN MỘT PHẦN: giữ đủ sản phẩm, đánh dấu sản phẩm đã hoàn =================
    private const string TikHead2 = "Order ID,Return Order ID,Tracking ID,Return Logistics Tracking ID,Product Name,Quantity,Return Quantity,Return Status\n";
    private const string Sent3 = "A,,B,,Ao,1,,Delivered\nA,,B,,Quan,1,,Delivered\nA,,B,,Non,1,,Delivered\n";
    private const string Return1 = "A,R1,B,C,Ao,3,1,In Process\n";

    private FileImportResult ImportTo(string db, ImportProductMode mode, string name, string csv, bool onlyReturns) =>
        FileImportService.Import(db, new[] { (name, B(csv)) }, new TikTokMapOptions { Today = Now, OnlyReturns = onlyReturns }, mode, Now);

    [Fact]
    public void Partial_return_keeps_all_three_products_and_marks_only_the_returned_one()
    {
        ImportTo(Db, ImportProductMode.Supplement, "f1.csv", TikHead2 + Sent3, onlyReturns: false);
        var sentOnly = Assert.Single(Rows()).Order;
        Assert.Equal(3, sentOnly.Items.Count);
        Assert.All(sentOnly.Items, i => Assert.Equal(0, i.ReturnedQty));          // mới gửi đi: chưa có gì "đã hoàn"

        var r = ImportTo(Db, ImportProductMode.Supplement, "f2.csv", TikHead2 + Return1, onlyReturns: true);
        Assert.Equal(1, r.Updated);
        var o = Assert.Single(Rows()).Order;                                       // cùng một kiện, không tạo dòng mới
        Assert.Equal("C", o.ReturnTrack); Assert.Equal("B", o.ForwardTrack); Assert.True(o.IsPartialReturn);
        Assert.Equal(3, o.Items.Count);                                            // GIỮ ĐỦ 3 sản phẩm
        Assert.Equal(1, o.Items.Single(i => i.Product == "Ao").ReturnedQty);       // sản phẩm đã hoàn
        Assert.Equal(0, o.Items.Single(i => i.Product == "Quan").ReturnedQty);     // hai sản phẩm còn lại không hoàn
        Assert.Equal(0, o.Items.Single(i => i.Product == "Non").ReturnedQty);

        ImportTo(Db, ImportProductMode.Supplement, "f2.csv", TikHead2 + Return1, onlyReturns: true);   // nạp lại: không đổi gì
        var again = Assert.Single(Rows()).Order;
        Assert.Equal(3, again.Items.Count);
        Assert.Equal(1, again.Items.Single(i => i.Product == "Ao").ReturnedQty);
    }

    [Fact]
    public void Partial_return_is_visible_in_export_and_phone_list_but_not_for_full_or_unknown_returns()
    {
        ImportTo(Db, ImportProductMode.Supplement, "f1.csv", TikHead2 + Sent3, onlyReturns: false);
        var unknown = Assert.Single(Rows());
        Assert.Equal("", QuanLyHangHoan.Data.OrderExporter.ReturnedItemsText(unknown.Order));          // chưa biết: không đánh dấu

        ImportTo(Db, ImportProductMode.Supplement, "f2.csv", TikHead2 + Return1, onlyReturns: true);
        var partial = Assert.Single(Rows());
        Assert.Equal("Ao x1", QuanLyHangHoan.Data.OrderExporter.ReturnedItemsText(partial.Order));
        var rows = OrderRepository.Load(Db);
        var phone = QuanLyHangHoan.Data.Sync.PhoneSnapshot.BuildRow(partial, Now);
        Assert.Equal("Ao", phone["rn"]);
        Assert.Contains("Quan", (string)phone["n"]);                                                    // danh sách đầy đủ vẫn còn đủ 3 sản phẩm
        var xlsx = QuanLyHangHoan.Data.Import.SpreadsheetReader.Read("x.xlsx", QuanLyHangHoan.Data.OrderExporter.ToXlsx(rows));
        Assert.Equal("Ao x1", xlsx.Single()["Sản phẩm đã hoàn"]);

        var db2 = Path.Combine(_dir, "full.db"); AppDatabase.Initialize(db2);
        ImportTo(db2, ImportProductMode.Supplement, "f.csv", TikHead2 + "A,R1,B,C,Ao,1,1,In Process\nA,R1,B,C,Quan,1,1,In Process\n", onlyReturns: true);
        var full = Assert.Single(OrderRepository.Load(db2));
        Assert.Equal("", QuanLyHangHoan.Data.OrderExporter.ReturnedItemsText(full.Order));            // hoàn đủ: không cần phân biệt
        Assert.False(QuanLyHangHoan.Data.Sync.PhoneSnapshot.BuildRow(full, Now).ContainsKey("rn"));
    }

    [Fact]
    public void Replace_mode_still_means_only_the_returned_product_remains()
    {
        var db = Path.Combine(_dir, "replace.db"); AppDatabase.Initialize(db);
        ImportTo(db, ImportProductMode.Replace, "f1.csv", TikHead2 + Sent3, onlyReturns: false);
        ImportTo(db, ImportProductMode.Replace, "f2.csv", TikHead2 + Return1, onlyReturns: true);
        var o = Assert.Single(OrderRepository.Load(db)).Order;
        var only = Assert.Single(o.Items);
        Assert.Equal("Ao", only.Product); Assert.Equal(1, only.ReturnedQty);
    }

    [Fact]
    public void Full_return_marks_every_product_as_returned()
    {
        ImportTo(Db, ImportProductMode.Supplement, "f.csv", TikHead2 + "A,R1,B,C,Ao,1,1,In Process\nA,R1,B,C,Quan,1,1,In Process\n", onlyReturns: true);
        var o = Assert.Single(Rows()).Order;
        Assert.Equal(2, o.Items.Count);
        Assert.All(o.Items, i => Assert.Equal(1, i.ReturnedQty));
    }

    [Fact]
    public void Shopee_return_rows_are_marked_returned_and_survive_reload_from_the_database()
    {
        Import(("a.csv", ShopeeHead + Sp("SP1", "RR1", "SPXVN0000000001", "Ao A", "2")));
        var o = Assert.Single(Rows()).Order;
        Assert.Equal(2, o.Items.Single().ReturnedQty);
    }

    // ================= GOOGLE SHEET =================
    private const string SheetHead = "Mã,Mã đơn,Loại,Mã vận đơn,Trạng thái về,Về shop lúc,Hạn khiếu nại,Hàng,SL,Tiền,Lý do,Ngày,Cập nhật\n";
    private static string SheetRow(string claim, string order, string track, string updated = "22/11/2026", string product = "Áo") =>
        $"{claim},{order},Trả hàng + hoàn tiền,{track},Đang về,,24/11/2026 10:00,\"{product} · Đen, M × 1\",1,100000,Sai mô tả,20/11/2026,{updated}\n";

    private sealed class FakeProtector : ISecretProtector
    {
        public string Protect(string plain) => "P:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
        public string? Unprotect(string t) => t.StartsWith("P:") ? Encoding.UTF8.GetString(Convert.FromBase64String(t[2..])) : null;
    }

    private SheetSyncService Sheet(Func<string> csv)
    {
        var s = new SheetSyncService(Db, Path.Combine(_dir, "sheet.json"), new FakeProtector(), () => Now, (u, ct) => Task.FromResult(csv()));
        Assert.True(s.SetUrl("https://script.google.com/macros/s/FAKE_ID_FOR_TESTS/exec", out _));
        return s;
    }

    [Fact]
    public void Sheet_same_order_with_an_old_and_a_new_request_keeps_only_the_latest_request_as_one_parcel()
    {
        var csv = SheetHead + SheetRow("RR1", "SP1", "SPXVN0000000001", "20/11/2026") + SheetRow("RR2", "SP1", "SPXVN0000000001", "22/11/2026");
        using var s = Sheet(() => csv);
        var r = s.RunOnce();
        Assert.True(r.Ok, r.Note);
        var rows = Rows();
        Assert.Single(rows);                                           // một đơn trên Sheet = một kiện (luật của app cũ)
        s.RunOnce();
        Assert.Single(Rows());
    }

    [Fact]
    public void Sheet_two_different_orders_sharing_one_tracking_code_are_not_merged_and_the_second_is_reported()
    {
        var csv = SheetHead + SheetRow("RR1", "SP1", "SPXVN0000000555", product: "Áo A") + SheetRow("RR2", "SP2", "SPXVN0000000555", product: "Áo B");
        using var s = Sheet(() => csv);
        var r = s.RunOnce();
        Assert.True(r.Ok, r.Note);
        var only = Assert.Single(Rows());                              // đơn đến trước giữ nguyên, không bị đổi mã đơn/nuốt sản phẩm
        Assert.Equal("SP1", only.Order.OrderId);
        Assert.Equal("Áo A", only.Order.Items.Single().Product);
        Assert.Equal(1, r.Import!.Ambiguous.Count);                    // đơn kia được BÁO để kiểm tra thủ công
        Assert.True(r.Import.Ambiguous.Single().OrderConflict);
    }

    [Fact]
    public void Sheet_row_that_matches_a_parcel_already_imported_from_a_file_updates_it_instead_of_duplicating()
    {
        Import(("a.csv", ShopeeHead + Sp("SP1", "RR1", "SPXVN0000000001", "Áo")));
        var csv = SheetHead + SheetRow("RR1", "SP1", "SPXVN0000000001");
        using var s = Sheet(() => csv);
        Assert.True(s.RunOnce().Ok);
        Assert.Single(Rows());
    }

    [Fact]
    public void Sheet_does_not_overwrite_warehouse_fields_or_resurrect_deleted_orders_when_ids_repeat()
    {
        var csv = SheetHead + SheetRow("RR1", "SP1", "SPXVN0000000001");
        using var s = Sheet(() => csv);
        Assert.True(s.RunOnce().Ok);
        var id = Rows().Single().Order.Id;
        OrderEditor.SetField(Db, id, EditField.UserNote, "ghi chú của tôi", Now);
        OrderEditor.Delete(Db, id, "Shopee - Hoàn hàng", Now);
        csv = SheetHead + SheetRow("RR1", "SP1", "SPXVN0000000001", "23/11/2026");
        Assert.True(s.RunOnce().Ok);
        var rows = Rows();
        Assert.Single(rows);                                           // đơn đã xóa không bị tạo lại
        Assert.True(rows[0].IsDeleted);
        Assert.Equal("ghi chú của tôi", rows[0].Order.UserNote);
    }
}
