using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Data;

public enum ImportSource { TikTok, Shopee }

public sealed class TikTokImportResult
{
    public bool Ok { get; init; } = true;
    public string Message { get; init; } = "";
    public int Added { get; set; }
    public int Updated { get; set; }
    public int EnrichedArchived { get; set; }
    public int SkippedDeleted { get; set; }
    public int SkippedArchivedUnchanged { get; set; }
    /// <summary>Dòng file bị lọc vì không phải đơn hoàn/hủy/thất bại (chế độ "chỉ đơn hoàn").</summary>
    public int RowsSkipped { get; set; }
    public int FailedDelivery { get; set; }
    public List<AmbiguousParcel> Ambiguous { get; } = new();
}

/// <summary>
/// Nạp bảng file TikTok/Salework (đã đọc bằng SpreadsheetReader) vào SQLite: ánh xạ -> gộp vào đơn có sẵn -> lưu, cả lô MỘT giao dịch.
/// Lỗi giữa chừng thì KHÔNG đổi gì. Không bao giờ ghi trường do kho nhập tay (trạng thái nhận, ngày nhận, ảnh, ghi chú của chủ shop, theo dõi).
/// </summary>
public static class TikTokImportService
{
    public static TikTokImportResult Apply(string dbPath, IReadOnlyList<Dictionary<string, string>> rows, TikTokMapOptions options,
        ImportProductMode mode, DateTime now) =>
        ApplyGroups(dbPath, new[] { rows }, options, mode, now);

    /// <summary>
    /// Nạp nhiều nhóm dòng (mỗi nhóm một cấu trúc cột, xem PlatformImportPrep) trong MỘT giao dịch: nhóm sau thấy kết quả của nhóm trước,
    /// lỗi giữa chừng thì không nhóm nào được ghi.
    /// </summary>
    public static TikTokImportResult ApplyGroups(string dbPath, IReadOnlyList<IReadOnlyList<Dictionary<string, string>>> groups,
        TikTokMapOptions options, ImportProductMode mode, DateTime now, ImportSource source = ImportSource.TikTok)
    {
        if (!File.Exists(dbPath)) return new TikTokImportResult { Ok = false, Message = "Chưa có cơ sở dữ liệu." };
        try
        {
            var stored = OrderRepository.Load(dbPath);

            var main = stored.Where(s => !s.IsArchived && !s.IsDeleted).Select(s => s.Order).ToList();
            var others = stored.Where(s => s.IsArchived && !s.IsDeleted).Select(s => new OtherEntry(s.Order, OtherWhere.Archive))
                .Concat(stored.Where(s => s.IsDeleted).Select(s => new OtherEntry(s.Order, OtherWhere.Deleted))).ToList();
            var original = main.ToHashSet();

            var outcome = new MergeOutcome();
            var skipped = 0;
            var failed = 0;
            foreach (var group in groups)
            {
                IReadOnlyList<ImportedParcel> parcels;
                Func<ImportedParcel, DateTime, ImportedParcel>? forNewRow = null;
                if (source == ImportSource.Shopee)
                {
                    var sm = ShopeeMapper.Map(group, new ShopeeMapOptions { OnlyReturns = options.OnlyReturns, Today = options.Today });
                    skipped += sm.SkippedSuccess;
                    parcels = sm.Parcels;
                    forNewRow = ShopeeMapper.ForNewRow;
                }
                else
                {
                    var map = TikTokMapper.Map(group, options);
                    skipped += map.Skipped;
                    failed += map.FailedDelivery;
                    parcels = map.Parcels;
                }
                var part = TikTokMerger.Apply(main, others, parcels, mode, options.Today, forNewRow);
                outcome.Added.AddRange(part.Added);
                outcome.Updated.AddRange(part.Updated);
                outcome.EnrichedArchived.AddRange(part.EnrichedArchived);
                outcome.Ambiguous.AddRange(part.Ambiguous);
                outcome.SkippedDeleted += part.SkippedDeleted;
                outcome.SkippedArchivedUnchanged += part.SkippedArchivedUnchanged;
                outcome.Enriched += part.Enriched;
            }

            var result = new TikTokImportResult
            {
                Added = outcome.Added.Count,
                Updated = outcome.Updated.Distinct().Count(o => original.Contains(o)),
                EnrichedArchived = outcome.EnrichedArchived.Distinct().Count(),
                SkippedDeleted = outcome.SkippedDeleted,
                SkippedArchivedUnchanged = outcome.SkippedArchivedUnchanged,
                RowsSkipped = skipped,
                FailedDelivery = failed,
            };
            result.Ambiguous.AddRange(outcome.Ambiguous);

            Persist(dbPath, outcome, result, now, source);
            return result;
        }
        catch (Exception ex)
        {
            return new TikTokImportResult { Ok = false, Message = "Nạp file thất bại, không có gì bị thay đổi: " + ex.Message };
        }
    }

