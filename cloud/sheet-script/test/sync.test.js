// Chạy: node cloud/sheet-script/test/sync.test.js — chạy tệp dist/QLHH.gs (đúng tệp dán vào Apps Script) với Apps Script GIẢ.
// Kiểm: bỏ cột cá nhân, chỉ gửi kiện đổi, 429 thì dừng và lần sau gửi tiếp, không lộ khóa trong nhật ký, lô <= 100.
"use strict";
const fs = require("fs");
const path = require("path");
const vm = require("vm");
const crypto = require("crypto");
const assert = require("assert");

require("child_process").execFileSync(process.execPath, [path.join(__dirname, "..", "build.js")], { stdio: "ignore" });
const code = fs.readFileSync(path.join(__dirname, "..", "dist", "QLHH.gs"), "utf8");
const KEY = "khoa-bi-mat-123";

function fakeSheet(name, values) {
  const s = {
    name, values, hidden: false,
    getName: () => s.name,
    getDataRange: () => ({ getDisplayValues: () => s.values.map((r) => r.map(String)) }),
    getLastRow: () => s.values.length,
    getRange: (r, c, nr, nc) => ({
      getValues: () => s.values.slice(r - 1, r - 1 + nr).map((row) => row.slice(c - 1, c - 1 + nc)),
      setValues: (v) => { for (let i = 0; i < v.length; i++) s.values[r - 1 + i] = v[i].slice(); },
    }),
    clearContents: () => { s.values = []; },
    hideSheet: () => { s.hidden = true; },
  };
  return s;
}

function env(tabValues) {
  const sheets = Object.entries(tabValues).map(([n, v]) => fakeSheet(n, v));
  const logs = [], posts = [];
  let respond = () => ({ code: 200, body: { added: 0, updated: 0, ambiguous: [], skippedDeleted: 0 } });
  const ctx = {
    console: { log: (m) => logs.push(String(m)) },
    PropertiesService: { getScriptProperties: () => ({ getProperty: (k) => ({ QLHH_CLOUD_URL: "https://x.example/v1/", QLHH_IMPORT_KEY: KEY })[k] || null }) },
    SpreadsheetApp: { getActiveSpreadsheet: () => ({
      getSheets: () => sheets, getSheetByName: (n) => sheets.find((s) => s.name === n) || null,
      insertSheet: (n) => { const s = fakeSheet(n, []); sheets.push(s); return s; },
    }) },
    LockService: { getScriptLock: () => ({ tryLock: () => true, releaseLock: () => {} }) },
    UrlFetchApp: { fetch: (url, o) => {
      const body = JSON.parse(o.payload);
      posts.push({ url, headers: o.headers, body });
      const r = respond(body);
      return { getResponseCode: () => r.code, getContentText: () => JSON.stringify(r.body) };
    } },
    Utilities: {
      DigestAlgorithm: { SHA_256: "sha256" }, Charset: { UTF_8: "utf8" },
      computeDigest: (_a, s) => [...crypto.createHash("sha256").update(s, "utf8").digest()],
      base64Encode: (b) => Buffer.from(b).toString("base64"),
      formatDate: (_d, _tz, f) => ({ "yyyy-MM-dd HH:mm:ss": "2026-11-23 12:00:00", H: "12", m: "0" })[f],
    },
  };
  vm.createContext(ctx);
  vm.runInContext(code, ctx);
  return { ctx, sheets, logs, posts, setRespond: (f) => { respond = f; } };
}

const header = ["Mã", "Mã đơn", "Loại", "Mã vận đơn", "Trạng thái về", "Về shop lúc", "Hàng", "SL", "Tiền", "Lý do", "Ngày", "Cập nhật", "Tên người nhận", "Số điện thoại", "Địa chỉ nhận hàng"];
const row = (n) => ["RR" + n, "SP" + n, "Trả hàng + hoàn tiền", "SPXVN" + (100000000 + n), "Đang về", "21/11/2026 10:00", "Áo thun · Đen, M × 1", "1", "100000", "Sai mô tả",
  "20/11/2026", "22/11/2026", "Nguyễn Văn Giả " + n, "0900000" + String(n).padStart(3, "0"), n + " Đường Giả"];

let pass = 0;
function t(name, fn) { fn(); pass++; console.log("  ok  " + name); }

