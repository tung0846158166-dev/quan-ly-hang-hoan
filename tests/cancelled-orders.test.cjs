const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const html = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
const mobile = fs.readFileSync(path.join(__dirname, '..', 'mobile_scanner.html'), 'utf8');
const start = html.indexOf('    let cancelledOrders = [];');
const end = html.indexOf('    loadCancelledOrders();', start);
assert.ok(start > 0 && end > start, 'không tìm thấy khối đơn hủy');
const block = html.slice(start, end);

function makeCtx() {
  const store = {};
  const acks = [];
  const ctx = vm.createContext({
    localStorage: { getItem: k => (k in store ? store[k] : null), setItem: (k, v) => { store[k] = String(v); } },
    document: { getElementById: () => null },
    window: {}, setTimeout: () => 0, savePhotoToDb: async () => true, photoMemoryCache: new Map(),
    console,
    sendAckToPhone: (...a) => acks.push(a),
    extractCleanTrackingCode: c => String(c || '').trim().toUpperCase(),
    autoDetectPlatformFromCode: c => (/^SPX/i.test(c) ? 'Shopee' : 'TikTok Shop'),
    detectCarrierByTrackingCode: c => (/^SPX/i.test(c) ? 'SPX Express' : 'J&T Express'),
    getNowDateTimeString: () => '15/11/2026 08:00',
    showAppToast() {}, appendPhoneLiveLog() {}, playScanSuccessTone() {}, playScanDuplicateTone() {},
    formatMoney: n => String(n),
    removeVietnameseTones: s => String(s).normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D'),
    acks, store,
  });
  vm.runInContext(block + '\nloadCancelledOrders();', ctx);
  return ctx;
}

test('điện thoại tạo đơn hủy: vào bảng, quét trùng chỉ nhận 1 đơn và báo đã tạo', () => {
  const ctx = makeCtx();
  const r1 = vm.runInContext("handleCancelledOrderScan({ code: 'spx123', requestId: 'r1', room: 'A', cancelled: true })", ctx);
  assert.strictEqual(r1.success, true);
  vm.runInContext("handleCancelledOrderScan({ code: 'SPX123', requestId: 'r2', room: 'A', cancelled: true })", ctx);
  assert.strictEqual(vm.runInContext('cancelledOrders.length', ctx), 1);
  assert.strictEqual(vm.runInContext("cancelledOrders[0].orderType", ctx), 'Đơn hủy');
  assert.strictEqual(vm.runInContext("cancelledOrders[0].platform", ctx), 'Shopee');
  assert.strictEqual(ctx.acks[0][3], 'success');
  assert.strictEqual(ctx.acks[1][3], 'duplicate');
  assert.match(ctx.acks[1][2], /ĐÃ TẠO/);
  assert.ok(JSON.parse(ctx.store.shopee_cancelled_orders_v1).length === 1, 'phải lưu bền');
});

test('quét nhận hàng đối chiếu bảng đơn hủy: lần đầu nhận, lần sau báo trùng, cùng requestId gửi lại đúng ack cũ', () => {
  const ctx = makeCtx();
  vm.runInContext("handleCancelledOrderScan({ code: 'TT999', requestId: 'c1', room: 'A', cancelled: true })", ctx);
  assert.strictEqual(vm.runInContext('cancelledOrders[0].received', ctx), true, 'đơn hủy quét từ điện thoại mặc định đã nhận');
  assert.ok(vm.runInContext('cancelledOrders[0].recvDate', ctx), 'có thời gian về kho');
  vm.runInContext("setCancelledReceived(cancelledOrders[0].id, false)", ctx);
  assert.strictEqual(vm.runInContext('cancelledOrders[0].recvDate', ctx), '', 'chuyển chưa nhận thì xóa ngày về kho');
  ctx.acks.length = 0;
  assert.strictEqual(vm.runInContext("confirmCancelledReceipt('tt999', { requestId: 'q1', room: 'A' }, false)", ctx), true);
  assert.strictEqual(ctx.acks[0][3], 'success');
  assert.match(ctx.acks[0][2], /ĐƠN HỦY/);
  assert.strictEqual(vm.runInContext('cancelledOrders[0].received', ctx), true);
  vm.runInContext("confirmCancelledReceipt('TT999', { requestId: 'q1', room: 'A' }, false)", ctx);
  assert.strictEqual(ctx.acks[1][3], 'success', 'gửi lại cùng yêu cầu không biến thành trùng');
  vm.runInContext("confirmCancelledReceipt('TT999', { requestId: 'q2', room: 'A' }, false)", ctx);
  assert.strictEqual(ctx.acks[2][3], 'duplicate');
  assert.strictEqual(vm.runInContext("confirmCancelledReceipt('KHONGCO', { requestId: 'q3', room: 'A' }, false)", ctx), false);
});

