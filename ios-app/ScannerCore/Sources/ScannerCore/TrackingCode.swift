import Foundation

/// Bản port của extractCleanTrackingCode trong mobile_scanner.html (giữ tương thích 100%, mở rộng QR JSON / nhiều dòng / ký tự ẩn).
public enum TrackingCode {
    private static func compile(_ pattern: String, ignoreCase: Bool = true) -> NSRegularExpression? {
        try? NSRegularExpression(pattern: pattern, options: ignoreCase ? [.caseInsensitive] : [])
    }

    // Biên dịch regex 1 lần duy nhất thay vì tạo mới mỗi khung hình camera.
    private static let reHttp = compile("^https?://")
    private static let reShortUrl = compile("^(?:www\\.)?(?:spx|ghn)\\.vn/")
    private static let reQueryKey = compile("^(?:spx|track|tracking|tracking_number|tracking_no|tracking_code|waybill|waybill_no|code|c|id|bill|order_code|s)$")
    private static let reUrlToken = compile("^[A-Za-z0-9._-]{6,50}$")
    private static let reSpxvn = compile("(SPXVN[0-9A-Za-z]{6,18})")
    private static let reSoc = compile("(SOC[0-9A-Za-z]{6,16})")
    private static let reNivn = compile("((?:NIVN|NLVN|SHPVN)[0-9A-Za-z]{6,16})")
    private static let reVtp = compile("(VTP?[0-9A-Za-z]{6,15})")
    private static let reGhn = compile("(GHN[0-9A-Za-z]{6,14}|G8[0-9A-Za-z]{6,14})")
    private static let reGhtk = compile("(S\\d+[\\.\\-][0-9A-Za-z\\.\\-]{6,24})")
    private static let reJt = compile("(?<![0-9])(8[0246][0-9]{10,13})(?![0-9])", ignoreCase: false)
    private static let rePlausible = compile("^[A-Z0-9][A-Z0-9._\\-]*$", ignoreCase: false)

    private static let reCarrierSpx = compile("^SPXVN|^SPX")
    private static let reCarrierVt = compile("^VN|^VTP")
    private static let reCarrierGhn = compile("^GHN")
    private static let reCarrierJt = compile("^8[0245]\\d{10,12}|^862|^863|^587|^JNT|^JT")
    private static let reCarrierNinja = compile("^NINJA")

    private static let jsonTrackingKeys: [String] = [
        "tracking_number", "tracking_no", "tracking_code", "tracking",
        "waybill_no", "waybill", "spx", "order_code", "code", "bill", "id", "c"
    ]

    private static func firstMatch(_ re: NSRegularExpression?, in s: String) -> String? {
        guard let re = re else { return nil }
        let ns = s as NSString
        guard let m = re.firstMatch(in: s, range: NSRange(location: 0, length: ns.length)), m.numberOfRanges > 1 else { return nil }
        let r = m.range(at: 1)
        if r.location == NSNotFound { return nil }
        return ns.substring(with: r)
    }

    private static func matches(_ re: NSRegularExpression?, _ s: String) -> Bool {
        guard let re = re else { return false }
        return re.firstMatch(in: s, range: NSRange(location: 0, length: (s as NSString).length)) != nil
    }

    /// Loại bỏ ký tự vô hình / điều khiển (BOM, zero-width space, NUL...) mà máy quét hoặc QR hay gắn kèm, giữ \r \n \t.
    private static func stripInvisible(_ s: String) -> String {
        var scalars = String.UnicodeScalarView()
        scalars.reserveCapacity(s.unicodeScalars.count)
        for u in s.unicodeScalars {
            let v = u.value
            if v == 0xFEFF || (v >= 0x200B && v <= 0x200F) || v == 0x2060 { continue }
            if v < 0x20 && v != 0x09 && v != 0x0A && v != 0x0D { continue }
            if v == 0x7F { continue }
            scalars.append(u)
        }
        return String(scalars)
    }

    private static func extractFromJSON(_ str: String, depth: Int) -> String? {
        guard depth < 3, let data = str.data(using: .utf8),
              let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        return extractFromJSONDict(obj, depth: depth)
    }

