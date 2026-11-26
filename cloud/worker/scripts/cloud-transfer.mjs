#!/usr/bin/env node
/**
 * Chuyển cloud sang tài khoản Cloudflare khác — NGUYÊN TRẠNG (27/11/2026).
 *
 *   node scripts/cloud-transfer.mjs backup  --url <cloud cũ>  --token-env FROM_TOKEN --out <thư mục>
 *   node scripts/cloud-transfer.mjs restore --url <cloud mới> --token-env TO_TOKEN   --dir <thư mục>
 *   node scripts/cloud-transfer.mjs counts  --url <cloud>     --token-env TOKEN
 *
 * backup : đọc mọi bảng + mọi ảnh (ảnh đầy đủ "p/<id>" và ảnh nhỏ "t/<id>") ra thư mục trên máy, kèm manifest.json (số lượng + băm ảnh).
 *          Thư mục này cũng là BẢN SAO LƯU cất giữ (đọc lại được bằng bất kỳ công cụ JSON nào).
 * restore: ghi y hệt vào cloud MỚI (phải còn trống): begin -> từng bảng -> từng ảnh -> end, rồi ĐỐI CHIẾU số lượng với manifest.
 * Token chủ cửa hàng đọc từ BIẾN MÔI TRƯỜNG (không in ra, không ghi vào tệp).
 */
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";

const TABLES = ["parcels", "parcel_codes", "return_watch", "audit_log", "scans", "photos", "import_state", "ai_digest", "import_log", "import_issues", "devices", "meta"];

function args() {
  const [cmd, ...rest] = process.argv.slice(2);
  const o = { cmd };
  for (let i = 0; i < rest.length; i += 2) o[rest[i].replace(/^--/, "")] = rest[i + 1];
  return o;
}

function client(url, tokenEnv) {
  const token = process.env[tokenEnv || ""];
  if (!token) throw new Error(`Thiếu token: đặt biến môi trường ${tokenEnv}`);
  const base = url.replace(/\/+$/, "").replace(/\/v1$/, "") + "/v1";
  async function req(method, p, body, raw) {
    for (let attempt = 1; ; attempt++) {
      try {
        const headers = { Authorization: `Bearer ${token}` };
        let payload;
        if (raw !== undefined) { payload = raw; headers["Content-Type"] = "image/jpeg"; }
        else if (body !== undefined) { payload = JSON.stringify(body); headers["Content-Type"] = "application/json"; }
        const r = await fetch(base + p, { method, headers, body: payload });
        if (r.status >= 500 || r.status === 429) throw new Error(`HTTP ${r.status}`);
        return r;
      } catch (e) {
        if (attempt >= 5) throw e;
        await new Promise((res) => setTimeout(res, 1500 * attempt));   // mạng chập chờn / cloud bận: thử lại
      }
    }
  }
  return {
    async json(method, p, body) {
      const r = await req(method, p, body);
      const t = await r.text();
      const j = t ? JSON.parse(t) : {};
      if (!r.ok) throw new Error(`${method} ${p}: ${r.status} ${j?.error?.message ?? t.slice(0, 200)}`);
      return j;
    },
    async getBlob(key) {
      const r = await req("GET", `/admin/transfer/blob?key=${encodeURIComponent(key)}`);
      if (r.status === 404) return null;
      if (!r.ok) throw new Error(`blob ${key}: ${r.status}`);
      return Buffer.from(await r.arrayBuffer());
    },
    async putBlob(key, bytes) {
      const r = await req("PUT", `/admin/transfer/blob?key=${encodeURIComponent(key)}`, undefined, bytes);
      if (!r.ok) throw new Error(`đẩy ảnh ${key}: ${r.status} ${await r.text()}`);
    },
  };
}

const sha = (b) => crypto.createHash("sha256").update(b).digest("hex");
const blobFile = (dir, key) => path.join(dir, "blobs", key.replace("/", "_"));

