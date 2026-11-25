using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Cloud;

namespace QuanLyHangHoan.Tests;

/// <summary>Máy tính ở chế độ cloud xem ảnh: trong máy trước, rồi bộ nhớ đệm, rồi tải từ cloud (lưu đệm ở bảng RIÊNG, không lẫn vào bảng ảnh gốc).</summary>
public sealed class CloudPhotoTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-cloudphoto-" + Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_dir, "data.db");
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9 };

    public CloudPhotoTests()
    {
        Directory.CreateDirectory(_dir);
        using var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Db};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "CREATE TABLE photos(photo_id TEXT PRIMARY KEY, source TEXT NOT NULL, mime TEXT NOT NULL, data BLOB, sha256 TEXT, inline_differs INTEGER NOT NULL DEFAULT 0)";
        cmd.ExecuteNonQuery();
    }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private sealed class Handler(Func<HttpRequestMessage, (HttpStatusCode, byte[])> reply) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            var (code, body) = reply(r);
            return Task.FromResult(new HttpResponseMessage(code) { Content = new ByteArrayContent(body) });
        }
    }

    [Fact]
    public async Task Photo_missing_locally_is_downloaded_once_cached_separately_and_mime_detected()
    {
        var h = new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("/photos/legacy_abc") ? (HttpStatusCode.OK, Png) : (HttpStatusCode.NotFound, Encoding.UTF8.GetBytes("{\"error\":{\"code\":\"not_found\",\"message\":\"x\"}}")));
        var api = new CloudApi(new HttpClient(h), "https://x.workers.dev", "tok");
        var p = new CloudPhotoProvider(Db, () => api);

        var a = await p.GetPhotoAsync("legacy_abc");
        Assert.NotNull(a);
        Assert.Equal("image/png", a!.Mime);
        Assert.Equal(Png, a.Bytes);
        Assert.Equal("cloud", a.Source);
        var b = await p.GetPhotoAsync("legacy_abc");             // lần hai: lấy từ bộ nhớ đệm, không gọi mạng
        Assert.Equal(Png, b!.Bytes);
        Assert.Equal(1, h.Calls);
        Assert.Null(PhotoRepository.Get(Db, "legacy_abc"));       // KHÔNG ghi vào bảng ảnh gốc (tránh bị đẩy ngược lên cloud)

        Assert.Null(await p.GetPhotoAsync("khong_co"));          // cloud không có: null, không lỗi
    }

    [Fact]
    public async Task Offline_or_unpaired_returns_null_without_throwing()
    {
        var p = new CloudPhotoProvider(Path.Combine(_dir, "may-moi.db"), () => null);   // máy mới: chưa có bảng ảnh gốc
        Assert.Null(await p.GetPhotoAsync("legacy_abc"));
        var down = new CloudApi(new HttpClient(new Handler(_ => throw new HttpRequestException("mất mạng"))), "https://x.workers.dev", "tok");
        Assert.Null(await new CloudPhotoProvider(Db, () => down).GetPhotoAsync("legacy_abc"));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    [InlineData(new byte[] { 1, 2, 3 }, "image/jpeg")]
    public void Mime_is_detected_from_bytes(byte[] bytes, string mime) => Assert.Equal(mime, CloudPhotoCache.DetectMime(bytes));

    [Fact]
    public void Cache_prunes_entries_older_than_the_retention()
    {
        CloudPhotoCache.Put(Db, "old", "image/jpeg", Jpeg, DateTimeOffset.UtcNow.AddDays(-100));
        CloudPhotoCache.Put(Db, "new", "image/jpeg", Jpeg, DateTimeOffset.UtcNow);
        Assert.Equal(1, CloudPhotoCache.Prune(Db, TimeSpan.FromDays(90)));
        Assert.Null(CloudPhotoCache.Get(Db, "old"));
        Assert.NotNull(CloudPhotoCache.Get(Db, "new"));
    }
}
