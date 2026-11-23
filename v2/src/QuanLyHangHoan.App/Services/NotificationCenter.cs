using System.IO;
using System.Text.Json;

namespace QuanLyHangHoan.App.Services;

public enum NotificationLevel { Info, Yellow, Red, Success }

/// <summary>Một thông báo trong chuông. <see cref="Target"/>: khóa màn hình ("sync", "file_import"...) hoặc "search:&lt;mã&gt;" để mở tìm kiếm đơn.</summary>
public sealed class NotificationEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Time { get; set; }
    public NotificationLevel Level { get; set; }
    public string Source { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Target { get; set; } = "";
    public int Count { get; set; } = 1;
    public bool Read { get; set; }
}

public interface INotificationStore
{
    IReadOnlyList<NotificationEntry> Load();
    void Save(IReadOnlyList<NotificationEntry> entries);
}

/// <summary>Chế độ demo và test: không chạm đĩa.</summary>
public sealed class InMemoryNotificationStore : INotificationStore
{
    private List<NotificationEntry> _data = new();
    public int SaveCalls { get; private set; }
    public IReadOnlyList<NotificationEntry> Load() => _data.Select(Clone).ToList();
    public void Save(IReadOnlyList<NotificationEntry> entries) { _data = entries.Select(Clone).ToList(); SaveCalls++; }
    internal static NotificationEntry Clone(NotificationEntry e) => new()
    {
        Id = e.Id, Time = e.Time, Level = e.Level, Source = e.Source, Title = e.Title,
        Detail = e.Detail, Target = e.Target, Count = e.Count, Read = e.Read,
    };
}

/// <summary>Tệp riêng cạnh settings.json. Ghi tạm rồi đổi tên (nguyên tử). Tệp hỏng: bỏ qua và bắt đầu danh sách trống, không sập.</summary>
public sealed class FileNotificationStore : INotificationStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    public FileNotificationStore(string path) => _path = path;

    public IReadOnlyList<NotificationEntry> Load()
    {
        try
        {
            if (!File.Exists(_path)) return Array.Empty<NotificationEntry>();
            var list = JsonSerializer.Deserialize<List<NotificationEntry>>(File.ReadAllText(_path), Json);
            return list?.Where(e => e is not null).ToList() ?? new List<NotificationEntry>();
        }
        catch (Exception) { return Array.Empty<NotificationEntry>(); }
    }

    public void Save(IReadOnlyList<NotificationEntry> entries)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(entries, Json));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception) { /* không lưu được thì thôi, không làm sập app */ }
    }
}

/// <summary>
/// Danh sách thông báo trong app: mới nhất trước, tối đa 300, gộp trùng liên tiếp (cùng nguồn + tiêu đề + chi tiết trong 60 giây thành "x2"),
/// nhớ trạng thái đã đọc. Chỉ gom từ sự kiện đã có; KHÔNG đụng dữ liệu đơn.
/// </summary>
public sealed class NotificationCenter
{
    public const int MaxEntries = 300;
    public static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan DisconnectGrace = TimeSpan.FromMinutes(2);

    private readonly INotificationStore _store;
    private readonly Func<DateTime> _clock;
    private readonly object _gate = new();
    private List<NotificationEntry> _items = new();
    private DateTime? _disconnectedSince;
    private bool _disconnectRaised;

    public NotificationCenter(INotificationStore store, Func<DateTime>? clock = null)
    {
        _store = store;
        _clock = clock ?? (() => DateTime.Now);
        _items = _store.Load().OrderByDescending(e => e.Time).Take(MaxEntries).ToList();
    }

    public event Action? Changed;

    public IReadOnlyList<NotificationEntry> Snapshot()
    {
        lock (_gate) return _items.Select(InMemoryNotificationStore.Clone).ToList();
    }

    public int UnreadCount { get { lock (_gate) return _items.Count(e => !e.Read); } }

    public void Add(NotificationLevel level, string source, string title, string detail, string target = "")
    {
        title = Sanitize(title); detail = Sanitize(detail);
        var now = _clock();
        lock (_gate)
        {
            var first = _items.FirstOrDefault();
            if (first is not null && first.Source == source && first.Title == title && first.Detail == detail
                && first.Level == level && now - first.Time <= MergeWindow && now >= first.Time)
            {
                first.Count++; first.Time = now; first.Read = false;
            }
            else
            {
                _items.Insert(0, new NotificationEntry
                {
                    Time = now, Level = level, Source = source, Title = title, Detail = detail, Target = target,
                });
                if (_items.Count > MaxEntries) _items.RemoveRange(MaxEntries, _items.Count - MaxEntries);
            }
            _store.Save(_items);
        }
        Changed?.Invoke();
    }

    public void MarkRead(string id)
    {
        lock (_gate)
        {
            var e = _items.FirstOrDefault(x => x.Id == id);
            if (e is null || e.Read) return;
            e.Read = true; _store.Save(_items);
        }
        Changed?.Invoke();
    }

    public void MarkAllRead()
    {
        lock (_gate)
        {
            if (_items.All(e => e.Read)) return;
            foreach (var e in _items) e.Read = true;
            _store.Save(_items);
        }
        Changed?.Invoke();
    }

    public void ClearAll()
    {
        lock (_gate) { if (_items.Count == 0) return; _items.Clear(); _store.Save(_items); }
        Changed?.Invoke();
    }

    /// <summary>
    /// Gọi định kỳ với trạng thái kết nối đồng bộ (khi đồng bộ đang bật). Mất kết nối quá 2 phút thì báo ĐÚNG MỘT lần;
    /// nối lại thì báo xanh một lần và đặt lại.
    /// </summary>
    public void ReportConnection(bool syncEnabled, bool connected)
    {
        var now = _clock();
        bool raise = false, recovered = false;
        lock (_gate)
        {
            if (!syncEnabled || connected)
            {
                recovered = _disconnectRaised && connected;
                _disconnectedSince = null; _disconnectRaised = false;
            }
            else
            {
                _disconnectedSince ??= now;
                if (!_disconnectRaised && now - _disconnectedSince.Value >= DisconnectGrace) { _disconnectRaised = true; raise = true; }
            }
        }
        if (raise) Add(NotificationLevel.Red, "sync", "Mất kết nối đồng bộ điện thoại", "Đã quá 2 phút chưa nối lại được. Kiểm tra mạng hoặc màn Đồng bộ điện thoại.", "phone_sync");
        if (recovered) Add(NotificationLevel.Success, "sync", "Đã nối lại đồng bộ điện thoại", "Kết nối đã trở lại bình thường.", "phone_sync");
    }

    /// <summary>Không bao giờ để địa chỉ Apps Script/token lọt vào thông báo.</summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p =>
            p.Contains("script.google.com", StringComparison.OrdinalIgnoreCase)
            || p.Contains("googleusercontent", StringComparison.OrdinalIgnoreCase)
            || p.Contains("token=", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? "(đã ẩn địa chỉ)" : p);
        return string.Join(' ', parts);
    }

    public static string RelativeTime(DateTime time, DateTime now)
    {
        var d = now - time;
        if (d < TimeSpan.FromMinutes(1)) return "vừa xong";
        if (d < TimeSpan.FromHours(1)) return $"{(int)d.TotalMinutes} phút trước";
        if (d < TimeSpan.FromDays(1)) return $"{(int)d.TotalHours} giờ trước";
        return $"{(int)d.TotalDays} ngày trước";
    }
}
