const test = require('node:test'), assert = require('node:assert'), fs = require('fs'), vm = require('vm'), path = require('path');
const html = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
const a = html.indexOf('const UNMATCHED_SCANS_KEY'), b = html.indexOf('    function phoneOrderMatches(rawCode)');
assert.ok(a > 0 && b > a);
function make(orders, stored, other) {
  const store = { shopee_unmatched_phone_scans_v1: stored ? JSON.stringify(stored) : undefined };
  const logs = [], notes = [];
  const ctx = { orders, localStorage: { getItem: k => store[k] ?? null, setItem: (k, v) => { store[k] = v; } },
    extractCleanTrackingCode: v => String(v || '').trim().toUpperCase(),
    phoneOrderMatches: c => orders.map((order, index) => ({ order, index })).filter(({ order }) => order.track === c),
    newOrdersInbox: [], saveNewOrdersInbox() {}, locateExistingAnywhere: c => (other || []).includes(c) ? 'archive' : null,
    autoDetectPlatformFromCode: () => 'Shopee', detectCarrierByTrackingCode: () => 'SPX Express', setInterval() {}, setTimeout() {}, Math, photoMemoryCache: new Map(), savePhotoToDb() {}, deletePhotoFromDb() {},
    addAuditLog: (t, d) => logs.push(t), addAppNotification: n => notes.push(n), showAppToast() {}, Date, JSON, String };
  vm.createContext(ctx);
  vm.runInContext(html.slice(a, b) + '\nthis.api={attach:attachPhotoToUnmatched,cache:photoMemoryCache,promote:promoteUnmatchedScans,inbox:()=>newOrdersInbox,convert:null,remember:rememberUnmatchedScan,reconcile:reconcileUnmatchedScans,list:()=>unmatchedPhoneScans};', ctx);
  return { api: ctx.api, store, logs, notes };
}
test('mã quét trước, đơn xuất hiện sau → tự đã nhận với giờ quét', () => {
  const orders = [];
  const t = make(orders);
  const at = new Date(2026, 10, 10, 9, 5, 7).getTime();
  t.api.remember('spx123', at);
  assert.strictEqual(t.api.reconcile(true), 0);
  assert.strictEqual(t.api.list().length, 1);
  orders.push({ track: 'SPX123', status: 'Chưa nhận' });
  assert.strictEqual(t.api.reconcile(true), 1);
  assert.strictEqual(orders[0].status, 'Hàng đã trả');
  assert.strictEqual(orders[0].recvDate, '10/11/2026 09:05:07');
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
  assert.match(html, /rememberUnmatchedScan\(cleanCode, capture/);
  assert.match(html, /reconcileUnmatchedScans\(importCommit\)/);
});

test('v1.9.13: mã chưa có đơn sau 10 phút → vào Hộp Đơn Mới; chưa đủ 10 phút thì giữ', () => {
  const t = make([]);
  t.api.remember('NEW1', Date.now() - 11 * 60 * 1000, Date.now() - 11 * 60 * 1000);
  t.api.remember('FRESH', Date.now() - 60 * 1000);
  assert.strictEqual(t.api.promote(), 1);
  const inbox = t.api.inbox();
  assert.strictEqual(inbox.length, 1);
  assert.strictEqual(inbox[0].track, 'NEW1');
  assert.strictEqual(inbox[0].fromUnmatchedScan, true);
  assert.strictEqual(t.api.list().map(e => e.code).join(','), 'FRESH');
});
test('v1.9.13: có ở kho lưu trữ/đơn hủy thì không tạo; trùng nhiều đơn thì giữ', () => {
  const t = make([{ track: 'DUP', status: 'x' }, { track: 'DUP', status: 'x' }], null, ['ARC1']);
  t.api.remember('ARC1', Date.now() - 20 * 60 * 1000, Date.now() - 20 * 60 * 1000);
  t.api.remember('DUP', Date.now() - 20 * 60 * 1000, Date.now() - 20 * 60 * 1000);
  assert.strictEqual(t.api.promote(), 0);
  assert.strictEqual(t.api.inbox().length, 0);
  assert.strictEqual(t.api.list().map(e => e.code).join(','), 'DUP');
});
test('v1.9.13: đơn trong hộp, sau đó file sàn có đơn → đã nhận và gỡ khỏi hộp', () => {
  const orders = [];
  const t = make(orders);
  const at = new Date(2026, 10, 10, 8, 0, 0).getTime();
  t.api.remember('LATE1', at, at);
  t.api.promote();
  assert.strictEqual(t.api.inbox().length, 1);
  orders.push({ track: 'LATE1', status: 'Chưa nhận' });
  assert.strictEqual(t.api.reconcile(true), 1);
  assert.strictEqual(orders[0].status, 'Hàng đã trả');
  assert.strictEqual(orders[0].recvDate, '10/11/2026 08:00:00');
  assert.strictEqual(t.api.inbox().length, 0);
});
test('v1.9.13: đẩy từ hộp ra bảng chính ghi sẵn Hàng đã trả', () => {
  assert.match(html, /status: item\.fromUnmatchedScan \? 'Hàng đã trả' : 'Chưa trả'/);
  assert.match(html, /recvDate: item\.fromUnmatchedScan && item\.scanTime/);
});

const PHOTO = 'data:image/jpeg;base64,AAAA';
test('v1.9.13: ảnh đi kèm mã chưa có đơn → sang Hộp Đơn Mới', () => {
  const t = make([]);
  t.api.remember('IMG1', Date.now() - 20 * 60 * 1000, Date.now() - 20 * 60 * 1000);
  assert.strictEqual(t.api.attach('IMG1', PHOTO), true);
  assert.strictEqual(t.api.attach('IMG1', 'khong-phai-anh'), false);
  t.api.promote();
  assert.strictEqual(t.api.inbox()[0].photo, PHOTO);
});
test('v1.9.13: ảnh đi kèm → gắn vào đơn khi file sàn có đơn', () => {
  const orders = [];
  const t = make(orders);
  t.api.attach('IMG2', PHOTO);
  orders.push({ track: 'IMG2', status: 'Chưa nhận' });
  assert.strictEqual(t.api.reconcile(true), 1);
  assert.strictEqual(orders[0].photo, PHOTO);
  assert.ok(orders[0].photoId);
});
test('v1.9.13: ảnh từ điện thoại được nối vào applyIncomingPhonePhoto', () => {
  assert.match(html, /attachPhotoToUnmatched\(cleanCode, photoDataUrl, capture/);
});

test('v1.9.13: giờ quét thật được giữ, nhưng chờ 10 phút tính từ lúc máy tính nhận', () => {
  const t = make([]);
  const old = Date.now() - 5 * 3600 * 1000;      // quét cách đây 5 giờ lúc máy tính tắt
  t.api.remember('OFF1', old);                    // vừa được gửi lại sau khi máy tính bật
  assert.strictEqual(t.api.list()[0].t, old);
  assert.strictEqual(t.api.promote(), 0);          // chưa đủ 10 phút kể từ lúc nhận → chưa vào hộp
  assert.strictEqual(t.api.list().length, 1);
});
test('v1.9.13: giờ quét vô lý (tương lai/0) thì dùng giờ hiện tại', () => {
  const t = make([]);
  t.api.remember('BAD1', Date.now() + 5 * 24 * 3600 * 1000);
  t.api.remember('BAD2', 0);
  assert.ok(t.api.list().every(e => e.t <= Date.now() + 1000));
});

test('v1.9.14: lượt quét gửi muộn (hàng chờ) vào hộp ngay, quét trực tiếp thì chờ 10 phút', () => {
  const t = make([]);
  const old = Date.now() - 3 * 3600 * 1000;
  t.api.remember('Q1', old, Date.now() - 20 * 1000);          // quét 3 giờ trước, máy tính nhận 20 giây trước
  t.api.remember('LIVE1', Date.now() - 5000, Date.now() - 20 * 1000);  // quét trực tiếp
  assert.strictEqual(t.api.list().find(e => e.code === 'Q1').late, true);
  assert.strictEqual(t.api.list().find(e => e.code === 'LIVE1').late, false);
  assert.strictEqual(t.api.promote(), 1);
  assert.strictEqual(t.api.inbox().map(i => i.track).join(','), 'Q1');
  assert.strictEqual(t.api.list().map(e => e.code).join(','), 'LIVE1');
});
test('v1.9.14: ảnh tới sau khi mã đã vào hộp thì gắn vào dòng trong hộp', () => {
  const t = make([]);
  t.api.remember('Q2', Date.now() - 3 * 3600 * 1000, Date.now() - 20 * 1000);
  t.api.promote();
  assert.strictEqual(t.api.inbox()[0].photo, '');
  assert.strictEqual(t.api.attach('Q2', 'data:image/jpeg;base64,AAAA'), true);
  assert.strictEqual(t.api.inbox()[0].photo, 'data:image/jpeg;base64,AAAA');
  assert.strictEqual(t.api.list().length, 0);
});
