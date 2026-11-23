using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.App.ViewModels;

public enum ItemLineKind { Neutral, Returned, NotReturned }

/// <param name="Text">"Áo thun (Đen/M x2)".</param>
/// <param name="Tag">"Hoàn 1" / "Không hoàn" / "" (chỉ khi đơn hoàn MỘT PHẦN).</param>
public sealed record ItemLine(string Text, string Tag, ItemLineKind Kind)
{
    public string Full => Tag.Length == 0 ? Text : $"{Text} · {Tag}";
}

/// <summary>
/// Cách hiển thị sản phẩm của một đơn khi hoàn MỘT PHẦN (yêu cầu của chủ dự án): luôn giữ ĐỦ mọi sản phẩm,
/// sản phẩm đã hoàn (ReturnedQty &gt; 0) nổi bật, sản phẩm không hoàn xám nhưng vẫn đọc được.
/// Chỉ phân biệt khi có CẢ sản phẩm hoàn lẫn không hoàn; hoàn đủ hoặc chưa biết (không dòng nào ReturnedQty &gt; 0) thì hiển thị như thường.
/// </summary>
public static class ItemDisplay
{
    public static bool IsMixed(IReadOnlyList<OrderItem> items)
    {
        if (items.Count < 2) return false;
        var returned = items.Count(i => i.ReturnedQty > 0);
        return returned > 0 && returned < items.Count;
    }

    private static string TextOf(OrderItem i)
    {
        var variant = string.Join("/", new[] { i.Color, i.Size }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var name = string.IsNullOrWhiteSpace(i.Product) ? "(chưa rõ sản phẩm)" : i.Product;
        return variant.Length == 0 ? $"{name} (x{i.Qty})" : $"{name} ({variant} x{i.Qty})";
    }

    public static IReadOnlyList<ItemLine> Build(IReadOnlyList<OrderItem> items)
    {
        if (items.Count == 0) return new[] { new ItemLine("Chưa có sản phẩm", "", ItemLineKind.Neutral) };
        var mixed = IsMixed(items);
        return items.Select(i => !mixed
            ? new ItemLine(TextOf(i), "", ItemLineKind.Neutral)
            : i.ReturnedQty > 0
                ? new ItemLine(TextOf(i), $"Hoàn {i.ReturnedQty}", ItemLineKind.Returned)
                : new ItemLine(TextOf(i), "Không hoàn", ItemLineKind.NotReturned)).ToList();
    }

    /// <summary>"Hoàn 1/3 SP" khi biết sản phẩm nào hoàn; "Hoàn một phần" khi chỉ có cờ của sàn; ngược lại rỗng.</summary>
    public static string Chip(IReadOnlyList<OrderItem> items, bool isPartialFlag)
    {
        if (IsMixed(items)) return $"Hoàn {items.Count(i => i.ReturnedQty > 0)}/{items.Count} SP";
        return isPartialFlag ? "Hoàn một phần" : "";
    }

    /// <summary>Mỗi sản phẩm một dòng (cho tooltip).</summary>
    public static string Tooltip(IReadOnlyList<OrderItem> items, bool isPartialFlag)
    {
        var chip = Chip(items, isPartialFlag);
        var body = string.Join(Environment.NewLine, Build(items).Select(l => "• " + l.Full));
        return chip.Length == 0 ? body : chip + Environment.NewLine + body;
    }

    /// <summary>Một chuỗi phẳng (tìm kiếm, danh sách chữ): "Áo (x1) · Hoàn 1 | Quần (x1) · Không hoàn".</summary>
    public static string Flat(IReadOnlyList<OrderItem> items, bool isPartialFlag)
    {
        var chip = Chip(items, isPartialFlag);
        var body = string.Join(" | ", Build(items).Select(l => l.Full));
        return chip.Length == 0 ? body : $"[{chip}] {body}";
    }
}
