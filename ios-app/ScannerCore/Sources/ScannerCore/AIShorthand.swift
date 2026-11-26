import Foundation

/// Một nút hỏi nhanh: chữ tắt gửi đi + ý nghĩa hiện trên nút.
public struct AIQuickAsk: Hashable, Sendable {
    public let short: String
    public let meaning: String
}

/// Hiểu câu hỏi TẮT cho trợ lý AI (chủ dự án 26/11/2026: "hỏi tóm tắt nó cũng hiểu"). Chạy ngay trên máy, không cần mạng.
/// Chỉ đổi khi MỌI chữ đều nhận ra được (không đoán); có chữ lạ thì trả nil để gửi nguyên văn cho AI.
/// KHÔNG đưa bảng chữ tắt vào lời dặn AI (mỗi lần hỏi giới hạn 4.500 token; dài thêm thì AI bị cắt bớt công cụ).
/// CÙNG quy tắc và cùng bộ ca thử với bản C# (v2/src/QuanLyHangHoan.Core/Ai/AiShorthand.cs).
public enum AIShorthand {
    private enum K {
        case platform, time, carrierName
        case red, yellow, wait, recv, cancel, lost, damaged, notNeeded, watch, archived, failedDelivery, new
        case top, compare, carrier, platformGroup, overview, find, dup, activity
        case count, list, money, filler
    }

    private static let phrases: [String: (K, String)] = {
        var d: [String: (K, String)] = [:]
        func add(_ k: K, _ v: String, _ keys: String...) { for key in keys { d[key] = (k, v) } }
        // sàn
        add(.platform, "Shopee", "sp", "shopee", "spe", "shp", "shoppe", "shope", "s")
        add(.platform, "TikTok", "tt", "tts", "tiktok", "tik", "tik tok", "tiktok shop", "tok", "tiktokshop")
        add(.platform, "Lazada", "lz", "laz", "lazada", "lzd", "laza")
        // thời gian
        add(.time, "hôm nay", "hn", "hnay", "hom nay", "nay", "homnay", "today", "trong ngay")
        add(.time, "hôm qua", "hq", "hom qua", "homqua", "hqua", "qua hom")
        add(.time, "hôm kia", "hk", "hom kia", "homkia")
        add(.time, "tuần này", "tuan", "tuan nay", "tn", "week")
        add(.time, "tuần trước", "tuan truoc", "tuan qua")
        add(.time, "tháng này", "thang", "thang nay", "thg", "thg nay", "month")
        add(.time, "tháng trước", "thang truoc", "thg truoc", "thang qua")
        add(.time, "3 ngày qua", "3n", "3 ngay")
        add(.time, "7 ngày qua", "7n", "7 ngay")
        add(.time, "30 ngày qua", "30n", "30 ngay")
        add(.time, "năm nay", "nam", "nam nay")
        // đơn vị vận chuyển (lọc)
        add(.carrierName, "SPX Express", "spx", "spx express", "shopee express")
        add(.carrierName, "J&T Express", "jt", "j&t", "jnt", "j t", "jt express", "j&t express")
        add(.carrierName, "GHN", "ghn", "giao hang nhanh")
        add(.carrierName, "GHTK", "ghtk", "giao hang tiet kiem")
        add(.carrierName, "Viettel Post", "vtp", "viettel", "viettel post", "vt post")
        add(.carrierName, "BEST Express", "best", "best express", "bes")
        add(.carrierName, "Ninja Van", "ninja", "njv", "ninja van")
        add(.carrierName, "LEX", "lex", "lex vn", "lazada express")
        add(.carrierName, "VNPost", "vnpost", "vnp", "buu dien")
        // trạng thái / loại
        add(.red, "", "do", "bao do", "qua han", "qh", "tre", "tre han", "cham", "gap", "khan", "24h", "canh bao do", "lau")
        add(.yellow, "", "vang", "bao vang", "12h", "sap han", "sap qua han", "canh bao vang")
        add(.wait, "", "cho", "cv", "chua ve", "dang cho", "chua tra", "chua nhan", "dang ve", "chua toi", "pending", "ton", "con cho", "cho ve")
        add(.recv, "", "ve", "da ve", "nhan", "da nhan", "dn", "ve kho", "nhap kho", "da tra", "tra roi", "xong", "nhan roi", "ve roi")
        add(.cancel, "", "huy", "don huy", "bi huy", "cancel")
        add(.lost, "", "mat", "bi mat", "that lac", "mat hang", "lost")
        add(.damaged, "", "hong", "hu", "hu hong", "bi hong", "loi", "vo")
        add(.notNeeded, "", "kcn", "khong can nhan", "ko can nhan", "k can nhan", "bo qua")
        add(.watch, "", "theo doi", "td", "watch", "dang theo doi", "can chu y")
        add(.archived, "", "luu tru", "kho luu tru", "lt", "da luu tru", "archive")
        add(.failedDelivery, "", "gtb", "giao that bai", "that bai", "giao khong thanh cong", "gkt", "giao loi")
        add(.new, "", "moi", "don moi", "moi nap", "vua nap", "nap moi", "moi ve")
        // thống kê / tra cứu
        add(.top, "", "top", "nhieu nhat", "hoan nhieu", "hay hoan", "top hoan", "top san pham", "san pham hoan", "san pham")
        add(.compare, "", "so", "so sanh", "vs", "tang giam", "so voi")
        add(.carrier, "", "hang", "dvvc", "hang vc", "van chuyen", "nha vc", "don vi vc", "don vi van chuyen", "ship", "shipper")
        add(.platformGroup, "", "san", "theo san", "cac san")
        add(.overview, "", "tong", "tong quan", "tq", "bc", "bao cao", "tinh hinh", "the nao", "sao roi", "tom tat")
        add(.find, "", "tim", "tra", "tim don", "tra cuu", "check", "kiem", "tim ma")
        add(.dup, "", "trung", "trung lap", "bi trung", "dup")
        add(.activity, "", "nhat ky", "log", "hoat dong", "ai lam", "ai sua")
        // cách hỏi
        add(.count, "", "bn", "bao nhieu", "dem", "sl", "so luong", "may", "co may", "tong so", "bnhieu")
        add(.list, "", "ds", "liet ke", "list", "danh sach", "xem", "cho xem", "show", "hien")
        add(.money, "", "tien", "gt", "gia tri", "tong tien", "doanh thu", "bao nhieu tien", "vnd", "dong")
        // chữ đệm
        add(.filler, "", "don", "kien", "cac", "nhung", "cua", "nao", "co", "la", "di", "nhe", "nha", "a", "ah", "oi", "giup",
            "cho toi", "cho minh", "minh", "voi", "duoc", "khong", "ko", "k", "hang hoan", "hoan", "dang", "hien tai", "bay gio", "luc nay")
        return d
    }()

