using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Data.Legacy;

public enum LegacyCategory { Main, Archived, Deleted }

/// <summary>Một bản ghi cũ đã ánh xạ, kèm JSON gốc để đối chiếu / lưu nguyên văn.</summary>
public sealed class LegacyRecord
{
    public LegacyCategory Category { get; init; }
    public string File { get; init; } = "";
    public int Index { get; init; }
    public Order Order { get; init; } = new();
    public string RawJson { get; init; } = "";

    /// <summary>Khóa khử trùng lặp: sàn + mã đơn + mã vận đơn + mã yêu cầu hoàn. KHÔNG dùng riêng orderId (kiện A/B/C).</summary>
    public string DedupKey =>
        string.Join("|", Order.Platform.Trim().ToUpperInvariant(), Order.OrderId.Trim(), Order.Track.Trim(), Order.ReturnOrderId.Trim());
}

public sealed class CategoryCount
{
    public int Read { get; set; }
    public int Mapped { get; set; }
    public int Failed { get; set; }
}

public sealed record RowError(string File, int Index, string Reason);

public sealed record DuplicateGroup(string Key, List<string> Occurrences);

public sealed class DryRunReport
{
    public string Folder { get; init; } = "";
    public CategoryCount Main { get; } = new();
    public CategoryCount Archived { get; } = new();
    public CategoryCount Deleted { get; } = new();

    public int AuditLogEntries { get; set; }
    public int InlinePhotos { get; set; }
    public int PhotoIdRefs { get; set; }

    public List<RowError> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<DuplicateGroup> Duplicates { get; } = new();
    /// <summary>Tệp có giá trị không phải JSON: lưu nguyên văn, không phân tích.</summary>
    public List<string> VerbatimFiles { get; } = new();
    /// <summary>Tệp có thật trong thư mục nhưng chưa có bộ nhập (báo để không bị bỏ sót âm thầm).</summary>
    public List<string> UnhandledFiles { get; } = new();
    /// <summary>Tệp cố ý không nhập theo quyết định của chủ dự án (không phải lỗi, không bị bỏ sót).</summary>
    public List<string> SkippedByDecision { get; } = new();

    public int TotalMapped => Main.Mapped + Archived.Mapped + Deleted.Mapped;
    /// <summary>Số bản ghi có sàn "TikTok Shop" được chuẩn hóa thành "TikTok" (JSON gốc giữ nguyên trong RawJson).</summary>
    public int PlatformRenamed { get; set; }

    public CategoryCount For(LegacyCategory c) => c switch
    {
        LegacyCategory.Main => Main,
        LegacyCategory.Archived => Archived,
        _ => Deleted,
    };
}

