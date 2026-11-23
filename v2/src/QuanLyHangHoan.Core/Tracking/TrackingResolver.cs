using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Core.Tracking;

public enum MatchKind
{
    /// <summary>Đúng một kiện khớp.</summary>
    Match,
    /// <summary>Không có kiện nào khớp.</summary>
    NotFound,
    /// <summary>Nhiều kiện khớp: bắt buộc người dùng kiểm tra, KHÔNG tự nhận.</summary>
    Ambiguous,
}

public sealed record MatchResult(MatchKind Kind, IReadOnlyList<Order> Candidates, string Via)
{
    public Order? Single => Kind == MatchKind.Match ? Candidates[0] : null;
}

/// <summary>
/// Quy tắc nghiệp vụ (docs/fixes-1.8.8.md):
///  - Mã vận đơn của chính kiện (track/returnTrack/altTrack/trackingCodes/scanAliases) khớp => ưu tiên cao nhất.
///  - Mã giao đi dùng chung, mã đơn gốc, mã yêu cầu hoàn: chỉ là ứng viên; nhiều ứng viên => Ambiguous.
///  - Nhận kiện A không bao giờ nhận B/C.
/// </summary>
public static class TrackingResolver
{
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var chars = code.Where(c => !char.IsWhiteSpace(c) && c != '​' && c != '﻿').ToArray();
        return new string(chars).ToUpperInvariant();
    }

    /// <summary>Mã thuộc về CHÍNH kiện (không gồm mã giao đi chung / mã đơn gốc).</summary>
    public static IEnumerable<string> OwnCodes(Order o)
    {
        yield return Normalize(o.Track);
        yield return Normalize(o.ReturnTrack);
        yield return Normalize(o.AltTrack);
        foreach (var c in o.TrackingCodes) yield return Normalize(c);
        foreach (var c in o.ScanAliases) yield return Normalize(c);
    }

    public static MatchResult Resolve(IEnumerable<Order> orders, string rawCode)
    {
        var code = Normalize(rawCode);
        if (code.Length == 0) return new(MatchKind.NotFound, Array.Empty<Order>(), "empty");

        var list = orders as IList<Order> ?? orders.ToList();

        // 1) mã riêng của kiện
        var own = list.Where(o => OwnCodes(o).Any(c => c.Length > 0 && c == code)).ToList();
        if (own.Count == 1) return new(MatchKind.Match, own, "own-track");
        if (own.Count > 1) return new(MatchKind.Ambiguous, own, "own-track");

        // 2) mã giao đi / mã đơn gốc / mã yêu cầu hoàn: chỉ là ứng viên
        var weak = list.Where(o =>
                Normalize(o.ForwardTrack) == code ||
                Normalize(o.OrderId) == code ||
                Normalize(o.ReturnOrderId) == code)
            .ToList();
        if (weak.Count == 1) return new(MatchKind.Match, weak, "forward-or-order");
        if (weak.Count > 1) return new(MatchKind.Ambiguous, weak, "forward-or-order");

        return new(MatchKind.NotFound, Array.Empty<Order>(), "none");
    }
}
