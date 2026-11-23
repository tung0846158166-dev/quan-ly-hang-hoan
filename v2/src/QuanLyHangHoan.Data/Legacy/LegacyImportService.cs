using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace QuanLyHangHoan.Data.Legacy;

public sealed record LegacyImportRun(
    bool Ok, string Message, string CopyFolder,
    DryRunReport? DryRun, ImportResult? Import, LegacyImporter.ReconcileReport? Reconcile);

/// <summary>
/// Quy trình nhập an toàn (quyết định chủ dự án 2026-11-23): mỗi lần nhập SAO CHÉP bản mới nhất của thư mục app cũ
/// sang một thư mục MỚI, kiểm hash từng tệp, rồi chỉ đọc từ bản sao. Không bao giờ ghi vào thư mục nguồn.
/// Dry-run có lỗi thì KHÔNG nhập. Không xóa, không ghi đè bản sao cũ.
/// </summary>
public static class LegacyImportService
{
    public static LegacyImportRun Run(string sourceFolder, string workRoot, string dbPath, bool dryRunOnly)
    {
        if (!Directory.Exists(sourceFolder))
            return Fail("Không tìm thấy thư mục dữ liệu cũ.", "");

        string copy;
        try { copy = CopyAndVerify(sourceFolder, workRoot); }
        catch (Exception ex) { return Fail("Sao chép/kiểm hash thất bại: " + ex.Message, ""); }

        DryRunReport dry;
        try { dry = LegacyDryRun.Analyze(copy); }
        catch (Exception ex) { return Fail("Đọc dữ liệu cũ thất bại: " + ex.Message, copy); }

        if (dry.Errors.Count > 0)
            return new(false, $"Dry-run có {dry.Errors.Count} lỗi, không nhập.", copy, dry, null, null);
        if (dryRunOnly)
            return new(true, "Dry-run xong (chưa nhập).", copy, dry, null, null);

        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var import = LegacyImporter.Import(copy, dbPath, Path.Combine(copy, "photos"));
            var rec = LegacyImporter.Reconcile(copy, dbPath);
            return new(rec.AllMatch, rec.AllMatch ? "Nhập xong, số liệu khớp." : "Nhập xong nhưng số liệu KHÔNG khớp, cần kiểm tra.",
                copy, dry, import, rec);
        }
        catch (Exception ex) { return Fail("Nhập thất bại: " + ex.Message, copy); }
    }

    private static LegacyImportRun Fail(string msg, string copy) => new(false, msg, copy, null, null, null);

    /// <summary>Chép tệp *.json ở gốc + thư mục photos sang thư mục mới có dấu thời gian; kiểm SHA256 từng tệp.</summary>
    private static string CopyAndVerify(string source, string workRoot)
    {
        var dest = NewFolder(workRoot);
        var files = Directory.GetFiles(source, "*.json").ToList();
        var photos = Path.Combine(source, "photos");
        if (Directory.Exists(photos)) files.AddRange(Directory.GetFiles(photos, "*", SearchOption.AllDirectories));

        foreach (var f in files)
        {
            var rel = Path.GetRelativePath(source, f);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(f, target, overwrite: false);
            if (!Hash(f).SequenceEqual(Hash(target)))
                throw new IOException("Hash không khớp: " + rel);
        }
        return dest;
    }

    private static string NewFolder(string workRoot)
    {
        Directory.CreateDirectory(workRoot);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
        for (var i = 0; ; i++)
        {
            var p = Path.Combine(workRoot, i == 0 ? stamp : $"{stamp}-{i}");
            if (Directory.Exists(p)) continue;
            Directory.CreateDirectory(p);
            return p;
        }
    }

    private static byte[] Hash(string path)
    {
        using var s = File.OpenRead(path);
        return SHA256.HashData(s);
    }
}
