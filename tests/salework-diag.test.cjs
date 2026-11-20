// Chẩn đoán Salework: báo cáo không được chứa giá trị dữ liệu, mã đơn, token.
const test = require('node:test');
const assert = require('node:assert');
const { sanitizeUrl, maskSegment, collectKeyPaths, maxArrayLen, SaleworkDiag } = require('../salework_diag.js');

test('che mã định danh trong đường dẫn, chỉ giữ tên tham số', () => {
  const s = sanitizeUrl('https://api.example.com/v1/orders/26102054X4PVRC/items/123456789?token=SECRET123&page=2&q=Nguyen');
  assert.strictEqual(s.host, 'api.example.com');
  assert.strictEqual(s.path, '/v1/orders/{id}/items/{id}');
  assert.deepStrictEqual(s.query, ['page', 'q', 'token']);
  assert.ok(!JSON.stringify(s).includes('SECRET123') && !JSON.stringify(s).includes('Nguyen'));
});

test('che mã trong phần # và uuid; từ chối địa chỉ không phải http', () => {
  assert.strictEqual(sanitizeUrl('https://a.vn/app/#/orders/2611060KRRWN0U0?x=1').path, '/app/#/orders/{id}');
  assert.strictEqual(sanitizeUrl('https://a.vn/x/3f2504e0-4f89-11d3-9a0c-0305e82c3301').path, '/x/{id}');
  assert.strictEqual(sanitizeUrl('file:///etc/passwd'), null);
  assert.strictEqual(sanitizeUrl('javascript:alert(1)'), null);
  assert.strictEqual(maskSegment('orders'), 'orders');
});

test('JSON: chỉ ra tên trường, không có giá trị', () => {
  const j = { code: 0, data: { items: [{ order_id: '26102054X4PVRC', buyer: { name: 'Nguyen Van A', phone: '0912345678' }, tracking: 'SPXVN06654167505B' }, { x: 1 }], total: 2 }, '26102054X4PVRC': 1 };
  const keys = [...collectKeyPaths(j, new Set())];
  assert.ok(keys.includes('data.items[].order_id'));
  assert.ok(keys.includes('data.items[].buyer.name'));
  const text = keys.join(' ');
  for (const secret of ['Nguyen Van A', '0912345678', 'SPXVN06654167505B', '26102054X4PVRC']) assert.ok(!text.includes(secret), secret);
  assert.ok(keys.includes('{id}'), 'khoá giống mã đơn bị che');
  assert.strictEqual(maxArrayLen(j), 2);
});

test('báo cáo: không chứa dữ liệu, gắn nhãn yêu cầu không phải đọc thuần', async () => {
  const d = new SaleworkDiag({});
  d.startedAt = 1;
  await d._onCdp('Network.requestWillBeSent', { requestId: 'a', type: 'XHR', request: { url: 'https://api.s.vn/order/list?access_token=ABCDEF&page=1', method: 'POST' } });
  await d._onCdp('Network.requestWillBeSent', { requestId: 'b', type: 'Image', request: { url: 'https://cdn.s.vn/a.png', method: 'GET' } });
  await d._onCdp('Network.responseReceived', { requestId: 'a', response: { status: 200, mimeType: 'application/json' } });
  d.win = { webContents: { debugger: { sendCommand: async () => ({ base64Encoded: false, body: JSON.stringify({ data: [{ tracking_no: 'SPXVN06654167505B', buyer: 'Tran Thi B' }] }) }) } }, isDestroyed: () => false };
  await d._onCdp('Network.loadingFinished', { requestId: 'a', encodedDataLength: 100 });
  d.pages.set('app.s.vn/orders', new Set(['Mã vận đơn', 'Trạng thái']));
  const r = d.report();
  assert.ok(r.includes('[GHI?] POST api.s.vn/order/list'));
  assert.ok(r.includes('data[].tracking_no') && r.includes('Mã vận đơn | Trạng thái'));
  assert.ok(!r.includes('cdn.s.vn'), 'ảnh/tệp tĩnh bị bỏ qua');
  for (const secret of ['ABCDEF', 'SPXVN06654167505B', 'Tran Thi B']) assert.ok(!r.includes(secret), secret);
  assert.ok(r.includes('access_token'), 'chỉ có TÊN tham số');
});

test('yêu cầu theo dõi vận đơn / đăng nhập không bị gắn nhãn "nền"', async () => {
  const d = new SaleworkDiag({});
  for (const [id, u] of [['1', 'https://a.vn/order/tracking/list'], ['2', 'https://a.vn/auth/login'], ['3', 'https://a.vn/chat/notification/count']])
    await d._onCdp('Network.requestWillBeSent', { requestId: id, type: 'XHR', request: { url: u, method: 'GET' } });
  const r = d.report();
  assert.ok(!/\[nền\] GET a\.vn\/order\/tracking/.test(r) && !/\[nền\] GET a\.vn\/auth\/login/.test(r));
  assert.ok(/\[nền\] GET a\.vn\/chat\/notification\/count/.test(r));
});
