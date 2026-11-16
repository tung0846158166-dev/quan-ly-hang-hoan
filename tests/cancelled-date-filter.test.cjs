const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const html = fs.readFileSync(require('path').join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');

test('bảng đơn hủy có ô lọc theo ngày và dùng trong applyCancelledFilters', () => {
  for (const id of ['cancelledDateField', 'cancelledDateFrom', 'cancelledDateTo']) assert.ok(html.includes('id="' + id + '"'), id);
  const fn = html.slice(html.indexOf('function applyCancelledFilters'), html.indexOf('function resetCancelledFilters'));
  assert.ok(fn.includes("cancelledDateFrom") && fn.includes("cancelledDateTo") && fn.includes('parseFullDate'));
  const reset = html.slice(html.indexOf('function resetCancelledFilters'), html.indexOf('function changeCancelledPage'));
  assert.ok(reset.includes('cancelledDateFrom') && reset.includes('cancelledDateTo'));
});
