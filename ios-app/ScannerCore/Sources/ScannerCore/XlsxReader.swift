import Foundation
#if canImport(FoundationXML)
import FoundationXML
#endif

// Port of QuanLyHangHoan.Data.Import.SpreadsheetReader (C#): xlsx / csv -> rows of strings, best order table, personal columns.

/// C# SpreadsheetFormatException (same Vietnamese messages).
public struct SpreadsheetFormatError: Error, LocalizedError, Equatable {
    public let message: String
    /// true = the workbook was readable but has no order table (unrelated file), unlike a broken file.
    public let notAnOrderTable: Bool
    public init(_ message: String, notAnOrderTable: Bool = false) {
        self.message = message
        self.notAnOrderTable = notAnOrderTable
    }
    public var errorDescription: String? { message }
}

/// Tiny XML element tree (namespace-agnostic: local names only), built with Foundation XMLParser.
final class XlsxXmlElement {
    let name: String
    let attributes: [String: String]
    var children: [XlsxXmlNode] = []

    init(name: String, attributes: [String: String]) {
        self.name = name
        self.attributes = attributes
    }

    /// XElement.Elements(): direct child elements.
    var elements: [XlsxXmlElement] {
        children.compactMap { node -> XlsxXmlElement? in
            if case .element(let e) = node { return e }
            return nil
        }
    }

    /// XElement.Descendants() filtered by local name (document order).
    func descendants(named local: String) -> [XlsxXmlElement] {
        var out: [XlsxXmlElement] = []
        func walk(_ e: XlsxXmlElement) {
            for c in e.children {
                if case .element(let ce) = c {
                    if ce.name == local { out.append(ce) }
                    walk(ce)
                }
            }
        }
        walk(self)
        return out
    }

    func firstDescendant(named local: String) -> XlsxXmlElement? {
        for c in children {
            if case .element(let ce) = c {
                if ce.name == local { return ce }
                if let d = ce.firstDescendant(named: local) { return d }
            }
        }
        return nil
    }

    /// XElement.Value: concatenation of all descendant text.
    var value: String {
        var s = ""
        func walk(_ e: XlsxXmlElement) {
            for c in e.children {
                switch c {
                case .text(let t): s += t
                case .element(let ce): walk(ce)
                }
            }
        }
        walk(self)
        return s
    }
}

enum XlsxXmlNode {
    case element(XlsxXmlElement)
    case text(String)
}

/// Builds the tree. Whitespace-only text outside xml:space="preserve" is dropped, like XDocument.Load without PreserveWhitespace.
final class XlsxXmlTreeBuilder: NSObject, XMLParserDelegate {
    private var stack: [XlsxXmlElement] = []
    private var preserve: [Bool] = [false]
    private var pending = ""
    private(set) var root: XlsxXmlElement?

    static func parse(_ data: Data) -> XlsxXmlElement? {
        let parser = XMLParser(data: data)
        parser.shouldProcessNamespaces = false
        parser.shouldReportNamespacePrefixes = false
        parser.shouldResolveExternalEntities = false
        let builder = XlsxXmlTreeBuilder()
        parser.delegate = builder
        guard parser.parse() else { return nil }
        return builder.root
    }

    private static func localName(_ qName: String) -> String {
        if let i = qName.lastIndex(of: ":") { return String(qName[qName.index(after: i)...]) }
        return qName
    }

    private func flush() {
        if pending.isEmpty { return }
        let text = pending
        pending = ""
        guard let top = stack.last else { return }
        let whitespaceOnly = text.unicodeScalars.allSatisfy { $0 == " " || $0 == "\t" || $0 == "\r" || $0 == "\n" }
        if whitespaceOnly && !(preserve.last ?? false) { return }
        top.children.append(.text(text))
    }

    func parser(_ parser: XMLParser, didStartElement elementName: String, namespaceURI: String?,
                qualifiedName qName: String?, attributes attributeDict: [String: String] = [:]) {
        flush()
        let e = XlsxXmlElement(name: XlsxXmlTreeBuilder.localName(elementName), attributes: attributeDict)
        if let top = stack.last { top.children.append(.element(e)) } else if root == nil { root = e }
        stack.append(e)
        var p = preserve.last ?? false
        if let sp = attributeDict["xml:space"] {
            if sp == "preserve" { p = true } else if sp == "default" { p = false }
        }
        preserve.append(p)
    }

