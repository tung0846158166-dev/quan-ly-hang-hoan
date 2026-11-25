using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace QuanLyHangHoan.Data.Cloud;

/// <summary>
/// Bản sao cục bộ của dữ liệu cloud trong SQLite (cùng bảng `orders`/`order_items` mà mọi màn hình đang đọc), nên giao diện không phải đổi.
/// Chỉ ghi các cột có trên cloud; KHÔNG đụng `buyer_note`, `raw_json` và các khóa khác của `extra_json`. Mọi dòng áp từ cloud được đánh dấu
/// `v2_modified=1` để nhập lại dữ liệu cũ không ghi đè. Không bao giờ xóa dòng (cloud cũng không xóa cứng).
/// </summary>
public static class CloudReplica
{
    private const string MetaSql = "CREATE TABLE IF NOT EXISTS cloud_meta (k TEXT PRIMARY KEY, v TEXT NOT NULL)";
    /// <summary>Băm nội dung từng kiện ở lần cuối ĐỒNG BỘ với cloud (áp từ cloud hoặc đã đẩy lên): kiện nào băm hiện tại khác thì đã đổi ở máy.</summary>
    internal const string PublishedSql = "CREATE TABLE IF NOT EXISTS cloud_published (id TEXT PRIMARY KEY, hash TEXT NOT NULL)";

    private static SqliteConnection Open(string dbPath)
    {
        var c = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        c.Open();
        using var p = c.CreateCommand();
        p.CommandText = "PRAGMA busy_timeout = 15000";
        p.ExecuteNonQuery();
        return c;
    }

    public static void EnsureMeta(string dbPath)
    {
        using var c = Open(dbPath);
        using var cmd = c.CreateCommand();
        cmd.CommandText = MetaSql;
        cmd.ExecuteNonQuery();
    }

    public static long GetRev(string dbPath)
    {
        if (!File.Exists(dbPath)) return 0;
        using var c = Open(dbPath);
        using var cmd = c.CreateCommand();
        cmd.CommandText = MetaSql + "; SELECT v FROM cloud_meta WHERE k = 'rev'";
        return long.TryParse(cmd.ExecuteScalar()?.ToString(), out var r) ? r : 0;
    }

    /// <summary>Giờ vào Bảng Hàng Hoàn Về (ms) của một đơn trong bản sao; 0 nếu chưa có dấu.</summary>
    public static long WatchSince(string dbPath, string orderId)
    {
        if (!File.Exists(dbPath)) return 0;
        using var c = Open(dbPath);
        using var cmd = c.CreateCommand();
        cmd.CommandText = ReturnWatchService.TableSql + "SELECT since FROM return_watch WHERE order_id = $i";
        cmd.Parameters.AddWithValue("$i", orderId);
        return long.TryParse(cmd.ExecuteScalar()?.ToString(), out var s) ? s : 0;
    }

    /// <summary>Quên mốc đồng bộ (ví dụ khi ghép sang cloud khác): lần sau lấy lại toàn bộ.</summary>
    public static void ResetRev(string dbPath)
    {
        using var c = Open(dbPath);
        using var cmd = c.CreateCommand();
        cmd.CommandText = MetaSql + "; DELETE FROM cloud_meta WHERE k = 'rev'";
        cmd.ExecuteNonQuery();
    }

