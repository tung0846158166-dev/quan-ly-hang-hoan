using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Khởi tạo cơ sở dữ liệu lúc khởi động app (chế độ thật).
/// Nếu DB có schema mới hơn app hoặc không thể mở, báo lỗi tiếng Việt rõ ràng và không sửa DB.
/// </summary>
public static class AppStartupInitializer
{
    public static bool TryInitializeDatabase(string dbPath, Action<string, string> showError)
    {
        try
        {
            AppDatabase.Initialize(dbPath);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            showError(
                "Không thể mở cơ sở dữ liệu",
                $"{ex.Message}\n\nỨng dụng sẽ thoát an toàn để tránh làm hỏng dữ liệu.");
            return false;
        }
        catch (Exception ex)
        {
            showError(
                "Lỗi khởi tạo cơ sở dữ liệu",
                $"Không thể khởi tạo cơ sở dữ liệu tại:\n{dbPath}\n\nChi tiết: {ex.Message}\n\nỨng dụng sẽ thoát an toàn.");
            return false;
        }
    }
}
