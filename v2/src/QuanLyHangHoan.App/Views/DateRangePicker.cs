using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace QuanLyHangHoan.App.Views;

/// <summary>
/// MỘT ô chọn khoảng ngày (chủ dự án 28/11/2026: bộ lọc gọn — chỉ một ô lịch). Bấm ô -> lịch hiện ra, bấm ngày đầu rồi giữ Shift bấm ngày
/// cuối (hoặc kéo chuột) để chọn khoảng; có nút nhanh Hôm nay / 7 ngày / 30 ngày / Tháng này / Xóa. Ràng buộc hai chiều <see cref="From"/>, <see cref="To"/>.
/// </summary>
public sealed class DateRangePicker : ContentControl
{
    public static readonly DependencyProperty FromProperty = DependencyProperty.Register(nameof(From), typeof(DateTime?), typeof(DateRangePicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((DateRangePicker)d).Sync()));
    public static readonly DependencyProperty ToProperty = DependencyProperty.Register(nameof(To), typeof(DateTime?), typeof(DateRangePicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((DateRangePicker)d).Sync()));

    public DateTime? From { get => (DateTime?)GetValue(FromProperty); set => SetValue(FromProperty, value); }
    public DateTime? To { get => (DateTime?)GetValue(ToProperty); set => SetValue(ToProperty, value); }

    private readonly ToggleButton _button = new() { Height = 30, MinWidth = 190, Padding = new Thickness(10, 0, 10, 0), HorizontalContentAlignment = HorizontalAlignment.Left, FontSize = 12 };
    private readonly Popup _popup = new() { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = true };
    private readonly System.Windows.Controls.Calendar _calendar = new() { SelectionMode = CalendarSelectionMode.SingleRange, Margin = new Thickness(4) };
    private bool _syncing;

    public DateRangePicker()
    {
        _popup.PlacementTarget = _button;
        _button.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding(nameof(Popup.IsOpen)) { Source = _popup, Mode = System.Windows.Data.BindingMode.TwoWay });
        _calendar.SelectedDatesChanged += (_, _) =>
        {
            if (_syncing || _calendar.SelectedDates.Count == 0) return;
            var dates = _calendar.SelectedDates.Cast<DateTime>().ToList();
            Set(dates.Min(), dates.Max());
        };
        _calendar.PreviewMouseUp += (_, _) => { if (Mouse.Captured is CalendarItem) Mouse.Capture(null); };   // để nút ngoài lịch bấm được ngay

        var quick = new WrapPanel { Margin = new Thickness(6, 0, 6, 6), MaxWidth = 230 };
        void Q(string text, Func<(DateTime?, DateTime?)> range, bool close = true)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 2, 8, 2), FontSize = 11.5 };
            b.Click += (_, _) => { var (f, t) = range(); Set(f, t); if (close) _popup.IsOpen = false; };
            quick.Children.Add(b);
        }
        Q("Hôm nay", () => (DateTime.Today, DateTime.Today));
        Q("Hôm qua", () => (DateTime.Today.AddDays(-1), DateTime.Today.AddDays(-1)));
        Q("7 ngày", () => (DateTime.Today.AddDays(-6), DateTime.Today));
        Q("30 ngày", () => (DateTime.Today.AddDays(-29), DateTime.Today));
        Q("Tháng này", () => (new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Today));
        Q("✕ Xóa", () => (null, null));

        var panel = new StackPanel();
        panel.Children.Add(_calendar);
        panel.Children.Add(new TextBlock { Text = "Bấm ngày đầu, giữ Shift bấm ngày cuối (hoặc kéo chuột)", FontSize = 11, Margin = new Thickness(8, 0, 8, 6), Opacity = 0.7, TextWrapping = TextWrapping.Wrap, MaxWidth = 230 });
        panel.Children.Add(quick);
        var border = new Border { Child = panel, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(2) };
        border.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrushLight");
        _popup.Child = border;

        var root = new Grid();
        root.Children.Add(_button);
        root.Children.Add(_popup);
        Content = root;
        Sync();
    }

    private void Set(DateTime? from, DateTime? to)
    {
        From = from?.Date;
        To = to?.Date;
    }

    private void Sync()
    {
        _button.Content = new TextBlock { Text = Label(From, To), TextTrimming = TextTrimming.CharacterEllipsis };
        _button.FontWeight = From.HasValue || To.HasValue ? FontWeights.SemiBold : FontWeights.Normal;
        _syncing = true;
        try
        {
            _calendar.SelectedDates.Clear();
            if (From is { } f)
            {
                var t = To ?? f;
                if (t >= f && (t - f).TotalDays <= 400) _calendar.SelectedDates.AddRange(f, t);
                _calendar.DisplayDate = t;
            }
        }
        finally { _syncing = false; }
    }

    public static string Label(DateTime? from, DateTime? to)
    {
        var c = CultureInfo.InvariantCulture;
        if (from is null && to is null) return "📅 Chọn ngày...";
        if (from is { } f && to is { } t) return f.Date == t.Date ? $"📅 {f.ToString("dd/MM/yyyy", c)}" : $"📅 {f.ToString("dd/MM", c)} – {t.ToString("dd/MM/yyyy", c)}";
        return from is { } a ? $"📅 Từ {a.ToString("dd/MM/yyyy", c)}" : $"📅 Đến {to!.Value.ToString("dd/MM/yyyy", c)}";
    }
}
