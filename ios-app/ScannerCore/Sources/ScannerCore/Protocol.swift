import Foundation

public enum ScanKind: String, Codable, CaseIterable, Sendable {
    case returnScan   // quét hoàn kho  -> type "barcode"
    case newOrder     // tạo đơn mới    -> type "new_order_scan" (đi thẳng vào bảng chính ở PC v2)
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
    /// v2: kiện thuộc loại "Đơn hủy" trên hệ thống (PC cũ không gửi trường này).
    public var cancelled: Bool?

    public init(requestId: String, code: String, outcome: String, ackStage: String? = nil,
                photoSaved: Bool? = nil, captureId: String? = nil, orderNum: String? = nil, extra: String? = nil, cancelled: Bool? = nil) {
        self.requestId = requestId
        self.code = code
        self.outcome = outcome
        self.ackStage = ackStage
        self.photoSaved = photoSaved
        self.captureId = captureId
        self.orderNum = orderNum
        self.extra = extra
        self.cancelled = cancelled
    }
}

public enum PcMessage: Equatable {
    case hello
    case heartbeat
    case ack(PcAck)
    case manifest([String])
    case snapshotChunk(SnapshotChunk)
    case snapshotDelta(SnapshotChunk)
    case watchSync(time: Int, items: [WatchItem])
    case pong(id: String)
    case other
}

/// Một đơn đang theo dõi trên PC kèm ghi chú/lý do theo dõi (PC gửi mỗi khi danh sách theo dõi hoặc ghi chú đổi).
public struct WatchItem: Equatable {
    public var c: String
    public var o: String
    public var r: String
    public init(c: String, o: String, r: String) { self.c = c; self.o = o; self.r = r }
}

public enum SyncProtocol {
    public static func topic(room: String) -> String {
        let allowed = Set("abcdefghijklmnopqrstuvwxyz0123456789_-")
        let clean = String(room.lowercased().filter { allowed.contains($0) })
        return "agy_shopee_" + clean
    }

    /// v2: không còn mã phòng mặc định ngắn dễ đoán. Rỗng = chưa cài đặt.
    public static func normalizeRoom(_ raw: String) -> String {
        raw.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
    }

    /// Mã phòng của PC v2 là chuỗi ngẫu nhiên dài (>= 16 ký tự); chấp nhận từ 12 ký tự để không chặn nhầm.
    public static func isValidRoom(_ raw: String) -> Bool {
        normalizeRoom(raw).count >= 12
    }

    public static func json(_ obj: [String: Any]) -> String {
        guard JSONSerialization.isValidJSONObject(obj),
              let data = try? JSONSerialization.data(withJSONObject: obj, options: [.withoutEscapingSlashes]),
              let s = String(data: data, encoding: .utf8) else { return "{}" }
        return s
    }

    /// Bản tin quét (không kèm ảnh; ảnh lưu cục bộ trong chi tiết đơn trên iPhone, không gửi sang PC).
    public static func scanPayload(kind: ScanKind, code: String, requestId: String, captureId: String,
                                   captureTime: Int, time: Int, room: String, note: String = "") -> [String: Any] {
        var p: [String: Any] = [
            "code": code, "requestId": requestId, "captureId": captureId,
            "captureTime": captureTime, "time": time, "room": room
        ]
        switch kind {
        case .returnScan:
            p["type"] = "barcode"
        case .newOrder:
            p["type"] = "new_order_scan"
            p["track"] = code
            p["orderId"] = code
            p["platform"] = "auto"
            p["cancelled"] = false
            p["carrier"] = TrackingCode.carrier(for: code)
            p["note"] = note.isEmpty ? "Đơn mới tạo từ máy quét điện thoại" : note
        }
        return p
    }

    public static func snapshotRequest(room: String, now: Int) -> String {
        json(["type": "snapshot_request", "room": room, "time": now])
    }

    /// Ghi chú đơn từ điện thoại: PC đánh dấu theo dõi đơn đó và lưu ghi chú làm lý do theo dõi.
    public static func orderNote(code: String, orderId: String, note: String, watch: Bool = true, room: String, requestId: String) -> String {
        json(["type": "order_note", "code": code, "orderId": orderId, "note": note, "watch": watch, "requestId": requestId, "room": room,
              "time": Int(Date().timeIntervalSince1970 * 1000)])
    }

