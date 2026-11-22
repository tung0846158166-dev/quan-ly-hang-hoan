const test = require('node:test'), assert = require('node:assert'), fs = require('fs'), vm = require('vm'), path = require('path'), zlib = require('zlib');
const src = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
function grab(name) { const m = src.match(new RegExp('function ' + name + '\\([^)]*\\) \\{[\\s\\S]*?\\r?\\n    \\}\\r?\\n')); assert.ok(m, name); return m[0]; }
const code = 'const PHONE_SNAPSHOT_MAX_ROWS = 30000;\n' + grab('getPhoneDueMs') + grab('buildPhoneSnapshotRows');
function ctx(orders, cancelled, archived) {
  const c = {
    orders, cancelledOrders: cancelled, archivedOrders: archived || [],
    isOrderReceived: o => o.status === 'Hàng đã trả' || o.status === 'Đã nhận',
    isOrderUnneeded: o => o.status === 'Không cần nhận',
    isOrderPending: o => !(o.status === 'Hàng đã trả' || o.status === 'Đã nhận') && o.status !== 'Không cần nhận' && o.status !== 'Bị mất' && o.status !== 'Hư hỏng',
    getOrderType: o => o.orderType || 'Trả hàng/Hoàn tiền',
    getOrderPlatform: o => o.platform || 'Shopee',
    computeDeadlineInfo: (rd, dl) => rd ? { due: 1000 } : null,
    ARRIVED_DANGER_HOURS: 24, getOrderArrivedAt: o => o.arrivedAt ? new Date(o.arrivedAt) : null, isCancelAfterPrepOrder: o => /huỷ sau chuẩn bị/i.test(o.platformStatus || '')
  };
  vm.createContext(c); vm.runInContext(code, c); return c;
}
const get = c => JSON.parse(JSON.stringify(vm.runInContext('buildPhoneSnapshotRows()', c)));
test('v1.9.3: snapshot gửi TOÀN BỘ đơn kèm cờ (đã nhận, lưu trữ, đơn hủy, đặt trước, theo dõi, ghi chú)', () => {
  const c = ctx([
    { track: 'A1', orderId: 'O1', status: 'Chưa nhận', returnDate: 'x', preOrder: 'Hàng đặt trước', isWatchlist: true, watchlistReason: 'Khiếu nại', note: 'Gọi shop', items: [{ product: 'Áo', color: 'Đen', size: 'L', qty: 2 }] },
    { track: 'B1', status: 'Hàng đã trả', returnDate: 'x' },
    { track: 'C1', status: 'Bị mất' },
    { track: 'D1', status: 'Không cần nhận' },
    { track: 'E1', orderId: 'OE', status: 'Chưa nhận', orderType: 'Đơn hủy', returnDate: 'x' }
  ], [{ track: 'X1', received: false, items: [{ product: 'Quần', color: 'Xanh', size: 'M', qty: 1 }] }, { track: 'X2', received: true }],
  [{ track: 'Z1', status: 'Hàng đã trả' }]);
  const rows = get(c), by = k => rows.find(r => r.c === k);
  assert.strictEqual(rows.length, 8, 'không bỏ đơn nào có mã');
  assert.deepStrictEqual(rows.slice(0, 3).map(r => r.c).sort(), ['A1', 'E1', 'X1'], 'đơn đang chờ xếp trước');
  assert.ok(rows.slice(3).every(r => r.pd === 0), 'đơn không còn chờ có pd = 0');
  assert.strictEqual(by('A1').co, 'Đen'); assert.strictEqual(by('A1').q, 2); assert.strictEqual(by('A1').due, 0, 'v1.9.71: đơn chưa về không có hạn gửi iPhone');
  assert.strictEqual(by('A1').pre, 1); assert.strictEqual(by('A1').wl, 1); assert.strictEqual(by('A1').wr, 'Khiếu nại'); assert.strictEqual(by('A1').nt, 'Gọi shop');
  assert.strictEqual(by('A1').pd, undefined, 'đơn chờ không gửi pd (mặc định là chờ)');
  assert.strictEqual(by('B1').rv, 1); assert.strictEqual(by('B1').due, 0);
  assert.strictEqual(by('Z1').k, 'a'); assert.strictEqual(by('Z1').rv, 1);
  assert.strictEqual(by('E1').ty, 'Đơn hủy');
  assert.strictEqual(by('X1').k, 'c'); assert.strictEqual(by('X1').pd, undefined);
  assert.strictEqual(by('X2').rv, 1); assert.strictEqual(by('X2').pd, 0);
});
test('PC có xử lý snapshot_request và gửi theo mảnh deflate-raw', () => {
  assert.match(src, /data\.type === 'snapshot_request'/);
  assert.match(src, /new CompressionStream\('deflate-raw'\)/);
  assert.match(src, /type: 'snapshot_chunk'/);
  const z = zlib.deflateRawSync(Buffer.from('{"rows":[]}')); assert.strictEqual(zlib.inflateRawSync(z).toString(), '{"rows":[]}');
});
test('v1.9.1: snapshot gửi cả qua MQTT, lỗi ntfy không dừng, và bỏ qua bản tin PC tự gửi vọng lại', () => {
  const fn = src.match(/async function broadcastOrdersSnapshot[\s\S]*?\r?\n    \}\r?\n/)[0];
  assert.match(fn, /mqttClient\.publish\(mqttTopic, m\)/);
  assert.match(fn, /extraMqttClients/);
  assert.match(fn, /fetch\('https:\/\/ntfy\.sh\/' \+ topic \+ '\?title=Shopee%20Snapshot'/);
  assert.match(fn, /AbortController/, 'ntfy có hạn chờ, không treo vòng gửi');
  assert.match(fn, /pass < 2/, 'MQTT gửi 2 lượt để bù mảnh rớt');
  assert.ok(fn.indexOf('(async () => {') > fn.indexOf('pass < 2'), 'ntfy chạy nền sau khi MQTT gửi xong');
  const filters = src.match(/\['pc_ack', 'snapshot_chunk', 'manifest_sync', 'pc_hello', 'pc_heartbeat', 'watch_sync'\]\.includes\(data\.type\)/g) || [];
  assert.strictEqual(filters.length, 2, 'cả hai bộ nhận MQTT (chính và phụ) đều phải lọc');
});

