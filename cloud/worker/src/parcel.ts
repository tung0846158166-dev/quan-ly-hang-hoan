import { normalize } from "../../core/src/tracking";
import { cleanTrackingCode } from "../../core/src/matching";

/** Danh sách trắng (cloud/API.md mục 1): khóa nào ngoài danh sách đều bị TỪ CHỐI (mặc định cấm dữ liệu cá nhân/dữ liệu thô). */
export const PARCEL_KEYS = new Set([
  "id", "orderId", "returnOrderId", "track", "returnTrack", "altTrack", "forwardTrack", "trackingCodes", "scanAliases",
  "platform", "orderType", "platformStatus", "preOrder", "status", "carrier",
  "shipDate", "returnDate", "recvDate", "returnedAt", "deadline", "price", "note", "userNote",
  "importWeek", "importedAt", "isWatchlist", "watchlistReason", "isPartialReturn", "photoId", "items", "extra",
  "isArchived", "archivedAt", "isDeleted", "deletedAt", "deletedFrom",
  // do máy chủ/nguồn thêm, bỏ qua khi lưu
  "rev", "updatedAt", "hash",
]);
export const EXTRA_KEYS = new Set([
  "returnReason", "returnStatus", "returnShippingStatus", "returnSuccessTime", "cancelType",
  "forwardCarrier", "returnCarrier", "priceNeedsReview", "priceHasValue",
]);
const ITEM_KEYS = new Set(["product", "color", "size", "qty", "returnedQty", "sku", "skuId"]);

export interface Item { product: string; color: string; size: string; qty: number; returnedQty: number; sku?: string; skuId?: string }

export interface Parcel {
  id: string; orderId: string; returnOrderId: string; track: string; returnTrack: string; altTrack: string; forwardTrack: string;
  trackingCodes: string[]; scanAliases: string[];
  platform: string; orderType: string; platformStatus: string; preOrder: string; status: string; carrier: string;
  shipDate: string; returnDate: string; recvDate: string; returnedAt: string; deadline: string;
  price: number; note: string; userNote: string; importWeek: string; importedAt: string;
  isWatchlist: boolean; watchlistReason: string; isPartialReturn: boolean; photoId: string;
  items: Item[]; extra: Record<string, string | number | boolean>;
  isArchived: boolean; archivedAt: string; isDeleted: boolean; deletedAt: string; deletedFrom: string;
}

export interface Validation { ok: true; parcel: Parcel }
export interface Invalid { ok: false; reason: string }

const str = (v: unknown): string => (typeof v === "string" ? v : v === undefined || v === null ? "" : String(v));
const strArr = (v: unknown): string[] => (Array.isArray(v) ? v.map(str) : []);

/** Kiểm tra + chuẩn hóa một kiện đến từ máy khách/nguồn nhập. */
export function parseParcel(raw: unknown): Validation | Invalid {
  if (typeof raw !== "object" || raw === null || Array.isArray(raw)) return { ok: false, reason: "không phải đối tượng" };
  const o = raw as Record<string, unknown>;
  for (const k of Object.keys(o)) if (!PARCEL_KEYS.has(k)) return { ok: false, reason: `khóa không được phép: ${k}` };
  const id = str(o.id).trim();
  if (id.length === 0 || id.length > 200) return { ok: false, reason: "thiếu id" };

  const extraIn = (typeof o.extra === "object" && o.extra !== null && !Array.isArray(o.extra) ? o.extra : {}) as Record<string, unknown>;
  const extra: Record<string, string | number | boolean> = {};
  for (const k of Object.keys(extraIn)) {
    if (!EXTRA_KEYS.has(k)) return { ok: false, reason: `extra.${k} không được phép` };
    const v = extraIn[k];
    if (typeof v === "string" || typeof v === "number" || typeof v === "boolean") extra[k] = v;
  }

  const items: Item[] = [];
  if (Array.isArray(o.items)) {
    for (const it of o.items) {
      if (typeof it !== "object" || it === null) return { ok: false, reason: "sản phẩm không hợp lệ" };
      const r = it as Record<string, unknown>;
      for (const k of Object.keys(r)) if (!ITEM_KEYS.has(k)) return { ok: false, reason: `items.${k} không được phép` };
      const item: Item = { product: str(r.product), color: str(r.color), size: str(r.size), qty: Number(r.qty) || 0, returnedQty: Number(r.returnedQty) || 0 };
      if (r.sku !== undefined && r.sku !== null) item.sku = str(r.sku);
      if (r.skuId !== undefined && r.skuId !== null) item.skuId = str(r.skuId);
      items.push(item);
    }
  }
  if (items.length > 200) return { ok: false, reason: "quá nhiều sản phẩm" };

  const p: Parcel = {
    id, orderId: str(o.orderId), returnOrderId: str(o.returnOrderId), track: str(o.track), returnTrack: str(o.returnTrack),
    altTrack: str(o.altTrack), forwardTrack: str(o.forwardTrack), trackingCodes: strArr(o.trackingCodes), scanAliases: strArr(o.scanAliases),
    platform: str(o.platform), orderType: str(o.orderType), platformStatus: str(o.platformStatus), preOrder: str(o.preOrder),
    status: str(o.status), carrier: str(o.carrier),
    shipDate: str(o.shipDate), returnDate: str(o.returnDate), recvDate: str(o.recvDate), returnedAt: str(o.returnedAt), deadline: str(o.deadline),
    price: Number(o.price) || 0, note: str(o.note), userNote: str(o.userNote), importWeek: str(o.importWeek), importedAt: str(o.importedAt),
    isWatchlist: o.isWatchlist === true, watchlistReason: str(o.watchlistReason), isPartialReturn: o.isPartialReturn === true,
    photoId: str(o.photoId), items, extra,
    isArchived: o.isArchived === true, archivedAt: str(o.archivedAt), isDeleted: o.isDeleted === true, deletedAt: str(o.deletedAt), deletedFrom: str(o.deletedFrom),
  };
  return { ok: true, parcel: p };
}

/** Chuỗi JSON ổn định (khóa theo thứ tự cố định) để tính băm. */
export function canonical(p: Parcel): string {
  return JSON.stringify(p, Object.keys(p).sort());
}

export async function sha256Hex(text: string): Promise<string> {
  const buf = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(buf)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

/** Mã dùng để tra nhanh khi quét: "own" (mã riêng của kiện) và "weak" (mã giao đi/mã đơn/mã yêu cầu hoàn). */
export function codesOf(p: Parcel): { code: string; kind: "own" | "weak" }[] {
  const out = new Map<string, "own" | "weak">();
  const own = [p.track, p.returnTrack, p.altTrack, ...p.trackingCodes, ...p.scanAliases].map(normalize).filter((c) => c.length > 0);
  for (const c of own) out.set(c, "own");
  // bản "làm sạch" (tách mã từ link...) để nhập Sheet tìm được ứng viên như bộ so khớp nhập dùng
  for (const c of [p.track, p.returnTrack, p.altTrack, p.forwardTrack, ...p.trackingCodes].map((x) => cleanTrackingCode(x)).filter((c) => c.length > 0))
    if (!out.has(c)) out.set(`${c}\u0000clean`, "weak");
  for (const c of [p.forwardTrack, p.orderId, p.returnOrderId].map(normalize).filter((c) => c.length > 0)) {
    // cùng một mã vừa là mã riêng vừa là mã yếu của chính kiện: giữ cả hai dòng tra
    out.set(`${c}\u0000weak`, "weak");
  }
  return [...out.entries()].map(([k, kind]) => ({ code: kind === "weak" ? k.split("\u0000")[0] as string : k, kind }));
}
