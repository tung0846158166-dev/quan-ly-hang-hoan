using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.Tests;

/// <summary>Phần không cần mạng của bộ truyền tin MQTT (kết nối thật được thử tay, không đưa vào test để khỏi gửi dữ liệu ra ngoài).</summary>
public sealed class MqttTransportTests
{
    [Theory]
    [InlineData("SHP-2026", "agy_shopee_return_scanner_2026/SHP-2026")]
    [InlineData("a b", "agy_shopee_return_scanner_2026/a b")]
    public void Topic_matches_legacy_scheme(string room, string expected) =>
        Assert.Equal(expected, MqttTransport.TopicFor(room));

    [Theory]
    [InlineData("SHP-2026", "agy_shopee_shp-2026")]
    [InlineData("Phòng #1!", "agy_shopee_phng1")]
    [InlineData("", "agy_shopee_shp-2026")]
    public void Ntfy_topic_matches_legacy_scheme(string room, string expected) =>
        Assert.Equal(expected, SyncRoom.NtfyTopic(room));

    [Theory]
    [InlineData("SHP-2026", false)]          // mã mặc định của app cũ: yếu
    [InlineData("abc", false)]
    [InlineData("a1b2c3d4e5f6g7h8", true)]   // 16 ký tự
    [InlineData("kho-hang-hoan-8f3a91c2d7", true)]
    public void Room_strength_requires_16_chars_and_not_default(string room, bool strong) =>
        Assert.Equal(strong, SyncRoom.IsStrong(room));

    [Fact]
    public void Generated_room_is_strong_unique_and_url_safe()
    {
        var a = SyncRoom.Generate();
        var b = SyncRoom.Generate();
        Assert.NotEqual(a, b);
        Assert.True(SyncRoom.IsStrong(a));
        Assert.Matches("^[a-z0-9-]+$", a);
    }

    [Fact]
    public void Default_brokers_are_the_three_legacy_websocket_endpoints()
    {
        Assert.Equal(new[]
        {
            "wss://broker.emqx.io:8084/mqtt", "wss://broker.hivemq.com:8884/mqtt", "wss://test.mosquitto.org:8081/",
        }, MqttTransport.DefaultBrokers);
    }

    [Fact]
    public async Task Not_started_transport_reports_no_connection_and_publish_does_not_throw()
    {
        using var t = new MqttTransport(MqttTransport.DefaultBrokers);
        Assert.Equal(0, t.ConnectedBrokers);
        await t.PublishAsync("room", "x");
        Assert.False(string.IsNullOrEmpty(t.LastError));
    }
}
