using QuanLyHangHoan.App.Services;

namespace QuanLyHangHoan.Tests;

/// <summary>Máy tính (máy chính) đẩy đơn đã nạp lên cloud: mất mạng lúc nạp thì khi NỐI LẠI phải tự đẩy phần còn thiếu.</summary>
public sealed class CloudPublishTriggerTests
{
    [Fact]
    public void Publishes_once_on_each_transition_from_locked_to_writable()
    {
        var t = new CloudPublishTrigger();
        Assert.False(t.OnLinkChanged(canWrite: false));   // đang kết nối
        Assert.True(t.OnLinkChanged(canWrite: true));     // nối được: đẩy bù
        Assert.False(t.OnLinkChanged(canWrite: true));    // vẫn nối: không đẩy lặp
        Assert.False(t.OnLinkChanged(canWrite: false));   // mất mạng
        Assert.True(t.OnLinkChanged(canWrite: true));     // nối lại: đẩy bù
    }

    [Theory]
    [InlineData(0, 0, 0, false, null)]
    [InlineData(12, 2, 10, false, "Đã đẩy 12 đơn lên cloud cho các máy khác (mới 2, cập nhật 10).")]
    public void Notes_are_shown_only_when_something_was_sent(int sent, int added, int updated, bool _, string? expected)
    {
        var r = new QuanLyHangHoan.Data.Cloud.CloudPublishResult(sent, sent, added, updated, 0, 0, 0);
        Assert.Equal(expected, CloudPublishTrigger.Note(r));
    }

    [Fact]
    public void Rejected_orders_are_reported_as_a_warning()
    {
        var r = new QuanLyHangHoan.Data.Cloud.CloudPublishResult(5, 5, 1, 2, 0, 2, 0);
        Assert.Contains("bị từ chối 2", CloudPublishTrigger.Note(r));
    }
}
