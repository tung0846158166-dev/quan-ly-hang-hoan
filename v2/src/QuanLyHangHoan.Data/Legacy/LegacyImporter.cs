using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace QuanLyHangHoan.Data.Legacy;

public sealed class ImportResult
{
    public int Inserted { get; set; }
    public int Updated { get; set; }
    /// <summary>Số đơn app cũ đã dời sang nhóm khác (chính / lưu trữ / đã xóa) được đổi cờ thay vì tạo dòng mới.</summary>
    public int Moved { get; set; }
}

/// <summary>
/// Nhập thật dữ liệu cũ (chính / lưu trữ / đã xóa) vào SQLite. Chỉ ĐỌC thư mục nguồn (hãy truyền BẢN SAO).
/// Idempotent: khóa nguồn = nhóm + DedupKey + số thứ tự trong nhóm; nhập lại thì cập nhật, không thêm dòng.
/// Không gộp, không xóa: bản ghi trùng khóa vẫn giữ riêng từng dòng để chủ dự án quyết.
/// </summary>
public static class LegacyImporter
{
    public const string Schema = """
        CREATE TABLE IF NOT EXISTS orders (
            source_key TEXT PRIMARY KEY,
            id TEXT NOT NULL,
            order_id TEXT NOT NULL, return_order_id TEXT NOT NULL, track TEXT NOT NULL, return_track TEXT NOT NULL,
            alt_track TEXT NOT NULL, forward_track TEXT NOT NULL, tracking_codes TEXT NOT NULL, scan_aliases TEXT NOT NULL,
            platform TEXT NOT NULL, order_type TEXT NOT NULL, platform_status TEXT NOT NULL, pre_order TEXT NOT NULL,
            status TEXT NOT NULL, carrier TEXT NOT NULL,
            ship_date TEXT NOT NULL, return_date TEXT NOT NULL, recv_date TEXT NOT NULL, returned_at TEXT NOT NULL, deadline TEXT NOT NULL,
            price REAL NOT NULL, note TEXT NOT NULL, buyer_note TEXT NOT NULL, user_note TEXT NOT NULL,
            import_week TEXT NOT NULL, imported_at TEXT NOT NULL,
            is_watchlist INTEGER NOT NULL, watchlist_reason TEXT NOT NULL, is_partial_return INTEGER NOT NULL,
            photo_id TEXT NOT NULL, extra_json TEXT NOT NULL,
            is_archived INTEGER NOT NULL DEFAULT 0, archived_at TEXT NOT NULL DEFAULT '',
            is_deleted INTEGER NOT NULL DEFAULT 0, deleted_at TEXT NOT NULL DEFAULT '', deleted_from TEXT NOT NULL DEFAULT '',
            raw_json TEXT NOT NULL,
            -- 1 = v2 đã quét/sửa tay/dời lưu trữ/nạp file: nhập lại dữ liệu cũ CHỈ được bổ sung chỗ trống, không ghi đè.
            v2_modified INTEGER NOT NULL DEFAULT 0,
            -- khiếu nại (26/11/2026): "" = không khiếu nại
            complaint_status TEXT NOT NULL DEFAULT '', complaint_at TEXT NOT NULL DEFAULT '', complaint_note TEXT NOT NULL DEFAULT '',
            complaint_done_at TEXT NOT NULL DEFAULT ''
        );
        CREATE INDEX IF NOT EXISTS ix_orders_order_id ON orders(order_id);
        CREATE INDEX IF NOT EXISTS ix_orders_track ON orders(track);
        CREATE TABLE IF NOT EXISTS order_items (
            source_key TEXT NOT NULL, line_no INTEGER NOT NULL,
            product TEXT NOT NULL, color TEXT NOT NULL, size TEXT NOT NULL, qty INTEGER NOT NULL, sku TEXT, sku_id TEXT,
            import_prices TEXT,
            PRIMARY KEY (source_key, line_no)
        );
        -- Ảnh theo photo_id. source: 'file' (photos/, ưu tiên) | 'inline' (data-URI nhúng, dự phòng) | 'missing' (mất cả hai).
        -- inline_differs=1: có cả hai nguồn nhưng khác byte; ảnh nhúng vẫn còn nguyên trong orders.raw_json.
        CREATE TABLE IF NOT EXISTS photos (
            photo_id TEXT PRIMARY KEY, source TEXT NOT NULL, mime TEXT NOT NULL, data BLOB,
            sha256 TEXT NOT NULL DEFAULT '', inline_differs INTEGER NOT NULL DEFAULT 0
        );
        -- Điện thoại gửi lại cùng requestId thì trả đúng xác nhận cũ, không xử lý lại (đồng bộ điện thoại).
        CREATE TABLE IF NOT EXISTS sync_inbox (
            request_id TEXT PRIMARY KEY, code TEXT NOT NULL, ack_json TEXT NOT NULL, handled_at TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS audit_log (
            seq INTEGER PRIMARY KEY, time TEXT NOT NULL, type TEXT NOT NULL, detail TEXT NOT NULL, raw_json TEXT NOT NULL
        );
        -- Khóa cũ theo CHỈ SỐ DÒNG (vd "290:status"), không theo mã đơn: giữ nguyên văn, chưa liên kết đơn nào.
        CREATE TABLE IF NOT EXISTS cell_edit_history (
            row_ref TEXT NOT NULL, field TEXT NOT NULL, seq INTEGER NOT NULL,
            time TEXT NOT NULL, label TEXT NOT NULL, old_val TEXT NOT NULL, new_val TEXT NOT NULL, edit_type TEXT NOT NULL,
            linked_order_key TEXT, raw_json TEXT NOT NULL,
            PRIMARY KEY (row_ref, field, seq)
        );
        """;

