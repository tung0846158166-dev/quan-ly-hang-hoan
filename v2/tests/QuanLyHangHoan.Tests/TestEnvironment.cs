using System.IO;
using System.Runtime.CompilerServices;
using QuanLyHangHoan.App.Services;

namespace QuanLyHangHoan.Tests;

/// <summary>
/// Chạy TRƯỚC mọi test: trỏ thư mục dữ liệu của app sang thư mục tạm riêng. Không có bước này, test giao diện dùng chung thư mục thật
/// (%LOCALAPPDATA%\QuanLyHangHoan\v2) với app đang mở: đụng khóa "chỉ một bản app" (test bị tắt giữa chừng) và đọc phải phiên cloud thật.
/// </summary>
internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Init()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AppPaths.DataDirEnvVar))) return;
        var dir = Path.Combine(Path.GetTempPath(), "qlhh-tests-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable(AppPaths.DataDirEnvVar, dir);
    }
}

public sealed class TestEnvironmentTests
{
    [Fact]
    public void Tests_never_use_the_real_app_data_folder()
    {
        var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuanLyHangHoan", "v2");
        Assert.NotEqual(Path.GetFullPath(real), Path.GetFullPath(AppPaths.BaseFolder));
        Assert.False(File.Exists(CloudMode.SessionPath) && CloudMode.SessionPath.StartsWith(real, StringComparison.OrdinalIgnoreCase));
    }
}
