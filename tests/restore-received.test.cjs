// v1.9.73: khôi phục đơn đã nhận bị đặt lại + khóa kho lưu trữ không làm mất đơn.
const test = require('node:test'), assert = require('node:assert'), fs = require('fs'), path = require('path'), vm = require('vm');
const html = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8').replace(/\r\n/g, '\n');
const m = html.match(/^    function applyReceivedPatch\([^]*?^    }/m); assert.ok(m);
function make(orders) {
  const c = { orders, String, Array, Object, JSON, Date, localStorage: { setItem() {} } };
  vm.createContext(c); c.addAuditLog = () => {}; c.saveOrders = () => {}; c.renderAll = () => {};
  vm.runInContext(m[0], c); return c;
}
const patch = { rows: [
  { orderId: 'A1', claimId: 'C1', fields: { status: 'Hàng đã trả', recvDate: '20/11/2026 10:00:00', returnTrack: 'SPX1', photoId: 'p_1' } },
  { orderId: 'A2', claimId: 'C2', fields: { status: 'Hàng đã trả', recvDate: '19/11/2026 10:00:00' } },
  { orderId: 'A3', claimId: 'C3', fields: { status: 'Hàng đã trả', recvDate: '19/11/2026 11:00:00' } },
  { orderId: 'A9', claimId: 'C9', fields: { status: 'Hàng đã trả', recvDate: 'x' } }] };
test('chỉ đơn đang chưa nhận được khôi phục; đã nhận sẵn / không thấy thì bỏ qua', () => {
  const orders = [
    { orderId: 'A1', status: 'Chưa trả', recvDate: '', returnTrack: '', rawDetails: { claimId: 'C1' } },
    { orderId: 'A2', status: 'Hàng đã trả', recvDate: '21/11/2026 08:00:00', rawDetails: { claimId: 'C2' } },
    { orderId: 'A3', status: 'Chưa trả', recvDate: '', rawDetails: { claimId: 'C3' } }, { orderId: 'A3', status: 'Chưa trả', recvDate: '', rawDetails: { claimId: 'C3' } }];
  const r = make(orders).applyReceivedPatch(patch);
  assert.strictEqual(r.restored, 1); assert.strictEqual(r.already, 1); assert.strictEqual(r.ambiguous, 1); assert.strictEqual(r.notFound, 1);
  assert.strictEqual(orders[0].status, 'Hàng đã trả'); assert.strictEqual(orders[0].recvDate, '20/11/2026 10:00:00');
  assert.strictEqual(orders[0].returnTrack, 'SPX1'); assert.strictEqual(orders[0].photoId, 'p_1');
  assert.strictEqual(orders[1].recvDate, '21/11/2026 08:00:00');
  assert.strictEqual(orders[2].status, 'Chưa trả');
});
test('khóa kho lưu trữ phân biệt 2 yêu cầu khác nhau của cùng mã đơn + mã giao', () => {
  assert.ok(html.includes("const archKey = a =>"));
  const k = html.match(/const archKey = a => (.*);/)[1];
  const f = new Function('a', 'return ' + k);
  const x = { orderId: 'O', track: 'T', rawDetails: { claimId: 'C1' }, recvDate: '1' }, y = { orderId: 'O', track: 'T', rawDetails: { claimId: 'C2' }, recvDate: '2' };
  assert.notStrictEqual(f(x), f(y)); assert.strictEqual(f(x), f(Object.assign({}, x)));
});
