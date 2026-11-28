import Foundation

// Port of QuanLyHangHoan.Core.Import.PlatformImportPrep (C#) + the phone entry point that mirrors the desktop Salework import:
// rows -> DropPersonalColumns -> PlatformImportPrep.Prepare(one file "salework_dang_cho_hoan") -> TikTokMapper.Map per group.

/// One import source file (C# SourceFile).
public struct SaleworkSourceFile {
    public var name: String
    public var rows: [SaleworkRow]
    public init(name: String, rows: [SaleworkRow]) { self.name = name; self.rows = rows }
}

public enum SaleworkPrep {
    private static let aliases: [[String]] = [
        ["orderid", "madonhang", "iddonhang", "madon"],
        ["returnorderid", "returnid", "masokhieunai", "makhieunai", "mayeucautrahang"],
        ["returnlogisticstrackingid", "returntrackingid", "returntracking", "mavandontrahang", "mvdtrahang"],
        ["trackingid", "trackingnumber", "tracking", "mavandon", "mvd"],
        ["productname", "tensanpham", "tensanphamtrensan", "tenhanghoa", "itemname"],
        ["quantity", "qty", "soluong"],
        ["returnquantity", "soluonghoan", "skuquantityofreturn"],
        ["variation", "skuname", "tenphanloai", "tenphanloaitrensan"],
        ["sellersku", "masanphamsalework", "skuphanloai"],
        ["orderstatus", "trangthaidonhang"],
        ["returnstatus", "trangthaihoan", "trangthaitrahanghoantien"],
    ]

    private static let identityKeys = ["orderid", "returnorderid", "returnlogisticstrackingid", "trackingid"]

    static func canonical(_ key: String) -> String {
        let n = SWText.key(key)
        if let g = aliases.first(where: { $0.contains(n) }), let first = g.first { return first }
        return n
    }

    /// PlatformImportPrep.Prepare: returns the non-empty groups (in first-seen schema order) and the duplicate count.
    public static func prepare(_ files: [SaleworkSourceFile]) -> (groups: [[SaleworkRow]], duplicateCount: Int) {
        var groups: [String: [SaleworkRow]] = [:]
        var groupOrder: [String] = []
        var previousCounts: [String: Int] = [:]
        var duplicates = 0

        for (fileIndex, file) in files.enumerated() {
            var counts: [String: Int] = [:]
            var countsOrder: [String] = []
            var inheritedIdentity: [(String, String)] = []
            var inheritedIdentityCells: [(String, String)] = []
            var inheritedStatusCells: [(String, String)] = []
            var fileRows: [SaleworkRow] = []

            for original in file.rows {
                let entries = original.pairs.filter { !SWText.starts($0.key, "__source") }
                var canonicalRow: [String: String] = [:]
                for (key, value) in entries { canonicalRow[canonical(key)] = SWText.trim(value) }

                var identity: [(String, String)] = []
                for k in identityKeys {
                    if let v = canonicalRow[k], !v.isEmpty { identity.append((k, v)) }
                }
                if !identity.isEmpty {
                    inheritedIdentity = identity
                    inheritedStatusCells = entries
                        .filter { SWText.isMatch("status|trangthai|loaidonhoan", canonical($0.key)) && !SWText.trim($0.value).isEmpty }
                        .map { ($0.key, $0.value) }
                    inheritedIdentityCells = entries
                        .filter { identityKeys.contains(canonical($0.key)) && !SWText.trim($0.value).isEmpty }
                        .map { ($0.key, $0.value) }
                } else {
                    for (k, v) in inheritedIdentity { canonicalRow[k] = v }
                }

                let hasIdentity = !inheritedIdentity.isEmpty
                let fingerprint = canonicalRow.keys.sorted(by: SWText.ordinalLess)
                    .map { $0 + "\u{2}" + (canonicalRow[$0] ?? "") }
                    .joined(separator: "\u{1}")
                let count = (counts[fingerprint] ?? 0) + 1
                if counts[fingerprint] == nil { countsOrder.append(fingerprint) }
                counts[fingerprint] = count
                if hasIdentity && count <= (previousCounts[fingerprint] ?? 0) { duplicates += 1; continue }

                var row = original
                if identity.isEmpty {
                    for (k, v) in inheritedIdentityCells { row[k] = v }
                    for (k, v) in inheritedStatusCells {
                        let ov = original[k]
                        if !(ov != nil && !SWText.trim(ov ?? "").isEmpty) { row[k] = v }
                    }
                }
                row["__sourceFileName"] = file.name
                row["__sourceFileId"] = String(fileIndex)
                fileRows.append(row)
            }
            for fp in countsOrder {
                let c = counts[fp] ?? 0
                previousCounts[fp] = max(previousCounts[fp] ?? 0, c)
            }

            var schemaKeys: [String] = []
            var seen = Set<[UInt16]>()
            for r in file.rows {
                for k in r.keys where !SWText.starts(k, "__source") && seen.insert(Array(k.utf16)).inserted { schemaKeys.append(k) }
            }
            let schema = schemaKeys.sorted(by: SWText.ordinalLess).joined(separator: "\u{1}")
            if groups[schema] == nil { groups[schema] = []; groupOrder.append(schema) }
            groups[schema, default: []].append(contentsOf: fileRows)
        }

        let out = groupOrder.compactMap { groups[$0] }.filter { !$0.isEmpty }
        return (out, duplicates)
    }
}