test('v1.9.4: bảng đối chiếu số đơn tính trên dữ liệu gốc của PC', () => {
  const full = src.match(/function buildPhoneSnapshotStats[\s\S]*?\r?\n    \}\r?\n/)[0];
  const c = {
    orders: [{ status: 'Hàng đã trả', isWatchlist: true, preOrder: 'Hàng đặt trước' }, { status: 'Chưa nhận', orderType: 'Đơn hủy' }, { status: 'Chưa nhận' }],
    archivedOrders: [{ status: 'Hàng đã trả' }, { status: 'Chưa nhận', isWatchlist: true }],
    cancelledOrders: [{ received: true }, { received: false }, { received: true }],
    isOrderReceived: o => o.status === 'Hàng đã trả', isOrderPending: o => o.status !== 'Hàng đã trả',
    getOrderType: o => o.orderType || 'Trả hàng/Hoàn tiền',
    getOrderReturnElapsedInfo: o => ({ level: o.lvl || 'safe' })
  };
  c.getOrderAlertLevel = o => (!c.isOrderPending(o) ? '' : (o.lvl === 'danger' ? 'overdue' : (o.lvl === 'warning' ? 'soon' : '')));
  c.orders[1].lvl = 'danger'; c.orders[2].lvl = 'warning';
  vm.createContext(c); vm.runInContext(full, c);
  const st = JSON.parse(JSON.stringify(vm.runInContext('buildPhoneSnapshotStats(7)', c)));
  assert.deepStrictEqual(st, { m_total: 3, m_recv: 1, m_pend: 2, m_watch: 1, m_pre: 1, m_cancelType: 1, m_danger: 1, m_warn: 1, a_total: 2, a_recv: 1, c_total: 3, c_recv: 2, sent: 7, dropped: 1 });
  assert.match(src, /stats: buildPhoneSnapshotStats\(rows\.length\)/);
});

