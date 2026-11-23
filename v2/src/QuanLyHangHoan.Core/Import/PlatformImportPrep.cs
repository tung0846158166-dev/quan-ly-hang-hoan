using System.Text.RegularExpressions;

namespace QuanLyHangHoan.Core.Import;

public sealed record SourceFile(string Name, IReadOnlyList<Dictionary<string, string>> Rows);

public sealed record PreparedImport(List<List<Dictionary<string, string>>> Groups, int DuplicateCount);

/// <summary>
/// Chuẩn bị lô nạp từ một hay nhiều file: preparePlatformImportGroups của app cũ (đối chiếu với mã JS cũ, Golden/prepare.*).
///  - Dòng sản phẩm phụ (không có mã) kế thừa mã đơn / mã yêu cầu hoàn / mã vận đơn và trạng thái của dòng trước.
///  - Dòng GIỐNG HỆT đã có ở file trước bị bỏ (các file xuất chồng nhau theo tuần không làm sản phẩm bị nhân đôi); lặp trong CÙNG
///    một file thì giữ nguyên (có thể là hai chiếc thật).
///  - Các file khác cấu trúc cột (TikTok / Salework...) tách thành các nhóm riêng vì bộ ánh xạ chọn cột một lần cho mỗi nhóm.
/// Mỗi dòng ra có thêm "__sourceFileName" và "__sourceFileId" (ranh giới file cho bộ ánh xạ).
/// </summary>
public static class PlatformImportPrep
{
    private static readonly string[][] Aliases =
    {
        new[] { "orderid", "madonhang", "iddonhang", "madon" },
        new[] { "returnorderid", "returnid", "masokhieunai", "makhieunai", "mayeucautrahang" },
        new[] { "returnlogisticstrackingid", "returntrackingid", "returntracking", "mavandontrahang", "mvdtrahang" },
        new[] { "trackingid", "trackingnumber", "tracking", "mavandon", "mvd" },
        new[] { "productname", "tensanpham", "tensanphamtrensan", "tenhanghoa", "itemname" },
        new[] { "quantity", "qty", "soluong" },
        new[] { "returnquantity", "soluonghoan", "skuquantityofreturn" },
        new[] { "variation", "skuname", "tenphanloai", "tenphanloaitrensan" },
        new[] { "sellersku", "masanphamsalework", "skuphanloai" },
        new[] { "orderstatus", "trangthaidonhang" },
        new[] { "returnstatus", "trangthaihoan", "trangthaitrahanghoantien" },
    };

    private static readonly string[] IdentityKeys = { "orderid", "returnorderid", "returnlogisticstrackingid", "trackingid" };

    private static string Canonical(string key)
    {
        var n = LegacyText.Key(key);
        var g = Aliases.FirstOrDefault(l => l.Contains(n));
        return g is not null ? g[0] : n;
    }

    public static PreparedImport Prepare(IReadOnlyList<SourceFile> files)
    {
        var groups = new Dictionary<string, List<Dictionary<string, string>>>();
        var groupOrder = new List<string>();
        var previousCounts = new Dictionary<string, int>();
        var duplicates = 0;

        for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
        {
            var file = files[fileIndex];
            var counts = new Dictionary<string, int>();
            var inheritedIdentity = new Dictionary<string, string>();
            var inheritedIdentityCells = new List<KeyValuePair<string, string>>();
            var inheritedStatusCells = new List<KeyValuePair<string, string>>();
            var fileRows = new List<Dictionary<string, string>>();

            foreach (var original in file.Rows)
            {
                var entries = original.Where(kv => !kv.Key.StartsWith("__source", StringComparison.Ordinal)).ToList();
                var canonical = new Dictionary<string, string>();
                foreach (var (key, value) in entries) canonical[Canonical(key)] = (value ?? "").Trim();

                var identity = IdentityKeys.Where(k => canonical.TryGetValue(k, out var v) && v.Length > 0).ToDictionary(k => k, k => canonical[k]);
                if (identity.Count > 0)
                {
                    inheritedIdentity = identity;
                    inheritedStatusCells = entries.Where(kv => Regex.IsMatch(Canonical(kv.Key), "status|trangthai") && (kv.Value ?? "").Trim().Length > 0).ToList();
                    inheritedIdentityCells = entries.Where(kv => IdentityKeys.Contains(Canonical(kv.Key)) && (kv.Value ?? "").Trim().Length > 0).ToList();
                }
                else foreach (var kv in inheritedIdentity) canonical[kv.Key] = kv.Value;

                var hasIdentity = inheritedIdentity.Count > 0;
                var fingerprint = string.Join("\u0001", canonical.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "\u0002" + kv.Value));
                var count = (counts.TryGetValue(fingerprint, out var c0) ? c0 : 0) + 1;
                counts[fingerprint] = count;
                if (hasIdentity && count <= (previousCounts.TryGetValue(fingerprint, out var pc) ? pc : 0)) { duplicates++; continue; }

                var row = new Dictionary<string, string>(original);
                if (identity.Count == 0)
                {
                    foreach (var kv in inheritedIdentityCells) row[kv.Key] = kv.Value;
                    foreach (var kv in inheritedStatusCells)
                        if (!(original.TryGetValue(kv.Key, out var ov) && (ov ?? "").Trim().Length > 0)) row[kv.Key] = kv.Value;
                }
                row["__sourceFileName"] = file.Name;
                row["__sourceFileId"] = fileIndex.ToString();
                fileRows.Add(row);
            }
            foreach (var (fp, count) in counts) previousCounts[fp] = Math.Max(previousCounts.TryGetValue(fp, out var p) ? p : 0, count);

            var schema = string.Join("\u0001", file.Rows.SelectMany(r => r.Keys.Where(k => !k.StartsWith("__source", StringComparison.Ordinal)))
                .Distinct().OrderBy(k => k, StringComparer.Ordinal));
            if (!groups.ContainsKey(schema)) { groups[schema] = new(); groupOrder.Add(schema); }
            groups[schema].AddRange(fileRows);
        }

        return new PreparedImport(groupOrder.Select(s => groups[s]).Where(g => g.Count > 0).ToList(), duplicates);
    }
}
