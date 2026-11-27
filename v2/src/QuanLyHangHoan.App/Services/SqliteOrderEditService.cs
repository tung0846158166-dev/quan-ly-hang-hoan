using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Triển khai thật gọi <see cref="OrderEditor"/> qua Task.Run để không chặn luồng giao diện.
/// </summary>
public sealed class SqliteOrderEditService : IOrderEditService
{
    private readonly string _dbPath;
    private readonly Func<DateTime> _clock;

    public SqliteOrderEditService(string dbPath, Func<DateTime>? clock = null)
    {
        _dbPath = dbPath;
        _clock = clock ?? (() => DateTime.Now);
    }

    public Task<EditResult> SetFieldAsync(
        string orderId,
        EditField field,
        string newValue,
        string? expectedOld = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => OrderEditor.SetField(_dbPath, orderId, field, newValue, _clock(), expectedOld), cancellationToken);

    public Task<EditResult> SetComplaintAsync(string orderId, string status, string reason, string? expectedOld = null, CancellationToken cancellationToken = default)
        => Task.Run(() => OrderEditor.SetField(_dbPath, orderId, EditField.ComplaintStatus, status, _clock(), expectedOld, reason), cancellationToken);

    public Task<EditResult> DeleteAsync(
        string orderId,
        string from,
        CancellationToken cancellationToken = default)
        => Task.Run(() => OrderEditor.Delete(_dbPath, orderId, from, _clock()), cancellationToken);

    public Task<EditResult> RestoreAsync(
        string orderId,
        CancellationToken cancellationToken = default)
        => Task.Run(() => OrderEditor.Restore(_dbPath, orderId, _clock()), cancellationToken);

    public Task<EditResult> ArchiveAsync(
        string orderId,
        CancellationToken cancellationToken = default)
        => Task.Run(() => OrderEditor.Archive(_dbPath, orderId, _clock()), cancellationToken);

    public Task<EditResult> UnarchiveAsync(
        string orderId,
        CancellationToken cancellationToken = default)
        => Task.Run(() => OrderEditor.Unarchive(_dbPath, orderId, _clock()), cancellationToken);

    public Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(
        string orderId,
        CancellationToken cancellationToken = default)
        => Task.Run(() => OrderEditor.History(_dbPath, orderId), cancellationToken);
}
