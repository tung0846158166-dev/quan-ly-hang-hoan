import Foundation

// Port of QuanLyHangHoan.Core.Import.LegacyText (C#) for the Salework import on the phone.
// Every helper mirrors .NET semantics: String.Length = UTF-16 units, ordinal comparisons, .NET whitespace set,
// ToUpperInvariant/ToLowerInvariant = simple (1:1) case mapping, Regex \s = [\f\n\r\t\v\x85\p{Z}].

/// One spreadsheet row whose keys keep their insertion order (C# Dictionary keeps it and the mapper depends on it:
/// "first matching column", buyer-note scan order...).
public struct SaleworkRow: Equatable {
    public private(set) var keys: [String] = []
    private var values: [String: String] = [:]

    public init() {}

    public init(_ pairs: [(String, String)]) {
        for (k, v) in pairs { self[k] = v }
    }

    /// Builds a row from an unordered dictionary: keys are ordered ordinally (column order is lost).
    public init(dictionary: [String: String]) {
        for k in dictionary.keys.sorted(by: SWText.ordinalLess) { self[k] = dictionary[k] }
    }

    public subscript(key: String) -> String? {
        get { values[key] }
        set {
            if let v = newValue {
                if values.updateValue(v, forKey: key) == nil { keys.append(key) }
            } else if values.removeValue(forKey: key) != nil {
                keys.removeAll { $0 == key }
            }
        }
    }

    public var count: Int { keys.count }
    public var isEmpty: Bool { keys.isEmpty }
    public var pairs: [(key: String, value: String)] { keys.map { ($0, values[$0] ?? "") } }
    public var dictionary: [String: String] { values }
}

/// Thrown by the mapper when a quantity cell is not a non-negative integer (C# FormatException).
public struct SaleworkImportError: Error, LocalizedError, Equatable {
    public let message: String
    public init(_ message: String) { self.message = message }
    public var errorDescription: String? { message }
}

/// A regex match over an NSString (UTF-16 indices, like .NET).
struct SWMatch {
    let ns: NSString
    let result: NSTextCheckingResult

    /// Group value, nil when the group did not participate (.NET Group.Success == false).
    func group(_ i: Int) -> String? {
        guard i >= 0, i < result.numberOfRanges else { return nil }
        let r = result.range(at: i)
        if r.location == NSNotFound { return nil }
        return ns.substring(with: r)
    }

    /// UTF-16 index of the whole match (.NET Match.Index).
    var index: Int { result.range.location }
}

enum SWText {
    // MARK: whitespace / trim (.NET char.IsWhiteSpace)

    static func isWS(_ u: Unicode.Scalar) -> Bool {
        switch u.value {
        case 0x09...0x0D, 0x20, 0x85, 0xA0, 0x1680, 0x2000...0x200A, 0x2028, 0x2029, 0x202F, 0x205F, 0x3000: return true
        default: return false
        }
    }

    /// .NET String.Trim().
    static func trim(_ s: String) -> String {
        let scalars = Array(s.unicodeScalars)
        var start = 0
        var end = scalars.count
        while start < end && isWS(scalars[start]) { start += 1 }
        while end > start && isWS(scalars[end - 1]) { end -= 1 }
        if start == 0 && end == scalars.count { return s }
        var out = String.UnicodeScalarView()
        out.append(contentsOf: scalars[start..<end])
        return String(out)
    }

    /// .NET String.TrimStart(params char[]).
    static func trimStart(_ s: String, _ chars: Set<Unicode.Scalar>) -> String {
        let scalars = Array(s.unicodeScalars)
        var start = 0
        while start < scalars.count && chars.contains(scalars[start]) { start += 1 }
        if start == 0 { return s }
        var out = String.UnicodeScalarView()
        out.append(contentsOf: scalars[start...])
        return String(out)
    }

    /// .NET string.IsNullOrWhiteSpace.
    static func isBlank(_ s: String?) -> Bool {
        guard let s = s else { return true }
        for u in s.unicodeScalars where !isWS(u) { return false }
        return true
    }

    // MARK: length / ordinal comparisons (UTF-16 like .NET)

    static func len(_ s: String) -> Int { s.utf16.count }

    static func ordinalEquals(_ a: String, _ b: String) -> Bool { a.utf16.elementsEqual(b.utf16) }

    static func ordinalLess(_ a: String, _ b: String) -> Bool { a.utf16.lexicographicallyPrecedes(b.utf16) }

