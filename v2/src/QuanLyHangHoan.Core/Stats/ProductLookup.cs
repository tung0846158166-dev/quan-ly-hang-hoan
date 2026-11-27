using System.Globalization;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Core.Stats;

/// <summary>Số cái theo kỳ: hôm nay, tuần này (từ thứ 2), tháng này, tất cả.</summary>
public sealed record PeriodUnits(int Today, int Week, int Month, int Total)
{
    public static readonly PeriodUnits Zero = new(0, 0, 0, 0);
}

/// <param name="Received">Đã VỀ KHO (đã nhận), tính theo ngày nhận kho.</param>
/// <param name="Returned">Bị hoàn, tính theo ngày hoàn (kể cả chưa về).</param>
public sealed record ProductLookupRow(string Name, PeriodUnits Received, PeriodUnits Returned, int Orders);

/// <summary>
/// Tra nhanh một sản phẩm (chủ dự án 27/11/2026): gõ tên là thấy bao nhiêu cái về kho / bị hoàn trong hôm nay, tuần này, tháng này.
/// Đếm cùng quy tắc với <see cref="ProductStatistics"/>. Khớp tên không phân biệt dấu / hoa thường, mọi từ gõ vào đều phải có trong tên.
/// </summary>
public static class ProductLookup
{
    private static readonly string[] Formats =
    [
        "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy", "d/M/yyyy HH:mm:ss", "d/M/yyyy HH:mm", "d/M/yyyy",
        "HH:mm:ss dd/MM/yyyy", "HH:mm dd/MM/yyyy", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd",
    ];

    public static DateTime? ParseDate(string? s)
    {
        var t = (s ?? "").Trim();
        if (t.Length == 0) return null;
        return DateTime.TryParseExact(t, Formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d) ? d : null;
    }

    public static bool IsReceived(Order o) => o.Status is ReceiveStatus.Received or "Đã nhận";

    /// <summary>Thứ 2 của tuần chứa <paramref name="day"/>.</summary>
    public static DateTime WeekStart(DateTime day) => day.Date.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>Các từ khóa (đã bỏ dấu) của câu tìm; rỗng = không tìm.</summary>
    public static string[] Terms(string? query) =>
        (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(LegacyText.Key).Where(k => k.Length > 0).ToArray();

    public static bool Matches(string productKey, string[] terms) => terms.Length > 0 && terms.All(productKey.Contains);

    public static IReadOnlyList<ProductLookupRow> Search(IEnumerable<Order> orders, string? query, DateTime now, int limit = 10, Func<string?, DateTime?>? parseDate = null)
    {
        var terms = Terms(query);
        if (terms.Length == 0) return Array.Empty<ProductLookupRow>();
        var parse = parseDate ?? ParseDate;
        var today = now.Date;
        var week = WeekStart(now);
        var month = new DateTime(now.Year, now.Month, 1);
        var acc = new Dictionary<string, Acc>(StringComparer.Ordinal);

        foreach (var o in orders)
        {
            DateTime? recv = null, ret = null;
            var datesRead = false;
            foreach (var (key, name, units) in ProductStatistics.CountedItems(o))
            {
                if (!Matches(key, terms)) continue;
                if (!datesRead)
                {
                    recv = IsReceived(o) ? parse(o.RecvDate) : null;
                    ret = parse(o.ReturnDate);
                    datesRead = true;
                }
                if (!acc.TryGetValue(key, out var a)) acc[key] = a = new Acc();
                a.Add(name, units, o.Id, recv, ret, today, week, month);
            }
        }

        return acc.Values.Select(a => a.ToRow())
            .OrderByDescending(r => r.Received.Month).ThenByDescending(r => r.Returned.Month)
            .ThenByDescending(r => r.Received.Total + r.Returned.Total).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(Math.Max(1, limit)).ToList();
    }

    private sealed class Acc
    {
        private readonly Dictionary<string, int> _spellings = new(StringComparer.Ordinal);
        private readonly HashSet<string> _orders = new(StringComparer.Ordinal);
        private int _rT, _rW, _rM, _rAll, _tT, _tW, _tM, _tAll;

        public void Add(string name, int units, string orderId, DateTime? recv, DateTime? ret, DateTime today, DateTime week, DateTime month)
        {
            _spellings[name] = (_spellings.TryGetValue(name, out var n) ? n : 0) + 1;
            _orders.Add(orderId);
            if (recv is { } r)
            {
                _rAll += units;
                if (r.Date == today) _rT += units;
                if (r.Date >= week && r.Date <= today) _rW += units;
                if (r.Date >= month && r.Date <= today) _rM += units;
            }
            _tAll += units;
            if (ret is { } t)
            {
                if (t.Date == today) _tT += units;
                if (t.Date >= week && t.Date <= today) _tW += units;
                if (t.Date >= month && t.Date <= today) _tM += units;
            }
        }

        public ProductLookupRow ToRow() => new(
            _spellings.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key,
            new PeriodUnits(_rT, _rW, _rM, _rAll), new PeriodUnits(_tT, _tW, _tM, _tAll), _orders.Count);
    }
}

/// <summary>Một sản phẩm đã từng có trong dữ liệu đơn: tên hay dùng nhất + màu / size đã gặp (nhiều nhất trước).</summary>
public sealed record CatalogProduct(string Key, string Name, int Units, IReadOnlyList<string> Colors, IReadOnlyList<string> Sizes);

/// <summary>
/// Danh mục sản phẩm dựng từ dữ liệu đơn sẵn có (chủ dự án 27/11/2026: tạo đơn ngoài sàn tận dụng dữ liệu sản phẩm).
/// Gộp tên theo khóa bỏ dấu; mỗi sản phẩm giữ các màu / size đã gặp để gợi ý khi tạo đơn nhanh.
/// </summary>
public sealed class ProductCatalog
{
    public IReadOnlyList<CatalogProduct> Products { get; }

