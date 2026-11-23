namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Bản cài đặt GIẢ của ILegacyImporter.
/// TUYỆT ĐỐI KHÔNG đọc hay ghi ổ đĩa; chỉ trả về số liệu giả lập theo tỉ lệ mẫu (T004/T006).
/// </summary>
public sealed class FakeLegacyImporter : ILegacyImporter
{
    private readonly Func<DateTime> _clock;
    private readonly int _simulatedDelayMs;

    public FakeLegacyImporter(Func<DateTime>? clock = null, int simulatedDelayMs = 180)
    {
        _clock = clock ?? (() => DateTime.Now);
        _simulatedDelayMs = Math.Max(0, simulatedDelayMs);
    }

    public async Task<LegacyImportDryRunReport> RunDryRunAsync(string sourceFolderPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFolderPath))
        {
            throw new ArgumentException("Vui lòng chọn hoặc nhập đường dẫn thư mục nguồn.", nameof(sourceFolderPath));
        }

        if (_simulatedDelayMs > 0)
        {
            await Task.Delay(_simulatedDelayMs, cancellationToken);
        }

        var skippedByDecision = new List<string>
        {
            "Bỏ tệp bảng đơn hủy riêng (phu_shopee_cancelled_orders_v1 — 66 bản ghi; đơn hủy chính vẫn nhập qua bảng đơn chung).",
            "Bỏ tệp khôi phục thủ công (khoi_phuc_don_da_nhan — 203 dòng; đọc trực tiếp từ phiên app đang chạy)."
        };

        var warnings = new List<string>
        {
            "[Giả lập] 3 nhóm trùng khóa trong cùng tệp — giữ nguyên để chủ dự án kiểm tra, không tự gộp/xóa.",
            "[Giả lập] 2.119 bản ghi có tên sàn cũ sẽ được chuẩn hóa thành nhãn ngắn \"TikTok\" (giữ nguyên RawJson).",
            "[Giả lập] 2 đơn thiếu giờ hàng về (returnedAt) — giữ nguyên trống, không tự suy đoán."
        };

        return new LegacyImportDryRunReport(
            SourceFolderPath: sourceFolderPath.Trim(),
            MainOrdersCount: 2_307,
            ArchivedOrdersCount: 2_195,
            DeletedOrdersCount: 166,
            EmbeddedPhotosCount: 1_032,
            PhotoIdReferencesCount: 2_191,
            NormalizedPlatformCount: 2_119,
            DuplicateGroupsCount: 3,
            WarningsCount: warnings.Count,
            SkippedByDecision: skippedByDecision,
            Warnings: warnings,
            SummaryNote: "Báo cáo GIẢ LẬP (không đọc ổ đĩa). Tổng đọc mẫu: 4.668 = chính 2.307 + lưu trữ 2.195 + đã xóa 166; ảnh nhúng 1.032.",
            GeneratedAt: _clock());
    }

    public async Task<LegacyImportResult> ExecuteImportAsync(string sourceFolderPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFolderPath))
        {
            throw new ArgumentException("Vui lòng chọn hoặc nhập đường dẫn thư mục nguồn.", nameof(sourceFolderPath));
        }

        if (_simulatedDelayMs > 0)
        {
            await Task.Delay(_simulatedDelayMs, cancellationToken);
        }

        return new LegacyImportResult(
            Success: true,
            SourceFolderPath: sourceFolderPath.Trim(),
            ImportedMainOrders: 2_307,
            ImportedArchivedOrders: 2_195,
            ImportedDeletedOrders: 166,
            ImportedPhotos: 1_032,
            NormalizedPlatformCount: 2_119,
            SkippedDuplicates: 0,
            Message: "Nhập dữ liệu GIẢ LẬP hoàn tất (không ghi vào ổ đĩa hay SQLite). Sẵn sàng nối ILegacyImporter thật.",
            CompletedAt: _clock());
    }
}
