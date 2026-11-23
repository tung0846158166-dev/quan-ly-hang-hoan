using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using QuanLyHangHoan.Core.Import;

namespace QuanLyHangHoan.Tests;

/// <summary>Chuẩn bị lô nạp (bỏ dòng trùng giữa file, kế thừa mã, tách nhóm) đối chiếu với mã JS cũ. Dữ liệu GIẢ.</summary>
public sealed class PlatformImportPrepGoldenTests
{
    private static string GoldenDir([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Golden");

    private static string Str(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => "",
    };

    private static string Flat(IEnumerable<KeyValuePair<string, string>> row) => string.Join(" | ", row.Select(kv => kv.Key + "=" + kv.Value));

    [Fact]
    public void Prepare_matches_legacy_js_for_every_case()
    {
        var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenDir(), "prepare.cases.json"))).RootElement.EnumerateArray().ToList();
        var golden = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenDir(), "prepare.golden.json"))).RootElement.EnumerateArray().ToList();
        Assert.Equal(cases.Count, golden.Count);

        var diffs = new List<string>();
        for (var c = 0; c < cases.Count; c++)
        {
            var name = cases[c].GetProperty("name").GetString()!;
            var files = cases[c].GetProperty("files").EnumerateArray().Select(f => new SourceFile(
                f.GetProperty("name").GetString()!,
                f.GetProperty("rows").EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object)
                    .Select(r => r.EnumerateObject().ToDictionary(p => p.Name, p => Str(p.Value))).ToList())).ToList();

            var result = PlatformImportPrep.Prepare(files);
            var gGroups = golden[c].GetProperty("groups").EnumerateArray().Select(g => g.EnumerateArray().ToList()).ToList();

            if (golden[c].GetProperty("duplicateCount").GetInt32() != result.DuplicateCount)
                diffs.Add($"[{name}] duplicateCount: mã cũ={golden[c].GetProperty("duplicateCount").GetInt32()} C#={result.DuplicateCount}");
            if (gGroups.Count != result.Groups.Count) { diffs.Add($"[{name}] số nhóm: mã cũ={gGroups.Count} C#={result.Groups.Count}"); continue; }
            for (var g = 0; g < gGroups.Count; g++)
            {
                if (gGroups[g].Count != result.Groups[g].Count) { diffs.Add($"[{name}] nhóm {g} số dòng: mã cũ={gGroups[g].Count} C#={result.Groups[g].Count}"); continue; }
                for (var r = 0; r < gGroups[g].Count; r++)
                {
                    var exp = Flat(gGroups[g][r].EnumerateObject().Select(p => new KeyValuePair<string, string>(p.Name, Str(p.Value))));
                    var act = Flat(result.Groups[g][r]);
                    if (exp != act) diffs.Add($"[{name}] nhóm {g} dòng {r}:\n  mã cũ: {exp}\n  C#:    {act}");
                }
            }
        }
        Assert.True(diffs.Count == 0, $"{diffs.Count} chênh lệch với mã cũ:\n" + string.Join("\n", diffs.Take(30)));
    }
}
