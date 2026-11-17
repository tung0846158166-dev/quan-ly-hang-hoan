const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs'), path = require('path'), vm = require('vm');
const html = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');

function extract(name) {
  const i = html.indexOf('function ' + name + '(');
  assert.ok(i >= 0, name);
  let depth = 0, j = html.indexOf('{', i);
  for (let k = j; k < html.length; k++) { if (html[k] === '{') depth++; else if (html[k] === '}') { depth--; if (!depth) return html.slice(i, k + 1); } }
}
const ctx = {};
vm.createContext(ctx);
vm.runInContext('function parseFullDate(v){const m=String(v||"").match(/^(\\d{1,2})\\/(\\d{1,2})\\/(\\d{4})/);return m?new Date(+m[3],+m[2]-1,+m[1]):null}', ctx);
for (const n of ['statsBucketKey', 'statsBucketLabel', 'computeStats']) vm.runInContext(extract(n), ctx);

test('thống kê chia theo ngày / tuần / tháng và đếm quá hạn, sắp đến hạn', () => {
  const list = [
    { rd: '02/11/2026 10:00', received: true, pending: false, level: '' },
    { rd: '02/11/2026', received: true, pending: false, level: '' },
    { rd: '03/11/2026', received: false, pending: true, level: 'overdue' },
    { rd: '09/11/2026', received: false, pending: true, level: 'soon' },
    { rd: '', received: false, pending: true, level: '' },
  ];
  const day = ctx.computeStats(list, 'day');
  assert.strictEqual(day.buckets.length, 3);
  assert.strictEqual(day.unknown, 1);
  assert.strictEqual(day.total, 5);
  assert.strictEqual(day.overdue, 1);
  assert.strictEqual(day.soon, 1);
  const week = ctx.computeStats(list, 'week');
  assert.strictEqual(week.buckets.length, 2);   // thứ Hai 02/11 và 09/11/2026
  assert.strictEqual(week.buckets[1].total, 3);
  const month = ctx.computeStats(list, 'month');
  assert.strictEqual(month.buckets.length, 1);
  assert.strictEqual(month.buckets[0].received, 2);
});

test('đơn hoàn lấy cả kho lưu trữ, đơn hủy lấy bảng đơn hủy', () => {
  const ret = extract('collectReturnStatsItems');
  assert.ok(ret.includes('archivedOrders') && !ret.includes("'Đơn hủy'"), 'đơn hoàn = toàn bộ bảng chính + kho lưu trữ');
  assert.ok(extract('collectCancelStatsItems').includes('cancelledOrders'));
  assert.ok(html.includes('id="statsModal"') && html.includes('openStatsModal()'));
});

test('thống kê có chọn sàn: cả 2 sàn / Shopee / TikTok Shop', () => {
  assert.ok(html.includes('id="statsPlatformSelect"') && html.includes('setStatsPlatform(this.value)'));
  assert.ok(extract('renderStatsModal').includes('statsByPlatform'));
  for (const n of ['collectReturnStatsItems', 'collectCancelStatsItems']) assert.ok(extract(n).includes('platform'), n);
});

test('v1.9.19: thanh bên trái giữ nguyên id các nút, ô tổng quan dùng cùng mức Báo Đỏ/Vàng với tab lọc', () => {
  const sb = extract('buildAppSidebar');
  for (const id of ['btnOpenArchivedOrders', 'btnOpenCancelledOrders', 'btnNewOrdersHeader', 'btnOpenStats', 'btnAddOrder', 'btnPhoneScanner', 'kpiStrip']) assert.ok(sb.includes("'" + id + "'"), id);
  assert.ok(!sb.includes('innerHTML = \'\''), 'không xóa nút');
  assert.ok(html.includes('function setAppDrawer') && sb.includes("key === 'Escape'") && sb.includes('appDrawerBackdrop'), 'menu ☰ đóng khi bấm ra ngoài / Esc');
  const k = extract('renderKpiStrip');
  assert.ok(k.includes('getOrderAlertLevel'));
  assert.ok(extract('collectReturnStatsItems').includes('getOrderAlertLevel'));
});

