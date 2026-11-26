/**
 * Gộp kiện NHẬP vào dữ liệu có sẵn (bản TypeScript của v2 Core/Import: TikTokMerger.Apply + ParcelMerge + ItemMerge,
 * vốn chuyển nguyên văn từ app cũ). Chạy trên Worker khi Google Sheet đẩy kiện lên. Không phụ thuộc Cloudflare.
 * Quy tắc bất biến 1-5: nhập lại cùng kiện thì CẬP NHẬT (không cộng lặp); khớp nhiều kiện hoặc khớp kiện của đơn khác => báo, KHÔNG đổi;
 * không đụng trường kho nhập tay (trạng thái nhận, ngày nhận, giờ về do quét, ảnh, ghi chú của tôi, theo dõi).
 * Kiểm chứng: cloud/golden/merge-vectors.json do CHÍNH bản C# sinh ra.
 */
import { cleanTrackingCode, findAllMatchIndexes, findShopeeTracklessSameOrderIndexes, findTracklessSameOrderIndex, isSameReturnParcel } from "./matching";

export interface MItem {
  product: string; color: string; size: string; qty: number; returnedQty: number;
  sku?: string | null; skuId?: string | null; importPrices?: Record<string, number> | null;
}

export interface MImport {
  priceModes: Record<string, number>; priceModeFlags: Record<string, boolean>; importModeSlots: Record<string, string | null>;
  importSelectedPriceMode: string | null; priceHasValue: boolean | null; priceNeedsReview: boolean | null; importPriceBasis: string | null;
  forwardCarrier: string; returnCarrier: string; rawDetails: Record<string, string>;
}

export interface MOrder {
  id: string;
  orderId: string; returnOrderId: string; track: string; returnTrack: string; altTrack: string; forwardTrack: string;
  trackingCodes: string[]; scanAliases: string[];
  platform: string; orderType: string; platformStatus: string; preOrder: string; status: string; carrier: string;
  shipDate: string; returnDate: string; recvDate: string; returnedAt: string; deadline: string;
  price: number; note: string; buyerNote: string; userNote: string; importWeek: string; importedAt: string;
  isWatchlist: boolean; watchlistReason: string; isPartialReturn: boolean; photoId: string;
  items: MItem[]; import: MImport;
}

/** Kiện nhập (ImportedParcel của C#). */
export interface Incoming {
  shipDate: string; returnDate: string; recvDate: string; platform: string; orderType: string; isPartialReturn: boolean;
  platformStatus: string; returnedAt: string; preOrder: string; orderId: string; returnOrderId: string; forwardTrack: string;
  returnTrack: string; trackingCodes: string[]; track: string; altTrack: string; carrier: string; price: number; priceHasValue: boolean;
  priceNeedsReview: boolean | null; importModeSlots: Record<string, string | null>; importSelectedPriceMode: string;
  priceModes: Record<string, number>; priceModeFlags: Record<string, boolean>; status: string; note: string; buyerNote: string;
  items: { product: string; color: string; size: string; qty: number; returnedQty: number; importPrices?: Record<string, number> | null; sellerSku?: string | null; skuId?: string | null }[];
  deadline: string; importPriceBasis: string; rawDetails: Record<string, string> | null;
}

export type OtherWhere = "archive" | "deleted";
export interface OtherEntry { order: MOrder; where: OtherWhere }
export interface AmbiguousParcel { platform: string; orderId: string; track: string; returnOrderId: string; candidates: number; orderConflict: boolean }
export interface MergeOutcome {
  added: MOrder[]; updated: MOrder[]; enrichedArchived: MOrder[]; ambiguous: AmbiguousParcel[];
  skippedArchivedUnchanged: number; skippedDeleted: number; enriched: number;
}
export type ProductMode = "supplement" | "replace";

// ------------------------------------------------------------------ chữ
/** removeVietnameseTones: bỏ dấu, đ->d, viết thường. */
export function removeTones(s: string | null | undefined): string {
  return (s ?? "").normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/đ/g, "d").replace(/Đ/g, "D").toLowerCase();
}

