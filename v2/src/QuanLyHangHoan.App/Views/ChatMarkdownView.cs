using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QuanLyHangHoan.Data.Ai;

namespace QuanLyHangHoan.App.Views;

/// <summary>Hiển thị câu trả lời của AI có định dạng (đậm, nghiêng, mã, danh sách, bảng) thay vì chữ thô có dấu **.</summary>
public sealed class ChatMarkdownView : Border
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown), typeof(string), typeof(ChatMarkdownView), new PropertyMetadata("", (d, _) => ((ChatMarkdownView)d).Rebuild()));

    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(ChatMarkdownView), new PropertyMetadata(Brushes.Black, (d, _) => ((ChatMarkdownView)d).Rebuild()));

    public static readonly DependencyProperty TextSizeProperty = DependencyProperty.Register(
        nameof(TextSize), typeof(double), typeof(ChatMarkdownView), new PropertyMetadata(13.5, (d, _) => ((ChatMarkdownView)d).Rebuild()));

    public string Markdown { get => (string)GetValue(MarkdownProperty); set => SetValue(MarkdownProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public double TextSize { get => (double)GetValue(TextSizeProperty); set => SetValue(TextSizeProperty, value); }

    private void Rebuild()
    {
        var panel = new StackPanel();
        foreach (var block in ChatMarkdown.Parse(Markdown))
        {
            switch (block)
            {
                case MdHeading h:
                    panel.Children.Add(Text(h.Inlines, bold: true, size: TextSize + 1, margin: new Thickness(0, 4, 0, 4)));
                    break;
                case MdParagraph p:
                    panel.Children.Add(Text(p.Inlines, margin: new Thickness(0, 0, 0, 5)));
                    break;
                case MdListItem l:
                    panel.Children.Add(ListItem(l));
                    break;
                case MdTable t:
                    panel.Children.Add(Table(t));
                    break;
            }
        }
        Child = panel;
    }

    private TextBlock Text(IEnumerable<MdInline> inlines, bool bold = false, double? size = null, Thickness? margin = null)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = size ?? TextSize,
            Foreground = TextBrush,
            Margin = margin ?? new Thickness(0),
            LineHeight = (size ?? TextSize) * 1.45,
        };
        if (bold) tb.FontWeight = FontWeights.Bold;
        foreach (var i in inlines)
        {
            var run = new System.Windows.Documents.Run(i.Text);
            if (i.Bold) run.FontWeight = FontWeights.Bold;
            if (i.Italic) run.FontStyle = FontStyles.Italic;
            if (i.Code)
            {
                run.FontFamily = new FontFamily("Consolas");
                run.Background = new SolidColorBrush(Color.FromArgb(40, 100, 116, 139));
            }
            tb.Inlines.Add(run);
        }
        return tb;
    }

    private UIElement ListItem(MdListItem l)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new TextBlock { Text = l.Marker, Foreground = TextBrush, FontSize = TextSize, FontWeight = FontWeights.SemiBold });
        var body = Text(l.Inlines);
        Grid.SetColumn(body, 1);
        g.Children.Add(body);
        return g;
    }

    private UIElement Table(MdTable t)
    {
        var grid = new Grid();
        var cols = t.Rows.Count == 0 ? 0 : t.Rows.Max(r => r.Count);
        for (var c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var r = 0; r < t.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var c = 0; c < t.Rows[r].Count; c++)
            {
                var header = t.HasHeader && r == 0;
                var cell = new Border
                {
                    Padding = new Thickness(9, 5, 9, 5),
                    BorderThickness = new Thickness(0, 0, c < cols - 1 ? 1 : 0, 1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(60, 100, 116, 139)),
                    Background = header ? new SolidColorBrush(Color.FromArgb(40, 99, 102, 241)) : Brushes.Transparent,
                    Child = Text(t.Rows[r][c], bold: header, size: TextSize - 1),
                };
                ((TextBlock)cell.Child).TextWrapping = TextWrapping.NoWrap;
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }
        var frame = new Border
        {
            BorderThickness = new Thickness(1, 1, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 100, 116, 139)),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 2, 0, 6),
            Child = grid,
        };
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = frame,
        };
    }
}
