const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),test=require('node:test'),path=require('node:path');
const source=fs.readFileSync(process.env.PARCEL_TEST_SOURCE||path.join(__dirname,'..','Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'),'utf8');
function context(){const c={console,activeImportBatch:null,captureImportState:()=>({}),commitImportState:async()=>true,notifyImportResult:()=>{},orders:[],shopeeOrderDb:{},selectedPriceMode:'tong_gia_tri',getTikTokPriceColumnMode:()=> 'tong_gia_tri',requireUnlocked:async()=>true,document:{getElementById:id=>({value:id==='importProductUpdateMode'?'replace':undefined,checked:id==='chkPushToMainOrders'})},detectCarrierByTrackingCode:()=>'',extractBuyerNote:()=>'',normalizeShortDate:x=>x};for(const n of ['saveShopeeOrderDb','renderShopeeDbTable','saveOrders','renderAll','setPlatformFilter','showAppToast','addAuditLog','addAppNotification','closeShopeeDbModal','alert'])c[n]=()=>{};vm.createContext(c);vm.runInContext(source.match(/    const COMMON_COLOR_WORDS = \[[^]*?    \];/)[0],c);for(const n of ['removeVietnameseTones','normalizeScientificOrderId','extractCleanTrackingCode','runImportUi','parseImportedQuantity','getImportProductMode','mergeImportedItems','reconcileImportPrices','getOrderTrackingCodes','getPhoneOrderMatches','parseImportedTrackingCodes','getReturnParcelId','getReturnParcelKey','isSameReturnParcel','findExistingReturnParcelIndex','getShopeeDbParcels','registerImportedParcel','lookupShopeeDb','applyShopeeDbMatchToOrder','parseMoneyCellValue','resolveOrderShipDate','extractDateFromCellValue','isKnownColorName','isValidColorString','isValidSizeString','isReturnReasonText','parseSaleworkProductAndVariation','ingestTikTokOnlyRecords','ingestShopeeRecords','pushAllDbOrdersToMainTable','exportToExcel']){const m=source.match(new RegExp('^    (?:async )?function '+n+'\\([^]*?^    }','m'));assert(m,n);vm.runInContext(m[0],c);}return c;}



