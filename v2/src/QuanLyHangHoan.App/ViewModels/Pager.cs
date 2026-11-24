using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace QuanLyHangHoan.App.ViewModels;

/// <summary>Bộ chia trang dùng chung cho các bảng (100 đơn/trang): giữ danh sách đã lọc đầy đủ, chỉ đưa một trang ra cho bảng dựng.</summary>
public sealed partial class Pager<T> : ObservableObject
{
    public const int PageSize = 100;

    private readonly Action<IReadOnlyList<T>> _onPage;
    private List<T> _all = [];
    private int _current = 1;

    public Pager(Action<IReadOnlyList<T>> onPage) { _onPage = onPage; }

    /// <summary>Tất cả mục sau khi lọc (mọi trang): dùng để xuất file và tìm mục.</summary>
    public IReadOnlyList<T> All => _all;

    /// <summary>Đặt khi chuyển trang / đổi bộ lọc: giao diện cuộn bảng về đầu.</summary>
    public bool ResetScrollOnNextChange { get; set; }

    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private string _pageInput = "1";

    public int CurrentPage => _current;
    public bool ShowPager => _all.Count > PageSize;
    public bool CanGoPrev => _current > 1;
    public bool CanGoNext => _current < TotalPages;
    public string RangeText
    {
        get
        {
            var from = _all.Count == 0 ? 0 : (_current - 1) * PageSize + 1;
            var to = Math.Min(_current * PageSize, _all.Count);
            return $"{from:N0}–{to:N0} / {_all.Count:N0}";
        }
    }

    /// <summary>Nạp danh sách đã lọc. resetPage=true (đổi bộ lọc) về trang 1; false (nạp lại dữ liệu) giữ trang đang xem.</summary>
    public void SetItems(List<T> filtered, bool resetPage)
    {
        _all = filtered;
        TotalPages = Math.Max(1, (filtered.Count + PageSize - 1) / PageSize);
        if (resetPage) _current = 1;
        Refresh(scrollTop: resetPage);
    }

    public int GoToPage(int page)
    {
        var p = Math.Clamp(page, 1, TotalPages);
        if (p != _current) { _current = p; Refresh(scrollTop: true); }
        else PageInput = p.ToString();
        return p;
    }

    /// <summary>Chuyển tới trang chứa mục này.</summary>
    public void GoToItem(T item)
    {
        var i = _all.IndexOf(item);
        if (i >= 0) GoToPage(i / PageSize + 1);
    }

    private void Refresh(bool scrollTop)
    {
        _current = Math.Clamp(_current, 1, TotalPages);
        ResetScrollOnNextChange = scrollTop;
        _onPage(_all.Count <= PageSize ? _all : _all.Skip((_current - 1) * PageSize).Take(PageSize).ToList());
        PageInput = _current.ToString();
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(ShowPager));
        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(RangeText));
        FirstPageCommand.NotifyCanExecuteChanged();
        PrevPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        LastPageCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGoPrev))] private void FirstPage() => GoToPage(1);
    [RelayCommand(CanExecute = nameof(CanGoPrev))] private void PrevPage() => GoToPage(_current - 1);
    [RelayCommand(CanExecute = nameof(CanGoNext))] private void NextPage() => GoToPage(_current + 1);
    [RelayCommand(CanExecute = nameof(CanGoNext))] private void LastPage() => GoToPage(TotalPages);

    [RelayCommand]
    private void GoToPageInput()
    {
        if (int.TryParse(PageInput?.Trim().Replace(".", "").Replace(",", ""), out var n)) GoToPage(n);
        else PageInput = _current.ToString();
    }
}
