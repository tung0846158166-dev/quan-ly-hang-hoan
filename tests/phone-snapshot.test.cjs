const test = require('node:test'), assert = require('node:assert'), fs = require('fs'), vm = require('vm'), path = require('path'), zlib = require('zlib');
const src = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
function grab(name) { const m = src.match(new RegExp('function ' + name + '\\([^)]*\\) \\{[\\s\\S]*?\\r?\\n    \\}\\r?\\n')); assert.ok(m, name); return m[0]; }
const code = 'const PHONE_SNAPSHOT_MAX_ROWS = 1500;\n' + grab('buildPhoneSnapshotRows');
function ctx(orders, cancelled) {
  const c = {
    orders, cancelledOrders: cancelled,
    isOrderReceived: o => o.status === 'Hàng đã trả' || o.status === 'Đã nhận',
    isOrderUnneeded: o => o.status === 'Không cần nhận',
    getOrderPlatform: o => o.platform || 'Shopee',
    computeDeadlineInfo: (rd, dl) => rd ? { due: 1000 } : null
  };
  vm.createContext(c); vm.runInContext(code, c); return c;
}
test('snapshot chỉ gồm đơn chưa về, có màu/size/hạn, đơn hủy chưa nhận', () => {
  const c = ctx([
    { track: 'A1', orderId: 'O1', status: 'Chưa nhận', returnDate: 'x', items: [{ product: 'Áo', color: 'Đen', size: 'L', qty: 2 }] },
    { track: 'B1', status: 'Hàng đã trả' },
    { track: 'C1', status: 'Bị mất' },
    { track: 'D1', status: 'Không cần nhận' }
  ], [{ track: 'X1', received: false, items: [{ product: 'Quần', color: 'Xanh', size: 'M', qty: 1 }] }, { track: 'X2', received: true }]);
  const rows = JSON.parse(JSON.stringify(vm.runInContext('buildPhoneSnapshotRows()', c)));
  assert.deepStrictEqual(rows.map(r => r.c), ['A1', 'X1']);
  assert.strictEqual(rows[0].co, 'Đen'); assert.strictEqual(rows[0].sz, 'L'); assert.strictEqual(rows[0].q, 2); assert.strictEqual(rows[0].due, 1000);
  assert.strictEqual(rows[1].k, 'c');
});
test('PC có xử lý snapshot_request và gửi theo mảnh deflate-raw', () => {
  assert.match(src, /data\.type === 'snapshot_request'/);
  assert.match(src, /new CompressionStream\('deflate-raw'\)/);
  assert.match(src, /type: 'snapshot_chunk'/);
  const z = zlib.deflateRawSync(Buffer.from('{"rows":[]}')); assert.strictEqual(zlib.inflateRawSync(z).toString(), '{"rows":[]}');
});
test('v1.9.1: snapshot gửi cả qua MQTT, lỗi ntfy không dừng, và bỏ qua bản tin PC tự gửi vọng lại', () => {
  const fn = src.match(/async function broadcastOrdersSnapshot[\s\S]*?\r?\n    \}\r?\n/)[0];
  assert.match(fn, /mqttClient\.publish\(mqttTopic, msg\)/);
  assert.match(fn, /extraMqttClients/);
  assert.match(fn, /try \{ await fetch\('https:\/\/ntfy\.sh\/' \+ topic \+ '\?title=Shopee%20Snapshot'/);
  const filters = src.match(/\['pc_ack', 'snapshot_chunk', 'manifest_sync', 'pc_hello', 'pc_heartbeat'\]\.includes\(data\.type\)/g) || [];
  assert.strictEqual(filters.length, 2, 'cả hai bộ nhận MQTT (chính và phụ) đều phải lọc');
});
