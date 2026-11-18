// Gộp dòng trùng mã đơn ở Bảng chính (v1.9.37). Các ca lấy từ dữ liệu thật của người dùng (file 12.json, 18/11/2026).
const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs'), path = require('path'), vm = require('vm');
const html = fs.readFileSync(path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
const extract = n => { const m = html.match(new RegExp('^    function ' + n + '\\([^]*?^    }', 'm')); assert.ok(m, n); return m[0]; };
function run(orders, claims) {
  const c = { String, Array, Set, Map, Object, JSON, RegExp, Math, Date, console };
  vm.createContext(c);
  ['removeVietnameseTones', 'extractCleanTrackingCode', 'getReturnParcelId', 'isOrderReceived', 'findStaleClaimDuplicates', 'mergeWarehouseIntoKeeper'].forEach(n => vm.runInContext(extract(n), c));
  const iso = html.match(/^    const isClosedRequest = [^]*?^    };/m)[0];
  vm.runInContext('const dupPlanOf = new Map();\n' + iso, c);
  c.orders = orders; c.getSheetLatestClaims = () => claims || {};
  const idx = vm.runInContext('findStaleClaimDuplicates()', c);
  const plan = idx.map(i => ({ i, ...vm.runInContext('dupPlanOf', c).get(orders[i]) }));
  // áp dụng giống app: chuyển dữ liệu rồi gỡ
  plan.slice().reverse().forEach(p => { c.mergeWarehouseIntoKeeper(orders[p.i], p.keep, p); orders.splice(p.i, 1); });
  return { orders, plan };
}
const S = o => Object.assign({ platform: 'Shopee', status: 'Chưa trả', items: [] }, o);
const T = o => Object.assign({ platform: 'TikTok Shop', status: 'Chưa về', items: [] }, o);

test('Shopee: dòng mới có mã vận đơn giữ, dòng Sheet chưa có MVĐ gỡ và chuyển trạng thái sàn mới nhất', () => {
  const r = run([S({ orderId: '2611137066DHNW', track: 'SPXVN06506626611B', platformStatus: 'Đang về', rawDetails: { claimId: '2611170FXJ0WGUQ' } }),
                 S({ orderId: '2611137066DHNW', track: '', platformStatus: 'Chờ bạn xác nhận hàng trả về' })],
    { '2611137066DHNW': { c: '2611170FXJ0WGUQ', s: 'Đang về', t: 'SPXVN06506626611B' } });
  assert.strictEqual(r.orders.length, 1);
  assert.strictEqual(r.orders[0].track, 'SPXVN06506626611B');
});

test('Shopee đơn huỷ: dòng nạp từ file (có mã vận đơn, không mã yêu cầu) giữ, dòng Sheet gỡ, nhận trạng thái sàn của Sheet', () => {
  const r = run([S({ orderId: '261109RYQX93M3', track: '01M6ZHRV754AD0DQNEGV3TXGS8', platformStatus: 'Đã hủy' }),
                 S({ orderId: '261109RYQX93M3', track: '', status: 'Không cần nhận', platformStatus: 'Huỷ sau chuẩn bị · Đã huỷ', rawDetails: { claimId: '261109RYQX93M3' } })],
    { '261109RYQX93M3': { c: '261109RYQX93M3', s: 'Huỷ sau chuẩn bị · Đã huỷ', t: '' } });
  assert.strictEqual(r.orders.length, 1);
  assert.strictEqual(r.orders[0].track, '01M6ZHRV754AD0DQNEGV3TXGS8');
  assert.strictEqual(r.orders[0].platformStatus, 'Huỷ sau chuẩn bị · Đã huỷ');
  assert.strictEqual(r.orders[0].status, 'Chưa trả', 'không tự chuyển "Không cần nhận" của dòng gỡ');
});

test('Shopee: yêu cầu cũ khách đã huỷ (có MVĐ) gỡ, giữ yêu cầu mới của Sheet', () => {
  const r = run([S({ orderId: '261030230BX60A', track: 'SPXVN06441784210B', status: 'Không cần nhận', platformStatus: 'Yêu cầu của Người mua đã hủy', rawDetails: { claimId: '26110209J0FGM6S' } }),
                 S({ orderId: '261030230BX60A', track: '', platformStatus: 'Đã đồng ý', rawDetails: { claimId: '2611030AS2BMEA2' } })],
    { '261030230BX60A': { c: '2611030AS2BMEA2', s: 'Đã đồng ý', t: '' } });
  assert.deepStrictEqual(r.orders.map(o => o.platformStatus), ['Đã đồng ý']);
});