t("gửi mọi kiện lần đầu, đúng địa chỉ + khóa ở header, không có dữ liệu cá nhân", () => {
  const e = env({ "Hàng hoàn": [header, ...Array.from({ length: 120 }, (_, i) => row(i))] });
  e.ctx.qlhhSyncToCloud(false);
  assert.strictEqual(e.posts.length, 3);
  assert.ok(e.posts.every((p) => p.url === "https://x.example/v1/import/sheet" && p.headers["X-Import-Key"] === KEY && p.body.parcels.length <= 100));
  assert.strictEqual(e.posts.reduce((s, p) => s + p.body.parcels.length, 0), 120);
  const all = JSON.stringify(e.posts.map((p) => p.body));
  assert.ok(!/Nguyễn Văn Giả|0900000|Đường Giả/.test(all), "lọt dữ liệu cá nhân");
  assert.ok(e.posts[0].body.parcels.every((p) => p.buyerNote === ""));
  assert.ok(!e.logs.join("\n").includes(KEY), "khóa lộ trong nhật ký");
  const st = e.sheets.find((s) => s.name === "_qlhh_state");
  assert.ok(st && st.hidden && st.values.length === 121);
});

t("lần sau chỉ gửi kiện đổi; đối soát đêm gửi lại tất cả", () => {
  const e = env({ "Hàng hoàn": [header, row(1), row(2), row(3)] });
  e.ctx.qlhhSyncToCloud(false);
  e.posts.length = 0;
  e.ctx.qlhhSyncToCloud(false);
  assert.strictEqual(e.posts.length, 0, "không đổi thì không gửi");
  e.sheets[0].values[2][4] = "Đã về shop";
  e.ctx.qlhhSyncToCloud(false);
  assert.strictEqual(e.posts.length, 1);
  assert.deepStrictEqual(e.posts[0].body.parcels.map((p) => p.orderId), ["SP2"]);
  e.posts.length = 0;
  e.ctx.qlhhNightlyFull();
  assert.strictEqual(e.posts[0].body.parcels.length, 3);
});

t("đổi cột cá nhân không làm gửi lại", () => {
  const e = env({ "Hàng hoàn": [header, row(1)] });
  e.ctx.qlhhSyncToCloud(false);
  e.posts.length = 0;
  e.sheets[0].values[1][13] = "0911111111";
  e.ctx.qlhhSyncToCloud(false);
  assert.strictEqual(e.posts.length, 0);
});

t("429 giữa chừng: dừng, không đánh dấu phần chưa gửi, lần sau gửi tiếp", () => {
  const e = env({ "Hàng hoàn": [header, ...Array.from({ length: 120 }, (_, i) => row(i))] });
  let n = 0;
  e.setRespond(() => (++n === 2 ? { code: 429, body: { error: { code: "quota_exceeded", message: "Hết hạn mức" } } } : { code: 200, body: { added: 1, updated: 0, ambiguous: [] } }));
  const note = e.ctx.qlhhSyncToCloud(false);
  assert.strictEqual(e.posts.length, 2);
  assert.ok(/gửi 50\/120/.test(note), note);
  assert.ok(e.logs.some((l) => l.includes("429")) && !e.logs.join("\n").includes(KEY));
  e.posts.length = 0;
  e.ctx.qlhhSyncToCloud(false);
  assert.strictEqual(e.posts.reduce((s, p) => s + p.body.parcels.length, 0), 70);
});