    private ProductCatalog(IReadOnlyList<CatalogProduct> products) => Products = products;

    public static readonly ProductCatalog Empty = new(Array.Empty<CatalogProduct>());

    public static ProductCatalog Build(IEnumerable<Order> orders)
    {
        var map = new Dictionary<string, Acc>(StringComparer.Ordinal);
        foreach (var o in orders)
        {
            foreach (var item in o.Items)
            {
                var name = Clean(item.Product);
                if (name.Length == 0 || name.StartsWith("Chưa rõ sản phẩm", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Sản phẩm mới (", StringComparison.Ordinal)) continue;
                var key = LegacyText.Key(name);
                if (key.Length == 0) continue;
                if (!map.TryGetValue(key, out var a)) map[key] = a = new Acc();
                a.Add(name, Clean(item.Color), Clean(item.Size), item.Qty > 0 ? item.Qty : 1);
            }
        }
        return new ProductCatalog(map.Select(kv => kv.Value.ToProduct(kv.Key))
            .OrderByDescending(p => p.Units).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    /// <summary>Sản phẩm khớp câu tìm (mọi từ, không phân biệt dấu), nhiều đơn nhất trước; tên BẮT ĐẦU bằng câu tìm được ưu tiên.</summary>
    public IReadOnlyList<CatalogProduct> Search(string? query, int limit = 12)
    {
        var terms = ProductLookup.Terms(query);
        if (terms.Length == 0) return Products.Take(limit).ToList();
        var whole = LegacyText.Key(query);
        return Products.Where(p => ProductLookup.Matches(p.Key, terms))
            .OrderBy(p => p.Key.StartsWith(whole, StringComparison.Ordinal) ? 0 : 1)
            .ThenByDescending(p => p.Units)
            .Take(limit).ToList();
    }

    public CatalogProduct? Find(string? name)
    {
        var key = LegacyText.Key(name);
        return key.Length == 0 ? null : Products.FirstOrDefault(p => p.Key == key);
    }

    private static string Clean(string? s) => string.Join(' ', (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed class Acc
    {
        private readonly Dictionary<string, int> _names = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _colors = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _sizes = new(StringComparer.OrdinalIgnoreCase);
        private int _units;

        public void Add(string name, string color, string size, int units)
        {
            _names[name] = (_names.TryGetValue(name, out var n) ? n : 0) + 1;
            if (color.Length > 0) _colors[color] = (_colors.TryGetValue(color, out var c) ? c : 0) + units;
            if (size.Length > 0) _sizes[size] = (_sizes.TryGetValue(size, out var z) ? z : 0) + units;
            _units += units;
        }

        private static IReadOnlyList<string> Top(Dictionary<string, int> d) =>
            d.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.CurrentCultureIgnoreCase).Select(kv => kv.Key).Take(30).ToList();

        public CatalogProduct ToProduct(string key) => new(key,
            _names.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key, _units, Top(_colors), Top(_sizes));
    }
}
