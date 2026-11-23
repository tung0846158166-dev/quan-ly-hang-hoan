using QuanLyHangHoan.Core.Alerts;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Core.Tracking;

namespace QuanLyHangHoan.Tests;

public class TrackingResolverTests
{
    private static Order Parcel(string id, string orderId, string returnTrack, string forward = "FW1") =>
        new() { Id = id, OrderId = orderId, ReturnTrack = returnTrack, ForwardTrack = forward };

    [Fact]
    public void ReceivingParcelA_DoesNotMatchSiblingsBorC()
    {
        var a = Parcel("A", "ORD1", "RA111");
        var b = Parcel("B", "ORD1", "RB222");
        var c = Parcel("C", "ORD1", "RC333");
        var r = TrackingResolver.Resolve(new[] { a, b, c }, "ra111");
        Assert.Equal(MatchKind.Match, r.Kind);
        Assert.Same(a, r.Single);
    }

    [Fact]
    public void SharedForwardTrack_WithSeveralParcels_IsAmbiguous()
    {
        var list = new[] { Parcel("A", "ORD1", "RA"), Parcel("B", "ORD1", "RB") };
        var r = TrackingResolver.Resolve(list, "FW1");
        Assert.Equal(MatchKind.Ambiguous, r.Kind);
        Assert.Equal(2, r.Candidates.Count);
        Assert.Null(r.Single);
    }

    [Fact]
    public void OriginalOrderId_WithSeveralParcels_IsAmbiguous_NotAutoReceived()
    {
        var list = new[] { Parcel("A", "ORD1", "RA", "F1"), Parcel("B", "ORD1", "RB", "F2") };
        Assert.Equal(MatchKind.Ambiguous, TrackingResolver.Resolve(list, "ord1").Kind);
    }

    [Fact]
    public void OnlyParcel_ByForwardTrack_Matches()
    {
        var list = new[] { Parcel("A", "ORD1", "RA", "FWX") };
        Assert.Equal(MatchKind.Match, TrackingResolver.Resolve(list, "FWX").Kind);
    }

    [Fact]
    public void ExplicitAlias_MatchesThatParcelOnly()
    {
        var a = Parcel("A", "ORD1", "RA");
        a.TrackingCodes.Add("OLDCODE9");
        var b = Parcel("B", "ORD1", "RB");
        var r = TrackingResolver.Resolve(new[] { a, b }, " old code9 ");
        Assert.Equal(MatchKind.Match, r.Kind);
        Assert.Same(a, r.Single);
    }

    [Fact]
    public void UnknownOrEmptyCode_NotFound()
    {
        var list = new[] { Parcel("A", "ORD1", "RA") };
        Assert.Equal(MatchKind.NotFound, TrackingResolver.Resolve(list, "ZZZ").Kind);
        Assert.Equal(MatchKind.NotFound, TrackingResolver.Resolve(list, "   ").Kind);
    }

    [Fact]
    public void ReturnOrderIdAlone_NeverMergesDifferentParcels()
    {
        var a = Parcel("A", "ORD1", "RA"); a.ReturnOrderId = "CLAIM1";
        var b = Parcel("B", "ORD1", "RB"); b.ReturnOrderId = "CLAIM1";
        Assert.Equal(MatchKind.Ambiguous, TrackingResolver.Resolve(new[] { a, b }, "CLAIM1").Kind);
    }
}

public class AlertPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);

    [Theory]
    [InlineData(5, AlertLevel.Safe)]
    [InlineData(12, AlertLevel.Yellow)]
    [InlineData(23.9, AlertLevel.Yellow)]
    [InlineData(24, AlertLevel.Red)]
    [InlineData(100, AlertLevel.Red)]
    public void MainTable_Thresholds(double hours, AlertLevel expected)
    {
        var level = AlertPolicy.MainTable.Evaluate(Now.AddHours(-hours), received: false, Now);
        Assert.Equal(expected, level);
    }

    [Fact]
    public void ReceivedOrNotArrived_NeverAlerts()
    {
        Assert.Equal(AlertLevel.Done, AlertPolicy.MainTable.Evaluate(Now.AddDays(-9), true, Now));
        Assert.Equal(AlertLevel.None, AlertPolicy.MainTable.Evaluate(null, false, Now));
    }
}
