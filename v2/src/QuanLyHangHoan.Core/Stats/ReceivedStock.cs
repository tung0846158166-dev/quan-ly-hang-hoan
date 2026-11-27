using System.Text.RegularExpressions;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Core.Stats;

/// <summary>Một kiện đã nhận chứa sản phẩm này (để tìm đúng kiện ngoài kho).</summary>
public sealed record ReceivedParcel(Order Order, int Units, DateTime ReceivedAt);

/// <summary>Một dòng hàng sẵn kho: mẫu + Premium/thường + màu + size, tổng số cái, các kiện chứa nó (mới nhận trước).</summary>
public sealed record ReceivedStockRow(string Key, string Product, string Color, string Size, int Units, DateTime LastReceived, IReadOnlyList<ReceivedParcel> Parcels, bool Premium = false);

/// <summary>
/// HÀNG HOÀN SẴN KHO (chủ dự án 27/11/2026): sản phẩm của các kiện hoàn ĐÃ NHẬN tại kho trong một khoảng thời gian, gom theo
/// mẫu + màu + size. Có đơn mới trùng sản phẩm thì lấy luôn hàng hoàn đóng đơn, khỏi cất lên kệ.
/// 28/11/2026: các kiểu tên của CÙNG một mẫu (tên ngắn "STRIPES", tên dài, tiền tố "[ DEAL ... ]") gộp một dòng (<see cref="ProductNames"/>);
/// bản Premium tách dòng riêng (đóng đơn phải lấy đúng loại) dù chữ "Premium" nằm ở tên, màu hay size.
/// Đếm như thống kê sản phẩm: đơn hoàn một phần chỉ tính sản phẩm đã hoàn; bỏ "Chưa rõ sản phẩm".
/// </summary>
public static class ReceivedStock
{
    private static readonly Regex PremiumWords = new(@"\s*[-–—]?\s*premium(\s+cao\s+c[aấ]p)?\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<ReceivedStockRow> Build(IEnumerable<Order> orders, DateTime since, string? query = null, Func<string?, DateTime?>? parseDate = null)
    {
        var parse = parseDate ?? ProductLookup.ParseDate;
        var terms = ProductLookup.Terms(query);
        var list = orders as IReadOnlyList<Order> ?? orders.ToList();
        var families = ProductFamilies.Build(list);   // mọi kiểu tên của cùng một mẫu: một dòng, một tên hiển thị
        var map = new Dictionary<(string, bool, string, string), Acc>();
        foreach (var o in list)
        {
            if (!ProductLookup.IsReceived(o) || o.Items.Count == 0) continue;
            if (parse(o.RecvDate) is not { } at || at < since) continue;
            var mixed = o.Items.Any(i => i.ReturnedQty > 0) && o.Items.Any(i => i.ReturnedQty <= 0);
            foreach (var item in o.Items)
            {
                if (mixed && item.ReturnedQty <= 0) continue;
                var units = mixed ? item.ReturnedQty : (item.Qty > 0 ? item.Qty : 1);
                var raw = Clean(item.Product);
                if (raw.Length == 0 || raw.StartsWith("Chưa rõ sản phẩm", StringComparison.OrdinalIgnoreCase)) continue;
                var premium = ProductStatistics.IsPremium(raw, item);
                var name = ProductStatistics.StripPremium(raw);
                var fk = families.Key(name);
                if (fk.Length == 0) continue;
                var color = StripPremiumWords(item.Color);
                var size = StripPremiumWords(item.Size);
                var ck = LegacyText.Key(color);
                var sk = LegacyText.Key(size);
                // tìm theo mọi kiểu tên của mẫu + màu + size (vd: "ao thun den L", "stripes xanh than xl", "premium")
                if (terms.Length > 0 && !terms.All((families.SearchText(fk) + "|" + ck + "|" + sk + (premium ? "|premium" : "")).Contains)) continue;
                var k = (fk, premium, ck, sk);
                if (!map.TryGetValue(k, out var a)) map[k] = a = new Acc(color, size, premium);
                a.Add(name, o, units, at);
            }
        }
        return map.Select(kv => kv.Value.ToRow(kv.Key.Item1) is var r && families.Display(kv.Key.Item1) is { Length: > 0 } shown ? r with { Product = shown } : kv.Value.ToRow(kv.Key.Item1))
            .OrderBy(r => r.Product, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Premium)
            .ThenBy(r => r.Color, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Size, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>"XL - Premium Cao Cấp" -> "XL".</summary>
    public static string StripPremiumWords(string? s) => Clean(PremiumWords.Replace(s ?? "", " "));

    private static string Clean(string? s) => Regex.Replace((s ?? "").Trim(), @"\s+", " ");

    private sealed class Acc(string color, string size, bool premium)
    {
        private readonly List<ReceivedParcel> _parcels = new();
        private readonly Dictionary<string, int> _names = new(StringComparer.Ordinal);

        public void Add(string name, Order o, int units, DateTime at)
        {
            _names[name] = (_names.TryGetValue(name, out var n) ? n : 0) + units;
            var i = _parcels.FindIndex(p => ReferenceEquals(p.Order, o));
            if (i >= 0) _parcels[i] = _parcels[i] with { Units = _parcels[i].Units + units };
            else _parcels.Add(new ReceivedParcel(o, units, at));
        }

        public ReceivedStockRow ToRow(string key)
        {
            var list = _parcels.OrderByDescending(p => p.ReceivedAt).ToList();
            return new ReceivedStockRow(key, ProductNames.PickDisplay(_names), color, size, list.Sum(p => p.Units), list[0].ReceivedAt, list, premium);
        }
    }
}