    public static let quickAsks: [AIQuickAsk] = [
        AIQuickAsk(short: "tổng", meaning: "tổng quan hiện tại"),
        AIQuickAsk(short: "đỏ", meaning: "kiện báo đỏ"),
        AIQuickAsk(short: "vàng", meaning: "kiện báo vàng"),
        AIQuickAsk(short: "về hn", meaning: "đã nhận hôm nay"),
        AIQuickAsk(short: "chờ sp", meaning: "Shopee chưa về"),
        AIQuickAsk(short: "chờ tt", meaning: "TikTok chưa về"),
        AIQuickAsk(short: "bn chờ tiền", meaning: "số kiện chờ + tiền"),
        AIQuickAsk(short: "mới hn", meaning: "đơn mới nạp hôm nay"),
        AIQuickAsk(short: "top tuần", meaning: "SP hoàn nhiều nhất"),
        AIQuickAsk(short: "so tuần", meaning: "so với tuần trước"),
        AIQuickAsk(short: "hãng tuần", meaning: "theo đơn vị vận chuyển"),
        AIQuickAsk(short: "mất", meaning: "kiện bị mất"),
        AIQuickAsk(short: "td", meaning: "đang theo dõi"),
        AIQuickAsk(short: "nhật ký hn", meaning: "hoạt động hôm nay"),
    ]

    public static let inputHint = "Gõ tắt được: đỏ · vàng · về hn · chờ sp · bn chờ tiền · mới hn · top tuần · so tuần · hãng · chờ spx · mất · hỏng · td · lt · gtb · trùng · nhật ký · tìm 5678 · tổng"

    public static func fold(_ s: String) -> String {
        s.lowercased().replacingOccurrences(of: "đ", with: "d")
            .folding(options: [.diacriticInsensitive], locale: Locale(identifier: "vi_VN"))
    }

    private static func looksLikeCode(_ raw: String) -> Bool {
        raw.count >= 4 && raw.contains(where: { $0.isNumber }) && raw.allSatisfy({ $0.isLetter || $0.isNumber })
    }

    private static let mainKinds: [K] = [.red, .yellow, .wait, .recv, .cancel, .lost, .damaged, .notNeeded, .watch, .archived, .failedDelivery, .new,
                                         .top, .compare, .carrier, .platformGroup, .overview, .find, .dup, .activity]

