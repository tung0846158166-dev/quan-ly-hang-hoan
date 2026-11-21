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
    constructor(o) { super(); this.o = o; this.destroyed = false; const ses = new EventEmitter(); if (global.__mouseEvents) this.__mouse = true;
    this.webContents = { sendInputEvent: global.__mouseEvents ? (e => global.__mouseEvents.push(e)) : undefined, setWindowOpenHandler: h => { this.openHandler = h; }, loadedUrls: [], session: ses, getURL: () => 'https://x.example/orders', executeJavaScript: async code => { const m = /\)\("(\w+)",(.*)\)$/s.exec(code); const a = m[1], arg = JSON.parse(m[2]); calls.push([a, arg]); return script(a, arg, ses); } }; }
    loadURL(u) { (this.webContents.loadedUrls = this.webContents.loadedUrls || []).push(u); return Promise.resolve(); } isDestroyed() { return this.destroyed; } destroy() { this.destroyed = true; } focus() {} close() { this.destroyed = true; this.emit('closed'); }
  }
  return { electron: { BrowserWindow: Win, session: { fromPartition: () => ({ clearStorageData: async () => {} }) } }, calls };
}
function dl(ses, xlsx, state = 'completed') {
  const item = new EventEmitter(); let p = '';
  item.getFilename = () => 'don_hoan.xlsx'; item.setSavePath = f => { p = f; };
  setTimeout(() => { if (state === 'completed') fs.writeFileSync(p, xlsx); item.emit('done', {}, state); }, 5);
  ses.emit('will-download', {}, item);
}
function mk(script, xlsx, extraCfg) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'swt-')); const f = fakeElectron(script, xlsx);
  const a = new SaleworkAuto(f.electron, dir, { pageTimeout: 1500, stepPause: 1, downloadTimeout: 600, watchPause: 1, actWait: 20, clickRetry: 20, keepOnError: 0 });
  fs.writeFileSync(path.join(dir, 'salework_auto.json'), JSON.stringify(Object.assign({ url: 'https://x.example/orders', stocked: false }, extraCfg || {})));
  return { a, calls: f.calls, dir };
}
const okScript = xlsx => (a, arg, ses) => {
  if (a === 'state') return { hasExport: true, hasPassword: false, channelTiktok: true, hasTab: true };
  if (a === 'mark' || a === 'banner' || a === 'exists' || a === 'hover') return { ok: true };
  if (a === 'click') { if (arg.text === 'Bắt Đầu Xuất') dl(ses, xlsx); return { ok: true }; }
};

test('run: thứ tự thao tác đúng, lấy được dữ liệu, không bấm "cập nhật", xoá file tạm', async () => {
  const { a, calls } = mk(okScript(sampleXlsx()));
  const r = await a.run();
  assert.equal(r.success, true, JSON.stringify(r)); assert.equal(r.count, 3);
  assert.ok(!('Tên khách hàng' in r.rows[0]));
  const clicks = calls.filter(c => c[0] === 'click').map(c => c[1].text);
  assert.deepEqual(clicks, ['Đang chờ hoàn', 'Theo bộ lọc', 'Bắt Đầu Xuất']);   // Xuất excel chỉ rê chuột
  assert.ok(calls.some(c => c[0] === 'hover' && c[1].text === 'Xuất excel'));
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

test('cửa sổ con (Salework Kho…) được mở trong chính cửa sổ đăng nhập, không tách cửa sổ mới', () => {
  const f = fakeElectron(() => ({})); const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'swt-')); const a = new SaleworkAuto(f.electron, dir);
  a.openLogin('https://salework.net');
  const w = a.loginWin; const r = w.openHandler({ url: 'https://kho.salework.net/orders' });
  assert.equal(r.action, 'deny'); assert.ok(w.webContents.loadedUrls.includes('https://kho.salework.net/orders'));
  assert.equal(w.openHandler({ url: 'http://evil.example' }).action, 'deny');
  assert.ok(!w.webContents.loadedUrls.includes('http://evil.example'));
});
test('dán link mới khi cửa sổ đã mở: chuyển sang link mới', () => {
  const f = fakeElectron(() => ({})); const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'swt-')); const a = new SaleworkAuto(f.electron, dir);
  a.openLogin('https://salework.net'); const w = a.loginWin;
  a.openLogin('https://app.salework.net/staff-login?x=1');
  assert.ok(w.webContents.loadedUrls.includes('https://app.salework.net/staff-login?x=1'));
});

