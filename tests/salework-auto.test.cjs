const test = require('node:test'), assert = require('node:assert/strict');
const zlib = require('node:zlib'), fs = require('node:fs'), os = require('node:os'), path = require('node:path');
const { EventEmitter } = require('node:events');
const { readXlsxRows, dropPersonalColumns } = require('../salework_xlsx.js');
const { SaleworkAuto, pageScript } = require('../salework_auto.js');

function crc32(b) { let c, crc = 0xffffffff; for (let i = 0; i < b.length; i++) { c = (crc ^ b[i]) & 0xff; for (let k = 0; k < 8; k++) c = c & 1 ? (c >>> 1) ^ 0xedb88320 : c >>> 1; crc = (crc >>> 8) ^ c; } return (crc ^ 0xffffffff) >>> 0; }
function zip(files) {
  const locals = [], cents = []; let off = 0;
  for (const [name, text] of Object.entries(files)) {
    const nb = Buffer.from(name), raw = Buffer.from(text, 'utf8'), def = zlib.deflateRawSync(raw), crc = crc32(raw);
    const lh = Buffer.alloc(30); lh.writeUInt32LE(0x04034b50, 0); lh.writeUInt16LE(20, 4); lh.writeUInt16LE(8, 8); lh.writeUInt32LE(crc, 14); lh.writeUInt32LE(def.length, 18); lh.writeUInt32LE(raw.length, 22); lh.writeUInt16LE(nb.length, 26);
    const ch = Buffer.alloc(46); ch.writeUInt32LE(0x02014b50, 0); ch.writeUInt16LE(20, 4); ch.writeUInt16LE(20, 6); ch.writeUInt16LE(8, 10); ch.writeUInt32LE(crc, 16); ch.writeUInt32LE(def.length, 20); ch.writeUInt32LE(raw.length, 24); ch.writeUInt16LE(nb.length, 28); ch.writeUInt32LE(off, 42);
    locals.push(lh, nb, def); cents.push(ch, nb); off += 30 + nb.length + def.length;
  }
  const cd = Buffer.concat(cents), e = Buffer.alloc(22); e.writeUInt32LE(0x06054b50, 0); e.writeUInt16LE(Object.keys(files).length, 8); e.writeUInt16LE(Object.keys(files).length, 10); e.writeUInt32LE(cd.length, 12); e.writeUInt32LE(off, 16);
  return Buffer.concat([...locals, cd, e]);
}
const S = (r, v, t) => `<c r="${r}"${t === false ? '' : ' t="str"'}><v>${v}</v></c>`;
const sheet = rows => '<?xml version="1.0"?><worksheet><sheetData>' + rows.map((cells, i) => `<row r="${i + 1}">${cells.join('')}</row>`).join('') + '</sheetData></worksheet>';
const wb = '<workbook><sheets><sheet name="DON_HOAN" sheetId="1" r:id="rId1"/></sheets></workbook>';
function sampleXlsx() {
  return zip({
    'xl/workbook.xml': wb,
    'xl/worksheets/sheet1.xml': sheet([
      [S('A1', 'Mã đơn hàng'), S('B1', 'Số lượng'), S('C1', 'Số lượng'), S('D1', 'Lý do hoàn'), S('E1', 'Tên khách hàng'), S('F1', 'Mã vận đơn'), S('G1', 'Đơn vị vận chuyển')],
      [S('A2', '587266216083031671'), S('B2', '1'), S('C2', '1'), S('D2', 'Change of mind', false), S('E2', 'K***h'), S('F2', '862773302897'), S('G2', 'J&amp;T Express')],
      [S('B3', '2'), S('D3', '&lt;x&gt;', false)],
      ['<c r="A5" t="inlineStr"><is><t>ABC</t></is></c>']
    ])
  });
}

test('reader: ô chữ không có kiểu t vẫn đọc được, cột trùng tên, giữ mã 18 chữ số, bỏ dòng trống', () => {
  const rows = readXlsxRows(sampleXlsx());
  assert.equal(rows.length, 3);
  assert.equal(rows[0]['Mã đơn hàng'], '587266216083031671');
  assert.equal(rows[0]['Lý do hoàn'], 'Change of mind');
  assert.equal(rows[0]['Số lượng'], '1'); assert.equal(rows[0]['Số lượng_1'], '1');
  assert.equal(rows[0]['Đơn vị vận chuyển'], 'J&T Express');
  assert.equal(rows[1]['Lý do hoàn'], '<x>');
  assert.equal(rows[2]['Mã đơn hàng'], 'ABC');
});
test('reader: bỏ cột thông tin cá nhân', () => {
  const d = dropPersonalColumns(readXlsxRows(sampleXlsx()));
  assert.ok(!('Tên khách hàng' in d[0])); assert.equal(d[0]['Mã vận đơn'], '862773302897');
});
test('reader: file hỏng báo lỗi rõ', () => { assert.throws(() => readXlsxRows(Buffer.from('không phải zip')), /xlsx/); });

