// Tạo đơn mới qua cloud (máy tính nhập tay, điện thoại quét chế độ "Đơn mới"): cùng quy tắc với NewOrderService của máy tính.
// Mã đã có ở BẤT KỲ đâu (bảng chính, lưu trữ, đã xóa) thì KHÔNG tạo lại, không hồi sinh đơn đã xóa.
import { normalize } from "../../core/src/tracking";
import type { Parcel } from "./parcel";

export const NEW_ORDER_WEEK = "Đơn mới tạo";
export const LEGACY_PHONE_NOTE = "Đơn mới tạo từ máy quét điện thoại";
/** Đơn bán ngoài sàn (Facebook, Zalo, tại shop...) do chủ shop tự tạo (chủ dự án 27/11/2026). */
export const OFF_PLATFORM = "Ngoài sàn";
export const OFF_PLATFORM_WEEK = "Đơn ngoài sàn";
export const OFF_PLATFORM_TYPES = ["Trả hàng/Hoàn tiền", "Đổi hàng", "Giao không thành công"];

export interface NewOrderItem { product: string; color: string; size: string; qty: number }

export interface NewOrderInput {
  code: string; orderId: string; platform: string; carrier: string;
  product: string; color: string; size: string; qty: number; note: string; origin: "phone" | "manual";
  /** Nhiều sản phẩm (đơn ngoài sàn); rỗng = dùng product/color/size/qty. */
  items: NewOrderItem[]; price: number; orderType: string; channel: string;
}

export const isOffPlatform = (platform: string): boolean => platform.trim().toLowerCase() === OFF_PLATFORM.toLowerCase();

/** Mã tự sinh cho đơn ngoài sàn không có mã vận đơn: "NS" + yyMMdd + 4 ký tự (không có 0/O/1/I để đọc không nhầm). */
export function offPlatformCode(stamp: string, rand: () => number = Math.random): string {
  const m = /^(\d{2})\/(\d{2})\/\d{2}(\d{2})/.exec(stamp);
  const ymd = m ? `${m[3]}${m[2]}${m[1]}` : "000000";
  const abc = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
  let tail = "";
  for (let i = 0; i < 4; i++) tail += abc[Math.floor(rand() * abc.length) % abc.length];
  return `NS${ymd}${tail}`;
}

const qtyOf = (v: unknown): number => { const n = Number(v); return Number.isFinite(n) && n > 0 ? Math.min(9999, Math.floor(n)) : 1; };

const s = (v: unknown, max = 300): string => (typeof v === "string" ? v.trim().slice(0, max) : "");

export function parseNewOrder(op: Record<string, unknown>): NewOrderInput {
  const qty = Number(op.qty);
  return {
    code: s(op.code, 100), orderId: s(op.orderId, 100), platform: s(op.platform, 40), carrier: s(op.carrier, 60),
    product: s(op.product, 200), color: s(op.color, 60), size: s(op.size, 60), qty: Number.isFinite(qty) && qty > 0 ? Math.min(9999, Math.floor(qty)) : 1,
    note: s(op.note, 300), origin: op.origin === "phone" ? "phone" : "manual",
    items: (Array.isArray(op.items) ? op.items : []).slice(0, 30)
      .map((x) => (typeof x === "object" && x !== null ? x as Record<string, unknown> : {}))
      .map((x) => ({ product: s(x.product, 200), color: s(x.color, 60), size: s(x.size, 60), qty: qtyOf(x.qty) }))
      .filter((x) => x.product || x.color || x.size),
    price: (() => { const n = Number(op.price); return Number.isFinite(n) && n > 0 ? Math.min(1e10, Math.round(n)) : 0; })(),
    orderType: s(op.orderType, 60), channel: s(op.channel, 40),
  };
}

/** PhoneActions.DetectPlatform của máy tính. */
export function detectPlatform(raw: string): string {
  const code = raw.trim().toUpperCase();
  if (!code) return "TikTok";
  if (code.startsWith("SPX") || code.startsWith("SHOPEE") || /^VN2[4-9][0-9A-Z]{8,14}$/.test(code)) return "Shopee";
  if (/^8[0245]\d{10,12}$/.test(code)) return "TikTok";
  if (/^5\d{17}$/.test(code) || /^4\d{18}$/.test(code)) return "TikTok";
  if (/^2[3-9][0-1]\d[0-3]\d[A-Z0-9]{8,10}$/.test(code) || (/^[A-Z0-9]{14,15}$/.test(code) && !/^\d+$/.test(code))) return "Shopee";
  return "TikTok";
}

