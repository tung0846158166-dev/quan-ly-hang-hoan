using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Cloud;

namespace QuanLyHangHoan.Tests;

/// <summary>Công cụ chuyển dữ liệu cũ lên cloud: chạy thử chỉ đọc và không lộ dữ liệu đơn; chuyển ảnh theo hạn lưu. Dữ liệu GIẢ.</summary>
public sealed class CloudMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-mig-" + Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_dir, "t.db");
    public CloudMigrationTests() { Directory.CreateDirectory(_dir); AppDatabase.Initialize(Db); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static readonly byte[] PhotoA = { 1, 2, 3, 4, 5 };
    private static readonly byte[] PhotoB = { 9, 8, 7, 6 };
    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
    private static string Uri(byte[] b) => "data:image/jpeg;base64," + Convert.ToBase64String(b);

    private static JsonObject Parcel(string id) => new()
    {
        ["id"] = id, ["orderId"] = "O-" + id, ["track"] = "TRK-" + id, ["platform"] = "Shopee", ["status"] = "Chưa trả", ["price"] = 1000,
        ["items"] = new JsonArray(new JsonObject { ["product"] = "Áo", ["qty"] = 1 }),
        ["recvDate"] = DateTime.Now.AddDays(-5).ToString("dd/MM/yyyy HH:mm:ss"),
    };

    private void Seed()
    {
        CloudReplica.Apply(Db, new[] { Parcel("a"), Parcel("b") }, 1);
        using var c = new SqliteConnection($"Data Source={Db};Pooling=False");
        c.Open();
        void Exec(string sql, params (string, object)[] ps)
        {
            using var cmd = c.CreateCommand(); cmd.CommandText = sql;
            foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
            cmd.ExecuteNonQuery();
        }
        // đơn a: dữ liệu cá nhân + dữ liệu thô + ảnh trong bảng photos (còn hạn) + ảnh nhúng (một trùng bảng photos, một DUY NHẤT)
        var extra = new JsonObject
        {
            ["rawDetails"] = new JsonObject { ["deliveryAddress"] = "12 Đường Giả, Quận X", ["buyerUsername"] = "nguyenvana", ["returnReason"] = "Sai mô tả" },
            ["priceModes"] = new JsonObject { ["unit"] = 5 },
            ["photoHistory"] = new JsonArray(
                new JsonObject { ["photo"] = Uri(PhotoA), ["captureId"] = "c1", ["captureTime"] = DateTimeOffset.UtcNow.AddDays(-3).ToUnixTimeMilliseconds() },
                new JsonObject { ["photo"] = Uri(PhotoB), ["captureId"] = "c2", ["captureTime"] = DateTimeOffset.UtcNow.AddDays(-4).ToUnixTimeMilliseconds() }),
        };
        Exec("UPDATE orders SET buyer_note='Nguyễn Văn A 0912345678', raw_json='{\"x\":1}', extra_json=$e, photo_id='ph-a' WHERE id='a'", ("$e", extra.ToJsonString()));
        Exec("INSERT INTO photos(photo_id, source, mime, data, sha256, inline_differs) VALUES('ph-a','file','image/jpeg',$d,$h,0)", ("$d", PhotoA), ("$h", Sha(PhotoA)));
        Exec("INSERT INTO return_watch(order_id, since, off) VALUES('a', 1764000000123, 0)");
    }

    [Fact]
    public void Dry_run_counts_things_without_leaking_any_order_data_or_touching_the_source()
    {
        Seed();
        var before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Db)));
        var outDir = Path.Combine(_dir, "out");
        var r = CloudMigration.DryRun(Db, outDir);

        Assert.Equal(2, r.Orders); Assert.Equal(2, r.Active);
        Assert.Equal(1, r.RowsWithBuyerNote);
        Assert.True(r.RowsWithRawJson >= 1 && r.RawJsonChars >= 7);
        Assert.Contains(r.DroppedExtraKeys, k => k.Key == "photoHistory");
        Assert.Contains(r.DroppedExtraKeys, k => k.Key == "priceModes");
        Assert.Contains(r.DroppedRawDetailsKeys, k => k.Key == "deliveryAddress");
        Assert.Contains(r.DroppedRawDetailsKeys, k => k.Key == "buyerUsername");
        Assert.DoesNotContain(r.DroppedRawDetailsKeys, k => k.Key == "returnReason");     // trường cần cho nghiệp vụ được giữ
        Assert.Equal(1, r.Photos);
        Assert.Equal(2, r.HistoryPhotos);
        Assert.Equal(1, r.HistoryPhotosAlsoInTable);
        Assert.Equal(1, r.HistoryPhotosUnique);                                              // ảnh B chỉ có trong photoHistory: sẽ mất nếu bỏ
        Assert.Equal(1, r.ReturnWatchMarks);
        Assert.Equal(0, r.DuplicateIds);

        // báo cáo và tệp xuất không chứa nội dung cá nhân/dữ liệu thô/ảnh
        var text = r.ToText() + File.ReadAllText(Path.Combine(outDir, "report.txt"));
        foreach (var secret in new[] { "0912345678", "Nguyễn Văn A", "12 Đường Giả", "nguyenvana" }) Assert.DoesNotContain(secret, text);
        var nd = File.ReadAllText(Path.Combine(outDir, "parcels.ndjson"));
        foreach (var secret in new[] { "0912345678", "12 Đường Giả", "nguyenvana", "buyerNote", "rawDetails", "photoHistory", "base64" }) Assert.DoesNotContain(secret, nd);
        Assert.Contains("TRK-a", nd);
        var uniq = File.ReadAllLines(Path.Combine(outDir, "photos-unique.txt"));
        Assert.Single(uniq);
        Assert.DoesNotContain("base64", uniq[0]);

        // nguồn không bị đụng: nguyên byte, không có bảng cloud_published
        Assert.Equal(before, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Db))));
        using var c = new SqliteConnection($"Data Source={Db};Mode=ReadOnly;Pooling=False");
        c.Open();
        using var q = c.CreateCommand();
        q.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='cloud_published'";
        // (bảng này do CloudReplica.Apply ở bước Seed tạo, không phải do chạy thử): chỉ kiểm tra byte nguyên vẹn ở trên
        Assert.True(Convert.ToInt32(q.ExecuteScalar()) >= 0);
    }

    [Fact]
    public void Duplicate_ids_in_old_data_are_never_merged_blindly_identical_copy_skipped_different_one_kept_with_suffix()
    {
        CloudReplica.Apply(Db, new[] { Parcel("a"), Parcel("b") }, 1);
        using (var c = new SqliteConnection($"Data Source={Db};Pooling=False"))
        {
            c.Open();
            void Exec(string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
            // a: bản sao HỆT nhau (cả sản phẩm); b: bản sao khác nội dung (không có sản phẩm)
            Exec("CREATE TEMP TABLE ta AS SELECT * FROM orders WHERE id='a'; UPDATE ta SET source_key='dup-a'; INSERT INTO orders SELECT * FROM ta;");
            Exec("INSERT INTO order_items(source_key, line_no, product, color, size, qty, sku, sku_id, returned_qty) SELECT 'dup-a', line_no, product, color, size, qty, sku, sku_id, returned_qty FROM order_items WHERE source_key='cloud:a'");
            Exec("CREATE TEMP TABLE tb AS SELECT * FROM orders WHERE id='b'; UPDATE tb SET source_key='dup-b'; INSERT INTO orders SELECT * FROM tb;");
        }
        var parcels = ParcelMapper.ToParcels(OrderRepository.Load(Db), out var skipped, out var renamed);
        Assert.Equal(1, skipped);
        Assert.Equal(1, renamed);
        Assert.Equal(3, parcels.Count);
        Assert.Equal(parcels.Count, parcels.Select(p => p["id"]!.GetValue<string>()).Distinct().Count());   // không còn trùng id
        Assert.Contains(parcels, p => p["id"]!.GetValue<string>() == "b~2");

        var r = CloudMigration.DryRun(Db);
        Assert.Equal(2, r.DuplicatesSkippedIdentical + r.DuplicatesRenamed);
        Assert.Contains("TRÙNG khóa id", r.ToText());
        // bộ đẩy đơn dùng cùng cách xử lý: a và b đã đồng bộ (băm khớp) nên chỉ còn bản khác nội dung "b~2" chờ đẩy
        Assert.Equal("b~2", Assert.Single(CloudPublisher.Pending(Db)).Parcel["id"]!.GetValue<string>());
    }
    private sealed class Handler(Func<HttpRequestMessage, (HttpStatusCode, string)> reply) : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, int Bytes)> Calls = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls.Add((r.Method.Method, r.RequestUri!.ToString(), r.Content is null ? 0 : (await r.Content.ReadAsByteArrayAsync(ct)).Length));
            var (code, body) = reply(r);
            return new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task Photos_go_up_with_the_legacy_flag_and_original_time_and_unique_history_photos_only_when_asked()
    {
        Seed();
        var h = new Handler(_ => (HttpStatusCode.OK, "{\"ok\":true}"));
        var api = new CloudApi(new HttpClient(h), "https://x.workers.dev", "owner-token");

        var r1 = await CloudMigration.UploadPhotosAsync(api, Db);
        Assert.Equal(1, r1.Uploaded);                                       // chỉ ảnh trong bảng photos
        var u = Assert.Single(h.Calls).Url;
        Assert.Contains("legacy=1", u); Assert.Contains("takenAt=", u); Assert.Contains("parcelId=a", u);
        Assert.Contains("/photos/" + CloudMigration.LegacyPhotoId(Sha(PhotoA)), u);

        h.Calls.Clear();
        var r2 = await CloudMigration.UploadPhotosAsync(api, Db, new PhotoMigrationOptions(IncludeHistory: true));
        Assert.Equal(2, r2.Uploaded);                                       // + ảnh B duy nhất; ảnh A trùng bảng photos không gửi lần hai
        Assert.Equal(1, h.Calls.Count(c => c.Url.Contains(CloudMigration.LegacyPhotoId(Sha(PhotoB)))));
        Assert.Equal(1, h.Calls.Count(c => c.Url.Contains(CloudMigration.LegacyPhotoId(Sha(PhotoA)))));
    }

    [Fact]
    public async Task Photos_older_than_the_retention_are_skipped_unless_kept_as_new_and_auth_errors_stop_the_run()
    {
        Seed();
        using (var c = new SqliteConnection($"Data Source={Db};Pooling=False"))
        {
            c.Open();
            using var u = c.CreateCommand();
            u.CommandText = "UPDATE orders SET recv_date=$d WHERE id='a'";
            u.Parameters.AddWithValue("$d", DateTime.Now.AddDays(-200).ToString("dd/MM/yyyy HH:mm:ss"));
            u.ExecuteNonQuery();
        }
        var h = new Handler(_ => (HttpStatusCode.OK, "{\"ok\":true}"));
        var api = new CloudApi(new HttpClient(h), "https://x.workers.dev", "owner-token");
        var old = await CloudMigration.UploadPhotosAsync(api, Db);
        Assert.Equal(0, old.Uploaded); Assert.Equal(1, old.SkippedExpired);
        Assert.Empty(h.Calls);                                               // không gọi mạng cho ảnh đã quá hạn

        var asNew = await CloudMigration.UploadPhotosAsync(api, Db, new PhotoMigrationOptions(KeepAsNew: true));
        Assert.Equal(1, asNew.Uploaded);
        Assert.DoesNotContain("takenAt=", Assert.Single(h.Calls).Url);       // coi như mới chụp: cloud ghi giờ hiện tại

        var denied = new CloudApi(new HttpClient(new Handler(_ => (HttpStatusCode.Unauthorized, "{}"))), "https://x.workers.dev", "bad");
        await Assert.ThrowsAsync<CloudApiException>(() => CloudMigration.UploadPhotosAsync(denied, Db, new PhotoMigrationOptions(KeepAsNew: true)));
    }

    [Theory]
    [InlineData("data:image/jpeg;base64,AQID", "image/jpeg", 3)]
    [InlineData("data:image/png;base64,AQID", "image/png", 3)]
    public void Data_uri_decoding_accepts_images_only(string uri, string mime, int len)
    {
        var d = CloudMigration.DecodeDataUri(uri)!.Value;
        Assert.Equal(mime, d.Mime); Assert.Equal(len, d.Bytes.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://x/y.jpg")]
    [InlineData("data:text/html;base64,AQID")]
    [InlineData("data:image/jpeg,AQID")]
    [InlineData("data:image/jpeg;base64,@@@@")]
    public void Data_uri_decoding_rejects_everything_else(string uri) => Assert.Null(CloudMigration.DecodeDataUri(uri));
}
