import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { applyImport, type Incoming, type MOrder, type OtherEntry, type ProductMode } from "../src/merge";

interface Case {
  input: { main: MOrder[]; others: { where: "archive" | "deleted"; order: MOrder }[]; incoming: Incoming[]; mode: ProductMode; today: string; year: number };
  expected: { main: MOrder[]; others: MOrder[]; updated: string[]; enrichedArchived: string[]; ambiguous: unknown[]; skippedArchivedUnchanged: number; skippedDeleted: number; enriched: number };
}

const here = dirname(fileURLToPath(import.meta.url));
const data = JSON.parse(readFileSync(join(here, "..", "..", "golden", "merge-vectors.json"), "utf8")) as { cases: Case[] };

describe("gộp kiện nhập vào dữ liệu có sẵn: cùng kết quả với C# (cloud/golden/merge-vectors.json)", () => {
  it("mọi ca thử khớp", () => {
    expect(data.cases.length).toBeGreaterThanOrEqual(500);
    const diffs: string[] = [];
    data.cases.forEach((c, n) => {
      const main: MOrder[] = JSON.parse(JSON.stringify(c.input.main));
      const others: OtherEntry[] = JSON.parse(JSON.stringify(c.input.others));
      let k = 0;
      const res = applyImport(main, others, c.input.incoming, c.input.mode, c.input.today, c.input.year, () => "new" + k++);
      const got = {
        main, others: others.map((e) => e.order), updated: res.updated.map((o) => o.id), enrichedArchived: res.enrichedArchived.map((o) => o.id),
        ambiguous: res.ambiguous, skippedArchivedUnchanged: res.skippedArchivedUnchanged, skippedDeleted: res.skippedDeleted, enriched: res.enriched,
      };
      const a = JSON.stringify(got), b = JSON.stringify(c.expected);
      if (a !== b) {
        // tìm trường đầu tiên lệch để dễ sửa
        let i = 0;
        while (i < a.length && a[i] === b[i]) i++;
        diffs.push(`#${n}: ...${b.slice(Math.max(0, i - 120), i + 60)}\n   ts: ...${a.slice(Math.max(0, i - 120), i + 60)}`);
      }
    });
    expect(diffs.slice(0, 5).join("\n\n")).toBe("");
  });
});
