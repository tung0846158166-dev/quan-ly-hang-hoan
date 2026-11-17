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
  for (const n of ['removeVietnameseTones', 'parseSheetItemsCell', 'normalizeSheetRows', 'sheetSyncTabs', 'sheetIdFromLink', 'loadSheetSyncCfg', 'saveSheetSyncCfg', 'sheetSyncHash', 'buildSheetSyncUrl', 'fetchSheetSyncText', 'runSheetSync', 'renderSheetSyncStatus', 'sheetSyncScriptText']) vm.runInContext(extract(n), c);
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
  assert.ok(code.includes("Sheets.Spreadsheets.Values.get(SHEET_ID"), 'đọc qua Google Sheets API — chỉ cần quyền XEM');
  assert.ok(!code.includes('SpreadsheetApp'), 'SpreadsheetApp.openById đòi quyền sửa, không được dùng');
  // link sheet -> mã sheet đưa vào đoạn mã
  const id = vm.runInContext("sheetIdFromLink('https://docs.google.com/spreadsheets/d/1AbCdEfGhIjKlMnOpQrStUvWxYz0123456789/edit#gid=0')", c);
  assert.strictEqual(id, '1AbCdEfGhIjKlMnOpQrStUvWxYz0123456789');
  const cfg = vm.runInContext('loadSheetSyncCfg()', c); cfg.sheetId = id; cfg.sheetTab = "Đơn hoàn"; vm.runInContext('saveSheetSyncCfg', c)(cfg);
  const code2 = vm.runInContext('sheetSyncScriptText()', c);
  assert.ok(code2.includes("const SHEET_ID = '" + id + "'") && code2.includes("const SHEET_NAMES = ['Đơn hoàn']"));
  assert.doesNotThrow(() => new vm.Script(code2));
  // chạy thử doGet với ContentService / Sheets API giả
  const cfg3 = vm.runInContext('loadSheetSyncCfg()', c); cfg3.sheetTab = ''; vm.runInContext('saveSheetSyncCfg', c)(cfg3);
  const code3 = vm.runInContext('sheetSyncScriptText()', c);
  const out = {}; const calls = [];
  const g = { ContentService: { MimeType: { JSON: 'json', CSV: 'csv' }, createTextOutput: t => ({ setMimeType: m => { out.t = t; out.m = m; return out; } }) },
    Sheets: { Spreadsheets: { get: (sid, o) => { calls.push(['meta', sid]); return { sheets: [{ properties: { title: 'Tab đầu' } }] }; },
      Values: { get: (sid, range) => { calls.push(['values', sid, range]); return { values: [['Mã đơn hàng', 'Tên sản phẩm'], ['A1', 'Áo, "len"']] }; } } } } };
  vm.createContext(g); vm.runInContext(code3, g);
  g.doGet({ parameter: { token: 'sai' } }); assert.strictEqual(out.m, 'json');
  // không giới hạn tab (SHEET_NAMES = []) -> đọc tab đầu tiên
  g.doGet({ parameter: { token: tok } }); assert.strictEqual(out.m, 'csv');
  assert.strictEqual(out.t, 'Mã đơn hàng,Tên sản phẩm\r\nA1,"Áo, ""len"""');
  assert.strictEqual(JSON.stringify(calls), JSON.stringify([['meta', id], ['values', id, "'Tab đầu'"]]));
  // lỗi Google (vd không có quyền) -> trả JSON lỗi, không làm sập
  g.Sheets.Spreadsheets.Values.get = () => { throw new Error('The caller does not have permission'); };
  g.doGet({ parameter: { token: tok } }); assert.strictEqual(out.m, 'json'); assert.ok(/does not have permission/.test(out.t));
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
  const allowed = new Set(['createTextOutput', 'stringify', 'setMimeType', 'get', 'map', 'test', 'replace', 'join', 'indexOf', 'out', 'err', 'String']);
  assert.deepStrictEqual(calls.filter(x => !allowed.has(x)), [], 'chỉ dùng lệnh đọc');
  assert.ok(!/\.(set(Value|Values|Formula|Formulas|Note)|clear|delete|insert|append|remove|copyTo|moveTo|sort)\w*\(/.test(code));
  vm.runInContext(extract('sheetSyncManifestText'), c);
  const man = JSON.parse(vm.runInContext('sheetSyncManifestText()', c));
  assert.deepStrictEqual(man.oauthScopes, ['https://www.googleapis.com/auth/spreadsheets.readonly']);
  assert.strictEqual(man.dependencies.enabledAdvancedServices[0].serviceId, 'sheets', 'bật Google Sheets API ngay trong tệp kê khai');
});

