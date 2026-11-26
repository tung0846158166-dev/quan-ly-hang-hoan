#!/usr/bin/env node
/**
 * Thử THẬT công cụ chuyển cloud trên máy (27/11/2026): hai cloud thử nghiệm chạy cục bộ (wrangler dev, dữ liệu riêng từng cổng),
 * không đụng cloud thật. Nạp dữ liệu GIẢ vào cloud A -> backup -> restore sang cloud B -> so từng đơn, lịch sử, ảnh.
 *   node scripts/cloud-transfer-e2e.mjs   (tự bật/tắt 2 cloud cục bộ)
 */
import { spawn, execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

const here = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Z]:)/, "$1"));
const root = path.resolve(here, "..");
const work = fs.mkdtempSync(path.join(os.tmpdir(), "qlhh-transfer-"));
const BOOT = "e2e-bootstrap-key", IMPORT = "e2e-import-key";

function startDev(port, name) {
  const p = spawn("npx", ["wrangler", "dev", "--local", "--port", String(port), "--persist-to", path.join(work, name),
    "--var", `BOOTSTRAP_KEY:${BOOT}`, "--var", `IMPORT_KEY:${IMPORT}`, "--var", "AI_OFF:1", "--var", "JOIN_KEY:e2e-join-key"],
    { cwd: root, shell: true, stdio: ["ignore", "pipe", "pipe"] });
  p.stdout.on("data", () => {}); p.stderr.on("data", () => {});
  return p;
}

async function waitUp(url) {
  for (let i = 0; i < 120; i++) {
    try { const r = await fetch(url + "/v1/me"); if (r.status === 401 || r.ok) return; } catch { /* chưa lên */ }
    await new Promise((r) => setTimeout(r, 1000));
  }
  throw new Error("cloud cục bộ không lên: " + url);
}

async function api(url, method, p, body, token, extra = {}) {
  const headers = { ...extra };
  if (token) headers.Authorization = `Bearer ${token}`;
  if (body !== undefined && !(body instanceof Uint8Array)) headers["Content-Type"] = "application/json";
  const r = await fetch(url + "/v1" + p, { method, headers, body: body === undefined ? undefined : body instanceof Uint8Array ? body : JSON.stringify(body) });
  const t = await r.text();
  let j; try { j = t ? JSON.parse(t) : null; } catch { j = t; }
  if (!r.ok) throw new Error(`${method} ${p}: ${r.status} ${t.slice(0, 200)}`);
  return j;
}

const A = "http://127.0.0.1:8791", B = "http://127.0.0.1:8792";
const devs = [startDev(8791, "a"), startDev(8792, "b")];
const killAll = () => { for (const d of devs) { try { if (process.platform === "win32") execFileSync("taskkill", ["/pid", String(d.pid), "/T", "/F"], { stdio: "ignore" }); else d.kill(); } catch { /* đã tắt */ } } };
process.on("exit", killAll);

