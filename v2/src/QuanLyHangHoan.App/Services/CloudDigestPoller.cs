using System.IO;
using QuanLyHangHoan.Data.Cloud;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Gợi ý AI theo khung giờ làm việc (cloud tự viết lúc 8:00, 10:30, 13:30, 16:30, 19:00, 21:00): máy tính hỏi bản mới,
/// mỗi bản chỉ trả về MỘT lần (nhớ mã bản cuối trong tệp nên mở lại app không báo lặp). Lần đầu chỉ lấy bản mới nhất.
/// </summary>
public sealed class CloudDigestPoller
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<CloudAiDigest>>> _load;
    private readonly string _seenPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CloudDigestPoller(Func<CancellationToken, Task<IReadOnlyList<CloudAiDigest>>> load, string seenPath) { _load = load; _seenPath = seenPath; }

    private long? LoadSeen()
    {
        try { return File.Exists(_seenPath) && long.TryParse(File.ReadAllText(_seenPath).Trim(), out var v) ? v : null; }
        catch (IOException) { return null; }
    }

    /// <returns>Các bản mới (cũ trước, mới sau). Lỗi mạng: rỗng, lần sau hỏi lại.</returns>
    public async Task<IReadOnlyList<CloudAiDigest>> PollAsync(CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false)) return Array.Empty<CloudAiDigest>();
        try
        {
            IReadOnlyList<CloudAiDigest> all;
            try { all = await _load(ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is CloudApiException or System.Net.Http.HttpRequestException or TaskCanceledException) { return Array.Empty<CloudAiDigest>(); }
            if (all.Count == 0) return Array.Empty<CloudAiDigest>();
            var seen = LoadSeen();
            var fresh = seen is null ? all.OrderByDescending(d => d.Id).Take(1).ToList() : all.Where(d => d.Id > seen).OrderBy(d => d.Id).ToList();
            var max = all.Max(d => d.Id);
            try { File.WriteAllText(_seenPath, max.ToString()); } catch (IOException) { }
            return fresh;
        }
        finally { _gate.Release(); }
    }
}
