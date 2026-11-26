// Gợi ý AI theo khung giờ làm việc (chủ dự án 26/11/2026): 8:00, 10:30, 13:30, 16:30, 19:00, 21:00 giờ Việt Nam.
// Cloud tự tính số liệu TỔNG HỢP (không tên/SĐT/địa chỉ khách), nhờ AI viết gợi ý ngắn; AI lỗi thì dùng bản viết theo quy tắc.
import type { Parcel } from "./parcel";

const VN = 7 * 3_600_000;

/** Giờ Việt Nam của một mốc ms: "HH:mm". */
export function vnClock(ms: number): string {
  const d = new Date(ms + VN);
  return `${String(d.getUTCHours()).padStart(2, "0")}:${String(d.getUTCMinutes()).padStart(2, "0")}`;
}

/** "dd/MM/yyyy[ HH:mm[:ss]]" hoặc "yyyy-MM-dd..." (giờ Việt Nam) -> ms; không đọc được thì null. Giống OrderStats.parseDate. */
export function parseVnDate(raw: string): number | null {
  const s = (raw ?? "").trim();
  if (!s) return null;
  const [datePart, timePart] = s.split(/[ T]/);
  const parts = (datePart ?? "").split(/[/\-.]/);
  if (parts.length !== 3) return null;
  const n = parts.map((x) => Number(x));
  if (n.some((x) => !Number.isInteger(x))) return null;
  let y: number, m: number, d: number;
  if ((parts[0] ?? "").length === 4) [y, m, d] = n as [number, number, number];
  else { [d, m, y] = n as [number, number, number]; if (y < 100) y += 2000; }
  if (m < 1 || m > 12 || d < 1 || d > 31 || y < 2000 || y > 2100) return null;
  let hh = 0, mm = 0, ss = 0;
  if (timePart) {
    const t = timePart.split(":").map((x) => Number(x));
    if (t.length >= 2 && Number.isInteger(t[0]) && Number.isInteger(t[1]) && t[0]! >= 0 && t[0]! <= 23 && t[1]! >= 0 && t[1]! <= 59) {
      hh = t[0]!; mm = t[1]!; ss = Number.isInteger(t[2]) ? t[2]! : 0;
    }
  }
  return Date.UTC(y, m - 1, d, hh, mm, ss) - VN;
}

/** Giờ hàng về shop (giống CloudRowMapper.arrivedAt của điện thoại). */
export function arrivedAt(p: Parcel): number | null {
  let s = (p.returnedAt ?? "").trim();
  if (!s) s = String(p.extra?.returnSuccessTime ?? "").trim();
  if (!s) return null;
  const m = /^(\d{1,2}:\d{1,2}(?::\d{1,2})?)\s+(\d{1,2}[/\-.]\d{1,2}[/\-.]\d{2,4})/.exec(s);
  if (m) s = `${m[2]} ${m[1]}`;
  return parseVnDate(s);
}

export const isReceived = (p: Parcel) => p.status === "Hàng đã trả" || p.status === "Đã nhận";
export const isPending = (p: Parcel) => !isReceived(p) && !["Không cần nhận", "Bị mất", "Hư hỏng"].includes(p.status);

export interface DigestStats {
  slot: string;
  pending: number; pendingByPlatform: Record<string, number>;
  yellow: number; red: number; redOldestDays: number; redByCarrier: [string, number][];
  receivedToday: number; newOrdersPending: number; watchlist: number;
  ambiguous24h: number; topProducts7d: [string, number][];
}

const top = (m: Map<string, number>, n: number): [string, number][] => [...m.entries()].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0])).slice(0, n);

/** Số liệu tổng hợp. Chỉ đơn đang dùng (không lưu trữ, không xóa). */
export function buildStats(parcels: Parcel[], nowMs: number, ambiguous24h: number): DigestStats {
  const live = parcels.filter((p) => !p.isArchived && !p.isDeleted);
  const todayKey = new Date(nowMs + VN).toISOString().slice(0, 10);
  const byPlatform = new Map<string, number>(), redCarrier = new Map<string, number>(), products = new Map<string, number>();
  let pending = 0, yellow = 0, red = 0, redOldest = 0, receivedToday = 0, newPending = 0, watch = 0;
  for (const p of live) {
    if (p.isWatchlist) watch++;
    const r = parseVnDate(p.recvDate);
    if (r !== null && new Date(r + VN).toISOString().slice(0, 10) === todayKey) receivedToday++;
    const rd = parseVnDate(p.returnDate);
    if (rd !== null && nowMs - rd <= 7 * 86_400_000)
      for (const it of p.items) if (it.product && !/^Sản phẩm mới/.test(it.product)) products.set(it.product.slice(0, 60), (products.get(it.product.slice(0, 60)) ?? 0) + Math.max(1, it.returnedQty || it.qty || 1));
    if (!isPending(p)) continue;
    pending++;
    byPlatform.set(p.platform || "Khác", (byPlatform.get(p.platform || "Khác") ?? 0) + 1);
    if (p.importWeek === "Đơn mới tạo") newPending++;
    const a = arrivedAt(p);
    if (a === null) continue;
    const el = nowMs - a;
    if (el >= 24 * 3_600_000) {
      red++;
      redOldest = Math.max(redOldest, Math.floor(el / 86_400_000));
      const c = p.carrier || "Không rõ hãng";
      redCarrier.set(c, (redCarrier.get(c) ?? 0) + 1);
    } else if (el >= 12 * 3_600_000) yellow++;
  }
  return {
    slot: vnClock(nowMs), pending, pendingByPlatform: Object.fromEntries(byPlatform), yellow, red, redOldestDays: redOldest,
    redByCarrier: top(redCarrier, 3), receivedToday, newOrdersPending: newPending, watchlist: watch, ambiguous24h, topProducts7d: top(products, 5),
  };
}

