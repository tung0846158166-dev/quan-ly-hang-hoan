const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs'), path = require('path'), vm = require('vm');
const html = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
function extract(name) {
  const m = html.match(new RegExp('^    (?:async )?function ' + name + '\\([^]*?^    }', 'm'));
  assert.ok(m, name); return m[0];
}
function ctx(fetchText) {
  const store = {};
  const c = {
    localStorage: { getItem: k => store[k] ?? null, setItem: (k, v) => { store[k] = String(v); } },
    crypto: { getRandomValues: a => { for (let i = 0; i < a.length; i++) a[i] = i; return a; } },
    TextEncoder, Date, JSON, Math, String, Array, Uint8Array, console,
    window: { desktopDiskAPI: { fetchSheetText: async url => { c.urls.push(url); return fetchText(url); } } },
    document: { getElementById: () => null },
    activeImportBatch: null, urls: [], imports: [], alerts: [],
    alert: m => c.alerts.push(m),
    readWorkbookRowsFromBuffer: (buf, isCsv) => { c.csvFlag = isCsv; const t = new TextDecoder().decode(buf); return t.split('\n').slice(1).filter(Boolean).map(l => ({ 'Mã đơn hàng': l })); },
    importParsedPlatformFiles: async (files, plat) => { c.imports.push({ files, plat }); return { success: true, messages: ['ok'], message: 'ok' }; },
  };
  vm.createContext(c);
  vm.runInContext("const SHEET_SYNC_KEY = 'qlhh_sheet_sync_v1';", c);
  for (const n of ['sheetIdFromLink', 'loadSheetSyncCfg', 'saveSheetSyncCfg', 'sheetSyncHash', 'buildSheetSyncUrl', 'fetchSheetSyncText', 'runSheetSync', 'renderSheetSyncStatus', 'sheetSyncScriptText']) vm.runInContext(extract(n), c);
  vm.runInContext('let sheetSyncRunning = false;', c);
  return c;
}

test('Google Sheet: nạp như file Shopee, gửi kèm mã bí mật, sheet không đổi thì bỏ qua', async () => {
  const c = ctx(async () => ({ ok: true, text: 'Mã đơn hàng\nA1\nA2' }));
  const cfg = vm.runInContext('loadSheetSyncCfg()', c);
  cfg.url = 'https://script.google.com/macros/s/abc/exec'; cfg.enabled = true;
  vm.runInContext('saveSheetSyncCfg', c)(cfg);
  assert.strictEqual((await vm.runInContext('runSheetSync(false)', c)).success, true);
  assert.strictEqual(c.imports.length, 1);
  assert.strictEqual(c.imports[0].plat, 'Shopee');
  assert.strictEqual(c.imports[0].files[0].rows.length, 2);
  assert.strictEqual(c.csvFlag, true);
  assert.ok(c.urls[0].includes('token=' + cfg.token));
  const r2 = await vm.runInContext('runSheetSync(false)', c);
  assert.strictEqual(r2.unchanged, true, 'sheet không đổi thì không nạp lại');
  assert.strictEqual(c.imports.length, 1);
});

test('Google Sheet: báo lỗi rõ khi sai mã bí mật hoặc Google trả trang web; không nạp gì', async () => {
  for (const text of ['{"error":"Sai mã bí mật"}', '<!DOCTYPE html><html>login</html>']) {
    const c = ctx(async () => ({ ok: true, text }));
    const cfg = vm.runInContext('loadSheetSyncCfg()', c); cfg.url = 'https://script.google.com/macros/s/abc/exec';
    vm.runInContext('saveSheetSyncCfg', c)(cfg);
    assert.strictEqual((await vm.runInContext('runSheetSync(true)', c)).success, false);
    assert.strictEqual(c.imports.length, 0);
    assert.ok(/Chưa cập nhật được/.test(c.alerts[0]));
  }
});

