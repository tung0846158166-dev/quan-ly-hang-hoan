import Foundation

/// Bản port của extractCleanTrackingCode trong mobile_scanner.html (phải cho kết quả giống hệt).
public enum TrackingCode {
    private static func firstMatch(_ pattern: String, in s: String, ignoreCase: Bool = true) -> String? {
        guard let re = try? NSRegularExpression(pattern: pattern, options: ignoreCase ? [.caseInsensitive] : []) else { return nil }
        let ns = s as NSString
        guard let m = re.firstMatch(in: s, range: NSRange(location: 0, length: ns.length)), m.numberOfRanges > 1 else { return nil }
        let r = m.range(at: 1)
        if r.location == NSNotFound { return nil }
        return ns.substring(with: r)
    }

    private static func matches(_ pattern: String, _ s: String) -> Bool {
        guard let re = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]) else { return false }
        return re.firstMatch(in: s, range: NSRange(location: 0, length: (s as NSString).length)) != nil
    }

    public static func clean(_ raw: String?) -> String {
        guard let raw = raw else { return "" }
        let str = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        if str.isEmpty { return "" }

        if matches("^https?://", str) || matches("^(?:spx|ghn)\\.vn/", str) {
            let full = matches("^https?://", str) ? str : "https://" + str
            if let comps = URLComponents(string: full) {
                let keyPattern = "^(?:spx|track|tracking|tracking_number|tracking_no|tracking_code|waybill|waybill_no|code|c|id|bill|order_code|s)$"
                for item in comps.queryItems ?? [] {
                    if matches(keyPattern, item.name), let v = item.value, matches("^[A-Za-z0-9._-]{6,50}$", v) {
                        return clean(v)
                    }
                }
                let segments = comps.path.split(separator: "/").map(String.init).reversed()
                for seg in segments {
                    let v = seg.removingPercentEncoding ?? seg
                    if matches("^[A-Za-z0-9._-]{6,50}$", v), v.rangeOfCharacter(from: .decimalDigits) != nil {
                        return clean(v)
                    }
                }
            }
            return ""
        }

        if let m = firstMatch("(SPXVN[0-9A-Za-z]{6,18})", in: str) { return m.uppercased() }
        if let m = firstMatch("(SOC[0-9A-Za-z]{6,16})", in: str) { return m.uppercased() }
        if let m = firstMatch("((?:NIVN|NLVN|SHPVN)[0-9A-Za-z]{6,16})", in: str) { return m.uppercased() }
        if let m = firstMatch("(VTP?[0-9A-Za-z]{6,15})", in: str) { return m.uppercased() }
        if let m = firstMatch("(GHN[0-9A-Za-z]{6,14}|G8[0-9A-Za-z]{6,14})", in: str) { return m.uppercased() }
        if let m = firstMatch("(S\\d+[\\.\\-][0-9A-Za-z\\.\\-]{6,24})", in: str) { return m.uppercased() }
        if let m = firstMatch("(?<![0-9])(8[0246][0-9]{10,13})(?![0-9])", in: str, ignoreCase: false) { return m }

        var strip = CharacterSet(charactersIn: "*\"'<>#[]()")
        strip.formUnion(.whitespacesAndNewlines)
        let cleaned = str.trimmingCharacters(in: strip)
        if cleaned.contains("/") || cleaned.contains("|") || cleaned.contains(";") {
            let parts = cleaned.components(separatedBy: CharacterSet(charactersIn: "/|;"))
            for p in parts {
                let c = clean(p)
                if !c.isEmpty { return c }
            }
        }
        return cleaned.uppercased()
    }

    /// Mã quét từ camera có đáng tin để gửi đi không. Chặn kết quả "linh tinh" do camera đọc nhầm hoa văn/chữ thành mã vạch
    /// (chuỗi quá ngắn, có khoảng trắng, chữ có dấu, không có chữ số). Nhập tay không đi qua bộ lọc này.
    public static func isPlausibleScan(_ raw: String) -> Bool {
        let c = clean(raw)
        guard c.count >= 8, c.count <= 40 else { return false }
        guard c.unicodeScalars.allSatisfy({ $0.isASCII }) else { return false }
        guard c.rangeOfCharacter(from: .decimalDigits) != nil else { return false }
        return matches("^[A-Z0-9][A-Z0-9._\\-]*$", c)
    }

    /// Port của detectCarrierFromCode.
    public static func carrier(for code: String) -> String {
        let c = code.trimmingCharacters(in: .whitespaces).uppercased()
        if c.isEmpty { return "" }
        if matches("^SPXVN|^SPX", c) { return "SPX Express" }
        if matches("^VN|^VTP", c) { return "Viettel Post" }
        if matches("^GHN", c) { return "Giao Hàng Nhanh" }
        if matches("^8[0245]\\d{10,12}|^862|^863|^587|^JNT|^JT", c) { return "J&T Express" }
        if matches("^NINJA", c) { return "Ninja Van" }
        return ""
    }
}
