using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using QuanLyHangHoan.Core.Import;

namespace QuanLyHangHoan.Tests;

/// <summary>Ghép 3 tab Google Sheet thành dòng kiểu Excel Shopee, so với mã JS cũ (Golden/sheet.*.json). Dữ liệu GIẢ.</summary>
public sealed class SheetPipelineGoldenTests
{
    private static string GoldenDir([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Golden");

    private static string Str(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Number => v.GetRawText(),
        _ => "",
    };

    [Fact]
    public void Build_matches_legacy_js_for_every_golden_case()
    {
        var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenDir(), "sheet.cases.json"))).RootElement.EnumerateArray().ToList();
        var golden = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenDir(), "sheet.golden.json"))).RootElement.EnumerateArray().ToList();
        Assert.Equal(cases.Count, golden.Count);
        var diffs = new List<string>();
        for (var c = 0; c < cases.Count; c++)
        {
            var k = cases[c]; var g = golden[c];
            var name = k.GetProperty("name").GetString()!;
            var tabs = k.GetProperty("tabs").EnumerateArray()
                .Select(t => t.EnumerateArray().Select(r => r.EnumerateObject().ToDictionary(p => p.Name, p => Str(p.Value))).ToList()).ToList();
            var res = SheetPipeline.Build(tabs, k.TryGetProperty("skipTypes", out var st) ? st.GetString() : null,
                k.TryGetProperty("maxDays", out var md) ? md.GetString() : null, new DateTime(2026, 11, 23, 12, 0, 0));

            var rows = g.GetProperty("rows").EnumerateArray().ToList();
            if (rows.Count != res.Rows.Count) { diffs.Add($"[{name}] số dòng: mã cũ={rows.Count} C#={res.Rows.Count}"); continue; }
            for (var i = 0; i < rows.Count; i++)
            {
                var exp = rows[i].EnumerateObject().ToDictionary(p => p.Name, p => Str(p.Value));
                foreach (var kv in exp)
                    if (!res.Rows[i].TryGetValue(kv.Key, out var v) || v != kv.Value) diffs.Add($"[{name}]#{i}.{kv.Key}: mã cũ=[{kv.Value}] C#=[{(res.Rows[i].TryGetValue(kv.Key, out var x) ? x : "<thiếu>")}]");
                foreach (var key in res.Rows[i].Keys.Where(key => !exp.ContainsKey(key))) diffs.Add($"[{name}]#{i}: C# thừa cột {key}");
            }
            if (g.GetProperty("removedTotal").GetInt32() != res.OldRequestsDropped) diffs.Add($"[{name}] yêu cầu cũ bị bỏ: {g.GetProperty("removedTotal")} vs {res.OldRequestsDropped}");
            if (g.GetProperty("skippedCancel").GetInt32() != res.SkippedCancelTypes) diffs.Add($"[{name}] đơn huỷ bỏ qua: {g.GetProperty("skippedCancel")} vs {res.SkippedCancelTypes}");
            if (g.GetProperty("tooOld").GetInt32() != res.TooOld) diffs.Add($"[{name}] quá cũ: {g.GetProperty("tooOld")} vs {res.TooOld}");
            if (g.GetProperty("parcelCount").GetInt32() != res.Parcels.Count) diffs.Add($"[{name}] số đơn: {g.GetProperty("parcelCount")} vs {res.Parcels.Count}");
        }
        Assert.True(diffs.Count == 0, $"{diffs.Count} chênh lệch với mã cũ:\n" + string.Join("\n", diffs.Take(60)));
    }

    [Fact]
    public void Sheet_rows_flow_into_shopee_mapper_as_one_parcel_per_order()
    {
        var tabs = new List<List<Dictionary<string, string>>>
        {
            new() { new() { ["Mã"] = "RR1", ["Mã đơn"] = "SP1", ["Loại"] = "Trả hàng", ["Mã vận đơn"] = "SPXVN0000000001", ["Trạng thái về"] = "Đã về shop",
                ["Về shop lúc"] = "21/11/2026 10:00", ["Hàng"] = "Áo · Đen, M × 2; Quần · Be, L", ["SL"] = "3", ["Tiền"] = "250000", ["Ngày"] = "20/11/2026" } },
        };
        var rows = SheetPipeline.Build(tabs, null, null, new DateTime(2026, 11, 23)).Rows;
        var parcels = ShopeeMapper.Map(rows, new ShopeeMapOptions { Today = new DateTime(2026, 11, 23) }).Parcels;
        var p = Assert.Single(parcels);
        Assert.Equal("SP1", p.OrderId);
        Assert.Equal(2, p.Items.Count);
        Assert.Equal("22/11/2026 10:00", p.Deadline);   // hạn khiếu nại = 24h sau lúc hàng về
        Assert.Equal(250000, p.Price);
    }
}