test('Google Sheet: app chỉ GỬI yêu cầu đọc (GET), không có đường ghi ngược lên Google', () => {
  const main = fs.readFileSync(path.join(__dirname, '..', 'main.js'), 'utf8');
  const fn = main.slice(main.indexOf('function fetchSheetText'), main.indexOf("ipcMain.handle('fetch-sheet-text'"));
  assert.ok(fn.includes("require('https').get("));
  assert.ok(!/method:\s*'(POST|PUT|DELETE|PATCH)'|\.request\(/.test(fn));
});

test('Google Sheet: chỉ đọc các tab đã chọn; tab không có cột đơn hàng thì bỏ qua, các tab còn lại vẫn nạp', async () => {
  const fetched = [];
  const c = ctx(async url => { const tab = decodeURIComponent((url.match(/[&?]tab=([^&]*)/) || [])[1] || ''); fetched.push(tab);
    return { ok: true, text: tab === 'Hiệu quả mẫu' ? 'Tên SP\n' : 'Mã đơn hàng\n' + tab + '-1\n' + tab + '-2' }; });
  c.readWorkbookRowsFromBuffer = buf => { const t = new TextDecoder().decode(buf); return t.startsWith('Mã đơn hàng') ? t.split('\n').slice(1).filter(Boolean).map(l => ({ 'Mã đơn hàng': l })) : []; };
  const cfg = vm.runInContext('loadSheetSyncCfg()', c);
  cfg.url = 'https://script.google.com/macros/s/abc/exec'; cfg.sheetId = 'SID'; cfg.sheetTab = 'Hàng hoàn; Trả hàng;Hiệu quả mẫu';
  vm.runInContext('saveSheetSyncCfg', c)(cfg);
  assert.strictEqual((await vm.runInContext('runSheetSync(true)', c)).success, true);
  assert.deepStrictEqual(fetched.join('|'), 'Hàng hoàn|Trả hàng|Hiệu quả mẫu');
  assert.strictEqual(c.imports[0].files.length, 2);
  assert.strictEqual(c.imports[0].files.map(f => f.name).join('|'), 'Google Sheet · Hàng hoàn|Google Sheet · Trả hàng');
  assert.ok(/bỏ qua tab không có cột đơn hàng: Hiệu quả mẫu/.test(vm.runInContext('loadSheetSyncCfg().lastNote', c)));
  // đoạn mã chặn tab ngoài danh sách
  const code = vm.runInContext('sheetSyncScriptText()', c);
  assert.ok(code.includes("const SHEET_NAMES = ['Hàng hoàn', 'Trả hàng', 'Hiệu quả mẫu']"));
  const out = {}; const g = { ContentService: { MimeType: { JSON: 'json', CSV: 'csv' }, createTextOutput: t => ({ setMimeType: m => { out.t = t; out.m = m; return out; } }) },
    Sheets: { Spreadsheets: { get: () => ({ sheets: [] }), Values: { get: (sid, range) => ({ values: [['Mã đơn hàng'], [range.replace(/^'|'$/g, '')]] }) } } } };
  vm.createContext(g); vm.runInContext(code, g);
  g.doGet({ parameter: { token: cfg.token, tab: 'Tiền về' } }); assert.strictEqual(out.m, 'json'); assert.ok(/không nằm trong danh sách/.test(out.t));
  g.doGet({ parameter: { token: cfg.token, tab: 'Trả hàng' } }); assert.strictEqual(out.m, 'csv'); assert.strictEqual(out.t, 'Mã đơn hàng\r\nTrả hàng');
});

test('Sheet "Hàng hoàn": đổi tên cột sang kiểu file Shopee, tách nhiều sản phẩm, Loại Bom -> Giao không thành công', () => {
  const c = ctx(async () => ({ ok: true, text: '' }));
  const rows = [
    { 'Mã': '2611160DFQNR4Y9', 'Mã đơn': '2611110W9GXPE8', 'Loại': 'Trả hàng', 'Mã vận đơn': 'SPXVN06576167517B', 'Trạng thái về': 'Đang về', 'Về shop lúc': '', 'Hạn khiếu nại': '20/11/2026', 'Hàng': 'KUNTA · Xám Tiêu Đậm, XL × 3; PRETTY IN CAO SU · Đen, L × 1', 'SL': '3', 'Tiền': '670.020 ₫', 'Lý do': 'Đổi ý', 'Ngày': '16/11/2026 00:40', 'Cập nhật': '16/11/2026 03:31' },
    { 'Mã': 'X2', 'Mã đơn': 'BOM1', 'Loại': 'Bom', 'Mã vận đơn': 'SPXVN0BOM1', 'Trạng thái về': 'Đang về', 'Hàng': 'ALTIC · Be, M', 'SL': '2', 'Tiền': '1 ₫', 'Lý do': '', 'Ngày': '15/11/2026', 'Cập nhật': '' },
  ];
  const out = JSON.parse(JSON.stringify(c.normalizeSheetRows(rows)));
  assert.strictEqual(out.length, 3, '2 sản phẩm của gói đầu thành 2 dòng');
  assert.strictEqual(out[0]['Mã đơn hàng'], '2611110W9GXPE8');
  assert.strictEqual(out[0]['Mã vận đơn'], 'SPXVN06576167517B');
  assert.strictEqual(out[0]['Mã số khiếu nại'], '2611160DFQNR4Y9');
  assert.strictEqual(out[0]['Trạng thái Trả hàng/Hoàn tiền'], 'Đang về');
  assert.strictEqual(out[0]['Lý do trả hàng/hoàn tiền'], 'Đổi ý');
  assert.strictEqual(out[0]['Thời gian yêu cầu'], '16/11/2026 00:40');
  assert.strictEqual(out[0]['Người bán cần phản hồi trước'], '20/11/2026');
  assert.strictEqual(out[0]['Tên sản phẩm'] + '|' + out[0]['Tên phân loại hàng'] + '|' + out[0]['Số lượng'], 'KUNTA|Xám Tiêu Đậm, XL|3');
  assert.strictEqual(out[1]['Tên sản phẩm'] + '|' + out[1]['Số lượng'], 'PRETTY IN CAO SU|1');
  assert.strictEqual(out[0]['Tổng số tiền hoàn trả'], '670.020 ₫');
  assert.strictEqual(out[1]['Tổng số tiền hoàn trả'], '', 'tiền chỉ tính 1 lần cho cả gói');
  assert.ok(!('Cập nhật' in out[0]) && !('Hàng' in out[0]) && !('SL' in out[0]));
  assert.strictEqual(out[2]['Lý do hủy'], 'Giao hàng thất bại', 'Bom -> Giao không thành công');
  assert.strictEqual(out[2]['Số lượng'], '2', 'không có × thì lấy cột SL');
  // file Excel Shopee thường không bị đổi
  const shopee = [{ 'Mã đơn hàng': 'A', 'Tên sản phẩm': 'X', 'Số lượng': '1' }];
  assert.strictEqual(c.normalizeSheetRows(shopee), shopee);
});

test('Tự cập nhật khi bảng đang khóa không bật hộp nhập mật khẩu', () => {
  const html2 = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
  const fn = html2.slice(html2.indexOf('async function requireUnlocked'), html2.indexOf('appPrompt', html2.indexOf('async function requireUnlocked')));
  assert.ok(fn.includes('sheetSyncRunning'));
});
