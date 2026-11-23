using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.App.Controls;

/// <summary>
/// Ô hiển thị danh sách sản phẩm của một đơn trên MỘT dòng: chip "Hoàn 1/3 SP" rồi các sản phẩm cách nhau " · ".
/// Sản phẩm đã hoàn: đậm + màu nhấn + nhãn "Hoàn N"; không hoàn: xám + nhãn "Không hoàn". Màu lấy từ theme (sáng/tối) nên đổi theme là đổi màu.
/// Luôn có nhãn chữ, không chỉ dựa vào màu.
/// </summary>
public sealed class ItemLinesTextBlock : TextBlock
{
    public static readonly DependencyProperty LinesProperty = DependencyProperty.Register(
        nameof(Lines), typeof(IEnumerable), typeof(ItemLinesTextBlock), new PropertyMetadata(null, (d, _) => ((ItemLinesTextBlock)d).Rebuild()));

    public static readonly DependencyProperty ChipProperty = DependencyProperty.Register(
        nameof(Chip), typeof(string), typeof(ItemLinesTextBlock), new PropertyMetadata("", (d, _) => ((ItemLinesTextBlock)d).Rebuild()));

    public IEnumerable? Lines { get => (IEnumerable?)GetValue(LinesProperty); set => SetValue(LinesProperty, value); }
    public string Chip { get => (string)GetValue(ChipProperty); set => SetValue(ChipProperty, value); }

    public ItemLinesTextBlock()
    {
        TextTrimming = TextTrimming.CharacterEllipsis;
        TextWrapping = TextWrapping.NoWrap;
        Loaded += (_, _) => Rebuild();   // lấy lại màu theo theme khi hiện ra
    }

    private Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private void Rebuild()
    {
        Inlines.Clear();
        var lines = Lines?.OfType<ItemLine>().ToList() ?? new List<ItemLine>();
        // Đơn hoàn MỘT PHẦN: mỗi sản phẩm một dòng để thấy ĐỦ mọi sản phẩm (dòng bảng tự cao ra); còn lại giữ một dòng gọn như cũ.
        var vertical = lines.Any(l => l.Kind != ItemLineKind.Neutral);
        TextWrapping = vertical ? TextWrapping.Wrap : TextWrapping.NoWrap;
        TextTrimming = vertical ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        var chip = Chip ?? "";
        if (chip.Length > 0)
        {
            Inlines.Add(new Run(chip) { FontWeight = FontWeights.Bold, Foreground = Res("ItemReturnedBrush", Brushes.OrangeRed) });
            if (vertical) Inlines.Add(new LineBreak()); else Inlines.Add(new Run("  "));
        }
        var first = true;
        foreach (var l in lines)
        {
            if (!first)
            {
                if (vertical) Inlines.Add(new LineBreak());
                else Inlines.Add(new Run("  ·  ") { Foreground = Res("ItemNotReturnedBrush", Brushes.Gray) });
            }
            first = false;
            switch (l.Kind)
            {
                case ItemLineKind.Returned:
                    Inlines.Add(new Run(l.Text) { FontWeight = FontWeights.Bold, Foreground = Res("ItemReturnedBrush", Brushes.OrangeRed) });
                    Inlines.Add(new Run($" [{l.Tag}]") { FontWeight = FontWeights.Bold, Foreground = Res("ItemReturnedBrush", Brushes.OrangeRed) });
                    break;
                case ItemLineKind.NotReturned:
                    Inlines.Add(new Run(l.Text) { Foreground = Res("ItemNotReturnedBrush", Brushes.Gray) });
                    Inlines.Add(new Run($" [{l.Tag}]") { FontSize = FontSize * 0.9, Foreground = Res("ItemNotReturnedBrush", Brushes.Gray) });
                    break;
                default:
                    Inlines.Add(new Run(l.Text));
                    break;
            }
        }
    }
}
