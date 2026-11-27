import Foundation

/// Gộp các kiểu tên của CÙNG một mẫu (chủ dự án 28/11/2026), cùng quy tắc với máy tính (Core.Stats.ProductNames / ProductFamilies):
/// Shopee lúc ghi tên ngắn là mã mẫu ("STRIPES"), lúc ghi tên dài "Áo Polo LEN THỎ Local Brand Lavi Studio/ STRIPES Nam Nữ Unisex";
/// TikTok thêm tiền tố "[ DEAL MỞ BÁN ]", chữ kiểu đặc biệt "𝐋𝐚𝐯𝐢 𝐒𝐭𝐮𝐝𝐢𝐨", hoặc viết thường "Plain".
/// Khóa mẫu = LOẠI (2 chữ đầu: "Áo Polo", "Quần Kaki") + MÃ (chữ viết hoa sau "/"); tên ngắn chỉ có mã gộp vào mẫu nếu mã chỉ thuộc một loại.
/// Không nhận ra mã thì dùng tên đầy đủ (bỏ dấu, bỏ tiền tố [...]).
public enum ProductNames {
    private static let brackets = try? NSRegularExpression(pattern: #"\[[^\]]*\]"#)

    /// Chữ kiểu đặc biệt -> chữ thường (NFKC), bỏ tiền tố [...], gộp khoảng trắng.
    public static func tidy(_ name: String) -> String {
        var s = name.precomposedStringWithCompatibilityMapping
        if let re = brackets { s = re.stringByReplacingMatches(in: s, range: NSRange(s.startIndex..., in: s), withTemplate: " ") }
        return s.split(whereSeparator: { $0.isWhitespace }).joined(separator: " ")
    }

    private static func isCode(_ w: String) -> Bool {
        let letters = w.filter { $0.isLetter }
        return letters.count >= 3 && w.allSatisfy { $0.isLetter || $0.isNumber } && letters.allSatisfy { $0.isUppercase }
    }

    /// Chữ đầu tiên sau dấu "/" (bỏ khoảng trắng, dừng ở khoảng trắng, "/" hoặc gạch ngang).
    private static func firstAfterSlash(_ t: String) -> String {
        guard let slash = t.firstIndex(of: "/") else { return "" }
        let rest = t[t.index(after: slash)...].drop { $0.isWhitespace }
        return String(rest.prefix { !$0.isWhitespace && $0 != "/" && $0 != "-" && $0 != "–" && $0 != "—" })
    }

    /// Mã mẫu viết hoa ("STRIPES") hoặc "".
    public static func modelCode(_ name: String) -> String {
        let t = tidy(name)
        if t.contains("/") { let w = firstAfterSlash(t); return isCode(w) ? w : "" }
        return !t.contains(" ") && isCode(t) ? t : ""
    }

    /// Chữ đầu tiên sau "/" (mọi kiểu hoa/thường) hoặc "".
    public static func looseCode(_ name: String) -> String {
        let w = firstAfterSlash(tidy(name))
        return w.filter { $0.isLetter }.count >= 3 && w.allSatisfy { $0.isLetter || $0.isNumber } ? w : ""
    }

    public static func isCodeOnly(_ name: String) -> Bool { let t = tidy(name); return !t.contains(" ") && !t.contains("/") && isCode(t) }

    /// Loại sản phẩm: 2 chữ đầu (bỏ dấu).
    public static func typeKey(_ name: String) -> String {
        tidy(name).split(separator: " ").prefix(2).map { ProductMetrics.groupKey(String($0)) }.joined()
    }

    public static func familyKey(_ name: String) -> String {
        let code = ProductMetrics.groupKey(modelCode(name))
        if code.isEmpty { return ProductMetrics.groupKey(tidy(name)) }
        return isCodeOnly(name) ? "m:\(code)" : "m:\(code):\(typeKey(name))"
    }

    /// Tên hiển thị: ưu tiên tên đầy đủ (nhiều chữ) hay gặp nhất.
    public static func pickDisplay(_ spellings: [String: Int]) -> String {
        var merged: [String: Int] = [:]
        for (k, v) in spellings { let t = tidy(k); if !t.isEmpty { merged[t, default: 0] += v } }
        return merged.sorted { a, b in
            let ma = a.key.contains(" ") ? 0 : 1, mb = b.key.contains(" ") ? 0 : 1
            if ma != mb { return ma < mb }
            if a.value != b.value { return a.value > b.value }
            return a.key < b.key
        }.first?.key ?? ""
    }
}

/// Gộp mẫu có ngữ cảnh (dựng từ toàn bộ dòng đơn đang xét).
public final class ProductFamilies {
    private var known = Set<String>()
    private var codeToKey: [String: String] = [:]
    private var names: [String: [String: Int]] = [:]
    private var search: [String: Set<String>] = [:]
    private var cache: [String: String] = [:]

    public init() {}

    public static func build(rows: [OrderRow]) -> ProductFamilies {
        let f = ProductFamilies()
        var all: [String] = []
        var codeTypes: [String: Set<String>] = [:]
        for r in rows {
            for raw in ProductMetrics.splitPipe(r.n) where !raw.isEmpty {
                let n = ProductMetrics.clean(raw)
                all.append(n)
                let fk = ProductNames.familyKey(ProductMetrics.stripPremium(n))
                if fk.hasPrefix("m:"), let i = fk.dropFirst(2).firstIndex(of: ":") {
                    codeTypes[String(fk[..<i]), default: []].insert(fk)
                }
            }
        }
        for (codeKey, keys) in codeTypes {
            f.known.formUnion(keys)
            if keys.count == 1, let k = keys.first { f.codeToKey[codeKey] = k }
        }
        for n in all {
            let k = f.key(n)
            let shown = ProductMetrics.stripPremium(n)
            f.names[k, default: [:]][shown, default: 0] += 1
            f.search[k, default: []].insert(ProductMetrics.groupKey(ProductNames.tidy(shown)))
        }
        return f
    }

    /// Khóa mẫu của một tên (có hay không đuôi Premium đều được).
    public func key(_ name: String) -> String {
        let n = ProductMetrics.stripPremium(name)
        if let hit = cache[n] { return hit }
        var fk = ProductNames.familyKey(n)
        if fk.hasPrefix("m:") && !fk.dropFirst(2).contains(":") {
            if let full = codeToKey[fk] { fk = full }
        } else if !fk.hasPrefix("m:") {
            let loose = ProductNames.looseCode(n)
            if !loose.isEmpty {
                let cand = "m:\(ProductMetrics.groupKey(loose)):\(ProductNames.typeKey(n))"
                if known.contains(cand) { fk = cand }
            }
        }
        cache[n] = fk
        return fk
    }

    public func display(_ key: String) -> String { names[key].map { ProductNames.pickDisplay($0) } ?? "" }
    public func searchText(_ key: String) -> String { (search[key].map { $0.joined(separator: "|") } ?? "") + "|" + key }
}
