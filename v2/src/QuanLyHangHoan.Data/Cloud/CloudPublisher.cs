using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace QuanLyHangHoan.Data.Cloud;

public sealed record CloudPublishResult(int Candidates, int Sent, int Added, int Updated, int Unchanged, int Rejected, int ReturnWatchSent);

/// <summary>
/// Đẩy lên cloud những đơn đã đổi Ở MÁY (nạp file Excel/thư mục/Salework, sửa cục bộ): so băm hiện tại với băm lần cuối đồng bộ
/// (`cloud_published`, ghi khi áp từ cloud hoặc khi đẩy thành công) nên chỉ gửi phần thật sự khác (tiết kiệm hạn mức ghi 100.000 hàng/ngày).
/// Gửi theo lô ≤ 500 qua `/import` (nhập lại cùng kiện thì cập nhật; cloud không đè ghi chú/theo dõi/lưu trữ/xóa/trạng thái đã nhận do người dùng đặt).
/// Không bao giờ gửi ghi chú người mua, dữ liệu thô hay dữ liệu cá nhân (xem <see cref="ParcelMapper.ToParcel"/>).
/// </summary>
public static class CloudPublisher
{
    public const int BatchSize = 500;

    private static SqliteConnection Open(string dbPath)
    {
        var c = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        c.Open();
        using var p = c.CreateCommand();
        p.CommandText = "PRAGMA busy_timeout = 15000";
        p.ExecuteNonQuery();
        return c;
    }

    private static Dictionary<string, string> LoadPublished(string dbPath)
    {
        var d = new Dictionary<string, string>();
        using var c = Open(dbPath);
        using var cmd = c.CreateCommand();
        cmd.CommandText = CloudReplica.PublishedSql + "; SELECT id, hash FROM cloud_published";
        using var r = cmd.ExecuteReader();
        while (r.Read()) d[r.GetString(0)] = r.GetString(1);
        return d;
    }

    /// <summary>
    /// Ghi "đã đồng bộ". `Old` = dấu băm lúc lấy danh sách đẩy: nếu trong lúc đẩy, lần kéo từ cloud đã ghi dấu khác (cloud có bản mới hơn)
    /// thì GIỮ dấu đó (không ghi đè), để bản trên máy không bị coi là đã đồng bộ với bản cũ.
    /// </summary>
    private static void SavePublished(string dbPath, IEnumerable<(string Id, string Hash, string? Old)> rows)
    {
        using var c = Open(dbPath);
        using var tx = c.BeginTransaction();
        foreach (var (id, hash, old) in rows)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = old is null
                ? "INSERT INTO cloud_published(id, hash) VALUES($i,$h) ON CONFLICT(id) DO NOTHING"
                : "INSERT INTO cloud_published(id, hash) VALUES($i,$h) ON CONFLICT(id) DO UPDATE SET hash = excluded.hash WHERE cloud_published.hash = $o";
            cmd.Parameters.AddWithValue("$i", id);
            cmd.Parameters.AddWithValue("$h", hash);
            cmd.Parameters.AddWithValue("$o", (object?)old ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Các đơn cần đẩy: băm hiện tại khác băm lần cuối đồng bộ (hoặc chưa từng đồng bộ).</summary>
    public static List<(JsonObject Parcel, string Hash)> Pending(string dbPath) => PendingWithOld(dbPath).Select(x => (x.Parcel, x.Hash)).ToList();

    private static List<(JsonObject Parcel, string Hash, string? Old)> PendingWithOld(string dbPath)
    {
        var published = LoadPublished(dbPath);
        var list = new List<(JsonObject, string, string?)>();
        foreach (var p in ParcelMapper.ToParcels(OrderRepository.Load(dbPath), out _, out _))
        {
            var h = ParcelMapper.Hash(p);
            var had = published.TryGetValue(p["id"]!.GetValue<string>(), out var old);
            if (had && old == h) continue;
            list.Add((p, h, had ? old : null));
        }
        return list;
    }

    /// <param name="includeReturnWatch">Gửi kèm dấu Bảng Hàng Hoàn Về cục bộ (chỉ dùng khi chuyển dữ liệu cũ lên lần đầu; cloud chỉ thêm khi chưa có).</param>
    public static async Task<CloudPublishResult> PublishAsync(CloudApi api, string dbPath, string source, bool includeReturnWatch = false, CancellationToken ct = default)
    {
        var pending = PendingWithOld(dbPath);
        int sent = 0, added = 0, updated = 0, unchanged = 0, rejected = 0;
        var signature = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        for (var i = 0; i < pending.Count; i += BatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = pending.Skip(i).Take(BatchSize).ToList();
            var res = await api.ImportAsync(source, signature, batch.Select(b => { b.Parcel["hash"] = b.Hash; return b.Parcel; }), null, ct).ConfigureAwait(false);
            var bad = res.Rejected.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            // chỉ ghi nhận "đã đồng bộ" cho kiện cloud đã nhận; kiện bị từ chối để lần sau thử lại
            SavePublished(dbPath, batch.Where(b => !bad.Contains(b.Parcel["id"]!.GetValue<string>())).Select(b => (b.Parcel["id"]!.GetValue<string>(), b.Hash, b.Old)));
            sent += batch.Count; added += res.Added; updated += res.Updated; unchanged += res.Unchanged; rejected += res.Rejected.Count;
        }

        var watchSent = 0;
        if (includeReturnWatch)
        {
            var marks = new List<JsonObject>();
            using (var c = Open(dbPath))
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = ReturnWatchService.TableSql + "SELECT order_id, since, off FROM return_watch";
                using var r = cmd.ExecuteReader();
                while (r.Read()) marks.Add(new JsonObject { ["id"] = r.GetString(0), ["since"] = r.GetInt64(1), ["off"] = r.GetInt32(2) != 0 });
            }
            for (var i = 0; i < marks.Count; i += 2000)
            {
                var res = await api.ImportAsync(source, signature, Array.Empty<JsonObject>(), marks.Skip(i).Take(2000), ct).ConfigureAwait(false);
                watchSent += res.ReturnWatchAdded;
            }
        }
        return new(pending.Count, sent, added, updated, unchanged, rejected, watchSent);
    }
}