    private static void Persist(string dbPath, MergeOutcome outcome, TikTokImportResult res, DateTime now, ImportSource source)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using var tx = conn.BeginTransaction();

        foreach (var o in outcome.Updated.Distinct().Where(o => !outcome.Added.Contains(o)).Concat(outcome.EnrichedArchived.Distinct()))
            OrderStore.UpdateImportedFields(conn, tx, o);
        foreach (var o in outcome.Added) OrderStore.Insert(conn, tx, o, "import|" + o.Id);

        OrderStore.AppendAudit(conn, tx, "import", now,
            $"Nạp file {(source == ImportSource.Shopee ? "Shopee" : "TikTok/Salework")}: thêm {res.Added}, cập nhật {res.Updated}, bổ sung kho lưu trữ {res.EnrichedArchived}, " +
            $"bỏ qua đơn đã xóa {res.SkippedDeleted}, nhập nhằng {res.Ambiguous.Count}, lọc bỏ {res.RowsSkipped} dòng không phải đơn hoàn");
        tx.Commit();
    }
}

/// <summary>Ghi đơn vào bảng orders/order_items (dùng chung cho các nguồn nạp). Chỉ ghi các trường DỮ LIỆU SÀN, không bao giờ ghi trường kho.</summary>
internal static class OrderStore
{
    public static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql, params (string Name, object? Value)[] ps)
    {
        using var c = conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = sql;
        foreach (var (n, v) in ps) c.Parameters.AddWithValue(n, v ?? DBNull.Value);
        c.ExecuteNonQuery();
    }

    public static object? Scalar(SqliteConnection conn, SqliteTransaction tx, string sql, params (string Name, object? Value)[] ps)
    {
        using var c = conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = sql;
        foreach (var (n, v) in ps) c.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return c.ExecuteScalar();
    }

    public static void AppendAudit(SqliteConnection conn, SqliteTransaction tx, string type, DateTime now, string detail)
    {
        var seq = Convert.ToInt32(Scalar(conn, tx, "SELECT COALESCE(MAX(seq)+1,0) FROM audit_log"));
        Exec(conn, tx, "INSERT INTO audit_log VALUES ($s,$t,$ty,$d,'{}')",
            ("$s", seq), ("$t", now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)), ("$ty", type), ("$d", detail));
    }

    private static void WriteItems(SqliteConnection conn, SqliteTransaction tx, string key, List<OrderItem> items)
    {
        Exec(conn, tx, "DELETE FROM order_items WHERE source_key=$k", ("$k", key));
        for (var i = 0; i < items.Count; i++)
            Exec(conn, tx, "INSERT INTO order_items (source_key, line_no, product, color, size, qty, sku, sku_id, import_prices, returned_qty) VALUES ($k,$n,$p,$c,$s,$q,$sku,$skuid,$ip,$rq)",
                ("$k", key), ("$n", i), ("$p", items[i].Product), ("$c", items[i].Color), ("$s", items[i].Size), ("$q", items[i].Qty), ("$rq", items[i].ReturnedQty),
                ("$sku", items[i].Sku), ("$skuid", items[i].SkuId),
                ("$ip", items[i].ImportPrices is { Count: > 0 } ? JsonSerializer.Serialize(items[i].ImportPrices) : null));
    }

    /// <summary>Cập nhật dữ liệu sàn của đơn có sẵn (theo id). KHÔNG ghi: status, recv_date, photo_id, user_note, watchlist, cờ lưu trữ/xóa.</summary>
    public static void UpdateImportedFields(SqliteConnection conn, SqliteTransaction tx, Order o)
    {
        var key = Scalar(conn, tx, "SELECT source_key FROM orders WHERE id=$id", ("$id", o.Id)) as string
                  ?? throw new InvalidOperationException("Không tìm thấy dòng cần cập nhật.");
        var extra = Scalar(conn, tx, "SELECT extra_json FROM orders WHERE id=$id", ("$id", o.Id)) as string;

        Exec(conn, tx, """
            UPDATE orders SET v2_modified=1, order_id=$oid, return_order_id=$rid, track=$trk, return_track=$rtrk, alt_track=$alt, forward_track=$fwd,
                tracking_codes=$codes, platform=$plat, order_type=$otype, platform_status=$pstat, pre_order=$pre, carrier=$car,
                ship_date=$ship, return_date=$ret, returned_at=$retat, price=$price, note=$note, buyer_note=$bnote,
                is_partial_return=$part, deadline=$dl, extra_json=$extra,
                -- ngoại lệ DUY NHẤT (quyết định 26/11/2026: đơn "Hủy sau chuẩn bị" có hàng về): đơn hủy đang "Không cần nhận" vì lúc nhập
                -- chưa có mã, nay có mã -> chờ nhận. Mọi trạng thái kho khác do quét / người dùng quyết định, nhập không đụng.
                status = CASE WHEN status = 'Không cần nhận' AND $otype = 'Đơn hủy' AND $codes <> '[]' AND $newst IN ('Chưa trả','Chưa về')
                              THEN $newst ELSE status END
            WHERE id=$id
            """,
            ("$newst", o.Status),
            ("$oid", o.OrderId), ("$rid", o.ReturnOrderId), ("$trk", o.Track), ("$rtrk", o.ReturnTrack), ("$alt", o.AltTrack), ("$fwd", o.ForwardTrack),
            ("$codes", JsonSerializer.Serialize(o.TrackingCodes)), ("$plat", o.Platform), ("$otype", o.OrderType), ("$pstat", o.PlatformStatus),
            ("$pre", o.PreOrder), ("$car", o.Carrier), ("$ship", o.ShipDate), ("$ret", o.ReturnDate), ("$retat", o.ReturnedAt),
            ("$price", (double)o.Price), ("$note", o.Note), ("$bnote", o.BuyerNote), ("$part", o.IsPartialReturn ? 1 : 0),
            ("$dl", o.Deadline), ("$extra", OrderExtra.Write(extra, o.Import)), ("$id", o.Id));
        WriteItems(conn, tx, key, o.Items);
    }

    public static void Insert(SqliteConnection conn, SqliteTransaction tx, Order o, string sourceKey)
    {
        Exec(conn, tx, """
            INSERT INTO orders (source_key, id, order_id, return_order_id, track, return_track, alt_track, forward_track, tracking_codes,
                scan_aliases, platform, order_type, platform_status, pre_order, status, carrier, ship_date, return_date, recv_date,
                returned_at, deadline, price, note, buyer_note, user_note, import_week, imported_at, is_watchlist, watchlist_reason,
                is_partial_return, photo_id, extra_json, raw_json, v2_modified)
            VALUES ($k,$id,$oid,$rid,$trk,$rtrk,$alt,$fwd,$codes,$alias,$plat,$otype,$pstat,$pre,$status,$car,$ship,$ret,$recv,
                $retat,$dl,$price,$note,$bnote,$unote,$week,$impat,$wl,$wlr,$part,$photo,$extra,'',1)
            """,
            ("$k", sourceKey), ("$id", o.Id), ("$oid", o.OrderId), ("$rid", o.ReturnOrderId), ("$trk", o.Track), ("$rtrk", o.ReturnTrack),
            ("$alt", o.AltTrack), ("$fwd", o.ForwardTrack), ("$codes", JsonSerializer.Serialize(o.TrackingCodes)),
            ("$alias", JsonSerializer.Serialize(o.ScanAliases)), ("$plat", o.Platform), ("$otype", o.OrderType), ("$pstat", o.PlatformStatus),
            ("$pre", o.PreOrder), ("$status", o.Status), ("$car", o.Carrier), ("$ship", o.ShipDate), ("$ret", o.ReturnDate), ("$recv", o.RecvDate),
            ("$retat", o.ReturnedAt), ("$dl", o.Deadline), ("$price", (double)o.Price), ("$note", o.Note), ("$bnote", o.BuyerNote),
            ("$unote", o.UserNote), ("$week", o.ImportWeek), ("$impat", o.ImportedAt), ("$wl", o.IsWatchlist ? 1 : 0), ("$wlr", o.WatchlistReason),
            ("$part", o.IsPartialReturn ? 1 : 0), ("$photo", o.PhotoId), ("$extra", OrderExtra.Write(null, o.Import)));
        WriteItems(conn, tx, sourceKey, o.Items);
    }
}
