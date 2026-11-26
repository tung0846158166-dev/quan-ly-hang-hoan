using System.Text.RegularExpressions;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Core.Import;

/// <summary>
/// Nạp một kiện từ file vào đơn CÓ SẴN: applyShopeeDbMatchToOrder, fillTracklessFromSalework, resolveOrderShipDate của app cũ
/// (phần dùng cho file TikTok/Salework). Trường do kho nhập tay (trạng thái nhận, ngày nhận, ảnh, ghi chú của chủ shop,
/// theo dõi) KHÔNG bao giờ bị ghi đè.
/// </summary>
public static class ParcelMerge
{
    public const string PlatformTikTok = "TikTok";

    /// <summary>Nhãn sàn ngắn đã chốt: "TikTok Shop" -> "TikTok".</summary>
    public static string NormalizePlatform(string? platform)
    {
        var t = (platform ?? "").Trim();
        return t.Equals("TikTok Shop", StringComparison.OrdinalIgnoreCase) || t.Equals("TikTok", StringComparison.OrdinalIgnoreCase) ? PlatformTikTok : platform ?? "";
    }

    private static List<string> CodesOf(Order o) =>
        new[] { o.Track, o.AltTrack, o.ForwardTrack, o.ReturnTrack }.Concat(o.TrackingCodes)
            .Select(c => LegacyText.CleanTrackingCode(c)).Where(c => c.Length > 0).Distinct().ToList();

    /// <summary>Kiện nhập dưới dạng Order (để so khớp, tạo dòng mới).</summary>
    public static Order ToOrder(ImportedParcel p)
    {
        var o = new Order
        {
            ShipDate = p.ShipDate, ReturnDate = p.ReturnDate, RecvDate = p.RecvDate, Platform = NormalizePlatform(p.Platform), OrderType = p.OrderType,
            IsPartialReturn = p.IsPartialReturn, PlatformStatus = p.PlatformStatus, ReturnedAt = p.ReturnedAt, PreOrder = p.PreOrder,
            OrderId = p.OrderId, ReturnOrderId = p.ReturnOrderId, ForwardTrack = p.ForwardTrack, ReturnTrack = p.ReturnTrack,
            TrackingCodes = new(p.TrackingCodes), Track = p.Track, AltTrack = p.AltTrack, Carrier = p.Carrier, Price = p.Price,
            Status = p.Status, Note = p.Note, BuyerNote = p.BuyerNote,
            Items = p.Items.Select(i => new OrderItem
            {
                Product = i.Product, Color = i.Color, Size = i.Size, Qty = i.Qty, ReturnedQty = i.ReturnedQty, Sku = i.SellerSku, SkuId = i.SkuId,
                ImportPrices = i.ImportPrices is null ? null : new(i.ImportPrices),
            }).ToList(),
            Import = new ImportMeta
            {
                PriceModes = new(p.PriceModes), PriceModeFlags = new(p.PriceModeFlags), ImportModeSlots = new(p.ImportModeSlots),
                ImportSelectedPriceMode = p.ImportSelectedPriceMode, PriceHasValue = p.PriceHasValue, PriceNeedsReview = p.PriceNeedsReview,
                ImportPriceBasis = p.ImportPriceBasis.Length > 0 ? p.ImportPriceBasis : null,
                ForwardCarrier = p.RawDetails is not null && p.RawDetails.TryGetValue("forwardCarrier", out var fc) ? fc : "",
                ReturnCarrier = p.RawDetails is not null && p.RawDetails.TryGetValue("returnCarrier", out var rc) ? rc : "",
                RawDetails = p.RawDetails is null ? new() : new(p.RawDetails),
            },
            Deadline = p.Deadline,
        };
        return o;
    }

    /// <summary>Dòng MỚI cho bảng chính từ kiện nhập (đã điền mặc định như app cũ).</summary>
    public static Order NewRow(ImportedParcel raw, DateTime today, Func<ImportedParcel, DateTime, ImportedParcel>? forNewRow = null)
    {
        var o = ToOrder((forNewRow ?? TikTokMapper.ForNewRow)(raw, today));
        o.Import.PriceHasValue = null; // app cũ không ghi cờ này trên dòng MỚI (chỉ khi cập nhật dòng có sẵn)
        return o;
    }