    /// Câu tắt -> câu đầy đủ; nil = để nguyên.
    public static func expand(_ input: String?) -> String? {
        guard var t = input?.replacingOccurrences(of: ",", with: " ").replacingOccurrences(of: ";", with: " ")
            .trimmingCharacters(in: .whitespacesAndNewlines), !t.isEmpty else { return nil }
        while let last = t.last, "?.!".contains(last) { t.removeLast() }
        let raw = t.split(whereSeparator: { $0.isWhitespace }).map(String.init)
        if raw.isEmpty || raw.count > 8 { return nil }
        let folded = raw.map(fold)

        var parts: [(K, String)] = []
        var code: String? = nil
        var i = 0
        while i < folded.count {
            var matched = false
            var len = min(4, folded.count - i)
            while len >= 1 && !matched {
                let key = folded[i..<(i + len)].joined(separator: " ")
                if let p = phrases[key] { parts.append(p); i += len; matched = true }
                len -= 1
            }
            if matched { continue }
            if code == nil && looksLikeCode(raw[i]) { code = raw[i]; i += 1; continue }
            return nil   // chữ lạ: không đoán
        }

        let platform = parts.last(where: { $0.0 == .platform })?.1
        let time = parts.last(where: { $0.0 == .time })?.1
        let carrierName = parts.last(where: { $0.0 == .carrierName })?.1
        func has(_ k: K) -> Bool { parts.contains(where: { $0.0 == k }) }
        var mains = mainKinds.filter(has)

        if let code {
            return mains.isEmpty || mains == [.find] ? "Tìm đơn có mã đơn hoặc mã vận đơn chứa \(code)." : nil
        }
        // chỉ có hãng vận chuyển (vd. "spx", "bn spx"): hiểu là kiện đang chờ của hãng đó
        if mains.isEmpty && carrierName != nil { mains.append(.wait) }
        guard mains.count == 1 else { return nil }
        let main = mains[0]

        let onP = platform.map { " trên " + $0 } ?? ""
        let spaceTime = time.map { " " + $0 } ?? ""
        let statsLike: [K] = [.top, .compare, .carrier, .platformGroup, .overview, .find, .dup, .activity]
        if statsLike.contains(main) && carrierName != nil && main != .top && main != .compare { return nil }
        let viaC = carrierName.map { " qua " + $0 } ?? ""
        switch main {
        case .find: return nil   // "tìm" mà không có mã
        case .overview:
            return "Tổng quan hiện tại\(onP): bao nhiêu kiện đang chờ, đã nhận hôm nay, báo đỏ, báo vàng và tổng giá trị đang chờ."
        case .top:
            return "Sản phẩm nào hoàn nhiều nhất \(time ?? "tuần này")\(onP)\(viaC)?"
        case .compare:
            let month = time == "tháng này" || time == "tháng trước"
            return "So sánh \(month ? "tháng này với tháng trước" : "tuần này với tuần trước")\(onP)\(viaC): số kiện hoàn và giá trị."
        case .carrier:
            return "Đơn vị vận chuyển nào có nhiều kiện hoàn nhất\(spaceTime)\(onP)?"
        case .platformGroup:
            return "Sàn nào có nhiều kiện hoàn nhất\(spaceTime)?"
        case .dup:
            return "Có đơn nào bị trùng lặp\(onP) không? Liệt kê nếu có."
        case .activity:
            return "Có những hoạt động gì \(time ?? "hôm nay") (nhật ký)?"
        default: break
        }

        let p1 = (platform.map { " " + $0 } ?? "") + (carrierName.map { " của " + $0 } ?? "")
        let subject: String
        switch main {
        case .red: subject = "kiện\(p1) đang chờ bị báo đỏ (chờ quá 24 giờ kể từ lúc hàng về)"
        case .yellow: subject = "kiện\(p1) đang chờ bị báo vàng (chờ quá 12 giờ kể từ lúc hàng về)"
        case .wait: subject = "kiện\(p1) đang chờ (chưa về)" + (time.map { ", ngày hoàn " + $0 } ?? "")
        case .recv: subject = "kiện\(p1) đã nhận về kho\(spaceTime)"
        case .lost: subject = "kiện\(p1) bị mất\(spaceTime)"
        case .damaged: subject = "kiện\(p1) bị hư hỏng\(spaceTime)"
        case .notNeeded: subject = "kiện\(p1) không cần nhận\(spaceTime)"
        case .watch: subject = "kiện\(p1) đang theo dõi"
        case .archived: subject = "kiện\(p1) trong Kho lưu trữ\(spaceTime)"
        case .failedDelivery: subject = "kiện\(p1) giao không thành công\(spaceTime)"
        case .new: subject = "kiện\(p1) mới nạp \(time ?? "hôm nay")"
        default: subject = "đơn hủy\(p1)\(spaceTime)"
        }
        let count = has(.count), list = has(.list), money = has(.money)
        if count && !list { return "Có bao nhiêu \(subject)?" + (money ? " Tổng giá trị bao nhiêu?" : "") }
        let alert = main == .red || main == .yellow
        let verb = list || alert ? "Liệt kê" : "Đếm và liệt kê"
        return "\(verb) các \(subject)." + (money ? " Kèm tổng giá trị." : "")
    }
}