test('nhập file: gộp nhiều dòng sản phẩm theo mã đơn, mặc định loại đơn hủy', () => {
  const ctx = makeCtx();
  const rows = [
    { 'Mã đơn hàng': 'D1', 'Mã vận đơn': 'SPX1', 'Tên sản phẩm': 'Áo', 'Số lượng': '2', 'Giá': '100000' },
    { 'Mã đơn hàng': 'D1', 'Mã vận đơn': 'SPX1', 'Tên sản phẩm': 'Quần', 'Số lượng': '1', 'Giá': '50000' },
    { 'Mã đơn hàng': 'D2', 'Tên sản phẩm': 'Nón', 'Số lượng': '3' },
  ];
  ctx.rows = rows;
  const out = vm.runInContext('mapCancelledRows(rows)', ctx);
  assert.strictEqual(out.length, 2);
  assert.strictEqual(out[0].items.length, 2);
  assert.strictEqual(out[0].orderType, 'Đơn hủy');
  assert.deepStrictEqual(JSON.parse(JSON.stringify(vm.runInContext('cancelledOrderTotals(mapCancelledRows(rows)[0])', ctx))), { qty: 3, value: 250000 });
});

test('bảng đơn hủy không có 3 cột ghi chú/lý do, có ô tìm kiếm, lọc và tổng cuối bảng', () => {
  const m = html.match(/<div id="cancelledOrdersModal"[\s\S]*?<tbody id="cancelledTableBody">/);
  assert.ok(m);
  assert.ok(!/Ghi chú người mua|Lý do hoàn|Ghi chú của tôi/i.test(m[0]));
  assert.ok(/cancelledSearchInput/.test(m[0]) && /cancelledPlatformFilter/.test(m[0]) && /cancelledFileInput/.test(m[0]));
  assert.ok(/cancelledTotalQty/.test(html) && /cancelledTotalOrders/.test(html));
});

test('PC: nhận đơn hủy từ điện thoại và đối chiếu quét nhận hàng cả hai bảng', () => {
  assert.ok(/data\.type === 'new_order_scan' && data\.cancelled === true\) return handleCancelledOrderScan/.test(html));
  assert.ok(/findCancelledOrder\(cleanCode\) !== -1 && phoneOrderMatches\(cleanCode\)\.length === 0/.test(html));
  assert.ok(/confirmCancelledReceipt\(cleanCode, null, true\)/.test(html));
});

test('điện thoại: có nút Tạo đơn hủy, gửi cờ cancelled, lịch sử riêng', () => {
  assert.ok(/switchMobileScanMode\('cancel_order'\)/.test(mobile));
  assert.ok(/handleNewOrderScan\(clean, capturedPhoto, currentMobileMode === 'cancel_order'\)/.test(mobile));
  assert.ok(/cancelled: cancelled === true,\s*carrier/.test(mobile));
  assert.ok(/function historyEntryKind/.test(mobile));
});

test('đơn hủy: ảnh gửi kèm được lưu theo yêu cầu, ghép đúng đơn; đơn mới không gửi ảnh', async () => {
  const ctx = makeCtx();
  const saved = {};
  ctx.savePhotoToDb = async (id, d) => { saved[id] = d; return true; };
  ctx.photoMemoryCache = new Map();
  const photo = 'data:image/jpeg;base64,AAAA';
  ctx.photo = photo;
  vm.runInContext("handleCancelledOrderScan({ code: 'SPX777', requestId: 'r9', captureId: 'c9', room: 'A', cancelled: true })", ctx);
  const target = vm.runInContext("findCancelledByPhotoRequest({ requestId: 'r9', captureId: 'c9' })", ctx);
  assert.ok(target, 'tìm được đơn theo requestId');
  assert.strictEqual(vm.runInContext("findCancelledByPhotoRequest({ requestId: 'zzz' })", ctx), null);
  ctx.acks.length = 0;
  const r = await vm.runInContext("attachCancelledPhoto(findCancelledByPhotoRequest({ requestId: 'r9', captureId: 'c9' }), 'SPX777', photo, { requestId: 'r9', captureId: 'c9', room: 'A' })", ctx);
  assert.strictEqual(r.success, true);
  assert.strictEqual(ctx.acks[0][7].ackStage, 'photo');
  assert.strictEqual(ctx.acks[0][7].photoSaved, true);
  assert.strictEqual(Object.values(saved)[0], photo);
  assert.ok(vm.runInContext('cancelledOrders[0].photoId', ctx));
  assert.ok(!JSON.stringify(JSON.parse(ctx.store.shopee_cancelled_orders_v1)).includes('base64'), 'không nhét ảnh vào localStorage');
});

