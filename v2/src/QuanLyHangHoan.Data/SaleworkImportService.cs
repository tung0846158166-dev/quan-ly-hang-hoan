using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data.Import;

namespace QuanLyHangHoan.Data;

public sealed class SaleworkImportResult
{
    public bool Ok { get; init; } = true;
    public string Message { get; init; } = "";
    public TikTokImportResult? Import { get; init; }
    /// <summary>Dòng chờ cũ (chưa có mã vận đơn) đã chuyển dữ liệu kho sang dòng Salework mới và được xóa MỀM.</summary>
    public int SupersededMerged { get; init; }
    public int SupersedeAmbiguous { get; init; }
    public int FilledTracks { get; init; }
}

/// <summary>
/// Nạp bảng "Đang chờ hoàn" lấy từ Salework (tự động hoặc file tải tay): nạp như file TikTok, rồi áp supersedeStaleRowsFromSalework.
/// Việc mở cửa sổ Salework / đăng nhập / bấm nút (WebView2) thuộc phần giao diện; ở đây chỉ nhận các dòng đã đọc.
/// </summary>
public static class SaleworkImportService
{
    public static SaleworkImportResult Apply(string dbPath, IReadOnlyList<Dictionary<string, string>> rows, TikTokMapOptions options,
        ImportProductMode mode, DateTime now)
    {
        var clean = SpreadsheetReader.DropPersonalColumns(rows);
        var prep = PlatformImportPrep.Prepare(new[] { new SourceFile("salework_dang_cho_hoan", clean) });
        var import = TikTokImportService.ApplyGroups(dbPath, prep.Groups.Select(g => (IReadOnlyList<Dictionary<string, string>>)g).ToList(), options, mode, now);
        if (!import.Ok) return new SaleworkImportResult { Ok = false, Message = import.Message, Import = import };

        try
        {
            var idKey = clean.SelectMany(r => r.Keys).FirstOrDefault(k => LegacyText.Key(k) is "madonhang" or "orderid");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (idKey is not null)
                foreach (var r in clean) if (r.TryGetValue(idKey, out var v) && v.Trim().Length > 0) ids.Add(v.Trim().ToUpperInvariant());

            var stored = OrderRepository.Load(dbPath).Where(s => !s.IsArchived && !s.IsDeleted).ToList();
            var sup = SaleworkSupersede.Apply(stored.Select(s => s.Order).ToList(), ids);
            if (sup.Merged.Count > 0 || sup.Filled.Count > 0)
            {
                using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
                conn.Open();
                using var tx = conn.BeginTransaction();
                foreach (var keeper in sup.KeepersChanged)
                    OrderStore.Exec(conn, tx, "UPDATE orders SET status=$s, recv_date=$r, photo_id=$p, user_note=$u, is_watchlist=$w, watchlist_reason=$wr, v2_modified=1 WHERE id=$id",
                        ("$s", keeper.Status), ("$r", keeper.RecvDate), ("$p", keeper.PhotoId), ("$u", keeper.UserNote), ("$w", keeper.IsWatchlist ? 1 : 0), ("$wr", keeper.WatchlistReason), ("$id", keeper.Id));
                foreach (var o in sup.Filled)
                    OrderStore.Exec(conn, tx, "UPDATE orders SET forward_track=$f, return_track=$r, tracking_codes=$c, v2_modified=1 WHERE id=$id",
                        ("$f", o.ForwardTrack), ("$r", o.ReturnTrack), ("$c", JsonSerializer.Serialize(o.TrackingCodes)), ("$id", o.Id));
                foreach (var (stale, _) in sup.Merged)
                    OrderStore.Exec(conn, tx, "UPDATE orders SET is_deleted=1, deleted_at=$t, deleted_from='Salework thay thế', v2_modified=1 WHERE id=$id",
                        ("$t", now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)), ("$id", stale.Id));
                OrderStore.AppendAudit(conn, tx, "salework", now,
                    $"Salework đang chờ hoàn: gộp {sup.Merged.Count} dòng chờ cũ (xóa mềm), điền mã vận đơn cho {sup.Filled.Count} dòng, {sup.Ambiguous} đơn nhiều kiện giữ nguyên");
                tx.Commit();
            }
            return new SaleworkImportResult { Import = import, SupersededMerged = sup.Merged.Count, SupersedeAmbiguous = sup.Ambiguous, FilledTracks = sup.Filled.Count };
        }
        catch (Exception ex)
        {
            return new SaleworkImportResult { Ok = false, Import = import, Message = "Đã nạp nhưng chưa gộp được dòng chờ cũ: " + ex.Message };
        }
    }
}
