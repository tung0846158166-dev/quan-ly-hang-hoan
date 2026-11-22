// v1.9.73: gộp mặt hàng bị phình do mỗi nguồn ghi tên/màu/size khác nhau.
const test = require('node:test'), assert = require('node:assert'), fs = require('fs'), path = require('path'), vm = require('vm');
const html = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8').replace(/\r\n/g, '\n');
const m = html.match(/^    function dedupeOrderItems\([^]*?^    }/m); assert.ok(m);
const c = { String, Array, Number, Object, Math }; vm.createContext(c);
c.removeVietnameseTones = s => String(s || '').normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D');
vm.runInContext(m[0], c);
const L = 'Áo Polo Len Thỏ Form Rộng Hàn Quốc Unisex Lavi Studio/ LEMONN';
test('đơn 261107NKBGPAV4: 6 dòng → 2 mặt hàng thật', () => {
  const items = [
    { product: L, color: 'Xanh Nâu', size: 'M', qty: 1 }, { product: L, color: 'Kem Nâu', size: 'L', qty: 1 },
    { product: L, color: '', size: '', qty: 1 }, { product: L, color: '', size: '', qty: 1 },
    { product: 'LEMONN', color: 'Kem Nâu', size: 'L', qty: 1 }, { product: 'LEMONN', color: 'Xanh Nâu', size: 'M', qty: 1 }];
  const r = c.dedupeOrderItems(items);
  assert.strictEqual(r.length, 2);
  assert.strictEqual(r.map(x => x.color + '/' + x.size + '/' + x.qty).join('|'), 'Xanh Nâu/M/1|Kem Nâu/L/1');
  assert.strictEqual(r[0].product, L);
});
test('sản phẩm khác nhau hoặc màu/size khác nhau không bị gộp', () => {
  const items = [{ product: 'Áo A', color: 'Đen', size: 'M', qty: 1 }, { product: 'Quần B', color: 'Đen', size: 'M', qty: 1 }, { product: 'Áo A', color: 'Đen', size: 'L', qty: 2 }];
  assert.strictEqual(c.dedupeOrderItems(items), items);
});
test('trùng màu/size cùng tên: lấy số lượng lớn nhất, không cộng dồn', () => {
  const r = c.dedupeOrderItems([{ product: 'Áo A', color: 'Đen', size: 'M', qty: 2 }, { product: 'Áo A', color: 'Đen', size: 'M', qty: 2 }]);
  assert.strictEqual(r.length, 1); assert.strictEqual(r[0].qty, 2);
});
test('các dòng trống không có dòng biến thể thì giữ nguyên; SKU khác nhau không gộp', () => {
  const a = [{ product: 'Áo A', qty: 1 }, { product: 'Áo A', qty: 1 }];
  assert.strictEqual(c.dedupeOrderItems(a), a);
  const b = [{ product: 'X', sku: 'S1', qty: 1 }, { product: 'X', sku: 'S2', qty: 1 }];
  assert.strictEqual(c.dedupeOrderItems(b), b);
});
