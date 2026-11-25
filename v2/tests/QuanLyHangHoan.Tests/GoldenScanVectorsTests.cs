using System.IO;
using System.Text.Json;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Core.Tracking;

namespace QuanLyHangHoan.Tests;

/// <summary>Bộ ca thử DÙNG CHUNG với bản TypeScript trên cloud (cloud/golden/scan-vectors.json): cùng dữ liệu phải cho cùng quyết định.</summary>
public sealed class GoldenScanVectorsTests
{
    private static string VectorsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var p = Path.Combine(dir.FullName, "cloud", "golden", "scan-vectors.json");
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Không thấy cloud/golden/scan-vectors.json");
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
    private static List<string> List(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new();

    private static Order ToOrder(JsonElement e) => new()
    {
        Id = Str(e, "id"), OrderId = Str(e, "orderId"), ReturnOrderId = Str(e, "returnOrderId"), Track = Str(e, "track"),
        ReturnTrack = Str(e, "returnTrack"), AltTrack = Str(e, "altTrack"), ForwardTrack = Str(e, "forwardTrack"),
        TrackingCodes = List(e, "trackingCodes"), ScanAliases = List(e, "scanAliases"), Status = Str(e, "status"),
    };

    private static string Kind(ScanDecisionKind k) => k switch
    {
        ScanDecisionKind.Receive => "receive",
        ScanDecisionKind.AlreadyReceived => "alreadyReceived",
        ScanDecisionKind.Ambiguous => "ambiguous",
        ScanDecisionKind.NotFound => "notFound",
        ScanDecisionKind.NeedsManualCheck => "needsManualCheck",
        _ => k.ToString(),
    };

    private static IEnumerable<(string Name, List<Order> Orders, string Code, JsonElement Expect)> Cases()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(VectorsPath()));
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var name = c.GetProperty("name").GetString()!;
            var orders = c.GetProperty("orders").EnumerateArray().Select(ToOrder).ToList();
            if (c.TryGetProperty("multi", out var multi))
                foreach (var m in multi.EnumerateArray())
                    yield return (name, orders, m.GetProperty("code").GetString()!, m.GetProperty("expect").Clone());
            else
                yield return (name, orders, c.GetProperty("code").GetString()!, c.GetProperty("expect").Clone());
        }
    }

    [Fact]
    public void Every_golden_case_gives_the_expected_decision()
    {
        var failures = new List<string>();
        var n = 0;
        foreach (var (name, orders, code, expect) in Cases())
        {
            n++;
            var d = ScanDecider.Decide(orders, code);
            var wantKind = expect.GetProperty("kind").GetString();
            var wantOrder = expect.GetProperty("orderId").ValueKind == JsonValueKind.Null ? null : expect.GetProperty("orderId").GetString();
            var wantCount = expect.GetProperty("candidates").GetInt32();
            if (Kind(d.Kind) != wantKind || d.Order?.Id != wantOrder || d.Candidates != wantCount)
                failures.Add($"[{name}] code='{code}': mong {wantKind}/{wantOrder}/{wantCount}, được {Kind(d.Kind)}/{d.Order?.Id}/{d.Candidates}");
        }
        Assert.True(n >= 20, "thiếu ca thử: " + n);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
