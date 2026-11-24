import XCTest
@testable import ScannerCore

/// Đọc markdown trong câu trả lời của AI: đậm, nghiêng, mã, danh sách, bảng, tiêu đề.
final class ChatMarkdownTests: XCTestCase {
    func testBoldItalicAndCode() {
        let blocks = ChatMarkdown.parse("Hôm nay **có 1 kiện** về, *chưa* kiểm `VN001` xong")
        XCTAssertEqual(blocks.count, 1)
        guard case .paragraph(let p) = blocks[0] else { return XCTFail("phải là đoạn") }
        XCTAssertEqual(p.map { $0.text }, ["Hôm nay ", "có 1 kiện", " về, ", "chưa", " kiểm ", "VN001", " xong"])
        XCTAssertTrue(p[1].bold)
        XCTAssertTrue(p[3].italic)
        XCTAssertTrue(p[5].code)
    }

    func testTableFromTheRealAnswer() {
        let md = "**Hôm nay (25/11/2026) có 1 kiện hàng đã về kho**:\n\n| Mã đơn | Mã vận đơn |\n|--------|------------|\n| 261109RYQX93M3 | 01M6ZHRV754AD0DQNEGV3TXGS8 |\n\nBạn có thể bấm vào mã đơn."
        let blocks = ChatMarkdown.parse(md)
        XCTAssertEqual(blocks.count, 3)
        guard case .table(let rows, let hasHeader) = blocks[1] else { return XCTFail("phải là bảng") }
        XCTAssertTrue(hasHeader)
        XCTAssertEqual(rows.count, 2)
        XCTAssertEqual(rows[0][0][0].text, "Mã đơn")
        XCTAssertEqual(rows[1][0][0].text, "261109RYQX93M3")
        XCTAssertEqual(rows[1][1][0].text, "01M6ZHRV754AD0DQNEGV3TXGS8")
    }

    func testListsHeadingsAndPlainLines() {
        let blocks = ChatMarkdown.parse("## Kết quả\n- Áo thun: 3\n* Quần jean: 1\n1. Một\n2) Hai\nDòng thường")
        XCTAssertEqual(blocks.count, 6)
        if case .heading = blocks[0] {} else { XCTFail("tiêu đề") }
        if case .listItem(_, let m) = blocks[1] { XCTAssertEqual(m, "•") } else { XCTFail("gạch đầu dòng") }
        if case .listItem(_, let m) = blocks[2] { XCTAssertEqual(m, "•") } else { XCTFail("gạch đầu dòng") }
        if case .listItem(_, let m) = blocks[3] { XCTAssertEqual(m, "1.") } else { XCTFail("số thứ tự") }
        if case .listItem(_, let m) = blocks[4] { XCTAssertEqual(m, "2.") } else { XCTFail("số thứ tự") }
        if case .paragraph = blocks[5] {} else { XCTFail("đoạn") }
    }

    func testBoldLineStartingWithStarsIsNotABullet() {
        let blocks = ChatMarkdown.parse("**Tổng cộng**: 5 kiện")
        XCTAssertEqual(blocks.count, 1)
        guard case .paragraph(let p) = blocks[0] else { return XCTFail("phải là đoạn") }
        XCTAssertTrue(p[0].bold)
    }

    func testOddInputNeverCrashesAndPlainTextDropsMarks() {
        XCTAssertTrue(ChatMarkdown.parse(nil).isEmpty)
        XCTAssertTrue(ChatMarkdown.parse("   \n  ").isEmpty)
        XCTAssertEqual(ChatMarkdown.parse("**chưa đóng và | lạ").count, 1)
        XCTAssertEqual(ChatMarkdown.toPlainText("Hôm nay **có 1 kiện**\n- một\n| Mã đơn | Mã vận đơn |\n|---|---|\n| A | B |"),
                       "Hôm nay có 1 kiện\n• một\nMã đơn  |  Mã vận đơn\nA  |  B")
    }
}
