import Foundation
#if canImport(Compression)
import Compression
#endif

/// Minimal read-only ZIP archive (enough for .xlsx): End Of Central Directory + central directory + local headers,
/// methods 0 (stored) and 8 (deflate). No ZIP64, no encryption.
struct XlsxZip {
    struct Entry {
        let name: String
        let method: UInt16
        let flags: UInt16
        let compressedSize: Int
        let uncompressedSize: Int
        let localHeaderOffset: Int
    }

    struct ZipError: Error {
        let reason: String
    }

    /// Entries in central-directory order.
    let entries: [Entry]
    private let bytes: [UInt8]
    private let byName: [String: Int]

    /// Upper bound for one decompressed entry (protects the phone from zip bombs).
    static let maxEntrySize = 512 * 1024 * 1024

    init(data: Data) throws {
        let b = [UInt8](data)
        bytes = b

        func u16(_ o: Int) throws -> Int {
            guard o >= 0, o + 2 <= b.count else { throw ZipError(reason: "truncated") }
            return Int(b[o]) | (Int(b[o + 1]) << 8)
        }
        func u32(_ o: Int) throws -> Int {
            guard o >= 0, o + 4 <= b.count else { throw ZipError(reason: "truncated") }
            return Int(b[o]) | (Int(b[o + 1]) << 8) | (Int(b[o + 2]) << 16) | (Int(b[o + 3]) << 24)
        }

        // End Of Central Directory: signature 0x06054b50, searched backwards (comment up to 65535 bytes).
        guard b.count >= 22 else { throw ZipError(reason: "too small") }
        var eocd = -1
        var i = b.count - 22
        let stop = max(0, b.count - 22 - 65535)
        while i >= stop {
            if b[i] == 0x50 && b[i + 1] == 0x4b && b[i + 2] == 0x05 && b[i + 3] == 0x06 { eocd = i; break }
            i -= 1
        }
        guard eocd >= 0 else { throw ZipError(reason: "no end of central directory") }
        let total = try u16(eocd + 10)
        let cdSize = try u32(eocd + 12)
        let cdOffset = try u32(eocd + 16)
        guard cdOffset >= 0, cdOffset + cdSize <= b.count else { throw ZipError(reason: "bad central directory") }

        var list: [Entry] = []
        var map: [String: Int] = [:]
        var p = cdOffset
        for _ in 0..<total {
            guard try u32(p) == 0x02014b50 else { throw ZipError(reason: "bad central header") }
            let flags = try u16(p + 8)
            let method = try u16(p + 10)
            let compSize = try u32(p + 20)
            let uncompSize = try u32(p + 24)
            let nameLen = try u16(p + 28)
            let extraLen = try u16(p + 30)
            let commentLen = try u16(p + 32)
            let localOffset = try u32(p + 42)
            guard p + 46 + nameLen <= b.count else { throw ZipError(reason: "truncated name") }
            let nameBytes = Array(b[(p + 46)..<(p + 46 + nameLen)])
            let name = String(bytes: nameBytes, encoding: .utf8) ?? String(bytes: nameBytes, encoding: .isoLatin1) ?? ""
            let entry = Entry(name: name, method: UInt16(method), flags: UInt16(flags), compressedSize: compSize,
                              uncompressedSize: uncompSize, localHeaderOffset: localOffset)
            if map[name] == nil { map[name] = list.count }
            list.append(entry)
            p += 46 + nameLen + extraLen + commentLen
        }
        entries = list
        byName = map
    }

    func entry(named name: String) -> Entry? {
        guard let i = byName[name], i < entries.count else { return nil }
        return entries[i]
    }

    /// Decompressed bytes of an entry.
    func read(_ e: Entry) throws -> Data {
        let b = bytes
        let o = e.localHeaderOffset
        guard o >= 0, o + 30 <= b.count,
              b[o] == 0x50, b[o + 1] == 0x4b, b[o + 2] == 0x03, b[o + 3] == 0x04 else { throw ZipError(reason: "bad local header") }
        if e.flags & 1 != 0 { throw ZipError(reason: "encrypted") }
        let nameLen = Int(b[o + 26]) | (Int(b[o + 27]) << 8)
        let extraLen = Int(b[o + 28]) | (Int(b[o + 29]) << 8)
        let start = o + 30 + nameLen + extraLen
        guard e.compressedSize >= 0, start + e.compressedSize <= b.count else { throw ZipError(reason: "truncated data") }
        guard e.uncompressedSize >= 0, e.uncompressedSize <= XlsxZip.maxEntrySize else { throw ZipError(reason: "entry too large") }
        let src = Array(b[start..<(start + e.compressedSize)])
        switch e.method {
        case 0:
            return Data(src)
        case 8:
            return try XlsxZip.inflate(src, expectedSize: e.uncompressedSize)
        default:
            throw ZipError(reason: "unsupported method \(e.method)")
        }
    }

    /// Raw DEFLATE (no zlib header): Apple's COMPRESSION_ZLIB is exactly that.
    static func inflate(_ src: [UInt8], expectedSize: Int) throws -> Data {
        if expectedSize == 0 { return Data() }
        #if canImport(Compression)
        if src.isEmpty { throw ZipError(reason: "empty deflate stream") }
        let capacity = expectedSize + 64
        var dst = [UInt8](repeating: 0, count: capacity)
        let written = dst.withUnsafeMutableBufferPointer { dp -> Int in
            src.withUnsafeBufferPointer { sp -> Int in
                guard let d = dp.baseAddress, let s = sp.baseAddress else { return 0 }
                return compression_decode_buffer(d, capacity, s, sp.count, nil, COMPRESSION_ZLIB)
            }
        }
        guard written == expectedSize else { throw ZipError(reason: "inflate failed (\(written) of \(expectedSize))") }
        return Data(dst[0..<written])
        #else
        throw ZipError(reason: "deflate not available on this platform")
        #endif
    }
}
