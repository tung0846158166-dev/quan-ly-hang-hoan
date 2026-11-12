// The import path now uses indexes instead of scanning every order/database entry per row.
// These tests run the indexed code and the original brute-force code on the same random data and
// require identical results, so the speed-up cannot change which parcels are matched.
const fs = require('node:fs'), vm = require('node:vm'), assert = require('node:assert/strict'), test = require('node:test'), path = require('node:path');
const current = fs.readFileSync(process.env.PARCEL_TEST_SOURCE || path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
const original = fs.readFileSync(path.join(__dirname, 'fixtures', 'import-reference-functions.txt'), 'utf8');
const NAMES = ['extractCleanTrackingCode', 'getOrderTrackingCodes', 'getReturnParcelId', 'getReturnParcelKey', 'isSameReturnParcel', 'findExistingReturnParcelIndex',
  'getShopeeDbParcels', 'registerImportedParcel', 'lookupShopeeDb', 'mergeImportedItems', 'reconcileImportPrices', 'getImportProductMode'];
function build(source) {
  const c = vm.createContext({ console, activeImportBatch: { messages: [] }, orders: [], shopeeOrderDb: {}, document: { getElementById: () => ({ value: 'supplement' }) } });
  for (const name of NAMES) {
    const m = source.match(new RegExp('^    (?:async )?function ' + name + '\\([^]*?^    }', 'm'));
    assert(m, name);
    vm.runInContext(m[0], c);
  }
  return c;
}
let seed = 12345;
const rnd = n => { seed = (seed * 1103515245 + 12345) & 0x7fffffff; return seed % n; };
const pick = list => list[rnd(list.length)];
const CODES = ['SPXVN0001234567', 'spxvn0001234567', 'https://spx.vn/track?spx=SPXVN0001234567', 'TTVN123456789', 'SOC11223344', '8626123456789', 'GHN12345678', 'HAN-DDA/SPXVN0009998887', '  ttvn123456789  ', ''];
function randomParcel(i) {
  const track = pick(CODES);
  const o = { orderId: pick(['', 'A1', 'a1', 'A2', 'B3', 'C' + (i % 7)]), returnOrderId: pick(['', '', 'R1', 'r1', 'R2', 'R' + (i % 5)]),
    track: pick([track, '']), returnTrack: pick(['', '', pick(CODES)]), forwardTrack: pick(['', '', pick(CODES)]),
    items: [{ product: 'P' + rnd(4), qty: 1 + rnd(3) }], price: rnd(5) * 1000, priceHasValue: pick([true, false, undefined]) };
  if (rnd(3) === 0) o.trackingCodes = [pick(CODES), pick(CODES)].filter(Boolean);
  return o;
}
const clone = v => JSON.parse(JSON.stringify(v));
const snapshotDb = c => JSON.stringify(Object.keys(c.shopeeOrderDb).sort().map(k => [k, c.shopeeOrderDb[k]]));

test('indexed import helpers behave exactly like the original brute-force ones', () => {
  for (let round = 0; round < 300; round++) {
    const a = build(current), b = build(original);
    const count = 30 + rnd(40);
    for (let i = 0; i < count; i++) {
      const parcel = randomParcel(i);
      a.registerImportedParcel(clone(parcel)); b.registerImportedParcel(clone(parcel));
      if (i % 4 === 0) {
        const probe = pick(CODES.concat(['A1', 'R1', 'B3', 'C2', 'nothing']));
        assert.equal(JSON.stringify(a.lookupShopeeDb(probe)), JSON.stringify(b.lookupShopeeDb(probe)), 'lookup ' + probe);
      }
    }
    assert.equal(snapshotDb(a), snapshotDb(b), 'database content after registering');
    for (const code of CODES.concat(['A1', 'A2', 'B3', 'R1', 'R2', 'C3', 'zzz'])) {
      assert.equal(JSON.stringify(a.lookupShopeeDb(code)), JSON.stringify(b.lookupShopeeDb(code)), 'final lookup ' + code);
    }
    // Orders: build a table, then look up incoming rows while appending / enriching like the importer does.
    for (let i = 0; i < 25; i++) { const o = randomParcel(i); a.orders.push(clone(o)); b.orders.push(clone(o)); }
    a.findExistingReturnParcelIndex.index = null;
    for (let i = 0; i < 40; i++) {
      const incoming = randomParcel(100 + i);
      const x = a.findExistingReturnParcelIndex(clone(incoming)), y = b.findExistingReturnParcelIndex(clone(incoming));
      assert.equal(x, y, 'existing parcel index');
      if (x === -1) { const o = randomParcel(200 + i); a.orders.push(clone(o)); b.orders.push(clone(o)); }
      else {
        const extra = pick(CODES);
        for (const c of [a, b]) { c.orders[x].trackingCodes = (c.orders[x].trackingCodes || []).concat(extra); if (!c.orders[x].returnTrack) c.orders[x].returnTrack = extra; }
        if (typeof a.findExistingReturnParcelIndex.touch === 'function') a.findExistingReturnParcelIndex.touch(a.orders[x], x);
      }
    }
  }
});

test('importing into a large table stays fast', () => {
  const c = build(current);
  const N = 4000;
  for (let i = 0; i < N; i++) {
    const o = { orderId: 'OR' + i, returnOrderId: 'CL' + i, returnTrack: 'TTVN' + (100000 + i), track: 'TTVN' + (100000 + i), items: [{ product: 'x', qty: 1 }] };
    c.orders.push(o);
    c.registerImportedParcel(clone(o));
  }
  const t = Date.now();
  for (let i = 0; i < 200; i++) {
    const row = { orderId: 'NEW' + i, returnOrderId: 'NCL' + i, returnTrack: 'TTVN' + (900000 + i), track: 'TTVN' + (900000 + i), items: [{ product: 'x', qty: 1 }] };
    c.registerImportedParcel(row);
    assert.equal(c.findExistingReturnParcelIndex(row), -1);
    c.orders.push(row);
    assert.notEqual(c.lookupShopeeDb('TTVN' + (100000 + i)), null);
  }
  const ms = Date.now() - t;
  assert(ms < 1500, 'took ' + ms + ' ms');
});