test('Google Sheet: đoạn mã Apps Script hợp lệ và kiểm tra mã bí mật', () => {
  const c = ctx(async () => ({ ok: true, text: '' }));
  const code = vm.runInContext('sheetSyncScriptText()', c);
  assert.doesNotThrow(() => new vm.Script(code));
  const tok = vm.runInContext('loadSheetSyncCfg().token', c);
  assert.ok(code.includes("const TOKEN = '" + tok + "'"));
  assert.ok(code.includes("SpreadsheetApp.openById(SHEET_ID)"), 'dự án riêng mở sheet theo mã, không cần cài vào sheet');
  // link sheet -> mã sheet đưa vào đoạn mã
  const id = vm.runInContext("sheetIdFromLink('https://docs.google.com/spreadsheets/d/1AbCdEfGhIjKlMnOpQrStUvWxYz0123456789/edit#gid=0')", c);
  assert.strictEqual(id, '1AbCdEfGhIjKlMnOpQrStUvWxYz0123456789');
  const cfg = vm.runInContext('loadSheetSyncCfg()', c); cfg.sheetId = id; cfg.sheetTab = "Đơn hoàn"; vm.runInContext('saveSheetSyncCfg', c)(cfg);
  const code2 = vm.runInContext('sheetSyncScriptText()', c);
  assert.ok(code2.includes("const SHEET_ID = '" + id + "'") && code2.includes("const SHEET_NAME = 'Đơn hoàn'"));
  assert.doesNotThrow(() => new vm.Script(code2));
  // chạy thử doGet với ContentService/SpreadsheetApp giả
  const out = {};
  const g = { ContentService: { MimeType: { JSON: 'json', CSV: 'csv' }, createTextOutput: t => ({ setMimeType: m => { out.t = t; out.m = m; return out; } }) },
    SpreadsheetApp: { getActiveSpreadsheet: () => ({ getSheets: () => [{ getDataRange: () => ({ getDisplayValues: () => [['Mã đơn hàng', 'Tên sản phẩm'], ['A1', 'Áo, "len"']] }) }] }) } };
  g.SpreadsheetApp.openById = () => g.SpreadsheetApp.getActiveSpreadsheet();
  vm.createContext(g); vm.runInContext(code, g);
  g.doGet({ parameter: { token: 'sai' } }); assert.strictEqual(out.m, 'json');
  g.doGet({ parameter: { token: tok } }); assert.strictEqual(out.m, 'csv');
  assert.strictEqual(out.t, 'Mã đơn hàng,Tên sản phẩm\r\nA1,"Áo, ""len"""');
});

test('Google Sheet: máy tính chỉ tải từ địa chỉ Google', () => {
  const main = fs.readFileSync(path.join(__dirname, '..', 'main.js'), 'utf8');
  assert.ok(main.includes("ipcMain.handle('fetch-sheet-text'") && main.includes('SHEET_ALLOWED_HOSTS'));
  const pre = fs.readFileSync(path.join(__dirname, '..', 'preload.js'), 'utf8');
  assert.ok(pre.includes('fetchSheetText'));
});

test('Google Sheet: đoạn mã chỉ đọc (không có lệnh ghi/xóa sheet) và tệp kê khai khóa quyền chỉ đọc', () => {
  const c = ctx(async () => ({ ok: true, text: '' }));
  const code = vm.runInContext('sheetSyncScriptText()', c);
  const calls = [...code.matchAll(/\.([a-zA-Z]+)\(/g)].map(x => x[1]);
  const allowed = new Set(['createTextOutput', 'stringify', 'setMimeType', 'openById', 'getActiveSpreadsheet', 'getSheetByName', 'getSheets', 'getDataRange', 'getDisplayValues', 'map', 'test', 'replace', 'join']);
  assert.deepStrictEqual(calls.filter(x => !allowed.has(x)), [], 'chỉ dùng lệnh đọc');
  assert.ok(!/\.(set(Value|Values|Formula|Formulas|Note)|clear|delete|insert|append|remove|copyTo|moveTo|sort)\w*\(/.test(code));
  vm.runInContext(extract('sheetSyncManifestText'), c);
  const man = JSON.parse(vm.runInContext('sheetSyncManifestText()', c));
  assert.deepStrictEqual(man.oauthScopes, ['https://www.googleapis.com/auth/spreadsheets.readonly']);
});

test('Google Sheet: app chỉ GỬI yêu cầu đọc (GET), không có đường ghi ngược lên Google', () => {
  const main = fs.readFileSync(path.join(__dirname, '..', 'main.js'), 'utf8');
  const fn = main.slice(main.indexOf('function fetchSheetText'), main.indexOf("ipcMain.handle('fetch-sheet-text'"));
  assert.ok(fn.includes("require('https').get("));
  assert.ok(!/method:\s*'(POST|PUT|DELETE|PATCH)'|\.request\(/.test(fn));
});