    private static func extractFromJSONDict(_ obj: [String: Any], depth: Int) -> String? {
        guard depth < 3 else { return nil }
        // Khớp khóa không phân biệt hoa/thường theo đúng thứ tự ưu tiên.
        var lowerMap: [String: Any] = [:]
        for (k, v) in obj { lowerMap[k.lowercased()] = v }
        for key in jsonTrackingKeys {
            guard let val = lowerMap[key] else { continue }
            if let s = val as? String {
                let cleaned = clean(s, depth: depth + 1)
                if !cleaned.isEmpty { return cleaned }
            } else if let n = val as? Int {
                let cleaned = clean(String(n), depth: depth + 1)
                if !cleaned.isEmpty { return cleaned }
            }
        }
        // Tìm lồng nhau tối đa 2 cấp (vd {"data": {"tracking_no": "..."}}).
        for v in obj.values {
            if let nested = v as? [String: Any], let hit = extractFromJSONDict(nested, depth: depth + 1) {
                return hit
            }
        }
        return nil
    }

    private static func parseURLComponents(_ full: String) -> URLComponents? {
        if let comps = URLComponents(string: full) { return comps }
        let trimmed = full.replacingOccurrences(of: " ", with: "%20")
        if let comps = URLComponents(string: trimmed) { return comps }
        if let encoded = full.addingPercentEncoding(withAllowedCharacters: .urlFragmentAllowed) {
            return URLComponents(string: encoded)
        }
        return nil
    }

    public static func clean(_ raw: String?) -> String {
        clean(raw, depth: 0)
    }

    private static func clean(_ raw: String?, depth: Int) -> String {
        guard let raw = raw, depth < 4 else { return "" }
        let str = stripInvisible(raw).trimmingCharacters(in: .whitespacesAndNewlines)
        if str.isEmpty { return "" }

        if matches(reHttp, str) || matches(reShortUrl, str) {
            let full = matches(reHttp, str) ? str : "https://" + str
            if let comps = parseURLComponents(full) {
                for item in comps.queryItems ?? [] {
                    if matches(reQueryKey, item.name), let v = item.value, matches(reUrlToken, v) {
                        return clean(v, depth: depth + 1)
                    }
                }
                let segments = comps.path.split(separator: "/").map(String.init).reversed()
                for seg in segments {
                    let v = seg.removingPercentEncoding ?? seg
                    if matches(reUrlToken, v), v.rangeOfCharacter(from: .decimalDigits) != nil {
                        return clean(v, depth: depth + 1)
                    }
                }
                if let frag = comps.fragment {
                    let v = frag.removingPercentEncoding ?? frag
                    if matches(reUrlToken, v), v.rangeOfCharacter(from: .decimalDigits) != nil {
                        return clean(v, depth: depth + 1)
                    }
                }
            }
            return ""
        }

        if str.hasPrefix("{"), str.hasSuffix("}"), let fromJson = extractFromJSON(str, depth: depth) {
            return fromJson
        }

        if let m = firstMatch(reSpxvn, in: str) { return m.uppercased() }
        if let m = firstMatch(reSoc, in: str) { return m.uppercased() }
        if let m = firstMatch(reNivn, in: str) { return m.uppercased() }
        if let m = firstMatch(reVtp, in: str) { return m.uppercased() }
        if let m = firstMatch(reGhn, in: str) { return m.uppercased() }
        if let m = firstMatch(reGhtk, in: str) { return m.uppercased() }
        if let m = firstMatch(reJt, in: str) { return m }

        // QR nhiều dòng (chỉ chạy khi chuỗi thật sự có xuống dòng để không đổi hành vi chuỗi 1 dòng).
        if str.contains("\n") || str.contains("\r") {
            let lines = str.components(separatedBy: .newlines)
                .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
                .filter { !$0.isEmpty }
            var candidates: [String] = []
            for line in lines {
                // Nếu dòng dạng "Mã vận đơn: TTVN1129574777", tách lấy phần sau dấu ":".
                let probe = line.contains(":") ? String(line.split(separator: ":", maxSplits: 1).last ?? "").trimmingCharacters(in: .whitespaces) : line
                let c = clean(probe.isEmpty ? line : probe, depth: depth + 1)
                if !c.isEmpty { candidates.append(c) }
            }
            if let carrierHit = candidates.first(where: { isPlausibleCleaned($0) && !carrier(for: $0).isEmpty }) {
                return carrierHit
            }
            if let plausible = candidates.first(where: { isPlausibleCleaned($0) }) {
                return plausible
            }
            if let first = candidates.first { return first }
        }

        var strip = CharacterSet(charactersIn: "*\"'<>#[]()")
        strip.formUnion(.whitespacesAndNewlines)
        let cleaned = str.trimmingCharacters(in: strip)
        if cleaned.contains("/") || cleaned.contains("|") || cleaned.contains(";") {
            let parts = cleaned.components(separatedBy: CharacterSet(charactersIn: "/|;"))
            for p in parts {
                let c = clean(p, depth: depth + 1)
                if !c.isEmpty { return c }
            }
        }
        return cleaned.uppercased()
    }