    func parser(_ parser: XMLParser, didEndElement elementName: String, namespaceURI: String?, qualifiedName qName: String?) {
        flush()
        if !stack.isEmpty { stack.removeLast() }
        if preserve.count > 1 { preserve.removeLast() }
    }

    func parser(_ parser: XMLParser, foundCharacters string: String) {
        pending += string
    }

    func parser(_ parser: XMLParser, foundCDATA CDATABlock: Data) {
        flush()
        if let top = stack.last, let s = String(data: CDATABlock, encoding: .utf8) { top.children.append(.text(s)) }
    }
}

public enum XlsxSpreadsheetReader {
    // Header sets (readWorkbookRowsFromBuffer of the old app).
    private static let identities: Set<String> = [
        "orderid", "madonhang", "iddonhang", "madon", "trackingid", "trackingnumber", "mavandon", "mavandontrahang", "mvdtrahang",
        "returnlogisticstrackingid", "returntrackingid", "returnorderid", "returnid", "masokhieunai", "makhieunai",
    ]
    private static let products: Set<String> = [
        "productname", "product", "tensanpham", "tensanphamtrensan", "tenhanghoa", "itemname", "sellersku", "masku", "skuname",
        "tenphanloai", "variation", "hang", "sanpham",
    ]
    private static let quantities: Set<String> = ["quantity", "qty", "soluong", "returnquantity", "soluonghoan", "skuquantityofreturn", "sl"]
    private static let statuses: Set<String> = [
        "orderstatus", "returnstatus", "trangthaidonhang", "trangthaitrahang", "trangthaitrahanghoantien", "trangthaihoan", "trangthaive", "trangthai",
    ]

    /// Customer personal columns: dropped before anything else.
    public static let personalHeaders = ["Tên khách hàng", "Số điện thoại", "Địa chỉ", "Tỉnh", "Huyện", "Ghi chú vận chuyển", "Người tạo đơn"]

    private static let personalNormalized: Set<String> = Set(personalHeaders.map { normalizeHeader($0) }).union([
        "tennguoimua", "nguoimua", "tennguoidungnguoimua", "tendangnhapnguoimua", "buyerusername", "buyername", "diachinhanhang",
        "diachilayhanghoantra", "tennguoinhan", "deliveryaddress", "returnaddress", "recipientname", "buyerphone",
        "recipient", "phone", "province", "district", "commune", "detailaddress", "additionaladdressinformation", "buyermessage",
    ])

    /// NFD, drop nonspacing marks, đ->d, lower, keep only a-z0-9.
    public static func normalizeHeader(_ value: String?) -> String {
        let s = (value ?? "").decomposedStringWithCanonicalMapping
        var out = String.UnicodeScalarView()
        for u in s.unicodeScalars {
            if u.properties.generalCategory == .nonspacingMark { continue }
            var c = u
            if u.value == 0x111 { c = "d" } else if u.value == 0x110 { c = "D" }
            if c.value >= 65 && c.value <= 90 { c = Unicode.Scalar(UInt8(c.value + 32)) }
            if (c.value >= 97 && c.value <= 122) || (c.value >= 48 && c.value <= 57) { out.append(c) }
        }
        return String(out)
    }

    /// SpreadsheetReader.Read: dispatch on the extension (.xlsx / .xlsm / .csv).
    public static func read(fileName: String, data: Data) throws -> [SaleworkRow] {
        let ext = SWText.lower(extensionOf(fileName))
        switch ext {
        case ".xlsx", ".xlsm": return try readXlsx(data)
        case ".csv": return try readCsv(String(decoding: data, as: UTF8.self))
        default: throw SpreadsheetFormatError("Định dạng file không hỗ trợ: \"\(ext)\" (chỉ nhận .xlsx, .csv).")
        }
    }

