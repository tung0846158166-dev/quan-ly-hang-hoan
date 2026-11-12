const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const test = require('node:test');
const source = fs.readFileSync(process.env.WORKFLOW_TEST_SOURCE || path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
function context() {
  const notices = [], calls = [];
  const c = { console, TextDecoder, Uint8Array, isLocked: false, activeImportBatch: null, notices, calls,
    removeVietnameseTones: value => String(value).normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D'),
    requireUnlocked: async () => true, alert: value => notices.push(value),
    FileReader: class { readAsArrayBuffer(file) { if (file.error) { this.error = file.error; this.onerror(); } else this.onload({ target: { result: file.rows } }); } },
    captureImportState: () => { calls.push('capture'); return {}; },
    commitImportState: async () => { calls.push('commit'); return true; },
    rollbackImportState: async () => { calls.push('rollback'); return true; },
    ingestTikTokOnlyRecords: async rows => { calls.push(['TikTok', rows]); c.activeImportBatch.messages.push('TikTok committed'); return { success: true }; },
    ingestShopeeOnlyRecords: async rows => { calls.push(['Shopee', rows]); c.activeImportBatch.messages.push('Shopee committed'); return { success: true }; },
    ingestShopeeRecords: async rows => { calls.push(['Auto', rows]); c.activeImportBatch.messages.push('Auto committed'); return { success: true }; }
  };
  vm.createContext(c);
  for (const name of ['readWorkbookRowsFromBuffer', 'preparePlatformImportGroups', 'handlePlatformExcelUpload']) {
    const match = source.match(new RegExp('^    (?:async )?function ' + name + '\\([^]*?^    }', 'm'));
    assert(match, name); vm.runInContext(match[0], c);
  }
  return c;
}
const row = { 'Order ID': 'ORDER001', 'Tracking ID': 'TRACK001', 'Product Name': 'Shirt', Quantity: 1 };
const event = files => ({ target: { files, value: 'selected' } });
function matrixEngine(sheets) {
  return { read: () => ({ SheetNames: Object.keys(sheets), Sheets: sheets }), utils: { sheet_to_json(sheet, opts) {
    if (opts.header === 1) return sheet;
    const start = opts.range || 0, keys = sheet[start] || [];
    return sheet.slice(start + 1).filter(cells => cells.some(value => String(value ?? '').trim())).map(cells => Object.fromEntries(keys.map((key, index) => [key, cells[index] ?? ''])));
  } } };
}
test('workbook chooses recognized order data after instructions and leading blank/title rows', () => {
  const c = context(); c.XLSX = matrixEngine({ Instructions: [['Read first'], ['Use export from seller center']], Orders: [[], ['Monthly orders'], Object.keys(row), Object.values(row)] });
  const rows = c.readWorkbookRowsFromBuffer(new ArrayBuffer(0), false);
  assert.equal(rows.length, 1); assert.equal(rows[0]['Order ID'], 'ORDER001');
});
test('empty, unrecognized and corrupt workbooks are distinguished', () => {
  const c = context(); c.XLSX = matrixEngine({ Empty: [[], []] }); assert.equal(c.readWorkbookRowsFromBuffer(new ArrayBuffer(0), false).length, 0);
  c.XLSX = matrixEngine({ Instructions: [['Read first']] }); assert.throws(() => c.readWorkbookRowsFromBuffer(new ArrayBuffer(0), false), /tiêu đề/);
  c.XLSX = { read() { throw new Error('corrupt workbook'); } }; assert.throws(() => c.readWorkbookRowsFromBuffer(new ArrayBuffer(0), false), /corrupt/);
});
test('different file schemas use separate parser calls and forced Shopee wins TikTok detection', async () => {
  const c = context(); c.readWorkbookRowsFromBuffer = rows => rows;
  const e = event([{ name: 'tiktok.xlsx', rows: [{ ...row, 'Return Order ID': 'CLAIM1' }] }, { name: 'salework.xlsx', rows: [{ 'Mã đơn hàng': 'ORDER002', 'Mã vận đơn': 'TRACK002', 'Tên sản phẩm': 'Other', 'Số lượng': 2 }] }]);
  const result = await c.handlePlatformExcelUpload(e, 'Shopee');
  assert.equal(result.success, true); assert.equal(c.calls.filter(Array.isArray).length, 2);
  assert(c.calls.filter(Array.isArray).every(([platform]) => platform === 'Shopee'));
  assert.equal(c.calls.filter(Array.isArray)[1][1][0]['Tên sản phẩm'], 'Other'); assert.equal(e.target.value, '');
});
test('overlapping files use multiset dedupe, preserving identical legitimate rows within a file', () => {
  const c = context(); const { groups, duplicateCount } = c.preparePlatformImportGroups([{ name: 'one', rows: [row, row] }, { name: 'two', rows: [row, row, row] }]);
  assert.equal(duplicateCount, 2); assert.equal(groups.length, 1); assert.equal(groups[0].length, 3);
  assert.equal(groups[0][0].__sourceFileId, '0'); assert.equal(groups[0][2].__sourceFileId, '1');
});
test('overlap fingerprints compare equivalent Vietnamese and English columns', () => {
  const c = context(); const { groups, duplicateCount } = c.preparePlatformImportGroups([{ name: 'one', rows: [row] }, { name: 'two', rows: [{ 'Mã đơn hàng': 'ORDER001', 'Mã vận đơn': 'TRACK001', 'Tên sản phẩm': 'Shirt', 'Số lượng': 1 }] }]);
  assert.equal(duplicateCount, 1); assert.equal(groups.flat().length, 1);
});
test('continuation SKU keeps its own file identity even if overlapping first row was removed', () => {
  const c = context(); const continuation = { ...row, 'Order ID': '', 'Tracking ID': '', 'Product Name': 'Second' };
  const { groups } = c.preparePlatformImportGroups([{ name: 'one', rows: [row] }, { name: 'two', rows: [row, continuation] }, { name: 'three', rows: [continuation] }]);
  assert.equal(groups[0][1]['Order ID'], 'ORDER001'); assert.equal(groups[0][1]['Tracking ID'], 'TRACK001');
  assert.equal(groups[0][2]['Order ID'], ''); assert.equal(groups[0][2]['Tracking ID'], '');
});
test('entire selection is parsed before mutation and corrupt file produces no success', async () => {
  const c = context(); c.readWorkbookRowsFromBuffer = rows => rows;
  const e = event([{ name: 'good.xlsx', rows: [row] }, { name: 'bad.xlsx', error: new Error('broken') }]);
  assert.equal((await c.handlePlatformExcelUpload(e)).success, false); assert.equal(c.calls.length, 0);
  assert.match(c.notices[0], /bad\.xlsx/); assert.equal(e.target.value, '');
});
test('success appears once only after batch commit and failed ingestion rolls back', async () => {
  const c = context(); c.readWorkbookRowsFromBuffer = rows => rows;
  c.commitImportState = async () => { assert.equal(c.notices.length, 0); c.calls.push('commit'); return true; };
  assert.equal((await c.handlePlatformExcelUpload(event([{ name: 'one', rows: [row] }]), 'Shopee')).success, true);
  assert.equal(c.notices.length, 1); assert.equal(c.activeImportBatch, null);
  c.notices.length = 0; c.calls.length = 0;
  c.ingestShopeeOnlyRecords = async () => { c.activeImportBatch.messages.push('must not escape'); throw new Error('bad import'); };
  assert.equal((await c.handlePlatformExcelUpload(event([{ name: 'bad', rows: [row] }]), 'Shopee')).success, false);
  assert.deepEqual(c.calls, ['capture', 'rollback']); assert.equal(c.notices.length, 1); assert.doesNotMatch(c.notices[0], /must not escape/); assert.equal(c.activeImportBatch, null);
});
test('failed persistence never releases queued success notices', async () => {
  const c = context(); c.readWorkbookRowsFromBuffer = rows => rows;
  c.commitImportState = async () => { c.calls.push('commit'); return false; };
  assert.equal((await c.handlePlatformExcelUpload(event([{ name: 'one', rows: [row] }]), 'Shopee')).success, false);
  assert.equal(c.notices.length, 0); assert.equal(c.activeImportBatch, null);
});
test('second invocation is rejected while first upload awaits file parsing', async () => {
  const c = context(); let finish;
  c.FileReader = class { readAsArrayBuffer() { finish = () => this.onload({ target: { result: [row] } }); } };
  c.readWorkbookRowsFromBuffer = rows => rows;
  const first = c.handlePlatformExcelUpload(event([{ name: 'one' }]), 'Shopee');
  await new Promise(resolve => setImmediate(resolve));
  assert.equal((await c.handlePlatformExcelUpload(event([{ name: 'two' }]), 'Shopee')).success, false);
  finish(); assert.equal((await first).success, true); assert.equal(c.calls.filter(Array.isArray).length, 1);
  assert.equal(c.handlePlatformExcelUpload.busy, false);
});
test('CSV decoding preserves Vietnamese headers and uses actual header row', () => {
  const c = context(); let input;
  c.XLSX = matrixEngine({ CSV: [['Mã đơn hàng', 'Tên sản phẩm', 'Số lượng'], ['ORDER001', 'Áo đỏ', '1']] });
  const read = c.XLSX.read; c.XLSX.read = (text, options) => { input = text; assert.equal(options.type, 'string'); return read(); };
  const result = c.readWorkbookRowsFromBuffer(new TextEncoder().encode('Mã đơn hàng,Tên sản phẩm,Số lượng\nORDER001,Áo đỏ,1').buffer, true);
  assert.match(input, /Áo đỏ/); assert.equal(result[0]['Tên sản phẩm'], 'Áo đỏ');
});