/** LegacyText.DetectCarrierByTrackingCode của máy tính. */
export function detectCarrier(raw: string): string | null {
  const code = raw.trim().toUpperCase();
  if (!code) return null;
  if (code.startsWith("SPXVN") || code.startsWith("SPX") || /^VN2[4-9][0-9A-Z]{8,14}$/.test(code)) return "SPX Express";
  if (code.includes(".") || code.startsWith("GHTK")) return "GHTK";
  if (!code.startsWith("SHOPEEVTP") && (code.startsWith("NIVN") || code.startsWith("SHP") || code.startsWith("NVSG") || code.startsWith("NVVN"))) return "Ninja Van";
  if (code.startsWith("AHA")) return "AhaMove";
  if (/^[A-Z]{2}[0-9]{9}VN$/.test(code) || (code.endsWith("VN") && code.length === 13)) return "VNPost";
  if (code.startsWith("SHOPEEVTP") || code.startsWith("VTP") || code.startsWith("VT")) return "Viettel Post";
  if (code.startsWith("TTVN") || code.startsWith("BEST")) return "Best Express";
  if (/^01M[0-9A-Z]{20,25}$/.test(code)) return "Khác";
  if (/^[0-9]+$/.test(code)) {
    if (code.length === 12 && ["80", "82", "84", "86", "66"].some((p) => code.startsWith(p))) return "J&T Express";
    if (code.length >= 13 && code.length <= 14) return "Best Express";
    if (code.length === 12) return "Best Express";
    if (code.length >= 9 && code.length <= 11) return "Viettel Post";
  }
  if (code.startsWith("GHN") || /^GY[A-Z0-9]{6,8}$/.test(code) || (/^[A-Z0-9]{6,10}$/.test(code) && /[A-Z]/.test(code) && /[0-9]/.test(code))) return "GHN";
  return null;
}

/** Kiện mới theo đúng hình dạng NewOrderService.Create của máy tính. `stamp` = giờ Việt Nam "dd/MM/yyyy HH:mm:ss". */
export function buildNewParcel(id: string, input: NewOrderInput, stamp: string): Parcel {
  const code = normalize(input.code);
  const phone = input.origin === "phone";
  const platformIn = input.platform;
  const platform = platformIn && platformIn.toLowerCase() !== "auto"
    ? (platformIn.toLowerCase().startsWith("tiktok") ? "TikTok" : platformIn)
    : detectPlatform(code);
  const off = isOffPlatform(platform);
  const carrier = input.carrier || (off ? "" : detectCarrier(code) || (platform === "Shopee" ? "SPX Express" : "J&T Express"));
  const fallbackProduct = phone ? "Sản phẩm mới (Quét điện thoại)" : "Sản phẩm mới (nhập tay)";
  const items = input.items.length > 0
    ? input.items.map((x) => ({ product: x.product || fallbackProduct, color: x.color, size: x.size, qty: x.qty, returnedQty: 0 }))
    : [{ product: input.product || fallbackProduct, color: input.color, size: input.size, qty: input.qty, returnedQty: 0 }];
  if (off) {
    // đơn ngoài sàn: sàn "Ngoài sàn", kênh bán ở cột trạng thái sàn, loại đơn chọn được; KHÔNG lưu thông tin người mua (quy tắc cloud)
    return {
      id, orderId: input.orderId || code, returnOrderId: "", track: code, returnTrack: "", altTrack: "", forwardTrack: "",
      trackingCodes: [], scanAliases: [],
      platform: OFF_PLATFORM, orderType: OFF_PLATFORM_TYPES.includes(input.orderType) ? input.orderType : OFF_PLATFORM_TYPES[0] as string,
      platformStatus: input.channel ? `Kênh: ${input.channel}` : "Kênh: Khác", preOrder: "Đơn thường", status: "Chưa trả", carrier,
      shipDate: stamp, returnDate: stamp, recvDate: "", returnedAt: "", deadline: "",
      price: input.price, note: input.note || "Đơn ngoài sàn tạo tay", userNote: "",
      importWeek: OFF_PLATFORM_WEEK, importedAt: stamp,
      isWatchlist: false, watchlistReason: "", isPartialReturn: false, photoId: "",
      items, extra: {},
      isArchived: false, archivedAt: "", isDeleted: false, deletedAt: "", deletedFrom: "",
    };
  }
  return {
    id, orderId: input.orderId || code, returnOrderId: "", track: code, returnTrack: "", altTrack: "", forwardTrack: "",
    trackingCodes: [], scanAliases: [],
    platform, orderType: "Trả hàng/Hoàn tiền", platformStatus: "Đang chuyển hoàn", preOrder: "Đơn thường", status: "Chưa trả", carrier,
    shipDate: stamp, returnDate: stamp, recvDate: "", returnedAt: "", deadline: "",
    price: input.price, note: input.note || (phone ? LEGACY_PHONE_NOTE : "Đơn tạo tay trên máy tính"), userNote: "",
    importWeek: NEW_ORDER_WEEK, importedAt: stamp,
    isWatchlist: false, watchlistReason: "", isPartialReturn: false, photoId: "",
    items, extra: {},
    isArchived: false, archivedAt: "", isDeleted: false, deletedAt: "", deletedFrom: "",
  };
}

/** Mã "của riêng" một kiện như NewOrderService.OwnCodes: mã vận đơn các loại; không có thì mã đơn. */
export function ownCodes(p: Parcel): Set<string> {
  const set = new Set([p.track, p.returnTrack, p.altTrack, ...p.trackingCodes, ...p.scanAliases].map(normalize).filter((c) => c.length > 0));
  if (set.size === 0 && normalize(p.orderId)) set.add(normalize(p.orderId));
  return set;
}