test('v1.9.5: mỗi dòng gửi iPhone kèm mức cảnh báo Báo Đỏ/Báo Vàng như cột Hạn Cần Xử Lý', () => {
  assert.match(src, /const l = getOrderAlertLevel\(o\); return l === 'overdue' \? 2 : \(l === 'soon' \? 1 : 0\)/);
  assert.match(src, /if \(x\.lv !== undefined\) row\.lv = x\.lv;/);
  assert.match(src, /rv: !!o\.received, lv: clv \}/, 'đơn hủy gửi mức cảnh báo theo hạn xử lý như bảng Thông báo PC');
});

test('v1.9.6: PC nhận ghi chú đơn từ iPhone, đánh dấu theo dõi và báo lại', () => {
  assert.match(src, /data\.type === 'order_note'/);
  assert.match(src, /order\.isWatchlist = watchOn;\s*order\.watchlistReason = watchOn \? noteText : ''/);
  assert.match(src, /ackStage: 'note'/);
});

test('v1.9.7: PC gửi danh sách theo dõi + ghi chú sang iPhone mỗi khi đổi (đồng bộ 2 chiều)', () => {
  assert.match(src, /markUnsavedToFile\(\);\s*try \{ schedulePhoneWatchSync\(\);/);
  const fn = src.match(/function sendPhoneWatchSync[\s\S]*?\r?\n    \}\r?\n/)[0];
  assert.match(fn, /type: 'watch_sync'/);
  assert.match(fn, /sig === phoneWatchSig/);
  const ctx = { orders: [{ isWatchlist: true, track: ' T1 ', orderId: 'O1', watchlistReason: 'x' }, { isWatchlist: false, track: 'T2' }] };
  vm.createContext(ctx); vm.runInContext(src.match(/function buildPhoneWatchList[\s\S]*?\r?\n    \}\r?\n/)[0], ctx);
  assert.deepStrictEqual(JSON.parse(JSON.stringify(vm.runInContext('buildPhoneWatchList()', ctx))), [{ c: 'T1', o: 'O1', r: 'x' }]);
});

test('v1.9.8: PC nhận sửa/xóa đơn từ iPhone và tự gửi lại danh sách khi PC sửa/xóa', () => {
  assert.match(src, /data\.type === 'order_edit' \|\| data\.type === 'order_delete'/);
  assert.match(src, /ackStage: isDel \? 'delete' : 'edit'/);
  assert.match(src, /phone_deleted_orders_backup/);
  assert.match(src, /isLocked \|\| hasLockedCellsInOrder\(eIdx\)/);
  assert.match(src, /schedulePhoneWatchSync\(\); schedulePhoneSnapshotSync\(\);/);
  assert.match(src, /function schedulePhoneSnapshotSync[\s\S]*?broadcastOrdersSnapshot\(false\)/);
});

test('v1.9.9: nạp file sàn không đẩy lại đơn đã có ở Kho lưu trữ / Bảng đơn hủy ra Bảng chính', () => {
  assert.strictEqual((src.match(/findInOtherTables === 'function' && findInOtherTables\(otherIdx, orderData\)\) \{ if \(findInOtherTables\(otherIdx, orderData\) === 'deleted'\) \{ skippedDeleted\+\+; return; \} const eo = enrichOtherTableEntry\(orderData\); if \(eo\.changed\) enrichedOtherCount\+\+; else skippedOther\+\+; return; \}/g) || []).length, 2, 'cả nạp Shopee và TikTok');
  const ctx = { archivedOrders: [{ orderId: 'a1', track: 'spx111' }], cancelledOrders: [{ orderId: 'B2', returnTrack: 'T222' }],
    extractCleanTrackingCode: v => String(v || '').trim().toUpperCase() };
  vm.createContext(ctx);
  vm.runInContext(['orderTrackingCodes', 'orderIdKey', 'buildOtherTablesIndex', 'findInOtherTables'].map(n => src.match(new RegExp('function ' + n + '[\\s\\S]*?\\r?\\n    \\}\\r?\\n'))[0]).join('\n'), ctx);
  vm.runInContext('var idx = buildOtherTablesIndex();', ctx);
  assert.strictEqual(vm.runInContext("findInOtherTables(idx, { orderId: 'A1' })", ctx), 'archive');
  assert.strictEqual(vm.runInContext("findInOtherTables(idx, { orderId: 'x', returnTrack: 't222' })", ctx), 'cancelled');
  assert.strictEqual(vm.runInContext("findInOtherTables(idx, { orderId: 'NEW', track: 'zzz' })", ctx), null);
  assert.strictEqual(vm.runInContext("findInOtherTables(idx, { orderId: 'A1', returnTrack: 'NEWRET' })", ctx), null, 'đơn hoàn lần 2 của cùng mã đơn không bị coi là trùng');
  assert.strictEqual(vm.runInContext("findInOtherTables(idx, { track: 'a1' }, true)", ctx), 'archive', 'điện thoại quét mã đơn vẫn nhận ra');
});

