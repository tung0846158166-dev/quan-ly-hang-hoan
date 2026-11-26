using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Import;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Tests;

/// <summary>Sao lưu DB, đọc nhật ký hoạt động, xuất CSV/XLSX. Dữ liệu GIẢ, thư mục tạm.</summary>
public sealed class BackupAuditExportTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-bk-" + Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_dir, "t.db");
    private string Bk => Path.Combine(_dir, "backups");

    public BackupAuditExportTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "du_lieu_hang_hoan.json"), JsonSerializer.Serialize(new object[]
        {
            new { platform = "Shopee", orderType = "Trả hàng", orderId = "O1", track = "SPXA1", status = "Chưa trả", note = "=HYPERLINK(\"x\")", userNote = "ghi, chú \"q\"", isWatchlist = true, watchlistReason = "kiểm", price = 125000, items = new[] { new { product = "Áo", color = "Đen", size = "M", qty = 2 } } },
            new { platform = "TikTok", orderType = "Trả hàng", orderId = "T1", track = "TKR1", status = "Hàng đã trả", note = "-bad", price = 5, items = Array.Empty<object>() },
        }));
        LegacyImporter.Import(_dir, Db);
    }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private long Scalar(string path, string sql)
    {
        using var c = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False"); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToInt64(cmd.ExecuteScalar());
    }

    // ---------------- sao lưu ----------------
    [Fact]
    public void Backup_creates_a_valid_copy_without_touching_the_source()
    {
        var before = File.GetLastWriteTimeUtc(Db);
        var r = BackupService.Backup(Db, Bk, Now);
        Assert.True(r.Ok, r.Message);
        Assert.Equal("data-20261123-120000.db", Path.GetFileName(r.Path));
        Assert.Equal(Scalar(Db, "SELECT COUNT(*) FROM orders"), Scalar(r.Path, "SELECT COUNT(*) FROM orders"));
        Assert.Equal(before, File.GetLastWriteTimeUtc(Db));
        Assert.Empty(Directory.GetFiles(Bk, "*.tmp"));
    }

    [Fact]
    public void Backup_of_a_missing_database_fails_politely()
    {
        var r = BackupService.Backup(Path.Combine(_dir, "khong-co.db"), Bk, Now);
        Assert.False(r.Ok);
        Assert.False(Directory.Exists(Bk));
    }

    [Fact]
    public void Backup_works_while_the_database_is_being_written()
    {
        using var w = new SqliteConnection($"Data Source={Db};Pooling=False"); w.Open();
        using var tx = w.BeginTransaction();
        using var cmd = w.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO audit_log VALUES (9999,'t','edit','dang ghi','{}')"; cmd.ExecuteNonQuery();
        var r = BackupService.Backup(Db, Bk, Now);   // giao dịch chưa commit: bản sao là trạng thái đã commit nhất quán
        Assert.True(r.Ok, r.Message);
        Assert.Equal(0, Scalar(r.Path, "SELECT COUNT(*) FROM audit_log WHERE seq=9999"));
        tx.Commit();
    }

    [Fact]
    public void Run_if_due_skips_a_fresh_backup_and_prunes_only_old_files_keeping_the_newest_three()
    {
        Assert.NotNull(BackupService.RunIfDue(Db, Bk, 2, 7, Now));
        Assert.Null(BackupService.RunIfDue(Db, Bk, 2, 7, Now.AddHours(1)));        // chưa đến hạn
        Assert.NotNull(BackupService.RunIfDue(Db, Bk, 2, 7, Now.AddHours(3)));

        foreach (var d in new[] { 10, 12, 20, 30 })    // 4 bản cũ quá hạn
            BackupService.Backup(Db, Bk, Now.AddDays(-d));
        File.WriteAllText(Path.Combine(Bk, "ghi-chu-cua-toi.txt"), "không phải sao lưu");
        File.WriteAllText(Path.Combine(Bk, "data-khong-hop-le.db"), "x");

        var removed = BackupService.Prune(Bk, 7, Now.AddHours(3));
        Assert.Equal(3, removed);                                                    // 6 bản - giữ 3 mới nhất = chỉ xóa bản quá hạn nằm ngoài 3 mới nhất
        Assert.True(File.Exists(Path.Combine(Bk, "ghi-chu-cua-toi.txt")));           // không đụng tệp lạ
        Assert.True(File.Exists(Path.Combine(Bk, "data-khong-hop-le.db")));
        Assert.Equal(3, BackupService.List(Bk).Count);
    }

    [Fact]
    public void Prune_never_goes_below_three_even_if_all_are_old()
    {
        foreach (var d in new[] { 40, 50, 60, 70 }) BackupService.Backup(Db, Bk, Now.AddDays(-d));
        BackupService.Prune(Bk, 1, Now);
        Assert.Equal(3, BackupService.List(Bk).Count);
    }

    [Fact]
    public void Prune_caps_automatic_backups_at_five_even_when_all_are_fresh()
    {
        // chủ dự án 26/11/2026: 1 ngày đã 6 GB vì mỗi lần nhập tệp + mỗi 2 giờ đều tạo bản mới, không giới hạn số bản
        for (var h = 0; h < 8; h++) BackupService.Backup(Db, Bk, Now.AddHours(-h));
        File.WriteAllText(Path.Combine(Bk, "truoc-chuyen-cloud-20261126.db"), "bản thủ công");
        Assert.Equal(3, BackupService.Prune(Bk, 7, Now));
        var left = BackupService.List(Bk);
        Assert.Equal(BackupService.MaxKeep, left.Count);
        Assert.Equal(Now, left[0].Time);                                              // giữ bản MỚI nhất
        Assert.True(File.Exists(Path.Combine(Bk, "truoc-chuyen-cloud-20261126.db"))); // bản thủ công không đụng
    }

    [Fact]
    public void Run_if_due_prunes_even_when_no_new_backup_is_needed()
    {
        for (var m = 0; m < 7; m++) BackupService.Backup(Db, Bk, Now.AddMinutes(-m)); // vd. sinh ra do nhiều lần nhập tệp
        Assert.Null(BackupService.RunIfDue(Db, Bk, 2, 7, Now.AddMinutes(1)));        // chưa đến hạn sao lưu
        Assert.Equal(BackupService.MaxKeep, BackupService.List(Bk).Count);            // nhưng vẫn dọn
    }

    // ---------------- nhật ký ----------------
    [Fact]
    public void Audit_log_is_read_newest_first_with_filters_and_labels()
    {
        var a = OrderRepository.Load(Db).Single(o => o.Order.Track == "SPXA1").Order;
        OrderEditor.SetField(Db, a.Id, EditField.Status, ReceiveStatus.Received, Now);
        OrderEditor.SetField(Db, a.Id, EditField.UserNote, "100% xong_nhé", Now.AddMinutes(1));

        var all = AuditLogReader.Read(Db);
        Assert.True(all.Count >= 2);
        Assert.True(all[0].Seq > all[1].Seq);
        Assert.Equal("Sửa đơn", all.First(e => e.Type == "edit").Label);
        Assert.All(AuditLogReader.Read(Db, type: "edit"), e => Assert.Equal("edit", e.Type));
        Assert.Contains("edit", AuditLogReader.Types(Db));
        Assert.Single(AuditLogReader.Read(Db, text: "100% xong_nhé").Where(e => e.Detail.Contains("100%")).Take(1));
        Assert.Empty(AuditLogReader.Read(Db, text: "100%_khong-ton-tai"));         // ký tự % _ được thoát, không thành ký tự đại diện
        Assert.Single(AuditLogReader.Read(Db, limit: 1));
        Assert.Equal(AuditLogReader.Count(Db), AuditLogReader.Read(Db, limit: 5000).Count);
        Assert.Empty(AuditLogReader.Read(Path.Combine(_dir, "khong-co.db")));
    }

    // ---------------- chẩn đoán ----------------
    [Fact]
    public void Diagnostics_has_counts_and_masks_secrets_and_never_leaks_order_data()
    {
        var settings = new AppSettings { Room = "ABCDEFGH-SECRET-ROOM-12345" };
        BackupService.Backup(Db, BackupService.DefaultDir(Db), Now);
        var text = DiagnosticsReport.Build(Db, settings, "2.0.0-test", Now);
        Assert.Contains("Phiên bản app: 2.0.0-test", text);
        Assert.Contains("bảng chính 2", text);
        Assert.Contains("Sao lưu: bản gần nhất", text);
        Assert.Contains("ABCD****", text);
        Assert.DoesNotContain("SECRET-ROOM", text);
        foreach (var leak in new[] { "SPXA1", "TKR1", "HYPERLINK", "ghi, chú" })
            Assert.DoesNotContain(leak, text);
    }

    [Fact]
    public void Diagnostics_survives_missing_database_and_missing_settings()
    {
        var text = DiagnosticsReport.Build(Path.Combine(_dir, "khong-co.db"), null, "x", Now);
        Assert.Contains("CHƯA CÓ", text);
        Assert.Contains("không đọc được", text);
    }

    // ---------------- xuất ----------------
    private IReadOnlyList<StoredOrder> Orders() => OrderRepository.Load(Db);

    [Fact]
    public void Csv_has_bom_headers_one_row_per_parcel_and_blocks_formulas()
    {
        var bytes = OrderExporter.ToCsv(Orders());
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1 + 2, lines.Length);
        Assert.StartsWith("STT,Sàn,Loại đơn", lines[0]);
        Assert.Contains("'=HYPERLINK", text);                  // công thức bị chặn
        Assert.Contains("'-bad", text);
        Assert.Contains("\"ghi, chú \"\"q\"\"\"", text);       // dấu phẩy và nháy kép được thoát
        Assert.Contains("125000", text);
    }

    [Fact]
    public void Xlsx_round_trips_through_the_spreadsheet_reader()
    {
        var bytes = OrderExporter.ToXlsx(Orders());
        using (var z = new ZipArchive(new MemoryStream(bytes))) Assert.NotNull(z.GetEntry("xl/worksheets/sheet1.xml"));
        var rows = SpreadsheetReader.Read("don.xlsx", bytes);
        Assert.Equal(2, rows.Count);
        var a = rows.Single(r => r["Mã vận đơn"] == "SPXA1");
        Assert.Equal("Shopee", a["Sàn"]); Assert.Equal("Có", a["Đang theo dõi"]); Assert.Equal("Bảng chính", a["Vị trí"]);
        Assert.Equal("=HYPERLINK(\"x\")", a["Ghi chú / Lý do"]);   // XLSX giữ chuỗi thuần, không là công thức
    }

    [Fact]
    public void Export_has_no_customer_personal_columns_and_marks_location()
    {
        Assert.DoesNotContain(OrderExporter.Headers, h => h.Contains("khách", StringComparison.OrdinalIgnoreCase) || h.Contains("SĐT") || h.Contains("địa chỉ", StringComparison.OrdinalIgnoreCase));
        OrderEditor.Delete(Db, OrderRepository.Load(Db).Single(o => o.Order.Track == "TKR1").Order.Id, "TikTok - Hoàn hàng", Now);
        var rows = SpreadsheetReader.Read("don.xlsx", OrderExporter.ToXlsx(Orders()));
        Assert.Equal("Đã xóa", rows.Single(r => r["Mã vận đơn"] == "TKR1")["Vị trí"]);
    }
}