const base={'Order ID':'ORDER001','Return Order ID':'CLAIM001','Return Logistics Tracking ID':'RETURN001','Product Name':'Shirt','Return Status':'In Process',Quantity:5};
async function run(rows,mode='tong_gia_tri') {const c=context();c.getTikTokPriceColumnMode=()=>mode;c.document={getElementById:id=>({value:id==='importProductUpdateMode'?'replace':undefined,checked:id==='chkPushToMainOrders'||id==='chkFilterOnlyReturns'})};await c.ingestTikTokOnlyRecords(rows);return c;}
test('TikTok return quantity wins, zero skips product, blank falls back to ordered quantity',async()=>{
 const c=await run([{...base,'Sku Quantity of return':1,'SKU Unit Original Price':10000},{...base,'Product Name':'Not returned','Sku Quantity of return':0,'SKU Unit Original Price':20000}]);
 assert.equal(c.orders[0].items.length,1);assert.equal(c.orders[0].items[0].qty,1);assert.equal(c.orders[0].price,10000);assert.equal(c.orders[0].isPartialReturn,true);
 const blank=await run([{...base,'Sku Quantity of return':'','SKU Unit Original Price':10000}]);assert.equal(blank.orders[0].items[0].qty,5);assert.equal(blank.orders[0].price,50000);
 const zero=await run([{...base,'Sku Quantity of return':0}]);assert.equal(zero.orders.length,0);
});
test('TikTok Return Quantity alias overrides Quantity and marks partial',async()=>{const c=await run([{...base,'Return Quantity':2,'Return unit price':10000}]);assert.equal(c.orders[0].items[0].qty,2);assert.equal(c.orders[0].isPartialReturn,true);assert.equal(c.orders[0].price,20000);});
test('TikTok return unit prices sum across returned SKU quantities and preserve other modes',async()=>{
 for(const [mode,expected] of [['tong_gia_tri',50000],['sau_chiet_khau',45000],['gia_salework',60000],['doanh_thu_uoc_tinh',42000]]) {
 const c=await run([{...base,Quantity:2,'Return unit price':10000,'SKU Subtotal After Discount':18000,'SKU Subtotal Before Discount':25000,'Doanh thu ước tính':42000},{...base,Quantity:1,'Product Name':'Pants','Return unit price':30000,'SKU Subtotal After Discount':27000,'SKU Subtotal Before Discount':35000,'Doanh thu ước tính':42000}],mode);
 assert.equal(c.orders[0].price,expected,mode);assert.equal(c.orders[0].priceModes.tong_gia_tri,50000);assert.equal(c.orders[0].isPartialReturn,false);
 }
});
test('TikTok onlyReturns checks populated return signals and complete status words',async()=>{
 const ordinary={'Order ID':'ORDER001','Tracking ID':'FORWARD001','Product Name':'Shirt',Quantity:1,'Return Order ID':''};
 for(const status of ['Delivered','Trạng thái đang giao hàng','In transit']) {const c=await run([{...ordinary,'Order Status':status}]);assert.equal(c.orders.length,0,status);}
 for(const status of ['Refunded','Return shipped','Đã trả hàng','Đang hoàn hàng']) {const c=await run([{...ordinary,'Order Status':status}]);assert.equal(c.orders.length,1,status);}
 const explicit=await run([{...ordinary,'Return Order ID':'CLAIM001','Order Status':'In Process'}]);assert.equal(explicit.orders.length,1);
});
test('TikTok continuation preserves SKU rows but explicit different claim starts a new parcel',async()=>{const c=await run([base,{'Product Name':'Second SKU',Quantity:2},{'Return Order ID':'CLAIM002','Product Name':'Another claim',Quantity:1}]);assert.equal(c.orders.length,2);assert.equal(c.orders[0].items.length,2);assert.equal(c.orders[1].returnOrderId,'CLAIM002');});
test('TikTok keeps explicit forward tracking with return tracking',async()=>{const c=await run([{...base,'Tracking ID':'FORWARD001'}]);assert(c.getOrderTrackingCodes(c.orders[0]).includes('FORWARD001'));assert.equal(c.orders[0].track,'RETURN001');assert.equal(c.orders[0].forwardTrack,'FORWARD001');assert.equal(c.orders[0].returnTrack,'RETURN001');});

test('TikTok source file boundary prevents orphan SKU attaching to prior file',async()=>{const c=await run([{...base,__sourceFileId:'file1'},{'Product Name':'Orphan SKU',Quantity:2,__sourceFileId:'file2'}]);assert.equal(c.orders.length,1);assert.equal(c.orders[0].items.length,1);});
test('TikTok preserves stable seller and platform SKU identities',async()=>{const c=await run([{...base,'Seller SKU':'SELLER001','SKU ID':'SKU001'}]);assert.equal(c.orders[0].items[0].sellerSku,'SELLER001');assert.equal(c.orders[0].items[0].skuId,'SKU001');assert.equal(c.orders[0].forwardTrack,'');});
test('TikTok stores forward identity even before return tracking is supplied',async()=>{const c=await run([{...base,'Return Logistics Tracking ID':'','Tracking ID':'FORWARD001'}]);assert.equal(c.orders[0].forwardTrack,'FORWARD001');assert.equal(c.orders[0].returnTrack,'');assert.equal(c.orders[0].track,'FORWARD001');});
test('Salework return file keeps the "Mã trả hàng" return tracking code',async()=>{
 const row={'Mã đơn hàng':'587139504996779037','Trạng thái':'THẤT THU','Mã sản phẩm':'DELAX_Den_M','Số lượng':1,'Giá Salework':700000,'Tên sản phẩm trên sàn':'Áo Polo Len - Đen, M - Premium Cao Cấp','Tổng COD':390150,'Mã vận đơn':'862799512623','Đơn vị vận chuyển':'J&T Express','Mã trả hàng':'854170248243'};
 const c=await run([row,{...row,'Mã đơn hàng':'587139219296323354','Mã vận đơn':'862762232763','Mã trả hàng':''}]);
 assert.equal(c.orders.length,2);
 assert.equal(c.orders[0].returnTrack,'854170248243');assert.equal(c.orders[0].track,'862799512623');
 assert.ok(c.orders[0].trackingCodes.includes('854170248243'));
 assert.equal(c.orders[1].returnTrack,'');
});
