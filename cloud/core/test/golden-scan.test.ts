import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { decide } from "../src/scan";
import { normalize, type ParcelKeys } from "../src/tracking";

interface Expect { kind: string; orderId: string | null; candidates: number }
interface Case { name: string; orders: ParcelKeys[]; code?: string; expect?: Expect; multi?: { code: string; expect: Expect }[] }

const here = dirname(fileURLToPath(import.meta.url));
const vectors = JSON.parse(readFileSync(join(here, "..", "..", "golden", "scan-vectors.json"), "utf8")) as { cases: Case[] };

describe("bộ ca thử chung với C# (cloud/golden/scan-vectors.json)", () => {
  for (const c of vectors.cases) {
    const runs = c.multi ?? [{ code: c.code ?? "", expect: c.expect as Expect }];
    for (const r of runs) {
      it(`${c.name} — mã ${JSON.stringify(r.code)}`, () => {
        const d = decide(c.orders, r.code);
        expect({ kind: d.kind, orderId: d.order?.id ?? null, candidates: d.candidates }).toEqual(r.expect);
      });
    }
  }

  it("có đủ ca thử", () => {
    const total = vectors.cases.reduce((n, c) => n + (c.multi?.length ?? 1), 0);
    expect(total).toBeGreaterThanOrEqual(20);
  });
});

describe("normalize", () => {
  it("bỏ khoảng trắng và ký tự vô hình, chuyển chữ hoa", () => {
    expect(normalize("  spx ​12﻿3 ")).toBe("SPX123");
    expect(normalize(null)).toBe("");
    expect(normalize("mã-đơn")).toBe("MÃ-ĐƠN");
  });
});
