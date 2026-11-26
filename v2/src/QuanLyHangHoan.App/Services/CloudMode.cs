using System.IO;
using System.Net.Http;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Cloud;
using QuanLyHangHoan.Data.Sheet;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Công tắc "bật cloud" của app máy tính. Bật khi chạy với tham số <c>--cloud</c> HOẶC máy đã ghép thiết bị (có tệp phiên).
/// Tắt (mặc định) thì app chạy y như cũ: SQLite cục bộ + MQTT, <see cref="ICloudLink"/> = null.
/// Khi bật: quét/sửa/Bảng Hàng Hoàn Về đi qua cloud; dữ liệu cloud đổ về bản sao SQLite (màn hình đọc như cũ);
/// sau mỗi lần nạp Sheet/thư mục/Salework/file thì đẩy các đơn đã đổi lên cloud (<see cref="CloudPublisher"/>).
/// </summary>
public sealed class CloudMode : IDisposable
{
    public const string Flag = "--cloud";

    public static string SessionPath => Path.Combine(AppPaths.BaseFolder, "cloud_session.json");

    public static bool IsEnabled(IEnumerable<string> args) =>
        args.Any(a => a.Equals(Flag, StringComparison.OrdinalIgnoreCase)) || File.Exists(SessionPath);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly string _dbPath;
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private int _reloadPending;

    public CloudLinkService Link { get; }
    public IScanService Scan { get; }
    public IOrderEditService Edit { get; }
    public IReturnWatchService ReturnWatch { get; }

    /// <summary>Báo kết quả đẩy cho người dùng (câu, là lỗi?). Chỉ phát khi có gửi hoặc có lỗi. Có thể phát từ luồng nền.</summary>
    public event Action<string, bool>? Published;

    private readonly CloudPublishTrigger _trigger = new();

    /// <summary>Cổng AI của cloud (null = chưa nối / chưa hỏi được). Cập nhật mỗi lần nối được cloud.</summary>
    public QuanLyHangHoan.Data.Ai.CloudAiAccess? AiAccess { get; private set; }
    /// <summary>Phát khi <see cref="AiAccess"/> đổi (có thể từ luồng nền).</summary>
    public event Action? AiAccessChanged;

    /// <summary>Kết quả lần đẩy đơn gần nhất (để hiển thị/chẩn đoán).</summary>
    public string LastPublishNote { get; private set; } = "";

    public CloudMode(string dbPath, ISecretProtector protector)
    {
        _dbPath = dbPath;
        Link = new CloudLinkService(_http, dbPath, new CloudSessionStore(SessionPath, protector));
        Scan = new CloudScanService(Link);
        Edit = new CloudOrderEditService(Link);
        ReturnWatch = new CloudReturnWatchService(Link, dbPath);
    }

    /// <summary>Nối với giao diện: dữ liệu cloud mới thì nạp lại màn hình (gộp nhiều lần báo thành một, chạy trên luồng giao diện).</summary>
    public void Start(Func<Task> reloadOnUi, Action<Action> dispatchToUi)
    {
        Link.DataChanged += () =>
        {
            if (Interlocked.Exchange(ref _reloadPending, 1) == 1) return;
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000).ConfigureAwait(false);   // gom các lô /sync liên tiếp: nạp lại bảng tối đa ~2 giây/lần (đỡ nhấp nháy)
                Interlocked.Exchange(ref _reloadPending, 0);
                dispatchToUi(() => _ = reloadOnUi());
            });
        };
        // nạp lúc mất mạng thì chưa đẩy được: nối lại là đẩy bù ngay
        Link.Changed += () =>
        {
            // chỉ đẩy khi đã KÉO XONG phần mới của cloud (Connected), không đẩy lúc đang Syncing: tránh gửi bản cũ ở máy đè thay đổi trên cloud
            if (!_trigger.OnLinkChanged(Link.State == CloudState.Connected)) return;
            _ = PublishAsync("reconnect");
            _ = RefreshAiAccessAsync();
        };
        Link.Start();
    }

    /// <summary>Đẩy đơn đã đổi ở máy lên cloud (sau khi nạp). Mất kết nối thì bỏ qua: lần sau đẩy tiếp (so băm nên không sót, không trùng).</summary>
    private int _publishAgain;

    public async Task PublishAsync(string source)
    {
        if (!Link.CanWrite || Link.Api is not { } api) { LastPublishNote = "Chưa đẩy lên cloud (mất kết nối): sẽ đẩy ở lần nạp sau."; return; }
        if (!await _publishGate.WaitAsync(0).ConfigureAwait(false)) { Interlocked.Exchange(ref _publishAgain, 1); return; }   // đang đẩy dở: xong sẽ đẩy thêm một lượt
        try
        {
            var r = await CloudPublisher.PublishAsync(api, _dbPath, source).ConfigureAwait(false);
            while (Interlocked.Exchange(ref _publishAgain, 0) == 1 && Link.CanWrite)
            {
                var more = await CloudPublisher.PublishAsync(api, _dbPath, source).ConfigureAwait(false);
                r = new CloudPublishResult(r.Candidates + more.Candidates, r.Sent + more.Sent, r.Added + more.Added, r.Updated + more.Updated,
                    r.Unchanged + more.Unchanged, r.Rejected + more.Rejected, r.ReturnWatchSent + more.ReturnWatchSent);
            }
            if (CloudPublishTrigger.Note(r) is { } note) Published?.Invoke(note, r.Rejected > 0);
            LastPublishNote = r.Sent == 0 ? "Không có đơn mới cần đẩy lên cloud."
                : $"Đã đẩy {r.Sent:N0} đơn lên cloud (mới {r.Added:N0}, cập nhật {r.Updated:N0}" + (r.Rejected > 0 ? $", bị từ chối {r.Rejected:N0}" : "") + ").";
        }
        catch (CloudApiException ex) { LastPublishNote = "Đẩy lên cloud lỗi: " + ex.Message + " (sẽ đẩy lại khi nối lại / lần nạp sau)"; Published?.Invoke(LastPublishNote, true); }
        catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException) { LastPublishNote = "Đẩy lên cloud lỗi: " + ex.Message; Published?.Invoke(LastPublishNote, true); }
        finally { _publishGate.Release(); }
    }

    /// <summary>Hỏi cloud có khóa AI nào. Lỗi mạng thì giữ trạng thái cũ (lần nối sau hỏi lại).</summary>
    public async Task RefreshAiAccessAsync()
    {
        if (Link.Api is not { Token: { } token } api) return;
        try
        {
            var st = await api.AiStatusAsync().ConfigureAwait(false);
            AiAccess = new QuanLyHangHoan.Data.Ai.CloudAiAccess(api.BaseUrl, token, st.Groq, st.Gemini, st.Nvidia, st.Cloudflare, st.Order, st.Models, st.TimeoutSec);
            AiAccessChanged?.Invoke();
        }
        catch (CloudApiException) { }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
    }

    public void Dispose()
    {
        Link.Dispose();
        _http.Dispose();
    }
}
