const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const html = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
const s0 = html.indexOf('    let cancelledOrders = [];');
const s1 = html.indexOf('    loadCancelledOrders();', s0);
const s2 = html.indexOf('    setTimeout(checkDeadlineNotifications', s1);
assert.ok(s0 > 0 && s1 > s0 && s2 > s1);
const cancelledBlock = html.slice(s0, s1);
const deadlineBlock = html.slice(s1, s2);

function makeCtx(extra = {}) {
  const store = {};
  const notifs = [];
  const ctx = vm.createContext(Object.assign({
    localStorage: { getItem: k => (k in store ? store[k] : null), setItem: (k, v) => { store[k] = String(v); } },
    document: { getElementById: () => null },
    window: {}, setTimeout: () => 0, savePhotoToDb: async () => true, photoMemoryCache: new Map(),
    console, Date, JSON, getNowDateTimeString: () => '15/11/2026 08:00', showAppToast() {}, formatMoney: n => String(n),
    escapeAttr: s => String(s == null ? '' : s).replace(/'/g, '&#39;').replace(/"/g, '&quot;'),
    removeVietnameseTones: s => String(s).normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/g, 'd'),
    isOrderMatch: (o, kw) => JSON.stringify(o).toLowerCase().includes(String(kw).toLowerCase()),
    parseFullDate: s => { const m = String(s || '').match(/(\d+)\/(\d+)\/(\d+)\s+(\d+):(\d+)/); return m ? new Date(+m[3], +m[2] - 1, +m[1], +m[4], +m[5]) : null; },
    getDeadlineStatus: o => o._s || 'normal',
    getOrderAlertLevel: o => (o._s === 'overdue' ? 'overdue' : (o._s === 'upcoming' ? 'soon' : '')),
    addAppNotification: n => notifs.push(n),
    orders: [], archivedOrders: [], store, notifs,
  }, extra));
  vm.runInContext(cancelledBlock + '\n' + deadlineBlock, ctx);
  return ctx;
}
const fmt = d => `${d.getDate()}/${d.getMonth() + 1}/${d.getFullYear()} ${d.getHours()}:${d.getMinutes()}`;

test('tìm ở bảng chính báo thêm đơn khớp trong kho lưu trữ và bảng đơn hủy, kèm nút đi đến', () => {
  const ctx = makeCtx();
  ctx.archivedOrders.push({ orderId: 'ARC1', track: 'SPXARC', items: [] });
  vm.runInContext("cancelledOrders.push(makeCancelledOrder({ id: 'cxl_1', orderId: 'CX9', track: 'SPXCX9' }))", ctx);
  const out = vm.runInContext("buildOtherTablesSearchHtml('spxcx9')", ctx);
  assert.match(out, /Bảng đơn hủy/);
  assert.match(out, /gotoSearchHit\('cancelled', 'cxl_1'\)/);
  assert.doesNotMatch(out, /Kho lưu trữ/);
  const out2 = vm.runInContext("buildOtherTablesSearchHtml('spxarc')", ctx);
  assert.match(out2, /gotoSearchHit\('archived', 'ARC1'\)/);
  assert.strictEqual(vm.runInContext("buildOtherTablesSearchHtml('a')", ctx), '');
  assert.strictEqual(vm.runInContext("buildOtherTablesSearchHtml('khongco123')", ctx), '');
});

test('đi đến vị trí: đặt tìm kiếm, nhảy đúng trang và tô xanh dòng', () => {
  assert.ok(/data-search-hit="1"/.test(html) && /bg-emerald-800\/70 ring-2 ring-emerald-400/.test(html));
  assert.ok(/cancelledCurrentPage = Math\.floor\(idx \/ cancelledPageSize\) \+ 1/.test(html));
  assert.ok(/archivedCurrentPage = Math\.floor\(idx \/ archivedPageSize\) \+ 1/.test(html));
  assert.ok(/buildOtherTablesSearchHtml\(kw\)/.test(html));
});

test('cảnh báo hạn: gom theo mức, chỉ báo 1 lần mỗi đơn mỗi mức, đơn hủy chưa nhận cũng tính', () => {
  const now = Date.now();
  const ctx = makeCtx();
  ctx.orders.push({ orderId: 'A', track: 'T1', _s: 'upcoming' }, { orderId: 'B', track: 'T2', _s: 'overdue' }, { orderId: 'C', track: 'T3', _s: 'done' });
  vm.runInContext(`cancelledOrders.push(makeCancelledOrder({ id: 'x1', track: 'CXL1', returnDate: '${fmt(new Date(now - 50 * 3600000))}' }), makeCancelledOrder({ id: 'x2', track: 'CXL2', returnDate: '${fmt(new Date(now - 80 * 3600000))}', received: true }))`, ctx);
  vm.runInContext('checkDeadlineNotifications()', ctx);
  assert.strictEqual(ctx.notifs.length, 2);
  const up = ctx.notifs.find(n => /sắp đến hạn/.test(n.title)), ov = ctx.notifs.find(n => /QUÁ HẠN/.test(n.title));
  assert.match(up.title, /^⏰ 2 đơn/);
  assert.match(up.detail, /T1/); assert.match(up.detail, /CXL1 \(hủy\)/);
  assert.match(ov.title, /^🚨 1 đơn/);
  assert.strictEqual(up.type, 'deadline_warning');
  vm.runInContext('checkDeadlineNotifications()', ctx);
  assert.strictEqual(ctx.notifs.length, 2, 'chạy lại không báo trùng');
  ctx.orders[0]._s = 'overdue';
  vm.runInContext('checkDeadlineNotifications()', ctx);
  assert.strictEqual(ctx.notifs.length, 3, 'đơn chuyển từ sắp hạn sang quá hạn thì báo thêm 1 lần');
  assert.match(ctx.notifs[2].title, /QUÁ HẠN/);
});

test('thông báo hạn thuộc nhóm Cảnh báo trong hộp thông báo', () => {
  assert.ok(/n\.type === 'deadline_warning'\) warningCount\+\+/.test(html));
  assert.ok(/item\.type !== 'deadline_warning'\)\) return false/.test(html));
});
