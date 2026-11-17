const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs'), path = require('path'), vm = require('vm');
const html = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
const extract = n => { const m = html.match(new RegExp('^    function ' + n + '\\([^]*?^    }', 'm')); assert.ok(m, n); return m[0]; };
function ctx() {
  const c = { String, Array, Set, Map, Object, JSON };
  vm.createContext(c);
  ['parseBatchSearchCodes', 'orderSearchCodes', 'batchSearchReport'].forEach(n => vm.runInContext(extract(n), c));
  return c;
}

test('Tìm nhiều mã: tách mã, bỏ chữ thừa, bỏ trùng; 1 từ khóa thường thì không phải tìm nhiều mã', () => {
  const c = ctx();
  assert.deepStrictEqual(Array.from(c.parseBatchSearchCodes('587135548819867135 spxvn062533901598, 2611111JGJDHQN (2 dòng sản phẩm) 587135548819867135')),
    ['587135548819867135', 'SPXVN062533901598', '2611111JGJDHQN']);
  assert.strictEqual(c.parseBatchSearchCodes('Áo polo len'), null);
  assert.strictEqual(c.parseBatchSearchCodes('SPXVN062533901598'), null);
  assert.strictEqual(c.parseBatchSearchCodes('SPXVN062533901598 áo'), null, 'chỉ 1 mã -> tìm thường');
});

test('Tìm nhiều mã: báo từng mã ở bảng nào hay không có', () => {
  const c = ctx();
  c.orders = [{ orderId: 'A0000001', track: 'SPX00000001', status: 'Chưa trả' }, { orderId: 'B0000002', trackingCodes: ['JT0000002'], status: 'Hàng đã trả' }];
  c.archivedOrders = [{ orderId: 'C0000003', track: 'SPX00000003', recvDate: '16/11/2026 10:00' }];
  c.cancelledOrders = [{ orderId: 'D0000004', received: true }];
  c.newOrdersInbox = [{ track: 'E0000005' }];
  const rep = c.batchSearchReport(['SPX00000001', 'JT0000002', 'C0000003', 'D0000004', 'E0000005', 'Z9999999']);
  assert.strictEqual(rep.get('SPX00000001')[0].label, 'main');
  assert.strictEqual(rep.get('SPX00000001')[0].text, 'dòng #1 · Chưa trả');
  assert.strictEqual(rep.get('JT0000002')[0].label, 'main');
  assert.strictEqual(rep.get('C0000003')[0].label, 'archive');
  assert.strictEqual(rep.get('D0000004')[0].text, 'đã nhận');
  assert.strictEqual(rep.get('E0000005')[0].label, 'inbox');
  assert.strictEqual(rep.get('Z9999999').length, 0);
});