    /// <summary>Áp một lô kiện từ cloud trong MỘT giao dịch; nếu <paramref name="newRev"/> có giá trị thì lưu mốc đồng bộ cùng giao dịch.</summary>
    public static int Apply(string dbPath, IEnumerable<JsonObject> parcels, long? newRev, IEnumerable<CloudWatchChange>? watch = null)
    {
        using var c = Open(dbPath);
        using var tx = c.BeginTransaction();
        using (var m = c.CreateCommand()) { m.Transaction = tx; m.CommandText = MetaSql + ";" + PublishedSql + ";" + ReturnWatchService.TableSql; m.ExecuteNonQuery(); }
        var n = 0;
        foreach (var p in parcels) { ApplyOne(c, tx, p); n++; }
        // Bảng Hàng Hoàn Về: cùng bảng `return_watch` mà màn hình đang đọc; cloud là nguồn gốc nên ghi đè giờ vào + cờ bỏ
        foreach (var w in watch ?? Array.Empty<CloudWatchChange>())
        {
            using var wc = c.CreateCommand();
            wc.Transaction = tx;
            wc.CommandText = "INSERT INTO return_watch(order_id, since, off) VALUES($i,$s,$o) ON CONFLICT(order_id) DO UPDATE SET since = excluded.since, off = excluded.off";
            wc.Parameters.AddWithValue("$i", w.Key);
            wc.Parameters.AddWithValue("$s", w.Since);
            wc.Parameters.AddWithValue("$o", w.Off ? 1 : 0);
            wc.ExecuteNonQuery();
            n++;
        }
        if (newRev is { } rev)
        {
            using var m = c.CreateCommand();
            m.Transaction = tx;
            m.CommandText = "INSERT INTO cloud_meta(k, v) VALUES ('rev', $v) ON CONFLICT(k) DO UPDATE SET v = excluded.v";
            m.Parameters.AddWithValue("$v", rev.ToString());
            m.ExecuteNonQuery();
        }
        tx.Commit();
        return n;
    }

