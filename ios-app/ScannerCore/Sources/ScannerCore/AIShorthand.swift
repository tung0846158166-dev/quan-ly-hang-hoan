import Foundation

/// Một nút hỏi nhanh: chữ tắt gửi đi + ý nghĩa hiện trên nút.
public struct AIQuickAsk: Hashable, Sendable {
    public let short: String
    public let meaning: String
}

/// Hiểu câu hỏi TẮT cho trợ lý AI (chủ dự án 26/11/2026: "hỏi tóm tắt nó cũng hiểu"). Chạy ngay trên máy, không cần mạng.
/// Chỉ đổi khi MỌI chữ đều nhận ra được (không đoán); câu đầy đủ / có chữ lạ thì trả nil để gửi nguyên văn (AI vẫn có `glossary`).
/// CÙNG quy tắc và cùng bộ ca thử với bản C# (v2/src/QuanLyHangHoan.Core/Ai/AiShorthand.cs).
public enum AIShorthand {
    private enum K { case platform, time, red, yellow, wait, recv, cancel, top, compare, carrier, platformGroup, overview, find, count, list, money, filler }

    private static let phrases: [String: (K, String)] = {
        var d: [String: (K, String)] = [:]
        func add(_ k: K, _ v: String, _ keys: String...) { for key in keys { d[key] = (k, v) } }
        add(.platform, "Shopee", "sp", "shopee", "spe")
        add(.platform, "TikTok", "tt", "tts", "tiktok", "tik")
        add(.platform, "Lazada", "lz", "laz", "lazada")
        add(.time, "hôm nay", "hn", "hnay", "hom nay", "nay")
        add(.time, "hôm qua", "hq", "hom qua")
        add(.time, "tuần này", "tuan", "tuan nay", "tn")
        add(.time, "tuần trước", "tuan truoc")
        add(.time, "tháng này", "thang", "thang nay")
        add(.time, "tháng trước", "thang truoc")
        add(.time, "7 ngày qua", "7n", "7 ngay")
        add(.red, "", "do", "bao do", "qua han", "qh")
        add(.yellow, "", "vang", "bao vang")
        add(.wait, "", "cho", "cv", "chua ve", "dang cho", "chua tra", "chua nhan")
        add(.recv, "", "ve", "da ve", "nhan", "da nhan", "dn", "ve kho")
        add(.cancel, "", "huy", "don huy")
        add(.top, "", "top", "nhieu nhat")
        add(.compare, "", "so", "so sanh")
        add(.carrier, "", "hang", "dvvc", "hang vc", "van chuyen")
        add(.platformGroup, "", "san")
        add(.overview, "", "tong", "tong quan", "tq")
        add(.find, "", "tim", "tra", "tim don")
        add(.count, "", "bn", "bao nhieu", "dem", "sl", "so luong")
        add(.list, "", "ds", "liet ke", "list", "danh sach")
        add(.money, "", "tien", "gt", "gia tri", "tong tien")
        add(.filler, "", "don", "kien", "cac", "nhung", "cua", "nao")
        return d
    }()

    /// Bảng chữ tắt đưa vào lời dặn của AI.
    public static let glossary =
        "CHỮ TẮT người dùng hay gõ: sp=Shopee, tt/tts=TikTok, lz=Lazada; hn=hôm nay, hq=hôm qua, tuần/tháng = tuần này/tháng này; " +
        "đỏ/quá hạn/qh = báo đỏ (chờ >24 giờ), vàng = báo vàng (>12 giờ); chờ/cv = đang chờ (chưa về), về/nhận/dn = đã nhận về kho; hủy = đơn hủy; " +
        "bn/đếm/sl = đếm số lượng, ds/liệt kê = liệt kê, tiền/gt = tổng giá trị; top = sản phẩm hoàn nhiều nhất; so = so sánh với kỳ trước; " +
        "hãng/dvvc = đơn vị vận chuyển, sàn = theo sàn; tổng/tq = tổng quan; tìm X hoặc một mã = tìm đơn theo mã. Câu tắt thì hiểu theo bảng này và làm luôn."

    public static let quickAsks: [AIQuickAsk] = [
        AIQuickAsk(short: "tổng", meaning: "tổng quan hiện tại"),
        AIQuickAsk(short: "đỏ", meaning: "kiện báo đỏ"),
        AIQuickAsk(short: "vàng", meaning: "kiện báo vàng"),
        AIQuickAsk(short: "về hn", meaning: "đã nhận hôm nay"),
        AIQuickAsk(short: "chờ sp", meaning: "Shopee chưa về"),
        AIQuickAsk(short: "chờ tt", meaning: "TikTok chưa về"),
        AIQuickAsk(short: "bn chờ tiền", meaning: "số kiện chờ + tiền"),
        AIQuickAsk(short: "top tuần", meaning: "SP hoàn nhiều nhất"),
        AIQuickAsk(short: "so tuần", meaning: "so với tuần trước"),
        AIQuickAsk(short: "hãng tuần", meaning: "theo đơn vị vận chuyển"),
    ]

