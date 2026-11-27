using System.Text;
using System.Text.RegularExpressions;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Core.Stats;

/// <summary>
/// Gộp các kiểu tên của CÙNG một mẫu (chủ dự án 28/11/2026: bảng Hàng hoàn sẵn kho hiện 2 tên cho 1 sản phẩm). Dữ liệu thật:
/// Shopee lúc ghi tên ngắn là mã mẫu ("STRIPES"), lúc ghi tên dài "Áo Polo LEN THỎ Local Brand Lavi Studio/ STRIPES Nam Nữ Unisex";
/// TikTok thêm tiền tố "[ DEAL MỞ BÁN ]", "[ Lavi Studio x ... ]", hoặc chữ kiểu đặc biệt "𝐋𝐚𝐯𝐢 𝐒𝐭𝐮𝐝𝐢𝐨".
/// Khóa mẫu = LOẠI sản phẩm (2 chữ đầu: "Áo Polo", "Áo Hoodie", "Quần Kaki") + MÃ mẫu (chữ viết hoa sau "/"), vì cùng một mã có thể là hai
/// sản phẩm khác loại (áo sweater và áo hoodie cùng tên mẫu). Tên ngắn chỉ có mã: gộp vào đúng loại nếu mã đó chỉ thuộc MỘT loại
/// (<see cref="ProductFamilies"/>). Không nhận ra mã thì khóa tên đầy đủ (bỏ dấu, bỏ tiền tố [...]). Bỏ đuôi Premium trước khi gọi.
/// </summary>
public static class ProductNames
{
    private static readonly Regex Brackets = new(@"\[[^\]]*\]", RegexOptions.CultureInvariant);
    private static readonly Regex AfterSlash = new(@"/\s*([^\s/\-–—]+)", RegexOptions.CultureInvariant);