try {
  await waitUp(A); await waitUp(B);
  // --- cloud A (cũ): chủ cửa hàng + điện thoại + dữ liệu giả
  const ownerA = (await api(A, "POST", "/pair/bootstrap", { bootstrapKey: BOOT, deviceName: "Máy chính", kind: "pc" })).token;
  const code = (await api(A, "POST", "/pair/start", {}, ownerA)).code;
  const phone = (await api(A, "POST", "/pair/complete", { code, deviceName: "iPhone kho 1", kind: "phone" })).token;
  const parcels = Array.from({ length: 30 }, (_, i) => ({ id: `e${i}`, orderId: `O-e${i}`, track: `SPXE2E${i}`, platform: i % 2 ? "Shopee" : "TikTok", orderType: "Trả hàng", status: "Chưa trả", price: 1000 * i, items: [{ product: "Áo giả", qty: 1 }] }));
  await api(A, "POST", "/import", { source: "file", signature: "s", parcels }, undefined, { "X-Import-Key": IMPORT });
  for (let i = 0; i < 10; i++) await api(A, "POST", "/ops", { ops: [{ opId: crypto.randomUUID(), type: "scan", code: `SPXE2E${i}` }] }, phone);
  const img = new Uint8Array(Array.from({ length: 2048 }, (_, i) => (i * 37) % 256)); img[0] = 0xff; img[1] = 0xd8;
  for (let i = 0; i < 5; i++) {
    await api(A, "PUT", `/photos/e2e_photo_000${i}?parcelId=e${i}&mime=image/jpeg`, img, phone);
    await api(A, "PUT", `/photos/e2e_photo_000${i}?parcelId=e${i}&mime=image/jpeg&kind=thumb`, img.slice(0, 300), phone);
  }
  await api(A, "POST", "/ops", { ops: [
    { opId: crypto.randomUUID(), type: "editCell", id: "e20", field: "complaintStatus", value: "Đang xử lý" },
    { opId: crypto.randomUUID(), type: "editNote", id: "e21", userNote: "ghi chú giả" },
    { opId: crypto.randomUUID(), type: "setWatch", id: "e22", on: true, reason: "theo dõi giả" },
    { opId: crypto.randomUUID(), type: "archive", id: "e23" },
    { opId: crypto.randomUUID(), type: "delete", id: "e24" },
  ] }, ownerA);

  // --- cloud B (mới, trống): chỉ có chủ cửa hàng mới
  const ownerB = (await api(B, "POST", "/pair/bootstrap", { bootstrapKey: BOOT, deviceName: "Máy chính (mới)", kind: "pc" })).token;

  const dir = path.join(work, "saoluu");
  const run = (args, env) => execFileSync(process.execPath, [path.join(here, "cloud-transfer.mjs"), ...args], { env: { ...process.env, ...env }, encoding: "utf8" });
  console.log(run(["backup", "--url", A, "--token-env", "TOK", "--out", dir], { TOK: ownerA }));
  console.log(run(["restore", "--url", B, "--token-env", "TOK", "--dir", dir], { TOK: ownerB }));

  // --- so sánh từng đơn (kể cả rev), lịch sử, ảnh
  const all = async (u, t) => (await api(u, "GET", "/sync?since=0&limit=1000", undefined, t)).changes;
  const strip = (cs) => JSON.stringify(cs.map((c) => { const x = { ...c }; if (x.parcel) { x.parcel = { ...x.parcel }; delete x.parcel.updatedAt; } return x; }).sort((a, b) => JSON.stringify(a).localeCompare(JSON.stringify(b))));
  const fails = [];
  if (strip(await all(A, ownerA)) !== strip(await all(B, ownerB))) fails.push("danh sách đơn / Bảng Hàng Hoàn Về khác nhau");
  for (const id of ["e0", "e3", "e20"]) {
    const ha = JSON.stringify(await api(A, "GET", `/parcels/${id}/history`, undefined, ownerA));
    const hb = JSON.stringify(await api(B, "GET", `/parcels/${id}/history`, undefined, ownerB));
    if (ha !== hb) fails.push(`lịch sử ${id} khác nhau`);
  }
  for (let i = 0; i < 5; i++) {
    const r = await fetch(`${B}/v1/photos/e2e_photo_000${i}`, { headers: { Authorization: `Bearer ${ownerB}` } });
    const b = new Uint8Array(await r.arrayBuffer());
    if (b.length !== img.length || b.some((v, k) => v !== img[k])) fails.push(`ảnh ${i} khác`);
  }
  // máy cũ không vào được cloud mới
  const old = await fetch(`${B}/v1/me`, { headers: { Authorization: `Bearer ${phone}` } });
  if (old.status !== 401) fails.push("máy cũ vẫn vào được cloud mới");
  // cloud mới nhận quét tiếp bình thường
  const sc = await api(B, "POST", "/ops", { ops: [{ opId: crypto.randomUUID(), type: "scan", code: "SPXE2E15" }] }, ownerB);
  if (sc.results[0]?.result?.outcome !== "received") fails.push("cloud mới không quét tiếp được");
  if (fails.length) { console.log("THẤT BẠI:\n  " + fails.join("\n  ")); process.exitCode = 1; }
  else console.log("THỬ THẬT ĐẠT: 30 đơn (10 đã nhận, khiếu nại, ghi chú, theo dõi, lưu trữ, đã xóa), lịch sử, 5 ảnh + ảnh nhỏ chép nguyên trạng; máy cũ bị chặn; cloud mới quét tiếp được.");
} catch (e) {
  console.error("LỖI: " + e.message); process.exitCode = 1;
} finally {
  killAll();
  try { fs.rmSync(work, { recursive: true, force: true }); } catch { /* bỏ qua */ }
}