test('chế độ xem: viền đỏ (mark) trước MỖI lần bấm, theo đúng thứ tự', async () => {
  const { a, calls } = mk(okScript(sampleXlsx()));
  const r = await a.run(); assert.equal(r.success, true, JSON.stringify(r));
  const seq = calls.filter(c => c[0] === 'mark' || c[0] === 'click' || c[0] === 'hover').map(c => c[0] + ':' + c[1].text);
  assert.deepEqual(seq, ['mark:Đang chờ hoàn', 'click:Đang chờ hoàn', 'mark:Xuất excel', 'hover:Xuất excel', 'mark:Theo bộ lọc', 'click:Theo bộ lọc', 'mark:Bắt Đầu Xuất', 'click:Bắt Đầu Xuất']);
});
test('tắt chế độ xem: không viền, cửa sổ ẩn', async () => {
  const { a, calls, dir } = mk(okScript(sampleXlsx()));
  a.setWatch(false); assert.equal(a.status().watch, false);
  const r = await a.run(); assert.equal(r.success, true);
  assert.ok(!calls.some(c => c[0] === 'mark'));
});
test('Dừng giữa chừng: không bấm thêm gì, báo đã dừng', async () => {
  let aKeep;
  const f = mk((act, arg, ses) => {
    if (act === 'state') return { hasExport: true, channelTiktok: true };
    if (act === 'mark' && arg.text === 'Xuất excel') aKeep.stop();   // người dùng bấm Dừng đúng lúc app sắp bấm Xuất excel
    return { ok: true };
  });
  aKeep = f.a;
  const r = await f.a.run();
  assert.equal(r.success, false); assert.match(r.error, /dừng/i);
  const clicks = f.calls.filter(c => c[0] === 'click').map(c => c[1].text);
  assert.deepEqual(clicks, ['Đang chờ hoàn']);
  assert.equal(f.a.status().running, false);
});
test('đóng cửa sổ khi đang chạy = dừng ngay', async () => {
  const f = mk((act, arg) => {
    if (act === 'state') return { hasExport: true, channelTiktok: true };
    if (act === 'mark' && arg.text === 'Xuất excel') f.a.curWin.emit('closed');
    return { ok: true };
  });
  const r = await f.a.run();
  assert.equal(r.success, false);
  assert.ok(!f.calls.some(c => c[0] === 'click' && c[1].text === 'Xuất excel'));
});
test('lỗi không thấy nút: báo kèm trang đang ở và các nút nhìn thấy', async () => {
  const f = mk(act => act === 'state' ? { hasExport: false, hasPassword: false, path: 'kho.example/orders/#', title: 'Salework Kho', buttons: ['Bán hàng', 'Đơn hoàn'] } : { ok: true });
  const r = await f.a.run();
  assert.equal(r.success, false); assert.match(r.error, /kho\.example\/orders\/#/); assert.match(r.error, /Bán hàng \| Đơn hoàn/);
});

test('trang: so khớp chữ không phụ thuộc kiểu gõ dấu (NFC/NFD)', () => {
  assert.match(pageScript('state'), /normalize\('NFC'\)/);
});

test('trang trống phần Đơn hoàn: menu chưa mở -> mở Kiểm tra, đối soát rồi vào Đơn hoàn, chạy tiếp', async () => {
  let inOrders = false, groupOpen = false;
  const f = mk((act, arg, ses) => {
    if (act === 'state') return inOrders ? { hasExport: true, channelTiktok: true } : { hasExport: false, hasPassword: false, path: 'stock.x/returnorder', title: 'Kho', buttons: ['Bán hàng'] };
    if ((act === 'mark' || act === 'exists') && arg.text === 'Đơn hoàn' && !groupOpen) return { ok: false, error: 'không thấy "Đơn hoàn"' };
    if (act === 'click') { if (arg.text === 'Kiểm tra, đối soát') groupOpen = true; if (arg.text === 'Đơn hoàn') inOrders = true; if (arg.text === 'Bắt Đầu Xuất') dl(ses, sampleXlsx()); }
    return { ok: true };
  });
  const r = await f.a.run();
  assert.equal(r.success, true, JSON.stringify(r));
  const clicks = f.calls.filter(c => c[0] === 'click').map(c => c[1].text);
  assert.deepEqual(clicks, ['Kiểm tra, đối soát', 'Đơn hoàn', 'Đang chờ hoàn', 'Theo bộ lọc', 'Bắt Đầu Xuất']);
});
test('trang trống nhưng menu đã mở sẵn: bấm thẳng Đơn hoàn, không đóng nhóm menu', async () => {
  let inOrders = false;
  const f = mk((act, arg, ses) => {
    if (act === 'state') return inOrders ? { hasExport: true, channelTiktok: true } : { hasExport: false, path: 'p', title: 't', buttons: [] };
    if (act === 'click') { if (arg.text === 'Đơn hoàn') inOrders = true; if (arg.text === 'Bắt Đầu Xuất') dl(ses, sampleXlsx()); }
    return { ok: true };
  });
  const r = await f.a.run(); assert.equal(r.success, true, JSON.stringify(r));
  assert.ok(!f.calls.some(c => c[0] === 'click' && c[1].text === 'Kiểm tra, đối soát'));
});
test('đi bằng menu vẫn không có nút Xuất excel: báo lỗi kèm trang và nút', async () => {
  const f = mk(act => act === 'state' ? { hasExport: false, path: 'stock.x/returnorder', title: 'Kho', buttons: ['Bán hàng'] } : { ok: true });
  const r = await f.a.run();
  assert.equal(r.success, false); assert.match(r.error, /bằng menu/); assert.match(r.error, /stock\.x\/returnorder/);
});
test('trang: bỏ chữ biểu tượng ở đầu HOẶC cuối khi so khớp nút (không bỏ cả hai)', () => {
  const src = fs.readFileSync(path.join(__dirname, '..', 'salework_auto.js'), 'utf8');
  const line = /const variants = (x => \[.*?\]);/.exec(src)[1];
  const variants = eval(line);
  assert.ok(variants('file_upload xuất excel').includes('xuất excel'));
  assert.ok(variants('xuất excel file_upload').includes('xuất excel'));
  assert.ok(!variants('file_upload xuất excel').includes('xuất'));   // lỗi cũ: bỏ luôn chữ "excel"
  assert.ok(variants('xuất excel').includes('xuất excel'));
});

test('cửa sổ xuất hiện chậm: app đợi (thăm dò) rồi mới bấm', async () => {
  let polls = 0;
  const f = mk((act, arg, ses) => {
    if (act === 'state') return { hasExport: true, channelTiktok: true };
    if (act === 'exists' && arg.text === 'Theo bộ lọc') { polls++; return polls < 3 ? { ok: false, error: 'không thấy "Theo bộ lọc"' } : { ok: true }; }
    if (act === 'click' && arg.text === 'Bắt Đầu Xuất') dl(ses, sampleXlsx());
    return { ok: true };
  });
  f.a.opts.actWait = 3000;
  const r = await f.a.run(); assert.equal(r.success, true, JSON.stringify(r)); assert.ok(polls >= 3);
});
test('không thấy nút: báo kèm chữ đang thấy trên trang (để chẩn đoán)', async () => {
  const f = mk((act, arg) => {
    if (act === 'state') return { hasExport: true, channelTiktok: true };
    if (act === 'exists' && arg.text === 'Theo bộ lọc') return { ok: false, error: 'không thấy "Theo bộ lọc"' };
    if (act === 'texts') return { ok: true, texts: ['Xuất excel', 'Tất cả các đơn', 'Bắt đầu'] };
    return { ok: true };
  });
  const r = await f.a.run();
  assert.equal(r.success, false); assert.equal(r.step, 'chọn "Theo bộ lọc"'); assert.match(r.error, /Tất cả các đơn/);
});
test('chế độ xem + lỗi: cửa sổ được giữ lại để người dùng xem (không bị huỷ ngay)', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'swt-'));
  const f = fakeElectron((act) => act === 'state' ? { hasExport: false, path: 'p', title: 't', buttons: [] } : { ok: true });
  const a = new SaleworkAuto(f.electron, dir, { pageTimeout: 100, stepPause: 1, watchPause: 1, actWait: 10, keepOnError: 50 });
  fs.writeFileSync(path.join(dir, 'salework_auto.json'), JSON.stringify({ url: 'https://x.example/orders', stocked: false }));
  const r = await a.run(); assert.equal(r.success, false);
  assert.equal(a.curWin.isDestroyed(), false);
  await new Promise(res => setTimeout(res, 120)); assert.equal(a.curWin.isDestroyed(), true);
});

