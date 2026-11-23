using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Core.Import;

/// <summary>
/// So khớp "cùng một kiện hoàn" khi nhập: chuyển NGUYÊN VĂN isSameReturnParcel / findExistingReturnParcelIndex /
/// findTracklessSameOrderIndex của app cũ (đối chiếu 1.500 tình huống ngẫu nhiên với mã JS cũ, Golden/match.golden.json).
/// Quy tắc cốt lõi (bất biến 1-4): khác mã yêu cầu hoàn là kiện khác; khác mã vận đơn chính là kiện khác trừ khi có liên kết
/// tường minh trong trackingCodes; dòng cũ chỉ có mã GIAO được nâng cấp khi dòng mới cùng mã đơn + cùng mã giao đã có mã hoàn.
/// </summary>
public static class ParcelMatching
{
    private static string Up(string? s) => (s ?? "").Trim().ToUpperInvariant();
    private static string Clean(string? s) => LegacyText.CleanTrackingCode(s ?? "");
    private static string First(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";

    /// <summary>getReturnParcelId: mã yêu cầu hoàn viết hoa (app cũ còn đọc claimId/rawDetails.claimId, v2 chỉ có ReturnOrderId).</summary>
    public static string ReturnParcelId(Order o) => Up(o.ReturnOrderId);

    public static bool IsSameReturnParcel(Order left, Order right)
    {
        var leftId = ReturnParcelId(left);
        var rightId = ReturnParcelId(right);
        if (leftId.Length > 0 && rightId.Length > 0 && leftId != rightId) return false;

        var leftReturn = Clean(left.ReturnTrack);
        var rightReturn = Clean(right.ReturnTrack);
        var sameOrder = !string.IsNullOrEmpty(left.OrderId) && Up(left.OrderId) == Up(right.OrderId);

        if (sameOrder && leftId.Length > 0 && leftId == rightId)
        {
            if (leftReturn.Length == 0 && rightReturn.Length > 0 && !string.IsNullOrEmpty(right.ForwardTrack) && Clean(left.Track) == Clean(right.ForwardTrack)) return true;
            if (rightReturn.Length == 0 && leftReturn.Length > 0 && !string.IsNullOrEmpty(left.ForwardTrack) && Clean(right.Track) == Clean(left.ForwardTrack)) return true;
        }

        if (sameOrder)
        {
            // Dòng cũ chỉ có mã GIAO (chưa mã hoàn, chưa mã yêu cầu) + dòng mới cùng mã đơn, cùng mã giao nhưng đã có mã hoàn = CÙNG kiện.
            bool FwdOnly(Order o) => Clean(o.ReturnTrack).Length == 0 && ReturnParcelId(o).Length == 0 && Clean(First(o.Track, o.ForwardTrack)).Length > 0;
            bool SameFwd(Order a, Order b) => Clean(b.ForwardTrack).Length > 0 && Clean(First(a.Track, a.ForwardTrack)) == Clean(b.ForwardTrack);
            if (FwdOnly(left) && rightReturn.Length > 0 && SameFwd(left, right)) return true;
            if (FwdOnly(right) && leftReturn.Length > 0 && SameFwd(right, left)) return true;
        }

        var leftPrimary = Clean(First(left.ReturnTrack, left.Track));
        var rightPrimary = Clean(First(right.ReturnTrack, right.Track));

        // Khác mã chính vẫn là kiện khác, kể cả cùng yêu cầu hoàn; chỉ thay mã khi có liên kết lịch sử TƯỜNG MINH.
        if (leftPrimary.Length > 0 && rightPrimary.Length > 0)
        {
            if (leftPrimary == rightPrimary) return true;
            var leftForward = Clean(left.ForwardTrack);
            var rightForward = Clean(right.ForwardTrack);
            return (rightPrimary != leftForward && left.TrackingCodes.Contains(rightPrimary)) ||
                   (leftPrimary != rightForward && right.TrackingCodes.Contains(leftPrimary));
        }
        if (leftId.Length > 0 && rightId.Length > 0) return leftId == rightId;
        if (leftId.Length > 0 || rightId.Length > 0 || leftPrimary.Length > 0 || rightPrimary.Length > 0) return false;
        return !string.IsNullOrEmpty(left.OrderId) && Up(left.OrderId) == Up(right.OrderId);
    }

    /// <summary>findExistingReturnParcelIndex: chỉ trả về khi có ĐÚNG MỘT kiện khớp; không có hoặc nhiều hơn một => -1 (không đoán).</summary>
    public static int FindExistingIndex(IReadOnlyList<Order> orders, Order incoming)
    {
        var found = -1;
        var count = 0;
        for (var i = 0; i < orders.Count; i++)
            if (IsSameReturnParcel(orders[i], incoming))
            {
                found = i;
                if (++count > 1) return -1;
            }
        return count == 1 ? found : -1;
    }

    /// <summary>Số kiện khớp (để báo "nhập nhằng" thay vì im lặng khi &gt; 1).</summary>
    public static IReadOnlyList<int> FindAllMatchIndexes(IReadOnlyList<Order> orders, Order incoming) =>
        Enumerable.Range(0, orders.Count).Where(i => IsSameReturnParcel(orders[i], incoming)).ToList();

    /// <summary>
    /// findTracklessSameOrderIndex: dòng Salework không có mã yêu cầu hoàn nên trước đây bị coi là kiện KHÁC; nay điền mã vào chính
    /// dòng cũ cùng mã đơn mà CHƯA có mã vận đơn nào (do file hoàn tiền TikTok / Google Sheet tạo).
    /// </summary>
    public static int FindTracklessSameOrderIndex(IReadOnlyList<Order> orders, Order incoming, Func<string, bool>? platformOk = null)
    {
        platformOk ??= p => p is "TikTok Shop" or "TikTok";
        if (ReturnParcelId(incoming).Length > 0) return -1;
        if (Clean(First(incoming.ReturnTrack, incoming.Track, incoming.ForwardTrack)).Length == 0) return -1;
        var oid = Up(incoming.OrderId);
        if (oid.Length == 0) return -1;
        for (var i = 0; i < orders.Count; i++)
        {
            var o = orders[i];
            if (Up(o.OrderId) != oid) continue;
            if (!string.IsNullOrEmpty(o.Platform) && !platformOk(o.Platform)) continue;
            if (Clean(o.Track).Length > 0 || Clean(o.ReturnTrack).Length > 0 || Clean(o.ForwardTrack).Length > 0 || Clean(o.AltTrack).Length > 0) continue;
            return i;
        }
        return -1;
    }
}
