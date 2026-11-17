// Mô phỏng PC nhận lệnh từ iPhone: ghi chú/theo dõi, sửa, xóa, tạo đơn mới/đơn hủy, đẩy ra bảng chính (v1.9.7 - 1.9.11).
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs'), vm = require('node:vm'), path = require('node:path');
const html = fs.readFileSync(path.join(__dirname, '../Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
function extract(n) { const m = html.match(new RegExp('^    (?:async )?function ' + n + '\\([^]*?^    }', 'm')); assert(m, n); return m[0]; }

function harness({ orders = [], archived = [], cancelled = [], inbox = [] } = {}) {
  const acks = [], saves = [], audits = [];
  const c = vm.createContext({
    console, Date, Math, Promise, Map, Set, JSON, Number, String, Array, Boolean,
    orders, archivedOrders: archived, cancelledOrders: cancelled, newOrdersInbox: inbox, isLocked: false, phoneRoomId: 'A',
    ORDER_TYPE_OPTIONS: ['Giao không thành công', 'Trả hàng/Hoàn tiền', 'Đơn hủy', 'THẤT THU'],
    extractCleanTrackingCode: v => String(v || '').trim().toUpperCase(),
    sendAckToPhone: (...v) => { acks.push(v); },
    saveOrders: () => { saves.push('orders'); return Promise.resolve({ success: true }); },
    saveNewOrdersInbox: () => ({ success: true }), saveCancelledOrders: () => ({ success: true }),
    renderAll() {}, updateWatchlistBadge() {}, addAuditLog: (a, b) => audits.push([a, b]), showAppToast() {}, addAppNotification() {},
    appendPhoneLiveLog() {}, playScanSuccessTone() {}, playScanDuplicateTone() {}, getNowDateTimeString: () => 'now',
    autoDetectPlatformFromCode: () => 'Shopee', detectCarrierByTrackingCode: () => '', importSingleNewOrder: id => { c.imported = id; },
    hasLockedCellsInOrder: () => false, shiftCellLocksAfterOrderDelete() {}, shiftCellEditHistoryAfterOrderDelete() {},
    localStorage: { getItem: () => null, setItem() {} }, window: {}, schedulePhoneWatchSync() {},
    findCancelledOrder: code => c.cancelledOrders.findIndex(o => [o.track, o.orderId].includes(code)),
    makeCancelledOrder: o => Object.assign({ id: 'c' + Math.random() }, o), setCancelledPhoto() {},
    getAutoPlatform: () => 'Shopee', typeof_: 0
  });
  for (const n of ['getOrderTrackingCodes', 'getPhoneOrderMatches', 'phoneOrderMatches', 'findExistingMainOrder', 'orderTrackingCodes', 'orderIdKey',
    'buildOtherTablesIndex', 'findInOtherTables', 'locateExistingAnywhere', 'isInboxItemDuplicate', 'handleCancelledOrderScan', 'processIncomingPhonePayload']) {
    if (html.includes('function ' + n + '(')) vm.runInContext(extract(n), c);
  }
  vm.runInContext(html.match(/const EXISTING_TABLE_LABEL = [^;]+;/)[0].replace('const ', 'var '), c);
  return { c, acks, saves, audits };
}
const last = h => h.acks.at(-1);

test('order_note: thay nội dung theo dõi (không nối thêm) và đánh dấu theo dõi; watch=false bỏ theo dõi', async () => {
  const h = harness({ orders: [{ track: 'T1', orderId: 'O1', status: 'Chưa trả' }] });
  await h.c.processIncomingPhonePayload({ type: 'order_note', code: 'T1', note: 'lần 1', room: 'A', requestId: 'r1' });
  assert.equal(h.c.orders[0].isWatchlist, true); assert.equal(h.c.orders[0].watchlistReason, 'lần 1');
  await h.c.processIncomingPhonePayload({ type: 'order_note', code: 'T1', note: 'lần 2', room: 'A', requestId: 'r2' });
  assert.equal(h.c.orders[0].watchlistReason, 'lần 2');
  await h.c.processIncomingPhonePayload({ type: 'order_note', code: 'T1', note: '', watch: false, room: 'A', requestId: 'r3' });
  assert.equal(h.c.orders[0].isWatchlist, false); assert.equal(h.c.orders[0].watchlistReason, '');
  assert.equal(last(h)[3], 'success'); assert.equal(last(h)[7].ackStage, 'note');
});

test('order_note: không thấy đơn thì not_found, không tạo gì', async () => {
  const h = harness();
  await h.c.processIncomingPhonePayload({ type: 'order_note', code: 'ZZ', note: 'x', room: 'A', requestId: 'r' });
  assert.equal(last(h)[3], 'not_found'); assert.equal(h.c.orders.length, 0);
});

test('order_edit: sửa hàng hóa, trạng thái, loại đơn; đơn nhiều sản phẩm từ chối sửa hàng hóa; đơn khóa từ chối', async () => {
  const h = harness({ orders: [{ track: 'T1', status: 'Chưa trả', items: [{ product: 'A', color: 'đỏ', size: 'M', qty: 1 }] },
    { track: 'T2', items: [{ product: 'A' }, { product: 'B' }] }] });
  await h.c.processIncomingPhonePayload({ type: 'order_edit', code: 'T1', fields: { n: 'Áo', co: 'xanh', sz: 'L', q: 3, st: 'Hàng đã trả', ty: 'Đơn hủy' }, room: 'A', requestId: 'e1' });
  const o = h.c.orders[0];
  assert.deepEqual([o.items[0].product, o.items[0].color, o.items[0].size, o.items[0].qty, o.status, o.orderType], ['Áo', 'xanh', 'L', 3, 'Hàng đã trả', 'Đơn hủy']);
  await h.c.processIncomingPhonePayload({ type: 'order_edit', code: 'T1', fields: { st: 'giá trị lạ', ty: 'loại lạ' }, room: 'A', requestId: 'e2' });
  assert.equal(o.status, 'Hàng đã trả', 'trạng thái không hợp lệ bị bỏ qua'); assert.equal(o.orderType, 'Đơn hủy');
  await h.c.processIncomingPhonePayload({ type: 'order_edit', code: 'T2', fields: { n: 'X' }, room: 'A', requestId: 'e3' });
  assert.equal(last(h)[3], 'invalid'); assert.equal(h.c.orders[1].items[0].product, 'A');
  h.c.isLocked = true;
  await h.c.processIncomingPhonePayload({ type: 'order_edit', code: 'T1', fields: { n: 'Y' }, room: 'A', requestId: 'e4' });
  assert.equal(last(h)[3], 'locked'); assert.equal(o.items[0].product, 'Áo');
});

test('order_delete: xóa đúng 1 đơn, mã trùng nhiều đơn thì từ chối, gửi lại lệnh cũ trả not_found chứ không xóa nhầm', async () => {
  const h = harness({ orders: [{ track: 'T1' }, { track: 'T2' }, { track: 'D' }, { track: 'D' }] });
  await h.c.processIncomingPhonePayload({ type: 'order_delete', code: 'T1', room: 'A', requestId: 'd1' });
  assert.deepEqual(h.c.orders.map(o => o.track), ['T2', 'D', 'D']); assert.equal(last(h)[3], 'success');
  await h.c.processIncomingPhonePayload({ type: 'order_delete', code: 'T1', room: 'A', requestId: 'd1' });
  assert.equal(last(h)[3], 'not_found'); assert.equal(h.c.orders.length, 3);
  await h.c.processIncomingPhonePayload({ type: 'order_delete', code: 'D', room: 'A', requestId: 'd2' });
  assert.equal(last(h)[3], 'ambiguous'); assert.equal(h.c.orders.length, 3);
});

test('tạo đơn mới từ điện thoại: đã có ở Bảng chính / Kho lưu trữ / Bảng đơn hủy thì báo trùng và không vào hộp', async () => {
  const h = harness({ orders: [{ track: 'M1' }], archived: [{ track: 'A1', orderId: 'OA' }], cancelled: [{ track: 'C1', orderId: 'OC' }] });
  for (const [code, label] of [['M1', 'BẢNG CHÍNH'], ['A1', 'KHO LƯU TRỮ'], ['C1', 'BẢNG ĐƠN HỦY'], ['OA', 'KHO LƯU TRỮ']]) {
    await h.c.processIncomingPhonePayload({ type: 'new_order_scan', code, room: 'A', requestId: 'n' + code });
    assert.equal(last(h)[3], 'duplicate'); assert.match(last(h)[2], new RegExp(label));
  }
  assert.equal(h.c.newOrdersInbox.length, 0);
  await h.c.processIncomingPhonePayload({ type: 'new_order_scan', code: 'NEW1', room: 'A', requestId: 'nn' });
  assert.equal(last(h)[3], 'success'); assert.equal(h.c.newOrdersInbox.length, 1);
});

test('tạo đơn hủy từ điện thoại: đã có ở Bảng chính / Kho lưu trữ thì không tạo; chưa có thì tạo', async () => {
  const h = harness({ orders: [{ track: 'M1' }], archived: [{ track: 'A1' }] });
  await h.c.processIncomingPhonePayload({ type: 'new_order_scan', cancelled: true, code: 'M1', room: 'A', requestId: 'x1' });
  assert.equal(last(h)[3], 'duplicate'); assert.equal(h.c.cancelledOrders.length, 0);
  await h.c.processIncomingPhonePayload({ type: 'new_order_scan', cancelled: true, code: 'A1', room: 'A', requestId: 'x2' });
  assert.equal(last(h)[3], 'duplicate'); assert.equal(h.c.cancelledOrders.length, 0);
  await h.c.processIncomingPhonePayload({ type: 'new_order_scan', cancelled: true, code: 'N9', room: 'A', requestId: 'x3' });
  assert.equal(last(h)[3], 'success'); assert.equal(h.c.cancelledOrders.length, 1);
});

test('đẩy ra bảng chính từ hộp: đơn trùng bị gỡ khỏi hộp và không nhập; đơn mới thì nhập', async () => {
  const h = harness({ archived: [{ track: 'A1' }], inbox: [{ id: 'i1', track: 'A1', orderId: 'A1' }, { id: 'i2', track: 'N1', orderId: 'N1' }] });
  await h.c.processIncomingPhonePayload({ type: 'inbox_push', code: 'A1', room: 'A', requestId: 'p1' });
  assert.equal(last(h)[3], 'duplicate'); assert.equal(h.c.imported, undefined); assert.equal(h.c.newOrdersInbox.length, 1);
  await h.c.processIncomingPhonePayload({ type: 'inbox_push', code: 'N1', room: 'A', requestId: 'p2' });
  assert.equal(last(h)[3], 'success'); assert.equal(h.c.imported, 'i2');
});

test('đơn hoàn lần 2 của cùng mã đơn (mã vận đơn hoàn khác) không bị coi là trùng đơn đã lưu trữ khi so theo dữ liệu nạp', () => {
  const h = harness({ archived: [{ orderId: 'ORD', returnTrack: 'R1', track: 'R1' }] });
  const idx = h.c.buildOtherTablesIndex();
  assert.equal(h.c.findInOtherTables(idx, { orderId: 'ORD', returnTrack: 'R2' }), null);
  assert.equal(h.c.findInOtherTables(idx, { orderId: 'ORD', returnTrack: 'R1' }), 'archive');
});
