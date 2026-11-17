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
    cancelledOrders: [], isOrderReceived: o => o.status === 'Hàng đã trả', getOrderPlatform: () => 'Shopee' };
  vm.createContext(c);
  vm.runInContext('function parseFullDate(v){const m=String(v||"").match(/^(\\d{1,2})\\/(\\d{1,2})\\/(\\d{4})/);return m?new Date(+m[3],+m[2]-1,+m[1]):null}', c);
  for (const n of ['statsBucketKey', 'computeStats', 'collectReceivedStatsItems']) vm.runInContext(extract(n), c);
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
