using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

internal static class OrderEditHelper
{
    public static async Task<bool> HandleResultAsync(
        EditResult result,
        Func<Task>? onDataChangedAsync,
        Action<string, bool>? onNotify)
    {
        switch (result.Kind)
        {
            case EditKind.Saved:
                if (onDataChangedAsync is not null)
                {
                    await onDataChangedAsync();
                }
                return true;

            case EditKind.NoChange:
                return false;

            case EditKind.Conflict:
                onNotify?.Invoke("Dữ liệu đã bị thay đổi ở nơi khác (ví dụ vừa quét). Đang tải lại, không ghi đè.", true);
                if (onDataChangedAsync is not null)
                {
                    await onDataChangedAsync();
                }
                return false;

            default:
                var msg = string.IsNullOrWhiteSpace(result.Message)
                    ? "Thao tác không thành công."
                    : result.Message;
                onNotify?.Invoke(msg, true);
                return false;
        }
    }

    public static async Task<(int Saved, int Other, string? FirstError)> RunBulkAsync<TRow>(
        IReadOnlyList<TRow> rows,
        Func<TRow, Task<EditResult>> operation,
        Func<Task>? onDataChangedAsync,
        Action<string, bool>? onNotify,
        Action<IReadOnlyList<TRow>>? onSavedRows = null)
    {
        int saved = 0;
        int other = 0;
        string? firstError = null;
        var savedRows = new List<TRow>();

        foreach (var row in rows)
        {
            var result = await operation(row);
            if (result.Kind == EditKind.Saved)
            {
                saved++;
                savedRows.Add(row);
            }
            else
            {
                other++;
                if (result.Kind != EditKind.NoChange && firstError is null)
                {
                    firstError = result.Kind == EditKind.Conflict
                        ? "Có đơn đã bị thay đổi ở nơi khác"
                        : result.Message;
                }
            }
        }

        if (saved > 0)
        {
            onSavedRows?.Invoke(savedRows);
            if (onDataChangedAsync is not null)
            {
                await onDataChangedAsync();
            }
        }

        var summary = firstError is null
            ? $"{saved} thành công, {other} không đổi/lỗi."
            : $"{saved} thành công, {other} không đổi/lỗi. Lỗi đầu tiên: {firstError}";
        onNotify?.Invoke(summary, other > 0 && firstError is not null);
        return (saved, other, firstError);
    }
}