    // ------------------------------------------------------------ ngày gửi
    /// <summary>resolveOrderShipDate (tầng 1, 4, 5; tầng 2-3 dùng rawDetails/bộ nhớ tra cứu cũ nên không áp dụng cho v2).</summary>
    public static string ResolveShipDate(ImportedParcel p, int year)
    {
        if (p.ShipDate.Trim().Length > 0)
        {
            var n = LegacyText.NormalizeShortDate(p.ShipDate.Trim(), year);
            if (n.Length > 0) return n;
        }
        var mOid = Regex.Match(p.OrderId.Trim(), @"^([0-9]{2})([0-9]{2})([0-9]{2})[A-Za-z0-9]+", RegexOptions.CultureInvariant);
        if (mOid.Success)
        {
            int yy = int.Parse(mOid.Groups[1].Value), mm = int.Parse(mOid.Groups[2].Value), dd = int.Parse(mOid.Groups[3].Value);
            if (yy is >= 20 and <= 35 && mm is >= 1 and <= 12 && dd is >= 1 and <= 31)
                return $"{mOid.Groups[3].Value}/{mOid.Groups[2].Value}/20{mOid.Groups[1].Value}";
        }
        if (p.ReturnDate.Trim().Length > 0)
        {
            var n = LegacyText.NormalizeShortDate(p.ReturnDate.Trim(), year);
            if (n.Length > 0) return n;
        }
        return "";
    }

    // ------------------------------------------------------------ điền mã vào dòng cũ chưa có mã
    /// <summary>fillTracklessFromSalework</summary>
    public static void FillTrackless(Order order, ImportedParcel match)
    {
        if (match.Track.Length > 0) order.Track = match.Track;
        if (match.ForwardTrack.Length > 0) order.ForwardTrack = match.ForwardTrack;
        if (match.ReturnTrack.Length > 0) order.ReturnTrack = match.ReturnTrack;
        order.TrackingCodes = CodesOf(order).Concat(TikTokMapper.TrackingCodesOf(match)).Where(c => c.Length > 0).Distinct().ToList();
        if (match.Carrier.Length > 0 && (order.Carrier.Length == 0 || order.Carrier == "Chờ bưu cục tiếp nhận")) order.Carrier = match.Carrier;
        if (match.PlatformStatus.Length > 0) order.PlatformStatus = match.PlatformStatus;
        if (match.ReturnedAt.Length > 0) order.ReturnedAt = match.ReturnedAt;
        if (match.ReturnDate.Length > 0 && order.ReturnDate.Length == 0) order.ReturnDate = match.ReturnDate;
        // Đơn huỷ bị đặt "Không cần nhận" CHỈ vì lúc nhập chưa có mã (ShopeeMapper/TikTokMapper.ForNewRow): có mã rồi thì chờ nhận
        // (quyết định chủ dự án 26/11/2026: đơn "Huỷ sau chuẩn bị" có hàng về). Trạng thái khác (đã nhận, bị mất, ...) không đụng tới.
        if (order.Status == ReceiveStatus.NotNeeded && order.OrderType == "Đơn hủy" && order.TrackingCodes.Count > 0)
            order.Status = order.Platform == "TikTok" ? ReceiveStatus.NotYetArrived : ReceiveStatus.NotReturned;
        if (match.Items.Any(i => i.Product.Length > 0) &&
            !order.Items.Any(i => i.Product.Length > 0 && !Regex.IsMatch(i.Product, "Sản phẩm mới|Chưa rõ sản phẩm")))
            order.Items = match.Items.Select(i => new OrderItem
            {
                Product = i.Product, Color = i.Color, Size = i.Size, Qty = i.Qty, ReturnedQty = i.ReturnedQty, Sku = i.SellerSku, SkuId = i.SkuId,
                ImportPrices = i.ImportPrices is null ? null : new(i.ImportPrices),
            }).ToList();
    }

