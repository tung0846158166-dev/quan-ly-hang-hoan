namespace QuanLyHangHoan.App;

/// <summary>
/// Loại bản cài (chủ dự án 26/11/2026): máy CHÍNH (đầy đủ) hoặc máy PHỤ (bỏ "Tạo mã ghép" và "Thiết bị đã ghép" trong Cài đặt).
/// Chọn lúc dựng: <c>dotnet publish -p:Edition=Secondary</c> đặt hằng biên dịch SECONDARY (xem QuanLyHangHoan.App.csproj).
/// </summary>
public static class AppEdition
{
#if SECONDARY
    private const bool Default = true;
#else
    private const bool Default = false;
#endif

    /// <summary>Bản máy phụ. Chỉ test mới được đổi giá trị này.</summary>
    public static bool IsSecondary { get; set; } = Default;

    /// <summary>Hậu tố tiêu đề cửa sổ.</summary>
    public static string TitleSuffix => IsSecondary ? " (máy phụ)" : "";
}
