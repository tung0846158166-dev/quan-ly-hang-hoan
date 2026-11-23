using System.Text.Json;

namespace QuanLyHangHoan.Data.Legacy;

/// <summary>
/// Chạy thử nhập dữ liệu cũ: CHỈ ĐỌC, không ghi gì (kể cả vào thư mục nguồn). Báo số lượng, trùng, lỗi, cảnh báo.
/// </summary>
public static class LegacyDryRun
{
    public const string MainFile = "du_lieu_hang_hoan.json";
    public const string AuditLogFile = "phu_shopee_excel_audit_log_v1.json";

    private static readonly Dictionary<LegacyCategory, string> Files = new()
    {
        [LegacyCategory.Main] = MainFile,
        [LegacyCategory.Archived] = "phu_shopee_archived_orders_v1.json",
        [LegacyCategory.Deleted] = "phu_shopee_deleted_orders_v1.json",
    };

    /// <summary>Tệp cố ý KHÔNG nhập theo quyết định của chủ dự án (2026-11-23).</summary>
    private static readonly Dictionary<string, string> IgnoredByDecision = new(StringComparer.OrdinalIgnoreCase)
    {
        ["phu_shopee_cancelled_orders_v1.json"] = "bảng đơn hủy riêng: không dùng nữa",
        ["khoi_phuc_don_da_nhan.json"] = "tệp khôi phục: app bản mới đã sửa lỗi mất file, lấy dữ liệu từ app đang chạy",
    };

    public static DryRunReport Analyze(string folder)
    {
        var report = new DryRunReport { Folder = folder };

        if (!Directory.Exists(folder))
        {
            report.Errors.Add(new RowError(folder, -1, "Không tìm thấy thư mục dữ liệu cũ."));
            return report;
        }

        if (!File.Exists(Path.Combine(folder, MainFile)))
            report.Errors.Add(new RowError(MainFile, -1, "Không tìm thấy tệp đơn chính."));

        var seen = new Dictionary<string, List<string>>();

        foreach (var category in Files.Keys)
        {
            var count = report.For(category);
            foreach (var item in Enumerate(folder, category))
            {
                count.Read++;
                if (item.Error is not null)
                {
                    count.Failed++;
                    report.Errors.Add(new RowError(Files[category], item.Index, item.Error));
                    continue;
                }

                var rec = item.Record!;
                count.Mapped++;
                if (item.InlinePhoto) report.InlinePhotos++;
                if (item.PlatformRenamed) report.PlatformRenamed++;
                if (rec.Order.PhotoId.Length > 0) report.PhotoIdRefs++;

                if (!seen.TryGetValue(rec.DedupKey, out var where)) seen[rec.DedupKey] = where = new();
                where.Add($"{Files[category]}#{rec.Index}");
            }
        }

        foreach (var kv in seen.Where(k => k.Value.Count > 1))
            report.Duplicates.Add(new DuplicateGroup(kv.Key, kv.Value));

        ScanOtherFiles(folder, report);
        return report;
    }

    /// <summary>Nạp đầy đủ bản ghi của một nhóm (dùng cho nhập thật và test).</summary>
    public static List<LegacyRecord> LoadOrders(string folder, LegacyCategory category) =>
        Enumerate(folder, category).Where(i => i.Record is not null).Select(i => i.Record!).ToList();

    private sealed record Item(int Index, LegacyRecord? Record, string? Error, bool InlinePhoto, bool PlatformRenamed);

    private static IEnumerable<Item> Enumerate(string folder, LegacyCategory category)
    {
        var file = Files[category];
        var (kind, doc, _) = LegacyJson.Read(Path.Combine(folder, file));
        if (kind != LegacyJson.Kind.Json || doc is null) yield break;

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) yield break;

            var index = 0;
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                LegacyRecord? rec = null;
                string? err = null;
                var renamed = false;
                try
                {
                    rec = new LegacyRecord
                    {
                        Category = category, File = file, Index = index,
                        Order = LegacyMapper.Map(row, category), RawJson = row.GetRawText(),
                    };
                    renamed = row.TryGetProperty("platform", out var p) && p.ValueKind == JsonValueKind.String
                              && p.GetString() != rec.Order.Platform;
                }
                catch (Exception ex) { err = ex.Message; }

                yield return new Item(index, rec, err, err is null && LegacyMapper.HasInlinePhoto(row), renamed);
                index++;
            }
        }
    }

    private static void ScanOtherFiles(string folder, DryRunReport report)
    {
        var handled = new HashSet<string>(Files.Values, StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.GetFiles(folder, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(path);
            if (handled.Contains(name)) continue;

            if (IgnoredByDecision.TryGetValue(name, out var reason))
            {
                report.SkippedByDecision.Add($"{name} ({reason})");
                continue;
            }

            var (kind, doc, _) = LegacyJson.Read(path);
            using (doc)
            {
                if (kind == LegacyJson.Kind.Verbatim) { report.VerbatimFiles.Add(name); continue; }

                if (name.Equals(AuditLogFile, StringComparison.OrdinalIgnoreCase) && doc?.RootElement.ValueKind == JsonValueKind.Array)
                    report.AuditLogEntries = doc.RootElement.GetArrayLength();
            }

            report.UnhandledFiles.Add(name);
        }
    }
}