    // ------------------------------------------------------------ cập nhật đơn có sẵn
    /// <summary>applyShopeeDbMatchToOrder(order, match, forceOverwrite). Trả về true nếu có gì đổi.</summary>
    public static bool ApplyMatch(Order order, ImportedParcel match, bool force, ImportProductMode mode, int year)
    {
        var matchOrder = ToOrder(match);
        if (!ParcelMatching.IsSameReturnParcel(order, matchOrder)) return false;
        var changed = false;

        // Thông tin giá của lần nhập (không tính là "thay đổi" như app cũ).
        var im = order.Import;
        if (match.ImportModeSlots is { } slots) im.ImportModeSlots = new(slots);
        im.ImportSelectedPriceMode = match.ImportSelectedPriceMode;
        if (match.PriceNeedsReview is not null) im.PriceNeedsReview = match.PriceNeedsReview;
        im.PriceHasValue = match.PriceHasValue;

        var codes = CodesOf(order).Concat(CodesOf(matchOrder)).Distinct().ToList();
        if (!order.TrackingCodes.SequenceEqual(codes)) { order.TrackingCodes = codes; changed = true; }

        bool Set(string current, string value, Action<string> assign)
        {
            if (current == value) return false;
            assign(value);
            return true;
        }

        if (match.ReturnOrderId.Length > 0 && order.ReturnOrderId.Length == 0) { order.ReturnOrderId = match.ReturnOrderId; changed = true; }

        var platform = NormalizePlatform(match.Platform);
        if (platform.Length > 0 && (force || order.Platform.Length == 0 || order.Platform == "Shopee"))
            changed |= Set(order.Platform, platform, v => order.Platform = v);

        if (match.OrderType.Length > 0 && (force || order.OrderType.Length == 0)) changed |= Set(order.OrderType, match.OrderType, v => order.OrderType = v);

        if (match.PreOrder.Length > 0)
        {
            if (match.PreOrder == "Hàng đặt trước") changed |= Set(order.PreOrder, "Hàng đặt trước", v => order.PreOrder = v);
            else if (order.PreOrder.Length == 0) { order.PreOrder = match.PreOrder; changed = true; }
        }

        if (match.PlatformStatus.Length > 0 && (force || order.PlatformStatus.Length == 0)) changed |= Set(order.PlatformStatus, match.PlatformStatus, v => order.PlatformStatus = v);
        if (match.OrderId.Length > 0 && (force || order.OrderId.Length == 0)) changed |= Set(order.OrderId, match.OrderId, v => order.OrderId = v);

        var ship = ResolveShipDate(match, year);
        if (ship.Length > 0 && (force || order.ShipDate.Length == 0)) changed |= Set(order.ShipDate, ship, v => order.ShipDate = v);
        if (match.ReturnDate.Length > 0 && (force || order.ReturnDate.Length == 0)) changed |= Set(order.ReturnDate, match.ReturnDate, v => order.ReturnDate = v);

        if (order.IsPartialReturn != match.IsPartialReturn) { order.IsPartialReturn = match.IsPartialReturn; changed = true; }

        if (match.PriceModes.Count > 0 || match.PriceModeFlags.Count > 0)
        {
            var merged = new List<KeyValuePair<string, string>>();
            foreach (var kv in im.PriceModes) merged.Add(new(kv.Key, kv.Value.ToString()));
            foreach (var kv in im.PriceModeFlags) merged.Add(new(kv.Key, kv.Value ? "true" : "false"));
            var before = string.Join(",", merged.Select(x => x.Key + "=" + x.Value));
            var newModes = new Dictionary<string, long>(im.PriceModes);
            foreach (var kv in match.PriceModes) newModes[kv.Key] = kv.Value;
            var newFlags = new Dictionary<string, bool>(im.PriceModeFlags);
            foreach (var kv in match.PriceModeFlags) newFlags[kv.Key] = kv.Value;
            var after = string.Join(",", newModes.Select(x => x.Key + "=" + x.Value).Concat(newFlags.Select(x => x.Key + "=" + (x.Value ? "true" : "false"))));
            if (before != after) { im.PriceModes = newModes; im.PriceModeFlags = newFlags; changed = true; }
        }

        if (match.PriceHasValue && (force || order.Price == 0) && order.Price != match.Price) { order.Price = match.Price; changed = true; }

        if (match.Carrier.Length > 0 && (force || order.Carrier.Length == 0 || order.Carrier == "SPX Express")) changed |= Set(order.Carrier, match.Carrier, v => order.Carrier = v);
        if (match.Track.Length > 0 && (force || order.Track.Length == 0)) changed |= Set(order.Track, match.Track, v => order.Track = v);
        if (match.AltTrack.Length > 0 && (force || order.AltTrack.Length == 0)) changed |= Set(order.AltTrack, match.AltTrack, v => order.AltTrack = v);
        if (match.ForwardTrack.Length > 0 && (force || order.ForwardTrack.Length == 0)) changed |= Set(order.ForwardTrack, match.ForwardTrack, v => order.ForwardTrack = v);
        if (match.ReturnTrack.Length > 0 && (force || order.ReturnTrack.Length == 0)) changed |= Set(order.ReturnTrack, match.ReturnTrack, v => order.ReturnTrack = v);
        if (match.Deadline.Length > 0 && (force || order.Deadline.Length == 0)) changed |= Set(order.Deadline, match.Deadline, v => order.Deadline = v);
        if (match.ImportPriceBasis.Length > 0) im.ImportPriceBasis = match.ImportPriceBasis;
        if (matchOrder.Import.ForwardCarrier.Length > 0 && (force || im.ForwardCarrier.Length == 0)) changed |= Set(im.ForwardCarrier, matchOrder.Import.ForwardCarrier, v => im.ForwardCarrier = v);
        if (matchOrder.Import.ReturnCarrier.Length > 0 && (force || im.ReturnCarrier.Length == 0)) changed |= Set(im.ReturnCarrier, matchOrder.Import.ReturnCarrier, v => im.ReturnCarrier = v);
        if (match.RawDetails is { Count: > 0 })
        {
            var before = string.Join("\u0001", im.RawDetails.Select(x => x.Key + "=" + x.Value));
            var mergedRaw = new Dictionary<string, string>(im.RawDetails);
            foreach (var kv in match.RawDetails) mergedRaw[kv.Key] = kv.Value;
            if (string.Join("\u0001", mergedRaw.Select(x => x.Key + "=" + x.Value)) != before) { im.RawDetails = mergedRaw; changed = true; }
        }
        if (match.Note.Length > 0 && (force || order.Note.Length == 0)) changed |= Set(order.Note, match.Note, v => order.Note = v);
        if (match.BuyerNote.Length > 0 && (force || order.BuyerNote.Length == 0)) changed |= Set(order.BuyerNote, match.BuyerNote, v => order.BuyerNote = v);

        if (matchOrder.Items.Count > 0)
        {
            var curFirst = order.Items.Count > 0 ? order.Items[0] : null;
            var isCurBlank = curFirst is null || (curFirst.Product.Length == 0 && curFirst.Color.Length == 0 && curFirst.Size.Length == 0);
            if (force || isCurBlank || matchOrder.Items.Count > order.Items.Count)
            {
                var before = ItemsKey(order.Items);
                order.Items = ItemMerge.Merge(order.Items, matchOrder.Items, mode);
                foreach (var it in order.Items)
                {
                    if (it.Size.Length > 0 && LegacyText.IsReturnReasonText(it.Size)) { if (order.Note.Length == 0) order.Note = it.Size; it.Size = ""; }
                    if (it.Color.Length > 0 && LegacyText.IsReturnReasonText(it.Color)) { if (order.Note.Length == 0) order.Note = it.Color; it.Color = ""; }
                    if (it.Size.Length > 0 && it.Color.Length == 0 && LegacyText.IsKnownColorName(it.Size)) { it.Color = it.Size; it.Size = ""; }
                }
                if (ItemsKey(order.Items) != before) changed = true;
            }
            else
            {
                var first = matchOrder.Items[0];
                if (curFirst!.Product.Length == 0 && first.Product.Length > 0) { curFirst.Product = first.Product; changed = true; }
                if (curFirst.Color.Length == 0 && first.Color.Length > 0)
                {
                    if (LegacyText.IsReturnReasonText(first.Color)) { if (order.Note.Length == 0) order.Note = first.Color; }
                    else curFirst.Color = first.Color;
                    changed = true;
                }
                if (curFirst.Size.Length == 0 && first.Size.Length > 0)
                {
                    if (LegacyText.IsReturnReasonText(first.Size)) { if (order.Note.Length == 0) order.Note = first.Size; }
                    else curFirst.Size = first.Size;
                    changed = true;
                }
                if ((curFirst.Qty == 0 || curFirst.Qty == 1) && first.Qty > 0) { curFirst.Qty = first.Qty; changed = true; }
            }
        }
        return changed;
    }

    private static string ItemsKey(List<OrderItem> items) =>
        string.Join(";", items.Select(i => $"{i.Product}|{i.Color}|{i.Size}|{i.Qty}|{i.Sku}|{i.SkuId}|" +
            (i.ImportPrices is null ? "-" : string.Join(",", i.ImportPrices.Select(p => p.Key + "=" + p.Value)))));
}
