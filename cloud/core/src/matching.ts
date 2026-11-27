/**
 * So khớp "cùng một kiện hoàn" khi NHẬP (bản TypeScript của v2 Core/Import/ParcelMatching.cs + LegacyText.CleanTrackingCode,
 * vốn chuyển nguyên văn từ app cũ isSameReturnParcel / findExistingReturnParcelIndex / findTracklessSameOrderIndex).
 * Kiểm chứng bằng 1.500 tình huống ngẫu nhiên do MÃ JS CŨ sinh ra (v2/tests/.../Golden/match.golden.json).
 */

export interface MatchParcel {
  platform?: string;
  orderId?: string;
  returnOrderId?: string;
  track?: string;
  returnTrack?: string;
  forwardTrack?: string;
  altTrack?: string;
  trackingCodes?: string[];
}

/** cleanTrackingCode: tách mã vận đơn từ link/chuỗi quét, viết hoa. */
export function cleanTrackingCode(raw: string | null | undefined): string {
  if (!raw) return "";
  let str = raw.trim();
  if (str.includes("http://") || str.includes("https://") || str.includes("spx.vn") || str.includes("ghn.vn")) {
    const mp = /(?:spx|track|code|c|id|bill|order_code|s)=([A-Za-z0-9.\-_]+)/i.exec(str);
    if (mp && mp[1]) str = mp[1];
  }
  for (const pattern of [
    /(SPXVN[0-9A-Za-z]{6,18}|VN2[4-9][0-9A-Za-z]{8,14})/i,
    /(SOC[0-9A-Za-z]{6,16})/i,
    /((?:NIVN|NLVN|SHPVN)[0-9A-Za-z]{6,16})/i,
    /(TTVN[0-9A-Za-z]{6,16})/i,
    /(SHOPEEVTP[0-9A-Za-z]{6,18}|VTP?[0-9A-Za-z]{6,15})/i,
    /(GHN[0-9A-Za-z]{6,14}|G8[0-9A-Za-z]{6,14}|GY[0-9A-Za-z]{6,10})/i,
    /(S[0-9]+[.\-][0-9A-Za-z.\-]{6,24})/i,
  ]) {
    const m = pattern.exec(str);
    if (m && m[1]) return m[1].toUpperCase();
  }
  const mj = /(?<![0-9])(8[0246][0-9]{10,13})(?![0-9])/.exec(str);
  if (mj && mj[1]) return mj[1];
  const cleaned = str.replace(/^[*"'<>#\s[\]()]+|[*"'<>#\s[\]()]+$/g, "").trim();
  if (cleaned.includes("/") || cleaned.includes("|") || cleaned.includes(";")) {
    for (const p of cleaned.split(/[/|;]/)) {
      const pc = cleanTrackingCode(p);
      if (pc.length > 0) return pc;
    }
  }
  return cleaned.toUpperCase();
}

const up = (s: string | undefined) => (s ?? "").trim().toUpperCase();
const clean = (s: string | undefined) => cleanTrackingCode(s ?? "");
const first = (...v: (string | undefined)[]) => v.find((x) => x !== undefined && x !== "") ?? "";

export const returnParcelId = (o: MatchParcel) => up(o.returnOrderId);

export function isSameReturnParcel(left: MatchParcel, right: MatchParcel): boolean {
  const leftId = returnParcelId(left), rightId = returnParcelId(right);
  if (leftId && rightId && leftId !== rightId) return false;

  const leftReturn = clean(left.returnTrack), rightReturn = clean(right.returnTrack);
  const sameOrder = !!left.orderId && up(left.orderId) === up(right.orderId);

  if (sameOrder && leftId && leftId === rightId) {
    if (!leftReturn && rightReturn && right.forwardTrack && clean(left.track) === clean(right.forwardTrack)) return true;
    if (!rightReturn && leftReturn && left.forwardTrack && clean(right.track) === clean(left.forwardTrack)) return true;
  }

  if (sameOrder) {
    const fwdOnly = (o: MatchParcel) => !clean(o.returnTrack) && !returnParcelId(o) && clean(first(o.track, o.forwardTrack)).length > 0;
    const sameFwd = (a: MatchParcel, b: MatchParcel) => clean(b.forwardTrack).length > 0 && clean(first(a.track, a.forwardTrack)) === clean(b.forwardTrack);
    if (fwdOnly(left) && rightReturn && sameFwd(left, right)) return true;
    if (fwdOnly(right) && leftReturn && sameFwd(right, left)) return true;
  }

  const leftPrimary = clean(first(left.returnTrack, left.track));
  const rightPrimary = clean(first(right.returnTrack, right.track));
  if (leftPrimary && rightPrimary) {
    if (leftPrimary === rightPrimary) return true;
    const leftForward = clean(left.forwardTrack), rightForward = clean(right.forwardTrack);
    return (rightPrimary !== leftForward && (left.trackingCodes ?? []).includes(rightPrimary)) ||
      (leftPrimary !== rightForward && (right.trackingCodes ?? []).includes(leftPrimary));
  }
  if (leftId && rightId) return leftId === rightId;
  if (leftId || rightId || leftPrimary || rightPrimary) return false;
  return !!left.orderId && up(left.orderId) === up(right.orderId);
}

/** Chỉ trả về khi có ĐÚNG MỘT kiện khớp; không có hoặc nhiều hơn một => -1 (không đoán). */
export function findExistingIndex(orders: readonly MatchParcel[], incoming: MatchParcel): number {
  let found = -1, count = 0;
  for (let i = 0; i < orders.length; i++) {
    if (isSameReturnParcel(orders[i] as MatchParcel, incoming)) {
      found = i;
      if (++count > 1) return -1;
    }
  }
  return count === 1 ? found : -1;
}

export function findAllMatchIndexes(orders: readonly MatchParcel[], incoming: MatchParcel): number[] {
  const out: number[] = [];
  orders.forEach((o, i) => { if (isSameReturnParcel(o, incoming)) out.push(i); });
  return out;
}

/** Dòng chưa có mã nào cùng mã đơn (do file hoàn tiền TikTok / Google Sheet tạo) được điền mã thay vì tạo kiện mới. */
/**
 * Shopee (quyết định 26/11/2026, = ParcelMatching.FindShopeeTracklessSameOrderIndexes): kiện mới CÓ mã vận đơn, KHÔNG có mã yêu cầu trả;
 * các kiện Shopee cùng mã đơn chưa có mã vận đơn nào và không có mã yêu cầu trả. Đúng một -> điền; từ hai trở lên -> nhập nhằng.
 */
/**
 * Chủ dự án 28/11/2026 (ảnh 31, = ParcelMatching.IsCodelessShopeeOfKnownOrder): dòng Shopee KHÔNG có mã nào (không mã vận đơn, không mã yêu cầu
 * trả — vd Google Sheet "Chưa gửi") mà đơn đó ĐÃ có kiện thì KHÔNG tạo kiện mới (trước đây tạo bản trùng "Chưa có MVĐ").
 */
export function isCodelessShopeeOfKnownOrder(known: readonly MatchParcel[], incoming: MatchParcel): boolean {
  if (incoming.platform !== "Shopee" || returnParcelId(incoming)) return false;
  if (clean(first(incoming.returnTrack, incoming.track, incoming.forwardTrack, incoming.altTrack))) return false;
  if ((incoming.trackingCodes ?? []).some((c) => clean(c))) return false;
  const oid = up(incoming.orderId);
  return !!oid && known.some((o) => up(o.orderId) === oid && o.platform === "Shopee");
}

export function findShopeeTracklessSameOrderIndexes(orders: readonly MatchParcel[], incoming: MatchParcel): number[] {
  if (incoming.platform !== "Shopee" || returnParcelId(incoming)) return [];
  if (!clean(first(incoming.returnTrack, incoming.track, incoming.forwardTrack))) return [];
  const oid = up(incoming.orderId);
  if (!oid) return [];
  const out: number[] = [];
  for (let i = 0; i < orders.length; i++) {
    const o = orders[i] as MatchParcel;
    if (up(o.orderId) === oid && o.platform === "Shopee" && !returnParcelId(o) &&
      !clean(o.track) && !clean(o.returnTrack) && !clean(o.forwardTrack) && !clean(o.altTrack)) out.push(i);
  }
  return out;
}

export function findTracklessSameOrderIndex(
  orders: readonly MatchParcel[], incoming: MatchParcel,
  platformOk: (p: string) => boolean = (p) => p === "TikTok Shop" || p === "TikTok",
): number {
  if (returnParcelId(incoming)) return -1;
  if (!clean(first(incoming.returnTrack, incoming.track, incoming.forwardTrack))) return -1;
  const oid = up(incoming.orderId);
  if (!oid) return -1;
  for (let i = 0; i < orders.length; i++) {
    const o = orders[i] as MatchParcel;
    if (up(o.orderId) !== oid) continue;
    if (o.platform && !platformOk(o.platform)) continue;
    if (clean(o.track) || clean(o.returnTrack) || clean(o.forwardTrack) || clean(o.altTrack)) continue;
    return i;
  }
  return -1;
}
