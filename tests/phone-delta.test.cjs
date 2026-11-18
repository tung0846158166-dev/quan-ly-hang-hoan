// v1.9.40: gửi sang iPhone phần thay đổi thay vì cả danh sách.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs'), path = require('path'), vm = require('vm');
const html = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
const extract = n => { const m = html.match(new RegExp('^    function ' + n + '\\([^]*?^    }', 'm')); assert.ok(m, n); return m[0]; };
function ctx() {
  const c = { Map, Set, JSON, Object, Array, String };
  vm.createContext(c);
  ['phoneRowKeys', 'buildPhoneDelta'].forEach(n => vm.runInContext(extract(n), c));
  vm.runInContext('var phoneBase = null; function setBase(rows){ const k = phoneRowKeys(rows); const m = new Map(); rows.forEach((r,i)=>m.set(k[i], JSON.stringify(r))); phoneBase = { sid: "snap_1", map: m }; }', c);
  return c;
}
const R = (c, o, extra = {}) => Object.assign({ k: 'm', c, o, st: 'Chưa trả' }, extra);

test('khóa dòng: dòng trùng được đánh số như iPhone', () => {
  const c = ctx();
  assert.deepStrictEqual([...c.phoneRowKeys([R('T1', 'O1'), R('T1', 'O1'), R('T2', 'O2', { ci: 'x' })])], ['m|T1|O1|', 'm|T1|O1|#2', 'm|T2|O2|x']);
});

test('phần thay đổi: chỉ dòng đổi/thêm và khóa dòng bỏ', () => {
  const c = ctx();
  assert.strictEqual(c.buildPhoneDelta([R('T1', 'O1')], [], {}), null, 'chưa có bản gốc thì không gửi phần thay đổi');
  c.setBase([R('T1', 'O1'), R('T2', 'O2'), R('T3', 'O3')]);
  const d = c.buildPhoneDelta([R('T1', 'O1'), R('T2', 'O2', { st: 'Hàng đã trả', rv: 1 }), R('T4', 'O4')], [], { a: 1 });
  assert.strictEqual(d.base, 'snap_1');
  assert.deepStrictEqual([...d.up.map(u => u._k)], ['m|T2|O2|', 'm|T4|O4|']);
  assert.deepStrictEqual([...d.rm], ['m|T3|O3|']);
  // không có gì đổi -> rỗng
  const e = c.buildPhoneDelta([R('T1', 'O1'), R('T2', 'O2'), R('T3', 'O3')], [], {});
  assert.strictEqual(e.up.length + e.rm.length, 0);
});