test('Shopee: cùng yêu cầu, mã vận đơn cũ chưa nhận gỡ; dòng đã nhận luôn được giữ', () => {
  const r = run([S({ orderId: 'X1', track: 'SPXVN06205983519B', platformStatus: 'Người mua đang trả hàng', rawDetails: { claimId: 'C1' } }),
                 S({ orderId: 'X1', track: 'SPXVN06522613242B', status: 'Hàng đã trả', platformStatus: 'Đã về shop', rawDetails: { claimId: 'C1' } })],
    { X1: { c: 'C1', s: 'Đang về', t: 'SPXVN06522613242B' } });
  assert.deepStrictEqual(r.orders.map(o => o.track), ['SPXVN06522613242B']);
  // yêu cầu cũ đã nhận hàng thì giữ
  const r2 = run([S({ orderId: 'X2', track: 'SPXOLD000001', status: 'Hàng đã trả', recvDate: '15/11/2026 10:00:00', rawDetails: { claimId: 'OLD' } }),
                  S({ orderId: 'X2', track: '', platformStatus: 'Đã đồng ý', rawDetails: { claimId: 'NEW' } })],
    { X2: { c: 'NEW', s: 'Đã đồng ý', t: '' } });
  assert.strictEqual(r2.orders.length, 2);
});

test('Gỡ dòng có dữ liệu kho thì chuyển sang dòng giữ (đã nhận, ngày nhận, ghi chú)', () => {
  const r = run([S({ orderId: 'R2', track: 'SPXR2000001', platformStatus: 'Đã về shop' }),
                 S({ orderId: 'R2', track: '', status: 'Hàng đã trả', recvDate: '17/11/2026 10:00:00', myNote: 'kệ A3', platformStatus: 'Đang xử lý' })],
    { R2: { c: '', s: 'Đã về shop', t: 'SPXR2000001' } });
  assert.strictEqual(r.orders.length, 1);
  assert.strictEqual(r.orders[0].status, 'Hàng đã trả');
  assert.strictEqual(r.orders[0].myNote, 'kệ A3');
});

test('TikTok: dòng y hệt gỡ; yêu cầu khách đã huỷ gỡ; chung một kiện thì gộp sản phẩm; 2 kiện khác nhau giữ cả 2', () => {
  let r = run([T({ orderId: '0714082259941', track: '0714082259941', platformStatus: 'Đang chuyển hoàn' }), T({ orderId: '0714082259941', track: '0714082259941', platformStatus: 'Đang chuyển hoàn' })]);
  assert.strictEqual(r.orders.length, 1);
  r = run([T({ orderId: '586912621220365952', track: '', platformStatus: 'Khách đã hủy yêu cầu', rawDetails: { claimId: '4042996284198717056' } }),
           T({ orderId: '586912621220365952', track: '854171753040', platformStatus: 'Đang vận chuyển về', rawDetails: { claimId: '4043107779352102528' } })]);
  assert.deepStrictEqual(r.orders.map(o => o.track), ['854171753040']);
  r = run([T({ orderId: '586973124969858960', track: '854169510889', rawDetails: { claimId: 'A' }, items: [{ product: 'Áo', color: 'Đen', size: 'L', qty: 1 }] }),
           T({ orderId: '586973124969858960', track: '854169510889', rawDetails: { claimId: 'B' }, items: [{ product: 'Quần', color: 'Xám', size: 'M', qty: 1 }] })]);
  assert.strictEqual(r.orders.length, 1);
  assert.deepStrictEqual(r.orders[0].items.map(i => i.product), ['Áo', 'Quần']);
  r = run([T({ orderId: '587069719787111618', track: '854171233916', platformStatus: 'Đang vận chuyển về', rawDetails: { claimId: 'A' } }),
           T({ orderId: '587069719787111618', track: '854171825802', platformStatus: 'Đang vận chuyển về', rawDetails: { claimId: 'B' } })]);
  assert.strictEqual(r.orders.length, 2);
});