t("tài khoản CHỈ XEM (dự án riêng, QLHH_SHEET_ID là link): không ghi gì vào Sheet gốc, trạng thái ở bảng tính riêng", () => {
  const e = env({});
  const source = [fakeSheet("Hàng hoàn", [header, row(1), row(2)])];
  source.forEach((s) => { s.getRange = () => { throw new Error("Sheet gốc chỉ có quyền xem"); }; s.clearContents = s.getRange; });
  const props = { QLHH_CLOUD_URL: "https://x.example", QLHH_IMPORT_KEY: KEY,
    QLHH_SHEET_ID: "https://docs.google.com/spreadsheets/d/1AbCdEfGhIjKlMnOpQrStUvWxYz0123456789/edit#gid=0" };
  const books = {}; let created = 0, openedSource = 0;
  const book = (id, sheets) => ({ getId: () => id, getSheets: () => sheets, getSheetByName: (n) => sheets.find((s) => s.name === n) || null,
    insertSheet: (n) => { const s = fakeSheet(n, []); sheets.push(s); return s; } });
  e.ctx.PropertiesService.getScriptProperties = () => ({ getProperty: (k) => props[k] || null, setProperty: (k, v) => { props[k] = v; } });
  e.ctx.SpreadsheetApp.openById = (id) => {
    if (id === "1AbCdEfGhIjKlMnOpQrStUvWxYz0123456789") { openedSource++; return Object.assign(book(id, source), { insertSheet: () => { throw new Error("không được tạo tab trong Sheet gốc"); } }); }
    if (books[id]) return books[id];
    throw new Error("không có");
  };
  e.ctx.SpreadsheetApp.create = () => { created++; const b = book("state-" + created, [fakeSheet("Trang tính1", [])]); books[b.getId()] = b; return b; };
  e.ctx.qlhhSyncToCloud(false);
  assert.strictEqual(e.posts[0].body.parcels.length, 2);
  assert.strictEqual(created, 1);
  assert.strictEqual(props.QLHH_STATE_BOOK_ID, "state-1");
  e.posts.length = 0;
  e.ctx.qlhhSyncToCloud(false);
  assert.strictEqual(e.posts.length, 0, "lần 2 không đổi thì không gửi");
  assert.strictEqual(created, 1, "không tạo thêm bảng trạng thái");
  assert.strictEqual(openedSource, 2);
});

t("QLHH_TABS kiểu máy tính (chấm phẩy, Huỷ/Hủy): chỉ đọc đúng tab đã chọn", () => {
  const e = env({ "Hàng hoàn": [header, row(1)], "Hủy · Chi tiết": [header, row(2)], "Ghi chú riêng": [header, row(3)] });
  const props = { QLHH_CLOUD_URL: "https://x.example", QLHH_IMPORT_KEY: KEY, QLHH_TABS: "Hàng hoàn; Trả hàng; Huỷ · chi tiết" };
  e.ctx.PropertiesService.getScriptProperties = () => ({ getProperty: (k) => props[k] || null });
  e.ctx.qlhhSyncToCloud(false);
  assert.deepStrictEqual(e.posts[0].body.parcels.map((p) => p.orderId).sort(), ["SP1", "SP2"]);
});

t("đọc tab như máy tính: dòng tiêu đề không ở dòng 1, cột trùng tên không ghi đè, cột thừa bên phải bỏ", () => {
  const hdr = ["Mã", "Mã đơn", "Loại", "Mã vận đơn", "Trạng thái về", "Hàng", "SL", "Mã đơn", "", ""];
  const e = env({ "Hàng hoàn": [["BÁO CÁO HÀNG HOÀN", "", "", "", "", "", "", "", "", ""], ["", "", "", "", "", "", "", "", "", ""], hdr,
    ["RR1", "SP1", "Trả hàng + hoàn tiền", "SPXVN000000101", "Đang về", "Áo thun · Đen, M × 1", "1", "", "", ""],
    ["RR2", "SP2", "Trả hàng + hoàn tiền", "SPXVN000000102", "Đang về", "Quần · Be, L × 1", "1", "", "", ""]] });
  const tab = e.ctx.qlhhReadTab_(e.sheets[0]);
  assert.deepStrictEqual(Object.keys(tab[0]), ["Mã", "Mã đơn", "Loại", "Mã vận đơn", "Trạng thái về", "Hàng", "SL", "Mã đơn_1"]);
  assert.strictEqual(tab[0]["Mã đơn"], "SP1");
  e.ctx.qlhhSyncToCloud(false);
  assert.deepStrictEqual(e.posts[0].body.parcels.map((p) => p.orderId), ["SP1", "SP2"]);
});

t("tab không phải bảng đơn (không có cột mã + cột hàng/SL/trạng thái): bỏ qua, không lỗi", () => {
  const e = env({ "Ghi chú": [["Ngày", "Việc"], ["20/11", "gọi khách"]], "Hàng hoàn": [header, row(1)] });
  e.ctx.qlhhSyncToCloud(false);
  assert.strictEqual(e.posts[0].body.parcels.length, 1);
});

t("thiếu khóa: báo lỗi rõ, không gọi mạng", () => {
  const e = env({ "Hàng hoàn": [header, row(1)] });
  e.ctx.PropertiesService.getScriptProperties = () => ({ getProperty: () => null });
  assert.throws(() => e.ctx.qlhhSyncToCloud(false), /QLHH_IMPORT_KEY/);
  assert.strictEqual(e.posts.length, 0);
});

console.log(`Đồng bộ Apps Script (giả lập): ${pass}/9 đạt`);
