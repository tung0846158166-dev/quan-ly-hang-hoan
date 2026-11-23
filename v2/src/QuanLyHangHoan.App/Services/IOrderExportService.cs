using System.IO;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

public enum ExportFormat { Xlsx, Csv }

/// <param name="Cancelled">Người dùng đóng hộp thoại lưu: không có gì được tạo.</param>
public sealed record ExportResult(bool Ok, bool Cancelled, string Message, string? Path, int Rows);

/// <summary>
/// Xuất danh sách đơn đang hiển thị ra Excel/CSV. Mỗi KIỆN một dòng, không có cột thông tin khách (xem <see cref="OrderExporter"/>).
/// Không bao giờ sửa dữ liệu đơn; tệp chỉ được tạo SAU khi người dùng chọn nơi lưu; ghi tạm rồi đổi tên nên không để lại tệp dở.
/// </summary>
public interface IOrderExportService
{
    Task<ExportResult> ExportAsync(IReadOnlyList<Order> orders, string tableName, ExportFormat format, bool archived = false, CancellationToken cancellationToken = default);
}

public sealed class OrderExportService : IOrderExportService
{
    private readonly IFileSaveService _save;
    private readonly Func<DateTime> _clock;

    public OrderExportService(IFileSaveService save, Func<DateTime>? clock = null)
    {
        _save = save;
        _clock = clock ?? (() => DateTime.Now);
    }

    public async Task<ExportResult> ExportAsync(IReadOnlyList<Order> orders, string tableName, ExportFormat format, bool archived = false, CancellationToken cancellationToken = default)
    {
        if (orders.Count == 0) return new ExportResult(false, false, "Không có dòng nào để xuất.", null, 0);

        var ext = format == ExportFormat.Xlsx ? "xlsx" : "csv";
        var safeName = new string(tableName.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '-' : c).ToArray());
        var fileName = $"QuanLyHangHoan-{safeName}-{_clock():yyyyMMdd-HHmm}.{ext}";
        var filter = format == ExportFormat.Xlsx ? "Excel (*.xlsx)|*.xlsx" : "CSV (*.csv)|*.csv";
        var path = _save.PickSavePath($"Xuất {orders.Count:N0} dòng ra {(format == ExportFormat.Xlsx ? "Excel" : "CSV")}", fileName, filter);
        if (string.IsNullOrWhiteSpace(path)) return new ExportResult(false, true, "Đã hủy xuất file.", null, 0);

        try
        {
            var stored = orders.Select(o => new StoredOrder(o, archived, archived ? "" : "", false, "", "")).ToList();
            var bytes = await Task.Run(() => format == ExportFormat.Xlsx ? OrderExporter.ToXlsx(stored, tableName.Length > 25 ? tableName[..25] : tableName) : OrderExporter.ToCsv(stored), cancellationToken);
            await _save.WriteBytesAsync(path, bytes, cancellationToken);
            return new ExportResult(true, false, $"Đã xuất {orders.Count:N0} dòng ra {Path.GetFileName(path)}.", path, orders.Count);
        }
        catch (OperationCanceledException) { return new ExportResult(false, true, "Đã hủy xuất file.", null, 0); }
        catch (Exception ex)
        {
            return new ExportResult(false, false, "Không ghi được tệp (tệp đang mở bằng Excel, ổ đĩa đầy hoặc không có quyền ghi?): " + ex.Message, null, 0);
        }
    }
}

/// <summary>Bản giả cho demo/test: không hỏi nơi lưu, không ghi đĩa.</summary>
public sealed class FakeOrderExportService : IOrderExportService
{
    public List<(int Rows, string Table, ExportFormat Format, bool Archived)> Calls { get; } = new();
    public ExportResult? NextResult { get; set; }

    public Task<ExportResult> ExportAsync(IReadOnlyList<Order> orders, string tableName, ExportFormat format, bool archived = false, CancellationToken cancellationToken = default)
    {
        if (orders.Count == 0) return Task.FromResult(new ExportResult(false, false, "Không có dòng nào để xuất.", null, 0));
        Calls.Add((orders.Count, tableName, format, archived));
        var r = NextResult ?? new ExportResult(true, false, $"Đã xuất {orders.Count:N0} dòng (demo: không ghi đĩa).", null, orders.Count);
        NextResult = null;
        return Task.FromResult(r);
    }
}
