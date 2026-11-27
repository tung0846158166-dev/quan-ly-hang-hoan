namespace QuanLyHangHoan.App.ViewModels;

/// <summary>
/// Trang biết mình đang hiện hay đang ẩn (khung <see cref="Views.PageHost"/> báo khi đổi tab). Chủ dự án 27/11/2026: giao diện lag khi
/// cập nhật vì MỌI bảng đang ẩn cũng vẽ lại dòng mỗi lần có dữ liệu mới. Trang ẩn chỉ giữ danh sách mới nhất, mở ra mới đưa lên bảng.
/// Số đếm / thẻ thanh bên vẫn cập nhật ngay (không đi qua đây).
/// </summary>
public interface IPageVisibilityAware
{
    void SetPageVisible(bool visible);
}

/// <summary>Cửa chặn danh sách hiển thị: hiện thì đưa ngay, ẩn thì giữ bản mới nhất tới lúc hiện. Mặc định là hiện (test, không có khung).</summary>
public sealed class PageGate<T>
{
    private readonly Action<IReadOnlyList<T>> _apply;
    private IReadOnlyList<T>? _pending;

    public PageGate(Action<IReadOnlyList<T>> apply) => _apply = apply;

    public bool IsVisible { get; private set; } = true;

    public void Push(IReadOnlyList<T> rows)
    {
        if (IsVisible) { _pending = null; _apply(rows); }
        else _pending = rows;
    }

    public void SetVisible(bool visible)
    {
        IsVisible = visible;
        if (visible && _pending is { } p) { _pending = null; _apply(p); }
    }
}
