import { describe, expect, it } from "vitest";
import { answerText, arrivedAt, buildStats, parseVnDate, ruleText, vnClock } from "../src/digest";
import type { Parcel } from "../src/parcel";

// Gợi ý AI theo khung giờ: số liệu tổng hợp, cảnh báo 12/24 giờ giống điện thoại. Dữ liệu GIẢ.
const P = (o: Partial<Parcel>): Parcel => ({
  id: "x", orderId: "O", returnOrderId: "", track: "T", returnTrack: "", altTrack: "", forwardTrack: "", trackingCodes: [], scanAliases: [],
  platform: "Shopee", orderType: "Trả hàng/Hoàn tiền", platformStatus: "", preOrder: "", status: "Chưa trả", carrier: "SPX Express",
  shipDate: "", returnDate: "", recvDate: "", returnedAt: "", deadline: "", price: 0, note: "", userNote: "", importWeek: "", importedAt: "",
  isWatchlist: false, watchlistReason: "", isPartialReturn: false, photoId: "", items: [], extra: {},
  isArchived: false, archivedAt: "", isDeleted: false, deletedAt: "", deletedFrom: "", ...o,
});

// 26/11/2026 13:30 giờ Việt Nam
const NOW = Date.UTC(2026, 10, 26, 6, 30);

describe("gợi ý AI theo khung giờ", () => {
  it("đọc ngày giờ Việt Nam và giờ hàng về (kể cả dạng Salework giờ trước ngày)", () => {
    expect(vnClock(NOW)).toBe("13:30");
    expect(parseVnDate("26/11/2026 13:30")).toBe(NOW);
    expect(arrivedAt(P({ returnedAt: "13:30:00 26/11/2026" }))).toBe(NOW);
    expect(arrivedAt(P({ extra: { returnSuccessTime: "25/11/2026 13:30" } }))).toBe(NOW - 86_400_000);
    expect(parseVnDate("xx")).toBeNull();
  });

  it("đếm vàng/đỏ, đã nhận hôm nay, đơn mới chưa nhận; bỏ đơn lưu trữ/đã xóa; không có dữ liệu khách", () => {
    const s = buildStats([
      P({ returnedAt: "24/11/2026 10:00", carrier: "SPX Express" }),                       // đỏ, 2 ngày
      P({ returnedAt: "25/11/2026 20:00", carrier: "J&T Express", platform: "TikTok" }),  // vàng (17,5 giờ)
      P({ returnedAt: "26/11/2026 12:00" }),                                              // mới về
      P({ status: "Hàng đã trả", recvDate: "26/11/2026 09:00:00", returnDate: "25/11/2026", items: [{ product: "Áo len", color: "", size: "", qty: 1, returnedQty: 1 }] }),
      P({ importWeek: "Đơn mới tạo" }),
      P({ returnedAt: "01/11/2026", isArchived: true }),
      P({ returnedAt: "01/11/2026", isDeleted: true }),
      P({ status: "Không cần nhận" }),
    ], NOW, 2);
    expect(s).toMatchObject({ slot: "13:30", pending: 4, red: 1, yellow: 1, redOldestDays: 2, receivedToday: 1, newOrdersPending: 1, ambiguous24h: 2 });
    expect(s.pendingByPlatform).toEqual({ Shopee: 3, TikTok: 1 });
    expect(s.redByCarrier).toEqual([["SPX Express", 1]]);
    expect(s.topProducts7d).toEqual([["Áo len", 1]]);
    const t = ruleText(s);
    expect(t).toContain("1 đơn ĐỎ");
    expect(t).toContain("1 đơn VÀNG");
    expect(t).toContain("2 trường hợp nhập nhằng");
  });

  it("lấy chữ trả lời của AI, bỏ phần suy nghĩ", () => {
    expect(answerText({ choices: [{ message: { content: "<think>abc</think>\n• Nhận 3 đơn đỏ" } }] })).toBe("• Nhận 3 đơn đỏ");
    expect(answerText({ response: "• ok" })).toBe("• ok");
    expect(answerText(null)).toBe("");
  });
});
