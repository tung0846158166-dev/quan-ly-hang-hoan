using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

public interface IOrderEditService
{
    Task<EditResult> SetFieldAsync(
        string orderId,
        EditField field,
        string newValue,
        string? expectedOld = null,
        CancellationToken cancellationToken = default);

    Task<EditResult> DeleteAsync(
        string orderId,
        string from,
        CancellationToken cancellationToken = default);

    Task<EditResult> RestoreAsync(
        string orderId,
        CancellationToken cancellationToken = default);

    Task<EditResult> ArchiveAsync(
        string orderId,
        CancellationToken cancellationToken = default);

    Task<EditResult> UnarchiveAsync(
        string orderId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(
        string orderId,
        CancellationToken cancellationToken = default);
}
