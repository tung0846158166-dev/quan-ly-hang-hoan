const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs'), path = require('path'), vm = require('vm');
const html = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
function extract(name) {
  const m = html.match(new RegExp('^    (?:async )?function ' + name + '\\([^]*?^    }', 'm'));
  assert.ok(m, name); return m[0];
}
function ctx() {
  const c = { String, Array, Object, Set, Map, Date, Math, JSON, parseInt, isNaN };
  vm.createContext(c);
  vm.runInContext("const SHEET_SKIP_CANCEL_TYPES = ['huy truoc chuan bi'];", c);
  for (const n of ['parseFullDate', 'removeVietnameseTones', 'parseSheetItemsCell', 'sheetSkipTypes', 'filterSheetParcelsByDays', 'sheetTabKind', 'sheetRowsToParcels', 'mergeSheetParcels', 'sheetParcelsToRows']) vm.runInContext(extract(n), c);
  return c;
}
const HH = (o) => Object.assign({ 'Mã': 'CL1', 'Mã đơn': 'O1', 'Loại': 'Trả hàng', 'Mã vận đơn': 'SPXVN001', 'Trạng thái về': 'Đang về', 'Về shop lúc': '', 'Hạn khiếu nại': '', 'Hàng': 'KUNTA · Xám, XL × 1; PLAIN · Đen, L × 2', 'SL': '3', 'Tiền': '100.000 ₫', 'Lý do': 'Đổi ý', 'Ngày': '16/11/2026 00:40', 'Cập nhật': '16/11/2026 03:31' }, o);
const HUY = (o) => Object.assign({ 'Ngày': '16/11/2026 00:40', 'Mã': 'CL1', 'Mã đơn': 'O1', 'Loại': 'Trả hàng + hoàn tiền', 'Ai huỷ': 'Khách', 'Lý do': 'Đổi ý', 'Khách ghi': '', 'Sản phẩm': 'KUNTA', 'SL': '1', 'Tiền': '100.000 ₫', 'Hành trình': '', 'Trạng thái': 'Đã về shop', 'Cập nhật': '17/11/2026 10:00', 'Mã SP': '1' }, o);
const TRA = (o) => Object.assign({ 'Ngày yêu cầu': '16/11/2026 00:40', 'Mã trả hàng': 'CL1', 'Mã đơn': 'O1', 'Trạng thái': 'Đang xử lý', 'Lý do': 'Đổi ý', 'Khách ghi': 'size nhỏ', 'SKU': '', 'Tên sản phẩm': 'KUNTA', 'SL': '1', 'Hoàn tiền': '100.000 ₫', 'Mã SP': '1', 'Mã phân loại': '2' }, o);

test('Sheet 3 tab: nhận đúng loại tab theo tiêu đề cột', () => {
  const c = ctx();
  assert.strictEqual(c.sheetTabKind([HH()]), 'hanghoan');
  assert.strictEqual(c.sheetTabKind([TRA()]), 'trahang');
  assert.strictEqual(c.sheetTabKind([HUY()]), 'huy');
  assert.strictEqual(c.sheetTabKind([{ 'Mã đơn hàng': 'x' }]), '');
});

test('Sheet 3 tab: tab Huỷ bỏ "Huỷ trước chuẩn bị" (mặc định) và các loại người dùng tự thêm', () => {
  const c = ctx();
  const rows = [HUY({ 'Mã đơn': 'A', 'Loại': 'Huỷ trước chuẩn bị' }), HUY({ 'Mã đơn': 'B', 'Loại': 'Chưa thanh toán' }), HUY({ 'Mã đơn': 'C', 'Loại': 'Bom hàng' })];
  let r = c.sheetRowsToParcels(rows, 'huy');
  assert.strictEqual(r.skipped, 1);
  assert.deepStrictEqual(r.parcels.map(p => p.orderId).join(','), 'B,C');
  const skip = c.sheetSkipTypes({ skipTypes: 'Huỷ trước chuẩn bị; Chưa thanh toán' });
  r = c.sheetRowsToParcels(rows, 'huy', skip);
  assert.strictEqual(r.skipped, 2);
  assert.deepStrictEqual(r.parcels.map(p => p.orderId).join(','), 'C');
  assert.strictEqual(r.parcels[0].type, 'bom');
  // ô để trống = không bỏ qua loại nào; chưa cài = mặc định
  assert.strictEqual(c.sheetSkipTypes({ skipTypes: '' }).length, 0);
  assert.deepStrictEqual(Array.from(c.sheetSkipTypes({})), ['huy truoc chuan bi']);
});

