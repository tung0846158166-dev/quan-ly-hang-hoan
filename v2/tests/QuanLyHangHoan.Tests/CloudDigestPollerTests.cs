using System.IO;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Data.Cloud;

namespace QuanLyHangHoan.Tests;

/// <summary>Gợi ý AI theo khung giờ: máy tính lấy bản mới từ cloud, mỗi bản chỉ báo MỘT lần (nhớ qua lần mở app sau). Dữ liệu GIẢ.</summary>
public sealed class CloudDigestPollerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-dig-" + Guid.NewGuid().ToString("N"));
    public CloudDigestPollerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static CloudAiDigest D(long id, string slot) => new(id, 1_795_000_000_000 + id, slot, "• việc " + id, true);

    [Fact]
    public async Task First_run_shows_only_the_latest_then_each_new_digest_once_even_after_restart()
    {
        var cloud = new List<CloudAiDigest> { D(3, "10:30"), D(2, "08:00"), D(1, "21:00") };   // mới nhất trước
        var seen = Path.Combine(_dir, "seen.txt");
        var p = new CloudDigestPoller(_ => Task.FromResult<IReadOnlyList<CloudAiDigest>>(cloud.ToList()), seen);

        Assert.Equal(new long[] { 3 }, (await p.PollAsync()).Select(d => d.Id));   // lần đầu: không dội cả loạt bản cũ
        Assert.Empty(await p.PollAsync());

        cloud.Insert(0, D(5, "16:30"));
        cloud.Insert(1, D(4, "13:30"));
        Assert.Equal(new long[] { 4, 5 }, (await p.PollAsync()).Select(d => d.Id));  // cũ trước, mới sau

        var again = new CloudDigestPoller(_ => Task.FromResult<IReadOnlyList<CloudAiDigest>>(cloud.ToList()), seen);
        Assert.Empty(await again.PollAsync());                                        // mở lại app: không báo lặp
    }

    [Fact]
    public async Task Network_errors_are_swallowed()
    {
        var p = new CloudDigestPoller(_ => throw new CloudApiException("unavailable", "x", 503), Path.Combine(_dir, "s.txt"));
        Assert.Empty(await p.PollAsync());
    }

    [Fact]
    public void Parses_digests_from_cloud_json()
    {
        var list = CloudApi.ParseDigests(System.Text.Json.Nodes.JsonNode.Parse("""[{"id":7,"at":1795000000000,"slot":"13:30","text":"• a","byAi":true}]""")!);
        Assert.Equal(new CloudAiDigest(7, 1795000000000, "13:30", "• a", true), Assert.Single(list));
    }
}