    public const string AuditLogFile = "phu_shopee_excel_audit_log_v1.json";
    public const string CellEditFile = "phu_ShopeeReturnCellEditHistory_v1.json";

    /// <param name="photosFolder">Thư mục `photos/` (bản sao). Mặc định: legacyFolder\photos.</param>
    public static ImportResult Import(string legacyFolder, string dbPath, string? photosFolder = null)
    {
        var result = new ImportResult();
        AppDatabase.Initialize(dbPath); // tạo thư mục/bảng nếu chưa có, kiểm phiên bản schema
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();

        using var tx = conn.BeginTransaction();
        var claimed = new HashSet<string>(); // dòng đã khớp trong lần chạy này (để hai bản ghi không cùng khớp một dòng)
        foreach (var cat in new[] { LegacyCategory.Main, LegacyCategory.Archived, LegacyCategory.Deleted })
        {
            var ordinal = new Dictionary<string, int>();
            foreach (var rec in LegacyDryRun.LoadOrders(legacyFolder, cat))
            {
                var baseKey = $"{cat}|{rec.DedupKey}";
                ordinal[baseKey] = ordinal.GetValueOrDefault(baseKey) + 1;
                Upsert(conn, tx, $"{baseKey}#{ordinal[baseKey]}", rec, result, claimed);
            }
        }
        ImportPhotos(conn, tx, photosFolder ?? Path.Combine(legacyFolder, "photos"));
        ImportAuditLog(conn, tx, legacyFolder);
        ImportCellEditHistory(conn, tx, legacyFolder);
        tx.Commit();
        return result;
    }

    private static (string Mime, byte[] Bytes)? DecodeDataUri(string? s)
    {
        if (string.IsNullOrEmpty(s) || !s.StartsWith("data:", StringComparison.Ordinal)) return null;
        var comma = s.IndexOf(',');
        if (comma < 0) return null;
        var head = s[5..comma];
        if (!head.EndsWith(";base64", StringComparison.Ordinal)) return null;
        try { return (head[..^7], Convert.FromBase64String(s[(comma + 1)..])); }
        catch (FormatException) { return null; }
    }

    private static void ImportPhotos(SqliteConnection conn, SqliteTransaction tx, string photosFolder)
    {
        // Gom photoId + ảnh nhúng (lấy ảnh nhúng đầu tiên) từ các đơn đã nhập.
        var ids = new Dictionary<string, string?>();
        using (var q = conn.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT photo_id, raw_json FROM orders WHERE photo_id <> ''";
            using var r = q.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetString(0);
                string? inline = null;
                using (var d = JsonDocument.Parse(r.GetString(1)))
                    if (d.RootElement.TryGetProperty("photo", out var p) && p.ValueKind == JsonValueKind.String) inline = p.GetString();
                if (!ids.TryGetValue(id, out var cur) || (cur is null && DecodeDataUri(inline) is not null)) ids[id] = inline;
            }
        }