const COLOR_WORDS = [
  "xanh navy", "xanh den", "xanh duong", "xanh la", "xanh reu", "xanh ngoc", "xanh coban", "xanh bo", "xanh nhat", "xanh dam", "xanh pastel", "xanh lam",
  "xanh than", "xanh nau", "xanh kem", "xam than", "xam dam", "xam nhat", "mau mo", "kaki", "retro", "mori", "urban", "koda",
  "den", "trang", "trang nga", "trang tinh", "do do", "do tuoi", "do man", "do ruou", "hong", "hong phan", "hong baby",
  "vang", "vang bo", "vang chanh", "nau", "nau dat", "nau bo", "nau tay", "nau socola",
  "be", "be nhat", "be dam", "kem", "kem sua", "xam", "xam tieu", "xam chuot", "xam khoi", "xam ghi", "ghi", "tim", "tim than", "cam", "cam dat", "reu",
];
const REASON_KEYWORDS = [
  "khong nhan", "nguoi nhan", "tu choi", "that bai", "khong thanh", "huy don", "hoan tien", "tra hang", "ly do", "be vo", "hu hong",
  "thieu hang", "doi y", "chua nhan", "lien lac", "dia chi", "sdt", "nghe may", "hen giao", "bom hang", "khong mua", "sai hang",
  "kem chat luong", "khac mo ta", "khong dung", "dong kiem", "thu tien", "chuyen hoan", "luu kho", "buyer cancelled", "delivery failed",
  "rejected", "return reason", "package lost",
];

export function isReturnReasonText(str: string | null | undefined): boolean {
  const s = (str ?? "").trim();
  if (!s) return false;
  const norm = removeTones(s);
  if (REASON_KEYWORDS.some((k) => norm.includes(k))) return true;
  // C# đếm theo đơn vị UTF-16 giống JS String.length
  return s.length > 30;
}

export function isKnownColorName(str: string | null | undefined): boolean {
  const norm = removeTones((str ?? "").trim());
  if (!norm) return false;
  return COLOR_WORDS.some((c) => norm === c || norm.startsWith(c + " ") || norm.endsWith(" " + c));
}

const pad2 = (s: string) => s.padStart(2, "0");

export function normalizeShortDate(val: string | null | undefined, currentYear: number): string {
  const s = (val ?? "").trim();
  if (!s) return "";
  let timeSuffix = "", datePart = s;
  const mt = /\s+([0-9]{1,2}:[0-9]{2}(?::[0-9]{2})?)$/.exec(s);
  if (mt) { timeSuffix = " " + mt[1]; datePart = s.slice(0, mt.index).trim(); }
  const mIso = /^([0-9]{4})[/\-.]([0-9]{1,2})[/\-.]([0-9]{1,2})/.exec(datePart);
  if (mIso) return `${pad2(mIso[3]!)}/${pad2(mIso[2]!)}/${mIso[1]}${timeSuffix}`;
  const mShort = /^([0-9]{1,2})[/\-.]([0-9]{1,2})$/.exec(datePart);
  if (mShort) return `${pad2(mShort[1]!)}/${pad2(mShort[2]!)}/${currentYear}${timeSuffix}`;
  const mFull = /^([0-9]{1,2})[/\-.]([0-9]{1,2})[/\-.]([0-9]{2,4})$/.exec(datePart);
  if (mFull) {
    let y = mFull[3]!;
    if (y.length === 2) y = "20" + y;
    return `${pad2(mFull[1]!)}/${pad2(mFull[2]!)}/${y}${timeSuffix}`;
  }
  return s;
}

// ------------------------------------------------------------------ dựng đơn
const uniq = <T>(a: T[]) => [...new Set(a)];
export const normalizePlatform = (p: string | null | undefined): string => {
  const t = (p ?? "").trim();
  return t.toLowerCase() === "tiktok shop" || t.toLowerCase() === "tiktok" ? "TikTok" : p ?? "";
};

export function emptyImport(): MImport {
  return { priceModes: {}, priceModeFlags: {}, importModeSlots: {}, importSelectedPriceMode: null, priceHasValue: null, priceNeedsReview: null,
    importPriceBasis: null, forwardCarrier: "", returnCarrier: "", rawDetails: {} };
}

export function blankOrder(id: string): MOrder {
  return { id, orderId: "", returnOrderId: "", track: "", returnTrack: "", altTrack: "", forwardTrack: "", trackingCodes: [], scanAliases: [],
    platform: "", orderType: "", platformStatus: "", preOrder: "", status: "", carrier: "", shipDate: "", returnDate: "", recvDate: "",
    returnedAt: "", deadline: "", price: 0, note: "", buyerNote: "", userNote: "", importWeek: "", importedAt: "", isWatchlist: false,
    watchlistReason: "", isPartialReturn: false, photoId: "", items: [], import: emptyImport() };
}

