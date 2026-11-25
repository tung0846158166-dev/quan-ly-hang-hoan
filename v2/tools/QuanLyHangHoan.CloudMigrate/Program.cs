using QuanLyHangHoan.Data.Cloud;

// Công cụ chuyển dữ liệu cũ lên cloud. Mặc định chỉ CHẠY THỬ (không mạng, không in nội dung đơn).
//   dry-run --db <data.db> [--out <thư mục>]
//   upload  --db <data.db> --url <địa chỉ Worker> [--watch] --yes        (token chủ: biến môi trường QLHH_CLOUD_TOKEN)
//   photos  --db <data.db> --url <địa chỉ Worker> [--include-history] [--keep-as-new] --yes
// Token KHÔNG nhận qua tham số (tránh lọt vào lịch sử lệnh) và không bao giờ được in ra.
static string? Arg(string[] a, string name)
{
    var i = Array.IndexOf(a, name);
    return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
}

static bool Flag(string[] a, string name) => Array.IndexOf(a, name) >= 0;

static void Usage()
{
    Console.WriteLine("Cách dùng:");
    Console.WriteLine("  CloudMigrate dry-run --db <data.db> [--out <thư mục>]");
    Console.WriteLine("  CloudMigrate upload  --db <data.db> --url <Worker> [--watch] --yes");
    Console.WriteLine("  CloudMigrate photos  --db <data.db> --url <Worker> [--include-history] [--keep-as-new] --yes");
    Console.WriteLine("Token chủ cửa hàng đặt trong biến môi trường QLHH_CLOUD_TOKEN.");
}

if (args.Length == 0) { Usage(); return 1; }
var cmd = args[0];
var db = Arg(args, "--db") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuanLyHangHoan", "v2", "data.db");

try
{
    if (cmd == "dry-run")
    {
        var report = CloudMigration.DryRun(db, Arg(args, "--out"));
        Console.WriteLine(report.ToText());
        return 0;
    }

    if (cmd is "upload" or "photos")
    {
        if (!Flag(args, "--yes")) { Console.WriteLine("Thiếu --yes. Hãy chạy 'dry-run' trước rồi thêm --yes để thật sự ghi lên cloud."); return 2; }
        var url = Arg(args, "--url");
        var token = Environment.GetEnvironmentVariable("QLHH_CLOUD_TOKEN");
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(token)) { Console.WriteLine("Cần --url và biến môi trường QLHH_CLOUD_TOKEN."); return 2; }
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var api = new CloudApi(http, url, token);
        var me = await api.MeAsync();
        if (me.Role != "owner") { Console.WriteLine("Token này không phải của chủ cửa hàng."); return 2; }

        if (cmd == "upload")
        {
            var r = await CloudPublisher.PublishAsync(api, db, "file", includeReturnWatch: Flag(args, "--watch"));
            Console.WriteLine($"Cần gửi {r.Candidates:N0}; đã gửi {r.Sent:N0} (mới {r.Added:N0}, cập nhật {r.Updated:N0}, không đổi {r.Unchanged:N0}, bị từ chối {r.Rejected:N0}); dấu Bảng Hàng Hoàn Về thêm {r.ReturnWatchSent:N0}.");
            return r.Rejected > 0 ? 3 : 0;
        }

        var progress = new Progress<string>(Console.WriteLine);
        var pr = await CloudMigration.UploadPhotosAsync(api, db, new PhotoMigrationOptions(Flag(args, "--include-history"), Flag(args, "--keep-as-new"), 90, progress));
        Console.WriteLine($"Ảnh: đã chuyển {pr.Uploaded:N0}; bỏ vì quá hạn lưu {pr.SkippedExpired:N0}; quá lớn/rỗng {pr.SkippedTooBig:N0}; kiện chưa có trên cloud {pr.SkippedNoParcel:N0}; lỗi {pr.Failed:N0}.");
        return pr.Failed > 0 || pr.SkippedNoParcel > 0 ? 3 : 0;
    }

    Usage();
    return 1;
}
catch (CloudApiException ex)
{
    Console.WriteLine("Lỗi cloud: " + ex.Message);
    return 4;
}
catch (Exception ex) when (ex is FileNotFoundException or IOException or Microsoft.Data.Sqlite.SqliteException)
{
    Console.WriteLine("Lỗi đọc cơ sở dữ liệu: " + ex.Message);
    return 4;
}
