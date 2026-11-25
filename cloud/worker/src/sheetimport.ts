import { cleanTrackingCode } from "../../core/src/matching";
import { blankOrder, emptyImport, type Incoming, type MOrder } from "../../core/src/merge";
import { normalize } from "../../core/src/tracking";
import { EXTRA_KEYS, type Parcel } from "./parcel";

const RAW_KEYS = ["returnReason", "returnStatus", "returnShippingStatus", "returnSuccessTime", "cancelType"];

/** Kiện trên cloud -> đơn cho bộ gộp (dữ liệu nhập phụ chỉ có phần trong danh sách trắng `extra`). */
export function parcelToOrder(p: Parcel): MOrder {
  const o = blankOrder(p.id);
  Object.assign(o, {
    orderId: p.orderId, returnOrderId: p.returnOrderId, track: p.track, returnTrack: p.returnTrack, altTrack: p.altTrack, forwardTrack: p.forwardTrack,
    trackingCodes: [...p.trackingCodes], scanAliases: [...p.scanAliases], platform: p.platform, orderType: p.orderType, platformStatus: p.platformStatus,
    preOrder: p.preOrder, status: p.status, carrier: p.carrier, shipDate: p.shipDate, returnDate: p.returnDate, recvDate: p.recvDate,
    returnedAt: p.returnedAt, deadline: p.deadline, price: p.price, note: p.note, userNote: p.userNote, importWeek: p.importWeek,
    importedAt: p.importedAt, isWatchlist: p.isWatchlist, watchlistReason: p.watchlistReason, isPartialReturn: p.isPartialReturn, photoId: p.photoId,
    items: p.items.map((i) => ({ product: i.product, color: i.color, size: i.size, qty: i.qty, returnedQty: i.returnedQty, sku: i.sku ?? null, skuId: i.skuId ?? null, importPrices: null })),
  });
  o.import = emptyImport();
  for (const k of RAW_KEYS) if (p.extra[k] !== undefined) o.import.rawDetails[k] = String(p.extra[k]);
  if (typeof p.extra.forwardCarrier === "string") o.import.forwardCarrier = p.extra.forwardCarrier;
  if (typeof p.extra.returnCarrier === "string") o.import.returnCarrier = p.extra.returnCarrier;
  if (typeof p.extra.priceNeedsReview === "boolean") o.import.priceNeedsReview = p.extra.priceNeedsReview;
  if (typeof p.extra.priceHasValue === "boolean") o.import.priceHasValue = p.extra.priceHasValue;
  return o;
}

/**
 * Đơn sau khi gộp -> kiện cloud. Giữ cờ lưu trữ/xóa của kiện gốc. CHỈ giữ trường trong danh sách trắng:
 * ghi chú người mua và dữ liệu thô ngoài danh sách bị BỎ (không lưu dữ liệu cá nhân lên cloud).
 */
export function orderToParcel(o: MOrder, base: Parcel | null): Parcel {
  const extra: Parcel["extra"] = {};
  for (const k of RAW_KEYS) { const v = o.import.rawDetails[k]; if (v !== undefined && v !== "") extra[k] = v; }
  if (o.import.forwardCarrier) extra.forwardCarrier = o.import.forwardCarrier;
  if (o.import.returnCarrier) extra.returnCarrier = o.import.returnCarrier;
  if (o.import.priceNeedsReview !== null) extra.priceNeedsReview = o.import.priceNeedsReview;
  if (o.import.priceHasValue !== null) extra.priceHasValue = o.import.priceHasValue;
  for (const k of Object.keys(extra)) if (!EXTRA_KEYS.has(k)) delete extra[k];
  return {
    id: o.id, orderId: o.orderId, returnOrderId: o.returnOrderId, track: o.track, returnTrack: o.returnTrack, altTrack: o.altTrack, forwardTrack: o.forwardTrack,
    trackingCodes: [...o.trackingCodes], scanAliases: [...o.scanAliases], platform: o.platform, orderType: o.orderType, platformStatus: o.platformStatus,
    preOrder: o.preOrder, status: o.status, carrier: o.carrier, shipDate: o.shipDate, returnDate: o.returnDate, recvDate: o.recvDate,
    returnedAt: o.returnedAt, deadline: o.deadline, price: o.price, note: o.note, userNote: o.userNote, importWeek: o.importWeek, importedAt: o.importedAt,
    isWatchlist: o.isWatchlist, watchlistReason: o.watchlistReason, isPartialReturn: o.isPartialReturn, photoId: o.photoId,
    items: o.items.map((i) => {
      const it: Parcel["items"][number] = { product: i.product, color: i.color, size: i.size, qty: i.qty, returnedQty: i.returnedQty };
      if (i.sku) it.sku = i.sku;
      if (i.skuId) it.skuId = i.skuId;
      return it;
    }),
    extra,
    isArchived: base?.isArchived ?? false, archivedAt: base?.archivedAt ?? "", isDeleted: base?.isDeleted ?? false,
    deletedAt: base?.deletedAt ?? "", deletedFrom: base?.deletedFrom ?? "",
  };
}

