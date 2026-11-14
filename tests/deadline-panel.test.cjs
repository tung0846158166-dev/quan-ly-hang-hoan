const test = require('node:test'), assert = require('node:assert'), fs = require('fs'), vm = require('vm'), path = require('path');
const src = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
function grab(name) { const m = src.match(new RegExp('function ' + name + '\\([^)]*\\) \\{[\\s\\S]*?\\n    \\}\\n')); assert.ok(m, name); return m[0]; }
const code = 'const DEADLINE_DAYS = 3;\n' + ['formatDuration', 'computeDeadlineInfo', 'collectDeadlineItems'].map(grab).join('\n');
function ctx(orders, cancelled) {
  const c = { orders, cancelledOrders: cancelled, Date, getDeadlineStatus: o => o.st || 'none', parseFullDate: v => new Date(v) };
  vm.createContext(c); vm.runInContext(code, c); return c;
}
const ago = h => new Date(Date.now() - h * 3600000).toISOString();
test('quá hạn hiện số ngày giờ, sắp đến hạn hiện còn bao lâu, sắp xếp quá hạn trước', () => {
  const c = ctx([{ track: 'A', st: 'upcoming', returnDate: ago(60) }, { track: 'B', st: 'overdue', returnDate: ago(72 + 30) }], []);
  const items = c.collectDeadlineItems();
  assert.strictEqual(items[0].code, 'B');
  assert.match(items[0].info.text, /Quá hạn 1 ngày 6 giờ/);
  assert.match(items[1].info.text, /Sắp đến hạn: còn 1[12] giờ/);
});
test('đơn hủy chưa về gần hạn được liệt kê, đã về thì không', () => {
  const c = ctx([], [{ id: 'x', track: 'C', returnDate: ago(80) }, { id: 'y', track: 'D', returnDate: ago(80), received: true }, { id: 'z', track: 'E', returnDate: ago(10) }]);
  const items = c.collectDeadlineItems();
  assert.strictEqual(items.map(i => i.code).join(), "C");
  assert.strictEqual(items[0].kind, 'cancelled');
});
test('UI có tab Hạn Xử Lý và hook render', () => {
  assert.match(src, /notifCatBtn_deadline/);
  assert.match(src, /renderDeadlinePanel\(container, deadlineItems, kw\)/);
});
test('ô tìm kiếm chính có nút Xóa', () => {
  assert.match(src, /id="btnClearMainSearch" onclick="clearMainSearch\(\)"/);
  assert.match(src, /function clearMainSearch\(\) \{[\s\S]*?value = ''[\s\S]*?renderAll\(\)/);
});
