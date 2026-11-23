using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Bộ nhập dữ liệu cũ thật bọc <see cref="LegacyImportService"/>.
/// Chạy đồng bộ trên background thread qua Task.Run để không chặn giao diện.
/// </summary>
public sealed class SqliteLegacyImporter : ILegacyImporter
{
    private readonly string _workRoot;
    private readonly string _dbPath;
    private readonly Func<DateTime> _clock;

    public SqliteLegacyImporter(string workRoot, string dbPath, Func<DateTime>? clock = null)
    {
        _workRoot = workRoot;
        _dbPath = dbPath;
        _clock = clock ?? (() => DateTime.Now);
    }

    public async Task<LegacyImportDryRunReport> RunDryRunAsync(
        string sourceFolderPath,
        CancellationToken cancellationToken = default)
    {
        var run = await Task.Run(
            () => LegacyImportService.Run(sourceFolderPath, _workRoot, _dbPath, dryRunOnly: true),
            cancellationToken);

        if (!run.Ok || run.DryRun is null)
        {
            throw new InvalidOperationException(run.Message);
        }

        var dry = run.DryRun;
        var warnings = new List<string>(dry.Warnings);
        foreach (var dup in dry.Duplicates)
        {
            warnings.Add($"Trùng khóa ({dup.Occurrences.Count} bản ghi, giữ riêng không gộp): {dup.Key} [{string.Join(", ", dup.Occurrences)}]");
        }

        var summaryNote =
            $"Bản sao kiểm hash: {run.CopyFolder}. " +
            $"Tổng ánh xạ: {dry.TotalMapped:N0} (chính {dry.Main.Mapped:N0} + lưu trữ {dry.Archived.Mapped:N0} + đã xóa {dry.Deleted.Mapped:N0}); " +
            $"ảnh nhúng {dry.InlinePhotos:N0}, tham chiếu photoId {dry.PhotoIdRefs:N0}, nhóm trùng khóa {dry.Duplicates.Count:N0}.";

        return new LegacyImportDryRunReport(
            SourceFolderPath: sourceFolderPath,
            MainOrdersCount: dry.Main.Mapped,
            ArchivedOrdersCount: dry.Archived.Mapped,
            DeletedOrdersCount: dry.Deleted.Mapped,
            EmbeddedPhotosCount: dry.InlinePhotos,
            PhotoIdReferencesCount: dry.PhotoIdRefs,
            NormalizedPlatformCount: dry.PlatformRenamed,
            DuplicateGroupsCount: dry.Duplicates.Count,
            WarningsCount: warnings.Count,
            SkippedByDecision: dry.SkippedByDecision,
            Warnings: warnings,
            SummaryNote: summaryNote,
            GeneratedAt: _clock());
    }

    public async Task<LegacyImportResult> ExecuteImportAsync(
        string sourceFolderPath,
        CancellationToken cancellationToken = default)
    {
        var run = await Task.Run(
            () => LegacyImportService.Run(sourceFolderPath, _workRoot, _dbPath, dryRunOnly: false),
            cancellationToken);

        var detail = run.Import is not null && run.Reconcile is not null
            ? $" (Thêm mới: {run.Import.Inserted:N0}, cập nhật: {run.Import.Updated:N0}, đối chiếu DB: {run.Reconcile.DbOrders:N0}/{run.Reconcile.ExpectedOrders:N0})."
            : string.Empty;

        return new LegacyImportResult(
            Success: run.Ok,
            SourceFolderPath: sourceFolderPath,
            ImportedMainOrders: run.DryRun?.Main.Mapped ?? 0,
            ImportedArchivedOrders: run.DryRun?.Archived.Mapped ?? 0,
            ImportedDeletedOrders: run.DryRun?.Deleted.Mapped ?? 0,
            ImportedPhotos: run.DryRun?.PhotoIdRefs ?? 0,
            NormalizedPlatformCount: run.DryRun?.PlatformRenamed ?? 0,
            SkippedDuplicates: 0,
            Message: run.Message + detail,
            CompletedAt: _clock());
    }
}
