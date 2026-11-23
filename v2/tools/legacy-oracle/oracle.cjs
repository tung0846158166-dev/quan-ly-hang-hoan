// "Đáp án chuẩn" từ MÃ CŨ: chạy chính các hàm nạp của app cũ (Bang_Excel...html) trong Node (vm) với DỮ LIỆU GIẢ,
// ghi lại kết quả để test C# so khớp (kiểm thử đối chiếu / golden master).
//
// Dùng:  node oracle.cjs tiktok <cases.json> <golden.json>
//   cases.json : [{ "name": "...", "mode": "tong_gia_tri", "rows": [ {cột: giá trị}, ... ], "existing": [ {đơn có sẵn}, ... ] }]
//   golden.json: [{ "name", "orders": [ {đơn sau khi nạp} ] }]
// CHỈ dùng dữ liệu giả. Không đọc/ghi dữ liệu thật. Không sửa file nguồn.
'use strict';
const fs = require('node:fs'), vm = require('node:vm'), path = require('node:path'), assert = require('node:assert/strict');

const SOURCE = process.env.PARCEL_TEST_SOURCE || path.join(__dirname, '..', '..', '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html');
const source = fs.readFileSync(SOURCE, 'utf8');

// Danh sách hàm cần nạp: giống tests/import-tiktok-189.test.cjs (đã chạy xanh trên mã cũ).
const FUNCTIONS = ['removeVietnameseTones', 'normalizeScientificOrderId', 'extractCleanTrackingCode', 'runImportUi', 'parseImportedQuantity',
  'getImportProductMode', 'dedupeOrderItems', 'mergeImportedItems', 'reconcileImportPrices', 'getOrderTrackingCodes', 'getPhoneOrderMatches',
  'parseImportedTrackingCodes', 'getReturnParcelId', 'getReturnParcelKey', 'isSameReturnParcel', 'findTracklessSameOrderIndex',
  'fillTracklessFromSalework', 'findExistingReturnParcelIndex', 'getShopeeDbParcels', 'registerImportedParcel', 'lookupShopeeDb',
  'applyShopeeDbMatchToOrder', 'parseMoneyCellValue', 'resolveOrderShipDate', 'extractDateFromCellValue', 'isKnownColorName',
  'isValidColorString', 'isValidSizeString', 'isReturnReasonText', 'parseSaleworkProductAndVariation', 'ingestTikTokOnlyRecords',
  'ingestShopeeRecords', 'pushAllDbOrdersToMainTable', 'exportToExcel',
  // Hàm THẬT (test cũ thay bằng hàm giả; ở đây cần hành vi thật của app): hãng vận chuyển theo mã, ghi chú khách, chuẩn hóa ngày.
  'detectCarrierByTrackingCode', 'extractBuyerNote', 'normalizeShortDate',
  // Kho lưu trữ / đã xóa: chống tạo lại và làm giàu đơn có sẵn.
  'orderIdKey', 'orderTrackingCodes', 'getDeletedOrders', 'buildOtherTablesIndex', 'findInOtherTables', 'enrichOtherTableEntry',
  // Chuẩn bị lô nạp: bỏ dòng trùng giữa các file, kế thừa mã cho dòng sản phẩm phụ, tách nhóm theo cấu trúc cột.
  'preparePlatformImportGroups',
  // Google Sheet: ghép tab -> dòng kiểu Excel Shopee.
  'parseFullDate', 'parseSheetItemsCell', 'keepLatestRequestPerOrder', 'sheetSkipTypes', 'sheetTabKind', 'sheetRowsToParcels',
  'filterSheetParcelsByDays', 'mergeSheetParcels', 'shopeeClaimDeadline', 'sheetParcelsToRows'];

