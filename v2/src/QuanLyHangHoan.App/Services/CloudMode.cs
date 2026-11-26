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
                await Task.Delay(400).ConfigureAwait(false);   // gom các lô /sync liên tiếp
                Interlocked.Exchange(ref _reloadPending, 0);
                dispatchToUi(() => _ = reloadOnUi());
            });
        };
        // nạp lúc mất mạng thì chưa đẩy được: nối lại là đẩy bù ngay
        Link.Changed += () => { if (_trigger.OnLinkChanged(Link.CanWrite)) _ = PublishAsync("reconnect"); };
        Link.Start();
    }

    /// <summary>Đẩy đơn đã đổi ở máy lên cloud (sau khi nạp). Mất kết nối thì bỏ qua: lần sau đẩy tiếp (so băm nên không sót, không trùng).</summary>
    public async Task PublishAsync(string source)
    {
        if (!Link.CanWrite || Link.Api is not { } api) { LastPublishNote = "Chưa đẩy lên cloud (mất kết nối): sẽ đẩy ở lần nạp sau."; return; }
        if (!await _publishGate.WaitAsync(0).ConfigureAwait(false)) return;   // đang đẩy dở: lần đẩy đó sẽ gửi luôn phần mới
        try
        {
            var r = await CloudPublisher.PublishAsync(api, _dbPath, source).ConfigureAwait(false);
            if (CloudPublishTrigger.Note(r) is { } note) Published?.Invoke(note, r.Rejected > 0);
            LastPublishNote = r.Sent == 0 ? "Không có đơn mới cần đẩy lên cloud."
                : $"Đã đẩy {r.Sent:N0} đơn lên cloud (mới {r.Added:N0}, cập nhật {r.Updated:N0}" + (r.Rejected > 0 ? $", bị từ chối {r.Rejected:N0}" : "") + ").";
        }
        catch (CloudApiException ex) { LastPublishNote = "Đẩy lên cloud lỗi: " + ex.Message + " (sẽ đẩy lại khi nối lại / lần nạp sau)"; Published?.Invoke(LastPublishNote, true); }
        catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException) { LastPublishNote = "Đẩy lên cloud lỗi: " + ex.Message; Published?.Invoke(LastPublishNote, true); }
        finally { _publishGate.Release(); }
    }

    public void Dispose()
    {
        Link.Dispose();
        _http.Dispose();
    }
}