    /// <summary>Chữ kiểu đặc biệt -> chữ thường (NFKC), bỏ tiền tố [...], gộp khoảng trắng.</summary>
    public static string Tidy(string? name)
    {
        var s = (name ?? "").Normalize(NormalizationForm.FormKC);
        s = Brackets.Replace(s, " ");
        return string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Mã mẫu ("STRIPES") nếu nhận ra, không thì "": chữ VIẾT HOA TOÀN BỘ ngay sau dấu "/" đầu tiên ("... Lavi Studio/ STRIPES Nam Nữ",
    /// "... Năng Động/ ALTIC"), hoặc cả tên chỉ là một chữ viết hoa ("STRIPES"). Chữ thường sau "/" ("Logo Thêu", "I Love Pet") không phải mã:
    /// dùng tên đầy đủ, tránh gộp nhầm hai sản phẩm khác nhau.
    /// </summary>
    public static string ModelCode(string? name)
    {
        var t = Tidy(name);
        var slash = t.IndexOf('/');
        if (slash >= 0)
        {
            var m = AfterSlash.Match(t, slash);
            return m.Success && IsCode(m.Groups[1].Value) ? m.Groups[1].Value : "";
        }
        return !t.Contains(' ') && IsCode(t) ? t : "";
    }

    private static bool IsCode(string w) =>
        w.Count(char.IsLetter) >= 3 && w.All(char.IsLetterOrDigit) && w.Where(char.IsLetter).All(char.IsUpper);

    /// <summary>Chữ đầu tiên sau dấu "/" (mọi kiểu hoa/thường), hoặc "".</summary>
    public static string LooseCode(string? name)
    {
        var t = Tidy(name);
        var slash = t.IndexOf('/');
        if (slash < 0) return "";
        var m = AfterSlash.Match(t, slash);
        return m.Success && m.Groups[1].Value.Count(char.IsLetter) >= 3 && m.Groups[1].Value.All(char.IsLetterOrDigit) ? m.Groups[1].Value : "";
    }

    /// <summary>Tên ngắn chỉ là mã ("STRIPES")?</summary>
    public static bool IsCodeOnly(string? name) { var t = Tidy(name); return !t.Contains(' ') && !t.Contains('/') && IsCode(t); }

    /// <summary>Loại sản phẩm: 2 chữ đầu (bỏ dấu) — "aopolo", "aohoodie", "quankaki".</summary>
    public static string TypeKey(string? name) =>
        string.Concat(Tidy(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(LegacyText.Key));

    /// <summary>Khóa gộp mẫu khi không có ngữ cảnh: "m:mã:loại" (tên dài), "m:mã" (tên ngắn), hoặc khóa tên đầy đủ.</summary>
    public static string FamilyKey(string? name)
    {
        var code = LegacyText.Key(ModelCode(name));
        if (code.Length == 0) return LegacyText.Key(Tidy(name));
        return IsCodeOnly(name) ? "m:" + code : "m:" + code + ":" + TypeKey(name);
    }

    /// <summary>Chọn tên hiển thị: ưu tiên tên đầy đủ (nhiều chữ) hay gặp nhất, đã bỏ tiền tố [...].</summary>
    public static string PickDisplay(IEnumerable<KeyValuePair<string, int>> spellings)
    {
        var list = spellings.Select(kv => (Name: Tidy(kv.Key), kv.Value)).Where(x => x.Name.Length > 0)
            .GroupBy(x => x.Name).Select(g => (Name: g.Key, Count: g.Sum(x => x.Value))).ToList();
        if (list.Count == 0) return "";
        return list.OrderBy(x => x.Name.Contains(' ') ? 0 : 1).ThenByDescending(x => x.Count).ThenBy(x => x.Name, StringComparer.Ordinal).First().Name;
    }
}

/// <summary>
/// Gộp mẫu có ngữ cảnh (dựng từ toàn bộ đơn): tên ngắn "STRIPES" vào đúng mẫu dài nếu mã chỉ thuộc một loại; tên hiển thị chung cho mỗi mẫu
/// (tên đầy đủ hay gặp nhất); chữ để tìm gồm mọi kiểu tên đã gặp (gõ "ao polo len" vẫn ra đơn chỉ ghi "STRIPES").
/// </summary>
public sealed class ProductFamilies
{
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);                         // mọi khóa "m:code:type" đã gặp
    private readonly Dictionary<string, string> _codeToKey = new(StringComparer.Ordinal);       // "m:code" -> "m:code:type" khi chỉ một loại
    private readonly Dictionary<string, Dictionary<string, int>> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _search = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);

    public static readonly ProductFamilies Empty = new();

    public static ProductFamilies Build(IEnumerable<Order> orders)
    {
        var f = new ProductFamilies();
        var all = new List<string>();
        var codeTypes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var o in orders)
            foreach (var it in o.Items)
            {
                var n = Clean(it.Product);
                if (n.Length == 0) continue;
                all.Add(n);
                var fk = ProductNames.FamilyKey(n);
                if (fk.StartsWith("m:", StringComparison.Ordinal) && fk.IndexOf(':', 2) is var c and > 0)
                {
                    var codeKey = fk[..c];
                    if (!codeTypes.TryGetValue(codeKey, out var set)) codeTypes[codeKey] = set = new HashSet<string>(StringComparer.Ordinal);
                    set.Add(fk);
                }
            }
        foreach (var (codeKey, keys) in codeTypes)
        {
            f._known.UnionWith(keys);
            if (keys.Count == 1) f._codeToKey[codeKey] = keys.First();
        }
        foreach (var n in all)
        {
            var k = f.Key(n);
            if (!f._names.TryGetValue(k, out var d)) f._names[k] = d = new Dictionary<string, int>(StringComparer.Ordinal);
            var shown = ProductStatistics.StripPremium(n);
            d[shown] = (d.TryGetValue(shown, out var x) ? x : 0) + 1;
            if (!f._search.TryGetValue(k, out var s)) f._search[k] = s = new HashSet<string>(StringComparer.Ordinal);
            s.Add(LegacyText.Key(ProductNames.Tidy(shown)));
        }
        return f;
    }

    /// <summary>Khóa mẫu của một tên (đã/không bỏ Premium đều được).</summary>
    public string Key(string? name)
    {
        var n = ProductStatistics.StripPremium(Clean(name));
        if (_cache.TryGetValue(n, out var hit)) return hit;
        var fk = ProductNames.FamilyKey(n);
        if (fk.StartsWith("m:", StringComparison.Ordinal) && fk.IndexOf(':', 2) < 0 && _codeToKey.TryGetValue(fk, out var full)) fk = full;
        else if (!fk.StartsWith("m:", StringComparison.Ordinal) && ProductNames.LooseCode(n) is { Length: > 0 } loose)
        {
            // "Lavi Studio/ Plain" (TikTok) cùng mẫu với "Lavi Studio/ PLAIN" (Shopee): gộp khi đã có mẫu viết hoa đúng mã + đúng loại
            var cand = "m:" + LegacyText.Key(loose) + ":" + ProductNames.TypeKey(n);
            if (_known.Contains(cand)) fk = cand;
        }
        _cache[n] = fk;
        return fk;
    }

    /// <summary>Tên hiển thị chung của mẫu; không biết thì "".</summary>
    public string Display(string key) => _names.TryGetValue(key, out var d) ? ProductNames.PickDisplay(d) : "";

    /// <summary>Chữ để tìm của mẫu (mọi kiểu tên, bỏ dấu, nối bằng "|").</summary>
    public string SearchText(string key) => _search.TryGetValue(key, out var s) ? string.Join("|", s) + "|" + key : key;

    private static string Clean(string? s) => string.Join(' ', (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
