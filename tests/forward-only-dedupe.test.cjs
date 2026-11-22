// v1.9.71: dòng cũ chỉ có mã GIAO + dòng mới cùng mã giao đã có mã HOÀN là một kiện (dữ liệu thật 22/11/2026: 109 đơn TikTok bị trùng).
const test = require('node:test'), assert = require('node:assert'), fs = require('fs'), path = require('path'), vm = require('vm');
const html = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8').replace(/\r\n/g, '\n');
const extract = n => { const m = html.match(new RegExp('^    function ' + n + '\\([^]*?^    }', 'm')); assert.ok(m, n); return m[0]; };
function ctxFor(orders) {
  const c = { String, Array, Set, Map, Object, JSON, RegExp, Math, Date, console };
  vm.createContext(c);
  ['removeVietnameseTones', 'extractCleanTrackingCode', 'getReturnParcelId', 'isOrderReceived', 'isSameReturnParcel', 'findStaleClaimDuplicates', 'mergeWarehouseIntoKeeper'].forEach(n => vm.runInContext(extract(n), c));
  vm.runInContext('const dupPlanOf = new Map();\n' + html.match(/^    const isClosedRequest = [^]*?^    };/m)[0], c);
  c.orders = orders; c.getSheetLatestClaims = () => ({});
  return c;
}
function dedupe(orders) {
  const c = ctxFor(orders);
  const idx = vm.runInContext('findStaleClaimDuplicates()', c);
  const plans = vm.runInContext('dupPlanOf', c);
  idx.slice().reverse().forEach(i => { c.mergeWarehouseIntoKeeper(orders[i], plans.get(orders[i]).keep, plans.get(orders[i])); orders.splice(i, 1); });
  return orders;
}
const T = o => Object.assign({ platform: 'TikTok Shop', status: 'Chưa về', items: [], orderId: '587276816345958204' }, o);
const A = o => T(Object.assign({ track: '862797907477', forwardTrack: '862797907477', returnTrack: '', returnOrderId: '', platformStatus: 'Chờ khách gửi hàng' }, o));
const B = o => T(Object.assign({ track: '862797907477', forwardTrack: '862797907477', returnTrack: '854172200329', returnOrderId: '', platformStatus: 'Đang về' }, o));

test('ảnh chụp 22/11: dòng chỉ có mã giao + dòng có mã hoàn → còn 1 dòng (giữ dòng có mã hoàn)', () => {
  const r = dedupe([A(), B()]);
  assert.strictEqual(r.length, 1);
  assert.strictEqual(r[0].returnTrack, '854172200329');
  assert.strictEqual(r[0].platformStatus, 'Đang về');
});
test('dòng cũ đã nhận hàng + ảnh: dòng giữ nhận trạng thái đã nhận, giờ nhận, ảnh', () => {
  const r = dedupe([A({ status: 'Hàng đã trả', recvDate: '22/11/2026 15:51:42', photo: 'data:x' }), B()]);
  assert.strictEqual(r.length, 1);
  assert.strictEqual(r[0].status, 'Hàng đã trả');
  assert.strictEqual(r[0].recvDate, '22/11/2026 15:51:42');
  assert.strictEqual(r[0].photo, 'data:x');
});
test('đơn nhiều kiện: chỉ gộp vào dòng cùng mã giao, kiện hoàn khác giữ nguyên', () => {
  const other = B({ forwardTrack: '', track: '854172072529', returnTrack: '854172072529', returnOrderId: '4043158054933399525' });
  const r = dedupe([A(), other, B({ returnOrderId: '4043158028370151397' })]);
  assert.strictEqual(r.length, 2);
  assert.ok(r.some(o => o.returnTrack === '854172072529'));
  assert.ok(r.some(o => o.returnTrack === '854172200329'));
});
test('hai dòng hoàn cùng mã giao (hoàn một phần) thì không đoán, giữ cả 3', () => {
  const r = dedupe([A(), B(), B({ returnTrack: '854172999999', returnOrderId: '4043000000000000001' })]);
  assert.strictEqual(r.length, 3);
});
test('nạp lại: dòng mới có mã hoàn được nhận ra là cùng kiện với dòng chỉ có mã giao', () => {
  const c = ctxFor([]);
  assert.strictEqual(c.isSameReturnParcel(A(), B()), true);
  assert.strictEqual(c.isSameReturnParcel(B(), A()), true);
  assert.strictEqual(c.isSameReturnParcel(A(), B({ orderId: 'KHAC' })), false);
  assert.strictEqual(c.isSameReturnParcel(A(), B({ forwardTrack: '862000000000' })), false);
});
test('đã nối vào lần dọn sau nạp file và khi mở app', () => {
  assert.match(html, /autoRemoveShadowDuplicates\(\); \} catch \(e\) \{\} \}, 45 \* 1000\)/);
  assert.match(html, /if \(res\.success\) \{ try \{ autoRemoveShadowDuplicates\(\)/);
});