/** Bản viết theo quy tắc (dùng khi AI lỗi/hết lượt). */
export function ruleText(s: DigestStats): string {
  const lines: string[] = [];
  if (s.red > 0) lines.push(`• ${s.red} đơn ĐỎ (quá 24 giờ chưa nhận, lâu nhất ${s.redOldestDays} ngày)` + (s.redByCarrier.length ? `: nhiều nhất ${s.redByCarrier.map(([c, n]) => `${c} ${n}`).join(", ")}.` : "."));
  if (s.yellow > 0) lines.push(`• ${s.yellow} đơn VÀNG (12–24 giờ): nên nhận trong hôm nay.`);
  lines.push(`• Đang chờ nhận ${s.pending} đơn` + (Object.keys(s.pendingByPlatform).length ? ` (${Object.entries(s.pendingByPlatform).map(([k, v]) => `${k} ${v}`).join(", ")})` : "") + `; hôm nay đã nhận ${s.receivedToday}.`);
  if (s.newOrdersPending > 0) lines.push(`• ${s.newOrdersPending} đơn mới tạo tay/quét chưa nhận.`);
  if (s.ambiguous24h > 0) lines.push(`• ${s.ambiguous24h} trường hợp nhập nhằng trong 24 giờ qua cần kiểm tra tay.`);
  if (s.topProducts7d.length) lines.push(`• Hoàn nhiều 7 ngày qua: ${s.topProducts7d.slice(0, 3).map(([p, n]) => `${p} (${n})`).join("; ")}.`);
  return lines.join("\n");
}

/** Yêu cầu gửi AI: chỉ số liệu tổng hợp. */
export function digestMessages(s: DigestStats): { role: string; content: string }[] {
  return [
    { role: "system", content: [
      "Bạn là trợ lý KHO HÀNG HOÀN của một shop quần áo bán trên Shopee/TikTok. Đây là hàng KHÁCH TRẢ LẠI / ĐƠN HỦY đang quay về shop,",
      "KHÔNG phải đơn đang giao cho khách. Việc của kho: quét nhận kiện hoàn khi về tới shop, kiểm tra hàng, đối chiếu với hãng vận chuyển,",
      "khiếu nại sàn trước hạn nếu kiện thất lạc/thiếu hàng. TUYỆT ĐỐI không gợi ý nhắn tin khách, đóng gói, giao hàng hay tuyển thêm người.",
      "Ý nghĩa số liệu: red = kiện đã về tới shop/bưu cục trên 24 giờ mà kho CHƯA quét nhận (gấp nhất, dễ quá hạn khiếu nại);",
      "yellow = đã về 12–24 giờ chưa nhận; pending = tổng kiện chưa nhận (gồm cả kiện còn đang trên đường hoàn về, chưa tới shop — không phải lỗi);",
      "redByCarrier = hãng vận chuyển của các kiện đỏ; receivedToday = kiện đã quét nhận hôm nay; newOrdersPending = đơn tạo tay/quét chưa nhận;",
      "ambiguous24h = mã khớp nhiều kiện cần người kiểm tra tay; topProducts7d = sản phẩm bị hoàn nhiều 7 ngày qua (gợi ý xem lại chất lượng/mô tả);",
      "watchlist = đơn đang theo dõi.",
      "Viết TIẾNG VIỆT, tối đa 5 dòng bắt đầu bằng \"• \", mỗi dòng một việc cụ thể kho nên làm ngay, ưu tiên đỏ rồi vàng.",
      "Chỉ dùng số liệu được đưa, không bịa. Không chào hỏi, không kết luận.",
    ].join(" ") },
    { role: "user", content: `Số liệu lúc ${s.slot}:\n${JSON.stringify(s)}` },
  ];
}

/** Lấy chữ trả lời từ phản hồi chuẩn OpenAI; bỏ phần suy nghĩ <think>…</think> nếu có. */
export function answerText(res: unknown): string {
  const c = (res as { choices?: { message?: { content?: unknown } }[] })?.choices?.[0]?.message?.content;
  const t = typeof c === "string" ? c : typeof (res as { response?: unknown })?.response === "string" ? (res as { response: string }).response : "";
  return t.replace(/<think>[\s\S]*?<\/think>/g, "").trim().slice(0, 2000);
}
