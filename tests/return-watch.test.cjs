// v1.9.72: Bảng Hàng Hoàn Về (đơn "Đã đồng ý" tự vào bảng, theo dõi 4 chặng).
const test = require('node:test'), assert = require('node:assert'), fs = require('fs'), path = require('path'), vm = require('vm');
const html = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8').replace(/\r\n/g, '\n');
const extract = n => { const m = html.match(new RegExp('^    function ' + n + '\\([^]*?^    }', 'm')); assert.ok(m, n); return m[0]; };
function make(orders) {
  const c = { orders, String, Array, Date, Boolean, RegExp, isNaN, Set };
  vm.createContext(c);
  c.isOrderReceived = o => o.status === 'Hàng đã trả';
  c.isOrderUnneeded = o => o.status === 'Không cần nhận';
  c.removeVietnameseTones = s => String(s || '').normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D');
  c.extractCleanTrackingCode = v => String(v || '').trim().toUpperCase();
  c.getOrderArrivedAt = o => o.arrivedAt ? new Date(o.arrivedAt) : null;
  vm.runInContext("const RW_STAGES = ['', 'Đã đồng ý', 'Đang về', 'Đã về shop', 'Đã nhận kho'];\n" + html.match(/^    const RW_AGREED_RE = .*$/m)[0], c);
  ['isReturnAgreedStatus', 'getReturnWatchStage', 'syncReturnWatchFlags', 'isInReturnWatch'].forEach(n => vm.runInContext(extract(n), c));
  return c;
}
test('đơn có trạng thái sàn "Đã đồng ý" và chưa nhận thì được đánh dấu vào bảng, chỉ một lần', () => {
  const orders = [{ platformStatus: 'Đã đồng ý', status: 'Chưa trả' }, { platformStatus: 'Đã đồng ý', status: 'Hàng đã trả' },
    { platformStatus: 'Đang xử lý', status: 'Chưa trả' }, { platformStatus: 'Đã đồng ý', status: 'Không cần nhận' }, { platformStatus: 'Seller agreed', status: 'Chưa trả' }];
  const c = make(orders);
  assert.strictEqual(c.syncReturnWatchFlags(), 2);
  assert.ok(orders[0].rwSince > 0 && orders[4].rwSince > 0);
  assert.ok(!orders[1].rwSince && !orders[2].rwSince && !orders[3].rwSince);
  const t = orders[0].rwSince;
  assert.strictEqual(c.syncReturnWatchFlags(), 0);
  assert.strictEqual(orders[0].rwSince, t);
});
test('đơn đã vào bảng thì ở lại dù trạng thái sàn đổi tiếp; bỏ theo dõi (rwOff) thì không tự vào lại', () => {
  const o = { platformStatus: 'Đã đồng ý', status: 'Chưa trả' };
  const c = make([o]);
  c.syncReturnWatchFlags();
  o.platformStatus = 'Đang về';
  assert.strictEqual(c.isInReturnWatch(o), true);
  o.platformStatus = 'Đã đồng ý'; o.rwOff = true; delete o.rwSince;
  assert.strictEqual(c.syncReturnWatchFlags(), 0);
  assert.strictEqual(c.isInReturnWatch(o), false);
});
test('4 chặng: Đã đồng ý → Đang về → Đã về shop → Đã nhận kho', () => {
  const c = make([]);
  assert.strictEqual(c.getReturnWatchStage({ platformStatus: 'Đã đồng ý', status: 'Chưa trả' }), 1);
  assert.strictEqual(c.getReturnWatchStage({ platformStatus: 'Đang về', status: 'Chưa trả' }), 2);
  assert.strictEqual(c.getReturnWatchStage({ platformStatus: 'Đã đồng ý', returnTrack: 'SPXVN1', status: 'Chưa trả' }), 2);
  assert.strictEqual(c.getReturnWatchStage({ platformStatus: 'Đang về', arrivedAt: Date.now() - 1000, status: 'Chưa trả' }), 3);
  assert.strictEqual(c.getReturnWatchStage({ platformStatus: 'Đang về', arrivedAt: Date.now(), status: 'Hàng đã trả' }), 4);
});
test('đã nối vào lưu đơn, nút đầu trang, bảng và dữ liệu gửi iPhone', () => {
  assert.match(html, /try \{ syncReturnWatchFlags\(\); \} catch \(e\) \{\}\n      markUnsavedToFile\(\)/);
  assert.match(html, /id="btnReturnWatchHeader"/);
  assert.match(html, /id="returnWatchModal"/);
  assert.match(html, /if \(x\.rw\) \{ row\.rw = x\.rw; row\.rws = x\.rws \|\| 1; if \(x\.ar\) row\.ar = x\.ar; \}/);
});

// v1.9.75: mọi bảng đều mở được từ menu Nhập / Xuất (nút ở thanh đầu trang có thể bị tràn khỏi màn hình)
test('menu Nhập / Xuất có lối mở nhanh các bảng', () => {
  const fs2 = require('node:fs'), path2 = require('node:path');
  const html = fs2.readFileSync(path2.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
  const a = html.indexOf('id="headerMenuImport"'), b = html.indexOf('id="headerMenuTools"');
  const menu = html.slice(a, b);
  for (const fn of ['openReturnWatchModal', 'openArchivedOrdersModal', 'openNewOrdersModal', 'openCancelledOrdersModal', 'openAnalyticsDashboardModal', 'openStatsModal', 'openQuickCreateOrdersModal', 'openBatchSessionModal']) {
    assert.ok(menu.includes(fn + '();'), fn + ' phải có trong menu');
    assert.ok(new RegExp('function ' + fn + '\\s*\\(').test(html), fn + ' phải là hàm có thật');
  }
});
