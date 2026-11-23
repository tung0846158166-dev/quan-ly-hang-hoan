using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Data;

public sealed record AmbiguousImport(string Platform, string OrderId, string Track, int Candidates);

public sealed class ImportApplyResult
{
    public bool Ok { get; init; } = true;
    public string Message { get; init; } = "";
    public int Inserted { get; set; }
    public int Updated { get; set; }
    /// <summary>Khớp một đơn ĐÃ XÓA: không nạp lại ("chống nạp lại"), không sửa.</summary>
    public int SkippedDeleted { get; set; }
    public List<AmbiguousImport> Ambiguous { get; } = new();
}

/// <summary>
/// Áp dụng một lô đơn nhập TỔNG QUÁT (đã đọc thành Order) vào SQLite, dùng ParcelUpsert (quy tắc 1-5):
///  - cùng kiện (cùng sàn + mã đơn + trùng mã riêng): CẬP NHẬT, thay sản phẩm (không cộng dồn), giữ trường do kho nhập tay;
///  - kiện A/B/C cùng mã đơn là các dòng riêng; nhiều ứng viên => báo Ambiguous, không đổi gì;
///  - khớp đơn đã xóa => bỏ qua (không hồi sinh), đơn lưu trữ => cập nhật tại chỗ, vẫn ở kho lưu trữ.
/// Cả lô trong MỘT giao dịch: lỗi giữa chừng thì không đổi gì.
/// (File TikTok/Salework dùng TikTokImportService, chính xác theo app cũ hơn.)
/// </summary>
public static class ImportApplier
{
    public static ImportApplyResult Apply(string dbPath, IEnumerable<Order> incoming, DateTime now)
    {
        if (!File.Exists(dbPath)) return new ImportApplyResult { Ok = false, Message = "Chưa có cơ sở dữ liệu." };

        var result = new ImportApplyResult();
        try
        {
            var stored = OrderRepository.Load(dbPath);
            var live = stored.Where(s => !s.IsDeleted).Select(s => s.Order).ToList();
            var deleted = stored.Where(s => s.IsDeleted).Select(s => s.Order).ToList();
            var originalIds = live.Select(o => o.Id).ToHashSet();

            var changed = new Dictionary<string, Order>();   // id -> đơn cần ghi (cập nhật)
            var added = new List<Order>();                    // đơn mới

            foreach (var raw in incoming)
            {
                var o = Clone(raw);
                o.Platform = LegacyMapper.NormalizePlatform(o.Platform);

                if (ParcelUpsert.FindMatches(deleted, o).Count > 0) { result.SkippedDeleted++; continue; }

                var r = ParcelUpsert.Apply(live, o);
                switch (r.Kind)
                {
                    case UpsertKind.Inserted:
                        added.Add(o); result.Inserted++; break;
                    case UpsertKind.Updated:
                        if (originalIds.Contains(r.Target!.Id)) changed[r.Target.Id] = r.Target;
                        // else: cập nhật một đơn vừa thêm trong cùng lô: đã nằm trong `added`
                        result.Updated++;
                        break;
                    default:
                        result.Ambiguous.Add(new AmbiguousImport(o.Platform, o.OrderId, o.Track, r.Candidates.Count));
                        break;
                }
            }

            Persist(dbPath, changed.Values, added, result, now);
            return result;
        }
        catch (Exception ex)
        {
            return new ImportApplyResult { Ok = false, Message = "Nhập thất bại, không có gì bị thay đổi: " + ex.Message };
        }
    }

    private static void Persist(string dbPath, IEnumerable<Order> updates, IEnumerable<Order> inserts, ImportApplyResult res, DateTime now)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using var tx = conn.BeginTransaction();

        foreach (var o in updates)
        {
            var key = (string?)Scalar(conn, tx, "SELECT source_key FROM orders WHERE id=$id", ("$id", o.Id))
                      ?? throw new InvalidOperationException("Không tìm thấy dòng cần cập nhật.");
            Exec(conn, tx, """
                UPDATE orders SET v2_modified=1, return_order_id=$rid, return_track=$rtrk, alt_track=$alt, forward_track=$fwd, tracking_codes=$codes,
                    order_type=$otype, platform_status=$pstat, pre_order=$pre, carrier=$car, ship_date=$ship, return_date=$ret,
                    deadline=$dl, price=$price, note=$note, buyer_note=$bnote, import_week=$week, imported_at=$impat,
                    is_partial_return=$part
                WHERE id=$id
                """,
                ("$rid", o.ReturnOrderId), ("$rtrk", o.ReturnTrack), ("$alt", o.AltTrack), ("$fwd", o.ForwardTrack),
                ("$codes", JsonSerializer.Serialize(o.TrackingCodes)), ("$otype", o.OrderType), ("$pstat", o.PlatformStatus),
                ("$pre", o.PreOrder), ("$car", o.Carrier), ("$ship", o.ShipDate), ("$ret", o.ReturnDate), ("$dl", o.Deadline),
                ("$price", (double)o.Price), ("$note", o.Note), ("$bnote", o.BuyerNote), ("$week", o.ImportWeek),
                ("$impat", o.ImportedAt), ("$part", o.IsPartialReturn ? 1 : 0), ("$id", o.Id));
            ReplaceItems(conn, tx, key, o.Items);
        }