test('Sheet 3 tab: đơn trùng mã ghép thành 1 — ô mới nhất thắng, mã vận đơn & màu/size từ tab Hàng hoàn được giữ', () => {
  const c = ctx();
  const hh = c.sheetRowsToParcels([HH()], 'hanghoan').parcels;
  const tra = c.sheetRowsToParcels([TRA()], 'trahang').parcels;
  const huy = c.sheetRowsToParcels([HUY()], 'huy').parcels;
  const m = c.mergeSheetParcels([hh, tra, huy]);
  assert.strictEqual(m.length, 1);
  const o = m[0];
  assert.strictEqual(o.track, 'SPXVN001', 'chỉ tab Hàng hoàn có mã vận đơn -> giữ');
  assert.strictEqual(o.requestStatus, 'Đã về shop', 'tab Huỷ cập nhật mới hơn (17/11) -> lấy trạng thái của nó');
  assert.strictEqual(o.parcelStatus, 'Đang về');
  assert.strictEqual(o.buyerNote, 'size nhỏ', 'chỉ tab Trả hàng có ghi chú -> giữ');
  assert.strictEqual(o.items.length, 2);
  assert.strictEqual(o.items[1].variation, 'Đen, L');
  assert.deepStrictEqual(Array.from(o.sources).sort().join(','), 'hanghoan,huy,trahang');
  // Hàng hoàn cập nhật mới hơn -> trạng thái kiện của Hàng hoàn được ưu tiên khi ghi ra dòng
  const rows = c.sheetParcelsToRows(m);
  assert.strictEqual(rows.length, 2, 'mỗi sản phẩm 1 dòng');
  assert.strictEqual(rows[0]['Mã đơn hàng'], 'O1');
  assert.strictEqual(rows[0]['Mã vận đơn'], 'SPXVN001');
  assert.strictEqual(rows[0]['Trạng thái Trả hàng/Hoàn tiền'], 'Đang về');
  assert.strictEqual(rows[0]['Tổng số tiền hoàn trả'], '100.000 ₫');
  assert.strictEqual(rows[1]['Tổng số tiền hoàn trả'], '', 'tiền không cộng 2 lần');
});

test('Sheet 3 tab: tab nào báo Bom thì đơn là Bom; đơn huỷ ghi vào cột Trạng thái đơn hàng', () => {
  const c = ctx();
  const hh = c.sheetRowsToParcels([HH({ 'Mã đơn': 'B1', 'Loại': 'Trả hàng' })], 'hanghoan').parcels;
  const huy = c.sheetRowsToParcels([HUY({ 'Mã đơn': 'B1', 'Loại': 'Bom hàng', 'Trạng thái': 'Đã huỷ' }), HUY({ 'Mã đơn': 'X1', 'Loại': 'Huỷ sau chuẩn bị', 'Trạng thái': 'Đã huỷ' })], 'huy').parcels;
  const m = c.mergeSheetParcels([hh, huy]);
  const b = m.find(p => p.orderId === 'B1'), x = m.find(p => p.orderId === 'X1');
  assert.strictEqual(b.type, 'bom');
  assert.strictEqual(x.type, 'cancel');
  const rows = c.sheetParcelsToRows(m);
  assert.strictEqual(rows.find(r => r['Mã đơn hàng'] === 'B1')['Lý do hủy'], 'Giao hàng thất bại');
  assert.strictEqual(rows.find(r => r['Mã đơn hàng'] === 'X1')['Trạng thái đơn hàng'], 'Huỷ sau chuẩn bị · Đã huỷ');
});

test('Sheet 3 tab: lần đồng bộ sau Shopee đổi dữ liệu -> giá trị mới thay giá trị cũ', () => {
  const c = ctx();
  const v1 = c.mergeSheetParcels([c.sheetRowsToParcels([HUY({ 'Trạng thái': 'Đang xử lý', 'Cập nhật': '17/11/2026 10:00' })], 'huy').parcels]);
  const v2 = c.mergeSheetParcels([c.sheetRowsToParcels([HUY({ 'Trạng thái': 'Đã đồng ý', 'Cập nhật': '17/11/2026 11:00' })], 'huy').parcels]);
  assert.strictEqual(v1[0].requestStatus, 'Đang xử lý');
  assert.strictEqual(v2[0].requestStatus, 'Đã đồng ý');
});

