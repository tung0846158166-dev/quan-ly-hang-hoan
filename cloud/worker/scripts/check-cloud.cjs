// Kiểm tra dữ liệu trên cloud (chỉ ĐỌC). Chạy: node cloud/worker/scripts/check-cloud.cjs [parcels.ndjson]
// - Đọc URL + OWNER_TOKEN + IMPORT_KEY từ cloud/worker/.secrets.local (không in ra).
// - Báo: tổng kiện, kiện do Sheet tạo (id s_), kiện thiếu mã đơn, nhóm trùng (cùng mã đơn + mã vận đơn), kiện Sheet chung mã với kiện cũ,
//   ca nhập nhằng, dấu Bảng Hàng Hoàn Về, hạn mức.
// - Có parcels.ndjson (CloudMigrate dry-run --out): so từng trường của đơn máy tính với cloud, đếm trường bị đổi + 1 ví dụ (mã che bớt).
"use strict";
const fs = require("fs");
const path = require("path");

const secrets = fs.readFileSync(path.join(__dirname, "..", ".secrets.local"), "utf8");
const env = Object.fromEntries(secrets.split(/\r?\n/).filter((l) => l.includes("=")).map((l) => [l.slice(0, l.indexOf("=")), l.slice(l.indexOf("=") + 1).trim()]));
const base = env.URL.replace(/\/+$/, "").replace(/\/v1$/, "");
const H = { Authorization: "Bearer " + env.OWNER_TOKEN };
const N = (s) => String(s || "").toUpperCase().replace(/[^A-Z0-9]/g, "");
const mask = (s) => String(s).replace(/[0-9A-Z]{8,}/g, "[mã]");

(async () => {
  const parcels = new Map(), watch = new Map();
  for (let since = 0; ;) {
    const j = await (await fetch(`${base}/v1/sync?since=${since}&limit=1000`, { headers: H })).json();
    for (const c of j.changes) c.type === "parcel" ? parcels.set(c.parcel.id, c.parcel) : watch.set(c.key, c);
    since = j.rev;
    if (!j.more) break;
  }
  const ps = [...parcels.values()];
  const live = ps.filter((p) => !p.isDeleted && !p.isArchived);
  const sheet = ps.filter((p) => p.id.startsWith("s_")), old = ps.filter((p) => !p.id.startsWith("s_"));
  console.log(`Kiện: ${ps.length} (đang dùng ${live.length}, lưu trữ ${ps.filter((p) => p.isArchived).length}, đã xóa ${ps.filter((p) => p.isDeleted).length})`);
  console.log(`Do Sheet tạo: ${sheet.length} | thiếu mã đơn: ${ps.filter((p) => !N(p.orderId)).length} (trong đó do Sheet tạo: ${sheet.filter((p) => !N(p.orderId)).length})`);

  const groups = new Map();
  for (const p of live) if (N(p.orderId)) { const k = N(p.orderId) + "|" + N(p.returnTrack || p.track); groups.set(k, (groups.get(k) || []).concat(p)); }
  const dups = [...groups.values()].filter((a) => a.length > 1);
  console.log(`Nhóm trùng (cùng mã đơn + mã vận đơn, đang dùng): ${dups.length} | trong đó có kiện Sheet tạo: ${dups.filter((a) => a.some((p) => p.id.startsWith("s_"))).length}`);

  const codes = (p) => [p.track, p.altTrack, p.returnTrack, p.forwardTrack, p.returnOrderId, ...(p.trackingCodes || [])].map(N).filter((x) => x.length > 5);
  const oldCodes = new Set(old.flatMap(codes)), oldOrders = new Set(old.map((p) => N(p.orderId)).filter(Boolean));
  console.log(`Kiện Sheet tạo mà cùng mã đơn với kiện cũ: ${sheet.filter((p) => oldOrders.has(N(p.orderId))).length} | chung mã vận đơn/mã yêu cầu với kiện cũ: ${sheet.filter((p) => codes(p).some((c) => oldCodes.has(c))).length}`);

  const iss = await (await fetch(`${base}/v1/import/issues`, { headers: { "X-Import-Key": env.IMPORT_KEY } })).json().catch(() => null);
  console.log(`Ca nhập nhằng: ${iss && Array.isArray(iss.issues) ? iss.issues.length : "?"}`);
  const now = Date.now(), w = [...watch.values()];
  console.log(`Bảng Hàng Hoàn Về: ${w.length} dấu (đã bỏ ${w.filter((x) => x.off).length}, tạo trong 1 giờ qua ${w.filter((x) => now - x.since < 3600e3).length})`);
  const q = await (await fetch(`${base}/v1/quota`, { headers: H })).json();
  console.log(`Hạn mức ngày ${q.day}: ghi ${q.rowsWritten.used}/${q.rowsWritten.limit}, yêu cầu ${q.requests.used}/${q.requests.limit} (${q.level})`);

  const file = process.argv[2];
  if (!file) return;
  const local = new Map(fs.readFileSync(file, "utf8").split("\n").filter(Boolean).map((l) => { const p = JSON.parse(l); return [p.id, p]; }));
  const skip = new Set(["rev", "updatedAt", "hash", "importedAt", "receivedBy", "receivedByName"]);
  const norm = (v) => JSON.stringify(v === undefined || v === null || v === "" ? null : v);
  const fields = {}, ex = {};
  let same = 0, diff = 0, missing = 0;
  for (const [id, l] of local) {
    const c = parcels.get(id);
    if (!c) { missing++; continue; }
    let any = false;
    for (const k of new Set([...Object.keys(l), ...Object.keys(c)])) {
      if (skip.has(k)) continue;
      const a = k === "extra" ? JSON.stringify(l[k] || {}) : norm(l[k]), b = k === "extra" ? JSON.stringify(c[k] || {}) : norm(c[k]);
      if (a === b) continue;
      any = true;
      fields[k] = (fields[k] || 0) + 1;
      if (!ex[k]) ex[k] = `máy tính ${mask(a).slice(0, 90)} | cloud ${mask(b).slice(0, 90)}`;
    }
    any ? diff++ : same++;
  }
  console.log(`So với máy tính (${local.size} đơn): giống ${same}, khác ${diff}, thiếu trên cloud ${missing}`);
  for (const k of Object.keys(fields)) console.log(`  ${k}: ${fields[k]} đơn — ví dụ ${ex[k]}`);
})();
