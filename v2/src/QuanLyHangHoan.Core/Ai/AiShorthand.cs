using System.Globalization;
using System.Text;

namespace QuanLyHangHoan.Core.Ai;

/// <summary>Một nút hỏi nhanh: chữ tắt gửi đi + ý nghĩa hiện trên nút.</summary>
public sealed record AiQuickAsk(string Short, string Meaning)
{
    public string Label => $"{Short} — {Meaning}";
}

/// <summary>
/// Hiểu câu hỏi TẮT cho trợ lý AI (chủ dự án 26/11/2026: "hỏi tóm tắt nó cũng hiểu"). Chạy ngay trên máy, không cần mạng:
/// "đỏ sp" -> "Liệt kê các kiện Shopee đang chờ bị báo đỏ ...". Chỉ đổi khi MỌI chữ đều nhận ra được (không đoán);
/// câu đầy đủ hoặc có chữ lạ thì trả null để gửi nguyên văn cho AI (AI vẫn có <see cref="Glossary"/> để tự hiểu chữ tắt).
/// Bản Swift (ScannerCore/AIShorthand.swift) phải ra CÙNG kết quả với cùng bộ ca thử.
/// </summary>
public static class AiShorthand
{
    private enum K { Platform, Time, Red, Yellow, Wait, Recv, Cancel, Top, Compare, Carrier, PlatformGroup, Overview, Find, Count, List, Money, Filler }

    private static readonly Dictionary<string, (K Kind, string Value)> Phrases = Build();

    private static Dictionary<string, (K, string)> Build()
    {
        var d = new Dictionary<string, (K, string)>(StringComparer.Ordinal);
        void Add(K k, string v, params string[] keys) { foreach (var key in keys) d[key] = (k, v); }
        Add(K.Platform, "Shopee", "sp", "shopee", "spe");
        Add(K.Platform, "TikTok", "tt", "tts", "tiktok", "tik");
        Add(K.Platform, "Lazada", "lz", "laz", "lazada");
        Add(K.Time, "hôm nay", "hn", "hnay", "hom nay", "nay");
        Add(K.Time, "hôm qua", "hq", "hom qua");
        Add(K.Time, "tuần này", "tuan", "tuan nay", "tn");
        Add(K.Time, "tuần trước", "tuan truoc");
        Add(K.Time, "tháng này", "thang", "thang nay");
        Add(K.Time, "tháng trước", "thang truoc");
        Add(K.Time, "7 ngày qua", "7n", "7 ngay");
        Add(K.Red, "", "do", "bao do", "qua han", "qh");
        Add(K.Yellow, "", "vang", "bao vang");
        Add(K.Wait, "", "cho", "cv", "chua ve", "dang cho", "chua tra", "chua nhan");
        Add(K.Recv, "", "ve", "da ve", "nhan", "da nhan", "dn", "ve kho");
        Add(K.Cancel, "", "huy", "don huy");
        Add(K.Top, "", "top", "nhieu nhat");
        Add(K.Compare, "", "so", "so sanh");
        Add(K.Carrier, "", "hang", "dvvc", "hang vc", "van chuyen");
        Add(K.PlatformGroup, "", "san");
        Add(K.Overview, "", "tong", "tong quan", "tq");
        Add(K.Find, "", "tim", "tra", "tim don");
        Add(K.Count, "", "bn", "bao nhieu", "dem", "sl", "so luong");
        Add(K.List, "", "ds", "liet ke", "list", "danh sach");
        Add(K.Money, "", "tien", "gt", "gia tri", "tong tien");
        Add(K.Filler, "", "don", "kien", "cac", "nhung", "cua", "nao");
        return d;
    }

    /// <summary>Bảng chữ tắt đưa vào lời dặn của AI (để AI tự hiểu cả câu tắt mà máy không đổi được).</summary>
    public const string Glossary =
        "CHỮ TẮT người dùng hay gõ: sp=Shopee, tt/tts=TikTok, lz=Lazada; hn=hôm nay, hq=hôm qua, tuần/tháng = tuần này/tháng này; " +
        "đỏ/quá hạn/qh = báo đỏ (chờ >24 giờ), vàng = báo vàng (>12 giờ); chờ/cv = đang chờ (chưa về), về/nhận/dn = đã nhận về kho; hủy = đơn hủy; " +
        "bn/đếm/sl = đếm số lượng, ds/liệt kê = liệt kê, tiền/gt = tổng giá trị; top = sản phẩm hoàn nhiều nhất; so = so sánh với kỳ trước; " +
        "hãng/dvvc = đơn vị vận chuyển, sàn = theo sàn; tổng/tq = tổng quan; tìm X hoặc một mã = tìm đơn theo mã. Câu tắt thì hiểu theo bảng này và làm luôn.";

