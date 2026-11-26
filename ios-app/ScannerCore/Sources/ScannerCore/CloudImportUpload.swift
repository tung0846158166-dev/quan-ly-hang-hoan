import Foundation

/// Kết quả một lần nạp lên cloud (cộng dồn các lô).
public struct CloudImportTotals: Equatable, Sendable {
    public var added = 0, updated = 0, superseded = 0, ambiguous = 0, written = 0
    public init() {}
}

/// Gửi kiện đã chuyển sẵn lên cloud (`POST /v1/import/mapped`) theo lô (26/11/2026: nạp Salework từ điện thoại, không cần máy tính).
/// Kiện gửi trước (lô 100); danh sách mã đơn (để cloud thay dòng chờ cũ chưa có mã vận đơn) gửi SAU CÙNG, khi mọi kiện đã lên.
/// Lô lỗi thì dừng ngay và báo đã gửi được bao nhiêu: nạp lại cả file sau đó vẫn an toàn (cloud cập nhật, không nhân đôi).
public enum CloudImportUpload {
    public static let parcelBatch = 100
    public static let orderIdBatch = 500

    public struct Failure: Error {
        public let sentParcels: Int
        public let message: String
    }

    /// - Parameter progress: (số kiện đã gửi, tổng số kiện) sau mỗi lô.
    public static func salework(api: CloudAPI, parcels: [[String: Any]], orderIds: [String],
                                progress: (Int, Int) -> Void) async throws -> CloudImportTotals {
        var totals = CloudImportTotals()
        var sent = 0
        let signature = "phone-" + String(Int(Date().timeIntervalSince1970))
        func send(_ batch: [[String: Any]], _ ids: [String]) async throws {
            do {
                let r = try await api.importMapped(source: "salework", parcels: batch, orderIds: ids, signature: signature)
                totals.added += r.added; totals.updated += r.updated; totals.superseded += r.superseded
                totals.ambiguous += r.ambiguous; totals.written += r.written
            } catch let e as CloudAPIError {
                throw Failure(sentParcels: sent, message: e.message)
            } catch {
                throw Failure(sentParcels: sent, message: "Không gửi được lên cloud.")
            }
        }
        var i = 0
        while i < parcels.count {
            let batch = Array(parcels[i..<min(i + parcelBatch, parcels.count)])
            try await send(batch, [])
            sent += batch.count
            i += batch.count
            progress(sent, parcels.count)
        }
        var j = 0
        while j < orderIds.count {
            let ids = Array(orderIds[j..<min(j + orderIdBatch, orderIds.count)])
            try await send([], ids)
            j += ids.count
        }
        if parcels.isEmpty { progress(0, 0) }
        return totals
    }
}

extension CloudAPI {
    /// Một lô nạp: kiện đã chuyển sẵn (KHÔNG có dữ liệu cá nhân người mua) + mã đơn có trong file.
    public func importMapped(source: String, parcels: [[String: Any]], orderIds: [String], signature: String) async throws -> CloudImportTotals {
        let r = try await postJSON("/import/mapped", body: ["source": source, "parcels": parcels, "orderIds": orderIds, "signature": signature])
        var t = CloudImportTotals()
        t.added = Self.int(r["added"]); t.updated = Self.int(r["updated"]); t.superseded = Self.int(r["superseded"])
        t.ambiguous = (r["ambiguous"] as? [Any])?.count ?? 0; t.written = Self.int(r["written"])
        return t
    }
}
