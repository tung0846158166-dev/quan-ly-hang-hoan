using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuanLyHangHoan.Data.Cloud;

/// <summary>
/// SAO LƯU TOÀN BỘ CLOUD VỀ MÁY (chủ dự án 28/11/2026, mục 10): cloud là nguồn gốc duy nhất, bản sao SQLite trên máy không có nhật ký
/// thao tác của cloud và ảnh trong kho ảnh. Mỗi ngày một lần, máy tính chủ shop đọc MỌI bảng (`/admin/transfer/export`) và MỌI ảnh còn hiệu lực
/// (`/admin/transfer/blob`), ghi theo ĐÚNG định dạng của cloud/worker/scripts/cloud-transfer.mjs (tables/*.json + manifest.json) để khôi phục
/// bằng chính công cụ đó (`restore --dir <thư mục ngày>`). Ảnh dùng chung một thư mục `blobs` (chỉ tải ảnh mới), mỗi ngày giữ một thư mục, tối đa
/// <see cref="KeepDays"/> ngày. Chỉ ĐỌC từ cloud, không ghi gì lên cloud.
/// </summary>
public static class CloudFullBackup
{
    public static readonly string[] Tables = ["parcels", "parcel_codes", "return_watch", "audit_log", "scans", "photos", "import_state", "ai_digest", "import_log", "import_issues", "devices", "meta"];
    public const int KeepDays = 7;

    public sealed record Result(string Folder, int Rows, int Photos, int NewPhotos);

    /// <summary>Thư mục của ngày (yyyyMMdd) trong <paramref name="root"/>.</summary>
    public static string DayFolder(string root, DateTime day) => Path.Combine(root, day.ToString("yyyyMMdd"));

    /// <summary>Hôm nay đã có bản sao lưu đầy đủ (có manifest) chưa.</summary>
    public static bool DoneFor(string root, DateTime day) => File.Exists(Path.Combine(DayFolder(root, day), "manifest.json"));

    public static string BlobFile(string root, string key) => Path.Combine(root, "blobs", key.Replace("/", "_"));

    /// <summary>Chạy một lần sao lưu. <paramref name="exportPage"/>(bảng, offset) -> (rows, more); <paramref name="getBlob"/>(khóa) -> byte[] hoặc null.</summary>
    public static async Task<Result> RunAsync(string root, DateTime now,
        Func<string, int, CancellationToken, Task<(JsonArray Rows, bool More)>> exportPage,
        Func<string, CancellationToken, Task<byte[]?>> getBlob,
        Func<CancellationToken, Task<JsonObject>> counts,
        CancellationToken ct = default)
    {
        var day = DayFolder(root, now);
        var tmp = day + ".dang-chay";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        Directory.CreateDirectory(Path.Combine(tmp, "tables"));
        Directory.CreateDirectory(Path.Combine(root, "blobs"));

        var tableRows = new JsonObject();
        var total = 0;
        JsonArray photos = new();
        foreach (var t in Tables)
        {
            var rows = new JsonArray();
            for (var offset = 0; ; )
            {
                var (page, more) = await exportPage(t, offset, ct).ConfigureAwait(false);
                foreach (var r in page.ToList()) { page.Remove(r); rows.Add(r); }
                offset = rows.Count;
                if (!more) break;
            }
            await File.WriteAllTextAsync(Path.Combine(tmp, "tables", t + ".json"), rows.ToJsonString(), ct).ConfigureAwait(false);
            tableRows[t] = rows.Count;
            total += rows.Count;
            if (t == "photos") photos = rows;
        }

        var blobs = new JsonObject();
        var newOnes = 0;
        foreach (var p in photos.OfType<JsonObject>())
        {
            if (Num(p["deleted_at"]) != 0) continue;
            var id = p["photo_id"]?.ToString() ?? "";
            if (id.Length == 0) continue;
            foreach (var key in new[] { "p/" + id, "t/" + id })
            {
                var f = BlobFile(root, key);
                byte[]? b = File.Exists(f) ? await File.ReadAllBytesAsync(f, ct).ConfigureAwait(false) : null;
                if (b is null)
                {
                    b = await getBlob(key, ct).ConfigureAwait(false);
                    if (b is null) continue;
                    await File.WriteAllBytesAsync(f + ".tmp", b, ct).ConfigureAwait(false);
                    File.Move(f + ".tmp", f, true);
                    newOnes++;
                }
                blobs[key] = Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
            }
        }

        var manifest = new JsonObject
        {
            ["at"] = now.ToUniversalTime().ToString("o"),
            ["source"] = "sao-luu-hang-ngay-may-tinh",
            ["counts"] = await counts(ct).ConfigureAwait(false),
            ["tableRows"] = tableRows,
            ["blobs"] = blobs,
            // ảnh nằm ở thư mục chung "../blobs": khôi phục bằng cloud-transfer.mjs restore --dir <thư mục ngày> (đọc blobDir)
            ["blobDir"] = "../blobs",
        };
        await File.WriteAllTextAsync(Path.Combine(tmp, "manifest.json"), manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        if (Directory.Exists(day)) Directory.Delete(day, true);
        Directory.Move(tmp, day);
        Prune(root, KeepDays);
        return new Result(day, total, blobs.Count, newOnes);
    }

    /// <summary>Giữ <paramref name="keep"/> thư mục ngày mới nhất; ảnh không còn ở bản nào thì xóa.</summary>
    public static void Prune(string root, int keep)
    {
        var days = Directory.GetDirectories(root).Select(Path.GetFileName).OfType<string>()
            .Where(n => n.Length == 8 && n.All(char.IsDigit)).OrderByDescending(n => n, StringComparer.Ordinal).ToList();
        foreach (var old in days.Skip(keep)) { try { Directory.Delete(Path.Combine(root, old), true); } catch (IOException) { } }
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in days.Take(keep))
        {
            try
            {
                var m = JsonNode.Parse(File.ReadAllText(Path.Combine(root, d, "manifest.json")));
                if (m?["blobs"] is JsonObject b) foreach (var k in b) used.Add(k.Key.Replace("/", "_"));
            }
            catch (Exception e) when (e is IOException or JsonException) { return; }   // đọc không được: KHÔNG xóa ảnh nào
        }
        var blobDir = Path.Combine(root, "blobs");
        if (!Directory.Exists(blobDir)) return;
        foreach (var f in Directory.GetFiles(blobDir))
            if (!used.Contains(Path.GetFileName(f)) && !f.EndsWith(".tmp", StringComparison.Ordinal)) { try { File.Delete(f); } catch (IOException) { } }
    }

    private static long Num(JsonNode? n) => n is JsonValue v && long.TryParse(v.ToString(), out var l) ? l : 0;
}
