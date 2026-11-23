using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Tests.ImporterTests;

/// <summary>Toàn bộ dữ liệu ở đây là GIẢ, mô phỏng cấu trúc đã khảo sát (docs/legacy-data-schema.md).</summary>
public sealed class LegacyDryRunTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-legacy-" + Guid.NewGuid().ToString("N"));

    public LegacyDryRunTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* bỏ qua */ } }

    private static object MainRow(string orderId, string track, string platform = "Shopee", string? returnOrderId = null) => new
    {
        shipDate = "01/11/2026", returnDate = "05/11/2026", recvDate = "", platform, orderType = "Trả hàng/Hoàn tiền",
        platformStatus = "Đang về", preOrder = "Không", orderId, track, altTrack = "", carrier = "SPX", price = 120000,
        status = "Chưa về", photo = "", note = "Size nhỏ", buyerNote = "", importWeek = "T45", importedAt = "2026-11-05",
        importedAtFormatted = "05/11/2026", returnOrderId = returnOrderId ?? "",
        items = new[] { new { product = "Áo giả", color = "Đen", size = "M", qty = 2 } },
        rawDetails = new { x = 1 }, trackingCodes = new[] { track + "-OLD" }
    };

    private void WriteMain(params object[] rows) =>
        File.WriteAllText(Path.Combine(_dir, "du_lieu_hang_hoan.json"), JsonSerializer.Serialize(rows));

    private void WriteWrapped(string file, object inner) =>
        File.WriteAllText(Path.Combine(_dir, file), JsonSerializer.Serialize(new { v = JsonSerializer.Serialize(inner) }));

    private void WriteWrappedRaw(string file, string rawInner) =>
        File.WriteAllText(Path.Combine(_dir, file), JsonSerializer.Serialize(new { v = rawInner }));

    private static string HashDir(string dir) =>
        string.Join("|", Directory.GetFiles(dir).OrderBy(f => f).Select(f => Path.GetFileName(f) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))));

    [Fact]
    public void Reads_unwrapped_main_and_wrapped_secondary_files()
    {
        WriteMain(MainRow("2611FAKE0001", "TRK1"), MainRow("2611FAKE0002", "TRK2"));
        WriteWrapped("phu_shopee_archived_orders_v1.json", new[] { MainRow("2610FAKE0009", "TRK9") });
        WriteWrapped("phu_shopee_deleted_orders_v1.json", new[] { MainRow("2610FAKE0010", "TRK10") });

        var report = LegacyDryRun.Analyze(_dir);

        Assert.Equal(2, report.Main.Read);
        Assert.Equal(1, report.Archived.Read);
        Assert.Equal(1, report.Deleted.Read);
        Assert.Equal(4, report.TotalMapped);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void Dry_run_never_modifies_source_folder()
    {
        WriteMain(MainRow("2611FAKE0001", "TRK1"));
        WriteWrapped("phu_shopee_archived_orders_v1.json", new[] { MainRow("2610FAKE0009", "TRK9") });
        var before = HashDir(_dir);

        LegacyDryRun.Analyze(_dir);

        Assert.Equal(before, HashDir(_dir));
    }

    [Fact]
    public void Parcels_A_and_B_with_same_order_id_stay_separate()
    {
        WriteMain(MainRow("2611SHARED", "TRK-A"), MainRow("2611SHARED", "TRK-B"));

        var report = LegacyDryRun.Analyze(_dir);

        Assert.Equal(2, report.Main.Mapped);
        Assert.Empty(report.Duplicates); // cùng orderId nhưng khác track => KHÔNG phải trùng
    }

    [Fact]
    public void Identical_key_inside_or_across_files_is_reported_as_duplicate_not_merged()
    {
        WriteMain(MainRow("2611DUP", "TRKD"));
        WriteWrapped("phu_shopee_archived_orders_v1.json", new[] { MainRow("2611DUP", "TRKD") });

        var report = LegacyDryRun.Analyze(_dir);

        Assert.Equal(1, report.Main.Mapped);
        Assert.Equal(1, report.Archived.Mapped); // vẫn đếm, không tự xóa/gộp
        var dup = Assert.Single(report.Duplicates);
        Assert.Equal(2, dup.Occurrences.Count);
    }

    [Fact]
    public void Cancelled_table_and_recovery_file_are_ignored_by_decision_but_main_cancel_type_orders_are_kept()
    {
        var cancelTypeInMain = MainRow("2611KEEP", "TRKK");
        WriteMain(cancelTypeInMain);
        WriteWrapped("phu_shopee_cancelled_orders_v1.json", new[] { new { id = "c1", orderId = "2611CANC001", track = "TRKC1" } });
        File.WriteAllText(Path.Combine(_dir, "khoi_phuc_don_da_nhan.json"), "{\"version\":1,\"rows\":[]}");

        var report = LegacyDryRun.Analyze(_dir);

        Assert.Equal(1, report.TotalMapped);
        Assert.Contains(report.SkippedByDecision, s => s.Contains("phu_shopee_cancelled_orders_v1.json"));
        Assert.Contains(report.SkippedByDecision, s => s.Contains("khoi_phuc_don_da_nhan.json"));
        Assert.DoesNotContain("phu_shopee_cancelled_orders_v1.json", report.UnhandledFiles);
        Assert.DoesNotContain("khoi_phuc_don_da_nhan.json", report.UnhandledFiles);
    }

    [Fact]
    public void Non_json_wrapped_values_are_kept_verbatim_not_failed()
    {
        WriteMain(MainRow("2611FAKE0001", "TRK1"));
        WriteWrappedRaw("phu_shopee_excel_active_import_week_v2.json", "abc");
        WriteWrappedRaw("phu_shopee_preferred_platform_workspace.json", "Tx");

        var report = LegacyDryRun.Analyze(_dir);

        Assert.Empty(report.Errors);
        Assert.Contains(report.VerbatimFiles, f => f == "phu_shopee_excel_active_import_week_v2.json");
        Assert.Contains(report.VerbatimFiles, f => f == "phu_shopee_preferred_platform_workspace.json");
    }

    [Fact]
    public void Bad_row_is_reported_with_index_and_other_rows_continue()
    {
        File.WriteAllText(Path.Combine(_dir, "du_lieu_hang_hoan.json"),
            "[" + JsonSerializer.Serialize(MainRow("2611OK", "TRKOK")) + ",\"không phải đối tượng\"," + JsonSerializer.Serialize(MainRow("2611OK2", "TRKOK2")) + "]");

        var report = LegacyDryRun.Analyze(_dir);

        Assert.Equal(3, report.Main.Read);
        Assert.Equal(2, report.Main.Mapped);
        var err = Assert.Single(report.Errors);
        Assert.Equal(1, err.Index);
        Assert.Equal("du_lieu_hang_hoan.json", err.File);
    }

    [Fact]
    public void TikTok_Shop_is_normalized_to_TikTok_and_original_json_is_kept()
    {
        WriteMain(MainRow("2611T1", "T1", "TikTok Shop"), MainRow("2611T2", "T2", "TikTok"), MainRow("2611S1", "S1", "Shopee"));

        var report = LegacyDryRun.Analyze(_dir);
        var recs = LegacyDryRun.LoadOrders(_dir, LegacyCategory.Main);

        Assert.Equal(new[] { "TikTok", "TikTok", "Shopee" }, recs.Select(r => r.Order.Platform).ToArray());
        Assert.Equal(1, report.PlatformRenamed);                 // chỉ dòng "TikTok Shop" bị đổi
        Assert.Contains("\"TikTok Shop\"", recs[0].RawJson);     // bản gốc không mất
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void Same_order_written_as_TikTok_Shop_and_TikTok_is_one_dedup_key()
    {
        WriteMain(MainRow("2611T1", "T1", "TikTok Shop"));
        WriteWrapped("phu_shopee_archived_orders_v1.json", new[] { MainRow("2611T1", "T1", "TikTok") });

        var report = LegacyDryRun.Analyze(_dir);

        Assert.Single(report.Duplicates); // phát hiện đúng nhờ chuẩn hóa nhãn sàn
    }

    [Fact]
    public void Unmapped_fields_and_items_are_preserved_for_lossless_import()
    {
        WriteMain(MainRow("2611FAKE0001", "TRK1"));

        var rec = Assert.Single(LegacyDryRun.LoadOrders(_dir, LegacyCategory.Main));

        Assert.Single(rec.Order.Items);
        Assert.Equal(2, rec.Order.Items[0].Qty);
        Assert.Contains("rawDetails", rec.Order.ExtraJson);
        Assert.Contains("TRK1-OLD", rec.Order.TrackingCodes);
        Assert.Contains("\"orderId\"", rec.RawJson); // giữ nguyên JSON gốc để đối chiếu
    }

    [Fact]
    public void Missing_folder_or_main_file_gives_clear_error_not_exception()
    {
        var report = LegacyDryRun.Analyze(Path.Combine(_dir, "khong-ton-tai"));

        Assert.NotEmpty(report.Errors);
        Assert.Equal(0, report.Main.Read);
    }
}