const copyItem = (i: MItem): MItem => ({ product: i.product, color: i.color, size: i.size, qty: i.qty, returnedQty: i.returnedQty, sku: i.sku ?? null,
  skuId: i.skuId ?? null, importPrices: i.importPrices ? { ...i.importPrices } : null });

const itemFromIncoming = (i: Incoming["items"][number]): MItem => ({ product: i.product, color: i.color, size: i.size, qty: i.qty, returnedQty: i.returnedQty,
  sku: i.sellerSku ?? null, skuId: i.skuId ?? null, importPrices: i.importPrices ? { ...i.importPrices } : null });

/** ParcelMerge.ToOrder */
export function toOrder(p: Incoming, id: string): MOrder {
  const o = blankOrder(id);
  Object.assign(o, {
    shipDate: p.shipDate, returnDate: p.returnDate, recvDate: p.recvDate, platform: normalizePlatform(p.platform), orderType: p.orderType,
    isPartialReturn: p.isPartialReturn, platformStatus: p.platformStatus, returnedAt: p.returnedAt, preOrder: p.preOrder, orderId: p.orderId,
    returnOrderId: p.returnOrderId, forwardTrack: p.forwardTrack, returnTrack: p.returnTrack, trackingCodes: [...p.trackingCodes], track: p.track,
    altTrack: p.altTrack, carrier: p.carrier, price: p.price, status: p.status, note: p.note, buyerNote: p.buyerNote, deadline: p.deadline,
    items: p.items.map(itemFromIncoming),
  });
  o.import = {
    priceModes: { ...p.priceModes }, priceModeFlags: { ...p.priceModeFlags }, importModeSlots: { ...p.importModeSlots },
    importSelectedPriceMode: p.importSelectedPriceMode, priceHasValue: p.priceHasValue, priceNeedsReview: p.priceNeedsReview,
    importPriceBasis: p.importPriceBasis ? p.importPriceBasis : null,
    forwardCarrier: p.rawDetails?.["forwardCarrier"] ?? "", returnCarrier: p.rawDetails?.["returnCarrier"] ?? "",
    rawDetails: p.rawDetails ? { ...p.rawDetails } : {},
  };
  return o;
}

const trackingCodesOf = (p: { track: string; altTrack: string; forwardTrack: string; returnTrack: string; trackingCodes: string[] }) =>
  uniq([p.track, p.altTrack, p.forwardTrack, p.returnTrack, ...p.trackingCodes].map((c) => cleanTrackingCode(c)).filter((c) => c.length > 0));

/** ShopeeMapper.ForNewRow (nạp Google Sheet dùng nhánh Shopee). */
export function shopeeForNewRow(raw: Incoming, today: string): Incoming {
  const p: Incoming = JSON.parse(JSON.stringify(raw));
  const cleanTrack = cleanTrackingCode(p.track);
  if (!p.returnDate) p.returnDate = p.shipDate ? p.shipDate : today;
  const rs = p.rawDetails?.["returnStatus"] ?? "";
  if (!p.platformStatus)
    p.platformStatus = rs ? rs : p.orderType === "Đơn hủy" ? "Đã hủy" : p.orderType === "Giao không thành công" ? "Giao hàng thất bại" : "Chờ bạn xác nhận hàng trả về";
  if (!p.orderType) p.orderType = "Trả hàng/Hoàn tiền";
  if ((p.rawDetails?.["isFullReturn"] ?? "").toLowerCase().includes("một phần")) p.isPartialReturn = true;
  p.trackingCodes = trackingCodesOf(p);
  if (cleanTrack) p.track = cleanTrack;
  const ca = cleanTrackingCode(p.altTrack);
  p.altTrack = ca ? ca : p.altTrack;
  if (!p.carrier) p.carrier = "SPX Express";
  p.status = p.orderType === "Đơn hủy" && !cleanTrack && !p.track ? "Không cần nhận" : rs.includes("hủy") ? "Không cần nhận" : "Chưa trả";
  if (p.items.length === 0) p.items.push({ product: "", color: "", size: "", qty: 1, returnedQty: 0, importPrices: null, sellerSku: null, skuId: null });
  return p;
}

/**
 * TikTokMapper.ForNewRow (nạp Salework / file TikTok; 26/11/2026). Dòng mới: ngày hoàn = ngày gửi hoặc hôm nay, trạng thái sàn "Chờ xử lý",
 * mã đơn = mã yêu cầu hoàn nếu trống, hãng "J&T Express", "Không cần nhận" cho đơn hủy chưa có mã, còn lại "Chưa về".
 */
