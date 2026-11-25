using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Core.Tracking;

public enum ScanDecisionKind
{
    /// <summary>Đúng một kiện, đang chờ: được phép nhận.</summary>
    Receive,
    /// <summary>Kiện đã nhận trước đó (giữ nguyên giờ nhận).</summary>
    AlreadyReceived,
    /// <summary>Nhiều kiện khớp: KHÔNG tự nhận.</summary>
    Ambiguous,
    NotFound,
    /// <summary>Trạng thái "Không cần nhận"/"Bị mất"...: không tự đổi.</summary>
    NeedsManualCheck,
}

public sealed record ScanDecision(ScanDecisionKind Kind, Order? Order, int Candidates);

/// <summary>
/// Quyết định THUẦN (không đụng CSDL) cho một lượt quét: dùng chung cho app Windows và làm chuẩn đối chiếu cho bản TypeScript trên cloud
/// (cùng bộ ca thử trong cloud/golden). Phần ghi (UPDATE có điều kiện, ACK sau khi ghi bền) nằm ở nơi gọi.
/// </summary>
public static class ScanDecider
{
    public static ScanDecision Decide(IEnumerable<Order> activeOrders, string code)
    {
        var m = TrackingResolver.Resolve(activeOrders, code);
        if (m.Kind == MatchKind.NotFound) return new(ScanDecisionKind.NotFound, null, 0);
        if (m.Kind == MatchKind.Ambiguous) return new(ScanDecisionKind.Ambiguous, null, m.Candidates.Count);

        var o = m.Single!;
        if (o.Status == ReceiveStatus.Received) return new(ScanDecisionKind.AlreadyReceived, o, 1);
        if (o.Status is not (ReceiveStatus.NotReturned or ReceiveStatus.NotYetArrived or ""))
            return new(ScanDecisionKind.NeedsManualCheck, o, 1);
        return new(ScanDecisionKind.Receive, o, 1);
    }
}
