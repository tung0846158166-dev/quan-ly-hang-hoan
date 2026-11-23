using CommunityToolkit.Mvvm.ComponentModel;

namespace QuanLyHangHoan.App.ViewModels;

public sealed partial class NavigationItem : ObservableObject
{
    public NavigationItem(
        string key,
        string icon,
        string title,
        string section,
        Func<ObservableObject> createViewModel,
        string badge = "",
        bool showSectionHeader = false)
    {
        Key = key;
        Icon = icon;
        Title = title;
        Section = section;
        CreateViewModel = createViewModel;
        _badge = badge;
        ShowSectionHeader = showSectionHeader && !string.IsNullOrWhiteSpace(section);
    }

    public string Key { get; }
    public string Icon { get; }
    public string Title { get; }
    public string Section { get; }
    public bool ShowSectionHeader { get; }
    public Func<ObservableObject> CreateViewModel { get; }

    [ObservableProperty]
    private string _badge;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupChevron))]
    [NotifyPropertyChangedFor(nameof(ShowCollapsedGroupBadge))]
    private bool _isGroupCollapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCollapsedGroupBadge))]
    private string _groupBadge = string.Empty;

    public string GroupChevron => IsGroupCollapsed ? "▸" : "▾";

    public bool ShowCollapsedGroupBadge => IsGroupCollapsed && !string.IsNullOrWhiteSpace(GroupBadge);
}