test('v1.9.20: thống kê đã về kho tách Bảng chính / Lưu trữ và không tính trùng đơn ở cả 2 nơi', () => {
  const c = { orders: [{ orderId: 'A', track: '1', status: 'Hàng đã trả', recvDate: '16/11/2026' }, { orderId: 'B', track: '2', status: 'Hàng đã trả', recvDate: '16/11/2026' }],
    archivedOrders: [{ orderId: 'A', track: '1', status: 'Hàng đã trả', recvDate: '16/11/2026' }, { orderId: 'C', track: '3', status: 'Hàng đã trả', recvDate: '16/11/2026' }],
    cancelledOrders: [], isOrderReceived: o => o.status === 'Hàng đã trả', getOrderPlatform: () => 'Shopee',
    isOrderPending: o => o.status === 'Chưa trả', getOrderAlertLevel: () => '', computeDeadlineInfo: () => null, statsScope: 'all', currentPlatformFilter: 'all', orderMatchesTableScope: () => true };
  vm.createContext(c);
  vm.runInContext('function parseFullDate(v){const m=String(v||"").match(/^(\\d{1,2})\\/(\\d{1,2})\\/(\\d{4})/);return m?new Date(+m[3],+m[2]-1,+m[1]):null}', c);
  vm.runInContext("const statsOrderKey = o => String(o.orderId || '').trim() + '|' + String(o.track || '').trim();", c);
  for (const n of ['statsBucketKey', 'computeStats', 'collectReturnStatsItems', 'collectCancelStatsItems', 'collectReceivedStatsItems']) vm.runInContext(extract(n), c);
  const r = vm.runInContext("computeStats(collectReceivedStatsItems('return'), 'day')", c);
  assert.strictEqual(r.total, 3);
  assert.strictEqual(r.buckets[0].main, 2);
  assert.strictEqual(r.buckets[0].arch, 1);
});

test('v1.9.20: thanh phân trang nằm ngay dưới bảng, có ◀ ▶, số đơn đang xem và ô đến trang', () => {
  const sb = extract('buildAppSidebar');
  assert.ok(sb.includes("tableFooterPager") && sb.includes('gotoPageInput') && sb.includes('paginationSummaryText'));
  assert.ok(html.includes('Đang xem đơn ${startIdx + 1}–${endIdx} / ${totalFiltered} đơn'));
});

test('v1.9.21: mỗi dòng thống kê cộng khớp: Số đơn = Đã về + Còn chờ + Khác; đơn trùng bảng chính/lưu trữ chỉ tính 1 lần', () => {
  const c = { orders: [{ orderId: 'A', track: '1', status: 'Hàng đã trả', returnDate: '15/11/2026', recvDate: '16/11/2026' }, { orderId: 'B', track: '2', status: 'Chưa trả', returnDate: '15/11/2026' }, { orderId: 'D', track: '4', status: 'Không cần nhận', returnDate: '15/11/2026' }],
    archivedOrders: [{ orderId: 'A', track: '1', status: 'Hàng đã trả', returnDate: '15/11/2026', recvDate: '16/11/2026' }],
    cancelledOrders: [{ id: 'x', received: true, recvDate: '15/11/2026' }], isOrderReceived: o => o.status === 'Hàng đã trả', getOrderPlatform: () => 'Shopee',
    isOrderPending: o => o.status === 'Chưa trả', getOrderAlertLevel: () => '', computeDeadlineInfo: () => null, statsScope: 'all', currentPlatformFilter: 'all', orderMatchesTableScope: () => true };
  vm.createContext(c);
  vm.runInContext('function parseFullDate(v){const m=String(v||"").match(/^(\\d{1,2})\\/(\\d{1,2})\\/(\\d{4})/);return m?new Date(+m[3],+m[2]-1,+m[1]):null}', c);
  vm.runInContext("const statsOrderKey = o => String(o.orderId || '').trim() + '|' + String(o.track || '').trim();", c);
  for (const n of ['statsBucketKey', 'computeStats', 'collectReturnStatsItems', 'collectCancelStatsItems', 'collectReceivedStatsItems']) vm.runInContext(extract(n), c);
  const f = vm.runInContext("computeStats(collectReturnStatsItems(), 'day')", c);
  assert.strictEqual(f.total, 3, 'đơn A ở cả 2 nơi chỉ tính 1 lần');
  const b = f.buckets[0];
  assert.strictEqual(b.total, b.received + b.pending + b.other);
  assert.strictEqual(b.other, 1);
  const can = vm.runInContext("computeStats(collectCancelStatsItems(), 'day')", c);
  assert.strictEqual(can.unknown, 1, 'đơn hủy không có ngày hủy vẫn được đếm ở dòng Chưa có ngày');
  assert.ok(html.includes('function showStatsDetail'), 'bấm vào số để xem danh sách đơn');
});