function makeContext(mode, opts = {}) {
  const onlyReturns = opts.onlyReturns !== false;               // chkFilterOnlyReturns (mặc định bật)
  const skipUnshippedCancel = opts.skipUnshippedCancel === true; // chkTikTokSkipUnshippedCancel (mặc định tắt)
  const c = {
    console, activeImportBatch: null, captureImportState: () => ({}), commitImportState: async () => true, notifyImportResult: () => {},
    orders: [], shopeeOrderDb: {}, selectedPriceMode: mode, getTikTokPriceColumnMode: () => mode, requireUnlocked: async () => true,
    document: { getElementById: id => ({
      // Mặc định THẬT của app: "supplement" (Nhập bổ sung). Test cũ dùng "replace"; ca nào cần thì đặt options.productMode.
      value: id === 'importProductUpdateMode' ? (opts.productMode === 'replace' ? 'replace' : 'supplement') : undefined,
      checked: id === 'chkPushToMainOrders' || (id === 'chkFilterOnlyReturns' && onlyReturns) || (id === 'chkTikTokSkipUnshippedCancel' && skipUnshippedCancel),
    }) },
  };
  // Cố định "hôm nay" = 23/11/2026 để đáp án không đổi theo ngày chạy (mã cũ dùng ngày hiện tại làm ngày hoàn mặc định).
  const FIXED = Date.UTC(2026, 10, 23, 5, 0, 0);
  const RealDate = Date;
  c.Date = class extends RealDate {
    constructor(...a) { if (a.length === 0) super(FIXED); else super(...a); }
    static now() { return FIXED; }
  };
  c.archivedOrders = JSON.parse(JSON.stringify(opts.archived || []));
  c.cancelledOrders = [];
  c.deletedOrders = null;
  c.DELETED_ORDERS_KEY = 'shopee_deleted_orders_v1';
  c.localStorage = { getItem: k => (k === 'shopee_deleted_orders_v1' ? JSON.stringify(opts.deleted || []) : null), setItem: () => {} };
  c.setTimeout = () => 0;
  for (const n of ['saveShopeeOrderDb', 'renderShopeeDbTable', 'saveOrders', 'renderAll', 'setPlatformFilter', 'showAppToast', 'addAuditLog',
    'addAppNotification', 'closeShopeeDbModal', 'alert', 'saveCancelledOrders', 'saveArchivedOrders', 'updateDeletedOrdersBadge']) c[n] = () => {};
  vm.createContext(c);
  vm.runInContext(source.match(/    const COMMON_COLOR_WORDS = \[[^]*?    \];/)[0], c);
  vm.runInContext(source.match(/    const SHEET_SKIP_CANCEL_TYPES = \[[^\n]*\];/)[0], c);
  for (const n of FUNCTIONS) {
    const m = source.match(new RegExp('^    (?:async )?function ' + n + '\\([^]*?^    }', 'm'));
    assert(m, 'không tìm thấy hàm ' + n);
    vm.runInContext(m[0], c);
  }
  return c;
}

async function runTikTok(kase, shopee = false) {
  const c = makeContext(kase.mode || 'tong_gia_tri', kase.options || {});
  c.orders = JSON.parse(JSON.stringify(kase.existing || []));
  let error = null;
  try {
    const rows = JSON.parse(JSON.stringify(kase.rows));
    if (shopee) await c.ingestShopeeRecords(rows, 'Shopee'); else await c.ingestTikTokOnlyRecords(rows);
  }
  catch (e) { error = String(e && e.message || e); }
  const orders = JSON.parse(JSON.stringify(c.orders));
  const archived = JSON.parse(JSON.stringify(c.archivedOrders));
  return { name: kase.name, error, orders, archived };
}