test('nút đổi chữ / vẽ lại giữa lúc chờ: bấm lại thành công sau vài lần thử', async () => {
  let fails = 0;
  const f = mk((act, arg, ses) => {
    if (act === 'state') return { hasExport: true, channelTiktok: true };
    if (act === 'click' && arg.text === 'Bắt Đầu Xuất') { if (fails < 2) { fails++; return { ok: false, error: 'không thấy "Bắt Đầu Xuất"' }; } dl(ses, sampleXlsx()); }
    return { ok: true };
  });
  f.a.opts.clickRetry = 3000; f.a.opts.downloadTimeout = 5000;
  const r = await f.a.run(); assert.equal(r.success, true, JSON.stringify(r)); assert.equal(fails, 2);
});
test('bấm không được hẳn: báo kèm chữ đang thấy trên trang', async () => {
  const f = mk((act, arg) => {
    if (act === 'state') return { hasExport: true, channelTiktok: true };
    if (act === 'click' && arg.text === 'Bắt Đầu Xuất') return { ok: false, error: 'không thấy "Bắt Đầu Xuất"' };
    if (act === 'texts') return { ok: true, texts: ['Đang xuất 364 đơn'] };
    return { ok: true };
  });
  const r = await f.a.run();
  assert.equal(r.success, false); assert.equal(r.step, 'bấm Bắt Đầu Xuất'); assert.match(r.error, /Đang xuất 364 đơn/);
});
test('Theo bộ lọc / Bắt Đầu Xuất tìm theo đầu chữ (nút có thể kèm số đơn)', async () => {
  const f = mk(okScript(sampleXlsx()));
  await f.a.run();
  const modes = f.calls.filter(c => c[0] === 'click' && /Theo bộ lọc|Bắt Đầu Xuất/.test(c[1].text)).map(c => c[1].mode);
  assert.deepEqual(modes, ['prefix', 'prefix']);
});

