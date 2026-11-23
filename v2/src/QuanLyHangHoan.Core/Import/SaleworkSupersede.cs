using System.Text.RegularExpressions;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Core.Import;

public sealed class SupersedeResult
{
    /// <summary>Dòng chờ cũ (chưa có mã vận đơn) đã được chuyển dữ liệu kho sang dòng mới: caller xóa MỀM (không xóa hẳn).</summary>
    public List<(Order Stale, Order Keeper)> Merged { get; } = new();
    /// <summary>Dòng không gỡ được nhưng được ĐIỀN mã vận đơn còn trống.</summary>
    public List<Order> Filled { get; } = new();
    /// <summary>Dòng GIỮ đã nhận dữ liệu kho từ dòng cũ (cần lưu lại).</summary>
    public List<Order> KeepersChanged { get; } = new();
    public int Ambiguous { get; set; }
}

/// <summary>
/// supersedeStaleRowsFromSalework của app cũ: Salework "Đang chờ hoàn" là trạng thái MỚI NHẤT của đơn. Một đơn có dòng "chờ" chưa có mã vận đơn
/// nào (vd "Chờ khách gửi hàng" từ Sheet) mà Salework giờ đã có dòng mang mã vận đơn của cùng mã đơn -> dòng cũ chỉ là bản cũ của cùng yêu cầu:
/// chuyển dữ liệu kho (đã nhận, ngày nhận, ảnh, ghi chú, theo dõi) sang dòng mới. Nhiều kiện KHÁC mã vận đơn thì không đoán: giữ nguyên,
/// chỉ điền mã còn trống khi mọi dòng Salework của đơn cùng chung 1 mã giao / 1 mã hoàn. v2 KHÁC app cũ: không xóa hẳn dòng cũ (xóa mềm, khôi phục được).
/// </summary>
public static class SaleworkSupersede
{
    private static string Up(string? s) => (s ?? "").Trim().ToUpperInvariant();
    private static string Own(Order o) => LegacyText.CleanTrackingCode(o.ReturnTrack.Length > 0 ? o.ReturnTrack : o.Track.Length > 0 ? o.Track : o.ForwardTrack.Length > 0 ? o.ForwardTrack : o.AltTrack);
    public static bool IsReceived(Order o) => o.Status is "Hàng đã trả" or "Đã nhận";
    public static bool IsScanPlaceholder(Order o) =>
        Regex.IsMatch(o.Items.FirstOrDefault()?.Product ?? "", "^Chưa rõ sản phẩm") || Regex.IsMatch(o.Note ?? "", @"^Hàng đã về \(điện thoại quét");

    /// <summary>mergeWarehouseIntoKeeper (copyPlatform=false): chỉ CHUYỂN, không ghi đè dữ liệu đã có ở dòng giữ.</summary>
    public static bool MergeWarehouse(Order src, Order dst)
    {
        var changed = false;
        if (IsReceived(src) && !IsReceived(dst)) { dst.Status = src.Status; changed = true; }
        if (dst.RecvDate.Length == 0 && src.RecvDate.Length > 0) { dst.RecvDate = src.RecvDate; changed = true; }
        if (dst.PhotoId.Length == 0 && src.PhotoId.Length > 0) { dst.PhotoId = src.PhotoId; changed = true; }
        var sn = (src.UserNote ?? "").Trim();
        if (sn.Length > 0 && !(dst.UserNote ?? "").Contains(sn)) { dst.UserNote = (dst.UserNote ?? "").Trim().Length > 0 ? dst.UserNote + " · " + sn : sn; changed = true; }
        if (src.IsWatchlist && !dst.IsWatchlist) { dst.IsWatchlist = true; if (src.WatchlistReason.Length > 0 && dst.WatchlistReason.Length == 0) dst.WatchlistReason = src.WatchlistReason; changed = true; }
        return changed;
    }

    public static SupersedeResult Apply(IReadOnlyList<Order> main, ISet<string> orderIds)
    {
        var res = new SupersedeResult();
        if (orderIds.Count == 0) return res;
        var by = new Dictionary<string, List<Order>>(); var order = new List<string>();
        foreach (var o in main)
        {
            var k = Up(o.OrderId);
            if (k.Length == 0 || !orderIds.Contains(k)) continue;
            if (!by.ContainsKey(k)) { by[k] = new(); order.Add(k); }
            by[k].Add(o);
        }
        foreach (var k in order)
        {
            var idxs = by[k];
            var tracked = idxs.Where(o => Own(o).Length > 0).ToList();
            var bare = idxs.Where(o => Own(o).Length == 0 && !IsScanPlaceholder(o)).ToList();
            if (tracked.Count == 0 || bare.Count == 0) continue;
            if (tracked.Select(Own).Distinct().Count() != 1)
            {
                res.Ambiguous++;
                FillOnly(bare, tracked, res);
                continue;
            }
            var keep = tracked.FirstOrDefault(IsReceived) ?? tracked[0];
            foreach (var stale in bare)
            {
                if (MergeWarehouse(stale, keep) && !res.KeepersChanged.Contains(keep)) res.KeepersChanged.Add(keep);
                res.Merged.Add((stale, keep));
            }
        }
        return res;
    }

    private static void FillOnly(List<Order> bare, List<Order> tracked, SupersedeResult res)
    {
        string One(Func<Order, string> f)
        {
            var s = tracked.Select(o => LegacyText.CleanTrackingCode(f(o))).Where(c => c.Length > 0).Distinct().ToList();
            return s.Count == 1 ? s[0] : "";
        }
        var fwd = One(o => o.ForwardTrack); var ret = One(o => o.ReturnTrack);
        foreach (var o in bare)
        {
            var ch = false;
            if (fwd.Length > 0 && o.ForwardTrack.Length == 0) { o.ForwardTrack = fwd; ch = true; }
            if (ret.Length > 0 && o.ReturnTrack.Length == 0) { o.ReturnTrack = ret; ch = true; }
            if (!ch) continue;
            o.TrackingCodes = o.TrackingCodes.Concat(new[] { fwd, ret }).Where(c => c.Length > 0).Distinct().ToList();
            res.Filled.Add(o);
        }
    }
}
