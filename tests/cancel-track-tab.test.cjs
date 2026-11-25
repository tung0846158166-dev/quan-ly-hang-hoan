const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs'), path = require('path'), vm = require('vm');
const html = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8').replace(/\r\n/g, '\n');
function extract(name) {
  const m = html.match(new RegExp('^    (?:async )?function ' + name + '\\([^]*?^    }', 'm'));
  assert.ok(m, name); return m[0];
}
const c = { Date, JSON, Math, String, Array, Object, console };
vm.createContext(c);
vm.runInContext("const SHEET_SKIP_CANCEL_TYPES = ['huy truoc chuan bi']; const SHEET_TRACK_TAB = 'Vận đơn huỷ';", c);
for (const n of ['removeVietnameseTones', 'parseFullDate', 'parseSheetItemsCell', 'sheetTabKind', 'sheetTrackMap', 'applySheetTrackMap', 'sheetRowsToParcels']) vm.runInContext(extract(n), c);

test('tab ẩn chỉ có Mã đơn + Mã vận đơn được nhận ra, tab Huỷ/Hàng hoàn vẫn như cũ', () => {
  assert.strictEqual(vm.runInContext("sheetTabKind([{'Mã đơn':'A','Mã vận đơn':'SPX1','Cập nhật':'x'}])", c), 'vandon');
  assert.strictEqual(vm.runInContext("sheetTabKind([{'Mã đơn':'A','Loại':'Bom','Ai huỷ':'x','Mã vận đơn':'S'}])", c), 'huy');
  assert.strictEqual(vm.runInContext("sheetTabKind([{'Hàng':'x','Mã đơn':'A','Trạng thái về':'y','Mã vận đơn':'S'}])", c), 'hanghoan');
});
test('mã vận đơn từ tab ẩn gắn vào đơn huỷ chưa có mã, không đè mã đã có', () => {
  const map = vm.runInContext("sheetTrackMap([{'Mã đơn':'a1','Mã vận đơn':'SPX1'},{'Mã đơn':'b2','Mã vận đơn':'SPX2'},{'Mã đơn':'c3','Mã vận đơn':''}])", c);
  assert.deepStrictEqual(JSON.parse(JSON.stringify(map)), { A1: 'SPX1', B2: 'SPX2' });
  const ps = [{ orderId: 'A1', track: '' }, { orderId: 'B2', track: 'OLD' }, { orderId: 'C3', track: '' }];
  c.ps = ps; c.map = map;
  assert.strictEqual(vm.runInContext('applySheetTrackMap(ps, map)', c), 1);
  assert.deepStrictEqual(ps.map(p => p.track), ['SPX1', 'OLD', '']);
});
test('tab Huỷ có cột Mã vận đơn thì đơn huỷ mang mã vận đơn', () => {
  c.rows = [{ 'Mã đơn': 'A1', 'Loại': 'Huỷ sau chuẩn bị', 'Ai huỷ': 'Người mua', 'Mã vận đơn': 'SPX9', 'Tên sản phẩm': 'x', 'SL': '1' }];
  const r = vm.runInContext("sheetRowsToParcels(rows, 'huy')", c);
  assert.strictEqual(r.parcels[0].track, 'SPX9');
});