    /// Path.GetExtension: from the last '.' after the last separator; "" when none or when '.' is the last char.
    static func extensionOf(_ fileName: String) -> String {
        let u = Array(fileName.utf16)
        var i = u.count - 1
        while i >= 0 {
            let c = u[i]
            if c == 0x2E { // '.'
                if i == u.count - 1 { return "" }
                return String(decoding: u[i...], as: UTF16.self)
            }
            if c == 0x2F || c == 0x5C || c == 0x3A { return "" } // '/', '\', ':'
            i -= 1
        }
        return ""
    }

    public static func dropPersonalColumns(_ rows: [SaleworkRow]) -> [SaleworkRow] {
        rows.map { r in SaleworkRow(r.pairs.filter { !personalNormalized.contains(normalizeHeader($0.key)) }.map { ($0.key, $0.value) }) }
    }

    // MARK: xlsx

    public static func readXlsx(_ data: Data) throws -> [SaleworkRow] {
        let sheets = try loadXlsxSheets(data)
        return try bestTable(sheets)
    }

    private static let invalidXlsx = SpreadsheetFormatError("Không phải file .xlsx hợp lệ.")
    private static let brokenXlsx = SpreadsheetFormatError("File .xlsx bị hỏng.")

    static func loadXlsxSheets(_ data: Data) throws -> [[[String]]] {
        let zip: XlsxZip
        do { zip = try XlsxZip(data: data) } catch { throw invalidXlsx }

        func doc(_ name: String) throws -> XlsxXmlElement {
            guard let e = zip.entry(named: name) else { throw invalidXlsx }
            let bytes: Data
            do { bytes = try zip.read(e) } catch { throw invalidXlsx }
            guard let root = XlsxXmlTreeBuilder.parse(bytes) else { throw brokenXlsx }
            return root
        }

        var sheetKeys: [(name: String, n: Int, order: Int)] = []
        for (i, e) in zip.entries.enumerated() {
            if let m = SWText.match(#"^xl/worksheets/sheet([0-9]+)\.xml$"#, e.name), let n = Int(m.group(1) ?? "") {
                sheetKeys.append((e.name, n, i))
            }
        }
        sheetKeys.sort { a, b in a.n != b.n ? a.n < b.n : a.order < b.order }
        if sheetKeys.isEmpty { throw invalidXlsx }

        var shared: [String] = []
        if zip.entry(named: "xl/sharedStrings.xml") != nil {
            for si in try doc("xl/sharedStrings.xml").descendants(named: "si") {
                shared.append(si.descendants(named: "t").map { $0.value }.joined())
            }
        }

        let dateStyles = try loadDateStyles(zip)

        var result: [[[String]]] = []
        for key in sheetKeys {
            var matrix: [[String]] = []
            for row in try doc(key.name).descendants(named: "row") {
                var cells: [String] = []
                for c in row.elements where c.name == "c" {
                    guard let rf = c.attributes["r"] else { continue }
                    let idx = colIndex(rf)
                    // C# would index cells[-1] and fail with an unhandled exception: treat as a broken file.
                    guard idx >= 0 else { throw brokenXlsx }
                    let t = c.attributes["t"]
                    var val = ""
                    if t == "inlineStr" {
                        val = c.descendants(named: "t").map { $0.value }.joined()
                    } else if let v = c.elements.first(where: { $0.name == "v" })?.value {
                        if t == "s" {
                            if let si = SWText.parseInt32(v), si >= 0, si < shared.count { val = shared[si] } else { val = "" }
                        } else {
                            val = v
                            if t == nil || t == "n", let sAttr = c.attributes["s"], let sidx = SWText.parseInt32(sAttr), dateStyles.contains(sidx),
                               let serial = SWText.parseDouble(v) {
                                guard let text = serialToText(serial) else { throw brokenXlsx }
                                val = text
                            }
                        }
                    }
                    while cells.count <= idx { cells.append("") }
                    cells[idx] = val
                }
                matrix.append(cells)
            }
            result.append(matrix)
        }
        return result
    }

    static func colIndex(_ cellRef: String) -> Int {
        var n = 0
        for u in cellRef.unicodeScalars {
            if u.value < 65 || u.value > 90 { break }
            n = n * 26 + Int(u.value - 65 + 1)
            if n > 10_000_000 { break }
        }
        return n - 1
    }