export function tiktokForNewRow(raw: Incoming, today: string): Incoming {
  const p: Incoming = JSON.parse(JSON.stringify(raw));
  const cleanTrack = cleanTrackingCode(p.track);
  if (!p.returnDate) p.returnDate = p.shipDate ? p.shipDate : today;
  if (!p.platformStatus) p.platformStatus = "Chờ xử lý";
  if (!p.orderId) p.orderId = p.returnOrderId;
  p.trackingCodes = trackingCodesOf(p);
  if (cleanTrack) p.track = cleanTrack;
  if (!p.carrier) p.carrier = "J&T Express";
  p.status = p.orderType === "Đơn hủy" && !cleanTrack && !p.track ? "Không cần nhận" : "Chưa về";
  return p;
}

export type NewRowKind = "shopee" | "tiktok";

/** ParcelMerge.NewRow */
export function newRow(raw: Incoming, today: string, id: string, kind: NewRowKind = "shopee"): MOrder {
  const o = toOrder(kind === "tiktok" ? tiktokForNewRow(raw, today) : shopeeForNewRow(raw, today), id);
  o.import.priceHasValue = null;
  return o;
}

// ------------------------------------------------------------------ sản phẩm (ItemMerge)
const nm = (v: string | null | undefined) => removeTones(v ?? "").replace(/[^a-z0-9]+/g, " ").trim();
const compat = (a: string, b: string) => !a || !b || a === b || a.includes(b) || b.includes(a);
export const skuOf = (it: MItem) => ((it.sku ? it.sku : it.skuId) ?? "").trim();

interface Info { it: MItem; name: string; color: string; size: string; sku: string; qty: number; hasVariant: boolean }

export function dedupeItems(items: MItem[]): MItem[] {
  if (items.length < 2) return items;
  const info: Info[] = items.map((it) => {
    const color = nm(it.color), size = nm(it.size);
    return { it, name: nm(it.product), color, size, sku: skuOf(it), qty: 0, hasVariant: color.length > 0 || size.length > 0 };
  });
  const output: Info[] = [];
  const same = (a: Info, b: Info) => (a.sku || b.sku) ? (a.sku.length > 0 && a.sku === b.sku) : (a.color === b.color && a.size === b.size && compat(a.name, b.name));
  for (const x of [...info.filter((i) => i.hasVariant), ...info.filter((i) => !i.hasVariant)]) {
    const qty = x.it.qty > 0 ? x.it.qty : 1;
    if (!x.hasVariant && !x.sku) {
      if (output.some((y) => (y.hasVariant || y.sku.length > 0) && compat(x.name, y.name))) continue;
    } else {
      const m = output.find((y) => same(y, x));
      if (m) {
        m.qty = Math.max(m.qty, qty);
        m.it.returnedQty = Math.max(m.it.returnedQty, x.it.returnedQty);
        if (x.name.length > m.name.length) { m.it.product = x.it.product; m.name = x.name; }
        if (!m.it.importPrices && x.it.importPrices) m.it.importPrices = x.it.importPrices;
        continue;
      }
    }
    output.push({ it: copyItem(x.it), name: x.name, color: x.color, size: x.size, sku: x.sku, qty, hasVariant: x.hasVariant });
  }
  const res = output.map((y) => { y.it.qty = y.qty; return y.it; });
  if (res.length >= items.length) return items;
  const rank = (it: MItem) => {
    const i = items.findIndex((z) => (z.color ?? "") === (it.color ?? "") && (z.size ?? "") === (it.size ?? ""));
    return i < 0 ? 1_000_000_000 : i;
  };
  return res.map((it, idx) => ({ it, idx })).sort((a, b) => rank(a.it) - rank(b.it) || a.idx - b.idx).map((t) => t.it);
}

const keyOf = (item: MItem) => {
  const sku = skuOf(item);
  return sku ? "SKU:" + sku : `[${item.product ?? ""}\u0001${item.color ?? ""}\u0001${item.size ?? ""}]`;
};

export function mergeItems(previous: MItem[], incoming: MItem[], mode: ProductMode): MItem[] {
  if (mode === "replace") return incoming.map(copyItem);
  const replacements = new Map<string, MItem[]>();
  for (const item of incoming) {
    const k = keyOf(item);
    const l = replacements.get(k);
    if (l) l.push(item); else replacements.set(k, [item]);
  }
  const result: MItem[] = [];
  const seen = new Set<string>();
  for (const item of previous) {
    const k = keyOf(item);
    const rep = replacements.get(k);
    if (rep) { if (!seen.has(k)) result.push(...rep); } else result.push(item);
    seen.add(k);
  }
  for (const [k, rep] of replacements) if (!seen.has(k)) result.push(...rep);
  return dedupeItems(result).map(copyItem);
}

