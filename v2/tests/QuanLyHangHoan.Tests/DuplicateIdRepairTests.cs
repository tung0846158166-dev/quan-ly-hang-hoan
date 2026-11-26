using System.IO;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Ai;
using QuanLyHangHoan.Data.Cloud;

namespace QuanLyHangHoan.Tests;

/// <summary>
/// Ảnh 28 (26/11/2026): dữ liệu cũ có 2 dòng cùng id; khi đẩy lên cloud dòng thứ 2 thành "X~2", cloud trả về "X~2" và máy chép thêm dòng
/// "cloud:X~2" -> 3 dòng cho 2 kiện, trợ lý AI lỗi "trùng khóa". Dữ liệu GIẢ.
/// </summary>
public sealed class DuplicateIdRepairTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-dup-" + Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_dir, "data.db");
    public DuplicateIdRepairTests() { Directory.CreateDirectory(_dir); AppDatabase.Initialize(Db); }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private void Insert(string key, string id, string product)
    {
        var o = new Order { Id = id, OrderId = "SP-DUP", Track = "SPXDUP1", Platform = "Shopee", OrderType = "Trả hàng/Hoàn tiền", Status = "Hàng đã trả",
            Items = { new OrderItem { Product = product, Qty = 1 } } };
        using var c = new SqliteConnection($"Data Source={Db};Pooling=False"); c.Open();
        using var tx = c.BeginTransaction(); OrderStore.Insert(c, tx, o, key); tx.Commit();
    }

    private List<string> Rows()
    {
        using var c = new SqliteConnection($"Data Source={Db};Pooling=False"); c.Open();
        using var q = c.CreateCommand(); q.CommandText = "SELECT source_key || '=' || id FROM orders ORDER BY rowid";
        using var r = q.ExecuteReader(); var l = new List<string>(); while (r.Read()) l.Add(r.GetString(0)); return l;
    }

    [Fact]
    public void Legacy_second_row_takes_the_cloud_id_and_the_extra_cloud_copy_goes_away()
    {
        Insert("Archived|SHOPEE|SP-DUP|#1", "X", "Áo");
        Insert("Archived|SHOPEE|SP-DUP|#2", "X", "Quần");
        Insert("cloud:X~2", "X~2", "Quần");
        Assert.Equal(1, CloudReplica.RepairDuplicateIds(Db));
        Assert.Equal(new[] { "Archived|SHOPEE|SP-DUP|#1=X", "Archived|SHOPEE|SP-DUP|#2=X~2" }, Rows());
        Assert.Equal(0, CloudReplica.RepairDuplicateIds(Db));     // chạy lại không đổi gì
    }

    [Fact]
    public void Without_the_matching_cloud_rows_nothing_is_guessed()
    {
        Insert("Archived|SHOPEE|SP-DUP|#1", "X", "Áo");
        Insert("Archived|SHOPEE|SP-DUP|#2", "X", "Quần");
        Assert.Equal(0, CloudReplica.RepairDuplicateIds(Db));
        Assert.Equal(2, Rows().Count);
    }

    [Fact]
    public void Ai_order_tools_do_not_crash_on_duplicate_ids()
    {
        Insert("Main|SHOPEE|SP-DUP|#1", "X", "Áo");
        Insert("Main|SHOPEE|SP-DUP|#2", "X", "Quần");
        var tools = new AiOrderTools(() => OrderRepository.Load(Db).ToList());
        var r = tools.Execute("list_orders", "{}");
        Assert.DoesNotContain("duplicate", r.Json, StringComparison.OrdinalIgnoreCase);
    }
}
