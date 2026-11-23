import Foundation

/// Dữ liệu mẫu GIẢ (không chứa bất kỳ mã đơn, mã vận đơn hay tên khách thật nào)
/// dùng cho chế độ "Xem thử với dữ liệu mẫu" trên iPhone khi chưa kết nối máy tính.
public enum DemoData {
    /// Tạo ~60 dòng OrderRow giả bao phủ đủ mọi tình huống thực tế cùng bảng `stats` đối chiếu khớp 100%.
    public static func snapshot(now: Date = Date(), calendar: Calendar = .current) -> SnapshotPayload {
        let nowMs = Int(now.timeIntervalSince1970 * 1000)
        let hourMs = 3_600_000
        let dayMs = 86_400_000
        let rows = buildRows(now: now, nowMs: nowMs, hourMs: hourMs, dayMs: dayMs, calendar: calendar)
        let stats = computeStats(rows: rows, nowMs: nowMs)
        return SnapshotPayload(
            time: nowMs,
            count: rows.count,
            rows: rows,
            stats: stats,
            sid: "demo_snapshot_v2"
        )
    }

    /// Vài mục lịch sử quét giả rải qua Hôm nay, Hôm qua và 2 ngày trước.
    public static func scanItems(now: Date = Date(), calendar: Calendar = .current) -> [ScanItem] {
        let nowMs = Int(now.timeIntervalSince1970 * 1000)
        let minMs = 60_000
        let dayMs = 86_400_000

        func make(
            id: String,
            code: String,
            kind: ScanKind,
            offsetMs: Int,
            status: ScanStatus,
            confirmed: Bool,
            detail: String,
            orderNum: String? = nil
        ) -> ScanItem {
            var it = ScanItem(requestId: id, code: code, kind: kind, time: nowMs + offsetMs, photoFile: nil)
            it.status = status
            it.scanConfirmed = confirmed
            it.detail = detail
            it.orderNum = orderNum
            it.attempts = 1
            return it
        }

        return [
            make(id: "demo-scan-01", code: "DEMO-SPX-1021", kind: .returnScan, offsetMs: -12 * minMs, status: .success, confirmed: true, detail: "PC đã xác nhận nhận hoàn kho.", orderNum: "DHM-2021"),
            make(id: "demo-scan-02", code: "DEMO-TT-1022", kind: .returnScan, offsetMs: -35 * minMs, status: .success, confirmed: true, detail: "PC đã xác nhận nhận hoàn kho.", orderNum: "DHM-2022"),
            make(id: "demo-scan-03", code: "DEMO-SPX-1021", kind: .returnScan, offsetMs: -50 * minMs, status: .duplicate, confirmed: true, detail: "Mã này đã quét trước đó.", orderNum: "DHM-2021"),
            make(id: "demo-scan-04", code: "DEMO-LZ-9904", kind: .returnScan, offsetMs: -95 * minMs, status: .notFound, confirmed: true, detail: "Chưa thấy mã này trong danh sách đơn hoàn."),
            make(id: "demo-scan-05", code: "DEMO-TT-1025", kind: .returnScan, offsetMs: -140 * minMs, status: .pending, confirmed: false, detail: "Đang chờ máy tính xác nhận (mẫu)."),
            make(id: "demo-scan-06", code: "DEMO-NEW-3001", kind: .newOrder, offsetMs: -45 * minMs, status: .success, confirmed: true, detail: "Đã tạo đơn mới ở Bảng chính.", orderNum: "DHM-3001"),
            make(id: "demo-scan-07", code: "DEMO-SPX-1031", kind: .returnScan, offsetMs: -dayMs - 20 * minMs, status: .success, confirmed: true, detail: "PC đã xác nhận nhận hoàn kho.", orderNum: "DHM-2031"),
            make(id: "demo-scan-08", code: "DEMO-LZ-1032", kind: .returnScan, offsetMs: -dayMs - 80 * minMs, status: .success, confirmed: true, detail: "PC đã xác nhận nhận hoàn kho.", orderNum: "DHM-2032"),
            make(id: "demo-scan-09", code: "DEMO-NEW-3002", kind: .newOrder, offsetMs: -dayMs - 110 * minMs, status: .success, confirmed: true, detail: "Đã tạo đơn mới ở Bảng chính.", orderNum: "DHM-3002"),
            make(id: "demo-scan-10", code: "DEMO-TT-1041", kind: .returnScan, offsetMs: -2 * dayMs - 30 * minMs, status: .success, confirmed: true, detail: "PC đã xác nhận nhận hoàn kho.", orderNum: "DHM-2041")
        ]
    }

    // MARK: - Nội bộ dựng 60 dòng OrderRow giả