    public static let inputHint = "Gõ tắt được: đỏ · vàng · về hn · chờ sp · bn chờ tiền · top tuần · so tuần · hãng · tìm 5678 · tổng"

    public static func fold(_ s: String) -> String {
        s.lowercased().replacingOccurrences(of: "đ", with: "d")
            .folding(options: [.diacriticInsensitive], locale: Locale(identifier: "vi_VN"))
    }

    private static func looksLikeCode(_ raw: String) -> Bool {
        raw.count >= 4 && raw.contains(where: { $0.isNumber }) && raw.allSatisfy({ $0.isLetter || $0.isNumber })
    }

    /// Câu tắt -> câu đầy đủ; nil = để nguyên.
    public static func expand(_ input: String?) -> String? {
        guard var t = input?.trimmingCharacters(in: .whitespacesAndNewlines), !t.isEmpty else { return nil }
        while let last = t.last, "?.!".contains(last) { t.removeLast() }
        let raw = t.split(whereSeparator: { $0.isWhitespace }).map(String.init)
        if raw.isEmpty || raw.count > 6 { return nil }
        let folded = raw.map(fold)

        var parts: [(K, String)] = []
        var code: String? = nil
        var i = 0
        while i < folded.count {
            var matched = false
            var len = min(3, folded.count - i)
            while len >= 1 && !matched {
                let key = folded[i..<(i + len)].joined(separator: " ")
                if let p = phrases[key] { parts.append(p); i += len; matched = true }
                len -= 1
            }
            if matched { continue }
            if code == nil && looksLikeCode(raw[i]) { code = raw[i]; i += 1; continue }
            return nil
        }

        let platform = parts.last(where: { $0.0 == .platform })?.1
        let time = parts.last(where: { $0.0 == .time })?.1
        func has(_ k: K) -> Bool { parts.contains(where: { $0.0 == k }) }
        let mains = [K.red, .yellow, .wait, .recv, .cancel, .top, .compare, .carrier, .platformGroup, .overview, .find].filter(has)

        if let code {
            return mains.isEmpty || mains == [.find] ? "Tìm đơn có mã đơn hoặc mã vận đơn chứa \(code)." : nil
        }
        guard mains.count == 1 else { return nil }

        let onP = platform.map { " trên " + $0 } ?? ""
        let spaceTime = time.map { " " + $0 } ?? ""
        switch mains[0] {
        case .find: return nil
        case .overview:
            return "Tổng quan hiện tại: bao nhiêu kiện đang chờ, đã nhận hôm nay, báo đỏ, báo vàng và tổng giá trị đang chờ."
        case .top:
            return "Sản phẩm nào hoàn nhiều nhất \(time ?? "tuần này")\(onP)?"
        case .compare:
            let month = time == "tháng này" || time == "tháng trước"
            return "So sánh \(month ? "tháng này với tháng trước" : "tuần này với tuần trước")\(onP): số kiện hoàn và giá trị."
        case .carrier:
            return "Đơn vị vận chuyển nào có nhiều kiện hoàn nhất\(spaceTime)\(onP)?"
        case .platformGroup:
            return "Sàn nào có nhiều kiện hoàn nhất\(spaceTime)?"
        default: break
        }

        let p1 = platform.map { " " + $0 } ?? ""
        let subject: String
        switch mains[0] {
        case .red: subject = "kiện\(p1) đang chờ bị báo đỏ (chờ quá 24 giờ kể từ lúc hàng về)"
        case .yellow: subject = "kiện\(p1) đang chờ bị báo vàng (chờ quá 12 giờ kể từ lúc hàng về)"
        case .wait: subject = "kiện\(p1) đang chờ (chưa về)" + (time.map { ", ngày hoàn " + $0 } ?? "")
        case .recv: subject = "kiện\(p1) đã nhận về kho\(spaceTime)"
        default: subject = "đơn hủy\(p1)\(spaceTime)"
        }
        let count = has(.count), list = has(.list), money = has(.money)
        if count && !list { return "Có bao nhiêu \(subject)?" + (money ? " Tổng giá trị bao nhiêu?" : "") }
        let alert = mains[0] == .red || mains[0] == .yellow
        let verb = list || alert ? "Liệt kê" : "Đếm và liệt kê"
        return "\(verb) các \(subject)." + (money ? " Kèm tổng giá trị." : "")
    }
}