// ------------------------------------------------------------------ ParcelMerge
const codesOf = (o: MOrder) =>
  uniq([o.track, o.altTrack, o.forwardTrack, o.returnTrack, ...o.trackingCodes].map((c) => cleanTrackingCode(c)).filter((c) => c.length > 0));

export function resolveShipDate(p: Incoming, year: number): string {
  if (p.shipDate.trim()) {
    const n = normalizeShortDate(p.shipDate.trim(), year);
    if (n) return n;
  }
  const m = /^([0-9]{2})([0-9]{2})([0-9]{2})[A-Za-z0-9]+/.exec(p.orderId.trim());
  if (m) {
    const yy = +m[1]!, mm = +m[2]!, dd = +m[3]!;
    if (yy >= 20 && yy <= 35 && mm >= 1 && mm <= 12 && dd >= 1 && dd <= 31) return `${m[3]}/${m[2]}/20${m[1]}`;
  }
  if (p.returnDate.trim()) {
    const n = normalizeShortDate(p.returnDate.trim(), year);
    if (n) return n;
  }
  return "";
}

const PLACEHOLDER = /Sản phẩm mới|Chưa rõ sản phẩm/;

export function fillTrackless(order: MOrder, match: Incoming): void {
  if (match.track) order.track = match.track;
  if (match.forwardTrack) order.forwardTrack = match.forwardTrack;
  if (match.returnTrack) order.returnTrack = match.returnTrack;
  order.trackingCodes = uniq([...codesOf(order), ...trackingCodesOf(match)].filter((c) => c.length > 0));
  if (match.carrier && (!order.carrier || order.carrier === "Chờ bưu cục tiếp nhận")) order.carrier = match.carrier;
  if (match.platformStatus) order.platformStatus = match.platformStatus;
  if (match.returnedAt) order.returnedAt = match.returnedAt;
  if (match.returnDate && !order.returnDate) order.returnDate = match.returnDate;
  // đơn huỷ bị đặt "Không cần nhận" chỉ vì lúc nhập chưa có mã: có mã rồi thì chờ nhận (quyết định chủ dự án 26/11/2026)
  if (order.status === "Không cần nhận" && order.orderType === "Đơn hủy" && order.trackingCodes.length > 0)
    order.status = order.platform === "TikTok" ? "Chưa về" : "Chưa trả";
  if (match.items.some((i) => i.product) && !order.items.some((i) => i.product && !PLACEHOLDER.test(i.product)))
    order.items = match.items.map(itemFromIncoming);
}

const itemsKey = (items: MItem[]) => items.map((i) => `${i.product}|${i.color}|${i.size}|${i.qty}|${i.sku ?? ""}|${i.skuId ?? ""}|` +
  (i.importPrices ? Object.entries(i.importPrices).map(([k, v]) => k + "=" + v).join(",") : "-")).join(";");

const seqEq = (a: string[], b: string[]) => a.length === b.length && a.every((v, i) => v === b[i]);

