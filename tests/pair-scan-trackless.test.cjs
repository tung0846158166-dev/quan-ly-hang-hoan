const test = require('node:test'), assert = require('node:assert'), fs = require('fs'), vm = require('vm'), path = require('path');
const html = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8').replace(/\r\n/g, '\n');
const a = html.indexOf('const UNMATCHED_SCANS_KEY'), b = html.indexOf('    function phoneOrderMatches(rawCode)');
assert.ok(a > 0 && b > a);
const pa = html.indexOf('    function parseFullDate(val) {');
const pb = html.indexOf('\n    }\n', pa) + 7;
const parseSrc = html.slice(pa, pb);
const ts = (d, h, m, s) => new Date(2026, 10, d, h, m, s).getTime();
function make(orders, inbox) {
  const store = {}, logs = [], notes = [];
  const ctx = { orders, localStorage: { getItem: k => store[k] ?? null, setItem: (k, v) => { store[k] = v; } },
    extractCleanTrackingCode: v => String(v || '').trim().toUpperCase(),
    phoneOrderMatches: c => orders.map((order, index) => ({ order, index })).filter(({ order }) => order.track === c),
    getOrderTrackingCodes: o => [o.track, o.returnTrack, ...(o.trackingCodes || [])].filter(Boolean),
    newOrdersInbox: inbox || [], saveNewOrdersInbox() {}, locateExistingAnywhere: () => null, saveOrders() {},
    autoDetectPlatformFromCode: () => 'Shopee', detectCarrierByTrackingCode: () => 'SPX Express', setInterval() {}, setTimeout() {}, Math, photoMemoryCache: new Map(), savePhotoToDb() {}, deletePhotoFromDb() {},
    addAuditLog: (t, d) => logs.push(t), addAppNotification: n => notes.push(n), showAppToast() {}, Date, JSON, String, Array, Set, Number, Infinity, isNaN };
  vm.createContext(ctx);
  vm.runInContext(parseSrc + html.slice(a, b) + '\nthis.api={reconcile:reconcileUnmatchedScans,inbox:()=>newOrdersInbox,list:()=>unmatchedPhoneScans,remember:rememberUnmatchedScan};', ctx);
  return { api: ctx.api, logs, notes, ctx };
}
const item = (track, at) => ({ id: 'i_' + track, track, orderId: track, time: '', scanTime: at, fromUnmatchedScan: true, photo: '' });
const row = (id, d, h, m, s) => ({ orderId: id, platform: 'Shopee', status: 'Hàng đã trả', recvDate: `${String(d).padStart(2, '0')}/11/2026 ${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}` });

test('dữ liệu thật 22/11: 5 mã SPXVN trong hộp tự gắn vào 5 đơn chỉ có mã đơn hàng', () => {
  const orders = [row('261117G02N82UD', 22, 14, 33, 18), row('261115C5U7CCRA', 22, 14, 33, 2), row('261120R649DWAG', 22, 14, 12, 36),
    row('261116FT1JDBUA', 22, 14, 12, 21), row('261120R279NQEH', 22, 14, 12, 7)];
  const inbox = [item('SPXVN06545042047B', ts(22, 14, 33, 17)), item('SPXVN06343098208B', ts(22, 14, 32, 57)), item('SPXVN06147895539B', ts(22, 14, 12, 35)),
    item('SPXVN06336025530B', ts(22, 14, 12, 20)), item('SPXVN06429563712B', ts(22, 14, 11, 52))];
  const t = make(orders, inbox);
  t.api.reconcile(true);
  assert.strictEqual(t.api.inbox().length, 0);
  assert.deepStrictEqual(orders.map(o => o.track), ['SPXVN06545042047B', 'SPXVN06343098208B', 'SPXVN06147895539B', 'SPXVN06336025530B', 'SPXVN06429563712B']);
  assert.ok(orders.every(o => o.status === 'Hàng đã trả' && o.carrier === 'SPX Express'));
});
test('quá 20 giây hoặc đơn đã có mã vận đơn thì không ghép', () => {
  const orders = [row('A', 22, 14, 0, 0), Object.assign(row('B', 22, 14, 10, 0), { track: 'SPXVN1' })];
  const t = make(orders, [item('SPXVN9', ts(22, 14, 10, 1))]);
  t.api.reconcile(true);
  assert.strictEqual(t.api.inbox().length, 1);
  assert.ok(!orders[0].track);
  assert.strictEqual(orders[1].track, 'SPXVN1');
});
test('hai đơn cùng cách đều một mã thì không đoán', () => {
  const orders = [row('A', 22, 14, 0, 0), row('B', 22, 14, 0, 10)];
  const t = make(orders, [item('SPXVN7', ts(22, 14, 0, 5))]);
  t.api.reconcile(true);
  assert.strictEqual(t.api.inbox().length, 1);
  assert.ok(!orders[0].track && !orders[1].track);
});
test('mã đang chờ trong bộ nhớ (chưa vào hộp) cũng được ghép', () => {
  const orders = [row('A', 22, 14, 0, 1)];
  const t = make(orders, []);
  t.api.remember('SPXVN5', ts(22, 14, 0, 0));
  t.api.reconcile(true);
  assert.strictEqual(orders[0].track, 'SPXVN5');
  assert.strictEqual(t.api.list().length, 0);
});
test('đã nối vào reconcile và hẹn giờ chạy lại khi mở app', () => {
  assert.match(html, /paired = pairScansWithTracklessRows\(\)/);
  assert.match(html, /reconcileUnmatchedScans\(true\); \} catch \(e\) \{\} \}, 30 \* 1000\)/);
});
