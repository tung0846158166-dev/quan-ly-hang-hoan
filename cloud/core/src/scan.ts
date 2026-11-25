import { resolve, type ParcelKeys } from "./tracking";

/** Trạng thái nhận kiện (giống v2 ReceiveStatus; chuỗi tiếng Việt là giá trị thật trong dữ liệu). */
export const ReceiveStatus = {
  received: "Hàng đã trả",
  notReturned: "Chưa trả",
  notNeeded: "Không cần nhận",
  lost: "Bị mất",
  notYetArrived: "Chưa về",
} as const;

export type ScanDecisionKind = "receive" | "alreadyReceived" | "ambiguous" | "notFound" | "needsManualCheck";

export interface ScanDecision<T extends ParcelKeys = ParcelKeys> {
  kind: ScanDecisionKind;
  order: T | null;
  candidates: number;
}

/**
 * Quyết định THUẦN cho một lượt quét (bản TypeScript của v2 ScanDecider). Phần ghi — UPDATE có điều kiện trạng thái cũ,
 * ACK chỉ sau khi đã ghi bền — nằm ở lớp lưu trữ (quy tắc 6).
 */
export function decide<T extends ParcelKeys>(activeOrders: readonly T[], code: string): ScanDecision<T> {
  const m = resolve(activeOrders, code);
  if (m.kind === "notFound") return { kind: "notFound", order: null, candidates: 0 };
  if (m.kind === "ambiguous") return { kind: "ambiguous", order: null, candidates: m.candidates.length };

  const o = m.candidates[0] as T;
  const status = o.status ?? "";
  if (status === ReceiveStatus.received) return { kind: "alreadyReceived", order: o, candidates: 1 };
  if (status !== ReceiveStatus.notReturned && status !== ReceiveStatus.notYetArrived && status !== "")
    return { kind: "needsManualCheck", order: o, candidates: 1 };
  return { kind: "receive", order: o, candidates: 1 };
}
