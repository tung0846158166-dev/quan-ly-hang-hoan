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
  assert.ok(/id="cancelHistoryList"/.test(mobile) && /function renderCancelHistoryPanel/.test(mobile));
});
