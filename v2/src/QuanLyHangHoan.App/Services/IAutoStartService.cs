using Microsoft.Win32;

namespace QuanLyHangHoan.App.Services;

public interface IAutoStartService
{
    bool IsEnabled();
    void SetEnabled(bool enabled, string? executablePath = null);
}

/// <summary>
/// Quản lý khóa tự mở cùng Windows tại HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// Chỉ chạm đúng khóa "QuanLyHangHoan", không đụng khóa khác.
/// </summary>
public sealed class WindowsAutoStartService : IAutoStartService
{
    public const string RunRegistrySubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "QuanLyHangHoan";

    public static string BuildStartupCommand(string? executablePath = null)
    {
        var path = string.IsNullOrWhiteSpace(executablePath)
            ? Environment.ProcessPath
            : executablePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Không xác định được đường dẫn tệp thực thi.");
        }

        return $"\"{path.Trim().Trim('"')}\" --tray";
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunRegistrySubKey, writable: false);
        return key?.GetValue(ValueName) is string val && !string.IsNullOrWhiteSpace(val);
    }

    public void SetEnabled(bool enabled, string? executablePath = null)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunRegistrySubKey, writable: true);
            key?.SetValue(ValueName, BuildStartupCommand(executablePath), RegistryValueKind.String);
            return;
        }

        using var openKey = Registry.CurrentUser.OpenSubKey(RunRegistrySubKey, writable: true);
        openKey?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

/// <summary>
/// Bản giả dùng cho --demo và UiTests: lưu trong bộ nhớ, không chạm Windows Registry thật.
/// </summary>
public sealed class FakeAutoStartService : IAutoStartService
{
    private readonly Dictionary<string, string> _entries = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Entries => _entries;

    public void SeedUnrelatedEntry(string name, string command)
    {
        _entries[name] = command;
    }

    public bool IsEnabled() => _entries.ContainsKey(WindowsAutoStartService.ValueName);

    public void SetEnabled(bool enabled, string? executablePath = null)
    {
        if (enabled)
        {
            var path = string.IsNullOrWhiteSpace(executablePath)
                ? @"C:\Program Files\QuanLyHangHoan\QuanLyHangHoan.App.exe"
                : executablePath;
            _entries[WindowsAutoStartService.ValueName] = WindowsAutoStartService.BuildStartupCommand(path);
        }
        else
        {
            _entries.Remove(WindowsAutoStartService.ValueName);
        }
    }
}