test('điện thoại: đơn hủy giữ ảnh, đơn mới bỏ ảnh; PC có cột Ảnh và route ảnh theo yêu cầu', () => {
  assert.ok(/if \(!cancelled\) capturedPhoto = ''/.test(mobile));
  assert.ok(/Ảnh đơn hàng<\/th>/.test(html.match(/<div id="cancelledOrdersModal"[\s\S]*?<tbody id="cancelledTableBody">/)[0]));
  assert.ok(/findCancelledByPhotoRequest\(capture\)/.test(html));
});

test('v1.8.38: tạo tay, đổi nhận/chưa nhận (xóa/ghi ngày về), chọn nhiều thao tác hàng loạt', () => {
  const ctx = makeCtx();
  ctx.filteredCancelledOrders = [];
  ctx.renderCancelledTable = () => {}; ctx.applyCancelledFilters = () => {};
  ctx.appConfirm = async () => true;
  ctx.vals = { ccCode: 'spx777', ccProduct: 'Áo', ccColor: 'Đen', ccSize: 'M', ccQty: '2', ccPlatform: 'auto' };
  ctx.document.getElementById = id => ({ value: ctx.vals[id] || '', checked: false, classList: { toggle() {}, add() {}, remove() {}, contains: () => true }, textContent: '' });
  assert.strictEqual(vm.runInContext('submitCancelledCreateForm()', ctx), true);
  assert.strictEqual(vm.runInContext('cancelledOrders[0].source', ctx), 'manual');
  assert.strictEqual(vm.runInContext('cancelledOrders[0].received', ctx), false);
  assert.strictEqual(vm.runInContext("cancelledOrders[0].items[0].size", ctx), 'M');
  // trùng mã không tạo thêm
  assert.strictEqual(vm.runInContext('submitCancelledCreateForm()', ctx), false);
  assert.strictEqual(vm.runInContext('cancelledOrders.length', ctx), 1);
  vm.runInContext("setCancelledReceived(cancelledOrders[0].id, true)", ctx);
  assert.strictEqual(vm.runInContext('cancelledOrders[0].recvDate', ctx), '15/11/2026 08:00');
  vm.runInContext("setCancelledReceived(cancelledOrders[0].id, false)", ctx);
  assert.strictEqual(vm.runInContext('cancelledOrders[0].recvDate', ctx), '');
  vm.runInContext("selectedCancelledIds.add(cancelledOrders[0].id); bulkSetCancelledReceived(true)", ctx);
  assert.strictEqual(vm.runInContext('cancelledOrders[0].received', ctx), true);
});

test('v1.8.38: tách màu/size từ "Tên phân loại hàng" khi nhập file', () => {
  const ctx = makeCtx();
  ctx.rows = [{ 'Mã đơn hàng': 'X1', 'Tên sản phẩm': 'Áo', 'Tên phân loại hàng': 'Xanh Nâu,L - Premium Cao Cấp', 'Số lượng': '1' }];
  const it = vm.runInContext('mapCancelledRows(rows)[0].items[0]', ctx);
  assert.strictEqual(it.color, 'Xanh Nâu');
  assert.strictEqual(it.size, 'L');
});

test('v1.8.38: bảng đơn hủy bỏ 4 cột, có checkbox, màu, size, nút tạo; điện thoại mặc định Tự động', () => {
  const m = html.match(/<div id="cancelledOrdersModal"[\s\S]*?<tbody id="cancelledTableBody">/)[0];
  assert.ok(!/Ngày gửi|Hạn xử lý|Trạng thái sàn|Hàng đặt trước/.test(m));
  assert.ok(/cancelledSelectAll/.test(m) && />Màu</.test(m) && />Size</.test(m) && /openCancelledCreateForm/.test(m));
  assert.match(mobile, /let scannerDecodeMode = 'auto';/);
});
