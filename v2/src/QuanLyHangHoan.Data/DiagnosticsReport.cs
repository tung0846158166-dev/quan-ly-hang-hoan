using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace QuanLyHangHoan.Data;

/// <summary>
/// Khối thông tin chẩn đoán để người dùng sao chép gửi cho người hỗ trợ. CHỈ ĐỌC và KHÔNG chứa dữ liệu nhạy cảm:
/// không mã vận đơn/mã đơn/tên sản phẩm/ghi chú, không địa chỉ Apps Script, không token, mã phòng chỉ hiện 4 ký tự đầu.
/// Chỉ có số đếm, phiên bản, đường dẫn thư mục dữ liệu và các câu trạng thái.
/// </summary>
public static class DiagnosticsReport
{
    public static string MaskRoom(string room)
    {
        var r = (room ?? "").Trim();
        return r.Length == 0 ? "(chưa có)" : (r.Length <= 4 ? new string('*', r.Length) : r[..4] + new string('*', Math.Min(8, r.Length - 4)) + $" ({r.Length} ký tự)");
    }

    public static string Build(string dbPath, AppSettings? settings, string appVersion, DateTime now)
    {
        var sb = new StringBuilder();
        void L(string s) => sb.AppendLine(s);
        L("== Chẩn đoán Quản lý hàng hoàn (v2) ==");
        L($"Thời điểm: {now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)}");
        L($"Phiên bản app: {appVersion}");
        L($"Hệ điều hành: {Environment.OSVersion.VersionString}; .NET {Environment.Version}");
        L($"Thư mục dữ liệu: {Path.GetDirectoryName(Path.GetFullPath(dbPath))}");

        if (!File.Exists(dbPath)) { L("Cơ sở dữ liệu: CHƯA CÓ"); }
        else
        {
            try
            {
                L($"Cơ sở dữ liệu: {new FileInfo(dbPath).Length / 1024.0 / 1024.0:0.0} MB");
                using var c = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
                c.Open();
                long Q(string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToInt64(cmd.ExecuteScalar() ?? 0); }
                L($"Phiên bản schema: {Q("PRAGMA user_version")}");
                L($"Đơn: bảng chính {Q("SELECT COUNT(*) FROM orders WHERE is_archived=0 AND is_deleted=0")}, kho lưu trữ {Q("SELECT COUNT(*) FROM orders WHERE is_archived=1 AND is_deleted=0")}, đã xóa {Q("SELECT COUNT(*) FROM orders WHERE is_deleted=1")}");
                L($"  Shopee {Q("SELECT COUNT(*) FROM orders WHERE platform='Shopee' AND is_deleted=0")}, TikTok {Q("SELECT COUNT(*) FROM orders WHERE platform='TikTok' AND is_deleted=0")}, đơn hủy {Q("SELECT COUNT(*) FROM orders WHERE order_type='Đơn hủy' AND is_deleted=0")}, đang theo dõi {Q("SELECT COUNT(*) FROM orders WHERE is_watchlist=1 AND is_deleted=0")}");
                L($"  chưa nhận (còn chờ) {Q("SELECT COUNT(*) FROM orders WHERE is_deleted=0 AND is_archived=0 AND status NOT IN ('Hàng đã trả','Đã nhận','Không cần nhận','Bị mất','Hư hỏng')")}");
                L($"Ảnh: {Q("SELECT COUNT(*) FROM photos")}; nhật ký: {Q("SELECT COUNT(*) FROM audit_log")} dòng; lịch sử sửa: {Q("SELECT COUNT(*) FROM cell_edit_history")} dòng");
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT time, type FROM audit_log ORDER BY seq DESC LIMIT 1";
                    using var r = cmd.ExecuteReader();
                    L(r.Read() ? $"Hoạt động gần nhất: {r.GetString(0)} ({AuditLogReader.LabelOf(r.GetString(1))})" : "Hoạt động gần nhất: chưa có");
                }
                L($"Yêu cầu đồng bộ đã xử lý: {Q("SELECT COUNT(*) FROM sync_inbox")}");
            }
            catch (Exception ex) { L("Cơ sở dữ liệu: KHÔNG ĐỌC ĐƯỢC (" + ex.GetType().Name + ": " + ex.Message + ")"); }

            var last = BackupService.List(BackupService.DefaultDir(dbPath)).FirstOrDefault();
            L(last is null ? "Sao lưu: chưa có bản nào" : $"Sao lưu: bản gần nhất {last.Time.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)} ({last.Bytes / 1024.0 / 1024.0:0.0} MB); tổng {BackupService.List(BackupService.DefaultDir(dbPath)).Count} bản");
        }

        if (settings is null) L("Cài đặt: (không đọc được)");
        else
        {
            L($"Mã phòng đồng bộ: {MaskRoom(settings.Room)}; đồng bộ {(settings.SyncEnabled ? "bật" : "tắt")}; ntfy {(settings.NtfyEnabled ? "bật" : "tắt")}; {settings.Brokers.Count} broker MQTT");
            L($"Giao diện: {settings.Theme}; tự mở cùng Windows: {(settings.AutoStart ? "bật" : "tắt")}; thu xuống khay: {(settings.MinimizeToTray ? "bật" : "tắt")}; thông báo vàng/đỏ: {(settings.NotifyAlerts ? "bật" : "tắt")}");
            L($"Tự sao lưu: {(settings.BackupEnabled ? $"bật, mỗi {settings.BackupEveryHours} giờ, giữ {settings.BackupKeepDays} ngày" : "tắt")}");
        }
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(dbPath));
            if (!string.IsNullOrEmpty(root)) L($"Ổ đĩa {root}: còn trống {new DriveInfo(root).AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0:0.0} GB");
        }
        catch (Exception) { /* không bắt buộc */ }
        L("(Báo cáo này không chứa mã vận đơn, tên sản phẩm, địa chỉ Apps Script hay token.)");
        return sb.ToString();
    }
}
