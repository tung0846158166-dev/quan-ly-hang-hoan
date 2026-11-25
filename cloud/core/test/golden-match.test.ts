import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { cleanTrackingCode, findExistingIndex, findTracklessSameOrderIndex, isSameReturnParcel, type MatchParcel } from "../src/matching";

interface Scenario { orders: MatchParcel[]; incoming: MatchParcel; expected: { same: boolean[]; existing: number; trackless: number } }

const here = dirname(fileURLToPath(import.meta.url));
const file = join(here, "..", "..", "..", "v2", "tests", "QuanLyHangHoan.Tests", "Golden", "match.golden.json");
const scenarios = JSON.parse(readFileSync(file, "utf8")) as Scenario[];

describe("so khớp kiện khi nhập: 1.500 tình huống do mã JS cũ sinh (cùng tệp C# dùng)", () => {
  it("mọi tình huống khớp kết quả mã cũ", () => {
    expect(scenarios.length).toBeGreaterThanOrEqual(1000);
    const diffs: string[] = [];
    scenarios.forEach((s, n) => {
      s.orders.forEach((o, i) => {
        if (isSameReturnParcel(o, s.incoming) !== s.expected.same[i]) diffs.push(`#${n} same[${i}]`);
      });
      const ex = findExistingIndex(s.orders, s.incoming);
      if (ex !== s.expected.existing) diffs.push(`#${n} existing: cũ=${s.expected.existing} ts=${ex}`);
      const tr = findTracklessSameOrderIndex(s.orders, s.incoming);
      if (tr !== s.expected.trackless) diffs.push(`#${n} trackless: cũ=${s.expected.trackless} ts=${tr}`);
    });
    expect(diffs.slice(0, 30)).toEqual([]);
  });

  it("cleanTrackingCode tách mã từ link và chuỗi lẫn", () => {
    expect(cleanTrackingCode("https://spx.vn/track?spx=spxvn0123456789")).toBe("SPXVN0123456789");
    expect(cleanTrackingCode("  *abc123* ")).toBe("ABC123");
    expect(cleanTrackingCode("xx/SPXVN0123456789")).toBe("SPXVN0123456789");
    expect(cleanTrackingCode("")).toBe("");
  });
});