/** ParcelMerge.ApplyMatch: trả về true nếu có gì đổi. */
export function applyMatch(order: MOrder, match: Incoming, force: boolean, mode: ProductMode, year: number): boolean {
  const matchOrder = toOrder(match, "");
  if (!isSameReturnParcel(order, matchOrder)) return false;
  let changed = false;
  const im = order.import;
  im.importModeSlots = { ...match.importModeSlots };
  im.importSelectedPriceMode = match.importSelectedPriceMode;
  if (match.priceNeedsReview !== null) im.priceNeedsReview = match.priceNeedsReview;
  im.priceHasValue = match.priceHasValue;

  const codes = uniq([...codesOf(order), ...codesOf(matchOrder)]);
  if (!seqEq(order.trackingCodes, codes)) { order.trackingCodes = codes; changed = true; }

  const set = (cur: string, val: string, assign: (v: string) => void) => { if (cur === val) return false; assign(val); return true; };

  if (match.returnOrderId && !order.returnOrderId) { order.returnOrderId = match.returnOrderId; changed = true; }
  const platform = normalizePlatform(match.platform);
  if (platform && (force || !order.platform || order.platform === "Shopee")) changed = set(order.platform, platform, (v) => (order.platform = v)) || changed;
  if (match.orderType && (force || !order.orderType)) changed = set(order.orderType, match.orderType, (v) => (order.orderType = v)) || changed;
  if (match.preOrder) {
    if (match.preOrder === "Hàng đặt trước") changed = set(order.preOrder, "Hàng đặt trước", (v) => (order.preOrder = v)) || changed;
    else if (!order.preOrder) { order.preOrder = match.preOrder; changed = true; }
  }
  if (match.platformStatus && (force || !order.platformStatus)) changed = set(order.platformStatus, match.platformStatus, (v) => (order.platformStatus = v)) || changed;
  if (match.orderId && (force || !order.orderId)) changed = set(order.orderId, match.orderId, (v) => (order.orderId = v)) || changed;
  const ship = resolveShipDate(match, year);
  if (ship && (force || !order.shipDate)) changed = set(order.shipDate, ship, (v) => (order.shipDate = v)) || changed;
  if (match.returnDate && (force || !order.returnDate)) changed = set(order.returnDate, match.returnDate, (v) => (order.returnDate = v)) || changed;
  if (order.isPartialReturn !== match.isPartialReturn) { order.isPartialReturn = match.isPartialReturn; changed = true; }

  if (Object.keys(match.priceModes).length > 0 || Object.keys(match.priceModeFlags).length > 0) {
    const before = [...Object.entries(im.priceModes).map(([k, v]) => k + "=" + v), ...Object.entries(im.priceModeFlags).map(([k, v]) => k + "=" + (v ? "true" : "false"))].join(",");
    const newModes = { ...im.priceModes, ...match.priceModes };
    const newFlags = { ...im.priceModeFlags, ...match.priceModeFlags };
    const after = [...Object.entries(newModes).map(([k, v]) => k + "=" + v), ...Object.entries(newFlags).map(([k, v]) => k + "=" + (v ? "true" : "false"))].join(",");
    if (before !== after) { im.priceModes = newModes; im.priceModeFlags = newFlags; changed = true; }
  }
  if (match.priceHasValue && (force || order.price === 0) && order.price !== match.price) { order.price = match.price; changed = true; }
  if (match.carrier && (force || !order.carrier || order.carrier === "SPX Express")) changed = set(order.carrier, match.carrier, (v) => (order.carrier = v)) || changed;
  if (match.track && (force || !order.track)) changed = set(order.track, match.track, (v) => (order.track = v)) || changed;
  if (match.altTrack && (force || !order.altTrack)) changed = set(order.altTrack, match.altTrack, (v) => (order.altTrack = v)) || changed;
  if (match.forwardTrack && (force || !order.forwardTrack)) changed = set(order.forwardTrack, match.forwardTrack, (v) => (order.forwardTrack = v)) || changed;
  if (match.returnTrack && (force || !order.returnTrack)) changed = set(order.returnTrack, match.returnTrack, (v) => (order.returnTrack = v)) || changed;
  if (match.deadline && (force || !order.deadline)) changed = set(order.deadline, match.deadline, (v) => (order.deadline = v)) || changed;
  if (match.importPriceBasis) im.importPriceBasis = match.importPriceBasis;
  if (matchOrder.import.forwardCarrier && (force || !im.forwardCarrier)) changed = set(im.forwardCarrier, matchOrder.import.forwardCarrier, (v) => (im.forwardCarrier = v)) || changed;
  if (matchOrder.import.returnCarrier && (force || !im.returnCarrier)) changed = set(im.returnCarrier, matchOrder.import.returnCarrier, (v) => (im.returnCarrier = v)) || changed;
  if (match.rawDetails && Object.keys(match.rawDetails).length > 0) {
    const before = Object.entries(im.rawDetails).map(([k, v]) => k + "=" + v).join("\u0001");
    const merged = { ...im.rawDetails, ...match.rawDetails };
    if (Object.entries(merged).map(([k, v]) => k + "=" + v).join("\u0001") !== before) { im.rawDetails = merged; changed = true; }
  }
  if (match.note && (force || !order.note)) changed = set(order.note, match.note, (v) => (order.note = v)) || changed;
  if (match.buyerNote && (force || !order.buyerNote)) changed = set(order.buyerNote, match.buyerNote, (v) => (order.buyerNote = v)) || changed;

  if (matchOrder.items.length > 0) {
    const curFirst = order.items.length > 0 ? order.items[0]! : null;
    const isCurBlank = curFirst === null || (!curFirst.product && !curFirst.color && !curFirst.size);
    if (force || isCurBlank || matchOrder.items.length > order.items.length) {
      const before = itemsKey(order.items);
      order.items = mergeItems(order.items, matchOrder.items, mode);
      for (const it of order.items) {
        if (it.size && isReturnReasonText(it.size)) { if (!order.note) order.note = it.size; it.size = ""; }
        if (it.color && isReturnReasonText(it.color)) { if (!order.note) order.note = it.color; it.color = ""; }
        if (it.size && !it.color && isKnownColorName(it.size)) { it.color = it.size; it.size = ""; }
      }
      if (itemsKey(order.items) !== before) changed = true;
    } else {
      const f = matchOrder.items[0]!;
      const c = curFirst!;
      if (!c.product && f.product) { c.product = f.product; changed = true; }
      if (!c.color && f.color) { if (isReturnReasonText(f.color)) { if (!order.note) order.note = f.color; } else c.color = f.color; changed = true; }
      if (!c.size && f.size) { if (isReturnReasonText(f.size)) { if (!order.note) order.note = f.size; } else c.size = f.size; changed = true; }
      if ((c.qty === 0 || c.qty === 1) && f.qty > 0) { c.qty = f.qty; changed = true; }
    }
  }
  return changed;
}