    static func starts(_ s: String, _ p: String) -> Bool { s.utf16.starts(with: p.utf16) }

    static func ends(_ s: String, _ p: String) -> Bool {
        let a = Array(s.utf16), b = Array(p.utf16)
        if b.count > a.count { return false }
        return Array(a[(a.count - b.count)...]) == b
    }

    static func contains(_ s: String, _ p: String) -> Bool {
        if p.isEmpty { return true }
        return (s as NSString).range(of: p, options: .literal).location != NSNotFound
    }

    /// First UTF-16 units of s (C# s[..n]).
    static func prefix16(_ s: String, _ n: Int) -> String {
        let ns = s as NSString
        return ns.substring(to: max(0, min(n, ns.length)))
    }

    /// From UTF-16 index n to the end (C# s[n..]).
    static func suffixFrom16(_ s: String, _ n: Int) -> String {
        let ns = s as NSString
        return ns.substring(from: max(0, min(n, ns.length)))
    }

    /// Distinct keeping the first occurrence, ordinal equality.
    static func distinct(_ list: [String]) -> [String] {
        var seen = Set<[UInt16]>()
        var out: [String] = []
        for s in list where seen.insert(Array(s.utf16)).inserted { out.append(s) }
        return out
    }

    static func padLeft2(_ s: String) -> String {
        let n = len(s)
        return n >= 2 ? s : String(repeating: "0", count: 2 - n) + s
    }

    // MARK: invariant case mapping (simple, 1:1)

    static func lower(_ s: String) -> String {
        var out = String.UnicodeScalarView()
        for u in s.unicodeScalars {
            if u.value < 0x80 {
                if u.value >= 65 && u.value <= 90 { out.append(Unicode.Scalar(UInt8(u.value + 32))) } else { out.append(u) }
                continue
            }
            let m = u.properties.lowercaseMapping.unicodeScalars
            if m.count == 1, let f = m.first { out.append(f) } else { out.append(u) }
        }
        return String(out)
    }

    static func upper(_ s: String) -> String {
        var out = String.UnicodeScalarView()
        for u in s.unicodeScalars {
            if u.value < 0x80 {
                if u.value >= 97 && u.value <= 122 { out.append(Unicode.Scalar(UInt8(u.value - 32))) } else { out.append(u) }
                continue
            }
            let m = u.properties.uppercaseMapping.unicodeScalars
            if m.count == 1, let f = m.first { out.append(f) } else { out.append(u) }
        }
        return String(out)
    }

    // MARK: numbers (.NET int.TryParse / double.TryParse with InvariantCulture)

    private static func isNumWS(_ u: Unicode.Scalar) -> Bool { (u.value >= 0x09 && u.value <= 0x0D) || u.value == 0x20 }

    private static func trimNumWS(_ s: String) -> String {
        let scalars = Array(s.unicodeScalars)
        var start = 0
        var end = scalars.count
        while start < end && isNumWS(scalars[start]) { start += 1 }
        while end > start && isNumWS(scalars[end - 1]) { end -= 1 }
        var out = String.UnicodeScalarView()
        out.append(contentsOf: scalars[start..<end])
        return String(out)
    }

    /// int.TryParse (NumberStyles.Integer): optional surrounding whitespace, optional sign, ASCII digits, Int32 range.
    static func parseInt32(_ s: String) -> Int? {
        let t = trimNumWS(s)
        var digits = Substring(t)
        var negative = false
        if let f = digits.first, f == "+" || f == "-" { negative = f == "-"; digits = digits.dropFirst() }
        if digits.isEmpty { return nil }
        var n: Int64 = 0
        for u in digits.unicodeScalars {
            guard u.value >= 48 && u.value <= 57 else { return nil }
            n = n * 10 + Int64(u.value - 48)
            if n > 2_147_483_648 { return nil }
        }
        if negative { n = -n }
        guard n >= Int64(Int32.min) && n <= Int64(Int32.max) else { return nil }
        return Int(n)
    }