        foreach (var o in inserts)
        {
            var key = "import|" + o.Id;
            Exec(conn, tx, """
                INSERT INTO orders (source_key, id, order_id, return_order_id, track, return_track, alt_track, forward_track, tracking_codes,
                    scan_aliases, platform, order_type, platform_status, pre_order, status, carrier, ship_date, return_date, recv_date,
                    returned_at, deadline, price, note, buyer_note, user_note, import_week, imported_at, is_watchlist, watchlist_reason,
                    is_partial_return, photo_id, extra_json, raw_json, v2_modified)
                VALUES ($k,$id,$oid,$rid,$trk,$rtrk,$alt,$fwd,$codes,$alias,$plat,$otype,$pstat,$pre,$status,$car,$ship,$ret,$recv,
                    $retat,$dl,$price,$note,$bnote,$unote,$week,$impat,$wl,$wlr,$part,$photo,$extra,'',1)
                """,
                ("$k", key), ("$id", o.Id), ("$oid", o.OrderId), ("$rid", o.ReturnOrderId), ("$trk", o.Track), ("$rtrk", o.ReturnTrack),
                ("$alt", o.AltTrack), ("$fwd", o.ForwardTrack), ("$codes", JsonSerializer.Serialize(o.TrackingCodes)),
                ("$alias", JsonSerializer.Serialize(o.ScanAliases)), ("$plat", o.Platform), ("$otype", o.OrderType),
                ("$pstat", o.PlatformStatus), ("$pre", o.PreOrder), ("$status", o.Status), ("$car", o.Carrier), ("$ship", o.ShipDate),
                ("$ret", o.ReturnDate), ("$recv", o.RecvDate), ("$retat", o.ReturnedAt), ("$dl", o.Deadline), ("$price", (double)o.Price),
                ("$note", o.Note), ("$bnote", o.BuyerNote), ("$unote", o.UserNote), ("$week", o.ImportWeek), ("$impat", o.ImportedAt),
                ("$wl", o.IsWatchlist ? 1 : 0), ("$wlr", o.WatchlistReason), ("$part", o.IsPartialReturn ? 1 : 0),
                ("$photo", o.PhotoId), ("$extra", o.ExtraJson));
            ReplaceItems(conn, tx, key, o.Items);
        }

        var seq = Convert.ToInt32(Scalar(conn, tx, "SELECT COALESCE(MAX(seq)+1,0) FROM audit_log"));
        Exec(conn, tx, "INSERT INTO audit_log VALUES ($s,$t,'import',$d,'{}')",
            ("$s", seq), ("$t", now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)),
            ("$d", $"Nhập: thêm {res.Inserted}, cập nhật {res.Updated}, bỏ qua đơn đã xóa {res.SkippedDeleted}, nhập nhằng {res.Ambiguous.Count}"));
        tx.Commit();
    }

    private static void ReplaceItems(SqliteConnection conn, SqliteTransaction tx, string key, List<OrderItem> items)
    {
        // Danh sách rỗng từ file nhập KHÔNG xóa sản phẩm đang có (ParcelUpsert chỉ thay khi file có sản phẩm).
        if (items.Count == 0) return;
        Exec(conn, tx, "DELETE FROM order_items WHERE source_key=$k", ("$k", key));
        for (var i = 0; i < items.Count; i++)
            Exec(conn, tx, "INSERT INTO order_items (source_key, line_no, product, color, size, qty, sku, sku_id) VALUES ($k,$n,$p,$c,$s,$q,$sku,$skuid)",
                ("$k", key), ("$n", i), ("$p", items[i].Product), ("$c", items[i].Color), ("$s", items[i].Size), ("$q", items[i].Qty),
                ("$sku", (object?)items[i].Sku ?? DBNull.Value), ("$skuid", (object?)items[i].SkuId ?? DBNull.Value));
    }

    private static Order Clone(Order o) => new()
    {
        Id = Guid.NewGuid().ToString("N"), OrderId = o.OrderId, ReturnOrderId = o.ReturnOrderId, Track = o.Track,
        ReturnTrack = o.ReturnTrack, AltTrack = o.AltTrack, ForwardTrack = o.ForwardTrack,
        TrackingCodes = new(o.TrackingCodes), ScanAliases = new(o.ScanAliases), Platform = o.Platform, OrderType = o.OrderType,
        PlatformStatus = o.PlatformStatus, PreOrder = o.PreOrder, Status = o.Status, Carrier = o.Carrier, ShipDate = o.ShipDate,
        ReturnDate = o.ReturnDate, RecvDate = o.RecvDate, ReturnedAt = o.ReturnedAt, Deadline = o.Deadline, Price = o.Price,
        Note = o.Note, BuyerNote = o.BuyerNote, UserNote = o.UserNote, ImportWeek = o.ImportWeek, ImportedAt = o.ImportedAt,
        IsWatchlist = o.IsWatchlist, WatchlistReason = o.WatchlistReason, IsPartialReturn = o.IsPartialReturn, PhotoId = o.PhotoId,
        Items = o.Items.Select(i => new OrderItem { Product = i.Product, Color = i.Color, Size = i.Size, Qty = i.Qty, Sku = i.Sku, SkuId = i.SkuId }).ToList(),
        ExtraJson = o.ExtraJson,
    };

    private static object? Scalar(SqliteConnection conn, SqliteTransaction tx, string sql, params (string Name, object Value)[] ps)
    {
        using var c = conn.CreateCommand();
        c.Transaction = tx; c.CommandText = sql;
        foreach (var (n, v) in ps) c.Parameters.AddWithValue(n, v);
        return c.ExecuteScalar();
    }

    private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql, params (string Name, object Value)[] ps)
    {
        using var c = conn.CreateCommand();
        c.Transaction = tx; c.CommandText = sql;
        foreach (var (n, v) in ps) c.Parameters.AddWithValue(n, v);
        c.ExecuteNonQuery();
    }
}
