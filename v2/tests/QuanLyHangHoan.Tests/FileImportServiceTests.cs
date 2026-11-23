using System.IO;
using System.Text;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.Tests;

/// <summary>Nạp tay nhiều file CSV (TikTok + Shopee + file lạ). Dữ liệu GIẢ, DB tạm.</summary>
public sealed class FileImportServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-fi-" + Guid.NewGuid().ToString("N"));
    public FileImportServiceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
    private string Db => Path.Combine(_dir, "t.db");
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    private const string Tiktok = "Order ID,Return Order ID,Return Logistics Tracking ID,Product Name,Return Status,Quantity\nT1,C1,RT111111,Shirt,In Process,1\n";
    private const string Shopee = "Mã đơn hàng,Mã số khiếu nại,Mã vận đơn,Tên sản phẩm,Tên phân loại hàng,Số lượng,Trạng thái Trả hàng/Hoàn tiền,Tổng số tiền hoàn trả,Tên người mua,Số điện thoại\nSP1,RR1,SPXVN0000000001,Ao,\"Den,M\",1,Chờ xử lý,199000,Nguyen Van A,0900000000\n";

    private static TikTokMapOptions Opt() => new() { Today = Now };

    [Fact]
    public void Routes_each_file_by_kind_and_reports_unknown_files_without_breaking_the_rest()
    {
        AppDatabase.Initialize(Db);
        var r = FileImportService.Import(Db, new[] { ("tiktok.csv", B(Tiktok)), ("shopee.csv", B(Shopee)), ("tai-lieu.csv", B("Tên,Ghi chú\nA,B\n")) }, Opt(), ImportProductMode.Supplement, Now);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(FileKind.TikTok, r.Files.Single(f => f.Name == "tiktok.csv").Kind);
        Assert.Equal(FileKind.Shopee, r.Files.Single(f => f.Name == "shopee.csv").Kind);
        Assert.NotNull(r.Files.Single(f => f.Name == "tai-lieu.csv").Error);
        Assert.Equal(2, r.Added);
        var all = OrderRepository.Load(Db).Select(s => s.Order).ToList();
        Assert.Contains(all, o => o.Platform == "TikTok");
        Assert.Contains(all, o => o.Platform == "Shopee" && o.Price == 199000m);
    }

    [Fact]
    public void Personal_columns_never_reach_the_database()
    {
        AppDatabase.Initialize(Db);
        FileImportService.Import(Db, new[] { ("shopee.csv", B(Shopee)) }, Opt(), ImportProductMode.Supplement, Now);
        using var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Db};Pooling=False");
        c.Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM orders WHERE extra_json LIKE '%Nguyen Van A%' OR extra_json LIKE '%0900000000%' OR note LIKE '%Nguyen%'";
        Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    [Fact]
    public void Importing_the_same_file_twice_does_not_duplicate()
    {
        AppDatabase.Initialize(Db);
        FileImportService.Import(Db, new[] { ("shopee.csv", B(Shopee)) }, Opt(), ImportProductMode.Supplement, Now);
        var r = FileImportService.Import(Db, new[] { ("shopee.csv", B(Shopee)) }, Opt(), ImportProductMode.Supplement, Now);
        Assert.Equal(0, r.Added);
        Assert.Single(OrderRepository.Load(Db));
    }

    [Fact]
    public void Corrupt_file_is_reported_per_file()
    {
        AppDatabase.Initialize(Db);
        var r = FileImportService.Import(Db, new[] { ("hong.xlsx", B("không phải xlsx")), ("shopee.csv", B(Shopee)) }, Opt(), ImportProductMode.Supplement, Now);
        Assert.NotNull(r.Files.Single(f => f.Name == "hong.xlsx").Error);
        Assert.Equal(1, r.Added);
    }
}
