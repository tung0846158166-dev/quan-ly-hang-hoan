import Foundation

/// Kiện trên cloud -> dòng danh sách của điện thoại (`OrderRow`). Bản Swift của `PhoneSnapshot.BuildRow` (C#) để điện thoại tự dựng
/// danh sách từ dữ liệu cloud giống hệt cách máy tính từng gửi sang: cùng quy tắc trạng thái, cảnh báo 12/24 giờ, hoàn một phần, mã phụ.
public enum CloudRowMapper {
    public static let cancelType = "Đơn hủy"

    /// Lịch Việt Nam (UTC+7) dùng cho mọi phép tính ngày giờ (giống máy tính của cửa hàng).
    public static var vietnamCalendar: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "Asia/Ho_Chi_Minh") ?? TimeZone(secondsFromGMT: 7 * 3600)!
        return c
    }

    // ---- quy tắc trạng thái (port isOrderReceived / isOrderPending)
    public static func isReceived(_ p: CloudParcel) -> Bool { p.status == "Hàng đã trả" || p.status == "Đã nhận" }
    public static func isPending(_ p: CloudParcel) -> Bool {
        !isReceived(p) && p.status != "Không cần nhận" && p.status != "Bị mất" && p.status != "Hư hỏng"
    }

    public static func kind(_ p: CloudParcel) -> String {
        p.isArchived ? "a" : (p.orderType == cancelType ? "c" : "m")
    }

    /// Bỏ khoảng trắng và ký tự vô hình, chuyển chữ hoa (giống TrackingResolver.Normalize).
    static func normalize(_ s: String) -> String {
        let drop: Set<Character> = ["\u{200B}", "\u{FEFF}"]
        return String(s.filter { !$0.isWhitespace && !drop.contains($0) }).uppercased()
    }

    private static func clip(_ s: String, _ n: Int) -> String { s.count <= n ? s : String(s.prefix(n)) }
    private static func join(_ parts: [String], _ max: Int) -> String { clip(parts.filter { !$0.isEmpty }.joined(separator: " | "), max) }
    private static func firstNonEmpty(_ v: [String]) -> String {
        for x in v { let t = x.trimmingCharacters(in: .whitespacesAndNewlines); if !t.isEmpty { return t } }
        return ""
    }

    /// Giờ hàng về shop (cột returnedAt, hoặc extra.returnSuccessTime). Salework ghi giờ trước ngày ("10:15:22 21/11/2026") nên đảo lại.
    public static func arrivedAt(_ p: CloudParcel, calendar: Calendar = vietnamCalendar) -> Date? {
        var s = p.returnedAt.trimmingCharacters(in: .whitespacesAndNewlines)
        if s.isEmpty { s = (p.extra["returnSuccessTime"] ?? "").trimmingCharacters(in: .whitespacesAndNewlines) }
        if s.isEmpty { return nil }
        if let re = try? NSRegularExpression(pattern: #"^(\d{1,2}:\d{1,2}(?::\d{1,2})?)\s+(\d{1,2}[/\-.]\d{1,2}[/\-.]\d{2,4})"#),
           let m = re.firstMatch(in: s, range: NSRange(s.startIndex..., in: s)),
           let t = Range(m.range(at: 1), in: s), let d = Range(m.range(at: 2), in: s) {
            s = String(s[d]) + " " + String(s[t])
        }
        return OrderStats.parseDate(s, calendar: calendar)
    }

    private static func ms(_ d: Date) -> Int { Int((d.timeIntervalSince1970 * 1000).rounded()) }

    /// Dòng điện thoại của một kiện; nil nếu kiện không có mã nào (không tra/quét được) hoặc đã xóa.
    public static func row(_ p: CloudParcel, now: Date, calendar: Calendar = vietnamCalendar, watch: CloudWatchChange? = nil) -> OrderRow? {
        if p.isDeleted { return nil }
        let k = kind(p)
        let pending = isPending(p)
        let arrived = arrivedAt(p, calendar: calendar)
        let c = firstNonEmpty([p.track, p.returnTrack, p.orderId])
        if c.isEmpty { return nil }
        let items = p.items

        var row = OrderRow(
            k: k, c: c, o: p.orderId.trimmingCharacters(in: .whitespacesAndNewlines),
            p: p.platform == "TikTok" ? "TikTok Shop" : p.platform,
            n: join(items.map { clip($0.product, 70) }, 160),
            co: clip(items.map { $0.color }.joined(separator: " | "), 60),
            sz: clip(items.map { $0.size }.joined(separator: " | "), 40),
            q: items.isEmpty ? 1 : items.reduce(0) { $0 + ($1.qty > 0 ? $1.qty : 1) },
            rd: p.returnDate, due: 0, st: k == "c" ? cancelType : p.status)

        // hoàn một phần: tên các sản phẩm ĐÃ HOÀN, chỉ khi có cả hoàn lẫn không hoàn
        let returned = items.filter { $0.returnedQty > 0 }.map { clip($0.product, 70) }.filter { !$0.isEmpty }
        if !returned.isEmpty && returned.count < items.count { row.rn = join(returned, 160) }

        var lv = 0
        if pending, let a = arrived {
            row.due = ms(a.addingTimeInterval(24 * 3600))
            let el = now.timeIntervalSince(a)
            lv = el >= 24 * 3600 ? 2 : (el >= 12 * 3600 ? 1 : 0)
        }
        row.lv = lv

        if !p.orderType.isEmpty { row.ty = p.orderType }
        if !p.recvDate.isEmpty { row.rcd = clip(p.recvDate, 24) }
        if !p.platformStatus.isEmpty { row.ps = clip(p.platformStatus, 60) }
        if p.price > 0 { row.pr = Int(p.price.rounded()) }
        if !p.carrier.isEmpty { row.cr = clip(p.carrier, 30) }

        // các mã KHÁC của chính kiện (không gồm mã giao đi dùng chung)
        var own: Set<String> = [normalize(c), normalize(p.orderId)]
        var extra: [String] = []
        for raw in [p.returnTrack, p.altTrack, p.returnOrderId] + p.trackingCodes + p.scanAliases {
            let v = normalize(raw)
            if !v.isEmpty && own.insert(v).inserted { extra.append(v) }
        }
        if !extra.isEmpty { row.x = extra.prefix(6).joined(separator: " ") }

        // Bảng Hàng Hoàn Về: chỉ đơn bảng chính còn trong bảng (chưa bị bỏ), không phải "Không cần nhận"/"Bị mất"
        if k == "m", let w = watch, !w.off, w.since > 0, p.status != "Không cần nhận", p.status != "Bị mất" {
            row.rw = w.since
            row.rws = stage(p, calendar: calendar)
            if let a = arrived { row.ar = ms(a) }
        }

        if p.preOrder == "Hàng đặt trước" { row.pre = true }
        if !pending { row.pd = false }
        if isReceived(p) { row.rv = true }
        if p.isWatchlist {
            row.wl = true
            if !p.watchlistReason.isEmpty { row.wr = clip(p.watchlistReason, 200) }
        }
        let nt = join([p.note, p.userNote].map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }, 200)
        if !nt.isEmpty { row.nt = nt }
        if k == "c" { row.ci = p.id }
        return row
    }

    /// Chặng trong Bảng Hàng Hoàn Về (port ReturnWatchService.StageOf): đã nhận 4; có giờ hàng về 3; "đang về"/có mã vận đơn hoàn 2; còn lại 1.
    public static func stage(_ p: CloudParcel, calendar: Calendar = vietnamCalendar) -> Int {
        if isReceived(p) { return 4 }
        if arrivedAt(p, calendar: calendar) != nil { return 3 }
        if fold(p.platformStatus).range(of: #"dang ve|dang van chuyen|van chuyen ve|dang giao"#, options: .regularExpression) != nil
            || !normalize(p.returnTrack).isEmpty { return 2 }
        return 1
    }

    /// Bỏ dấu tiếng Việt, đ -> d, chữ thường.
    static func fold(_ s: String) -> String {
        s.replacingOccurrences(of: "đ", with: "d").replacingOccurrences(of: "Đ", with: "D")
            .folding(options: [.diacriticInsensitive, .caseInsensitive], locale: nil).lowercased()
    }

    /// Mọi dòng của điện thoại: đơn đang chờ lên trước, hạn gần trước; ổn định theo thứ tự đầu vào.
    public static func rows(_ parcels: [CloudParcel], now: Date, calendar: Calendar = vietnamCalendar, watch: [String: CloudWatchChange] = [:]) -> [OrderRow] {
        let built: [(row: OrderRow, index: Int)] = parcels.enumerated().compactMap { (i, p) in
            guard let r = row(p, now: now, calendar: calendar, watch: watch[p.id]) else { return nil }
            return (r, i)
        }
        let sorted = built.sorted { a, b in
            let pa = a.row.pd ? 0 : 1, pb = b.row.pd ? 0 : 1
            if pa != pb { return pa < pb }
            let da = a.row.due == 0 ? Int.max : a.row.due, db = b.row.due == 0 ? Int.max : b.row.due
            if da != db { return da < db }
            return a.index < b.index
        }
        return sorted.map { $0.row }
    }
}
