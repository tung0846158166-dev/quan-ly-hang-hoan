using System.Diagnostics;
using System.IO;
using System.Reflection;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Khối thông tin chẩn đoán để gửi người hỗ trợ (KHÔNG chứa mã vận đơn, tên sản phẩm, địa chỉ Apps Script, token: xem DiagnosticsReport)
/// và việc mở thư mục dữ liệu. Bản giả cho demo/test không đọc đĩa.
/// </summary>
public interface IDiagnosticsProvider
{
    string AppVersion { get; }
    string DataFolderPath { get; }
    string BuildReport();
    void OpenDataFolder();
}

public sealed class SqliteDiagnosticsProvider : IDiagnosticsProvider
{
    private readonly string _dbPath;
    private readonly IUiSettingsStore _settings;
    private readonly Func<DateTime> _clock;
    private readonly Action<string> _openFolder;

    public SqliteDiagnosticsProvider(string dbPath, IUiSettingsStore settings, Func<DateTime>? clock = null, Action<string>? openFolder = null)
    {
        _dbPath = dbPath;
        _settings = settings;
        _clock = clock ?? (() => DateTime.Now);
        _openFolder = openFolder ?? (d => Process.Start(new ProcessStartInfo { FileName = d, UseShellExecute = true }));
    }

    public string AppVersion => ReadVersion();
    public string DataFolderPath => Path.GetDirectoryName(Path.GetFullPath(_dbPath)) ?? ".";

    public string BuildReport()
    {
        AppSettings? s = null;
        try { s = _settings.Load(); } catch (Exception) { /* báo cáo vẫn dựng được khi không đọc được cài đặt */ }
        return DiagnosticsReport.Build(_dbPath, s, AppVersion, _clock());
    }

    public void OpenDataFolder()
    {
        try { Directory.CreateDirectory(DataFolderPath); _openFolder(DataFolderPath); }
        catch (Exception) { /* không mở được thì thôi, không làm sập app */ }
    }

    public static string ReadVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrWhiteSpace(info) ? (asm.GetName().Version?.ToString() ?? "2.0.0") : info.Split('+')[0];
    }
}

public sealed class FakeDiagnosticsProvider : IDiagnosticsProvider
{
    public string AppVersion => "2.0.0-demo";
    public string DataFolderPath => "(chế độ demo: không có thư mục thật)";
    public int OpenCalls { get; private set; }
    public string BuildReport() =>
        "== Chẩn đoán Quản lý hàng hoàn (v2) ==\r\n(chế độ demo: dữ liệu giả, không đọc cơ sở dữ liệu thật)\r\nPhiên bản app: 2.0.0-demo\r\n" +
        "Đơn: bảng chính 40, kho lưu trữ 12, đã xóa 3\r\nMã phòng đồng bộ: ROOM**** (24 ký tự)\r\n" +
        "(Báo cáo này không chứa mã vận đơn, tên sản phẩm, địa chỉ Apps Script hay token.)";
    public void OpenDataFolder() => OpenCalls++;
}
