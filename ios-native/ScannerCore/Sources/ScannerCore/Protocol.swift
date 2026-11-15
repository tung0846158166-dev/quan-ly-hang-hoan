import Foundation

public enum ScanKind: String, Codable, CaseIterable {
    case returnScan   // quét hoàn kho  -> type "barcode"
    case newOrder     // tạo đơn mới    -> type "new_order_scan"
    case cancelOrder  // tạo đơn hủy    -> type "new_order_scan" + cancelled
}

public struct PcAck: Equatable {
    public var requestId: String
    public var code: String
    public var outcome: String
    public var ackStage: String?
    public var photoSaved: Bool?
    public var captureId: String?
    public var orderNum: String?
    public var extra: String?
}

public enum PcMessage: Equatable {
    case hello
    case heartbeat
    case ack(PcAck)
    case manifest([String])
    case snapshotChunk(SnapshotChunk)
    case other
}

public enum SyncProtocol {
    public static func topic(room: String) -> String {
        let allowed = Set("abcdefghijklmnopqrstuvwxyz0123456789_-")
        let clean = String(room.lowercased().filter { allowed.contains($0) })
        return "agy_shopee_" + clean
    }

    public static func normalizeRoom(_ raw: String) -> String {
        let t = raw.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
        return t.isEmpty ? "SHP-2026" : t
    }

    public static func json(_ obj: [String: Any]) -> String {
        guard JSONSerialization.isValidJSONObject(obj),
              let data = try? JSONSerialization.data(withJSONObject: obj, options: [.withoutEscapingSlashes]),
              let s = String(data: data, encoding: .utf8) else { return "{}" }
        return s
    }

    /// Bản tin quét (không kèm ảnh; ảnh đi riêng bằng photo_chunk).
    public static func scanPayload(kind: ScanKind, code: String, requestId: String, captureId: String,
                                   captureTime: Int, time: Int, room: String, note: String = "") -> [String: Any] {
        var p: [String: Any] = [
            "code": code, "requestId": requestId, "captureId": captureId,
            "captureTime": captureTime, "time": time, "room": room
        ]
        switch kind {
        case .returnScan:
            p["type"] = "barcode"
        case .newOrder, .cancelOrder:
            p["type"] = "new_order_scan"
            p["track"] = code
            p["orderId"] = code
            p["platform"] = "auto"
            p["cancelled"] = (kind == .cancelOrder)
            p["carrier"] = TrackingCode.carrier(for: code)
            p["note"] = note.isEmpty ? "Đơn mới tạo từ máy quét điện thoại" : note
        }
        return p
    }

    public static func photoChunks(dataURL: String, code: String, kind: ScanKind, requestId: String, captureId: String,
                                   captureTime: Int, room: String, now: Int, photoId: String,
                                   chunkSize: Int = 2400) -> [[String: Any]] {
        let chars = Array(dataURL)
        let total = max(1, Int((Double(chars.count) / Double(chunkSize)).rounded(.up)))
        var out: [[String: Any]] = []
        for i in 0..<total {
            let start = i * chunkSize
            let end = min(chars.count, start + chunkSize)
            out.append([
                "type": "photo_chunk", "captureId": captureId, "captureTime": captureTime,
                "requestId": requestId, "kind": kind == .returnScan ? "barcode" : "new_order_scan",
                "photoOnly": false, "bypassScan": false, "photoId": photoId, "code": code,
                "idx": i, "total": total, "chunk": String(chars[start..<end]), "room": room, "time": now + i
            ])
        }
        return out
    }

    public static func snapshotRequest(room: String, now: Int) -> String {
        json(["type": "snapshot_request", "room": room, "time": now])
    }

    public static func inboxPush(code: String, room: String, requestId: String) -> String {
        json(["type": "inbox_push", "code": code, "requestId": requestId, "room": room, "time": Int(Date().timeIntervalSince1970 * 1000)])
    }

    /// `sseData` là dòng "data:" của ntfy: {"event":"message","message":"<json>"}
    public static func parseIncoming(sseData: String, room: String) -> PcMessage? {
        guard let outer = (try? JSONSerialization.jsonObject(with: Data(sseData.utf8))) as? [String: Any],
              let message = outer["message"] as? String else { return nil }
        return parseMessage(message, room: room)
    }

    public static func parseMessage(_ message: String, room: String) -> PcMessage? {
        guard let d = (try? JSONSerialization.jsonObject(with: Data(message.utf8))) as? [String: Any],
              (d["room"] as? String) == room, let type = d["type"] as? String else { return nil }
        switch type {
        case "pc_hello": return .hello
        case "pc_heartbeat": return .heartbeat
        case "manifest_sync":
            let codes = (d["knownCodes"] as? [Any])?.compactMap { $0 as? String } ?? []
            return .manifest(codes.map { TrackingCode.clean($0) })
        case "snapshot_chunk":
            guard let sid = d["snapshotId"] as? String, let idx = d["idx"] as? Int, let total = d["total"] as? Int,
                  let chunk = d["chunk"] as? String else { return .other }
            return .snapshotChunk(SnapshotChunk(snapshotId: sid, idx: idx, total: total, chunk: chunk))
        case "pc_ack":
            guard let rid = d["requestId"] as? String, let code = d["code"] as? String,
                  let outcome = d["outcome"] as? String else { return .other }
            var order: String? = nil
            if let n = d["orderNum"] as? Int { order = String(n) } else if let s = d["orderNum"] as? String { order = s }
            return .ack(PcAck(requestId: rid, code: TrackingCode.clean(code), outcome: outcome,
                              ackStage: d["ackStage"] as? String, photoSaved: d["photoSaved"] as? Bool,
                              captureId: d["captureId"] as? String, orderNum: order, extra: d["extra"] as? String))
        default: return .other
        }
    }
}
