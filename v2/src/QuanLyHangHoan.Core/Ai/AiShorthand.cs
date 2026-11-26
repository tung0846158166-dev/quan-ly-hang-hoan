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
/// câu có chữ lạ thì trả null để gửi nguyên văn cho AI. KHÔNG đưa bảng chữ tắt vào lời dặn AI: mỗi lần hỏi giới hạn 4.500 token,
/// lời dặn dài thêm thì AI bị cắt bớt công cụ, trả lời kém đi.
/// Bản Swift (ScannerCore/AIShorthand.swift) phải ra CÙNG kết quả với cùng bộ ca thử.
/// </summary>
public static class AiShorthand
{
    private enum K
    {
        Platform, Time, CarrierName,
        Red, Yellow, Wait, Recv, Cancel, Lost, Damaged, NotNeeded, Watch, Archived, FailedDelivery, New,
        Top, Compare, Carrier, PlatformGroup, Overview, Find, Dup, Activity,
        Count, List, Money, Filler,
    }

    private static readonly Dictionary<string, (K Kind, string Value)> Phrases = Build();

    private static Dictionary<string, (K, string)> Build()
    {
        var d = new Dictionary<string, (K, string)>(StringComparer.Ordinal);
        void Add(K k, string v, params string[] keys) { foreach (var key in keys) d[key] = (k, v); }
        // sàn
        Add(K.Platform, "Shopee", "sp", "shopee", "spe", "shp", "shoppe", "shope", "s");
        Add(K.Platform, "TikTok", "tt", "tts", "tiktok", "tik", "tik tok", "tiktok shop", "tok", "tiktokshop");
        Add(K.Platform, "Lazada", "lz", "laz", "lazada", "lzd", "laza");
        // thời gian
        Add(K.Time, "hôm nay", "hn", "hnay", "hom nay", "nay", "homnay", "today", "trong ngay");
        Add(K.Time, "hôm qua", "hq", "hom qua", "homqua", "hqua", "qua hom");
        Add(K.Time, "hôm kia", "hk", "hom kia", "homkia");
        Add(K.Time, "tuần này", "tuan", "tuan nay", "tn", "week");
        Add(K.Time, "tuần trước", "tuan truoc", "tuan qua");
        Add(K.Time, "tháng này", "thang", "thang nay", "thg", "thg nay", "month");
        Add(K.Time, "tháng trước", "thang truoc", "thg truoc", "thang qua");
        Add(K.Time, "3 ngày qua", "3n", "3 ngay");
        Add(K.Time, "7 ngày qua", "7n", "7 ngay");
        Add(K.Time, "30 ngày qua", "30n", "30 ngay");
        Add(K.Time, "năm nay", "nam", "nam nay");
        // đơn vị vận chuyển (lọc)
        Add(K.CarrierName, "SPX Express", "spx", "spx express", "shopee express");
        Add(K.CarrierName, "J&T Express", "jt", "j&t", "jnt", "j t", "jt express", "j&t express");
        Add(K.CarrierName, "GHN", "ghn", "giao hang nhanh");
        Add(K.CarrierName, "GHTK", "ghtk", "giao hang tiet kiem");
        Add(K.CarrierName, "Viettel Post", "vtp", "viettel", "viettel post", "vt post");
        Add(K.CarrierName, "BEST Express", "best", "best express", "bes");
        Add(K.CarrierName, "Ninja Van", "ninja", "njv", "ninja van");
        Add(K.CarrierName, "LEX", "lex", "lex vn", "lazada express");
        Add(K.CarrierName, "VNPost", "vnpost", "vnp", "buu dien");
        // trạng thái / loại
        Add(K.Red, "", "do", "bao do", "qua han", "qh", "tre", "tre han", "cham", "gap", "khan", "24h", "canh bao do", "lau");
        Add(K.Yellow, "", "vang", "bao vang", "12h", "sap han", "sap qua han", "canh bao vang");
        Add(K.Wait, "", "cho", "cv", "chua ve", "dang cho", "chua tra", "chua nhan", "dang ve", "chua toi", "pending", "ton", "con cho", "cho ve");
        Add(K.Recv, "", "ve", "da ve", "nhan", "da nhan", "dn", "ve kho", "nhap kho", "da tra", "tra roi", "xong", "nhan roi", "ve roi");
        Add(K.Cancel, "", "huy", "don huy", "bi huy", "cancel");
        Add(K.Lost, "", "mat", "bi mat", "that lac", "mat hang", "lost");
        Add(K.Damaged, "", "hong", "hu", "hu hong", "bi hong", "loi", "vo");
        Add(K.NotNeeded, "", "kcn", "khong can nhan", "ko can nhan", "k can nhan", "bo qua");
        Add(K.Watch, "", "theo doi", "td", "watch", "dang theo doi", "can chu y");
        Add(K.Archived, "", "luu tru", "kho luu tru", "lt", "da luu tru", "archive");
        Add(K.FailedDelivery, "", "gtb", "giao that bai", "that bai", "giao khong thanh cong", "gkt", "giao loi");
        Add(K.New, "", "moi", "don moi", "moi nap", "vua nap", "nap moi", "moi ve");
        // thống kê / tra cứu
        Add(K.Top, "", "top", "nhieu nhat", "hoan nhieu", "hay hoan", "top hoan", "top san pham", "san pham hoan", "san pham");
        Add(K.Compare, "", "so", "so sanh", "vs", "tang giam", "so voi");
        Add(K.Carrier, "", "hang", "dvvc", "hang vc", "van chuyen", "nha vc", "don vi vc", "don vi van chuyen", "ship", "shipper");
        Add(K.PlatformGroup, "", "san", "theo san", "cac san");
        Add(K.Overview, "", "tong", "tong quan", "tq", "bc", "bao cao", "tinh hinh", "the nao", "sao roi", "tom tat");
        Add(K.Find, "", "tim", "tra", "tim don", "tra cuu", "check", "kiem", "tim ma");
        Add(K.Dup, "", "trung", "trung lap", "bi trung", "dup");
        Add(K.Activity, "", "nhat ky", "log", "hoat dong", "ai lam", "ai sua");
        // cách hỏi
        Add(K.Count, "", "bn", "bao nhieu", "dem", "sl", "so luong", "may", "co may", "tong so", "bnhieu");
        Add(K.List, "", "ds", "liet ke", "list", "danh sach", "xem", "cho xem", "show", "hien");
        Add(K.Money, "", "tien", "gt", "gia tri", "tong tien", "doanh thu", "bao nhieu tien", "vnd", "dong");
        // chữ đệm
        Add(K.Filler, "", "don", "kien", "cac", "nhung", "cua", "nao", "co", "la", "di", "nhe", "nha", "a", "ah", "oi", "giup",
            "cho toi", "cho minh", "minh", "voi", "duoc", "khong", "ko", "k", "hang hoan", "hoan", "dang", "hien tai", "bay gio", "luc nay");
        return d;
    }


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
        new AiQuickAsk("mới hn", "đơn mới nạp hôm nay"),
        new AiQuickAsk("top tuần", "SP hoàn nhiều nhất"),
        new AiQuickAsk("so tuần", "so với tuần trước"),
        new AiQuickAsk("hãng tuần", "theo đơn vị vận chuyển"),
        new AiQuickAsk("mất", "kiện bị mất"),
        new AiQuickAsk("td", "đang theo dõi"),
        new AiQuickAsk("nhật ký hn", "hoạt động hôm nay"),
    };

    /// <summary>Gợi ý ngắn dưới ô nhập.</summary>
    public const string InputHint = "Gõ tắt được: đỏ · vàng · về hn · chờ sp · bn chờ tiền · mới hn · top tuần · so tuần · hãng · chờ spx · mất · hỏng · td · lt · gtb · trùng · nhật ký · tìm 5678 · tổng";

    public static string Fold(string s)
    {
        var n = s.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(n.Length);
        foreach (var ch in n) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool LooksLikeCode(string raw) =>
        raw.Length >= 4 && raw.Any(char.IsDigit) && raw.All(char.IsLetterOrDigit);

    private static readonly K[] Mains =
    {
        K.Red, K.Yellow, K.Wait, K.Recv, K.Cancel, K.Lost, K.Damaged, K.NotNeeded, K.Watch, K.Archived, K.FailedDelivery, K.New,
        K.Top, K.Compare, K.Carrier, K.PlatformGroup, K.Overview, K.Find, K.Dup, K.Activity,
    };

    /// <summary>Câu tắt -> câu đầy đủ; null = để nguyên (có chữ lạ, hoặc không rõ ý).</summary>
    public static string? Expand(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var cleaned = input.Replace(',', ' ').Replace(';', ' ').Trim().TrimEnd('?', '.', '!');
        var raw = cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (raw.Length == 0 || raw.Length > 8) return null;
        var folded = raw.Select(Fold).ToArray();

        var parts = new List<(K Kind, string Value)>();
        string? code = null;
        for (var i = 0; i < folded.Length;)
        {
            var matched = false;
            for (var len = Math.Min(4, folded.Length - i); len >= 1 && !matched; len--)
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
        string? carrier = parts.Where(p => p.Kind == K.CarrierName).Select(p => p.Value).LastOrDefault();
        bool Has(K k) => parts.Any(p => p.Kind == k);
        var mains = Mains.Where(Has).ToList();

        if (code is not null)
            return mains.Count == 0 || (mains.Count == 1 && mains[0] == K.Find) ? $"Tìm đơn có mã đơn hoặc mã vận đơn chứa {code}." : null;
        // chỉ có hãng vận chuyển (vd. "spx", "bn spx"): hiểu là kiện đang chờ của hãng đó
        if (mains.Count == 0 && carrier is not null) mains.Add(K.Wait);
        if (mains.Count != 1) return null;

        var onP = platform is null ? "" : " trên " + platform;
        var spaceTime = time is null ? "" : " " + time;
        var main = mains[0];
        var statsLike = main is K.Top or K.Compare or K.Carrier or K.PlatformGroup or K.Overview or K.Find or K.Dup or K.Activity;
        if (statsLike && carrier is not null && main != K.Top && main != K.Compare) return null;
        var viaC = carrier is null ? "" : " qua " + carrier;
        switch (main)
        {
            case K.Find: return null;   // "tìm" mà không có mã
            case K.Overview:
                return $"Tổng quan hiện tại{onP}: bao nhiêu kiện đang chờ, đã nhận hôm nay, báo đỏ, báo vàng và tổng giá trị đang chờ.";
            case K.Top:
                return $"Sản phẩm nào hoàn nhiều nhất {time ?? "tuần này"}{onP}{viaC}?";
            case K.Compare:
                var month = time is "tháng này" or "tháng trước";
                return $"So sánh {(month ? "tháng này với tháng trước" : "tuần này với tuần trước")}{onP}{viaC}: số kiện hoàn và giá trị.";
            case K.Carrier:
                return $"Đơn vị vận chuyển nào có nhiều kiện hoàn nhất{spaceTime}{onP}?";
            case K.PlatformGroup:
                return $"Sàn nào có nhiều kiện hoàn nhất{spaceTime}?";
            case K.Dup:
                return $"Có đơn nào bị trùng lặp{onP} không? Liệt kê nếu có.";
            case K.Activity:
                return $"Có những hoạt động gì {time ?? "hôm nay"} (nhật ký)?";
        }

        var p1 = (platform is null ? "" : " " + platform) + (carrier is null ? "" : " của " + carrier);
        string subject = main switch
        {
            K.Red => $"kiện{p1} đang chờ bị báo đỏ (chờ quá 24 giờ kể từ lúc hàng về)",
            K.Yellow => $"kiện{p1} đang chờ bị báo vàng (chờ quá 12 giờ kể từ lúc hàng về)",
            K.Wait => $"kiện{p1} đang chờ (chưa về)" + (time is null ? "" : ", ngày hoàn " + time),
            K.Recv => $"kiện{p1} đã nhận về kho{spaceTime}",
            K.Lost => $"kiện{p1} bị mất{spaceTime}",
            K.Damaged => $"kiện{p1} bị hư hỏng{spaceTime}",
            K.NotNeeded => $"kiện{p1} không cần nhận{spaceTime}",
            K.Watch => $"kiện{p1} đang theo dõi",
            K.Archived => $"kiện{p1} trong Kho lưu trữ{spaceTime}",
            K.FailedDelivery => $"kiện{p1} giao không thành công{spaceTime}",
            K.New => $"kiện{p1} mới nạp {time ?? "hôm nay"}",
            _ => $"đơn hủy{p1}{spaceTime}",
        };
        bool count = Has(K.Count), list = Has(K.List), money = Has(K.Money);
        if (count && !list) return $"Có bao nhiêu {subject}?" + (money ? " Tổng giá trị bao nhiêu?" : "");
        var alert = main is K.Red or K.Yellow;
        var verb = list || alert ? "Liệt kê" : "Đếm và liệt kê";
        return $"{verb} các {subject}." + (money ? " Kèm tổng giá trị." : "");
    }
}
