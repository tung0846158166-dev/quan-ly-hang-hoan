import XCTest
@testable import ScannerCore

final class MQTTTests: XCTestCase {
    func testConnectPacket() {
        let d = MQTTPacket.connect(clientId: "ab", keepAlive: 30)
        XCTAssertEqual([UInt8](d), [0x10, 0x0E, 0x00, 0x04, 0x4D, 0x51, 0x54, 0x54, 0x04, 0x02, 0x00, 0x1E, 0x00, 0x02, 0x61, 0x62])
    }

    func testConnectPacketWithCredentials() {
        let d = MQTTPacket.connect(clientId: "ab", keepAlive: 30, username: "u", password: "p")
        XCTAssertEqual([UInt8](d), [0x10, 0x14, 0x00, 0x04, 0x4D, 0x51, 0x54, 0x54, 0x04, 0xC2, 0x00, 0x1E, 0x00, 0x02, 0x61, 0x62, 0x00, 0x01, 0x75, 0x00, 0x01, 0x70])
    }

    func testSubscribePacket() {
        let d = MQTTPacket.subscribe(packetId: 1, topic: "a/b")
        XCTAssertEqual([UInt8](d), [0x82, 0x08, 0x00, 0x01, 0x00, 0x03, 0x61, 0x2F, 0x62, 0x00])
    }

    func testPublishPacketSmall() {
        let d = MQTTPacket.publish(topic: "t", payload: Data("hi".utf8))
        XCTAssertEqual([UInt8](d), [0x30, 0x05, 0x00, 0x01, 0x74, 0x68, 0x69])
    }

    func testRemainingLengthEncoding() {
        XCTAssertEqual(MQTTPacket.remainingLength(0), [0x00])
        XCTAssertEqual(MQTTPacket.remainingLength(127), [0x7F])
        XCTAssertEqual(MQTTPacket.remainingLength(128), [0x80, 0x01])
        XCTAssertEqual(MQTTPacket.remainingLength(203), [0xCB, 0x01])
        XCTAssertEqual(MQTTPacket.remainingLength(16383), [0xFF, 0x7F])
        XCTAssertEqual(MQTTPacket.remainingLength(16384), [0x80, 0x80, 0x01])
    }

    func testPublishLongPayloadRoundTrip() {
        let payload = String(repeating: "x", count: 20000)
        let topicName = "agy_shopee_return_scanner_2026/ROOM-MQTT-2026"
        let wire = MQTTPacket.publish(topic: topicName, payload: Data(payload.utf8))
        var dec = MQTTDecoder()
        let out = dec.feed(wire)
        XCTAssertEqual(out.count, 1)
        guard case let .publish(topic, data, qos, _) = out[0] else { return XCTFail("không phải PUBLISH") }
        XCTAssertEqual(topic, topicName)
        XCTAssertEqual(qos, 0)
        XCTAssertEqual(String(data: data, encoding: .utf8), payload)
    }

    func testDecoderHandlesSplitAndStuckPackets() {
        var all = Data([0x20, 0x02, 0x00, 0x00])                       // CONNACK ok
        all.append(Data([0x90, 0x03, 0x00, 0x01, 0x00]))               // SUBACK
        all.append(MQTTPacket.publish(topic: "t", payload: Data("{\"a\":1}".utf8)))
        all.append(Data([0xD0, 0x00]))                                  // PINGRESP
        var dec = MQTTDecoder()
        var results: [MQTTIncoming] = []
        // Cắt thành từng khúc 3 byte để thử gói bị chia nhỏ.
        var i = 0
        while i < all.count {
            let end = min(i + 3, all.count)
            results += dec.feed(all.subdata(in: i..<end))
            i = end
        }
        XCTAssertEqual(results.count, 4)
        XCTAssertEqual(results[0], .connack(code: 0))
        XCTAssertEqual(results[1], .suback)
        XCTAssertEqual(results[2], .publish(topic: "t", payload: Data("{\"a\":1}".utf8), qos: 0, packetId: 0))
        XCTAssertEqual(results[3], .pingResp)
    }

    func testDecoderQoS1PublishHasPacketId() {
        // PUBLISH QoS1: topic "t", packet id 0x0102, payload "z"
        let wire = Data([0x32, 0x06, 0x00, 0x01, 0x74, 0x01, 0x02, 0x7A])
        var dec = MQTTDecoder()
        let out = dec.feed(wire)
        XCTAssertEqual(out, [.publish(topic: "t", payload: Data("z".utf8), qos: 1, packetId: 0x0102)])
        XCTAssertEqual([UInt8](MQTTPacket.puback(packetId: 0x0102)), [0x40, 0x02, 0x01, 0x02])
    }

    func testDecoderDropsOversizedCorruptPacketAndRecovers() {
        var dec = MQTTDecoder()
        // Remaining length = 0xFF 0xFF 0xFF 0x7F (~268 MB > 8 MB limit)
        XCTAssertEqual(dec.feed(Data([0x30, 0xFF, 0xFF, 0xFF, 0x7F])), [])
        // Gói hợp lệ kế tiếp vẫn giải mã bình thường
        XCTAssertEqual(dec.feed(Data([0xD0, 0x00])), [.pingResp])
    }

    func testClientRejectsInvalidURLWithoutCrash() {
        let bad = MQTTClient(name: "bad", url: "ht tp://[invalid")
        XCTAssertFalse(bad.isConnected)
    }

    func testMqttTopicMatchesPcAndWebScanner() {
        XCTAssertEqual(SyncProtocol.mqttTopic(room: SyncProtocol.normalizeRoom("room-test-2026")), "agy_shopee_return_scanner_2026/ROOM-TEST-2026")
    }
}
