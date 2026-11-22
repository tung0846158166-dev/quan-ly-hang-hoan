// v1.9.77: các tab tuần gộp thành một danh sách chọn
const test = require('node:test'), assert = require('node:assert'), fs = require('fs'), path = require('path'), vm = require('vm');
const html = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8').replace(/\r\n/g, '\n');
function fn(name) { const m = html.match(new RegExp('^    function ' + name + '\\([^)]*\\) \\{[\\s\\S]*?^    \\}', 'm')); assert.ok(m, name); return m[0]; }
function run(orders, current, custom) {
  const box = { innerHTML: '' };
  const c = vm.createContext({ orders, currentImportWeekFilter: current, savedCustomWeeks: custom || [],
    document: { getElementById: id => id === 'dynamicWeekTabs' ? box : null },
    getOrderImportWeek: o => o.importWeek, getWeekInfo: () => ({ label: 'x' }),
    getWeekInfoFromKey: k => ({ label: 'Tuần ' + k }), escapeAttr: s => String(s).replace(/"/g, '&quot;').replace(/</g, '&lt;') });
  vm.runInContext(fn('renderWeeklyImportTabs'), c);
  c.renderWeeklyImportTabs();
  return box.innerHTML;
}
test('tab tuần là một ô chọn, có "Tất cả" và từng tuần, chọn đúng tuần hiện tại', () => {
  const orders = [{ importWeek: '2026-W47', qty: 2 }, { importWeek: '2026-W47', qty: 1 }, { importWeek: '2026-W48', qty: 1 }];
  const out = run(orders, '2026-W47');
  assert.equal((out.match(/<select/g) || []).length, 1);
  assert.equal((out.match(/<option/g) || []).length, 3);
  assert.ok(/<option value="2026-W47" selected>[^<]*2 đơn/.test(out));
  assert.ok(!/<option value="all" selected/.test(out));
  assert.ok(!/<button/.test(out), 'tuần có đơn thì không có nút xóa');
  assert.ok(/onchange="setImportWeekFilter\(this\.value\)"/.test(out));
});
test('chọn Tất cả, và tuần trống tự tạo có nút xóa', () => {
  assert.ok(/<option value="all" selected>[^<]*3 đơn/.test(run([{ importWeek: 'a' }, { importWeek: 'a' }, { importWeek: 'b' }], 'all')));
  const out = run([{ importWeek: '2026-W47' }], '2026-W49', ['2026-W49']);
  assert.ok(/<option value="2026-W49" selected>/.test(out));
  assert.ok(/removeCustomWeekTab\('2026-W49'\)/.test(out));
});
