using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Tests;

/// <summary>1.500 tình huống ngẫu nhiên (hạt giống cố định) chạy qua MÃ JS CŨ và qua bản C#: phải ra cùng kết quả. Dữ liệu GIẢ.</summary>
public sealed class ParcelMatchingGoldenTests
{
    private static string GoldenFile([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Golden", "match.golden.json");

    private static string S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static Order ToOrder(JsonElement e) => new()
    {
        Platform = S(e, "platform"), OrderId = S(e, "orderId"), ReturnOrderId = S(e, "returnOrderId"), Track = S(e, "track"),
        ReturnTrack = S(e, "returnTrack"), ForwardTrack = S(e, "forwardTrack"), AltTrack = S(e, "altTrack"),
        TrackingCodes = e.TryGetProperty("trackingCodes", out var tc) && tc.ValueKind == JsonValueKind.Array
            ? tc.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>(),
    };

    [Fact]
    public void Matching_agrees_with_legacy_js_on_all_random_scenarios()
    {
        var scenarios = JsonDocument.Parse(File.ReadAllText(GoldenFile())).RootElement.EnumerateArray().ToList();
        Assert.True(scenarios.Count >= 1000);

        var diffs = new List<string>();
        for (var n = 0; n < scenarios.Count; n++)
        {
            var orders = scenarios[n].GetProperty("orders").EnumerateArray().Select(ToOrder).ToList();
            var incoming = ToOrder(scenarios[n].GetProperty("incoming"));
            var exp = scenarios[n].GetProperty("expected");

            var expSame = exp.GetProperty("same").EnumerateArray().Select(x => x.GetBoolean()).ToList();
            for (var i = 0; i < orders.Count; i++)
                if (ParcelMatching.IsSameReturnParcel(orders[i], incoming) != expSame[i])
                    diffs.Add($"#{n} same[{i}]: mã cũ={expSame[i]}");

            var ex = ParcelMatching.FindExistingIndex(orders, incoming);
            if (ex != exp.GetProperty("existing").GetInt32()) diffs.Add($"#{n} existing: mã cũ={exp.GetProperty("existing").GetInt32()} C#={ex}");

            var tr = ParcelMatching.FindTracklessSameOrderIndex(orders, incoming);
            if (tr != exp.GetProperty("trackless").GetInt32()) diffs.Add($"#{n} trackless: mã cũ={exp.GetProperty("trackless").GetInt32()} C#={tr}");
        }
        Assert.True(diffs.Count == 0, $"{diffs.Count} chênh lệch với mã cũ:\n" + string.Join("\n", diffs.Take(40)));
    }

    [Fact]
    public void Ambiguous_incoming_is_reported_via_all_matches()
    {
        var a = new Order { OrderId = "O1", Track = "T1", TrackingCodes = { "SHARED" } };
        var b = new Order { OrderId = "O1", Track = "T2", TrackingCodes = { "SHARED" } };
        var incoming = new Order { OrderId = "O1", Track = "SHARED" };
        Assert.Equal(-1, ParcelMatching.FindExistingIndex(new[] { a, b }, incoming));
        Assert.Equal(2, ParcelMatching.FindAllMatchIndexes(new[] { a, b }, incoming).Count);
    }
}