test('trang: mã chạy trong trang là biểu thức hợp lệ và có chặn chữ "cập nhật"', () => {
  const s = pageScript('click', { text: 'Xuất excel' });
  assert.doesNotThrow(() => new Function('return ' + s.replace(/^\(/, '(')));
  assert.match(s, /c\[aậ\]p/);
});

// ---- run() với cửa sổ giả ----
function fakeElectron(script, xlsx, opts = {}) {
  const calls = [];
  class Win extends EventEmitter {
    constructor(o) { super(); this.o = o; this.destroyed = false; const ses = new EventEmitter(); this.webContents = { session: ses, getURL: () => 'https://x.example/orders', executeJavaScript: async code => { const m = /\)\("(\w+)",(.*)\)$/s.exec(code); const a = m[1], arg = JSON.parse(m[2]); calls.push([a, arg]); return script(a, arg, ses); } }; }
    loadURL() { return Promise.resolve(); } isDestroyed() { return this.destroyed; } destroy() { this.destroyed = true; } focus() {} close() { this.destroyed = true; this.emit('closed'); }
  }
  return { electron: { BrowserWindow: Win, session: { fromPartition: () => ({ clearStorageData: async () => {} }) } }, calls };
}
function dl(ses, xlsx, state = 'completed') {
  const item = new EventEmitter(); let p = '';
  item.getFilename = () => 'don_hoan.xlsx'; item.setSavePath = f => { p = f; };
  setTimeout(() => { if (state === 'completed') fs.writeFileSync(p, xlsx); item.emit('done', {}, state); }, 5);
  ses.emit('will-download', {}, item);
}
function mk(script, xlsx) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'swt-')); const f = fakeElectron(script, xlsx);
  const a = new SaleworkAuto(f.electron, dir, { pageTimeout: 1500, stepPause: 1, downloadTimeout: 600 });
  fs.writeFileSync(path.join(dir, 'salework_auto.json'), JSON.stringify({ url: 'https://x.example/orders' }));
  return { a, calls: f.calls, dir };
}
const okScript = xlsx => (a, arg, ses) => {
  if (a === 'state') return { hasExport: true, hasPassword: false, channelTiktok: true, hasTab: true };
  if (a === 'click') { if (arg.text === 'Bắt Đầu Xuất') dl(ses, xlsx); return { ok: true }; }
};

test('run: thứ tự thao tác đúng, lấy được dữ liệu, không bấm "cập nhật", xoá file tạm', async () => {
  const { a, calls } = mk(okScript(sampleXlsx()));
  const r = await a.run();
  assert.equal(r.success, true, JSON.stringify(r)); assert.equal(r.count, 3);
  assert.ok(!('Tên khách hàng' in r.rows[0]));
  const clicks = calls.filter(c => c[0] === 'click').map(c => c[1].text);
  assert.deepEqual(clicks, ['Đang chờ hoàn', 'Xuất excel', 'Theo bộ lọc', 'Bắt Đầu Xuất']);
  assert.ok(!clicks.some(t => /cập nhật/i.test(t)));
  assert.equal(a.status().running, false); assert.match(a.status().lastNote, /3 dòng/);
});
test('run: bị đăng xuất -> báo cần đăng nhập lại, không bấm gì', async () => {
  const { a, calls } = mk((x) => x === 'state' ? { hasExport: false, hasPassword: true } : { ok: true });
  const r = await a.run();
  assert.equal(r.success, false); assert.equal(r.relogin, true);
  assert.equal(calls.filter(c => c[0] === 'click').length, 0);
});
test('run: sai kênh (không phải Tiktok) -> dừng trước khi xuất', async () => {
  const { a, calls } = mk((x, arg) => x === 'state' ? { hasExport: true, channelTiktok: false } : { ok: true });
  const r = await a.run();
  assert.equal(r.success, false); assert.match(r.error, /Tiktok/);
  assert.ok(!calls.some(c => c[0] === 'click' && c[1].text === 'Xuất excel'));
});
test('run: không có file tải về -> báo lỗi đúng bước; chạy lại được', async () => {
  const { a } = mk((x) => x === 'state' ? { hasExport: true, channelTiktok: true } : { ok: true });
  const r = await a.run();
  assert.equal(r.success, false); assert.equal(r.step, 'chờ file tải về');
  assert.equal(a.status().running, false);
});
test('run: chưa cài đặt trang -> báo chưa cấu hình', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'swt-')); const a = new SaleworkAuto(fakeElectron(() => ({})).electron, dir);
  const r = await a.run(); assert.equal(r.success, false); assert.equal(r.step, 'config');
});
test('lưu trang: từ chối địa chỉ đăng nhập', () => {
  const f = fakeElectron(() => ({})); const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'swt-')); const a = new SaleworkAuto(f.electron, dir);
  a.openLogin('https://salework.net'); a.loginWin.webContents.getURL = () => 'https://app.salework.net/login';
  assert.equal(a.saveCurrentPage().success, false);
  a.loginWin.webContents.getURL = () => 'https://app.salework.net/return-orders'; assert.equal(a.saveCurrentPage().success, true);
  assert.equal(a.status().url, 'https://app.salework.net/return-orders');
});
