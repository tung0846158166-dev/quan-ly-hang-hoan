using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Data;

/// <summary>
/// Khởi tạo cơ sở dữ liệu v2 (độc lập với việc nhập dữ liệu cũ). Chỉ TẠO nếu chưa có (CREATE IF NOT EXISTS),
/// không bao giờ xóa/sửa dòng cũ. Phiên bản schema lưu ở PRAGMA user_version; DB mới hơn app => từ chối, không hạ cấp.
/// </summary>
public static class AppDatabase
{
    /// <summary>1 = bảng đơn/ảnh/nhật ký; 2 = thêm sync_inbox (đồng bộ điện thoại); 3 = thêm order_items.import_prices (nạp file sàn);
    /// 4 = thêm orders.v2_modified (bảo vệ khi nhập lại dữ liệu cũ); 5 = thêm bảng return_watch (Bảng Hàng Hoàn Về); 6 = thêm order_items.returned_qty (sản phẩm nào đã hoàn).</summary>
    public const int SchemaVersion = 7;

    public static void Initialize(string dbPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            conn.Open();

            var current = Convert.ToInt32(Scalar(conn, "PRAGMA user_version"));
            if (current > SchemaVersion)
                throw new InvalidOperationException(
                    $"Cơ sở dữ liệu có phiên bản schema {current}, mới hơn app ({SchemaVersion}). Hãy cập nhật app, không hạ cấp dữ liệu.");

            using var tx = conn.BeginTransaction();
            using (var c = conn.CreateCommand()) { c.Transaction = tx; c.CommandText = LegacyImporter.Schema; c.ExecuteNonQuery(); }

            // Nâng cấp tại chỗ: DB cũ (phiên bản < 3) chưa có cột order_items.import_prices. Chỉ THÊM cột, không đụng dòng nào.
            var hasImportPrices = Convert.ToInt32(ScalarTx(conn, tx, "SELECT COUNT(*) FROM pragma_table_info('order_items') WHERE name='import_prices'")) > 0;
            if (!hasImportPrices)
                using (var c = conn.CreateCommand()) { c.Transaction = tx; c.CommandText = "ALTER TABLE order_items ADD COLUMN import_prices TEXT"; c.ExecuteNonQuery(); }

            // Phiên bản 4: cột v2_modified. Dòng có sẵn mặc định 0 (chưa biết v2 đã đụng tới): nhập lại dữ liệu cũ vẫn như trước với các dòng đó.
            var hasModified = Convert.ToInt32(ScalarTx(conn, tx, "SELECT COUNT(*) FROM pragma_table_info('orders') WHERE name='v2_modified'")) > 0;
            if (!hasModified)
                using (var c = conn.CreateCommand()) { c.Transaction = tx; c.CommandText = "ALTER TABLE orders ADD COLUMN v2_modified INTEGER NOT NULL DEFAULT 0"; c.ExecuteNonQuery(); }

            // Phiên bản 6: order_items.returned_qty (số lượng đã hoàn của từng sản phẩm). Chỉ THÊM cột, dòng cũ = 0 (chưa biết).
            var hasReturnedQty = Convert.ToInt32(ScalarTx(conn, tx, "SELECT COUNT(*) FROM pragma_table_info('order_items') WHERE name='returned_qty'")) > 0;
            if (!hasReturnedQty)
                using (var c = conn.CreateCommand()) { c.Transaction = tx; c.CommandText = "ALTER TABLE order_items ADD COLUMN returned_qty INTEGER NOT NULL DEFAULT 0"; c.ExecuteNonQuery(); }

            // Phiên bản 7: khiếu nại (26/11/2026). Chỉ THÊM cột, dòng cũ = không khiếu nại.
            foreach (var col in new[] { "complaint_status", "complaint_at", "complaint_note" })
            {
                var has = Convert.ToInt32(ScalarTx(conn, tx, $"SELECT COUNT(*) FROM pragma_table_info('orders') WHERE name='{col}'")) > 0;
                if (!has)
                    using (var c = conn.CreateCommand()) { c.Transaction = tx; c.CommandText = $"ALTER TABLE orders ADD COLUMN {col} TEXT NOT NULL DEFAULT ''"; c.ExecuteNonQuery(); }
            }

            // Phiên bản 5: bảng return_watch (Bảng Hàng Hoàn Về). Chỉ THÊM bảng.
            using (var c = conn.CreateCommand()) { c.Transaction = tx; c.CommandText = ReturnWatchService.TableSql; c.ExecuteNonQuery(); }

            if (current < SchemaVersion)
                using (var c = conn.CreateCommand()) { c.Transaction = tx; c.CommandText = $"PRAGMA user_version = {SchemaVersion}"; c.ExecuteNonQuery(); }
            tx.Commit();
        }
    }

    private static object? ScalarTx(SqliteConnection c, SqliteTransaction tx, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private static object? Scalar(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
