const test = require('node:test'), assert = require('node:assert'), fs = require('fs'), vm = require('vm'), path = require('path');
const html = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
const a = html.indexOf('const UNMATCHED_SCANS_KEY'), b = html.indexOf('    function phoneOrderMatches(rawCode)');
assert.ok(a > 0 && b > a);
function make(orders, stored) {
  const store = { shopee_unmatched_phone_scans_v1: stored ? JSON.stringify(stored) : undefined };
  const logs = [], notes = [];
  const ctx = { orders, localStorage: { getItem: k => store[k] ?? null, setItem: (k, v) => { store[k] = v; } },
    extractCleanTrackingCode: v => String(v || '').trim().toUpperCase(),
    phoneOrderMatches: c => orders.map((order, index) => ({ order, index })).filter(({ order }) => order.track === c),
    addAuditLog: (t, d) => logs.push(t), addAppNotification: n => notes.push(n), showAppToast() {}, Date, JSON, String };
  vm.createContext(ctx);
  vm.runInContext(html.slice(a, b) + '\nthis.api={remember:rememberUnmatchedScan,reconcile:reconcileUnmatchedScans,list:()=>unmatchedPhoneScans};', ctx);
  return { api: ctx.api, store, logs, notes };
}
test('mã quét trước, đơn xuất hiện sau → tự đã nhận với giờ quét', () => {
  const orders = [];
  const t = make(orders);
  const at = new Date(2026, 10, 16, 9, 5, 7).getTime();
  t.api.remember('spx123', at);
  assert.strictEqual(t.api.reconcile(true), 0);
  assert.strictEqual(t.api.list().length, 1);
  orders.push({ track: 'SPX123', status: 'Chưa nhận' });
  assert.strictEqual(t.api.reconcile(true), 1);
  assert.strictEqual(orders[0].status, 'Hàng đã trả');
  assert.strictEqual(orders[0].recvDate, '16/11/2026 09:05:07');
  assert.strictEqual(t.api.list().length, 0);
  assert.strictEqual(t.notes.length, 1);
});
test('trùng nhiều đơn thì giữ lại, mã quá 30 ngày thì bỏ', () => {
  const orders = [{ track: 'A1', status: 'x' }, { track: 'A1', status: 'x' }];
  const t = make(orders);
  t.api.remember('A1'); t.api.remember('OLD', Date.now() - 31 * 24 * 3600 * 1000);
  t.api.reconcile(true);
  assert.strictEqual(t.api.list().map(e => e.code).join(','), 'A1');
  assert.strictEqual(orders[0].status, 'x');
});
test('không ghi đè giờ nhận có sẵn và không nhận hai lần', () => {
  const orders = [{ track: 'B1', status: 'Hàng đã trả', recvDate: '01/01/2026 00:00:00' }];
  const t = make(orders); t.api.remember('B1');
  assert.strictEqual(t.api.reconcile(true), 0);
  assert.strictEqual(orders[0].recvDate, '01/01/2026 00:00:00');
  assert.strictEqual(t.api.list().length, 0);
});
test('đã nối vào xử lý quét và saveOrders', () => {
  assert.match(html, /rememberUnmatchedScan\(cleanCode\)/);
  assert.match(html, /reconcileUnmatchedScans\(importCommit\)/);
});