        foreach (var (id, inlineUri) in ids)
        {
            (string Mime, byte[] Bytes)? file = null;
            var path = Path.Combine(photosFolder,
                Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(id))).ToLowerInvariant() + ".json");
            if (File.Exists(path))
            {
                try
                {
                    using var d = JsonDocument.Parse(File.ReadAllBytes(path));
                    if (d.RootElement.TryGetProperty("id", out var fid) && fid.GetString() == id
                        && d.RootElement.TryGetProperty("data", out var data))
                        file = DecodeDataUri(data.GetString());
                }
                catch (JsonException) { /* tệp hỏng: coi như không có, rơi về ảnh nhúng */ }
            }
            var inline = DecodeDataUri(inlineUri);

            string source; (string Mime, byte[] Bytes)? chosen; var differs = 0;
            if (file is not null) { source = "file"; chosen = file; if (inline is not null && !inline.Value.Bytes.AsSpan().SequenceEqual(file.Value.Bytes)) differs = 1; }
            else if (inline is not null) { source = "inline"; chosen = inline; }
            else { source = "missing"; chosen = null; }

            // KHÔNG BAO GIỜ làm mất ảnh đã có: ảnh chụp từ điện thoại (nguồn "phone") do v2 tạo, và bản "missing" không được đè lên ảnh có dữ liệu.
            using (var chk = conn.CreateCommand())
            {
                chk.Transaction = tx;
                chk.CommandText = "SELECT source, data IS NOT NULL FROM photos WHERE photo_id=$id";
                chk.Parameters.AddWithValue("$id", id);
                using var cr = chk.ExecuteReader();
                if (cr.Read() && (cr.GetString(0) == "phone" || (chosen is null && cr.GetInt32(1) == 1))) continue;
            }

            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "INSERT OR REPLACE INTO photos VALUES ($id,$src,$mime,$data,$sha,$diff)";
            c.Parameters.AddWithValue("$id", id); c.Parameters.AddWithValue("$src", source);
            c.Parameters.AddWithValue("$mime", chosen?.Mime ?? "");
            c.Parameters.AddWithValue("$data", chosen is null ? DBNull.Value : chosen.Value.Bytes);
            c.Parameters.AddWithValue("$sha", chosen is null ? "" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(chosen.Value.Bytes)));
            c.Parameters.AddWithValue("$diff", differs);
            c.ExecuteNonQuery();
        }
    }

    public sealed record ReconcileReport(int ExpectedOrders, long DbOrders, long DbArchived, long DbDeleted, bool AllMatch);

    /// <summary>Đối chiếu sau nhập: số dòng trong DB phải bằng số bản ghi dry-run đã ánh xạ (tổng và từng nhóm).</summary>
    public static ReconcileReport Reconcile(string legacyFolder, string dbPath)
    {
        var dry = LegacyDryRun.Analyze(legacyFolder);
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        long Q(string where)
        {
            using var c = conn.CreateCommand();
            c.CommandText = "SELECT COUNT(*) FROM orders" + where;
            return (long)c.ExecuteScalar()!;
        }
        var total = Q(""); var arch = Q(" WHERE is_archived=1"); var del = Q(" WHERE is_deleted=1");
        var match = total == dry.TotalMapped && arch == dry.Archived.Mapped && del == dry.Deleted.Mapped;
        return new ReconcileReport(dry.TotalMapped, total, arch, del, match);
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind == JsonValueKind.Null ? "" : v.GetRawText())
            : "";

    private static void ImportAuditLog(SqliteConnection conn, SqliteTransaction tx, string folder)
    {
        var (kind, doc, _) = LegacyJson.Read(Path.Combine(folder, AuditLogFile));
        using (doc)
        {
            if (kind != LegacyJson.Kind.Json || doc is null || doc.RootElement.ValueKind != JsonValueKind.Array) return;
            var seq = 0;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                using var c = conn.CreateCommand();
                c.Transaction = tx;
                c.CommandText = "INSERT OR REPLACE INTO audit_log VALUES ($s,$t,$ty,$d,$r)";
                c.Parameters.AddWithValue("$s", seq++);
                c.Parameters.AddWithValue("$t", Str(e, "time"));
                c.Parameters.AddWithValue("$ty", Str(e, "type"));
                c.Parameters.AddWithValue("$d", Str(e, "detail"));
                c.Parameters.AddWithValue("$r", e.GetRawText());
                c.ExecuteNonQuery();
            }
        }
    }

    private static void ImportCellEditHistory(SqliteConnection conn, SqliteTransaction tx, string folder)
    {
        var (kind, doc, _) = LegacyJson.Read(Path.Combine(folder, CellEditFile));
        using (doc)
        {
            if (kind != LegacyJson.Kind.Json || doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array) continue;
                var colon = prop.Name.IndexOf(':');
                var rowRef = colon < 0 ? prop.Name : prop.Name[..colon];
                var field = colon < 0 ? "" : prop.Name[(colon + 1)..];
                var seq = 0;
                foreach (var e in prop.Value.EnumerateArray())
                {
                    using var c = conn.CreateCommand();
                    c.Transaction = tx;
                    c.CommandText = "INSERT OR REPLACE INTO cell_edit_history VALUES ($rr,$f,$s,$t,$l,$o,$n,$et,NULL,$raw)";
                    c.Parameters.AddWithValue("$rr", rowRef); c.Parameters.AddWithValue("$f", field);
                    c.Parameters.AddWithValue("$s", seq++); c.Parameters.AddWithValue("$t", Str(e, "time"));
                    c.Parameters.AddWithValue("$l", Str(e, "label")); c.Parameters.AddWithValue("$o", Str(e, "oldVal"));
                    c.Parameters.AddWithValue("$n", Str(e, "newVal")); c.Parameters.AddWithValue("$et", Str(e, "editType"));
                    c.Parameters.AddWithValue("$raw", e.GetRawText());
                    c.ExecuteNonQuery();
                }
            }
        }
    }

    // ===================== Nhập từng bản ghi (an toàn khi nhập lại) =====================
    private static readonly string[] OrderColumns =
    {
        "order_id", "return_order_id", "track", "return_track", "alt_track", "forward_track", "tracking_codes", "scan_aliases",
        "platform", "order_type", "platform_status", "pre_order", "status", "carrier", "ship_date", "return_date", "recv_date",
        "returned_at", "deadline", "price", "note", "buyer_note", "user_note", "import_week", "imported_at", "is_watchlist",
        "watchlist_reason", "is_partial_return", "photo_id", "extra_json", "is_archived", "archived_at", "is_deleted", "deleted_at",
        "deleted_from", "raw_json",
    };

    /// <summary>Dữ liệu SÀN: dòng v2 đã sửa chỉ được BỔ SUNG chỗ trống từ dữ liệu cũ, không bị ghi đè.</summary>
    private static readonly string[] FillOnlyText =
    {
        "order_id", "return_order_id", "track", "return_track", "alt_track", "forward_track", "platform", "order_type", "platform_status",
        "pre_order", "carrier", "ship_date", "return_date", "returned_at", "deadline", "note", "buyer_note", "import_week", "imported_at",
    };

    private static bool IsEmpty(object? v) => v is null || v is DBNull || (v is string s && s.Length == 0);

    private static Dictionary<string, object?>? ReadRow(SqliteConnection conn, SqliteTransaction tx, string whereSql, params (string, object)[] ps)
    {
        using var c = conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "SELECT source_key, id, v2_modified, " + string.Join(",", OrderColumns) + " FROM orders WHERE " + whereSql + " LIMIT 1";
        foreach (var (n, v) in ps) c.Parameters.AddWithValue(n, v);
        using var r = c.ExecuteReader();
        if (!r.Read()) return null;
        var d = new Dictionary<string, object?>();
        for (var i = 0; i < r.FieldCount; i++) d[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        return d;
    }

    private static string UnionJsonLists(object? existing, object? legacy)
    {
        List<string> Parse(object? v)
        {
            try { return JsonSerializer.Deserialize<List<string>>(v as string ?? "[]") ?? new(); }
            catch (JsonException) { return new(); }
        }
        return JsonSerializer.Serialize(Parse(existing).Concat(Parse(legacy)).Distinct().ToList());
    }

    private static string MergeExtra(object? existing, object? legacy)
    {
        System.Text.Json.Nodes.JsonObject Obj(object? v)
        {
            try { return System.Text.Json.Nodes.JsonNode.Parse(string.IsNullOrWhiteSpace(v as string) ? "{}" : (string)v!) as System.Text.Json.Nodes.JsonObject ?? new(); }
            catch (JsonException) { return new(); }
        }
        var e = Obj(existing);
        foreach (var kv in Obj(legacy)) if (!e.ContainsKey(kv.Key)) e[kv.Key] = kv.Value?.DeepClone();
        return e.Count == 0 ? "" : e.ToJsonString();
    }

    private static void Upsert(SqliteConnection conn, SqliteTransaction tx, string key, LegacyRecord rec, ImportResult result, HashSet<string> claimed)
    {
        var o = rec.Order;
        using var doc = JsonDocument.Parse(rec.RawJson);
        string Raw(string n) => doc.RootElement.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        var legacy = new Dictionary<string, object?>
        {
            ["order_id"] = o.OrderId, ["return_order_id"] = o.ReturnOrderId, ["track"] = o.Track, ["return_track"] = o.ReturnTrack,
            ["alt_track"] = o.AltTrack, ["forward_track"] = o.ForwardTrack, ["tracking_codes"] = JsonSerializer.Serialize(o.TrackingCodes),
            ["scan_aliases"] = JsonSerializer.Serialize(o.ScanAliases), ["platform"] = o.Platform, ["order_type"] = o.OrderType,
            ["platform_status"] = o.PlatformStatus, ["pre_order"] = o.PreOrder, ["status"] = o.Status, ["carrier"] = o.Carrier,
            ["ship_date"] = o.ShipDate, ["return_date"] = o.ReturnDate, ["recv_date"] = o.RecvDate, ["returned_at"] = o.ReturnedAt,
            ["deadline"] = o.Deadline, ["price"] = (double)o.Price, ["note"] = o.Note, ["buyer_note"] = o.BuyerNote, ["user_note"] = o.UserNote,
            ["import_week"] = o.ImportWeek, ["imported_at"] = o.ImportedAt, ["is_watchlist"] = o.IsWatchlist ? 1 : 0,
            ["watchlist_reason"] = o.WatchlistReason, ["is_partial_return"] = o.IsPartialReturn ? 1 : 0, ["photo_id"] = o.PhotoId,
            ["extra_json"] = o.ExtraJson, ["is_archived"] = rec.Category == LegacyCategory.Archived ? 1 : 0, ["archived_at"] = Raw("archivedAt"),
            ["is_deleted"] = rec.Category == LegacyCategory.Deleted ? 1 : 0, ["deleted_at"] = Raw("deletedAt"), ["deleted_from"] = Raw("deletedFrom"),
            ["raw_json"] = rec.RawJson,
        };

        // 1) cùng khóa nguồn; 2) cùng bản ghi nhưng app cũ đã DỜI sang nhóm khác (chính / lưu trữ / đã xóa): đổi cờ, KHÔNG tạo dòng mới;
        // 3) dòng do v2 tự tạo (nạp file sàn) trùng danh tính với bản ghi cũ: không tạo bản trùng.
        var existing = ReadRow(conn, tx, "source_key=$k", ("$k", key));
        var moved = false;
        if (existing is null)
        {
            var tail = key[(key.IndexOf('|') + 1)..];
            existing = ReadRow(conn, tx, "substr(source_key, instr(source_key,'|')+1)=$t AND source_key<>$k AND source_key NOT LIKE 'import|%'" +
                                         " AND source_key NOT IN (SELECT value FROM json_each($claimed))",
                ("$t", tail), ("$k", key), ("$claimed", JsonSerializer.Serialize(claimed)));
            moved = existing is not null;
        }
        if (existing is null)
        {
            existing = ReadRow(conn, tx, "source_key LIKE 'import|%' AND platform=$p AND order_id=$o AND track=$t AND return_order_id=$r" +
                                         " AND source_key NOT IN (SELECT value FROM json_each($claimed))",
                ("$p", o.Platform), ("$o", o.OrderId), ("$t", o.Track), ("$r", o.ReturnOrderId), ("$claimed", JsonSerializer.Serialize(claimed)));
        }

        if (existing is null)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO orders (source_key, id, " + string.Join(",", OrderColumns) + ", v2_modified) VALUES ($k,$id," +
                              string.Join(",", OrderColumns.Select(c => "$" + c)) + ",0)";
            ins.Parameters.AddWithValue("$k", key);
            ins.Parameters.AddWithValue("$id", o.Id);
            foreach (var c in OrderColumns) ins.Parameters.AddWithValue("$" + c, legacy[c] ?? DBNull.Value);
            ins.ExecuteNonQuery();
            claimed.Add(key);
            result.Inserted++;
            WriteItems(conn, tx, key, o.Items);
            return;
        }

        var targetKey = (string)existing["source_key"]!;
        claimed.Add(targetKey);
        var modified = Convert.ToInt32(existing["v2_modified"]) != 0;
        Dictionary<string, object?> values;
        if (!modified) values = legacy;                       // dòng chưa bị v2 đụng tới: lấy nguyên dữ liệu cũ mới nhất (như trước)
        else
        {
            // v2 đã quét/sửa/dời/nạp file: trường kho và cờ GIỮ NGUYÊN; dữ liệu sàn chỉ được bổ sung chỗ trống.
            values = new Dictionary<string, object?>();
            foreach (var c in OrderColumns) values[c] = existing[c];
            foreach (var c in FillOnlyText) if (IsEmpty(existing[c])) values[c] = legacy[c];
            if (Convert.ToDouble(existing["price"] ?? 0.0) == 0) values["price"] = legacy["price"];
            values["tracking_codes"] = UnionJsonLists(existing["tracking_codes"], legacy["tracking_codes"]);
            values["scan_aliases"] = UnionJsonLists(existing["scan_aliases"], legacy["scan_aliases"]);
            values["extra_json"] = MergeExtra(existing["extra_json"], legacy["extra_json"]);
        }

        using var upd = conn.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = "UPDATE orders SET " + string.Join(",", OrderColumns.Select(c => $"{c}=${c}")) + " WHERE source_key=$k";
        upd.Parameters.AddWithValue("$k", targetKey);
        foreach (var c in OrderColumns) upd.Parameters.AddWithValue("$" + c, values[c] ?? DBNull.Value);
        upd.ExecuteNonQuery();

        result.Updated++;
        if (moved && !modified) result.Moved++;

        // Sản phẩm: dòng chưa bị v2 đụng tới lấy theo dữ liệu cũ; dòng v2 đã đụng tới chỉ lấy khi đang chưa có sản phẩm thật.
        var hasRealItems = Convert.ToInt32(ScalarSql(conn, tx, "SELECT COUNT(*) FROM order_items WHERE source_key=$k AND product<>''", ("$k", targetKey))) > 0;
        if (!modified || !hasRealItems) WriteItems(conn, tx, targetKey, o.Items);
    }

    private static object? ScalarSql(SqliteConnection conn, SqliteTransaction tx, string sql, params (string, object)[] ps)
    {
        using var c = conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = sql;
        foreach (var (n, v) in ps) c.Parameters.AddWithValue(n, v);
        return c.ExecuteScalar();
    }

    private static void WriteItems(SqliteConnection conn, SqliteTransaction tx, string key, List<QuanLyHangHoan.Core.Models.OrderItem> items)
    {
        using var del = conn.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM order_items WHERE source_key=$k";
        del.Parameters.AddWithValue("$k", key);
        del.ExecuteNonQuery();

        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            using var ic = conn.CreateCommand();
            ic.Transaction = tx;
            ic.CommandText = "INSERT INTO order_items (source_key, line_no, product, color, size, qty, sku, sku_id, import_prices) VALUES ($k,$n,$p,$c,$s,$q,$sku,$skuid,$ip)";
            ic.Parameters.AddWithValue("$k", key); ic.Parameters.AddWithValue("$n", i);
            ic.Parameters.AddWithValue("$p", it.Product); ic.Parameters.AddWithValue("$c", it.Color);
            ic.Parameters.AddWithValue("$s", it.Size); ic.Parameters.AddWithValue("$q", it.Qty);
            ic.Parameters.AddWithValue("$sku", (object?)it.Sku ?? DBNull.Value);
            ic.Parameters.AddWithValue("$skuid", (object?)it.SkuId ?? DBNull.Value);
            ic.Parameters.AddWithValue("$ip", it.ImportPrices is { Count: > 0 } ? JsonSerializer.Serialize(it.ImportPrices) : DBNull.Value);
            ic.ExecuteNonQuery();
        }
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
