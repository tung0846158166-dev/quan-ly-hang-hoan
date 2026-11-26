using QuanLyHangHoan.Data.Cloud;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Máy tính là máy chính: đơn nạp từ Salework / Excel / thư mục phải lên cloud cho các máy khác. Nạp lúc mất mạng thì
/// <see cref="CloudMode.PublishAsync"/> bỏ qua, nên mỗi lần chuyển từ khóa ghi sang ghi được phải đẩy bù (so băm: không trùng, không sót).
/// </summary>
public sealed class CloudPublishTrigger
{
    private bool _writable;

    /// <summary>true = vừa chuyển từ khóa ghi sang ghi được: nên đẩy bù.</summary>
    private readonly object _lock = new();

    public bool OnLinkChanged(bool canWrite)
    {
        lock (_lock)   // gọi từ nhiều luồng nền
        {
            var rising = canWrite && !_writable;
            _writable = canWrite;
            return rising;
        }
    }

    /// <summary>Câu báo cho người dùng; null khi không gửi gì (không làm phiền).</summary>
    public static string? Note(CloudPublishResult r)
    {
        if (r.Sent == 0) return null;
        return $"Đã đẩy {r.Sent:N0} đơn lên cloud cho các máy khác (mới {r.Added:N0}, cập nhật {r.Updated:N0}" + (r.Rejected > 0 ? $", bị từ chối {r.Rejected:N0}" : "") + ").";
    }
}
