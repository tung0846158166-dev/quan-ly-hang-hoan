const test = require('node:test'), assert = require('node:assert'), fs = require('fs'), vm = require('vm'), path = require('path'), zlib = require('zlib');
const src = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
function grab(name) { const m = src.match(new RegExp('function ' + name + '\\([^)]*\\) \\{[\\s\\S]*?\\r?\\n    \\}\\r?\\n')); assert.ok(m, name); return m[0]; }
const code = 'const PHONE_SNAPSHOT_MAX_ROWS = 30000;\n' + grab('buildPhoneSnapshotRows');
function ctx(orders, cancelled, archived) {
  const c = {
    orders, cancelledOrders: cancelled, archivedOrders: archived || [],
    isOrderReceived: o => o.status === 'Hàng đã trả' || o.status === 'Đã nhận',
    isOrderUnneeded: o => o.status === 'Không cần nhận',
    isOrderPending: o => !(o.status === 'Hàng đã trả' || o.status === 'Đã nhận') && o.status !== 'Không cần nhận' && o.status !== 'Bị mất' && o.status !== 'Hư hỏng',
    getOrderType: o => o.orderType || 'Trả hàng/Hoàn tiền',
    getOrderPlatform: o => o.platform || 'Shopee',
    computeDeadlineInfo: (rd, dl) => rd ? { due: 1000 } : null
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
  assert.strictEqual(by('A1').co, 'Đen'); assert.strictEqual(by('A1').q, 2); assert.strictEqual(by('A1').due, 1000);
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
  const filters = src.match(/\['pc_ack', 'snapshot_chunk', 'manifest_sync', 'pc_hello', 'pc_heartbeat'\]\.includes\(data\.type\)/g) || [];
  assert.strictEqual(filters.length, 2, 'cả hai bộ nhận MQTT (chính và phụ) đều phải lọc');
});

test('v1.9.4: bảng đối chiếu số đơn tính trên dữ liệu gốc của PC', () => {
  const full = src.match(/function buildPhoneSnapshotStats[\s\S]*?\r?\n    \}\r?\n/)[0];
  const c = {
    orders: [{ status: 'Hàng đã trả', isWatchlist: true, preOrder: 'Hàng đặt trước' }, { status: 'Chưa nhận', orderType: 'Đơn hủy' }, { status: 'Chưa nhận' }],
    archivedOrders: [{ status: 'Hàng đã trả' }, { status: 'Chưa nhận', isWatchlist: true }],
    cancelledOrders: [{ received: true }, { received: false }, { received: true }],
    isOrderReceived: o => o.status === 'Hàng đã trả', isOrderPending: o => o.status !== 'Hàng đã trả',
    getOrderType: o => o.orderType || 'Trả hàng/Hoàn tiền'
  };
  vm.createContext(c); vm.runInContext(full, c);
  const st = JSON.parse(JSON.stringify(vm.runInContext('buildPhoneSnapshotStats(7)', c)));
  assert.deepStrictEqual(st, { m_total: 3, m_recv: 1, m_pend: 2, m_watch: 1, m_pre: 1, m_cancelType: 1, a_total: 2, a_recv: 1, c_total: 3, c_recv: 2, sent: 7, dropped: 1 });
  assert.match(src, /stats: buildPhoneSnapshotStats\(rows\.length\)/);
});
