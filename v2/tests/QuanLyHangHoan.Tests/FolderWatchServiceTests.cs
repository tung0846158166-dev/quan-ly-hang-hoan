using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.FolderWatch;

namespace QuanLyHangHoan.Tests;

/// <summary>Theo dõi thư mục: tự nạp file TikTok mới, không đụng file nào trong thư mục. File GIẢ.</summary>
public sealed class FolderWatchServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "qlhh-fw-" + Guid.NewGuid().ToString("N"));
    private string Watch => Path.Combine(_root, "watch");
    private string Db => Path.Combine(_root, "t.db");
    private string State => Path.Combine(_root, "folder_watch.json");
    private DateTime _now = new(2026, 11, 23, 12, 0, 0);

    public FolderWatchServiceTests()
    {
        Directory.CreateDirectory(Watch);
        AppDatabase.Initialize(Db);
    }
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private FolderWatchService Service() => new(Db, State, () => _now);

    private static string TikTokCsv(params (string order, string claim, string track, string product)[] rows) =>
        "Order ID,Return Order ID,Return Logistics Tracking ID,Product Name,Return Status,Quantity\r\n" +
        string.Concat(rows.Select(r => $"{r.order},{r.claim},{r.track},{r.product},In Process,1\r\n"));

    private string Put(string name, string content, TimeSpan? age = null)
    {
        var p = Path.Combine(Watch, name);
        File.WriteAllText(p, content, new UTF8Encoding(false));
        File.SetLastWriteTime(p, _now - (age ?? TimeSpan.FromMinutes(1)));
        return p;
    }

    private long OrdersCount()
    {
        using var c = new SqliteConnection($"Data Source={Db};Pooling=False"); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM orders";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private string HashDir() => string.Join("|", Directory.GetFiles(Watch).OrderBy(f => f).Select(f =>
        Path.GetFileName(f) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))) + ":" + File.GetLastWriteTimeUtc(f).Ticks));

    /// <summary>Bật theo dõi rồi cho thời gian trôi để file "mới" (sau lúc bật) và đã ổn định.</summary>
    private FolderWatchService EnabledService()
    {
        var s = Service();
        s.Configure(Watch, true);          // Since = lúc này
        _now = _now.AddMinutes(10);        // các file sau đó có mtime > Since
        return s;
    }

    [Fact]
    public void New_stable_tiktok_file_is_imported_once_after_the_size_settles()
    {
        var s = EnabledService();
        Put("don_hoan.csv", TikTokCsv(("O1", "C1", "RT111111", "Shirt")));

        var first = s.RunOnce();                       // lần đầu thấy: chờ kích thước ổn định
        Assert.Equal(0, first.FilesImported);
        Assert.Equal(0, OrdersCount());

        var second = s.RunOnce();                      // kích thước không đổi, file đã cũ: nạp
        Assert.True(second.Ok);
        Assert.Equal(1, second.FilesImported);
        Assert.Equal(1, OrdersCount());

        var third = s.RunOnce();                       // đã thấy rồi: không nạp lại
        Assert.Equal(0, third.FilesImported);
        Assert.Equal(1, OrdersCount());
    }

    [Fact]
    public void File_that_is_still_being_written_waits()
    {
        var s = EnabledService();
        var p = Put("don_hoan.csv", TikTokCsv(("O1", "C1", "RT111111", "Shirt")), age: TimeSpan.FromSeconds(1));  // mới ghi 1 giây trước
        s.RunOnce();
        var again = s.RunOnce();
        Assert.Equal(0, again.FilesImported);          // dưới 3 giây tuổi: chưa nạp

        File.SetLastWriteTime(p, _now - TimeSpan.FromMinutes(1));
        Assert.Equal(1, s.RunOnce().FilesImported);    // kích thước ổn định + đã đủ cũ: nạp
    }

    [Fact]
    public void Files_present_before_enabling_are_skipped_automatically_but_can_be_loaded_manually_once()
    {
        Put("cu.csv", TikTokCsv(("O1", "C1", "RT111111", "Shirt")), age: TimeSpan.FromDays(1));
        var s = Service();
        s.Configure(Watch, true);
        _now = _now.AddMinutes(10);

        s.RunOnce(); s.RunOnce(); s.RunOnce();
        Assert.Equal(0, OrdersCount());                                  // có sẵn trước khi bật: không tự nạp

        var manual = s.RunOnce(includeExisting: true);                   // "Nạp các file đang có": nạp ngay, không chờ
        Assert.Equal(1, manual.FilesImported);
        Assert.Equal(1, OrdersCount());

        Assert.Equal(0, s.RunOnce(includeExisting: true).FilesImported); // lần sau không nạp lại
        Assert.Equal(1, OrdersCount());
    }
    [Fact]
    public void Non_tiktok_files_and_junk_are_skipped_and_never_block_the_good_ones()
    {
        var s = EnabledService();
        Put("khac.csv", "Cot A,Cot B\r\n1,2\r\n");                                      // không phải TikTok
        Put("hong.xlsx", "đây không phải xlsx");                                          // hỏng
        Put("cu.xls", "binary xls");                                                      // .xls chưa hỗ trợ
        Put("~$khoa.xlsx", "tệp tạm của Excel");                                          // bị bỏ qua hẳn
        Put("ghi_chu.txt", "không phải bảng tính");
        Put("tot.csv", TikTokCsv(("O1", "C1", "RT111111", "Shirt")));

        s.RunOnce();
        var r = s.RunOnce();

        Assert.Equal(1, r.FilesImported);
        Assert.Contains("tot.csv", r.Names);
        Assert.Contains("khac.csv", r.Skipped);
        Assert.Contains(r.Failed, f => f.StartsWith("hong.xlsx"));
        Assert.Contains(r.Failed, f => f.StartsWith("cu.xls"));
        Assert.Equal(1, OrdersCount());

        var again = s.RunOnce();                                                          // cả file lỗi cũng không bị thử lại mãi
        Assert.Equal(0, again.FilesImported);
        Assert.Empty(again.Failed);
    }

    [Fact]
    public void Overlapping_exports_do_not_double_the_products()
    {
        var s = EnabledService();
        Put("tuan1.csv", TikTokCsv(("O1", "C1", "RT111111", "Shirt"), ("O2", "C2", "RT222222", "Pants")));
        Put("tuan2.csv", TikTokCsv(("O1", "C1", "RT111111", "Shirt"), ("O3", "C3", "RT333333", "Hat")));

        s.RunOnce();
        var r = s.RunOnce();

        Assert.Equal(2, r.FilesImported);
        Assert.Equal(3, OrdersCount());                                                   // O1 chỉ một lần
        var all = OrderRepository.Load(Db);
        Assert.Equal(1, all.Single(o => o.Order.OrderId == "O1").Order.Items.Single().Qty);
    }

    [Fact]
    public void Watching_never_changes_deletes_or_moves_any_file()
    {
        var s = EnabledService();
        Put("tot.csv", TikTokCsv(("O1", "C1", "RT111111", "Shirt")));
        Put("khac.csv", "Cot A,Cot B\r\n1,2\r\n");
        var before = HashDir();

        s.RunOnce(); s.RunOnce(); s.RunOnce();

        Assert.Equal(before, HashDir());
        Assert.Equal(2, Directory.GetFiles(Watch).Length);
    }

    [Fact]
    public void State_persists_so_a_new_instance_does_not_reimport()
    {
        var s = EnabledService();
        Put("tot.csv", TikTokCsv(("O1", "C1", "RT111111", "Shirt")));
        s.RunOnce(); s.RunOnce();
        Assert.Equal(1, OrdersCount());

        var s2 = Service();                                                                // "khởi động lại app"
        Assert.True(s2.Config.Enabled);
        Assert.Equal(Watch, s2.Config.Dir);
        s2.RunOnce(); s2.RunOnce();
        Assert.Equal(1, OrdersCount());
        Assert.Contains("tot.csv", File.ReadAllText(State));
    }

    [Fact]
    public void Database_failure_does_not_mark_files_as_seen_so_they_are_retried()
    {
        var s = EnabledService();
        Put("tot.csv", TikTokCsv(("O1", "C1", "RT111111", "Shirt")));
        s.RunOnce();

        var moved = Db + ".away";
        
        File.Move(Db, moved);
        var failed = s.RunOnce();
        Assert.False(failed.Ok);
        Assert.Equal(0, failed.FilesImported);

        File.Move(moved, Db);
        var retry = s.RunOnce();
        Assert.True(retry.Ok);
        Assert.Equal(1, retry.FilesImported);
        Assert.Equal(1, OrdersCount());
    }

    [Fact]
    public void Disabled_or_missing_folder_is_reported_without_throwing()
    {
        var s = Service();
        Assert.False(s.RunOnce().Ran);                                   // chưa bật

        s.Configure(Path.Combine(_root, "khong-co"), true);
        var r = s.RunOnce();
        Assert.False(r.Ok);
        Assert.Contains("Không tìm thấy thư mục", r.Note);
        Assert.False(s.Config.LastOk);
    }

    [Fact]
    public void Empty_and_nested_files_are_ignored()
    {
        var s = EnabledService();
        Put("rong.csv", "");
        Directory.CreateDirectory(Path.Combine(Watch, "con"));
        File.WriteAllText(Path.Combine(Watch, "con", "trong_thu_muc_con.csv"), TikTokCsv(("O9", "C9", "RT999999", "Hat")));
        File.SetLastWriteTime(Path.Combine(Watch, "con", "trong_thu_muc_con.csv"), _now - TimeSpan.FromMinutes(1));

        s.RunOnce();
        var r = s.RunOnce();

        Assert.Equal(0, r.FilesImported);
        Assert.Equal(0, OrdersCount());
    }

    [Fact]
    public void Seen_list_is_capped_at_3000_keeping_the_newest()
    {
        var s = Service();
        s.Configure(Watch, true);
        for (var i = 0; i < 3100; i++) s.Config.Seen[$"f{i}|1|{i}"] = 1000 + i;
        s.SaveConfig();

        var reloaded = Service();
        Assert.Equal(3000, reloaded.Config.Seen.Count);
        Assert.Contains("f3099|1|3099", reloaded.Config.Seen.Keys);
        Assert.DoesNotContain("f0|1|0", reloaded.Config.Seen.Keys);
    }

    [Fact]
    public void Completed_event_is_raised_with_the_note_and_counts()
    {
        var s = EnabledService();
        FolderWatchRunResult? got = null;
        s.Completed += r => got = r;
        Put("tot.csv", TikTokCsv(("O1", "C1", "RT111111", "Shirt")));

        s.RunOnce(); s.RunOnce();

        Assert.NotNull(got);
        Assert.True(got!.Ok);
        Assert.Contains("tot.csv", got.Note);
        Assert.Equal(1, got.Import!.Added);
        Assert.True(s.Config.LastOk);
        Assert.Contains("tot.csv", s.Config.LastNote);
    }

    [Fact]
    public void Personal_columns_are_dropped_and_never_reach_the_database()
    {
        var s = EnabledService();
        Put("pii.csv", "Order ID,Return Order ID,Return Logistics Tracking ID,Product Name,Return Status,Quantity,Tên khách hàng,Số điện thoại,Địa chỉ\r\n" +
                       "O1,C1,RT111111,Shirt,In Process,1,Nguyễn Văn Giả,0900000000,Số 1 Đường Giả\r\n");

        s.RunOnce(); s.RunOnce();

        Assert.Equal(1, OrdersCount());
        using var c = new SqliteConnection($"Data Source={Db};Pooling=False"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM orders WHERE raw_json LIKE '%0900000000%' OR extra_json LIKE '%0900000000%' OR buyer_note LIKE '%Nguyễn%' OR note LIKE '%Đường Giả%'";
        Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
    }
}
