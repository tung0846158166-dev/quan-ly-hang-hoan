namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Lần cuối người dùng bấm / gõ / cuộn (chủ dự án 26/11/2026: bảng lag khi đang thao tác). Nạp lại dữ liệu từ cloud hoãn tới lúc người dùng
/// dừng tay một chút, để bảng không bị dựng lại ngay giữa lúc đang bấm/cuộn.
/// </summary>
public static class UserActivity
{
    private static long _lastTicks;

    public static void Touch() => Interlocked.Exchange(ref _lastTicks, DateTime.UtcNow.Ticks);

    public static TimeSpan SinceLast => DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastTicks), DateTimeKind.Utc);

    /// <summary>Chờ tới khi người dùng dừng tay <paramref name="idle"/>, tối đa <paramref name="max"/> (dữ liệu không bị trễ quá lâu).</summary>
    public static async Task WaitForIdleAsync(TimeSpan idle, TimeSpan max)
    {
        var end = DateTime.UtcNow + max;
        while (SinceLast < idle && DateTime.UtcNow < end)
            await Task.Delay(250).ConfigureAwait(false);
    }
}
