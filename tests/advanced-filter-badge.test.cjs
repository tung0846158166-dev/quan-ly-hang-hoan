const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const html = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
function extract(name) {
  const m = html.match(new RegExp('^    function ' + name + '\\([\\s\\S]*?^    }', 'm'));
  assert.ok(m, 'không tìm thấy hàm ' + name);
  return m[0];
}

function makeCtx(values, flags) {
  const els = {};
  Object.entries(values).forEach(([id, value]) => { els[id] = { value, checked: false, classList: { toggle() {} }, textContent: '' }; });
  els.chkOnlyWithDate = { checked: !!flags.onlyDate, value: '' };
  els.advancedFilterBadge = { textContent: '', classList: { hidden: null, toggle(c, on) { this.hidden = on; } } };
  const ctx = vm.createContext({
    currentOrderTypeFilter: flags.type || 'all',
    currentPreOrderFilter: flags.pre || 'all',
    currentTrackingFilter: flags.track || 'all',
    document: { getElementById: id => els[id] || null },
    els,
  });
  vm.runInContext(extract('countActiveAdvancedFilters') + '\n' + extract('updateAdvancedFilterBadge'), ctx);
  return ctx;
}

test('không có bộ lọc nâng cao nào bật thì đếm 0 và ẩn huy hiệu', () => {
  const ctx = makeCtx({ filterDateFrom: '', filterDateTo: '', filterTimeFrom: '', filterTimeTo: '', recvDateFilterSelect: 'all' }, {});
  assert.strictEqual(ctx.countActiveAdvancedFilters(), 0);
  ctx.updateAdvancedFilterBadge();
  assert.strictEqual(ctx.els.advancedFilterBadge.classList.hidden, true);
});

test('đếm đúng từng nhóm bộ lọc đang bật', () => {
  const ctx = makeCtx(
    { filterDateFrom: '2026-11-01', filterDateTo: '', filterTimeFrom: '08:00', filterTimeTo: '', recvDateFilterSelect: 'today' },
    { type: 'Đơn hủy', track: 'no_track', onlyDate: true }
  );
  // loại đơn + MVĐ + ngày + giờ + mốc nhanh + "Có ngày" = 6
  assert.strictEqual(ctx.countActiveAdvancedFilters(), 6);
  ctx.updateAdvancedFilterBadge();
  assert.strictEqual(ctx.els.advancedFilterBadge.textContent, '6');
  assert.strictEqual(ctx.els.advancedFilterBadge.classList.hidden, false);
});

test('chân bảng không còn các span tổng lặp và dock có K.cần nhận', () => {
  assert.ok(!/id="footDisplayOrders"/.test(html));
  assert.ok(/id="dockUnneededBadge"/.test(html));
  assert.ok(/id="advancedFilterPanel"/.test(html));
});

test('mở app mặc định chỉ hiện danh sách đơn, bảng thống kê mở bằng nút dock', () => {
  assert.ok(/let currentMainView = 'orders';/.test(html));
  assert.ok(/window\.switchMainView = switchMainView;\s*\n[^\n]*\n\s*switchMainView\('orders'\);/.test(html));
  assert.ok(/onclick="switchMainView\('summary'\); scrollToSummaryTable\(\);"/.test(html));
  assert.ok(/onclick="switchMainView\('orders'\); scrollToMainTable\(\);"/.test(html));
});
