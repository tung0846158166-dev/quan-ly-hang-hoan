using System.Text.RegularExpressions;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Core.Import;

public enum ImportProductMode
{
    /// <summary>"Nhập bổ sung": giữ sản phẩm không có trong file mới (mặc định của app cũ).</summary>
    Supplement,
    /// <summary>"Thay toàn bộ sản phẩm của từng kiện có trong file".</summary>
    Replace,
}

/// <summary>
/// Gộp dòng sản phẩm khi nhập lại: dedupeOrderItems + mergeImportedItems của app cũ. Cùng một mặt hàng mà mỗi nguồn ghi khác
/// (tên đầy đủ/rút gọn, có/không màu-size) thì gộp thành MỘT, lấy tên dài nhất, số lượng LỚN NHẤT (không cộng).
/// </summary>
public static class ItemMerge
{
    private static string Nm(string? v) => Regex.Replace(LegacyText.RemoveTones(v ?? ""), "[^a-z0-9]+", " ").Trim();
    private static bool Compat(string a, string b) => a.Length == 0 || b.Length == 0 || a == b || a.Contains(b) || b.Contains(a);
    /// <summary>it.sku || it.sellerSku || it.skuId (v2 lưu sellerSku vào Sku).</summary>
    public static string SkuOf(OrderItem it) => ((!string.IsNullOrEmpty(it.Sku) ? it.Sku : it.SkuId) ?? "").Trim();

    private sealed class Info
    {
        public required OrderItem It { get; init; }
        public string Name { get; set; } = "";
        public string Color { get; init; } = "";
        public string Size { get; init; } = "";
        public string Sku { get; init; } = "";
        public int Qty { get; set; }
        public bool HasVariant => Color.Length > 0 || Size.Length > 0;
    }

    public static OrderItem Copy(OrderItem i) => new()
    {
        Product = i.Product, Color = i.Color, Size = i.Size, Qty = i.Qty, Sku = i.Sku, SkuId = i.SkuId,
        ImportPrices = i.ImportPrices is null ? null : new(i.ImportPrices),
    };

    public static List<OrderItem> Dedupe(List<OrderItem> items)
    {
        if (items.Count < 2) return items;
        var info = items.Select(it => new Info { It = it, Name = Nm(it.Product), Color = Nm(it.Color), Size = Nm(it.Size), Sku = SkuOf(it) }).ToList();
        var output = new List<Info>();
        bool Same(Info a, Info b) => (a.Sku.Length > 0 || b.Sku.Length > 0) ? (a.Sku.Length > 0 && a.Sku == b.Sku)
            : (a.Color == b.Color && a.Size == b.Size && Compat(a.Name, b.Name));

        foreach (var x in info.Where(i => i.HasVariant).Concat(info.Where(i => !i.HasVariant)))
        {
            var qty = x.It.Qty > 0 ? x.It.Qty : 1;
            if (!x.HasVariant && x.Sku.Length == 0)
            {
                // dòng trống chỉ bị bỏ khi đã có dòng có màu/size (hoặc sku) cùng tên hàng; các dòng trống với nhau giữ nguyên.
                if (output.Any(y => (y.HasVariant || y.Sku.Length > 0) && Compat(x.Name, y.Name))) continue;
            }
            else
            {
                var m = output.FirstOrDefault(y => Same(y, x));
                if (m is not null)
                {
                    m.Qty = Math.Max(m.Qty, qty);
                    if (x.Name.Length > m.Name.Length) { m.It.Product = x.It.Product; m.Name = x.Name; }
                    if (m.It.ImportPrices is null && x.It.ImportPrices is not null) m.It.ImportPrices = x.It.ImportPrices;
                    continue;
                }
            }
            output.Add(new Info { It = Copy(x.It), Name = x.Name, Color = x.Color, Size = x.Size, Sku = x.Sku, Qty = qty });
        }

        var res = output.Select(y => { y.It.Qty = y.Qty; return y.It; }).ToList();
        if (res.Count >= items.Count) return items;
        int Rank(OrderItem it)
        {
            var i = items.FindIndex(z => (z.Color ?? "") == (it.Color ?? "") && (z.Size ?? "") == (it.Size ?? ""));
            return i < 0 ? 1_000_000_000 : i;
        }
        return res.Select((it, idx) => (it, idx)).OrderBy(t => Rank(t.it)).ThenBy(t => t.idx).Select(t => t.it).ToList(); // sort ổn định như JS
    }

    private static string KeyOf(OrderItem item)
    {
        var sku = SkuOf(item);
        return sku.Length > 0 ? "SKU:" + sku : $"[{item.Product ?? ""}\u0001{item.Color ?? ""}\u0001{item.Size ?? ""}]";
    }

    /// <summary>mergeImportedItems(previous, incoming, mode)</summary>
    public static List<OrderItem> Merge(List<OrderItem> previous, List<OrderItem> incoming, ImportProductMode mode)
    {
        if (mode == ImportProductMode.Replace) return incoming.Select(Copy).ToList();

        var replacements = new Dictionary<string, List<OrderItem>>();
        var replacementOrder = new List<string>();
        foreach (var item in incoming)
        {
            var k = KeyOf(item);
            if (!replacements.TryGetValue(k, out var list)) { replacements[k] = list = new(); replacementOrder.Add(k); }
            list.Add(item);
        }
        var result = new List<OrderItem>();
        var seen = new HashSet<string>();
        foreach (var item in previous)
        {
            var k = KeyOf(item);
            if (replacements.TryGetValue(k, out var rep)) { if (!seen.Contains(k)) result.AddRange(rep); }
            else result.Add(item);
            seen.Add(k);
        }
        foreach (var k in replacementOrder) if (!seen.Contains(k)) result.AddRange(replacements[k]);
        return Dedupe(result).Select(Copy).ToList();
    }
}