test('Sheet 3 tab: lọc theo số ngày gần nhất (trống = lấy hết, đơn không có ngày vẫn giữ)', () => {
  const c = ctx();
  const ps = [{ orderId: 'A', returnDate: '16/11/2026 10:00' }, { orderId: 'B', returnDate: '01/09/2026 10:00' }, { orderId: 'C', returnDate: '' }];
  const now = new Date(2026, 10, 17, 12).getTime();
  assert.strictEqual(c.filterSheetParcelsByDays(ps, 0, now).parcels.length, 3);
  const r = c.filterSheetParcelsByDays(ps, 30, now);
  assert.deepStrictEqual(r.parcels.map(p => p.orderId).join(','), 'A,C');
  assert.strictEqual(r.dropped, 1);
  // ngày cố định: không trôi theo thời gian
  const later = new Date(2026, 11, 20).getTime();
  assert.deepStrictEqual(c.filterSheetParcelsByDays(ps, '01/11/2026', later).parcels.map(p => p.orderId).join(','), 'A,C');
  assert.deepStrictEqual(c.filterSheetParcelsByDays(ps, '01/09/2026', later).parcels.map(p => p.orderId).join(','), 'A,B,C');
});

test('Bộ nhớ tra cứu lớn: không ghi được localStorage thì lưu IndexedDB, không báo lỗi nạp', async () => {
  const store = {}; const idb = {};
  const c = {
    localStorage: { getItem: k => store[k] ?? null, setItem: (k, v) => { if (String(v).length > 50) throw new Error('QuotaExceededError'); store[k] = String(v); }, removeItem: k => { delete store[k]; } },
    JSON, String, Object, Promise, console: { warn() {} }, activeImportBatch: null,
    markUnsavedToFile() {}, updateShopeeDbBadge() {},
    openSafeStoreDb: () => Promise.resolve({ transaction: () => { const tx = { objectStore: () => ({ put: v => { idb[v.key] = v.data; setTimeout(() => tx.oncomplete()); }, get: k => { const req = {}; setTimeout(() => { req.result = idb[k] ? { data: idb[k] } : undefined; req.onsuccess(); }); return req; } }) }; return tx; } }),
  };
  vm.createContext(c);
  vm.runInContext("const SHOPEE_DB_KEY = 'shopee_excel_order_db_v1'; const ORDERS_STORE_NAME = 'orders_data'; let shopeeOrderDb = {};", c);
  const blockStart = html.indexOf('    const SHOPEE_DB_IDB_FLAG');
  const blockEnd = html.indexOf('    function loadShopeeOrderDb()');
  vm.runInContext(html.slice(blockStart, blockEnd), c);
  vm.runInContext(extract('loadShopeeOrderDb'), c);
  vm.runInContext(extract('saveShopeeOrderDb'), c);
  vm.runInContext("const defaultSampleShopeeDb = {};", c);
  // nhỏ -> localStorage, trả về true ngay
  vm.runInContext("shopeeOrderDb = { a: 1 };", c);
  assert.strictEqual(vm.runInContext('saveShopeeOrderDb(true)', c), true);
  assert.ok(store.shopee_excel_order_db_v1);
  // lớn -> IndexedDB
  vm.runInContext("shopeeOrderDb = {}; for (let i = 0; i < 20; i++) shopeeOrderDb['K' + i] = { orderId: 'K' + i };", c);
  assert.strictEqual(await vm.runInContext('saveShopeeOrderDb(true)', c), true);
  assert.strictEqual(store.shopee_excel_order_db_v1, undefined, 'bản cũ trong localStorage bị gỡ để không nạp nhầm');
  assert.strictEqual(store.shopee_excel_order_db_v1_in_idb, '1');
  assert.strictEqual(Object.keys(idb.shopee_order_db).length, 20);
  // mở lại app -> đọc từ IndexedDB
  vm.runInContext("shopeeOrderDb = {}; loadShopeeOrderDb();", c);
  await vm.runInContext('shopeeDbReady', c);
  assert.strictEqual(vm.runInContext('Object.keys(shopeeOrderDb).length', c), 20);
});
