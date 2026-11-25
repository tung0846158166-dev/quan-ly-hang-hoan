import { ReceiveStatus } from "../../core/src/scan";
import type { Parcel } from "./parcel";

/**
 * BẢNG HÀNG HOÀN VỀ (bản TypeScript của v2 ReturnWatchService): đơn chưa nhận mà trạng thái sàn là "Đã đồng ý" tự vào bảng.
 * TikTok không có "Đã đồng ý": đơn đang chờ khách gửi / đang về cũng vào. Vào bảng rồi thì ở lại; chỉ người dùng mới bỏ ra (cờ `off`).
 */
const AGREED = /(đồng|dong)\s*(ý|y)|agreed/i;
const TIKTOK_ENTRY = /cho khach gui hang|dang ve\b|dang van chuyen ve/;

/** Bỏ dấu tiếng Việt, đ -> d, chữ thường (giống Fold của C#). */
export function fold(s: string): string {
  return s.normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/đ/g, "d").replace(/Đ/g, "D").toLowerCase();
}

export function isReceived(p: Pick<Parcel, "status">): boolean {
  return p.status === ReceiveStatus.received || p.status === "Đã nhận";
}

function entryStatus(p: Parcel): boolean {
  const ps = p.platformStatus ?? "";
  if (ps.length === 0) return false;
  if (AGREED.test(ps.normalize("NFC"))) return true;
  return (p.platform === "TikTok" || p.platform === "TikTok Shop") && TIKTOK_ENTRY.test(fold(ps));
}

/** Đủ điều kiện TỰ vào bảng: có trạng thái vào bảng, chưa nhận, không phải "Không cần nhận"/"Bị mất", đang ở bảng chính. */
export function returnWatchEligible(p: Parcel): boolean {
  if (p.isArchived || p.isDeleted) return false;
  return entryStatus(p) && !isReceived(p) && p.status !== ReceiveStatus.notNeeded && p.status !== ReceiveStatus.lost;
}