    // DateTime(1899, 12, 30) as days since 1970-01-01, ticks per day/ms like .NET.
    private static let ticksPerDay: Int64 = 864_000_000_000
    private static let ticksPerSecond: Int64 = 10_000_000

    /// days_from_civil (proleptic Gregorian), days since 1970-01-01.
    static func daysFromCivil(_ y0: Int64, _ m: Int64, _ d: Int64) -> Int64 {
        let y = m <= 2 ? y0 - 1 : y0
        let era = (y >= 0 ? y : y - 399) / 400
        let yoe = y - era * 400
        let mp = (m + 9) % 12
        let doy = (153 * mp + 2) / 5 + d - 1
        let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy
        return era * 146097 + doe - 719468
    }

    /// civil_from_days
    static func civilFromDays(_ z0: Int64) -> (y: Int64, m: Int64, d: Int64) {
        let z = z0 + 719468
        let era = (z >= 0 ? z : z - 146096) / 146097
        let doe = z - era * 146097
        let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365
        let y = yoe + era * 400
        let doy = doe - (365 * yoe + yoe / 4 - yoe / 100)
        let mp = (5 * doy + 2) / 153
        let d = doy - (153 * mp + 2) / 5 + 1
        let m = mp < 10 ? mp + 3 : mp - 9
        return (m <= 2 ? y + 1 : y, m, d)
    }

    private static func pad(_ v: Int64, _ width: Int) -> String {
        let s = String(v)
        return s.count >= width ? s : String(repeating: "0", count: width - s.count) + s
    }

    /// SerialToText (.NET 8 DateTime.AddDays tick arithmetic): whole -> "yyyy-MM-dd", else +0.5 ms -> "yyyy-MM-dd HH:mm:ss".
    /// nil when out of DateTime range (C# throws).
    static func serialToText(_ serial: Double) -> String? {
        guard serial.isFinite, abs(serial) <= 10_000_000 else { return nil }
        let integral = serial.rounded(.towardZero)
        let fraction = serial - integral
        var ticks = Int64(integral) * ticksPerDay + Int64(fraction * Double(ticksPerDay))
        let whole = abs(serial - serial.rounded(.down)) < 1e-9
        if !whole { ticks += 5_000 }
        // Absolute ticks since 0001-01-01 must stay inside DateTime range.
        let baseDays = daysFromCivil(1899, 12, 30) - daysFromCivil(1, 1, 1)
        let absTicks = baseDays * ticksPerDay + ticks
        let maxTicks = (daysFromCivil(10000, 1, 1) - daysFromCivil(1, 1, 1)) * ticksPerDay - 1
        guard absTicks >= 0, absTicks <= maxTicks else { return nil }
        let dayIndex = absTicks / ticksPerDay
        let inDay = absTicks % ticksPerDay
        let civil = civilFromDays(dayIndex + daysFromCivil(1, 1, 1))
        let date = "\(pad(civil.y, 4))-\(pad(civil.m, 2))-\(pad(civil.d, 2))"
        if whole { return date }
        let secs = inDay / ticksPerSecond
        return "\(date) \(pad(secs / 3600, 2)):\(pad((secs / 60) % 60, 2)):\(pad(secs % 60, 2))"
    }

