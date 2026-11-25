using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace QuanLyHangHoan.Data.Cloud;

public sealed record MigrationReport(
    int Orders, int Active, int Archived, int Deleted, long ParcelBytes,
    int RowsWithRawJson, long RawJsonChars, int RowsWithBuyerNote, long ExtraJsonChars,
    IReadOnlyList<(string Key, int Rows, long Chars)> DroppedExtraKeys,
    IReadOnlyList<(string Key, int Rows, long Chars)> DroppedRawDetailsKeys,
    int Photos, long PhotoBytes, int OrdersPointingToMissingPhoto,
    int HistoryPhotos, int HistoryPhotosAlsoInTable, int HistoryPhotosUnique, long HistoryUniqueBytes,
    int ReturnWatchMarks, int ReturnWatchOff, int ParcelsWithoutCode, int DuplicateIds, int DuplicatesSkippedIdentical = 0, int DuplicatesRenamed = 0)
{
    public string ToText()
    {
        var sb = new StringBuilder();
        string Mb(long b) => (b / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        sb.AppendLine("=== BÁO CÁO CHẠY THỬ (không ghi gì lên mạng, không in nội dung đơn) ===");
        sb.AppendLine($"Đơn: {Orders:N0} (đang dùng {Active:N0}, lưu trữ {Archived:N0}, đã xóa {Deleted:N0})");
        sb.AppendLine($"Dữ liệu gọn sẽ lên cloud: {Mb(ParcelBytes)} (D1/Durable Object hạn mức 500 MB lưu trữ).");
        sb.AppendLine($"Kiện không có mã nào (khó tra/quét): {ParcelsWithoutCode:N0}");
        sb.AppendLine($"Dòng TRÙNG khóa id trong dữ liệu cũ: {DuplicateIds:N0} (hệt nhau, bỏ qua bản sao: {DuplicatesSkippedIdentical:N0}; khác nhau, giữ cả hai với id hậu tố ~2: {DuplicatesRenamed:N0})");
        sb.AppendLine("--- KHÔNG đưa lên cloud ---");
        sb.AppendLine($"raw_json (bản sao nguyên văn): {RowsWithRawJson:N0} dòng, {Mb(RawJsonChars)}");
        sb.AppendLine($"Ghi chú người mua (buyer_note) khác rỗng: {RowsWithBuyerNote:N0} dòng");
        sb.AppendLine($"extra_json tổng: {Mb(ExtraJsonChars)}; các khóa bị bỏ (tên khóa, số dòng, dung lượng):");
        foreach (var k in DroppedExtraKeys) sb.AppendLine($"  - {k.Key}: {k.Rows:N0} dòng, {Mb(k.Chars)}");
        sb.AppendLine("rawDetails: các khóa bị bỏ:");
        foreach (var k in DroppedRawDetailsKeys) sb.AppendLine($"  - {k.Key}: {k.Rows:N0} dòng, {Mb(k.Chars)}");
        sb.AppendLine("--- ẢNH ---");
        sb.AppendLine($"Bảng photos: {Photos:N0} ảnh, {Mb(PhotoBytes)}; đơn trỏ tới ảnh không có trong bảng: {OrdersPointingToMissingPhoto:N0}");
        sb.AppendLine($"Ảnh nhúng trong photoHistory: {HistoryPhotos:N0} (đã có trong bảng photos: {HistoryPhotosAlsoInTable:N0}; DUY NHẤT, sẽ MẤT nếu bỏ: {HistoryPhotosUnique:N0}, {Mb(HistoryUniqueBytes)})");
        sb.AppendLine("--- BẢNG HÀNG HOÀN VỀ ---");
        sb.AppendLine($"Dấu: {ReturnWatchMarks:N0} (đã bị bỏ khỏi bảng: {ReturnWatchOff:N0})");
        return sb.ToString();
    }
}

public sealed record PhotoMigrationOptions(bool IncludeHistory = false, bool KeepAsNew = false, int RetentionDays = 90, IProgress<string>? Progress = null);
public sealed record PhotoMigrationResult(int Uploaded, int SkippedExpired, int SkippedTooBig, int SkippedNoParcel, int Failed);

/// <summary>
/// Chuyển dữ liệu cũ lên cloud. CHẠY THỬ (<see cref="DryRun"/>) chỉ đọc CSDL ở chế độ chỉ-đọc, không bao giờ mở mạng và không in nội dung đơn
/// (chỉ đếm, tên khóa, dung lượng). Ảnh được chuyển bằng <see cref="UploadPhotosAsync"/> qua đường "ảnh cũ" (chỉ chủ cửa hàng).
/// </summary>
public static class CloudMigration
{
    private static readonly HashSet<string> KeptTop = new() { "forwardCarrier", "returnCarrier", "priceNeedsReview", "priceHasValue" };
    private static readonly HashSet<string> KeptRaw = new() { "returnReason", "returnStatus", "returnShippingStatus", "returnSuccessTime", "cancelType" };

    private static SqliteConnection OpenRead(string dbPath)
    {
        var c = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        c.Open();
        return c;
    }

    /// <summary>Mọi kiện dạng cloud (không đụng bảng cloud_published; không ghi gì vào CSDL nguồn).</summary>
    public static List<JsonObject> AllParcels(string dbPath)
    {
        var list = ParcelMapper.ToParcels(OrderRepository.Load(dbPath), out _, out _);
        foreach (var p in list) p["hash"] = ParcelMapper.Hash(p);
        return list;
    }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    /// <summary>Giải mã "data:image/jpeg;base64,...." thành (mime, byte); null nếu không đúng dạng.</summary>
    public static (string Mime, byte[] Bytes)? DecodeDataUri(string? uri)
    {
        if (string.IsNullOrEmpty(uri) || !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
        var comma = uri.IndexOf(',');
        if (comma < 0) return null;
        var head = uri[5..comma];
        if (!head.EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) return null;
        var mime = head[..^7].ToLowerInvariant();
        if (mime is not ("image/jpeg" or "image/png" or "image/webp")) return null;
        try { return (mime, Convert.FromBase64String(uri[(comma + 1)..])); } catch (FormatException) { return null; }
    }

    public static MigrationReport DryRun(string dbPath, string? outDir = null)
    {
        if (!File.Exists(dbPath)) throw new FileNotFoundException("Không thấy cơ sở dữ liệu.", dbPath);
        var all = OrderRepository.Load(dbPath);
        var parcels = ParcelMapper.ToParcels(all, out var skippedIdentical, out var renamed);
        long parcelBytes = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int dup = 0, noCode = 0;
        StreamWriter? nd = null;
        if (outDir is not null)
        {
            Directory.CreateDirectory(outDir);
            nd = new StreamWriter(Path.Combine(outDir, "parcels.ndjson"), false, new UTF8Encoding(false));
        }
        foreach (var p in parcels)
        {
            var text = p.ToJsonString();
            parcelBytes += Encoding.UTF8.GetByteCount(text) + 1;
            nd?.WriteLine(text);
            if (!seen.Add(p["id"]!.GetValue<string>())) dup++;
            if (string.IsNullOrWhiteSpace(p["track"]?.GetValue<string>()) && string.IsNullOrWhiteSpace(p["returnTrack"]?.GetValue<string>()) && string.IsNullOrWhiteSpace(p["orderId"]?.GetValue<string>())) noCode++;
        }
        nd?.Dispose();

        using var c = OpenRead(dbPath);
        long rawChars = 0, extraChars = 0; int rawRows = 0, buyerRows = 0;
        var topKeys = new Dictionary<string, (int Rows, long Chars)>();
        var rawKeys = new Dictionary<string, (int Rows, long Chars)>();
        var historyShas = new List<(string Sha, int Bytes, string OrderId, string CaptureId)>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT id, raw_json, buyer_note, extra_json FROM orders";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var raw = r.GetString(1);
                if (raw.Length > 0) { rawRows++; rawChars += raw.Length; }
                if (r.GetString(2).Trim().Length > 0) buyerRows++;
                var extra = r.GetString(3);
                extraChars += extra.Length;
                if (extra.Length < 3) continue;
                try
                {
                    using var doc = JsonDocument.Parse(extra);
                    foreach (var pr in doc.RootElement.EnumerateObject())
                    {
                        var len = pr.Value.GetRawText().Length;
                        if (!KeptTop.Contains(pr.Name))
                        {
                            if (pr.Name == "rawDetails" && pr.Value.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var q in pr.Value.EnumerateObject())
                                    if (!KeptRaw.Contains(q.Name))
                                        rawKeys[q.Name] = rawKeys.TryGetValue(q.Name, out var w) ? (w.Rows + 1, w.Chars + q.Value.GetRawText().Length) : (1, q.Value.GetRawText().Length);
                                continue;
                            }
                            topKeys[pr.Name] = topKeys.TryGetValue(pr.Name, out var v) ? (v.Rows + 1, v.Chars + len) : (1, len);
                        }
                        if (pr.Name == "photoHistory" && pr.Value.ValueKind == JsonValueKind.Array)
                            foreach (var e in pr.Value.EnumerateArray())
                            {
                                if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("photo", out var ph) || ph.ValueKind != JsonValueKind.String) continue;
                                var d = DecodeDataUri(ph.GetString());
                                if (d is null) continue;
                                historyShas.Add((Sha(d.Value.Bytes), d.Value.Bytes.Length, r.GetString(0),
                                    e.TryGetProperty("captureId", out var cid) && cid.ValueKind == JsonValueKind.String ? cid.GetString()! : ""));
                            }
                    }
                }
                catch (JsonException) { /* extra_json hỏng: bỏ qua, không đoán */ }
            }
        }

        int photos = 0; long photoBytes = 0; int missing = 0;
        var tableShas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*), COALESCE(SUM(length(data)),0) FROM photos";
            using var r = cmd.ExecuteReader();
            if (r.Read()) { photos = r.GetInt32(0); photoBytes = r.GetInt64(1); }
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT sha256 FROM photos WHERE sha256 <> ''";
            using var r = cmd.ExecuteReader();
            while (r.Read()) tableShas.Add(r.GetString(0));
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM orders o WHERE o.photo_id <> '' AND NOT EXISTS (SELECT 1 FROM photos p WHERE p.photo_id = o.photo_id)";
            missing = Convert.ToInt32(cmd.ExecuteScalar());
        }
        var unique = historyShas.Where(h => !tableShas.Contains(h.Sha)).GroupBy(h => h.Sha).Select(g => g.First()).ToList();
        if (outDir is not null)
            File.WriteAllLines(Path.Combine(outDir, "photos-unique.txt"), unique.Select(u => $"{u.OrderId}|{u.CaptureId}|{u.Sha[..16]}|{u.Bytes}"), new UTF8Encoding(false));

        int marks = 0, off = 0;
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='return_watch'";
            if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
            {
                cmd.CommandText = "SELECT COUNT(*), COALESCE(SUM(off),0) FROM return_watch";
                using var r = cmd.ExecuteReader();
                if (r.Read()) { marks = r.GetInt32(0); off = Convert.ToInt32(r.GetValue(1)); }
            }
        }

        var report = new MigrationReport(all.Count, all.Count(s => !s.IsArchived && !s.IsDeleted), all.Count(s => s.IsArchived && !s.IsDeleted), all.Count(s => s.IsDeleted),
            parcelBytes, rawRows, rawChars, buyerRows, extraChars,
            topKeys.OrderByDescending(k => k.Value.Chars).Select(k => (k.Key, k.Value.Rows, k.Value.Chars)).ToList(),
            rawKeys.OrderByDescending(k => k.Value.Chars).Select(k => (k.Key, k.Value.Rows, k.Value.Chars)).ToList(),
            photos, photoBytes, missing, historyShas.Count, historyShas.Count(h => tableShas.Contains(h.Sha)), unique.Count, unique.Sum(u => (long)u.Bytes),
            marks, off, noCode, skippedIdentical + renamed, skippedIdentical, renamed);
        if (outDir is not null) File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToText(), new UTF8Encoding(false));
        return report;
    }

    // ---------------------------------------------------------------- ảnh
    private static long? TakenAtMs(string recvDate, string importedAt)
    {
        foreach (var s in new[] { recvDate, importedAt })
        {
            if (DateTime.TryParseExact((s ?? "").Trim(), new[] { "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Unspecified), TimeSpan.FromHours(7)).ToUnixTimeMilliseconds();
        }
        return null;
    }

    public static string LegacyPhotoId(string sha) => "legacy_" + sha[..Math.Min(32, sha.Length)].ToLowerInvariant();

    /// <summary>
    /// Chuyển ảnh cũ lên cloud (cần token CHỦ). Ảnh quá hạn lưu (mặc định 90 ngày theo ngày nhận/nạp của đơn) bị bỏ qua vì cloud sẽ tự xóa ngay,
    /// trừ khi <see cref="PhotoMigrationOptions.KeepAsNew"/> (coi như mới chụp, giữ 90 ngày kể từ hôm nay). Dừng ngay khi 401/403/429.
    /// </summary>
    public static async Task<PhotoMigrationResult> UploadPhotosAsync(CloudApi api, string dbPath, PhotoMigrationOptions? options = null, CancellationToken ct = default)
    {
        options ??= new();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-options.RetentionDays).ToUnixTimeMilliseconds();
        int up = 0, expired = 0, big = 0, noParcel = 0, failed = 0;
        using var c = OpenRead(dbPath);

        async Task SendAsync(string photoId, string parcelId, string mime, byte[] bytes, long? takenAt)
        {
            if (bytes.Length == 0 || bytes.Length > 1_000_000) { big++; return; }
            if (!options.KeepAsNew && takenAt is { } t && t < cutoff) { expired++; return; }
            try
            {
                await api.PutPhotoAsync(photoId, parcelId, mime, thumb: false, bytes, ct, legacy: true, takenAtMs: options.KeepAsNew ? null : takenAt).ConfigureAwait(false);
                up++;
                if (up % 100 == 0) options.Progress?.Report($"Đã chuyển {up:N0} ảnh...");
            }
            catch (CloudApiException ex) when (ex.Status is 401 or 403 or 429) { throw; }
            catch (CloudApiException ex) when (ex.Code == "not_found") { noParcel++; }
            catch (CloudApiException ex) when (ex.Code == "expired") { expired++; }
            catch (CloudApiException) { failed++; }
        }

        var tableShas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<(string Pid, string OrderId, string Mime, byte[] Data, string Sha, string Recv, string Imported)>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT p.photo_id, o.id, p.mime, p.data, p.sha256, o.recv_date, o.imported_at FROM photos p JOIN orders o ON o.photo_id = p.photo_id WHERE p.data IS NOT NULL";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                rows.Add((r.GetString(0), r.GetString(1), r.GetString(2), (byte[])r.GetValue(3), r.GetString(4), r.GetString(5), r.GetString(6)));
        }
        foreach (var x in rows)
        {
            ct.ThrowIfCancellationRequested();
            var sha = x.Sha.Length > 0 ? x.Sha : Sha(x.Data);
            tableShas.Add(sha);
            await SendAsync(LegacyPhotoId(sha), x.OrderId, string.IsNullOrEmpty(x.Mime) ? "image/jpeg" : x.Mime, x.Data, TakenAtMs(x.Recv, x.Imported)).ConfigureAwait(false);
        }

        if (options.IncludeHistory)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT id, extra_json, recv_date, imported_at FROM orders WHERE extra_json LIKE '%photoHistory%'";
            var hist = new List<(string Id, string Extra, string Recv, string Imp)>();
            using (var r = cmd.ExecuteReader()) while (r.Read()) hist.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));
            var sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in hist)
            {
                JsonDocument doc;
                try { doc = JsonDocument.Parse(h.Extra); } catch (JsonException) { continue; }
                using (doc)
                {
                    if (!doc.RootElement.TryGetProperty("photoHistory", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
                    foreach (var e in arr.EnumerateArray())
                    {
                        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("photo", out var ph) || ph.ValueKind != JsonValueKind.String) continue;
                        var d = DecodeDataUri(ph.GetString());
                        if (d is null) continue;
                        var sha = Sha(d.Value.Bytes);
                        if (tableShas.Contains(sha) || !sent.Add(sha)) continue;      // đã có trong bảng photos / đã gửi: không nhân đôi
                        long? takenAt = e.TryGetProperty("captureTime", out var ct2) && ct2.ValueKind == JsonValueKind.Number && ct2.TryGetInt64(out var ms) && ms > 0 ? ms : TakenAtMs(h.Recv, h.Imp);
                        await SendAsync(LegacyPhotoId(sha), h.Id, d.Value.Mime, d.Value.Bytes, takenAt).ConfigureAwait(false);
                    }
                }
            }
        }
        return new(up, expired, big, noParcel, failed);
    }
}