    private static func isPlausibleCleaned(_ c: String) -> Bool {
        guard c.count >= 8, c.count <= 40 else { return false }
        guard c.unicodeScalars.allSatisfy({ $0.isASCII }) else { return false }
        guard c.rangeOfCharacter(from: .decimalDigits) != nil else { return false }
        return matches(rePlausible, c)
    }

    /// Mã quét từ camera có đáng tin để gửi đi không. Chặn kết quả "linh tinh" do camera đọc nhầm hoa văn/chữ thành mã vạch
    /// (chuỗi quá ngắn, có khoảng trắng, chữ có dấu, không có chữ số). Nhập tay không đi qua bộ lọc này.
    public static func isPlausibleScan(_ raw: String) -> Bool {
        isPlausibleCleaned(clean(raw))
    }

    /// Nhiều mã cùng lúc trên một nhãn (mã vận đơn, mã đơn hàng, QR...): chọn mã vận đơn.
    /// Thứ tự ưu tiên: (1) mã đã có trong dữ liệu trên máy, (2) mã đúng dạng một hãng vận chuyển, (3) mã đầu tiên trông hợp lệ.
    /// Trả về chuỗi gốc (chưa làm sạch) của mã được chọn; nil nếu không mã nào đáng tin.
    public static func pickBest(_ raws: [String], isKnown: (String) -> Bool) -> String? {
        let plausible = raws.filter { isPlausibleScan($0) }
        if let known = plausible.first(where: { isKnown(clean($0)) }) { return known }
        if let carrierCode = plausible.first(where: { !carrier(for: clean($0)).isEmpty }) { return carrierCode }
        return plausible.first
    }

    /// Tách danh sách mã vận đơn từ nội dung người dùng dán (hỗ trợ URL, JSON, nhiều dòng, phân cách dấu phẩy/chấm phẩy/khoảng trắng).
    public static func extractPasteCodes(_ text: String) -> [String] {
        let trimmed = stripInvisible(text).trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return [] }
        // Nếu toàn bộ là 1 đối tượng JSON duy nhất, trích trực tiếp trước khi tách theo khoảng trắng.
        if trimmed.hasPrefix("{"), trimmed.hasSuffix("}") {
            let c = clean(trimmed)
            if c.count >= 6, c.contains(where: { $0.isNumber }) { return [c] }
        }
        var seen = Set<String>()
        var out: [String] = []
        for tok in trimmed.components(separatedBy: CharacterSet(charactersIn: " \t\r\n,;|")) {
            let c = clean(tok)
            guard c.count >= 6, c.count <= 50,
                  c.unicodeScalars.allSatisfy({ $0.isASCII }),
                  c.contains(where: { $0.isNumber }),
                  matches(rePlausible, c),
                  !seen.contains(c) else { continue }
            seen.insert(c)
            out.append(c)
        }
        return out
    }

    /// Port của detectCarrierFromCode.
    public static func carrier(for code: String) -> String {
        let c = code.trimmingCharacters(in: .whitespaces).uppercased()
        if c.isEmpty { return "" }
        if matches(reCarrierSpx, c) { return "SPX Express" }
        if matches(reCarrierVt, c) { return "Viettel Post" }
        if matches(reCarrierGhn, c) { return "Giao Hàng Nhanh" }
        if matches(reCarrierJt, c) { return "J&T Express" }
        if matches(reCarrierNinja, c) { return "Ninja Van" }
        return ""
    }
}