function mouseScript(opts = {}) {
  return (act, arg, ses) => {
    if (act === 'state') return { hasExport: true, channelTiktok: true };
    if (act === 'rect') return { ok: true, x: 100 + arg.text.length, y: 50, topOk: opts.covered !== arg.text, topText: 'lớp phủ' };
    if (act === 'exists' && arg.text === 'Theo bộ lọc') return opts.noPopover ? (opts.clickedExport ? { ok: true } : { ok: false, error: 'không thấy' }) : { ok: true };
    if (act === 'click') { if (arg.text === 'Xuất excel') opts.clickedExport = true; }
    if (act === 'mouse-dl') dl(ses, sampleXlsx());
    return { ok: true };
  };
}
test('chuột thật: rê vào Xuất excel (không bấm), rồi bấm thật Theo bộ lọc / Bắt Đầu Xuất', async () => {
  global.__mouseEvents = [];
  try {
    const ses = {};
    const f = mk((act, arg, s) => { if (act === 'click' && arg.text === 'Bắt Đầu Xuất') dl(s, sampleXlsx()); return mouseScript()(act, arg, s); });
    // với chuột thật, việc bấm "Bắt Đầu Xuất" là sự kiện chuột: giả lập tải file khi nhận mouseUp cuối
    const ev = global.__mouseEvents; const orig = ev.push.bind(ev);
    ev.push = e => { orig(e); if (e.type === 'mouseUp' && ev.filter(z => z.type === 'mouseUp').length === 3) setTimeout(() => f.sesRef && dl(f.sesRef, sampleXlsx()), 1); return ev.length; };
    // lấy session của cửa sổ để phát will-download
    const origRun = f.a.run.bind(f.a);
    const p = origRun(); await new Promise(r => setTimeout(r, 50)); f.sesRef = f.a.curWin && f.a.curWin.webContents.session;
    const r = await p; assert.equal(r.success, true, JSON.stringify(r));
    const types = ev.map(e => e.type);
    // 1 lần rê Xuất excel + 3 lần bấm thật (tab, Theo bộ lọc, Bắt Đầu Xuất) mỗi lần có mouseMove
    assert.equal(types.filter(t => t === 'mouseDown').length, 3);
    const xl = ev.find(e => e.type === 'mouseMove' && e.x === 100 + 'Xuất excel'.length);
    assert.ok(xl, 'phải có rê chuột vào Xuất excel');
    const idx = ev.indexOf(xl); assert.notEqual(ev[idx + 1] && ev[idx + 1].type, 'mouseDown');   // rê xong không bấm ngay
  } finally { delete global.__mouseEvents; }
});
test('chuột thật: nút bị che -> không bấm, báo rõ', async () => {
  global.__mouseEvents = [];
  try {
    const f = mk(mouseScript({ covered: 'Xuất excel' }));
    const r = await f.a.run();
    assert.equal(r.success, false); assert.match(r.error, /bị che/);
    assert.ok(!global.__mouseEvents.some(e => e.type === 'mouseDown' && e.x === 100 + 'Xuất excel'.length));
  } finally { delete global.__mouseEvents; }
});
test('rê chuột chưa mở menu: bấm Xuất excel rồi mới chọn Theo bộ lọc', async () => {
  const opts = { noPopover: true };
  const f = mk((act, arg, s) => { if (act === 'click' && arg.text === 'Bắt Đầu Xuất') dl(s, sampleXlsx()); return mouseScript(opts)(act, arg, s); });
  const r = await f.a.run(); assert.equal(r.success, true, JSON.stringify(r));
  assert.equal(opts.clickedExport, true);
});

