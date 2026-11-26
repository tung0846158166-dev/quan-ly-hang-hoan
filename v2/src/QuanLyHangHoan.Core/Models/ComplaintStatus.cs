namespace QuanLyHangHoan.Core.Models;

/// <summary>Trạng thái khiếu nại (chủ dự án 26/11/2026). Cùng giá trị với cloud.</summary>
public static class ComplaintStatuses
{
    public const string Processing = "Đang xử lý";
    public const string Success = "Thành công";
    public const string Failed = "Thất bại";
    public static readonly string[] All = { Processing, Success, Failed };
}
