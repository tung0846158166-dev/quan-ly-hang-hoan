using CommunityToolkit.Mvvm.ComponentModel;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Cổng điều kiện ghi tập trung cho toàn bộ giao diện (T041).
/// Khi mất kết nối cloud (hoặc chưa ghép / bị thu hồi), <see cref="CanWrite"/> = false và mọi lệnh ghi đọc từ đây bị khóa kèm tooltip lý do.
/// Khi chưa bật chế độ cloud (ICloudLink == null), <see cref="CanWrite"/> luôn là true.
/// </summary>
public sealed partial class WriteGate : ObservableObject
{
    public const string DefaultLockedReason = "Mất kết nối cloud — chỉ xem được";

    /// <summary>Cổng mở sẵn dùng làm mặc định khi các ViewModel được khởi tạo độc lập trong test cũ.</summary>
    public static WriteGate AlwaysOpen { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocked))]
    [NotifyPropertyChangedFor(nameof(TooltipReason))]
    private bool _canWrite = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TooltipReason))]
    private string _lockedReason = string.Empty;

    public bool IsLocked => !CanWrite;

    /// <summary>Lý do hiển thị ở tooltip khi bị khóa; null khi được phép ghi (tránh hiện tooltip thừa).</summary>
    public string? TooltipReason => CanWrite
        ? null
        : (string.IsNullOrWhiteSpace(LockedReason) ? DefaultLockedReason : LockedReason);

    public event Action? Changed;

    public void Set(bool canWrite, string? reason = null)
    {
        var normalizedReason = canWrite
            ? string.Empty
            : (string.IsNullOrWhiteSpace(reason) ? DefaultLockedReason : reason.Trim());

        if (CanWrite == canWrite && string.Equals(LockedReason, normalizedReason, StringComparison.Ordinal))
        {
            return;
        }

        CanWrite = canWrite;
        LockedReason = normalizedReason;
        Changed?.Invoke();
    }
}