test('run: lấy thêm tab "Đã hoàn kho" (chỉ đọc) sau tab Đang chờ hoàn, trả về stocked', async () => {
  const { a, calls } = mk(okScript(sampleXlsx()), null, { stocked: true, extraChannels: [] });
  const r = await a.run();
  assert.equal(r.success, true, JSON.stringify(r)); assert.equal(r.count, 3);
  assert.equal(r.stocked.count, 3); assert.ok(!('Tên khách hàng' in r.stocked.rows[0])); assert.equal(r.stockedError, '');
  const clicks = calls.filter(c => c[0] === 'click').map(c => c[1].text);
  assert.deepEqual(clicks, ['Đang chờ hoàn', 'Theo bộ lọc', 'Bắt Đầu Xuất', 'Đã hoàn kho', 'Theo bộ lọc', 'Bắt Đầu Xuất']);
  assert.ok(!clicks.some(t => /cập nhật/i.test(t)));
});
test('run: tab "Đã hoàn kho" lỗi -> lượt lấy chính vẫn thành công, ghi rõ lỗi', async () => {
  const base = okScript(sampleXlsx());
  const { a } = mk((x, arg, ses) => (x === 'click' && arg.text === 'Đã hoàn kho') ? (() => { throw new Error('không thấy tab'); })() : base(x, arg, ses), null, { stocked: true, extraChannels: [] });
  const r = await a.run();
  assert.equal(r.success, true); assert.equal(r.count, 3); assert.equal(r.stocked, null); assert.match(r.stockedError, /không thấy tab|Đã hoàn kho/);
  assert.match(a.status().lastNote, /Đã hoàn kho lỗi/);
});

// kênh: kịch bản trang giả có menu chọn kênh
function channelScript(xlsx, startChannel, log) {
  let channel = startChannel; const base = okScript(xlsx);
  return (a, arg, ses) => {
    if (a === 'state') return { hasExport: true, hasPassword: false, channel, channelTiktok: /tiktok/i.test(channel), hasTab: true };
    if (a === 'exists' && arg.text === 'Bán ngoài sàn') return { ok: true };
    if (a === 'click' && arg.near === 'Bán ngoài sàn') { log.push(arg.text); channel = arg.text; return { ok: true }; }
    if (a === 'click') log.push(arg.text);
    return base(a, arg, ses);
  };
}
test('run: sau Tiktok, lấy tab Đã hoàn kho của Shopee rồi chuyển LẠI kênh Tiktok', async () => {
  const log = []; const { a } = mk(channelScript(sampleXlsx(), 'Tiktok', log), null, { stocked: true });
  const r = await a.run();
  assert.equal(r.success, true, JSON.stringify(r)); assert.equal(r.extra.length, 1); assert.equal(r.extra[0].channel, 'Shopee'); assert.equal(r.extra[0].count, 3);
  assert.deepEqual(r.extraErrors, []);
  assert.deepEqual(log.filter(t => t === 'Shopee' || t === 'Tiktok'), ['Shopee', 'Tiktok']);   // đổi sang Shopee rồi về Tiktok
  assert.ok(!log.some(t => /cập nhật/i.test(t)));
});
test('run: đang để kênh Shopee từ trước -> chuyển sang Tiktok trước khi xuất', async () => {
  const log = []; const { a } = mk(channelScript(sampleXlsx(), 'Shopee', log), null, { stocked: true, extraChannels: [] });
  const r = await a.run();
  assert.equal(r.success, true, JSON.stringify(r)); assert.equal(log[0], 'Tiktok');
});
test('run: chuyển kênh Shopee lỗi -> TikTok vẫn thành công, báo lỗi sàn khác', async () => {
  const log = []; const inner = channelScript(sampleXlsx(), 'Tiktok', log);
  const { a } = mk((x, arg, ses) => (x === 'click' && arg.near && arg.text === 'Shopee') ? { ok: false, error: 'không thấy "Shopee" trong menu' } : inner(x, arg, ses), null, { stocked: true });
  const r = await a.run();
  assert.equal(r.success, true); assert.equal(r.count, 3); assert.equal(r.extra.length, 0); assert.match(r.extraErrors.join(' '), /Shopee/);
});
