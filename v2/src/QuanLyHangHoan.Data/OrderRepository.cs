using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Data;

/// <summary>Đơn kèm cờ lưu trữ/đã xóa (kho lưu trữ = cùng đơn ở trạng thái "đã dời", không phải bảng khác).</summary>
public sealed record StoredOrder(Order Order, bool IsArchived, string ArchivedAt, bool IsDeleted, string DeletedAt, string DeletedFrom);

/// <summary>Chỉ ĐỌC từ SQLite. Không ghi, không tạo tệp DB nếu chưa có.</summary>
public static class OrderRepository
{
    public static IReadOnlyList<StoredOrder> Load(string dbPath)
    {
        if (!File.Exists(dbPath)) return Array.Empty<StoredOrder>();

        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        conn.Open();

        var items = new Dictionary<string, List<OrderItem>>();
        using (var ic = conn.CreateCommand())
        {
            // DB phiên bản < 3 chưa có cột import_prices (chỉ mở chỉ-đọc, chưa nâng cấp): đọc được cả hai dạng.
            var hasPrices = false;
            var hasReturned = false;
            using (var pc = conn.CreateCommand())
            {
                pc.CommandText = "SELECT COUNT(*) FROM pragma_table_info('order_items') WHERE name='import_prices'";
                hasPrices = Convert.ToInt32(pc.ExecuteScalar()) > 0;
                pc.CommandText = "SELECT COUNT(*) FROM pragma_table_info('order_items') WHERE name='returned_qty'";
                hasReturned = Convert.ToInt32(pc.ExecuteScalar()) > 0;
            }
            ic.CommandText = "SELECT source_key, product, color, size, qty, sku, sku_id, " + (hasPrices ? "import_prices" : "NULL") +
                             ", " + (hasReturned ? "returned_qty" : "0") + " FROM order_items ORDER BY source_key, line_no";
            using var r = ic.ExecuteReader();
            while (r.Read())
            {
                var key = r.GetString(0);
                if (!items.TryGetValue(key, out var list)) items[key] = list = new();
                list.Add(new OrderItem
                {
                    Product = r.GetString(1), Color = r.GetString(2), Size = r.GetString(3), Qty = r.GetInt32(4),
                    Sku = r.IsDBNull(5) ? null : r.GetString(5), SkuId = r.IsDBNull(6) ? null : r.GetString(6),
                    ImportPrices = r.IsDBNull(7) ? null : ReadPrices(r.GetString(7)),
                    ReturnedQty = r.IsDBNull(8) ? 0 : r.GetInt32(8),
                });
            }
        }

        var result = new List<StoredOrder>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT source_key, id, order_id, return_order_id, track, return_track, alt_track, forward_track, tracking_codes, scan_aliases,
                   platform, order_type, platform_status, pre_order, status, carrier, ship_date, return_date, recv_date, returned_at, deadline,
                   price, note, buyer_note, user_note, import_week, imported_at, is_watchlist, watchlist_reason, is_partial_return,
                   photo_id, extra_json, is_archived, archived_at, is_deleted, deleted_at, deleted_from,
                   complaint_status, complaint_at, complaint_note, complaint_done_at
            FROM orders ORDER BY rowid
            """;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var key = rd.GetString(0);
            var o = new Order
            {
                Id = rd.GetString(1), OrderId = rd.GetString(2), ReturnOrderId = rd.GetString(3), Track = rd.GetString(4),
                ReturnTrack = rd.GetString(5), AltTrack = rd.GetString(6), ForwardTrack = rd.GetString(7),
                TrackingCodes = Strings(rd.GetString(8)), ScanAliases = Strings(rd.GetString(9)),
                Platform = rd.GetString(10), OrderType = rd.GetString(11), PlatformStatus = rd.GetString(12), PreOrder = rd.GetString(13),
                Status = rd.GetString(14), Carrier = rd.GetString(15), ShipDate = rd.GetString(16), ReturnDate = rd.GetString(17),
                RecvDate = rd.GetString(18), ReturnedAt = rd.GetString(19), Deadline = rd.GetString(20),
                Price = (decimal)rd.GetDouble(21), Note = rd.GetString(22), BuyerNote = rd.GetString(23), UserNote = rd.GetString(24),
                ImportWeek = rd.GetString(25), ImportedAt = rd.GetString(26), IsWatchlist = rd.GetInt32(27) != 0,
                WatchlistReason = rd.GetString(28), IsPartialReturn = rd.GetInt32(29) != 0, PhotoId = rd.GetString(30),
                ExtraJson = rd.GetString(31), Items = items.GetValueOrDefault(key) ?? new(),
                ComplaintStatus = rd.GetString(37), ComplaintAt = rd.GetString(38), ComplaintNote = rd.GetString(39), ComplaintDoneAt = rd.GetString(40),
            };
            var extraText = o.ExtraJson;
            o.SetLazyImport(() => OrderExtra.Read(extraText));
            result.Add(new StoredOrder(o, rd.GetInt32(32) != 0, rd.GetString(33), rd.GetInt32(34) != 0, rd.GetString(35), rd.GetString(36)));
        }
        return result;
    }

    /// <summary>Đơn đang ở bảng chính: không lưu trữ, không đã xóa.</summary>
    public static IReadOnlyList<Order> LoadActive(string dbPath) =>
        Load(dbPath).Where(s => !s.IsArchived && !s.IsDeleted).Select(s => s.Order).ToList();

    private static Dictionary<string, long>? ReadPrices(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, long>>(json); }
        catch (JsonException) { return null; }
    }

    private static List<string> Strings(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }
}
