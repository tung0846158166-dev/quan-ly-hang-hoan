const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const test = require('node:test');
const path = require('node:path');
// Parsed worksheet rows only: this does not exercise an XLSX engine or workbook serialization.
const source = fs.readFileSync(process.env.EXCEL_TEST_SOURCE || path.join(__dirname, '..', 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
function context(mode = 'tong_gia_tri', onlyReturns = false) {
  const c = {console, orders: [], shopeeOrderDb: {}, requireUnlocked: async()=>true,
    document: {getElementById: id=>({checked: id === 'chkFilterOnlyReturns' && onlyReturns})}, getTikTokPriceColumnMode: ()=>mode,
    detectCarrierByTrackingCode: ()=>'', extractBuyerNote: ()=>'', saveShopeeOrderDb(){}, renderShopeeDbTable(){}, autoEnrichAllMissingOrdersFromDb(){}, addAuditLog(){}, alert(){}};
  vm.createContext(c);
  vm.runInContext(source.match(/    const COMMON_COLOR_WORDS = \[[^]*?    \];/)[0], c);
  for (const name of ['removeVietnameseTones','normalizeScientificOrderId','extractCleanTrackingCode','parseMoneyCellValue','isKnownColorName','isValidColorString','isValidSizeString','isReturnReasonText','parseSaleworkProductAndVariation','getOrderTrackingCodes','parseImportedTrackingCodes','getReturnParcelId','getReturnParcelKey','isSameReturnParcel','getShopeeDbParcels','registerImportedParcel','ingestTikTokOnlyRecords']) {
    const match = source.match(new RegExp('^    (?:async )?function '+name+'\\([^]*?^    }', 'm'));
    assert(match, name); vm.runInContext(match[0], c);
  }
  return c;
}
const base = {'Order ID':'700000000000000001','Tracking ID':'TTVN1234567890','Product Name':'Fixture Shirt','SKU Name':'White, XL',Quantity:2,'Order Status':'Returned'};
async function ingest(rows, mode, onlyReturns) {const c=context(mode,onlyReturns); await c.ingestTikTokOnlyRecords(rows); return c.shopeeOrderDb[base['Order ID']];}
test('blank platform status imports',async()=>{const e=await ingest([{...base,'Order Status':''}]); assert(e); assert.equal(e.platformStatus,'');});
test('SKU subtotals aggregate independently of quantity and unit price',async()=>{for(const mode of ['tong_gia_tri','sau_chiet_khau','gia_salework']) {const e=await ingest([{...base,'SKU Subtotal After Discount':100000,'SKU Subtotal Before Discount':120000,'SKU Unit Original Price':60000},{...base,'Product Name':'Second','SKU Subtotal After Discount':200000,'SKU Subtotal Before Discount':220000,'SKU Unit Original Price':110000}],mode); assert.equal(e.items.length,2); assert.equal(e.priceModes.sau_chiet_khau,300000); assert.equal(e.priceModes.gia_salework,340000); assert.equal(e.price,mode==='gia_salework'?340000:300000);}});
test('repeated order totals retained once; unit prices sum by quantity',async()=>{for(const key of ['Order Amount','Order Total']) {const e=await ingest([{...base,[key]:500000},{...base,[key]:500000}]);assert.equal(e.price,500000);} const e=await ingest([{...base,'SKU Unit Original Price':10000},{...base,'SKU Unit Original Price':20000}]);assert.equal(e.price,60000);});
test('explicit zero subtotal remains selectable; empty cells use fallback',async()=>{const e=await ingest([{...base,'Order Amount':500000,'SKU Subtotal After Discount':0},{...base,'Order Amount':500000,'SKU Subtotal After Discount':''}], 'sau_chiet_khau');assert.equal(e.price,0); const fallback=await ingest([{...base,'Order Amount':500000,'SKU Subtotal After Discount':''}],'sau_chiet_khau');assert.equal(fallback.price,500000);});
test('size only variation and actual single colors use parser',async()=>{let e=await ingest([{...base,'SKU Name':'XL'}]); assert.equal(e.items[0].size,'XL');assert.equal(e.items[0].color,''); for(const color of ['Black','White','Xanh than']){e=await ingest([{...base,'SKU Name':color}]);assert.equal(e.items[0].color,color);assert.equal(e.items[0].size,'');}});
test('canonical cancellation spellings pass default filter',async()=>{for(const status of ['Cancelled','Canceled','Cancel']){const e=await ingest([{...base,'Order Status':status}],undefined,true);assert(e,status);assert.equal(e.orderType,'Đơn hủy');}assert.equal(await ingest([{...base,'Order Status':'Đang vận chuyển'}],undefined,true),undefined);});
test('price modes keep order revenue once and support continuation rows', async()=>{
  const rows = [{...base,'Order Amount':500000,'SKU Subtotal After Discount':100000,'Giá Salework':30000,'Doanh thu ước tính':450000},
    {...base,'Order ID':'','Tracking ID':'','Product Name':'Second SKU','SKU Subtotal After Discount':200000,'Giá Salework':40000,'Doanh thu ước tính':450000,'Order Amount':500000}];
  for (const [mode, expected] of [['tong_gia_tri',300000],['sau_chiet_khau',300000],['gia_salework',140000],['doanh_thu_uoc_tinh',450000]]) {
    const e = await ingest(rows,mode); assert.equal(e.price,expected);assert.equal(e.items.length,2); assert.equal(e.priceModes.hasOrderLevelAM,false);
  }
});