async function backup(o) {
  const c = client(o.url, o["token-env"]);
  const out = o.out; if (!out) throw new Error("Thiếu --out");
  fs.mkdirSync(path.join(out, "tables"), { recursive: true });
  fs.mkdirSync(path.join(out, "blobs"), { recursive: true });
  const counts = (await c.json("GET", "/admin/transfer/counts")).counts;
  const tableRows = {};
  for (const t of TABLES) {
    const rows = [];
    for (let offset = 0; ; ) {
      const r = await c.json("GET", `/admin/transfer/export?table=${t}&offset=${offset}&limit=500`);
      rows.push(...r.rows);
      if (!r.more) break;
      offset += r.rows.length;
    }
    fs.writeFileSync(path.join(out, "tables", `${t}.json`), JSON.stringify(rows));
    tableRows[t] = rows.length;
    console.log(`  bảng ${t}: ${rows.length}`);
  }
  // ảnh: mọi ảnh còn hiệu lực (ảnh đã hết hạn đã bị xóa khỏi kho ảnh)
  const photos = JSON.parse(fs.readFileSync(path.join(out, "tables", "photos.json"), "utf8")).filter((p) => Number(p.deleted_at) === 0);
  const blobs = {};
  let n = 0;
  for (const p of photos) {
    for (const k of [`p/${p.photo_id}`, `t/${p.photo_id}`]) {
      const f = blobFile(out, k);
      let b = fs.existsSync(f) ? fs.readFileSync(f) : await c.getBlob(k);   // chạy lại sau khi đứt giữa chừng: bỏ qua ảnh đã tải
      if (!b) continue;
      if (!fs.existsSync(f)) fs.writeFileSync(f, b);
      blobs[k] = sha(b);
    }
    if (++n % 100 === 0) console.log(`  ảnh: ${n}/${photos.length}`);
  }
  const manifest = { at: new Date().toISOString(), source: o.url.replace(/^https?:\/\//, "").split("/")[0], counts, tableRows, blobs };
  fs.writeFileSync(path.join(out, "manifest.json"), JSON.stringify(manifest, null, 1));
  console.log(`XONG sao lưu: ${Object.values(tableRows).reduce((a, b) => a + b, 0)} dòng, ${Object.keys(blobs).length} tệp ảnh -> ${out}`);
  return manifest;
}

async function restore(o) {
  const c = client(o.url, o["token-env"]);
  const dir = o.dir; if (!dir) throw new Error("Thiếu --dir");
  const manifest = JSON.parse(fs.readFileSync(path.join(dir, "manifest.json"), "utf8"));
  await c.json("POST", "/admin/transfer/begin", { confirm: "CHEP_DU_LIEU_CLOUD" });
  for (const t of TABLES) {
    const rows = JSON.parse(fs.readFileSync(path.join(dir, "tables", `${t}.json`), "utf8"));
    for (let i = 0; i < rows.length; i += 200) await c.json("POST", "/admin/transfer/import", { table: t, rows: rows.slice(i, i + 200) });
    console.log(`  bảng ${t}: ${rows.length}`);
  }
  let n = 0;
  for (const [k, hash] of Object.entries(manifest.blobs)) {
    const b = fs.readFileSync(blobFile(dir, k));
    if (sha(b) !== hash) throw new Error(`Tệp ảnh ${k} trên máy bị hỏng (băm lệch)`);
    await c.putBlob(k, b);
    if (++n % 200 === 0) console.log(`  ảnh: ${n}/${Object.keys(manifest.blobs).length}`);
  }
  const end = await c.json("POST", "/admin/transfer/end", {});
  // đối chiếu: mọi bảng (trừ devices: cloud mới có thêm máy chủ mới) phải BẰNG cloud cũ; kiểm thử lại vài ảnh
  const diffs = [];
  for (const [k, v] of Object.entries(manifest.counts)) {
    const got = end.counts[k];
    if (k === "devices" ? got < v : k === "meta" ? got < 1 : got !== v) diffs.push(`${k}: cũ ${v}, mới ${got}`);
  }
  for (const k of Object.keys(manifest.blobs).slice(0, 5)) {
    const b = await c.getBlob(k);
    if (!b || sha(b) !== manifest.blobs[k]) diffs.push(`ảnh ${k} đọc lại không khớp`);
  }
  if (diffs.length) { console.log("LỆCH:\n  " + diffs.join("\n  ")); process.exitCode = 2; }
  else console.log(`XONG khôi phục + đối chiếu KHỚP: ${end.counts.parcels} đơn (đã nhận ${end.counts.received}), ${end.counts.photosLive} ảnh, ${n} tệp ảnh.`);
  return { end, diffs };
}

async function counts(o) {
  const c = client(o.url, o["token-env"]);
  console.log(JSON.stringify((await c.json("GET", "/admin/transfer/counts")).counts, null, 1));
}

const o = args();
const run = { backup, restore, counts }[o.cmd];
if (!run) { console.log("Dùng: backup | restore | counts (xem đầu tệp)"); process.exit(1); }
run(o).catch((e) => { console.error("LỖI: " + e.message); process.exit(1); });
