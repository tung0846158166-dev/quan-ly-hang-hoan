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

    /// <summary>
    /// Khiếu nại / đổi trạng thái khiếu nại kèm lý do (chủ dự án 27/11/2026): lý do thành một dòng có ngày giờ trong ghi chú khiếu nại.
    /// Mặc định (dịch vụ giả): bỏ qua lý do.
    /// </summary>
    Task<EditResult> SetComplaintAsync(string orderId, string status, string reason, string? expectedOld = null, CancellationToken cancellationToken = default)
        => SetFieldAsync(orderId, EditField.ComplaintStatus, status, expectedOld, cancellationToken);

    /// <summary>Xóa vĩnh viễn một đơn ĐÃ ở mục Đã xóa (chỉ chế độ cloud, chỉ máy chủ shop). Mặc định: không hỗ trợ.</summary>
    Task<EditResult> PurgeAsync(string orderId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new EditResult(EditKind.Invalid, "Xóa vĩnh viễn chỉ dùng được khi kết nối cloud."));

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
