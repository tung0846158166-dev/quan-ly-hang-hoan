using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.App.Services;

public sealed record PlaceholderPreviewRow(
    string Col1,
    string Col2,
    string Col3,
    string Col4,
    string Col5);

public sealed record PlaceholderScreenData(
    string Title,
    string Subtitle,
    string BadgeText,
    IReadOnlyList<string> ColumnHeaders,
    IReadOnlyList<PlaceholderPreviewRow> SampleRows,
    string FooterNote);

/// <summary>
/// Hợp đồng cấp dữ liệu cho tầng giao diện.
/// T002 cài bằng FakeDataService; về sau Claude sẽ thay bằng SqliteReturnOrderProvider.
/// </summary>
public interface IReturnOrderProvider
{
    DateTime GetCurrentTime();
    IReadOnlyList<Order> GetReturnOrders(int count = 40);
    Task<IReadOnlyList<Order>> GetReturnOrdersAsync(int count = 40, CancellationToken cancellationToken = default);
    PlaceholderScreenData GetPlaceholderScreenData(string screenKey);
}