    /// Indices of cellXfs styles that have a date/time number format.
    static func loadDateStyles(_ zip: XlsxZip) throws -> Set<Int> {
        var result = Set<Int>()
        guard let entry = zip.entry(named: "xl/styles.xml") else { return result }
        let bytes: Data
        do { bytes = try zip.read(entry) } catch { throw invalidXlsx }
        guard let root = XlsxXmlTreeBuilder.parse(bytes) else { throw brokenXlsx }
        var custom: [Int: String] = [:]
        for nf in root.descendants(named: "numFmt") {
            guard let idText = nf.attributes["numFmtId"], let id = SWText.parseInt32(idText) else { continue }
            if custom[id] == nil { custom[id] = nf.attributes["formatCode"] ?? "" }
        }
        let xfs = root.firstDescendant(named: "cellXfs")?.elements.filter { $0.name == "xf" } ?? []
        for (i, xf) in xfs.enumerated() {
            let id = xf.attributes["numFmtId"].flatMap { SWText.parseInt32($0) } ?? 0
            var isDate = (14...22).contains(id) || (27...36).contains(id) || (45...47).contains(id) || (50...58).contains(id)
            if !isDate, let code = custom[id] {
                let stripped = SWText.replace(code, #""[^"]*"|\\.|\[[^\]]*\]"#)
                isDate = SWText.isMatch("[dmyhs]", stripped, ci: true) && !SWText.contains(stripped, "%")
            }
            if isDate { result.insert(i) }
        }
        return result
    }

    // MARK: csv

    public static func readCsv(_ text: String) throws -> [SaleworkRow] {
        try bestTable([parseCsv(text)])
    }

    static func parseCsv(_ input: String) -> [[String]] {
        let text = SWText.trimStart(input, ["\u{FEFF}"])
        var firstLine: [Unicode.Scalar] = []
        for u in text.unicodeScalars {
            if u == "\n" { break }
            firstLine.append(u)
        }
        let candidates: [Unicode.Scalar] = [",", ";", "\t"]
        var delim: Unicode.Scalar = ","
        var best = -1
        for d in candidates {
            let n = firstLine.filter { $0 == d }.count
            if n > best { best = n; delim = d }
        }

        var matrix: [[String]] = []
        var row: [String] = []
        var cell = String.UnicodeScalarView()
        var quoted = false
        let chars = Array(text.unicodeScalars)
        var i = 0
        while i < chars.count {
            let ch = chars[i]
            if quoted {
                if ch == "\"" {
                    if i + 1 < chars.count && chars[i + 1] == "\"" { cell.append("\""); i += 1 } else { quoted = false }
                } else {
                    cell.append(ch)
                }
            } else if ch == "\"" && cell.isEmpty {
                quoted = true
            } else if ch == delim {
                row.append(String(cell)); cell = String.UnicodeScalarView()
            } else if ch == "\r" {
                // handled at \n
            } else if ch == "\n" {
                row.append(String(cell)); cell = String.UnicodeScalarView()
                matrix.append(row); row = []
            } else {
                cell.append(ch)
            }
            i += 1
        }
        if !cell.isEmpty || !row.isEmpty { row.append(String(cell)); matrix.append(row) }
        return matrix
    }

    // MARK: best order table

    static func bestTable(_ sheets: [[[String]]]) throws -> [SaleworkRow] {
        var best: [SaleworkRow]? = nil
        var bestScore = -1
        var hasCells = false

        for matrix in sheets {
            for h in 0..<matrix.count {
                let cells = matrix[h]
                if cells.contains(where: { !SWText.isBlank($0) }) { hasCells = true }
                let keys = cells.map { normalizeHeader($0) }
                let identityCount = keys.filter { identities.contains($0) }.count
                let categories = [products, quantities, statuses].filter { set in keys.contains(where: { set.contains($0) }) }.count
                if identityCount == 0 || categories == 0 { continue }

                let names = headerNames(cells)
                var rows: [SaleworkRow] = []
                var r = h + 1
                while r < matrix.count {
                    var d = SaleworkRow()
                    var any = false
                    let line = matrix[r]
                    for c in 0..<names.count {
                        let v = c < line.count ? line[c] : ""
                        d[names[c]] = v
                        if !SWText.isBlank(v) { any = true }
                    }
                    if any { rows.append(d) }
                    r += 1
                }
                let score = categories * 10 + identityCount
                if !rows.isEmpty && score > bestScore { best = rows; bestScore = score }
            }
        }

        if let b = best { return b }
        if hasCells {
            throw SpreadsheetFormatError("Không tìm thấy trang tính có tiêu đề dữ liệu đơn hàng và dòng dữ liệu.", notAnOrderTable: true)
        }
        return []
    }

    /// Duplicate names get _1, _2 (like SheetJS); empty names become __col{i}.
    static func headerNames(_ cells: [String]) -> [String] {
        var seen: [String: Int] = [:]
        var names: [String] = []
        for (i, raw) in cells.enumerated() {
            var name = SWText.trim(raw)
            if name.isEmpty { name = "__col\(i)" }
            if let n = seen[name] {
                seen[name] = n + 1
                name = name + "_" + String(n + 1)
            } else {
                seen[name] = 0
            }
            names.append(name)
        }
        return names
    }
}