test('v1.9.21: phạm vi "Đúng như bảng chính" chỉ lấy đơn bảng chính khớp bộ lọc (giống số trên tab), không lấy kho lưu trữ', () => {
  const c = { orders: [{ orderId: 'A', track: '1', status: 'Hàng đã trả', p: 'Shopee' }, { orderId: 'B', track: '2', status: 'Chưa trả', p: 'TikTok Shop' }],
    archivedOrders: [{ orderId: 'C', track: '3', status: 'Hàng đã trả' }], cancelledOrders: [],
    isOrderReceived: o => o.status === 'Hàng đã trả', isOrderPending: o => o.status === 'Chưa trả', getOrderAlertLevel: () => '', getOrderPlatform: o => o.p,
    statsScope: 'table', currentPlatformFilter: 'Shopee', orderMatchesTableScope: o => o.p === 'Shopee' };
  vm.createContext(c);
  vm.runInContext("const statsOrderKey = o => String(o.orderId || '').trim() + '|' + String(o.track || '').trim();", c);
  vm.runInContext(extract('collectReturnStatsItems'), c);
  const r = vm.runInContext('collectReturnStatsItems()', c);
  assert.strictEqual(r.length, 1);
  assert.strictEqual(r[0].o.orderId, 'A');
  const tabs = html.slice(html.indexOf('let countAll = 0, countWaiting = 0'), html.indexOf('countAll++;'));
  assert.ok(tabs.includes('orderMatchesTableScope(o)'), 'số trên tab dùng cùng hàm phạm vi');
});

test('v1.9.22: phạm vi "bảng chính + kho lưu trữ" lọc kho lưu trữ theo cùng bộ lọc với bảng chính', () => {
  const c = { orders: [{ orderId: 'A', track: '1', status: 'Hàng đã trả', p: 'Shopee' }],
    archivedOrders: [{ orderId: 'A', track: '1', status: 'Hàng đã trả', p: 'Shopee' }, { orderId: 'C', track: '3', status: 'Hàng đã trả', p: 'Shopee' }, { orderId: 'D', track: '4', status: 'Hàng đã trả', p: 'TikTok Shop' }],
    isOrderReceived: o => o.status === 'Hàng đã trả', isOrderPending: () => false, getOrderAlertLevel: () => '', getOrderPlatform: o => o.p,
    statsScope: 'all', currentPlatformFilter: 'Shopee', lastStatsMerge: null,
    orderMatchesTableScope: (o, ignorePlatform) => ignorePlatform || o.p === 'Shopee' };
  vm.createContext(c);
  vm.runInContext("const statsOrderKey = o => String(o.orderId || '').trim() + '|' + String(o.track || '').trim();", c);
  vm.runInContext(extract('collectReturnStatsItems'), c);
  assert.strictEqual(vm.runInContext('collectReturnStatsItems().length', c), 2, 'A (bảng chính) + C (lưu trữ Shopee); D là TikTok bị lọc; A trùng bỏ');
  assert.strictEqual(vm.runInContext('lastStatsMerge.dup', c), 1);
  assert.strictEqual(vm.runInContext('collectReturnStatsItems(true).length', c), 3, 'bảng Theo sàn bỏ qua tab sàn');
  assert.ok(html.includes('🏬 THEO SÀN'));
});