test('v1.9.10: tạo đơn mới / đơn hủy / đẩy ra bảng chính từ điện thoại bỏ qua đơn đã có ở bất kỳ bảng nào', () => {
  assert.match(src, /const dupWhere = typeof locateExistingAnywhere === 'function' \? locateExistingAnywhere\(trackCode, data\.orderId\) : null;/);
  assert.match(src, /const dupWhere2 = typeof locateExistingAnywhere === 'function' \? locateExistingAnywhere\(trackCode, data\.orderId\) : null;/);
  assert.match(src, /inboxItem && typeof isInboxItemDuplicate === 'function' && isInboxItemDuplicate\(inboxItem\)/);
  assert.match(src, /dupSelected = selectedItems\.filter\(isInboxItemDuplicate\)/);
  assert.match(src, /dupAll = newOrdersInbox\.filter\(isInboxItemDuplicate\)/);
});

test('v1.9.19: Báo Đỏ/Báo Vàng trong bảng đối chiếu chỉ đếm đơn còn chờ (đơn Hư hỏng cũ không làm lệch với điện thoại)', () => {
  const full = src.match(/function buildPhoneSnapshotStats[\s\S]*?\r?\n    \}\r?\n/)[0];
  const c = {
    orders: [{ status: 'Hư hỏng', lvl: 'danger' }, { status: 'Chưa nhận', lvl: 'danger' }, { status: 'Hư hỏng', lvl: 'warning' }],
    archivedOrders: [], cancelledOrders: [],
    isOrderReceived: () => false, isOrderPending: o => o.status !== 'Hư hỏng',
    getOrderType: () => 'Trả hàng/Hoàn tiền', getOrderReturnElapsedInfo: o => ({ level: o.lvl })
  };
  c.getOrderAlertLevel = o => (!c.isOrderPending(o) ? '' : (o.lvl === 'danger' ? 'overdue' : (o.lvl === 'warning' ? 'soon' : '')));
  vm.createContext(c); vm.runInContext(full, c);
  const st = vm.runInContext('buildPhoneSnapshotStats(3)', c);
  assert.strictEqual(st.m_danger, 1);
  assert.strictEqual(st.m_warn, 0);
});

test('v1.9.71: due gửi iPhone chỉ có cho đơn đã về (giờ về + 24h) và hủy sau chuẩn bị; đơn chưa về due = 0', () => {
  const t0 = new Date(2026, 10, 22, 10, 0, 0).getTime();
  const c = ctx([
    { track: 'NOTYET', status: 'Chưa nhận', returnDate: 'x', platformStatus: 'Chờ khách gửi hàng' },
    { track: 'ARR', status: 'Chưa nhận', returnDate: 'x', platformStatus: 'Đã về', arrivedAt: t0 },
    { track: 'CANC', status: 'Chưa nhận', returnDate: 'x', platformStatus: 'Huỷ sau chuẩn bị · Đã huỷ' }
  ], []);
  const by = k => get(c).find(r => r.c === k);
  assert.strictEqual(by('NOTYET').due, 0, 'đơn chưa về: iPhone không lên lịch thông báo');
  assert.strictEqual(by('ARR').due, t0 + 24 * 3600000);
  assert.strictEqual(by('CANC').due, 1000);
});
