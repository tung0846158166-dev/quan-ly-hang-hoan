using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Data.Import;

namespace QuanLyHangHoan.Data;

public enum FileKind { Unknown, TikTok, Shopee }

public sealed record FileImportItem(string Name, FileKind Kind, int Rows, string? Error);

public sealed class FileImportResult
{
    public bool Ok { get; init; } = true;
    public string Message { get; init; } = "";
    public List<FileImportItem> Files { get; } = new();
    public TikTokImportResult? TikTok { get; set; }
    public TikTokImportResult? Shopee { get; set; }
    public int Added => (TikTok?.Added ?? 0) + (Shopee?.Added ?? 0);
    public int Updated => (TikTok?.Updated ?? 0) + (Shopee?.Updated ?? 0);
    public int Ambiguous => (TikTok?.Ambiguous.Count ?? 0) + (Shopee?.Ambiguous.Count ?? 0);
}

/// <summary>
/// Nạp tay một hay nhiều file Excel/CSV đơn sàn (TikTok / Salework / Shopee): tự nhận diện từng file, bỏ cột cá nhân ngay khi đọc,
/// gộp các file CÙNG loại thành một lô (bỏ dòng trùng giữa các file xuất chồng nhau), mỗi loại nạp trong MỘT giao dịch.
/// File lạ / hỏng được báo trong danh sách, không làm hỏng các file còn lại. Không đọc/ghi/xóa file gốc (nhận byte từ giao diện).
/// </summary>
public static class FileImportService
{
    public static FileKind Detect(IReadOnlyList<Dictionary<string, string>> rows)
    {
        if (rows.Count == 0) return FileKind.Unknown;
        if (TikTokMapper.IsTikTokExport(rows)) return FileKind.TikTok;
        var keys = rows.Take(5).SelectMany(r => r.Keys).Select(LegacyText.Key).ToList();
        // Chỉ file TikTok dùng "Nạp file Excel". File xuất từ Salework (có "Giá Salework" + "Loại đơn hoàn"/"Mã phiếu nhập hoàn") phải đi cùng
        // đường như nạp tự động từ Salework, tránh cùng một đơn bị nhận nhầm là Shopee rồi trùng với dòng Salework tự lấy.
        if (keys.Contains("giasalework") && (keys.Contains("loaidonhoan") || keys.Contains("maphieunhaphoan"))) return FileKind.TikTok;
        return keys.Any(k => k.Contains("madonhang") || k.Contains("orderid") || k.Contains("madon") || k.Contains("mavandon") || k.Contains("trackingnumber") || k.Contains("trackingid"))
            ? FileKind.Shopee : FileKind.Unknown;
    }

    /// <param name="cloud">Chế độ cloud: gửi kiện lên cloud gộp (không gộp vào máy). null = nạp vào máy như cũ.</param>
    public static FileImportResult Import(string dbPath, IReadOnlyList<(string Name, byte[] Bytes)> files, TikTokMapOptions options,
        ImportProductMode mode, DateTime now, QuanLyHangHoan.Data.Cloud.CloudImportSink? cloud = null, string cloudSource = "file")
    {
        var result = new FileImportResult();
        var byKind = new Dictionary<FileKind, List<SourceFile>> { [FileKind.TikTok] = new(), [FileKind.Shopee] = new() };
        foreach (var (name, bytes) in files)
        {
            try
            {
                var rows = SpreadsheetReader.DropPersonalColumns(SpreadsheetReader.Read(name, bytes));
                var kind = Detect(rows);
                result.Files.Add(new FileImportItem(name, kind, rows.Count, kind == FileKind.Unknown ? "Không phải file đơn hàng (không thấy cột mã đơn / mã vận đơn)." : null));
                if (kind != FileKind.Unknown) byKind[kind].Add(new SourceFile(name, rows));
            }
            catch (Exception ex)
            {
                result.Files.Add(new FileImportItem(name, FileKind.Unknown, 0, ex.Message));
            }
        }

        foreach (var (kind, source) in new[] { (FileKind.TikTok, ImportSource.TikTok), (FileKind.Shopee, ImportSource.Shopee) })
        {
            var list = byKind[kind];
            if (list.Count == 0) continue;
            var prep = PlatformImportPrep.Prepare(list);
            var groups = prep.Groups.Select(g => (IReadOnlyList<Dictionary<string, string>>)g).ToList();
            var r = cloud is not null
                ? cloud.Apply(groups, options, mode, source, cloudSource).Import
                : TikTokImportService.ApplyGroups(dbPath, groups, options, mode, now, source);
            if (kind == FileKind.TikTok) result.TikTok = r; else result.Shopee = r;
            if (!r.Ok) return new FileImportResult { Ok = false, Message = r.Message, TikTok = result.TikTok, Shopee = result.Shopee }.WithFiles(result.Files);
        }
        return result;
    }

    private static FileImportResult WithFiles(this FileImportResult r, IEnumerable<FileImportItem> files) { r.Files.AddRange(files); return r; }
}