test('Đơn tạm "Chưa rõ sản phẩm" có mã là MÃ YÊU CẦU trả hàng của đơn thật: gộp vào đơn thật, ghi Hàng đã trả + mã vận đơn vừa quét', () => {
  const c = { String, Array, Set, Map, Object, JSON, RegExp, Math, Date, WeakMap, console };
  vm.createContext(c);
  ['removeVietnameseTones', 'extractCleanTrackingCode', 'getReturnParcelId', 'isOrderReceived', 'getOrderTrackingCodes', 'isScanPlaceholder', 'findPlaceholderTargets', 'mergePlaceholderIntoOrder'].forEach(n => vm.runInContext(extract(n), c));
  const real = { orderId: '26102054X4PVRC', track: 'SPXVN06229297911A', trackingCodes: ['SPXVN06229297911A'], status: 'Chưa trả', platformStatus: 'Đã đồng ý', rawDetails: { claimId: '2611060KRRWN0U0' }, items: [{ product: 'Áo Polo', qty: 1 }] };
  const ph = { orderId: '2611060KRRWN0U0', track: 'SPXVN06654167505B', status: 'Hàng đã trả', recvDate: '18/11/2026 14:13:00', note: 'Hàng đã về (điện thoại quét lúc 18/11/2026 14:13:00) nhưng chưa có đơn trong file sàn', items: [{ product: 'Chưa rõ sản phẩm (hàng đã về, chưa có đơn)', qty: 1 }] };
  const lonely = { orderId: 'SPXVN06769320671B', track: 'SPXVN06769320671B', status: 'Hàng đã trả', items: [{ product: 'Chưa rõ sản phẩm (hàng đã về, chưa có đơn)', qty: 1 }] };
  c.orders = [ph, real, lonely];
  const plan = vm.runInContext('findPlaceholderTargets()', c);
  assert.strictEqual(plan.size, 1);
  assert.strictEqual(plan.get(ph), real);
  c.mergePlaceholderIntoOrder(ph, real);
  assert.strictEqual(real.status, 'Hàng đã trả');
  assert.strictEqual(real.recvDate, '18/11/2026 14:13:00');
  assert.strictEqual(real.returnTrack, 'SPXVN06654167505B');
  assert.ok(real.trackingCodes.includes('SPXVN06654167505B'));
});

test('Hộp Đơn Mới: ô mã đơn là MÃ TRẢ HÀNG của đơn có sẵn -> đẩy ra thì ghi vào đơn đó, không tạo dòng mới', () => {
  const c = { String, Array, Set, Map, Object, JSON, RegExp, Math, Date, Number, Boolean, console };
  vm.createContext(c);
  ['extractCleanTrackingCode', 'getReturnParcelId', 'isOrderReceived', 'getOrderTrackingCodes', 'getPhoneOrderMatches', 'isScanPlaceholder', 'mergePlaceholderIntoOrder', 'convertInboxItemToOrder', 'mergeInboxItemIntoMain']
    .forEach(n => vm.runInContext(extract(n), c));
  c.getNowDateTimeString = () => '18/11/2026 14:13:00'; c.formatScanTime = () => '18/11/2026 14:13:00'; c.autoDetectPlatformFromCode = () => 'Shopee';
  c.orders = [S({ orderId: '26102054X4PVRC', track: 'SPXVN06229297911A', rawDetails: { claimId: '2611060KRRWN0U0' } })];
  const real = c.mergeInboxItemIntoMain({ track: 'SPXVN06654167505B', orderId: '2611060KRRWN0U0', fromUnmatchedScan: true, scanTime: 1, time: '18/11/2026 14:13:00', product: 'Chưa rõ sản phẩm (hàng đã về, chưa có đơn)' });
  assert.ok(real);
  assert.strictEqual(c.orders.length, 1);
  assert.strictEqual(real.status, 'Hàng đã trả');
  assert.strictEqual(real.returnTrack, 'SPXVN06654167505B');
  // không khớp đơn nào -> không ghi gì
  assert.strictEqual(c.mergeInboxItemIntoMain({ track: 'SPXVN0999999999X', orderId: 'SPXVN0999999999X' }), null);
});
