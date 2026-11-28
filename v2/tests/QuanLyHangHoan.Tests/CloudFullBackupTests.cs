using System.IO;
using System.Text.Json.Nodes;
using QuanLyHangHoan.Data.Cloud;

namespace QuanLyHangHoan.Tests;

/// <summary>Sao lưu toàn bộ cloud về máy mỗi ngày (chủ dự án 28/11/2026). Cloud GIẢ trong bộ nhớ.</summary>
public sealed class CloudFullBackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "qlhh-cbk-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private int _blobCalls;

    private Task<(JsonArray, bool)> Export(string table, int offset, CancellationToken _)
    {
        // parcels: 3 dòng, trả từng trang 2 dòng; photos: 1 ảnh còn, 1 ảnh đã xóa
        if (table == "parcels")
        {
            var all = new[] { "a", "b", "c" };
            var page = new JsonArray(all.Skip(offset).Take(2).Select(id => (JsonNode)new JsonObject { ["id"] = id }).ToArray());
            return Task.FromResult((page, offset + 2 < all.Length));
        }
        if (table == "photos")
            return Task.FromResult((new JsonArray(new JsonObject { ["photo_id"] = "ph1", ["deleted_at"] = 0 }, new JsonObject { ["photo_id"] = "ph2", ["deleted_at"] = 5 }), false));
        return Task.FromResult((new JsonArray(), false));
    }

    private Task<byte[]?> Blob(string key, CancellationToken _) { _blobCalls++; return Task.FromResult<byte[]?>(key.StartsWith("t/") ? null : new byte[] { 1, 2, 3 }); }
    private static Task<JsonObject> Counts(CancellationToken _) => Task.FromResult(new JsonObject { ["parcels"] = 3 });

    [Fact]
    public async Task Backup_writes_every_table_in_transfer_format_downloads_photos_once_and_keeps_seven_days()
    {
        var day1 = new DateTime(2026, 11, 28, 2, 0, 0);
        Assert.False(CloudFullBackup.DoneFor(_root, day1));
        var r = await CloudFullBackup.RunAsync(_root, day1, Export, Blob, Counts);
        Assert.True(CloudFullBackup.DoneFor(_root, day1));
        Assert.Equal(5, r.Rows);                                                    // 3 kiện + 2 dòng ảnh
        Assert.Equal(1, r.Photos);                                                  // ảnh nhỏ không có, ảnh đã xóa bỏ qua
        Assert.Equal(3, JsonNode.Parse(File.ReadAllText(Path.Combine(r.Folder, "tables", "parcels.json")))!.AsArray().Count);
        foreach (var t in CloudFullBackup.Tables) Assert.True(File.Exists(Path.Combine(r.Folder, "tables", t + ".json")), t);
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(r.Folder, "manifest.json")))!;
        Assert.Equal("../blobs", manifest["blobDir"]!.GetValue<string>());
        Assert.Equal(3, manifest["counts"]!["parcels"]!.GetValue<int>());
        Assert.True(File.Exists(CloudFullBackup.BlobFile(_root, "p/ph1")));

        // ngày sau: ảnh đã có thì không tải lại
        var calls = _blobCalls;
        var r2 = await CloudFullBackup.RunAsync(_root, day1.AddDays(1), Export, Blob, Counts);
        Assert.Equal(0, r2.NewPhotos);
        Assert.Equal(calls + 1, _blobCalls);                                        // chỉ hỏi lại ảnh nhỏ (không có)

        // giữ 7 ngày
        for (var i = 2; i < 10; i++) await CloudFullBackup.RunAsync(_root, day1.AddDays(i), Export, Blob, Counts);
        var days = Directory.GetDirectories(_root).Select(Path.GetFileName).Where(n => n!.Length == 8).ToList();
        Assert.Equal(7, days.Count);
        Assert.DoesNotContain("20261128", days);
        Assert.True(File.Exists(CloudFullBackup.BlobFile(_root, "p/ph1")));         // ảnh còn dùng: giữ
    }
}