    /// <summary>Các nút hỏi nhanh (gửi chữ tắt; máy tự đổi thành câu đầy đủ).</summary>
    public static readonly IReadOnlyList<AiQuickAsk> QuickAsks = new[]
    {
        new AiQuickAsk("tổng", "tổng quan hiện tại"),
        new AiQuickAsk("đỏ", "kiện báo đỏ"),
        new AiQuickAsk("vàng", "kiện báo vàng"),
        new AiQuickAsk("về hn", "đã nhận hôm nay"),
        new AiQuickAsk("chờ sp", "Shopee chưa về"),
        new AiQuickAsk("chờ tt", "TikTok chưa về"),
        new AiQuickAsk("bn chờ tiền", "số kiện chờ + tiền"),
        new AiQuickAsk("top tuần", "SP hoàn nhiều nhất"),
        new AiQuickAsk("so tuần", "so với tuần trước"),
        new AiQuickAsk("hãng tuần", "theo đơn vị vận chuyển"),
    };

    /// <summary>Gợi ý ngắn dưới ô nhập.</summary>
    public const string InputHint = "Gõ tắt được: đỏ · vàng · về hn · chờ sp · bn chờ tiền · top tuần · so tuần · hãng · tìm 5678 · tổng";

    public static string Fold(string s)
    {
        var n = s.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(n.Length);
        foreach (var ch in n) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool LooksLikeCode(string raw) =>
        raw.Length >= 4 && raw.Any(char.IsDigit) && raw.All(char.IsLetterOrDigit);

    /// <summary>Câu tắt -> câu đầy đủ; null = để nguyên (câu đầy đủ, có chữ lạ, hoặc không rõ ý).</summary>
    public static string? Expand(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var raw = input.Trim().TrimEnd('?', '.', '!').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (raw.Length == 0 || raw.Length > 6) return null;
        var folded = raw.Select(Fold).ToArray();

        var parts = new List<(K Kind, string Value)>();
        string? code = null;
        for (var i = 0; i < folded.Length;)
        {
            var matched = false;
            for (var len = Math.Min(3, folded.Length - i); len >= 1 && !matched; len--)
            {
                var key = string.Join(' ', folded, i, len);
                if (Phrases.TryGetValue(key, out var p)) { parts.Add(p); i += len; matched = true; }
            }
            if (matched) continue;
            if (code is null && LooksLikeCode(raw[i])) { code = raw[i]; i++; continue; }
            return null;   // chữ lạ: không đoán
        }

        string? platform = parts.Where(p => p.Kind == K.Platform).Select(p => p.Value).LastOrDefault();
        string? time = parts.Where(p => p.Kind == K.Time).Select(p => p.Value).LastOrDefault();
        bool Has(K k) => parts.Any(p => p.Kind == k);
        var mains = new[] { K.Red, K.Yellow, K.Wait, K.Recv, K.Cancel, K.Top, K.Compare, K.Carrier, K.PlatformGroup, K.Overview, K.Find }.Where(Has).ToList();

        if (code is not null)
            return mains.Count == 0 || (mains.Count == 1 && mains[0] == K.Find) ? $"Tìm đơn có mã đơn hoặc mã vận đơn chứa {code}." : null;
        if (mains.Count != 1) return null;

        var onP = platform is null ? "" : " trên " + platform;
        var spaceTime = time is null ? "" : " " + time;
        switch (mains[0])
        {
            case K.Find: return null;   // "tìm" mà không có mã
            case K.Overview:
                return "Tổng quan hiện tại: bao nhiêu kiện đang chờ, đã nhận hôm nay, báo đỏ, báo vàng và tổng giá trị đang chờ.";
            case K.Top:
                return $"Sản phẩm nào hoàn nhiều nhất {time ?? "tuần này"}{onP}?";
            case K.Compare:
                var month = time is "tháng này" or "tháng trước";
                return $"So sánh {(month ? "tháng này với tháng trước" : "tuần này với tuần trước")}{onP}: số kiện hoàn và giá trị.";
            case K.Carrier:
                return $"Đơn vị vận chuyển nào có nhiều kiện hoàn nhất{spaceTime}{onP}?";
            case K.PlatformGroup:
                return $"Sàn nào có nhiều kiện hoàn nhất{spaceTime}?";
        }

        var p1 = platform is null ? "" : " " + platform;
        string subject = mains[0] switch
        {
            K.Red => $"kiện{p1} đang chờ bị báo đỏ (chờ quá 24 giờ kể từ lúc hàng về)",
            K.Yellow => $"kiện{p1} đang chờ bị báo vàng (chờ quá 12 giờ kể từ lúc hàng về)",
            K.Wait => $"kiện{p1} đang chờ (chưa về)" + (time is null ? "" : ", ngày hoàn " + time),
            K.Recv => $"kiện{p1} đã nhận về kho{spaceTime}",
            _ => $"đơn hủy{p1}{spaceTime}",
        };
        bool count = Has(K.Count), list = Has(K.List), money = Has(K.Money);
        if (count && !list) return $"Có bao nhiêu {subject}?" + (money ? " Tổng giá trị bao nhiêu?" : "");
        var alert = mains[0] is K.Red or K.Yellow;
        var verb = list || alert ? "Liệt kê" : "Đếm và liệt kê";
        return $"{verb} các {subject}." + (money ? " Kèm tổng giá trị." : "");
    }
}
