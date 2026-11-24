using System.Text.Json;
using QuanLyHangHoan.Core.Tracking;

namespace QuanLyHangHoan.Data.Sync;

public sealed record ScanRequest(string Room, string RequestId, string Code, string CaptureId);

/// <summary>
/// Định dạng bản tin tương thích trang quét điện thoại của app cũ (mobile_scanner.html):
///  - Điện thoại gửi:  {"type":"barcode","room":..,"requestId":..,"code":..,"captureId":..,...}
///  - PC trả xác nhận: {"type":"pc_ack","room":..,"requestId":..,"code":..,"outcome":"success|duplicate|not_found|ambiguous|error","extra":..,"ackStage":"scan"}
/// Bản tin không liên quan / thiếu requestId hoặc code bị bỏ qua (không bao giờ coi là lệnh nhận kiện).
/// </summary>
public static class SyncProtocol
{
    public static bool TryParseScan(string json, out ScanRequest request)
    {
        request = new ScanRequest("", "", "", "");
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return false;
            if (Str(r, "type") != "barcode") return false;

            var requestId = Str(r, "requestId").Trim();
            var code = TrackingResolver.Normalize(Str(r, "code"));
            if (requestId.Length == 0 || code.Length == 0) return false;

            request = new ScanRequest(Str(r, "room"), requestId, code, Str(r, "captureId"));
            return true;
        }
        catch (JsonException) { return false; }
    }

    public static string BuildAck(ScanRequest req, ScanResult result)
    {
        var outcome = result.Outcome switch
        {
            ScanOutcome.Received => "success",
            ScanOutcome.AlreadyReceived => "duplicate",
            ScanOutcome.NotFound => "not_found",
            ScanOutcome.Ambiguous => "ambiguous",
            _ => "error", // NeedsManualCheck, Error: phone phải coi là CHƯA nhận
        };
        return JsonSerializer.Serialize(new
        {
            type = "pc_ack", room = req.Room, requestId = req.RequestId, code = req.Code,
            outcome, extra = result.Message, ackStage = "scan",
            captureId = req.CaptureId.Length > 0 ? req.CaptureId : null,
        });
    }

    // ---- Ảnh từ điện thoại (bản tin đến từ mạng công cộng: kiểm tra chặt) ----
    public const int MaxPhotoBytes = 4 * 1024 * 1024;
    public const int MaxChunks = 2500;          // 2.400 ký tự/mảnh => ~6 triệu ký tự, đủ cho ảnh <= 4 MB dạng base64
    private static readonly string[] AllowedMimes = { "image/jpeg", "image/png", "image/webp" };

    public sealed record PhotoRequest(string Room, string RequestId, string CaptureId, string Code, string DataUri)
    {
        /// <summary>Khóa chống lặp của gói ảnh.</summary>
        public string IdempotencyKey => "photo:" + (CaptureId.Length > 0 ? CaptureId : RequestId);
    }

    public sealed record ChunkRequest(string Room, string RequestId, string CaptureId, string Code, string PhotoId, int Index, int Total, string Chunk);

    public static bool TryParsePhoto(string json, out PhotoRequest req)
    {
        req = new PhotoRequest("", "", "", "", "");
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || Str(r, "type") != "photo") return false;
            var p = new PhotoRequest(Str(r, "room"), Str(r, "requestId").Trim(), Str(r, "captureId").Trim(),
                TrackingResolver.Normalize(Str(r, "code")), Str(r, "photo"));
            if (p.Code.Length == 0 || (p.CaptureId.Length == 0 && p.RequestId.Length == 0)) return false;
            req = p;
            return true;
        }
        catch (JsonException) { return false; }
    }

    public static bool TryParseChunk(string json, out ChunkRequest req)
    {
        req = new ChunkRequest("", "", "", "", "", 0, 0, "");
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || Str(r, "type") != "photo_chunk") return false;
            if (!r.TryGetProperty("idx", out var i) || !i.TryGetInt32(out var idx)) return false;
            if (!r.TryGetProperty("total", out var t) || !t.TryGetInt32(out var total)) return false;
            var c = new ChunkRequest(Str(r, "room"), Str(r, "requestId").Trim(), Str(r, "captureId").Trim(),
                TrackingResolver.Normalize(Str(r, "code")), Str(r, "photoId").Trim(), idx, total, Str(r, "chunk"));
            if (c.Code.Length == 0 || c.PhotoId.Length == 0 || (c.CaptureId.Length == 0 && c.RequestId.Length == 0)) return false;
            if (total < 1 || total > MaxChunks || idx < 0 || idx >= total) return false;
            req = c;
            return true;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>Giải mã data-URI ảnh: chỉ jpeg/png/webp, đúng chữ ký tệp, không rỗng, không vượt MaxPhotoBytes.</summary>
    public static bool TryDecodeImage(string dataUri, out string mime, out byte[] bytes, out string error)
    {
        mime = ""; bytes = Array.Empty<byte>(); error = "";
        if (string.IsNullOrEmpty(dataUri) || !dataUri.StartsWith("data:", StringComparison.Ordinal)) { error = "Không phải data-URI ảnh."; return false; }
        var comma = dataUri.IndexOf(',');
        if (comma < 0 || !dataUri[5..comma].EndsWith(";base64", StringComparison.Ordinal)) { error = "Ảnh không ở dạng base64."; return false; }
        mime = dataUri[5..(comma - 7)].ToLowerInvariant();
        if (!AllowedMimes.Contains(mime)) { error = "Chỉ nhận ảnh jpeg/png/webp."; return false; }
        if ((dataUri.Length - comma) / 4 * 3 > MaxPhotoBytes + 3) { error = "Ảnh quá lớn."; return false; }
        try { bytes = Convert.FromBase64String(dataUri[(comma + 1)..]); }
        catch (FormatException) { error = "Base64 hỏng."; return false; }
        if (bytes.Length == 0) { error = "Ảnh rỗng."; return false; }
        if (bytes.Length > MaxPhotoBytes) { error = "Ảnh quá lớn."; return false; }
        if (!HasImageSignature(bytes, mime)) { error = "Nội dung không đúng định dạng ảnh."; return false; }
        return true;
    }

    private static bool HasImageSignature(byte[] b, string mime) => mime switch
    {
        "image/jpeg" => b.Length > 2 && b[0] == 0xFF && b[1] == 0xD8,
        "image/png" => b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47,
        "image/webp" => b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P',
        _ => false,
    };

    public static string BuildPhotoAck(string room, string requestId, string captureId, string code, string outcome, string message) =>
        JsonSerializer.Serialize(new
        {
            type = "pc_ack", room, requestId, code, outcome, extra = message, ackStage = "photo",
            // Điện thoại chỉ coi ảnh là đã lưu khi thấy photoSaved=true (quy tắc 6): chỉ báo true khi PC thực sự đã lưu ảnh bền.
            photoSaved = outcome == "success",
            captureId = captureId.Length > 0 ? captureId : null,
        });

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
