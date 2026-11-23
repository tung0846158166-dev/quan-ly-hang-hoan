using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;

namespace QuanLyHangHoan.App.ViewModels;

public sealed class NotificationItemViewModel
{
    public NotificationItemViewModel(NotificationEntry e, DateTime now)
    {
        Id = e.Id; Target = e.Target; Level = e.Level; IsUnread = !e.Read;
        Icon = e.Level switch { NotificationLevel.Red => "🔴", NotificationLevel.Yellow => "🟡", NotificationLevel.Success => "✅", _ => "ℹ️" };
        Title = e.Count > 1 ? $"{e.Title}  ×{e.Count}" : e.Title;
        Detail = e.Detail;
        TimeText = NotificationCenter.RelativeTime(e.Time, now);
        LevelKey = e.Level.ToString();
    }

    public string Id { get; }
    public string Target { get; }
    public NotificationLevel Level { get; }
    public string LevelKey { get; }
    public bool IsUnread { get; }
    public string Icon { get; }
    public string Title { get; }
    public string Detail { get; }
    public string TimeText { get; }
}

/// <summary>Chuông thông báo ở thanh trên: số chưa đọc ("99+" khi nhiều, ẩn khi 0), khung thả xuống, đánh dấu đã đọc, xóa danh sách (không đụng dữ liệu đơn).</summary>
public sealed partial class NotificationCenterViewModel : ObservableObject
{
    private readonly NotificationCenter _center;
    private readonly Func<DateTime> _clock;
    private readonly Action<string> _navigate;
    private readonly Action<Action> _dispatch;

    public NotificationCenterViewModel(NotificationCenter center, Func<DateTime> clock, Action<string> navigate, Action<Action> dispatch)
    {
        _center = center; _clock = clock; _navigate = navigate; _dispatch = dispatch;
        _center.Changed += () => _dispatch(Refresh);
        Refresh();
    }

    public ObservableCollection<NotificationItemViewModel> Items { get; } = new();
    public bool IsDemoMode { get; set; }

    [ObservableProperty] private int _unreadCount;
    [ObservableProperty] private string _unreadText = "";
    [ObservableProperty] private bool _hasUnread;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _isOpen;

    public void Refresh()
    {
        var now = _clock();
        var snap = _center.Snapshot();
        Items.Clear();
        foreach (var e in snap) Items.Add(new NotificationItemViewModel(e, now));
        UnreadCount = snap.Count(e => !e.Read);
        HasUnread = UnreadCount > 0;
        UnreadText = UnreadCount > 99 ? "99+" : UnreadCount.ToString();
        IsEmpty = snap.Count == 0;
    }

    [RelayCommand] private void Toggle() { if (!IsOpen) Refresh(); IsOpen = !IsOpen; }
    [RelayCommand] private void Close() => IsOpen = false;
    [RelayCommand] private void MarkAllRead() => _center.MarkAllRead();
    [RelayCommand] private void ClearAll() => _center.ClearAll();

    [RelayCommand]
    private void OpenItem(NotificationItemViewModel? item)
    {
        if (item is null) return;
        _center.MarkRead(item.Id);
        IsOpen = false;
        if (!string.IsNullOrWhiteSpace(item.Target)) _navigate(item.Target);
    }
}