    private static string S(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    private static int Flag(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b ? 1 : 0;
    private static string Json(JsonNode? n)
    {
        var a = new JsonArray();
        if (n is JsonArray src) foreach (var x in src) a.Add(S(x));
        return a.ToJsonString();
    }

    private static void ApplyOne(SqliteConnection c, SqliteTransaction tx, JsonObject p)
    {
        var id = S(p["id"]);
        if (id.Length == 0) return;

        string? key = null, existingExtra = null;
        using (var q = c.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT source_key, extra_json FROM orders WHERE id = $id LIMIT 1";
            q.Parameters.AddWithValue("$id", id);
            using var r = q.ExecuteReader();
            if (r.Read()) { key = r.GetString(0); existingExtra = r.GetString(1); }
        }

        var extra = ParcelMapper.ExtraFrom(p["extra"] as JsonObject, existingExtra);
        double price = (double)ParcelMapper.Num(p["price"]);

        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        var isNew = key is null;
        key ??= "cloud:" + id;
        cmd.CommandText = isNew
            ? """
              INSERT INTO orders(source_key, id, order_id, return_order_id, track, return_track, alt_track, forward_track, tracking_codes, scan_aliases,
                platform, order_type, platform_status, pre_order, status, carrier, ship_date, return_date, recv_date, returned_at, deadline,
                price, note, buyer_note, user_note, import_week, imported_at, is_watchlist, watchlist_reason, is_partial_return, photo_id, extra_json,
                is_archived, archived_at, is_deleted, deleted_at, deleted_from, raw_json, v2_modified)
              VALUES($k, $id, $order_id, $return_order_id, $track, $return_track, $alt_track, $forward_track, $tracking_codes, $scan_aliases,
                $platform, $order_type, $platform_status, $pre_order, $status, $carrier, $ship_date, $return_date, $recv_date, $returned_at, $deadline,
                $price, $note, '', $user_note, $import_week, $imported_at, $is_watchlist, $watchlist_reason, $is_partial_return, $photo_id, $extra,
                $is_archived, $archived_at, $is_deleted, $deleted_at, $deleted_from, '', 1)
              """
            : """
              UPDATE orders SET order_id=$order_id, return_order_id=$return_order_id, track=$track, return_track=$return_track, alt_track=$alt_track,
                forward_track=$forward_track, tracking_codes=$tracking_codes, scan_aliases=$scan_aliases, platform=$platform, order_type=$order_type,
                platform_status=$platform_status, pre_order=$pre_order, status=$status, carrier=$carrier, ship_date=$ship_date, return_date=$return_date,
                recv_date=$recv_date, returned_at=$returned_at, deadline=$deadline, price=$price, note=$note, user_note=$user_note,
                import_week=$import_week, imported_at=$imported_at, is_watchlist=$is_watchlist, watchlist_reason=$watchlist_reason,
                is_partial_return=$is_partial_return, photo_id=$photo_id, extra_json=$extra, is_archived=$is_archived, archived_at=$archived_at,
                is_deleted=$is_deleted, deleted_at=$deleted_at, deleted_from=$deleted_from, v2_modified=1
              WHERE source_key=$k
              """;
        void P(string n, object? v) => cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        P("$k", key); P("$id", id); P("$order_id", S(p["orderId"])); P("$return_order_id", S(p["returnOrderId"]));
        P("$track", S(p["track"])); P("$return_track", S(p["returnTrack"])); P("$alt_track", S(p["altTrack"])); P("$forward_track", S(p["forwardTrack"]));
        P("$tracking_codes", Json(p["trackingCodes"])); P("$scan_aliases", Json(p["scanAliases"]));
        P("$platform", S(p["platform"])); P("$order_type", S(p["orderType"])); P("$platform_status", S(p["platformStatus"])); P("$pre_order", S(p["preOrder"]));
        P("$status", S(p["status"])); P("$carrier", S(p["carrier"]));
        P("$ship_date", S(p["shipDate"])); P("$return_date", S(p["returnDate"])); P("$recv_date", S(p["recvDate"]));
        P("$returned_at", S(p["returnedAt"])); P("$deadline", S(p["deadline"]));
        P("$price", price); P("$note", S(p["note"])); P("$user_note", S(p["userNote"]));
        P("$import_week", S(p["importWeek"])); P("$imported_at", S(p["importedAt"]));
        P("$is_watchlist", Flag(p["isWatchlist"])); P("$watchlist_reason", S(p["watchlistReason"])); P("$is_partial_return", Flag(p["isPartialReturn"]));
        P("$photo_id", S(p["photoId"])); P("$extra", extra);
        P("$is_archived", Flag(p["isArchived"])); P("$archived_at", S(p["archivedAt"]));
        P("$is_deleted", Flag(p["isDeleted"])); P("$deleted_at", S(p["deletedAt"])); P("$deleted_from", S(p["deletedFrom"]));
        cmd.ExecuteNonQuery();

        using (var ph = c.CreateCommand())
        {
            ph.Transaction = tx;
            ph.CommandText = "INSERT INTO cloud_published(id, hash) VALUES($i,$h) ON CONFLICT(id) DO UPDATE SET hash = excluded.hash";
            ph.Parameters.AddWithValue("$i", id);
            ph.Parameters.AddWithValue("$h", ParcelMapper.Hash(ParcelMapper.ToParcel(ParcelMapper.ToStored(p))));
            ph.ExecuteNonQuery();
        }

        // sản phẩm: thay toàn bộ các dòng của kiện này (đúng với nội dung cloud)
        using (var del = c.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM order_items WHERE source_key = $k";
            del.Parameters.AddWithValue("$k", key);
            del.ExecuteNonQuery();
        }
        var line = 0;
        if (p["items"] is JsonArray items)
            foreach (var i in items)
            {
                if (i is not JsonObject it) continue;
                using var ins = c.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO order_items(source_key, line_no, product, color, size, qty, sku, sku_id, returned_qty) VALUES($k,$n,$p,$c,$s,$q,$sku,$skuid,$rq)";
                ins.Parameters.AddWithValue("$k", key);
                ins.Parameters.AddWithValue("$n", line++);
                ins.Parameters.AddWithValue("$p", S(it["product"]));
                ins.Parameters.AddWithValue("$c", S(it["color"]));
                ins.Parameters.AddWithValue("$s", S(it["size"]));
                ins.Parameters.AddWithValue("$q", (int)ParcelMapper.Num(it["qty"]));
                ins.Parameters.AddWithValue("$sku", it["sku"] is null ? DBNull.Value : S(it["sku"]));
                ins.Parameters.AddWithValue("$skuid", it["skuId"] is null ? DBNull.Value : S(it["skuId"]));
                ins.Parameters.AddWithValue("$rq", (int)ParcelMapper.Num(it["returnedQty"]));
                ins.ExecuteNonQuery();
            }
    }
}
