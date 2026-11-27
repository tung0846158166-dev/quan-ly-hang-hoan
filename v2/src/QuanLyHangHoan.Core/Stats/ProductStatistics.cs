using System.Text.RegularExpressions;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Core.Stats;

public sealed record VariantStat(string Color, string Size, int Units);

public sealed record ProductStat(
    string Name,
    int Units,
    int Orders,
    double Share,
    string TopReason,
    int ShopeeUnits,
    int TikTokUnits,
    IReadOnlyList<VariantStat> Variants,
    int PremiumUnits = 0,
    int StandardUnits = 0);

public sealed record ProductStatsResult(
    IReadOnlyList<ProductStat> Products,
    int TotalUnits,
    int OrdersWithItems,
    int UnknownUnits);

/// <summary>
/// Thống kê SẢN PHẨM bị hoàn: sản phẩm nào hoàn nhiều nhất, màu/size nào, lý do hay gặp, theo sàn. Chỉ ĐỌC dữ liệu đơn, không sửa gì.
/// Đơn hoàn MỘT PHẦN (có cả sản phẩm hoàn lẫn không hoàn): chỉ đếm sản phẩm ĐÃ hoàn, theo số lượng đã hoàn.
/// Sản phẩm giữ chỗ "Chưa rõ sản phẩm..." (hàng quét về nhưng chưa có đơn) không tính vào danh sách, chỉ báo số lượng riêng.
/// </summary>
public static class ProductStatistics
{
    public static ProductStatsResult Compute(IEnumerable<Order> orders)
    {
        var groups = new Dictionary<string, Acc>(StringComparer.Ordinal);
        var totalUnits = 0;
        var unknown = 0;
        var ordersWithItems = 0;
        var list = orders as IReadOnlyList<Order> ?? orders.ToList();
        var families = ProductFamilies.Build(list);   // gộp các kiểu tên của cùng một mẫu (28/11/2026)

        foreach (var order in list)
        {
            if (order.Items.Count == 0) continue;
            var mixed = order.Items.Any(i => i.ReturnedQty > 0) && order.Items.Any(i => i.ReturnedQty <= 0);
            var counted = false;

            foreach (var item in order.Items)
            {
                if (mixed && item.ReturnedQty <= 0) continue;           // sản phẩm KHÔNG hoàn của đơn hoàn một phần
                var units = mixed ? item.ReturnedQty : (item.Qty > 0 ? item.Qty : 1);
                var rawName = Clean(item.Product);
                var premium = IsPremium(rawName, item);
                var name = StripPremium(rawName);
                if (name.Length == 0 || name.StartsWith("Chưa rõ sản phẩm", StringComparison.OrdinalIgnoreCase)) { unknown += units; continue; }

                var key = families.Key(name);
                if (key.Length == 0) { unknown += units; continue; }
                if (!groups.TryGetValue(key, out var acc)) groups[key] = acc = new Acc(name);
                acc.Add(name, units, order, item, premium);
                totalUnits += units;
                counted = true;
            }
            if (counted) ordersWithItems++;
        }

        var products = groups
            .Select(kv => kv.Value.ToStat(totalUnits) is var st && families.Display(kv.Key) is { Length: > 0 } shown ? st with { Name = shown } : kv.Value.ToStat(totalUnits))
            .OrderByDescending(p => p.Units).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new ProductStatsResult(products, totalUnits, ordersWithItems, unknown);
    }

    /// <summary>
    /// Các dòng sản phẩm ĐƯỢC TÍNH của một đơn theo đúng quy tắc của <see cref="Compute"/> (hoàn một phần: chỉ sản phẩm đã hoàn;
    /// bỏ đuôi Premium; bỏ "Chưa rõ sản phẩm"). Key = khóa gộp tên (bỏ dấu, chữ thường).
    /// </summary>
    public static IEnumerable<(string Key, string Name, int Units)> CountedItems(Order order, ProductFamilies? families = null)
    {
        if (order.Items.Count == 0) yield break;
        var mixed = order.Items.Any(i => i.ReturnedQty > 0) && order.Items.Any(i => i.ReturnedQty <= 0);
        foreach (var item in order.Items)
        {
            if (mixed && item.ReturnedQty <= 0) continue;
            var units = mixed ? item.ReturnedQty : (item.Qty > 0 ? item.Qty : 1);
            var name = StripPremium(Clean(item.Product));
            if (name.Length == 0 || name.StartsWith("Chưa rõ sản phẩm", StringComparison.OrdinalIgnoreCase)) continue;
            var key = families?.Key(name) ?? ProductNames.FamilyKey(name);
            if (key.Length == 0) continue;
            yield return (key, name, units);
        }
    }

    private static readonly Regex PremiumTail = new(@"\s*[-–—]?\s*premium(\s+cao\s+c[aấ]p)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Premium = có chữ "Premium" ở tên, màu hoặc size. Chỉ là phân loại BÊN TRONG cùng một mẫu, không phải mẫu khác.</summary>
    public static bool IsPremium(string name, OrderItem item) =>
        name.Contains("premium", StringComparison.OrdinalIgnoreCase)
        || (item.Size ?? "").Contains("premium", StringComparison.OrdinalIgnoreCase)
        || (item.Color ?? "").Contains("premium", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bỏ đuôi "- Premium Cao Cấp" để hai bản của cùng một mẫu gộp thành một dòng.</summary>
    public static string StripPremium(string name)
    {
        var stripped = PremiumTail.Replace(name, "").Trim();
        return stripped.Length == 0 ? name : stripped;
    }

    private static string Clean(string? s) => string.Join(' ', (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed class Acc
    {
        private readonly Dictionary<string, int> _spellings = new(StringComparer.Ordinal);
        private readonly HashSet<string> _orderIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _reasons = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(string, string), int> _variants = new();
        private int _units, _shopee, _tiktok, _premium;

        public Acc(string first) => _spellings[first] = 0;

        public void Add(string spelling, int units, Order order, OrderItem item, bool premium)
        {
            _spellings[spelling] = (_spellings.TryGetValue(spelling, out var s) ? s : 0) + 1;
            _units += units;
            if (premium) _premium += units;
            _orderIds.Add(order.Id);
            var reason = Clean(order.Note);
            if (reason.Length > 0 && reason.Length <= 120) _reasons[reason] = (_reasons.TryGetValue(reason, out var r) ? r : 0) + 1;
            var color = Clean(item.Color); var size = Clean(item.Size);
            _variants[(color, size)] = (_variants.TryGetValue((color, size), out var v) ? v : 0) + units;
            if (order.Platform.Contains("Shopee", StringComparison.OrdinalIgnoreCase)) _shopee += units;
            else if (order.Platform.Contains("TikTok", StringComparison.OrdinalIgnoreCase)) _tiktok += units;
        }

        public ProductStat ToStat(int totalUnits)
        {
            var name = ProductNames.PickDisplay(_spellings);
            var reason = _reasons.Count == 0 ? "" : _reasons.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).First().Key;
            var variants = _variants
                .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Item1, StringComparer.CurrentCultureIgnoreCase).ThenBy(kv => kv.Key.Item2, StringComparer.CurrentCultureIgnoreCase)
                .Select(kv => new VariantStat(kv.Key.Item1, kv.Key.Item2, kv.Value)).ToList();
            return new ProductStat(name, _units, _orderIds.Count, totalUnits == 0 ? 0 : (double)_units / totalUnits, reason, _shopee, _tiktok, variants, _premium, _units - _premium);
        }
    }
}
