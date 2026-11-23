using System.Text.Json;
using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.Data;

/// <summary>Vị trí/kích thước cửa sổ lần trước (đơn vị DIP). Giao diện phải kiểm tra còn nằm trong màn hình trước khi áp dụng.</summary>
public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

/// <summary>
/// Cài đặt app v2 (JSON). Lần đầu tạo mã phòng NGẪU NHIÊN mạnh (không dùng SHP-2026). Ghi nguyên tử (tmp -> rename).
/// Tệp hỏng không bị ghi đè âm thầm: đổi tên thành settings.json.corrupt-* rồi tạo cài đặt mới.
/// </summary>
public sealed class AppSettings
{
    public string Room { get; set; } = "";
    public bool SyncEnabled { get; set; } = true;
    /// <summary>Bật đường ntfy.sh (dự phòng như app cũ). Một số mạng chặn ntfy.sh: tắt được mà vẫn còn MQTT.</summary>
    public bool NtfyEnabled { get; set; } = true;
    public List<string> Brokers { get; set; } = new(MqttTransport.DefaultBrokers);

    // ---- Tùy chọn giao diện ----
    /// <summary>"light" | "dark"; giá trị lạ coi là "light".</summary>
    public string Theme { get; set; } = "light";
    /// <summary>Tên các nhóm sidebar đang thu gọn (SHOPEE, TIKTOK, CHUNG...).</summary>
    public List<string> CollapsedNavGroups { get; set; } = new();
    /// <summary>Khóa màn hình mở lần trước ("" = Trang chủ).</summary>
    public string LastPage { get; set; } = "";
    public WindowPlacement? Window { get; set; }

    // ---- Chạy nền / thông báo / sao lưu (giao diện đọc-ghi; mặc định an toàn) ----
    /// <summary>Tự mở app cùng Windows (khóa Run của người dùng hiện tại). Mặc định TẮT: người dùng tự bật.</summary>
    public bool AutoStart { get; set; }
    /// <summary>Bấm X thì thu nhỏ xuống khay hệ thống thay vì thoát (để đồng bộ điện thoại và theo dõi thư mục chạy tiếp).</summary>
    public bool MinimizeToTray { get; set; } = true;
    /// <summary>Báo vàng/đỏ (12h/24h) bằng thông báo Windows.</summary>
    public bool NotifyAlerts { get; set; } = true;
    /// <summary>Phát âm thanh khi quét/nhận đơn từ điện thoại.</summary>
    public bool Sounds { get; set; } = true;
    public bool BackupEnabled { get; set; } = true;
    public int BackupEveryHours { get; set; } = 2;
    public int BackupKeepDays { get; set; } = 7;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings LoadOrCreate(string path)
    {
        AppSettings? s = null;
        if (File.Exists(path))
        {
            try { s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)); }
            catch (JsonException)
            {
                File.Move(path, $"{path}.corrupt-{DateTime.Now:yyyyMMddHHmmssfff}");
            }
        }

        var created = s is null;
        s ??= new AppSettings();
        if (s.Theme is not ("light" or "dark")) s.Theme = "light";
        s.CollapsedNavGroups ??= new();
        s.LastPage ??= "";
        s.BackupEveryHours = Math.Clamp(s.BackupEveryHours, 1, 24);
        s.BackupKeepDays = Math.Clamp(s.BackupKeepDays, 1, 90);
        if (string.IsNullOrWhiteSpace(s.Room)) { s.Room = SyncRoom.Generate(); created = true; }
        if (s.Brokers is null || s.Brokers.Count == 0) { s.Brokers = new(MqttTransport.DefaultBrokers); created = true; }
        if (created) s.Save(path);
        return s;
    }

    public void Save(string path)
    {
        if (Theme is not ("light" or "dark")) Theme = "light";
        if (string.IsNullOrWhiteSpace(Room)) Room = SyncRoom.Generate();
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