// ------------------------------------------------------------------ TikTokMerger
const up = (s: string | null | undefined) => (s ?? "").trim().toUpperCase();

function realOrderId(o: MOrder): string {
  const id = up(o.orderId);
  if (!id) return "";
  if (id === up(o.track) || id === up(o.returnTrack) || id === up(o.forwardTrack) || id === up(o.altTrack)) return "";
  return id;
}
const orderIdConflict = (a: MOrder, b: MOrder) => { const x = realOrderId(a), y = realOrderId(b); return !!x && !!y && x !== y; };

function otherCodes(o: MOrder): Set<string> {
  const set = new Set<string>();
  for (const v of [o.track, o.altTrack, o.returnTrack, ...o.trackingCodes]) {
    const t = up(v);
    if (!t) continue;
    set.add(t);
    const c = cleanTrackingCode(t);
    if (c) set.add(c);
  }
  return set;
}

function buildIndex(others: readonly OtherEntry[]): Map<string, OtherWhere> {
  const map = new Map<string, OtherWhere>();
  const put = (k: string, w: OtherWhere) => { if (k && !map.has(k)) map.set(k, w); };
  for (const e of others) {
    const tr = otherCodes(e.order);
    for (const c of tr) put("T:" + c, e.where);
    const oid = up(e.order.orderId);
    if (oid) { put("O:" + oid, e.where); if (tr.size === 0) put("N:" + oid, e.where); }
  }
  return map;
}

function findInOther(index: Map<string, OtherWhere>, incoming: MOrder): OtherWhere | null {
  const tr = otherCodes(incoming);
  for (const c of tr) { const w = index.get("T:" + c); if (w) return w; }
  const oid = up(incoming.orderId);
  if (oid) {
    if (tr.size === 0) { const w = index.get("O:" + oid); if (w) return w; }
    if (tr.size > 0) { const w = index.get("N:" + oid); if (w) return w; }
  }
  return null;
}

function enrichArchived(others: readonly OtherEntry[], incoming: MOrder): { entry: MOrder | null; changed: boolean } {
  const codes = otherCodes(incoming);
  const oid = up(incoming.orderId);
  const entry = others.filter((e) => e.where === "archive").map((e) => e.order).find((o) => {
    const tr = otherCodes(o);
    for (const c of codes) if (tr.has(c)) return true;
    return !!oid && up(o.orderId) === oid && (codes.size === 0 || tr.size === 0);
  }) ?? null;
  if (!entry) return { entry: null, changed: false };
  let changed = false;
  type K = "orderId" | "returnOrderId" | "track" | "returnTrack" | "carrier" | "shipDate" | "platform" | "platformStatus" | "returnedAt" | "preOrder";
  const setv = (k: K, v: string) => { if (v && entry[k] !== v) { entry[k] = v; changed = true; } };
  const fill = (k: K, v: string) => { if (v && !entry[k]) { entry[k] = v; changed = true; } };
  const eTrack = up(entry.track);
  if (incoming.orderId && (!entry.orderId || up(entry.orderId) === eTrack)) setv("orderId", incoming.orderId);
  fill("returnOrderId", incoming.returnOrderId);
  fill("track", incoming.track);
  fill("returnTrack", incoming.returnTrack);
  fill("carrier", incoming.carrier);
  fill("shipDate", incoming.shipDate);
  if (incoming.platform) setv("platform", incoming.platform);
  setv("platformStatus", incoming.platformStatus);
  setv("returnedAt", incoming.returnedAt);
  fill("preOrder", incoming.preOrder);
  if (entry.price === 0 && incoming.price !== 0) { entry.price = incoming.price; changed = true; }
  const hasReal = entry.items.some((it) => it.product && !PLACEHOLDER.test(it.product));
  if (!hasReal && incoming.items.some((it) => it.product)) { entry.items = incoming.items.map(copyItem); changed = true; }
  const merged = uniq([...entry.trackingCodes, ...incoming.trackingCodes]);
  if (merged.length > 0 && !seqEq(entry.trackingCodes, merged)) { entry.trackingCodes = merged; changed = true; }
  return { entry, changed };
}