/// One group of rows with the same column layout and what the mapper produced from it.
public struct SaleworkGroup {
    public let rows: [SaleworkRow]
    public let parcels: [SaleworkParcel]
    public let skipped: Int
    public let failedDelivery: Int
    public let premiumItems: Int
}

/// Result of the phone Salework import (same as the desktop SaleworkImportService.Apply up to the mapped parcels).
public struct SaleworkPrepared {
    /// Rows after the personal columns were dropped.
    public let clean: [SaleworkRow]
    public let duplicateCount: Int
    public let groups: [SaleworkGroup]
    /// Distinct order ids (trimmed, upper-cased, ordinal sort) of the clean rows.
    public let orderIds: [String]

    public var parcels: [SaleworkParcel] { groups.flatMap { $0.parcels } }
    /// The JSON objects to POST to the cloud (/import/mapped).
    public var incomingJSON: [[String: Any]] { parcels.map { $0.incomingJSON() } }
}

public enum SaleworkImport {
    /// Name the desktop app gives the single Salework source file.
    public static let sourceFileName = "salework_dang_cho_hoan"

    /// Reads a Salework export (.xlsx / .xlsm / .csv) into rows (SpreadsheetReader.Read).
    public static func readRows(fileName: String, data: Data) throws -> [SaleworkRow] {
        try XlsxSpreadsheetReader.read(fileName: fileName, data: data)
    }

    /// Year of `today` in the device calendar.
    public static func year(of today: Date) -> Int {
        Calendar(identifier: .gregorian).component(.year, from: today)
    }

    /// Full pipeline. Throws SaleworkImportError when a quantity cell is invalid (the desktop app fails the import too).
    public static func prepare(rows: [SaleworkRow], today: Date, options: SaleworkMapOptions? = nil) throws -> SaleworkPrepared {
        try prepare(rows: rows, todayYear: year(of: today), options: options)
    }

    /// Convenience for unordered dictionaries (column order is then ordinal, which can change "first matching column" choices).
    public static func prepare(rows: [[String: String]], today: Date) throws -> SaleworkPrepared {
        try prepare(rows: rows.map { SaleworkRow(dictionary: $0) }, today: today)
    }

    public static func prepare(rows: [SaleworkRow], todayYear: Int, options: SaleworkMapOptions? = nil) throws -> SaleworkPrepared {
        let clean = XlsxSpreadsheetReader.dropPersonalColumns(rows)
        let prep = SaleworkPrep.prepare([SaleworkSourceFile(name: sourceFileName, rows: clean)])
        var opts = options ?? SaleworkMapOptions(todayYear: todayYear)
        opts.todayYear = todayYear
        var groups: [SaleworkGroup] = []
        for g in prep.groups {
            let m = try SaleworkMapper.map(g, options: opts)
            groups.append(SaleworkGroup(rows: g, parcels: m.parcels, skipped: m.skipped, failedDelivery: m.failedDelivery, premiumItems: m.premiumItems))
        }
        return SaleworkPrepared(clean: clean, duplicateCount: prep.duplicateCount, groups: groups, orderIds: orderIds(clean))
    }

    /// Ảnh 31 (28/11/2026): kiểm kênh bằng mã đơn trong file (không phụ thuộc đọc được ô kênh trên trang).
    /// TikTok: dãy 15–20 chữ số; Shopee: 6 số ngày + 8 ký tự có chữ cái (vd "261120ABCDEF"). Trả số mã mỗi loại.
    public static func channelCounts(orderIds: [String]) -> (tiktok: Int, shopee: Int) {
        var t = 0, s = 0
        for raw in orderIds {
            let v = raw.trimmingCharacters(in: .whitespaces).uppercased()
            if v.count >= 15 && v.count <= 20 && v.allSatisfy({ $0.isASCII && $0.isNumber }) { t += 1 }
            else if v.count == 14 && v.prefix(6).allSatisfy({ $0.isASCII && $0.isNumber }) && v.contains(where: { $0.isASCII && $0.isLetter })
                        && v.allSatisfy({ $0.isASCII && ($0.isNumber || $0.isLetter) }) { s += 1 }
        }
        return (t, s)
    }

    /// File có phải đơn TikTok không: nil = không đủ mã để biết (cho qua), false = phần lớn là mã Shopee.
    public static func looksLikeTikTok(orderIds: [String]) -> Bool? {
        let c = channelCounts(orderIds: orderIds)
        if c.tiktok + c.shopee == 0 { return nil }
        return c.tiktok >= c.shopee
    }

    /// First column whose key is "madonhang" / "orderid"; values trimmed + upper-cased, distinct, ordinal sort.
    public static func orderIds(_ clean: [SaleworkRow]) -> [String] {
        var idKey: String? = nil
        outer: for r in clean {
            for k in r.keys {
                let n = SWText.key(k)
                if n == "madonhang" || n == "orderid" { idKey = k; break outer }
            }
        }
        guard let key = idKey else { return [] }
        var ids: [String] = []
        for r in clean {
            if let v = r[key], !SWText.trim(v).isEmpty { ids.append(SWText.upper(SWText.trim(v))) }
        }
        return SWText.distinct(ids).sorted(by: SWText.ordinalLess)
    }
}
