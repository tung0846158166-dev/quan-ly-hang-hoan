using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

/// <summary>Nguồn nhật ký hoạt động (chỉ ĐỌC): bản thật đọc bảng audit_log, bản giả dùng cho demo/test.</summary>
public interface IActivityLogProvider
{
    Task<IReadOnlyList<AuditEntry>> ReadAsync(int limit, int offset, string type, string text, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> TypesAsync(CancellationToken cancellationToken = default);
    Task<long> CountAsync(CancellationToken cancellationToken = default);
}

public sealed class SqliteActivityLogProvider : IActivityLogProvider
{
    private readonly string _dbPath;
    public SqliteActivityLogProvider(string dbPath) => _dbPath = dbPath;

    public Task<IReadOnlyList<AuditEntry>> ReadAsync(int limit, int offset, string type, string text, CancellationToken cancellationToken = default) =>
        Task.Run(() => AuditLogReader.Read(_dbPath, limit, offset, type, text), cancellationToken);

    public Task<IReadOnlyList<string>> TypesAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => AuditLogReader.Types(_dbPath), cancellationToken);

    public Task<long> CountAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => AuditLogReader.Count(_dbPath), cancellationToken);
}

/// <summary>Bản giả: danh sách trong bộ nhớ (lọc giống bản thật). Không đọc đĩa.</summary>
public sealed class FakeActivityLogProvider : IActivityLogProvider
{
    private readonly List<AuditEntry> _items;
    public int ReadCalls { get; private set; }

    public FakeActivityLogProvider(IEnumerable<AuditEntry>? items = null)
    {
        _items = (items ?? Demo()).OrderByDescending(e => e.Seq).ToList();
    }

    public void Add(string type, string detail)
    {
        var seq = _items.Count == 0 ? 1 : _items.Max(e => e.Seq) + 1;
        _items.Insert(0, new AuditEntry(seq, DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"), type, AuditLogReader.LabelOf(type), detail));
    }

    public Task<IReadOnlyList<AuditEntry>> ReadAsync(int limit, int offset, string type, string text, CancellationToken cancellationToken = default)
    {
        ReadCalls++;
        IEnumerable<AuditEntry> q = _items;
        if (!string.IsNullOrEmpty(type)) q = q.Where(e => e.Type == type);
        if (!string.IsNullOrEmpty(text)) q = q.Where(e => e.Detail.Contains(text, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<IReadOnlyList<AuditEntry>>(q.Skip(offset).Take(limit).ToList());
    }

    public Task<IReadOnlyList<string>> TypesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(_items.Select(e => e.Type).Distinct().OrderBy(x => x).ToList());

    public Task<long> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult((long)_items.Count);

    private static IEnumerable<AuditEntry> Demo()
    {
        var rows = new (string Type, string Detail)[]
        {
            ("phone_new_order", "Điện thoại quét tạo đơn mới (MVĐ: SPXVN069876543210, sàn: Shopee) vào bảng chính"),
            ("scan", "Nhận kiện SPXFAKE000101 (quét từ điện thoại)"),
            ("edit", "Sửa đơn SPXFAKE000102: Nhận tại kho: Chưa trả -> Hàng đã trả"),
            ("import", "Nạp file: thêm 12, cập nhật 4, nhập nhằng 1"),
            ("salework", "Salework: gộp 3 dòng chờ vào dòng có mã vận đơn"),
            ("return_watch", "Bỏ khỏi Bảng Hàng Hoàn Về: TTVNFAKE00203"),
            ("delete", "Xóa đơn 5789FAKE000209 (khôi phục được ở màn Đã xóa)"),
        };
        var now = DateTime.Now;
        for (var i = 0; i < rows.Length; i++)
            yield return new AuditEntry(rows.Length - i, now.AddMinutes(-7 * i).ToString("dd/MM/yyyy HH:mm:ss"), rows[i].Type, AuditLogReader.LabelOf(rows[i].Type), rows[i].Detail);
    }
}
