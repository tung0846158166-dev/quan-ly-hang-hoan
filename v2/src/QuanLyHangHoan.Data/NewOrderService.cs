using System.Globalization;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Core.Tracking;
using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.Data;

/// <summary>Đầu vào tạo đơn mới. <see cref="Origin"/>: "manual" (nhập tay trên máy tính) hoặc "phone" (quét từ điện thoại).</summary>
public sealed record NewOrderInput(
    string Track, string OrderId = "", string Platform = "", string Carrier = "",
    string Product = "", string Color = "", string Size = "", int Qty = 1, string Note = "", string Origin = "manual");

/// <param name="Outcome">success | duplicate | invalid</param>
public sealed record NewOrderResult(bool Ok, string Outcome, string Message, string Code, string Platform, string OrderId);

/// <summary>
/// Tạo MỘT kiện mới thẳng vào bảng chính (đánh dấu <see cref="NewOrderMarker"/>). Dùng chung cho nhập tay và điện thoại.
/// Không tạo lại nếu mã đã có ở bất kỳ đâu (kể cả kho lưu trữ / đã xóa), không "hồi sinh" đơn đã xóa. Ghi một dòng nhật ký.
/// </summary>
public static class NewOrderService
{
    public static NewOrderResult Create(string dbPath, NewOrderInput input, DateTime now)
    {
        var raw = input.Track ?? "";
        var code = TrackingResolver.Normalize(raw);
        if (code.Length == 0) return new NewOrderResult(false, "invalid", "Mã vận đơn trống", raw.Trim(), "", "");

        var orderId = (input.OrderId ?? "").Trim();
        var oidNorm = TrackingResolver.Normalize(orderId);
        var hit = OrderRepository.Load(dbPath).FirstOrDefault(s =>
            OwnCodes(s.Order).Contains(code) || (oidNorm.Length > 0 && TrackingResolver.Normalize(s.Order.OrderId) == oidNorm && s.Order.Track.Length == 0));
        if (hit is not null)
        {
            var where = hit.IsDeleted ? "mục Đã xóa" : hit.IsArchived ? "Kho lưu trữ" : "Bảng chính";
            return new NewOrderResult(false, "duplicate", $"ĐƠN ĐÃ CÓ Ở {where.ToUpperInvariant()}, KHÔNG TẠO LẠI", code, "", hit.Order.OrderId);
        }

        var phone = string.Equals(input.Origin, "phone", StringComparison.OrdinalIgnoreCase);
        var platformIn = (input.Platform ?? "").Trim();
        var platform = platformIn.Length > 0 && !platformIn.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? (platformIn.StartsWith("TikTok", StringComparison.OrdinalIgnoreCase) ? "TikTok" : platformIn)
            : PhoneActions.DetectPlatform(code);
        var carrierIn = (input.Carrier ?? "").Trim();
        var carrier = carrierIn.Length > 0 ? carrierIn
            : (LegacyText.DetectCarrierByTrackingCode(code) ?? (platform == "Shopee" ? "SPX Express" : "J&T Express"));
        var stamp = now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        var note = (input.Note ?? "").Trim();
        var product = (input.Product ?? "").Trim();
        var qty = input.Qty > 0 ? input.Qty : 1;

        var created = new Order
        {
            OrderId = orderId.Length > 0 ? orderId : code, Track = code, Platform = platform, Carrier = carrier,
            OrderType = "Trả hàng/Hoàn tiền", PlatformStatus = "Đang chuyển hoàn", PreOrder = "Đơn thường",
            Status = ReceiveStatus.NotReturned, ShipDate = stamp, ReturnDate = stamp, ImportedAt = stamp,
            ImportWeek = NewOrderMarker.Week,
            Note = note.Length > 0 ? Clip(note, 300) : (phone ? NewOrderMarker.LegacyPhoneNote : "Đơn tạo tay trên máy tính"),
            Items =
            {
                new OrderItem
                {
                    Product = product.Length > 0 ? Clip(product, 200) : (phone ? "Sản phẩm mới (Quét điện thoại)" : "Sản phẩm mới (nhập tay)"),
                    Color = Clip((input.Color ?? "").Trim(), 60), Size = Clip((input.Size ?? "").Trim(), 60), Qty = qty,
                },
            },
        };

        using (var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            conn.Open();
            using var tx = conn.BeginTransaction();
            OrderStore.Insert(conn, tx, created, (phone ? "phone|" : "manual|") + created.Id);
            OrderStore.AppendAudit(conn, tx, phone ? "phone_new_order" : "manual_new_order", now,
                phone ? $"Điện thoại quét tạo đơn mới (MVĐ: {code}, sàn: {platform}) vào bảng chính"
                      : $"Tạo đơn mới bằng tay (MVĐ: {code}, sàn: {platform}) vào bảng chính");
            tx.Commit();
        }
        return new NewOrderResult(true, "success", $"ĐÃ TẠO ĐƠN MỚI Ở BẢNG CHÍNH ({platform})", code, platform, created.OrderId);
    }

    private static HashSet<string> OwnCodes(Order o)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in new[] { o.Track, o.ReturnTrack, o.AltTrack }.Concat(o.TrackingCodes).Concat(o.ScanAliases))
            if (TrackingResolver.Normalize(v ?? "") is { Length: > 0 } n) set.Add(n);
        if (set.Count == 0 && TrackingResolver.Normalize(o.OrderId ?? "") is { Length: > 0 } oid) set.Add(oid);
        return set;
    }

    private static string Clip(string s, int n) => s.Length <= n ? s : s[..n];
}
