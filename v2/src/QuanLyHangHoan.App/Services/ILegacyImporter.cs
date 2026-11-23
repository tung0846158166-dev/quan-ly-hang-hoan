namespace QuanLyHangHoan.App.Services;

public sealed record LegacyImportDryRunReport(
    string SourceFolderPath,
    int MainOrdersCount,
    int ArchivedOrdersCount,
    int DeletedOrdersCount,
    int EmbeddedPhotosCount,
    int PhotoIdReferencesCount,
    int NormalizedPlatformCount,
    int DuplicateGroupsCount,
    int WarningsCount,
    IReadOnlyList<string> SkippedByDecision,
    IReadOnlyList<string> Warnings,
    string SummaryNote,
    DateTime GeneratedAt);

public sealed record LegacyImportResult(
    bool Success,
    string SourceFolderPath,
    int ImportedMainOrders,
    int ImportedArchivedOrders,
    int ImportedDeletedOrders,
    int ImportedPhotos,
    int NormalizedPlatformCount,
    int SkippedDuplicates,
    string Message,
    DateTime CompletedAt);

/// <summary>
/// Hợp đồng nhập dữ liệu từ app cũ (v1.x) sang SQLite v2.
/// Bản giả (FakeLegacyImporter) không đọc ổ đĩa; Claude sẽ nối bản thật ở Data.
/// </summary>
public interface ILegacyImporter
{
    Task<LegacyImportDryRunReport> RunDryRunAsync(string sourceFolderPath, CancellationToken cancellationToken = default);
    Task<LegacyImportResult> ExecuteImportAsync(string sourceFolderPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Dịch vụ mở hộp thoại chọn thư mục (tách khỏi ViewModel để giữ MVVM thuần và dễ viết test).
/// </summary>
public interface IFolderPickerService
{
    string? PickFolder(string title, string? initialDirectory = null);
}