    /// iPhone sửa trạng thái đơn ở bảng chính: PC v2 chỉ nhận trường `st` (trạng thái).
    public static func orderEdit(code: String, orderId: String, fields: [String: Any], room: String, requestId: String) -> String {
        json(["type": "order_edit", "code": code, "orderId": orderId, "fields": fields, "requestId": requestId, "room": room,
              "time": Int(Date().timeIntervalSince1970 * 1000)])
    }

    public static func orderDelete(code: String, orderId: String, room: String, requestId: String) -> String {
        json(["type": "order_delete", "code": code, "orderId": orderId, "requestId": requestId, "room": room,
              "time": Int(Date().timeIntervalSince1970 * 1000)])
    }

    /// Đánh dấu đã nhận / chưa nhận nhiều đơn trong bảng đơn hủy (theo id trên PC).
    public static func cancelReceive(ids: [String], received: Bool, room: String, requestId: String) -> String {
        json(["type": "cancel_receive", "ids": ids, "received": received, "requestId": requestId, "room": room, "time": Int(Date().timeIntervalSince1970 * 1000)])
    }

    /// `sseData` là dòng "data:" của ntfy: {"event":"message","message":"<json>"}
    public static func parseIncoming(sseData: String, room: String) -> PcMessage? {
        guard let outer = (try? JSONSerialization.jsonObject(with: Data(sseData.utf8))) as? [String: Any],
              let message = outer["message"] as? String else { return nil }
        return parseMessage(message, room: room)
    }

    public static func parseMessage(_ message: String, room: String) -> PcMessage? {
        guard !room.isEmpty,
              let d = (try? JSONSerialization.jsonObject(with: Data(message.utf8))) as? [String: Any],
              (d["room"] as? String) == room, let type = d["type"] as? String else { return nil }
        switch type {
        case "pc_hello": return .hello
        case "pc_heartbeat": return .heartbeat
        case "pc_pong": return .pong(id: (d["id"] as? String) ?? "")
        case "manifest_sync":
            let codes = (d["knownCodes"] as? [Any])?.compactMap { $0 as? String } ?? []
            return .manifest(codes.map { TrackingCode.clean($0) })
        case "watch_sync":
            let list = (d["items"] as? [[String: Any]]) ?? []
            let items = list.map { WatchItem(c: ($0["c"] as? String) ?? "", o: ($0["o"] as? String) ?? "", r: ($0["r"] as? String) ?? "") }
            return .watchSync(time: (d["time"] as? NSNumber)?.intValue ?? 0, items: items)
        case "snapshot_chunk":
            guard let sid = d["snapshotId"] as? String, let idx = d["idx"] as? Int, let total = d["total"] as? Int,
                  let chunk = d["chunk"] as? String else { return .other }
            return .snapshotChunk(SnapshotChunk(snapshotId: sid, idx: idx, total: total, chunk: chunk))
        case "snapshot_delta":
            guard let did = d["deltaId"] as? String, let idx = d["idx"] as? Int, let total = d["total"] as? Int,
                  let chunk = d["chunk"] as? String else { return .other }
            return .snapshotDelta(SnapshotChunk(snapshotId: did, idx: idx, total: total, chunk: chunk))
        case "pc_ack":
            guard let rid = d["requestId"] as? String, let code = d["code"] as? String,
                  let outcome = d["outcome"] as? String else { return .other }
            var order: String? = nil
            if let n = d["orderNum"] as? Int { order = String(n) } else if let s = d["orderNum"] as? String { order = s }
            return .ack(PcAck(requestId: rid, code: TrackingCode.clean(code), outcome: outcome,
                              ackStage: d["ackStage"] as? String, photoSaved: d["photoSaved"] as? Bool,
                              captureId: d["captureId"] as? String, orderNum: order, extra: d["extra"] as? String,
                              cancelled: d["cancelled"] as? Bool))
        default: return .other
        }
    }
}
