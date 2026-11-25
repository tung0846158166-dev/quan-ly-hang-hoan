/**
 * Khớp mã vận đơn — bản TypeScript của v2/src/QuanLyHangHoan.Core/Tracking/TrackingResolver.cs.
 * Quy tắc (AGENTS.md 1-4): mã riêng của chính kiện ưu tiên cao nhất; mã giao đi dùng chung / mã đơn gốc / mã yêu cầu hoàn chỉ là ứng viên,
 * nhiều ứng viên => "ambiguous"; nhận kiện A không bao giờ nhận B/C.
 */

/** Một kiện đang hoạt động (chỉ các trường dùng để khớp mã và quyết định quét). */
export interface ParcelKeys {
  id: string;
  orderId?: string;
  returnOrderId?: string;
  track?: string;
  returnTrack?: string;
  altTrack?: string;
  forwardTrack?: string;
  trackingCodes?: string[];
  scanAliases?: string[];
  status?: string;
}

export type MatchKind = "match" | "notFound" | "ambiguous";

export interface MatchResult<T extends ParcelKeys = ParcelKeys> {
  kind: MatchKind;
  candidates: T[];
  via: string;
}

/** Bỏ khoảng trắng và ký tự vô hình (U+200B, U+FEFF), chuyển chữ hoa (giống C#: ToUpperInvariant). */
export function normalize(code: string | null | undefined): string {
  if (code === null || code === undefined) return "";
  // C# char.IsWhiteSpace: khoảng trắng Unicode; JS \s bao gồm cả U+FEFF nên liệt kê tường minh cho rõ.
  return code.replace(/[\s​﻿]+/gu, "").toUpperCase();
}

/** Mã thuộc về CHÍNH kiện (không gồm mã giao đi chung / mã đơn gốc). */
export function ownCodes(o: ParcelKeys): string[] {
  return [
    normalize(o.track),
    normalize(o.returnTrack),
    normalize(o.altTrack),
    ...(o.trackingCodes ?? []).map(normalize),
    ...(o.scanAliases ?? []).map(normalize),
  ];
}

export function resolve<T extends ParcelKeys>(orders: readonly T[], rawCode: string): MatchResult<T> {
  const code = normalize(rawCode);
  if (code.length === 0) return { kind: "notFound", candidates: [], via: "empty" };

  const own = orders.filter((o) => ownCodes(o).some((c) => c.length > 0 && c === code));
  if (own.length === 1) return { kind: "match", candidates: own, via: "own-track" };
  if (own.length > 1) return { kind: "ambiguous", candidates: own, via: "own-track" };

  const weak = orders.filter(
    (o) => normalize(o.forwardTrack) === code || normalize(o.orderId) === code || normalize(o.returnOrderId) === code,
  );
  if (weak.length === 1) return { kind: "match", candidates: weak, via: "forward-or-order" };
  if (weak.length > 1) return { kind: "ambiguous", candidates: weak, via: "forward-or-order" };

  return { kind: "notFound", candidates: [], via: "none" };
}