// ---- Chế độ "match": so khớp "cùng một kiện" (isSameReturnParcel / findExistingReturnParcelIndex / findTracklessSameOrderIndex) ----
// Sinh N tình huống NGẪU NHIÊN (hạt giống cố định => tái lập), chạy hàm cũ và ghi cả tình huống lẫn kết quả vào MỘT tệp.
function mulberry32(seed) { return function () { seed |= 0; seed = (seed + 0x6D2B79F5) | 0; let t = Math.imul(seed ^ (seed >>> 15), 1 | seed); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }

function genMatchScenarios(n, seed) {
  const rnd = mulberry32(seed);
  const pick = list => list[Math.floor(rnd() * list.length)];
  const maybe = (p, v) => (rnd() < p ? v : '');
  const order = () => {
    const o = {
      platform: pick(['TikTok Shop', 'TikTok Shop', 'Shopee', '']),
      orderId: pick(['O1', 'O1', 'O2', '']),
      returnOrderId: pick(['C1', 'C2', '', '']),
      track: pick(['T1', 'T2', 'T3', '']),
      returnTrack: pick(['R1', 'R2', '', '']),
      forwardTrack: pick(['F1', 'F2', '', '']),
      altTrack: maybe(0.1, 'A1'),
    };
    if (rnd() < 0.35) o.trackingCodes = [...new Set([pick(['T1', 'R1', 'F1', 'X9', 'T2']), pick(['T1', 'R2', 'F2', 'X9'])])];
    return o;
  };
  const list = [];
  for (let i = 0; i < n; i++) {
    const orders = Array.from({ length: 1 + Math.floor(rnd() * 4) }, order);
    list.push({ orders, incoming: order() });
  }
  return list;
}

function runMatch(scenarios) {
  const c = makeContext('tong_gia_tri');
  return scenarios.map(s => {
    c.orders = JSON.parse(JSON.stringify(s.orders));
    c.findExistingReturnParcelIndex.index = null;
    const incoming = JSON.parse(JSON.stringify(s.incoming));
    return {
      same: c.orders.map(o => c.isSameReturnParcel(o, incoming)),
      existing: c.findExistingReturnParcelIndex(incoming),
      trackless: c.findTracklessSameOrderIndex(incoming),
    };
  });
}

(async () => {
  const [kind, a, b] = process.argv.slice(2);
  if (kind === 'prepare') {
    // node oracle.cjs prepare <cases.json> <golden.json>   cases: [{name, files:[{name, rows:[...]}]}]
    const cases = JSON.parse(fs.readFileSync(a, 'utf8'));
    const c = makeContext('tong_gia_tri');
    const out = cases.map(k => {
      const r = c.preparePlatformImportGroups(JSON.parse(JSON.stringify(k.files)));
      return { name: k.name, groups: JSON.parse(JSON.stringify(r.groups)), duplicateCount: r.duplicateCount };
    });
    fs.writeFileSync(b, JSON.stringify(out, null, 2), 'utf8');
    console.log(`Đã ghi ${out.length} ca chuẩn bị lô vào ${b}`);
    return;
  }
  if (kind === 'sheet') {
    // node oracle.cjs sheet <cases.json> <golden.json>   cases: [{name, tabs:[[row...]...], skipTypes?, maxDays?}]
    const cases = JSON.parse(fs.readFileSync(a, 'utf8'));
    const c = makeContext('tong_gia_tri');
    const out = cases.map(k => {
      const lists = []; const other = []; let removedTotal = 0, skippedCancel = 0;
      for (const tab of JSON.parse(JSON.stringify(k.tabs))) {
        if (!tab.length) continue;
        const kept = c.keepLatestRequestPerOrder(tab); removedTotal += kept.removed;
        const kind2 = c.sheetTabKind(kept.rows);
        if (kind2) { const pr = c.sheetRowsToParcels(kept.rows, kind2, c.sheetSkipTypes(typeof k.skipTypes === 'string' ? { skipTypes: k.skipTypes } : {})); skippedCancel += pr.skipped; lists.push(pr.parcels); }
        else other.push(...kept.rows);
      }
      let rows = [], tooOld = 0, parcels = [];
      if (lists.length) {
        const f = c.filterSheetParcelsByDays(c.mergeSheetParcels(lists), k.maxDays, Date.UTC(2026, 10, 23, 5, 0, 0));
        tooOld = f.dropped; parcels = f.parcels; rows = c.sheetParcelsToRows(f.parcels);
      }
      return { name: k.name, rows: JSON.parse(JSON.stringify(rows.concat(other))), removedTotal, skippedCancel, tooOld, parcelCount: parcels.length };
    });
    fs.writeFileSync(b, JSON.stringify(out, null, 2), 'utf8');
    console.log(`Đã ghi ${out.length} ca Google Sheet vào ${b}`);
    return;
  }
  if (kind === 'match') {
    // node oracle.cjs match <số tình huống> <tệp ra>
    const scenarios = genMatchScenarios(parseInt(a, 10) || 800, 20261123);
    const results = runMatch(scenarios);
    fs.writeFileSync(b, JSON.stringify(scenarios.map((s, i) => ({ ...s, expected: results[i] })), null, 1), 'utf8');
    console.log(`Đã ghi ${scenarios.length} tình huống so khớp vào ${b}`);
    return;
  }
  if ((kind !== 'tiktok' && kind !== 'shopee') || !a || !b) { console.error('Dùng: node oracle.cjs tiktok|shopee <cases.json> <golden.json>  |  node oracle.cjs match <n> <out.json>'); process.exit(2); }
  const cases = JSON.parse(fs.readFileSync(a, 'utf8'));
  const out = [];
  for (const k of cases) out.push(await runTikTok(k, kind === 'shopee'));
  fs.writeFileSync(b, JSON.stringify(out, null, 2), 'utf8');
  console.log(`Đã ghi ${out.length} ca vào ${b}`);
})();