const str = (v: unknown) => (typeof v === "string" ? v : v === undefined || v === null ? "" : String(v));
const num = (v: unknown) => (typeof v === "number" && Number.isFinite(v) ? v : Number(v) || 0);
const strMap = (v: unknown): Record<string, string> =>
  typeof v === "object" && v !== null && !Array.isArray(v) ? Object.fromEntries(Object.entries(v as Record<string, unknown>).map(([k, x]) => [k, str(x)])) : {};

/** Kiểm tra + chuẩn hóa một kiện nhập do Apps Script gửi (hình dạng ImportedParcel). Trả null nếu không dùng được. */
export function parseIncoming(raw: unknown): Incoming | null {
  if (typeof raw !== "object" || raw === null || Array.isArray(raw)) return null;
  const r = raw as Record<string, unknown>;
  const items = Array.isArray(r.items) ? r.items.slice(0, 200).map((x) => {
    const i = (typeof x === "object" && x !== null ? x : {}) as Record<string, unknown>;
    return { product: str(i.product), color: str(i.color), size: str(i.size), qty: num(i.qty), returnedQty: num(i.returnedQty), importPrices: null,
      sellerSku: i.sellerSku ? str(i.sellerSku) : null, skuId: i.skuId ? str(i.skuId) : null };
  }) : [];
  const p: Incoming = {
    shipDate: str(r.shipDate), returnDate: str(r.returnDate), recvDate: str(r.recvDate), platform: str(r.platform) || "Shopee", orderType: str(r.orderType),
    isPartialReturn: r.isPartialReturn === true, platformStatus: str(r.platformStatus), returnedAt: str(r.returnedAt), preOrder: str(r.preOrder),
    orderId: str(r.orderId), returnOrderId: str(r.returnOrderId), forwardTrack: str(r.forwardTrack), returnTrack: str(r.returnTrack),
    trackingCodes: Array.isArray(r.trackingCodes) ? r.trackingCodes.map(str).slice(0, 50) : [], track: str(r.track), altTrack: str(r.altTrack),
    carrier: str(r.carrier), price: num(r.price), priceHasValue: r.priceHasValue === true,
    priceNeedsReview: typeof r.priceNeedsReview === "boolean" ? r.priceNeedsReview : null,
    importModeSlots: {}, importSelectedPriceMode: str(r.importSelectedPriceMode) || "tong_gia_tri", priceModes: {}, priceModeFlags: {},
    status: str(r.status) || "Chưa về", note: str(r.note), buyerNote: "",   // ghi chú người mua không bao giờ lên cloud
    items, deadline: str(r.deadline), importPriceBasis: str(r.importPriceBasis),
    rawDetails: r.rawDetails ? strMap(r.rawDetails) : null,
  };
  if (!p.orderId && !p.track && !p.returnTrack && !p.returnOrderId) return null;
  return p;
}

/** Mọi mã để tra ứng viên trong bảng `parcel_codes` (bản chuẩn hóa và bản làm sạch). */
export function lookupKeys(p: Incoming): string[] {
  const out = new Set<string>();
  for (const v of [p.orderId, p.returnOrderId, p.track, p.returnTrack, p.altTrack, p.forwardTrack, ...p.trackingCodes]) {
    const a = normalize(v), b = cleanTrackingCode(v);
    if (a) out.add(a);
    if (b) out.add(b);
  }
  return [...out];
}

/** Ngày hôm nay theo giờ Việt Nam (dd/MM/yyyy) và năm. */
export function vnToday(nowMs: number): { today: string; year: number } {
  const d = new Date(nowMs + 7 * 3_600_000);
  const p = (n: number) => String(n).padStart(2, "0");
  return { today: `${p(d.getUTCDate())}/${p(d.getUTCMonth() + 1)}/${d.getUTCFullYear()}`, year: d.getUTCFullYear() };
}
