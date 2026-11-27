using System.Text.RegularExpressions;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Core.Stats;

/// <summary>Một kiện đã nhận chứa sản phẩm này (để tìm đúng kiện ngoài kho).</summary>
public sealed record ReceivedParcel(Order Order, int Units, DateTime ReceivedAt);

/// <summary>Một dòng hàng sẵn kho: sản phẩm + màu + size, tổng số cái, các kiện chứa nó (mới nhận trước).</summary>
public sealed record ReceivedStockRow(string Key, string Product, string Color, string Size, int Units, DateTime LastReceived, IReadOnlyList<ReceivedParcel> Parcels);

/// <summary>
/// HÀNG HOÀN SẴN KHO (chủ dự án 27/11/2026): sản phẩm của các kiện hoàn ĐÃ NHẬN tại kho trong một khoảng thời gian, gom theo
/// sản phẩm + màu + size. Có đơn mới trùng sản phẩm thì lấy luôn hàng hoàn đóng đơn, khỏi cất lên kệ.
/// Đếm theo quy tắc thống kê sản phẩm: đơn hoàn một phần chỉ tính sản phẩm đã hoàn; bỏ "Chưa rõ sản phẩm".
/// </summary>
public static class ReceivedStock
{
    public static IReadOnlyList<ReceivedStockRow> Build(IEnumerable<Order> orders, DateTime since, string? query = null, Func<string?, DateTime?>? parseDate = null)
    {
        var parse = parseDate ?? ProductLookup.ParseDate;
        var terms = ProductLookup.Terms(query);
        var map = new Dictionary<(string, string, string), Acc>();
        foreach (var o in orders)
        {
            if (!ProductLookup.IsReceived(o) || o.Items.Count == 0) continue;
            if (parse(o.RecvDate) is not { } at || at < since) continue;
            var mixed = o.Items.Any(i => i.ReturnedQty > 0) && o.Items.Any(i => i.ReturnedQty <= 0);
            foreach (var item in o.Items)
            {
                if (mixed && item.ReturnedQty <= 0) continue;
                var units = mixed ? item.ReturnedQty : (item.Qty > 0 ? item.Qty : 1);
                var name = Clean(item.Product);
                if (name.Length == 0 || name.StartsWith("Chưa rõ sản phẩm", StringComparison.OrdinalIgnoreCase)) continue;
                var key = LegacyText.Key(name);
                if (key.Length == 0) continue;
                var color = Clean(item.Color);
                var size = Clean(item.Size);
                // tìm theo tên, màu hoặc size (vd: "ao thun den L")
                if (terms.Length > 0 && !terms.All((key + LegacyText.Key(color) + "|" + LegacyText.Key(size)).Contains)) continue;
                var k = (key, LegacyText.Key(color), LegacyText.Key(size));
                if (!map.TryGetValue(k, out var a)) map[k] = a = new Acc(name, color, size);
                a.Add(o, units, at);
            }
        }
        return map.Select(kv => kv.Value.ToRow(kv.Key.Item1))
            .OrderBy(r => r.Product, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Color, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Size, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string Clean(string? s) => Regex.Replace((s ?? "").Trim(), @"\s+", " ");

    private sealed class Acc(string product, string color, string size)
    {
        private readonly List<ReceivedParcel> _parcels = new();
        public void Add(Order o, int units, DateTime at)
        {
            var i = _parcels.FindIndex(p => ReferenceEquals(p.Order, o));
            if (i >= 0) _parcels[i] = _parcels[i] with { Units = _parcels[i].Units + units };
            else _parcels.Add(new ReceivedParcel(o, units, at));
        }
        public ReceivedStockRow ToRow(string key)
        {
            var list = _parcels.OrderByDescending(p => p.ReceivedAt).ToList();
            return new ReceivedStockRow(key, product, color, size, list.Sum(p => p.Units), list[0].ReceivedAt, list);
        }
    }
}