    /// double.TryParse(NumberStyles.Float, InvariantCulture): whitespace, sign, digits, '.', exponent. (No hex/inf/nan words.)
    static func parseDouble(_ s: String) -> Double? {
        let t = trimNumWS(s)
        guard isMatch(#"^[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?$"#, t) else { return nil }
        return Double(t)
    }

    // MARK: regex (NSRegularExpression with .NET-compatible \s)

    private static let wsClassInner = "\\t\\n\\x{0B}\\f\\r\\x{85}\\p{Z}"
    private static var cache: [String: NSRegularExpression] = [:]
    private static let lock = NSLock()

    /// Rewrites .NET-only constructs for ICU: \s -> explicit .NET whitespace set.
    static func translate(_ pattern: String) -> String {
        let chars = Array(pattern.unicodeScalars)
        var out = ""
        var inClass = false
        var i = 0
        while i < chars.count {
            let c = chars[i]
            if c == "\\" && i + 1 < chars.count {
                let n = chars[i + 1]
                if n == "s" {
                    out += inClass ? wsClassInner : "[" + wsClassInner + "]"
                } else {
                    out.unicodeScalars.append(c)
                    out.unicodeScalars.append(n)
                }
                i += 2
                continue
            }
            if !inClass && c == "[" { inClass = true } else if inClass && c == "]" { inClass = false }
            out.unicodeScalars.append(c)
            i += 1
        }
        return out
    }

    static func regex(_ pattern: String, ci: Bool) -> NSRegularExpression? {
        let key = (ci ? "i|" : "c|") + pattern
        lock.lock()
        defer { lock.unlock() }
        if let r = cache[key] { return r }
        guard let r = try? NSRegularExpression(pattern: translate(pattern), options: ci ? [.caseInsensitive] : []) else { return nil }
        cache[key] = r
        return r
    }

    static func match(_ pattern: String, _ s: String, ci: Bool = false) -> SWMatch? {
        guard let re = regex(pattern, ci: ci) else { return nil }
        let ns = s as NSString
        guard let m = re.firstMatch(in: s, options: [], range: NSRange(location: 0, length: ns.length)) else { return nil }
        return SWMatch(ns: ns, result: m)
    }

    static func isMatch(_ pattern: String, _ s: String, ci: Bool = false) -> Bool { match(pattern, s, ci: ci) != nil }

    static func replace(_ s: String, _ pattern: String, ci: Bool = false) -> String {
        guard let re = regex(pattern, ci: ci) else { return s }
        let ns = s as NSString
        return re.stringByReplacingMatches(in: s, options: [], range: NSRange(location: 0, length: ns.length), withTemplate: "")
    }

    /// .NET Regex.Split for patterns without capture groups (keeps empty pieces).
    static func split(_ s: String, _ pattern: String) -> [String] {
        guard let re = regex(pattern, ci: false) else { return [s] }
        let ns = s as NSString
        let matches = re.matches(in: s, options: [], range: NSRange(location: 0, length: ns.length))
        if matches.isEmpty { return [s] }
        var out: [String] = []
        var pos = 0
        for m in matches {
            let r = m.range
            if r.length == 0 && (r.location == 0 || r.location == ns.length) { continue }
            out.append(ns.substring(with: NSRange(location: pos, length: r.location - pos)))
            pos = r.location + r.length
        }
        out.append(ns.substring(from: pos))
        return out
    }

    // MARK: LegacyText

    /// removeVietnameseTones: NFD, drop U+0300..U+036F, đ->d, lower (invariant). Result stays decomposed like C#.
    static func removeTones(_ s: String?) -> String {
        let d = (s ?? "").decomposedStringWithCanonicalMapping
        var out = String.UnicodeScalarView()
        for u in d.unicodeScalars {
            if u.value >= 0x300 && u.value <= 0x36F { continue }
            if u.value == 0x111 { out.append("d") } else if u.value == 0x110 { out.append("D") } else { out.append(u) }
        }
        return lower(String(out))
    }

    /// Column key: no tones, lower, only a-z0-9.
    static func key(_ s: String?) -> String {
        var out = String.UnicodeScalarView()
        for u in removeTones(s).unicodeScalars where (u.value >= 97 && u.value <= 122) || (u.value >= 48 && u.value <= 57) {
            out.append(u)
        }
        return String(out)
    }

    static func cleanTrackingCode(_ raw: String?) -> String {
        guard let raw = raw, !raw.isEmpty else { return "" }
        var str = trim(raw)

        if contains(str, "http://") || contains(str, "https://") || contains(str, "spx.vn") || contains(str, "ghn.vn") {
            if let m = match(#"(?:spx|track|code|c|id|bill|order_code|s)=([A-Za-z0-9\.\-_]+)"#, str, ci: true),
               let g = m.group(1), len(g) > 0 {
                str = g
            }
        }

        let patterns = [
            #"(SPXVN[0-9A-Za-z]{6,18}|VN2[4-9][0-9A-Za-z]{8,14})"#,
            #"(SOC[0-9A-Za-z]{6,16})"#,
            #"((?:NIVN|NLVN|SHPVN)[0-9A-Za-z]{6,16})"#,
            #"(TTVN[0-9A-Za-z]{6,16})"#,
            #"(SHOPEEVTP[0-9A-Za-z]{6,18}|VTP?[0-9A-Za-z]{6,15})"#,
            #"(GHN[0-9A-Za-z]{6,14}|G8[0-9A-Za-z]{6,14}|GY[0-9A-Za-z]{6,10})"#,
            #"(S[0-9]+[\.\-][0-9A-Za-z\.\-]{6,24})"#,
        ]
        for p in patterns {
            if let m = match(p, str, ci: true) { return upper(m.group(1) ?? "") }
        }

        if let m = match(#"(?<![0-9])(8[0246][0-9]{10,13})(?![0-9])"#, str) { return m.group(1) ?? "" }

        let cleaned = trim(replace(str, #"^[\*"'<>#\s\[\]\(\)]+|[\*"'<>#\s\[\]\(\)]+$"#))
        if contains(cleaned, "/") || contains(cleaned, "|") || contains(cleaned, ";") {
            for p in split(cleaned, #"[\/\|;]"#) {
                let pc = cleanTrackingCode(p)
                if !pc.isEmpty { return pc }
            }
        }
        return upper(cleaned)
    }

    /// normalizeScientificOrderId
    static func normalizeScientificOrderId(_ val: String?) -> String {
        guard let val = val else { return "" }
        let s = trim(replace(trim(val), #"^['"]+|['"]+$"#))
        if s.isEmpty { return "" }
        if let m = match(#"^([+-]?[0-9]+)(?:\.([0-9]+))?[eE]\+?([0-9]+)$"#, s), isMatch(#"[.+]"#, s),
           let exp = parseInt32(m.group(3) ?? ""), exp <= 40 {
            let intPart = trimStart(m.group(1) ?? "", ["+", "-"])
            let frac = m.group(2) ?? ""
            let fracLen = len(frac)
            return exp >= fracLen
                ? intPart + frac + String(repeating: "0", count: exp - fracLen)
                : intPart + prefix16(frac, exp)
        }
        return s
    }

    /// parseImportedQuantity: non-negative integer, throws otherwise.
    static func parseQuantity(_ value: String?, fallback: Int = 0) throws -> Int {
        guard let value = value, !trim(value).isEmpty else { return fallback }
        var text = replace(trim(value), #"\s"#)
        if isMatch(#"^[0-9]+[.,]0{1,2}$"#, text) {
            text = split(text, #"[.,]"#).first ?? text
        } else if isMatch(#"^[0-9]{1,3}(?:,[0-9]{3})+$"#, text) {
            text = text.replacingOccurrences(of: ",", with: "")
        }
        let err = SaleworkImportError("Số lượng không hợp lệ: " + value + ". Cần số nguyên không âm.")
        guard let q = parseDouble(text), q == q.rounded(.down), q >= 0, q <= 9007199254740991.0 else { throw err }
        guard q <= Double(Int32.max) else { throw err } // C# checked((int)q) -> OverflowException
        return Int(q)
    }

    /// parseMoneyCellValue
    static func parseMoney(_ val: String?) -> Int64 {
        var s = trim(val ?? "")
        if s.isEmpty { return 0 }
        s = replace(s, #"[.,][0-9]{1,2}$"#)
        let digits = replace(s, #"[^0-9]"#)
        if digits.isEmpty { return 0 }
        return Int64(digits) ?? 0
    }

    /// normalizeShortDate
    static func normalizeShortDate(_ val: String?, _ currentYear: Int) -> String {
        let s = trim(val ?? "")
        if s.isEmpty { return "" }
        var timeSuffix = ""
        var datePart = s
        if let mt = match(#"\s+([0-9]{1,2}:[0-9]{2}(?::[0-9]{2})?)$"#, s) {
            timeSuffix = " " + (mt.group(1) ?? "")
            datePart = trim(prefix16(s, mt.index))
        }
        if let m = match(#"^([0-9]{4})[\/\-\.]([0-9]{1,2})[\/\-\.]([0-9]{1,2})"#, datePart) {
            return "\(padLeft2(m.group(3) ?? ""))/\(padLeft2(m.group(2) ?? ""))/\(m.group(1) ?? "")\(timeSuffix)"
        }
        if let m = match(#"^([0-9]{1,2})[\/\-\.]([0-9]{1,2})$"#, datePart) {
            return "\(padLeft2(m.group(1) ?? ""))/\(padLeft2(m.group(2) ?? ""))/\(currentYear)\(timeSuffix)"
        }
        if let m = match(#"^([0-9]{1,2})[\/\-\.]([0-9]{1,2})[\/\-\.]([0-9]{2,4})$"#, datePart) {
            var y = m.group(3) ?? ""
            if len(y) == 2 { y = "20" + y }
            return "\(padLeft2(m.group(1) ?? ""))/\(padLeft2(m.group(2) ?? ""))/\(y)\(timeSuffix)"
        }
        return s
    }

    /// extractDateFromCellValue (string branch)
    static func extractDate(_ val: String?, _ currentYear: Int) -> String {
        let s = trim(val ?? "")
        if s.isEmpty { return "" }

        var timeStr = ""
        if let mTime = match(#"(?:\s|^)([0-9]{1,2}:[0-9]{2}(?::[0-9]{2})?)(?:\s|$)"#, s) {
            let p = (mTime.group(1) ?? "").components(separatedBy: ":")
            let p0 = p.count > 0 ? p[0] : ""
            let p1 = p.count > 1 ? p[1] : ""
            let ss = p.count > 2 ? padLeft2(p[2]) : ""
            timeStr = "\(padLeft2(p0)):\(padLeft2(p1))\(!ss.isEmpty && ss != "00" ? ":" + ss : "")"
        }
        let suffix = timeStr.isEmpty ? "" : " " + timeStr

        if let m = match(#"(\b[0-9]{1,2})[\/\-\.]([0-9]{1,2})[\/\-\.]([0-9]{4})\b"#, s) {
            return "\(padLeft2(m.group(1) ?? ""))/\(padLeft2(m.group(2) ?? ""))/\(m.group(3) ?? "")\(suffix)"
        }
        if let m = match(#"(\b[0-9]{4})[\/\-\.]([0-9]{1,2})[\/\-\.]([0-9]{1,2})\b"#, s) {
            return "\(padLeft2(m.group(3) ?? ""))/\(padLeft2(m.group(2) ?? ""))/\(m.group(1) ?? "")\(suffix)"
        }
        if let m = match(#"(\b[0-9]{1,2})[\/\-\.]([0-9]{1,2})[\/\-\.]([0-9]{2})\b"#, s) {
            return "\(padLeft2(m.group(1) ?? ""))/\(padLeft2(m.group(2) ?? ""))/20\(m.group(3) ?? "")\(suffix)"
        }
        return normalizeShortDate(s, currentYear)
    }

    /// detectCarrierByTrackingCode (global function of the old app); nil when unknown.
    static func detectCarrierByTrackingCode(_ rawTrack: String?) -> String? {
        let code = upper(trim(rawTrack ?? ""))
        if code.isEmpty { return nil }
        let n = len(code)

        if starts(code, "SPXVN") || starts(code, "SPX") || isMatch(#"^VN2[4-9][0-9A-Z]{8,14}$"#, code) { return "SPX Express" }
        if contains(code, ".") || starts(code, "GHTK") { return "GHTK" }
        if !starts(code, "SHOPEEVTP") && (starts(code, "NIVN") || starts(code, "SHP") || starts(code, "NVSG") || starts(code, "NVVN")) { return "Ninja Van" }
        if starts(code, "AHA") { return "AhaMove" }
        if isMatch(#"^[A-Z]{2}[0-9]{9}VN$"#, code) || (ends(code, "VN") && n == 13) { return "VNPost" }
        if starts(code, "SHOPEEVTP") || starts(code, "VTP") || starts(code, "VT") { return "Viettel Post" }
        if starts(code, "TTVN") || starts(code, "BEST") { return "Best Express" }
        if isMatch(#"^01M[0-9A-Z]{20,25}$"#, code) { return "Khác" }
        if isMatch(#"^[0-9]+$"#, code) {
            if n == 12 && (starts(code, "80") || starts(code, "82") || starts(code, "84") || starts(code, "86") || starts(code, "66")) { return "J&T Express" }
            if n >= 13 && n <= 14 { return "Best Express" }
            if n == 12 { return "Best Express" }
            if n >= 9 && n <= 11 { return "Viettel Post" }
        }
        if starts(code, "GHN") || isMatch(#"^GY[A-Z0-9]{6,8}$"#, code) ||
            (isMatch(#"^[A-Z0-9]{6,10}$"#, code) && isMatch("[A-Z]", code) && isMatch("[0-9]", code)) { return "GHN" }
        return nil
    }

    /// detectCarrierFromCode (local function of the TikTok importer).
    static func detectTikTokCarrierFromCode(_ code: String?) -> String {
        guard let code = code, !code.isEmpty else { return "" }
        let t = trim(upper(code))
        if isMatch(#"^8[0245][0-9]{10,12}$"#, t) { return "J&T Express" }
        if starts(t, "VTP") || starts(t, "VT") { return "Viettel Post" }
        if starts(t, "SPX") { return "SPX Express" }
        if starts(t, "GHN") || starts(t, "G8") { return "GHN" }
        if starts(t, "S") && contains(t, ".") { return "GHTK" }
        if starts(t, "NLVN") || starts(t, "NIVN") || starts(t, "SHPVN") { return "Ninja Van" }
        if starts(t, "VN") || ends(t, "VN") { return "VNPost" }
        if starts(t, "BEST") || starts(t, "TTVN") { return "Best Express" }
        return ""
    }

    // MARK: buyer note

    private static let buyerNoteDirect = [
        "Ghi chú của Người mua khi trả hàng", "Nhận xét từ Người mua", "Ghi chú của Người mua khi đặt hàng", "Ghi chú của khách",
        "Lưu ý từ người mua", "Ghi chú của người mua", "Ghi chú người mua", "Ghi chú khách hàng", "Lời nhắn từ người mua",
        "Buyer Message", "Customer Note", "Buyer Note",
    ]

    static func isNoneNote(_ v: String?) -> Bool {
        isMatch(#"^(không|khong|none|\-)$"#, removeTones(v), ci: true)
    }

    static func extractBuyerNote(_ r: SaleworkRow) -> String {
        for dl in buyerNoteDirect {
            if let raw = r[dl] {
                let v = trim(raw)
                if !v.isEmpty && !isNoneNote(v) { return v }
            }
        }
        for (k, raw) in r.pairs {
            let kn = key(k)
            let hit = (contains(kn, "nhanxet") && contains(kn, "nguoimua"))
                || (contains(kn, "ghichu") && (contains(kn, "nguoimua") || contains(kn, "khach")))
                || kn == "buyermessage" || kn == "customernote" || kn == "buyernote"
            if !hit { continue }
            if contains(kn, "nguoiban") || contains(kn, "tien") || contains(kn, "phi") || contains(kn, "gia") || contains(kn, "lydo") || contains(kn, "lido") { continue }
            let v = trim(raw)
            if !v.isEmpty && !isNoneNote(v) { return v }
        }
        return ""
    }

    // MARK: product / color / size

    private static let colorWords = [
        "xanh navy", "xanh den", "xanh duong", "xanh la", "xanh reu", "xanh ngoc", "xanh coban", "xanh bo", "xanh nhat", "xanh dam", "xanh pastel", "xanh lam",
        "xanh than", "xanh nau", "xanh kem", "xam than", "xam dam", "xam nhat", "mau mo", "kaki", "retro", "mori", "urban", "koda",
        "den", "trang", "trang nga", "trang tinh", "do do", "do tuoi", "do man", "do ruou", "hong", "hong phan", "hong baby",
        "vang", "vang bo", "vang chanh", "nau", "nau dat", "nau bo", "nau tay", "nau socola",
        "be", "be nhat", "be dam", "kem", "kem sua", "xam", "xam tieu", "xam chuot", "xam khoi", "xam ghi", "ghi", "tim", "tim than", "cam", "cam dat", "reu",
    ]

    /// ColorWords.OrderByDescending(c => c.Length) (stable).
    private static let colorWordsByLength: [String] = colorWords.enumerated()
        .sorted { a, b in a.element.count != b.element.count ? a.element.count > b.element.count : a.offset < b.offset }
        .map { $0.element }

    private static let reasonKeywords = [
        "khong nhan", "nguoi nhan", "tu choi", "that bai", "khong thanh", "huy don", "hoan tien", "tra hang", "ly do", "be vo", "hu hong",
        "thieu hang", "doi y", "chua nhan", "lien lac", "dia chi", "sdt", "nghe may", "hen giao", "bom hang", "khong mua", "sai hang",
        "kem chat luong", "khac mo ta", "khong dung", "dong kiem", "thu tien", "chuyen hoan", "luu kho", "buyer cancelled", "delivery failed",
        "rejected", "return reason", "package lost",
    ]

    static func isReturnReasonText(_ str: String?) -> Bool {
        let s = trim(str ?? "")
        if s.isEmpty { return false }
        let norm = removeTones(s)
        if reasonKeywords.contains(where: { contains(norm, $0) }) { return true }
        return len(s) > 30
    }

    static func isKnownColorName(_ str: String?) -> Bool {
        let norm = removeTones(trim(str ?? ""))
        if norm.isEmpty { return false }
        return colorWords.contains { c in ordinalEquals(norm, c) || starts(norm, c + " ") || ends(norm, " " + c) }
    }

    static func isValidColorString(_ str: String?) -> Bool {
        let s = trim(str ?? "")
        if s.isEmpty { return false }
        if isMatch(#"^[0-9]{4,}$"#, s) { return false }
        if isReturnReasonText(s) { return false }
        if len(s) > 25 { return false }
        return !isMatch(#"^(xxxl|xxl|xl|xs|s|m|l|[2-5]xl|[0-9]{2}|freesize|free size|onesize|one size)$"#, removeTones(s), ci: true)
    }

    static func isValidSizeString(_ str: String?) -> Bool {
        let s = trim(str ?? "")
        if s.isEmpty { return false }
        if isReturnReasonText(s) { return false }
        if isMatch(#"^[0-9]{4,}$"#, s) { return false }
        let norm = removeTones(s)
        if ["freesize", "free size", "onesize", "one size", "fs"].contains(where: { ordinalEquals(norm, $0) }) { return true }
        if len(s) > 10 { return false }
        if split(s, #"\s+"#).filter({ !$0.isEmpty }).count > 2 { return false }
        if isKnownColorName(s) { return false }
        return true
    }

    static func stripQualitySuffix(_ str: String?) -> String {
        var s = str ?? ""
        s = replace(s, #"\s*[\-\|\/]\s*premium\s*cao\s*c[aấ]p?\b"#, ci: true)
        s = replace(s, #"\s*[\-\|\/]\s*cao\s*c[aấ]p?\b"#, ci: true)
        s = replace(s, #"\s*[\-\|\/]\s*premium\b"#, ci: true)
        s = replace(s, #"\(\s*(?:pre\s*)?cao\s*c[aấ]p?\s*\)"#, ci: true)
        s = replace(s, #"\(\s*premium\s*\)"#, ci: true)
        s = replace(s, #"\bpremium\s*cao\s*c[aấ]p?\b"#, ci: true)
        s = replace(s, #"\s+premium$"#, ci: true)
        s = trim(s)
        s = replace(s, #"^[\-\,\;\/\|]+|[\-\,\;\/\|]+$"#)
        return trim(s)
    }

    struct ProductVariation {
        let product: String
        let color: String
        let size: String
        let extraNote: String
    }

    /// parseSaleworkProductAndVariation
    static func parseProductAndVariation(_ rawProd: String?, _ rawVar: String?, _ rawSellerSku: String?) -> ProductVariation {
        var cleanProd = trim(rawProd ?? "")
        var cleanVar = trim(rawVar ?? "")
        let cleanSku = trim(rawSellerSku ?? "")

        let combined = "\(cleanProd) \(cleanVar)"
        var premium = ""
        if isMatch(#"premium\s*cao\s*c[aấ]p?"#, combined, ci: true) { premium = "Premium Cao Cấp" }
        else if isMatch(#"\(\s*premium\s*\)"#, combined, ci: true) { premium = "Premium Cao Cấp" }
        else if isMatch(#"[\-\|\/]\s*premium\b"#, combined, ci: true) || isMatch(#"\s+premium$"#, cleanVar, ci: true) { premium = "Premium Cao Cấp" }

        if !cleanProd.isEmpty && !cleanVar.isEmpty {
            let varNoSuffix = stripQualitySuffix(cleanVar)
            let lowerProd = lower(cleanProd)
            for candidate in [lower(cleanVar), lower(varNoSuffix)] {
                if candidate.isEmpty { continue }
                let dash = " - " + candidate
                if ends(lowerProd, dash) {
                    cleanProd = trim(prefix16(cleanProd, len(cleanProd) - len(dash)))
                    break
                }
            }
        }

        cleanProd = replace(cleanProd, #"\s*[\-\|\/]\s*premium\s*cao\s*c[aấ]p?\s*$"#, ci: true)
        cleanProd = trim(replace(cleanProd, #"\s*\(\s*premium\s*\)\s*$"#, ci: true))

        if let mTrail = match(#"^(.*?)\s+-\s+([^,\-]{1,35},\s*(?:XXXL|XXL|XL|XS|S|M|L|[2-5]XL|[0-9]{2}|Free\s*size))$"#, cleanProd, ci: true) {
            cleanProd = trim(mTrail.group(1) ?? "")
            if cleanVar.isEmpty { cleanVar = trim(mTrail.group(2) ?? "") }
        }

        if !premium.isEmpty && !cleanProd.isEmpty && !isMatch("premium", cleanProd, ci: true) {
            cleanProd = "\(cleanProd) - \(premium)"
        }

        let cleanedVar = stripQualitySuffix(cleanVar)
        var color = "", size = "", extra = ""

        if !cleanedVar.isEmpty && !isMatch(#"^[0-9]{4,}$"#, cleanedVar) && !isMatch(#"^[0-9]+\.[0-9]+e\+[0-9]+$"#, cleanedVar, ci: true) {
            let mColor = match(#"(?:màu\s*sắc|màu|mau\s*sac|mau|color)\s*[:\-]\s*([^,;\/\n\|]+)"#, cleanedVar, ci: true)
            let mSize = match(#"(?:kích\s*thước|kích\s*cỡ|size|kich\s*thuoc|kich\s*co)\s*[:\-]\s*([^,;\/\n\|]+)"#, cleanedVar, ci: true)
            if let mc = mColor, isValidColorString(mc.group(1)) { color = stripQualitySuffix(mc.group(1)) }
            if let ms = mSize, isValidSizeString(ms.group(1)) { size = upper(trim(ms.group(1) ?? "")) }

            if color.isEmpty || size.isEmpty {
                let sizeExact = #"^(XXXL|XXL|XL|XS|S|M|L|[2-5]XL|[0-9]{2}|Free\s*size|Freesize|One\s*size|[<>]\s*[0-9]+\s*kg)$"#
                let parts = split(cleanedVar, #"[,;\/\|]"#).map { stripQualitySuffix($0) }.filter { !$0.isEmpty }
                for p in parts {
                    for sp in split(p, #"\s*\-\s*"#).map({ stripQualitySuffix($0) }).filter({ !$0.isEmpty }) {
                        if isMatch(sizeExact, sp, ci: true) && size.isEmpty && isValidSizeString(sp) {
                            size = replace(upper(sp), #"\s+"#)
                        } else if color.isEmpty && isValidColorString(sp) {
                            color = trim(replace(sp, #"^(?:màu\s*sắc|màu|mau\s*sac|mau|color)\s*[:\-]\s*"#, ci: true))
                        } else if isReturnReasonText(sp) && extra.isEmpty {
                            extra = sp
                        }
                    }
                }
                if color.isEmpty, let first = parts.first, isValidColorString(first), !isMatch(sizeExact, first, ci: true) {
                    color = first
                }
            }
        }

        if color.isEmpty && !cleanProd.isEmpty {
            let withoutPrem = stripQualitySuffix(cleanProd)
            let normProd = removeTones(withoutPrem)
            for cWord in colorWordsByLength where ends(normProd, " " + cWord) {
                color = trim(suffixFrom16(withoutPrem, len(withoutPrem) - len(cWord)))
                break
            }
        }

        if (color.isEmpty || size.isEmpty) && !cleanSku.isEmpty && contains(cleanSku, "_") {
            let skuParts = cleanSku.components(separatedBy: "_").map { trim($0) }.filter { !$0.isEmpty }
            if skuParts.count >= 2, let last = skuParts.last {
                if isMatch(#"^(XXXL|XXL|XL|XS|S|M|L|[2-5]XL|[0-9]{2})$"#, last, ci: true) && size.isEmpty { size = upper(last) }
                if skuParts.count >= 3 && color.isEmpty { color = skuParts[1..<(skuParts.count - 1)].joined(separator: " ") }
                if cleanProd.isEmpty, let first = skuParts.first, !first.isEmpty { cleanProd = first }
            }
        }

        return ProductVariation(product: cleanProd, color: color, size: size, extraNote: extra)
    }
}
