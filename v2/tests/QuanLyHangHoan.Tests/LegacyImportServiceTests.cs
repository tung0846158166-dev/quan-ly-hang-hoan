using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Tests;

/// <summary>Quy trình nhập an toàn: sao chép -> kiểm hash -> dry-run -> nhập -> đối chiếu. Dữ liệu GIẢ.</summary>
public sealed class LegacyImportServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "qlhh-svc-" + Guid.NewGuid().ToString("N"));
    private string Src => Path.Combine(_root, "src");
    private string Work => Path.Combine(_root, "work");
    private string Db => Path.Combine(_root, "data", "v2.db");

    public LegacyImportServiceTests() => Directory.CreateDirectory(Src);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static object Row(string oid, string track) => new
    {
        platform = "TikTok Shop", orderType = "THẤT THOÁT", orderId = oid, track, status = "Chưa về", price = 1,
        items = new[] { new { product = "Áo giả", color = "Đen", size = "M", qty = 1 } }
    };

    private void Seed(params object[] rows)
    {
        File.WriteAllText(Path.Combine(Src, "du_lieu_hang_hoan.json"), JsonSerializer.Serialize(rows));
        Directory.CreateDirectory(Path.Combine(Src, "photos"));
        File.WriteAllText(Path.Combine(Src, "photos", "x.json"), "{}");
    }

    private static string Hash(string dir) => string.Join("|", Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
        .OrderBy(f => f).Select(f => Path.GetRelativePath(dir, f) + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f)))));

    [Fact]
    public void DryRunOnly_copies_and_analyzes_but_does_not_create_database()
    {
        Seed(Row("O1", "A"), Row("O2", "B"));
        var before = Hash(Src);

        var r = LegacyImportService.Run(Src, Work, Db, dryRunOnly: true);

        Assert.True(r.Ok);
        Assert.Equal(2, r.DryRun!.Main.Mapped);
        Assert.False(File.Exists(Db));
        Assert.True(Directory.Exists(r.CopyFolder));
        Assert.Equal(before, Hash(Src));
        Assert.Null(r.Import);
    }

    [Fact]
    public void Full_run_imports_from_copy_and_reconciles()
    {
        Seed(Row("O1", "A"), Row("O2", "B"));
        var before = Hash(Src);

        var r = LegacyImportService.Run(Src, Work, Db, dryRunOnly: false);

        Assert.True(r.Ok);
        Assert.Equal(2, r.Import!.Inserted);
        Assert.True(r.Reconcile!.AllMatch);
        Assert.Equal(2, OrderRepository.Load(Db).Count);
        Assert.Equal(before, Hash(Src)); // nguồn không đổi
        Assert.NotEqual(Path.GetFullPath(Src), Path.GetFullPath(r.CopyFolder));
        Assert.Equal(Hash(Src), Hash(r.CopyFolder)); // bản sao khớp nguồn (đã kiểm hash)
    }

    [Fact]
    public void Each_run_makes_a_new_copy_and_second_import_is_idempotent()
    {
        Seed(Row("O1", "A"));
        var a = LegacyImportService.Run(Src, Work, Db, dryRunOnly: false);
        var b = LegacyImportService.Run(Src, Work, Db, dryRunOnly: false);

        Assert.NotEqual(a.CopyFolder, b.CopyFolder);
        Assert.Equal(0, b.Import!.Inserted);
        Assert.Single(OrderRepository.Load(Db));
    }

    [Fact]
    public void Missing_source_fails_without_touching_anything()
    {
        var r = LegacyImportService.Run(Path.Combine(_root, "nope"), Work, Db, dryRunOnly: false);
        Assert.False(r.Ok);
        Assert.False(File.Exists(Db));
        Assert.NotEmpty(r.Message);
    }

    [Fact]
    public void Dry_run_errors_block_the_real_import()
    {
        File.WriteAllText(Path.Combine(Src, "du_lieu_hang_hoan.json"), "{ không phải json");
        var r = LegacyImportService.Run(Src, Work, Db, dryRunOnly: false);
        Assert.False(r.Ok);
        Assert.False(File.Exists(Db));
    }
}