/**
 * TikTokMerger.Apply (nhánh nạp Shopee/Google Sheet). `main` bị sửa tại chỗ; kiện mới được thêm vào `main` với id do `newId` sinh.
 * `today` dạng "dd/MM/yyyy", `year` = năm hiện tại.
 */
export function applyImport(main: MOrder[], others: readonly OtherEntry[], parcels: readonly Incoming[], mode: ProductMode,
  today: string, year: number, newId: () => string, kind: NewRowKind = "shopee"): MergeOutcome {
  const out: MergeOutcome = { added: [], updated: [], enrichedArchived: [], ambiguous: [], skippedArchivedUnchanged: 0, skippedDeleted: 0, enriched: 0 };
  const index = buildIndex(others);
  for (const raw of parcels) {
    const incoming = toOrder(raw, "");
    const all = findAllMatchIndexes(main, incoming);
    if (all.length > 1) {
      out.ambiguous.push({ platform: incoming.platform, orderId: incoming.orderId, track: incoming.track, returnOrderId: incoming.returnOrderId, candidates: all.length, orderConflict: false });
      continue;
    }
    const existingIdx = all.length === 1 ? all[0]! : -1;
    if (existingIdx !== -1 && orderIdConflict(main[existingIdx]!, incoming)) {
      out.ambiguous.push({ platform: incoming.platform, orderId: incoming.orderId, track: incoming.track, returnOrderId: incoming.returnOrderId, candidates: 1, orderConflict: true });
      continue;
    }
    if (existingIdx === -1) {
      const fillIdx = findTracklessSameOrderIndex(main, incoming);
      const tracklessCount = fillIdx === -1 ? 0 : main.filter((o) => findTracklessSameOrderIndex([o], incoming) === 0).length;
      if (tracklessCount > 1) {
        // rà lỗi 26/11/2026 (= TikTokMerger C#): nhiều kiện chưa có mã cùng mã đơn -> không đoán kiện nào
        out.ambiguous.push({ platform: incoming.platform, orderId: incoming.orderId, track: incoming.track, returnOrderId: incoming.returnOrderId, candidates: tracklessCount, orderConflict: false });
        continue;
      }
      if (fillIdx !== -1) {
        fillTrackless(main[fillIdx]!, raw);
        out.updated.push(main[fillIdx]!);
        out.enriched++;
        continue;
      }
      const shopee = findShopeeTracklessSameOrderIndexes(main, incoming);
      if (shopee.length > 1) {
        out.ambiguous.push({ platform: incoming.platform, orderId: incoming.orderId, track: incoming.track, returnOrderId: incoming.returnOrderId, candidates: shopee.length, orderConflict: false });
        continue;
      }
      if (shopee.length === 1) {
        fillTrackless(main[shopee[0]!]!, raw);
        out.updated.push(main[shopee[0]!]!);
        out.enriched++;
        continue;
      }
    }
    if (existingIdx !== -1) {
      const order = main[existingIdx]!;
      const oldStatus = order.platformStatus;
      const changed = applyMatch(order, raw, true, mode, year);
      if (raw.platformStatus && raw.platformStatus !== oldStatus) order.platformStatus = raw.platformStatus;
      if (raw.returnedAt) order.returnedAt = raw.returnedAt;
      if (raw.buyerNote) order.buyerNote = raw.buyerNote;
      if (changed || (raw.platformStatus && raw.platformStatus !== oldStatus)) { out.enriched++; out.updated.push(order); }
      continue;
    }
    const where = findInOther(index, incoming);
    if (where === "deleted") { out.skippedDeleted++; continue; }
    if (where === "archive") {
      const r = enrichArchived(others, incoming);
      if (r.changed && r.entry) out.enrichedArchived.push(r.entry); else out.skippedArchivedUnchanged++;
      continue;
    }
    const row = newRow(raw, today, newId(), kind);
    main.push(row);
    out.added.push(row);
  }
  return out;
}