    private static func formatDate(_ date: Date, withTime: Bool, calendar: Calendar) -> String {
        let comps = calendar.dateComponents([.day, .month, .year, .hour, .minute], from: date)
        let d = comps.day ?? 1
        let m = comps.month ?? 1
        let y = comps.year ?? 2026
        if withTime {
            let h = comps.hour ?? 9
            let min = comps.minute ?? 0
            return String(format: "%02d/%02d/%04d %02d:%02d", d, m, y, h, min)
        }
        return String(format: "%02d/%02d/%04d", d, m, y)
    }

    private static func dateOffset(_ days: Int, hour: Int = 10, minute: Int = 15, from now: Date, calendar: Calendar) -> Date {
        let start = calendar.startOfDay(for: now)
        let shifted = calendar.date(byAdding: .day, value: days, to: start) ?? start
        return calendar.date(byAdding: .minute, value: hour * 60 + minute, to: shifted) ?? shifted
    }

    private static func buildRows(now: Date, nowMs: Int, hourMs: Int, dayMs: Int, calendar: Calendar) -> [OrderRow] {
        var rows: [OrderRow] = []
        rows.reserveCapacity(60)

        let products = [
            "Áo thun cotton basic",
            "Quần jean ống rộng",
            "Áo sơ mi linen tay dài",
            "Váy midi xếp ly",
            "Áo khoác gió 2 lớp",
            "Quần short kaki nam",
            "Áo polo dệt kim",
            "Chân váy chữ A",
            "Túi tote vải canvas",
            "Mũ lưỡi trai thêu chữ"
        ]
        let colors = ["Đen", "Trắng", "Be", "Xanh navy", "Xám", "Nâu"]
        let sizes = ["S", "M", "L", "XL", "Freesize"]

        func prod(_ idx: Int) -> String { products[idx % products.count] }
        func col(_ idx: Int) -> String { colors[idx % colors.count] }
        func sz(_ idx: Int) -> String { sizes[idx % sizes.count] }

        // 1. 7 đơn QUÁ HẠN (Báo Đỏ, lv = 2) trên bảng chính (Shopee / TikTok / Lazada)
        let overdueSpecs: [(String, String, String, Int, Bool, String, Bool, String, Int)] = [
            ("DEMO-SPX-1001", "DHM-1001", "Shopee", 28, true, "Kiểm tra hàng rách tem niêm phong", false, "", 3),
            ("DEMO-TT-1002", "DHM-1002", "TikTok", 19, true, "Khách báo thiếu dây đeo", true, "Áo thun cotton basic", 3),
            ("DEMO-SPX-1003", "DHM-1003", "Shopee", 11, false, "", false, "", 3),
            ("DEMO-LZ-1004", "DHM-1004", "Lazada", 7, false, "", false, "", 2),
            ("DEMO-TT-1005", "DHM-1005", "TikTok", 5, true, "Đơn giá trị cao, quay video khi mở", false, "", 3),
            ("DEMO-SPX-1006", "DHM-1006", "Shopee", 3, false, "", true, "Quần short kaki nam", 2),
            ("DEMO-LZ-1007", "DHM-1007", "Lazada", 2, false, "", false, "", 0)
        ]
        for (i, s) in overdueSpecs.enumerated() {
            let name = s.6 ? "\(s.7) | \(prod(i + 2)) | \(prod(i + 4))" : prod(i)
            var r = OrderRow(
                k: "m", c: s.0, o: s.1, p: s.2, n: name,
                co: col(i), sz: sz(i), q: s.6 ? 3 : 1,
                rd: formatDate(dateOffset(-4, hour: 9 + i, from: now, calendar: calendar), withTime: false, calendar: calendar),
                due: nowMs - s.3 * hourMs,
                st: "Chưa trả"
            )
            r.ty = i % 2 == 0 ? "Trả hàng/Hoàn tiền" : "Giao không thành công"
            r.pd = true; r.rv = false; r.lv = 2
            r.wl = s.4; r.wr = s.5
            r.rn = s.7
            if s.8 > 0 {
                r.rw = nowMs - 4 * dayMs
                r.rws = s.8
                if s.8 == 3 { r.ar = nowMs - s.3 * hourMs }
            }
            if i == 2 { r.ps = "Huỷ sau chuẩn bị · Đã huỷ" }
            rows.append(r)
        }

        // 2. 8 đơn SẮP ĐẾN HẠN (Báo Vàng, lv = 1) trên bảng chính
        let soonSpecs: [(String, String, String, Int, Bool, String, String, Int)] = [
            ("DEMO-SPX-1008", "DHM-1008", "Shopee", 2, true, "Bưu cục báo giao chiều nay", "", 3),
            ("DEMO-TT-1009", "DHM-1009", "TikTok", 4, false, "", "Váy midi xếp ly", 2),
            ("DEMO-LZ-1010", "DHM-1010", "Lazada", 6, false, "", "", 2),
            ("DEMO-SPX-1011", "DHM-1011", "Shopee", 9, true, "Đối chiếu màu thực tế", "", 3),
            ("DEMO-TT-1012", "DHM-1012", "TikTok", 12, false, "", "", 2),
            ("DEMO-SPX-1013", "DHM-1013", "Shopee", 15, false, "", "Áo polo dệt kim", 1),
            ("DEMO-LZ-1014", "DHM-1014", "Lazada", 18, false, "", "", 1),
            ("DEMO-TT-1015", "DHM-1015", "TikTok", 21, false, "", "", 2)
        ]
        for (i, s) in soonSpecs.enumerated() {
            let isPartial = !s.6.isEmpty
            let name = isPartial ? "\(s.6) | \(prod(i + 3))" : prod(i + 1)
            var r = OrderRow(
                k: "m", c: s.0, o: s.1, p: s.2, n: name,
                co: col(i + 2), sz: sz(i + 1), q: isPartial ? 2 : 1,
                rd: formatDate(dateOffset(-2, hour: 10 + i, from: now, calendar: calendar), withTime: false, calendar: calendar),
                due: nowMs + s.3 * hourMs,
                st: "Chưa trả"
            )
            r.ty = "Trả hàng/Hoàn tiền"
            r.pd = true; r.rv = false; r.lv = 1
            r.wl = s.4; r.wr = s.5
            r.rn = s.6
            r.rw = nowMs - 2 * dayMs
            r.rws = s.7
            if s.7 == 3 { r.ar = nowMs - 6 * hourMs }
            if i == 0 || i == 4 { r.ps = "Huỷ sau chuẩn bị · Đã huỷ" }
            if i == 3 { r.pre = true }
            rows.append(r)
        }

        // 3. 11 đơn ĐANG CHỜ bình thường (lv = 0) — gồm đủ chặng 1, 2 của Hàng hoàn về + hoàn một phần + đặt trước
        let normalPlatforms = ["Shopee", "TikTok", "Lazada", "Shopee", "TikTok", "Shopee", "Lazada", "TikTok", "Shopee", "TikTok", "Lazada"]
        for i in 0..<11 {
            let num = 1016 + i
            let plat = normalPlatforms[i]
            let prefix = plat == "Shopee" ? "DEMO-SPX" : (plat == "TikTok" ? "DEMO-TT" : "DEMO-LZ")
            let isPartial = (i == 1 || i == 6)
            let p1 = prod(i + 3)
            let name = isPartial ? "\(p1) | \(prod(i + 5)) | \(prod(i + 7))" : p1
            var r = OrderRow(
                k: "m", c: "\(prefix)-\(num)", o: "DHM-\(num)", p: plat, n: name,
                co: col(i), sz: sz(i), q: isPartial ? 3 : 1,
                rd: formatDate(dateOffset(-1, hour: 8 + (i % 8), from: now, calendar: calendar), withTime: false, calendar: calendar),
                due: nowMs + (36 + i * 6) * hourMs,
                st: "Chưa trả"
            )
            r.ty = i % 3 == 0 ? "Giao không thành công" : "Trả hàng/Hoàn tiền"
            r.pd = true; r.rv = false; r.lv = 0
            r.rn = isPartial ? p1 : ""
            r.pre = (i == 2 || i == 8)
            if i == 4 {
                r.wl = true
                r.wr = "Khách đổi ý sau khi nhận"
            }
            if i < 4 {
                r.rw = nowMs - dayMs
                r.rws = 1
            } else if i < 8 {
                r.rw = nowMs - 2 * dayMs
                r.rws = 2
            }
            if i == 5 { r.ps = "Huỷ sau chuẩn bị · Đã huỷ" }
            rows.append(r)
        }

        // 4. 20 đơn ĐÃ NHẬN KHO (rv = true, pd = false) rải đều 7 ngày gần nhất (hôm nay có 6 đơn)
        // Phân bổ theo ngày offset: 0 (6 đơn), -1 (4), -2 (3), -3 (2), -4 (2), -5 (1), -6 (2) = 20 đơn
        let recvDayOffsets = [0, 0, 0, 0, 0, 0, -1, -1, -1, -1, -2, -2, -2, -3, -3, -4, -4, -5, -6, -6]
        let recvPlatforms = ["Shopee", "TikTok", "Lazada"]
        for (i, dayOff) in recvDayOffsets.enumerated() {
            let num = 2001 + i
            let plat = recvPlatforms[i % recvPlatforms.count]
            let prefix = plat == "Shopee" ? "DEMO-SPX" : (plat == "TikTok" ? "DEMO-TT" : "DEMO-LZ")
            let recvDate = dateOffset(dayOff, hour: 8 + (i % 9), minute: (i * 7) % 60, from: now, calendar: calendar)
            let isPartial = (i == 0 || i == 7)
            let p1 = prod(i)
            let name = isPartial ? "\(p1) | \(prod(i + 2))" : p1
            var r = OrderRow(
                k: "m", c: "\(prefix)-\(num)", o: "DHM-\(num)", p: plat, n: name,
                co: col(i + 1), sz: sz(i + 2), q: isPartial ? 2 : 1,
                rd: formatDate(dateOffset(dayOff - 2, hour: 10, from: now, calendar: calendar), withTime: false, calendar: calendar),
                due: 0,
                st: "Hàng đã trả"
            )
            r.ty = "Trả hàng/Hoàn tiền"
            r.pd = false; r.rv = true; r.lv = 0
            r.rcd = formatDate(recvDate, withTime: true, calendar: calendar)
            r.rn = isPartial ? p1 : ""
            if i < 6 {
                // Chặng 4 (Đã nhận kho) trong Bảng Hàng Hoàn Về
                r.rw = nowMs - (3 + i % 3) * dayMs
                r.rws = 4
                r.ar = nowMs - (1 + i % 2) * dayMs
            }
            rows.append(r)
        }

        // 5. 8 đơn HỦY (k == "c"): 5 chưa nhận, 3 đã nhận
        for i in 0..<8 {
            let num = 3001 + i
            let plat = i % 2 == 0 ? "Shopee" : "TikTok"
            let received = i >= 5
            var r = OrderRow(
                k: "c", c: "DEMO-CXL-\(num)", o: "DHM-\(num)", p: plat, n: prod(i + 4),
                co: col(i), sz: sz(i), q: 1,
                rd: formatDate(dateOffset(-(i % 4), hour: 11, from: now, calendar: calendar), withTime: false, calendar: calendar),
                due: 0,
                st: received ? "Đã nhận" : "Chưa nhận"
            )
            r.ty = "Đơn hủy"
            r.ci = "demo-cxl-id-\(num)"
            r.pd = !received; r.rv = received; r.lv = 0
            if received {
                r.rcd = formatDate(dateOffset(-1, hour: 14 + i, from: now, calendar: calendar), withTime: true, calendar: calendar)
            }
            rows.append(r)
        }

        // 6. 6 đơn ĐÃ LƯU TRỮ (k == "a", đã nhận trước đó)
        for i in 0..<6 {
            let num = 4001 + i
            let plat = recvPlatforms[i % recvPlatforms.count]
            var r = OrderRow(
                k: "a", c: "DEMO-ARC-\(num)", o: "DHM-\(num)", p: plat, n: prod(i + 2),
                co: col(i + 3), sz: sz(i), q: 1,
                rd: formatDate(dateOffset(-12 - i, hour: 9, from: now, calendar: calendar), withTime: false, calendar: calendar),
                due: 0,
                st: "Hàng đã trả"
            )
            r.ty = "Trả hàng/Hoàn tiền"
            r.pd = false; r.rv = true; r.lv = 0
            r.rcd = formatDate(dateOffset(-9 - i, hour: 15, from: now, calendar: calendar), withTime: true, calendar: calendar)
            rows.append(r)
        }

        return rows
    }

    private static func computeStats(rows: [OrderRow], nowMs: Int) -> [String: Int] {
        func count(_ pred: (OrderRow) -> Bool) -> Int { rows.filter(pred).count }
        return [
            "sent": rows.count,
            "dropped": 0,
            "m_total": count { $0.k == "m" },
            "m_recv": count { $0.k == "m" && $0.rv },
            "m_pend": count { $0.k == "m" && $0.pd },
            "m_watch": count { $0.k == "m" && $0.wl },
            "m_pre": count { $0.k == "m" && $0.pre },
            "m_cancelType": count { $0.k == "m" && $0.ty == "Đơn hủy" },
            "a_total": count { $0.k == "a" },
            "a_recv": count { $0.k == "a" && $0.rv },
            "c_total": count { $0.k == "c" },
            "c_recv": count { $0.k == "c" && $0.rv },
            "m_danger": count { $0.k == "m" && $0.pd && $0.alertLevel(now: nowMs) == .overdue },
            "m_warn": count { $0.k == "m" && $0.pd && $0.alertLevel(now: nowMs) == .soon }
        ]
    }
}
