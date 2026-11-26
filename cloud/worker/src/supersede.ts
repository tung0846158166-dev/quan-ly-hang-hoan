/**
 * Bản cloud của SaleworkSupersede (C#, v2/src/QuanLyHangHoan.Core/Import/SaleworkSupersede.cs) — chạy sau khi nạp Salework (26/11/2026).
 * Salework "Đang chờ hoàn" là trạng thái MỚI NHẤT của đơn: đơn có dòng "chờ" CHƯA có mã vận đơn nào mà nay Salework có dòng mang mã vận đơn
 * của cùng mã đơn -> dòng cũ chỉ là bản cũ: chuyển dữ liệu kho (đã nhận + ngày nhận, ảnh, ghi chú, theo dõi) sang dòng mới, dòng cũ xóa MỀM.
 * Nhiều kiện KHÁC mã vận đơn thì không đoán: chỉ điền mã còn trống khi mọi dòng Salework của đơn chung 1 mã giao / 1 mã hoàn.
 */
import { cleanTrackingCode } from "../../core/src/matching";
import type { Parcel } from "./parcel";

export interface SupersedeResult {
  merged: { stale: Parcel; keeper: Parcel }[];
  filled: Parcel[];
  keepersChanged: Parcel[];
  ambiguous: number;
}

const up = (s: string | null | undefined) => (s ?? "").trim().toUpperCase();
const own = (o: Parcel) => cleanTrackingCode(o.returnTrack ? o.returnTrack : o.track ? o.track : o.forwardTrack ? o.forwardTrack : o.altTrack);
export const isReceived = (o: Parcel) => o.status === "Hàng đã trả" || o.status === "Đã nhận";
export const isScanPlaceholder = (o: Parcel) =>
  /^Chưa rõ sản phẩm/.test(o.items[0]?.product ?? "") || /^Hàng đã về \(điện thoại quét/.test(o.note ?? "");

/** Chỉ CHUYỂN, không ghi đè dữ liệu đã có ở dòng giữ. "Đã nhận" không có ngày nhận thì không chép (rà lỗi 26/11/2026). */
export function mergeWarehouse(src: Parcel, dst: Parcel): boolean {
  let changed = false;
  if (isReceived(src) && !isReceived(dst) && src.recvDate.trim().length > 0) { dst.status = src.status; changed = true; }
  if (!dst.recvDate && src.recvDate) { dst.recvDate = src.recvDate; changed = true; }
  if (!dst.photoId && src.photoId) { dst.photoId = src.photoId; changed = true; }
  const sn = (src.userNote ?? "").trim();
  if (sn && !(dst.userNote ?? "").includes(sn)) { dst.userNote = (dst.userNote ?? "").trim() ? dst.userNote + " · " + sn : sn; changed = true; }
  if (src.isWatchlist && !dst.isWatchlist) {
    dst.isWatchlist = true;
    if (src.watchlistReason && !dst.watchlistReason) dst.watchlistReason = src.watchlistReason;
    changed = true;
  }
  return changed;
}

export function applySupersede(main: readonly Parcel[], orderIds: ReadonlySet<string>): SupersedeResult {
  const res: SupersedeResult = { merged: [], filled: [], keepersChanged: [], ambiguous: 0 };
  if (orderIds.size === 0) return res;
  const by = new Map<string, Parcel[]>();
  for (const o of main) {
    const k = up(o.orderId);
    if (!k || !orderIds.has(k)) continue;
    if (!by.has(k)) by.set(k, []);
    by.get(k)!.push(o);
  }
  for (const rows of by.values()) {
    const tracked = rows.filter((o) => own(o).length > 0);
    const bare = rows.filter((o) => own(o).length === 0 && !isScanPlaceholder(o));
    if (tracked.length === 0 || bare.length === 0) continue;
    if (new Set(tracked.map(own)).size !== 1) {
      res.ambiguous++;
      fillOnly(bare, tracked, res);
      continue;
    }
    const keep = tracked.find(isReceived) ?? tracked[0]!;
    for (const stale of bare) {
      if (mergeWarehouse(stale, keep) && !res.keepersChanged.includes(keep)) res.keepersChanged.push(keep);
      res.merged.push({ stale, keeper: keep });
    }
  }
  return res;
}

function fillOnly(bare: Parcel[], tracked: Parcel[], res: SupersedeResult): void {
  const one = (f: (o: Parcel) => string) => {
    const s = [...new Set(tracked.map((o) => cleanTrackingCode(f(o))).filter((c) => c.length > 0))];
    return s.length === 1 ? s[0]! : "";
  };
  const fwd = one((o) => o.forwardTrack), ret = one((o) => o.returnTrack);
  for (const o of bare) {
    let ch = false;
    if (fwd && !o.forwardTrack) { o.forwardTrack = fwd; ch = true; }
    if (ret && !o.returnTrack) { o.returnTrack = ret; ch = true; }
    if (!ch) continue;
    o.trackingCodes = [...new Set([...o.trackingCodes, fwd, ret].filter((c) => c.length > 0))];
    res.filled.push(o);
  }
}
