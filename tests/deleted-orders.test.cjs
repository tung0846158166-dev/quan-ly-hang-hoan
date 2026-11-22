const test=require('node:test'),fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const html=fs.readFileSync('Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html','utf8').replace(/\r\n/g,'\n');
function fn(n){const m=html.match(new RegExp('^    (?:async )?function '+n+'\\([^]*?^    }','m'));assert(m,n);return m[0]}
function mk(){const store={};const c=vm.createContext({console,archivedOrders:[],cancelledOrders:[],extractCleanTrackingCode:s=>String(s||'').trim().toUpperCase(),
  localStorage:{getItem:k=>store[k]??null,setItem:(k,v)=>{store[k]=String(v)}},store});
  const consts="function orderIdKey(o) { return String((o && o.orderId) || '').trim().toUpperCase(); }const DELETED_ORDERS_KEY='shopee_deleted_orders_v1';const DELETED_ORDERS_CAP=5000;let deletedOrders=null;";
  vm.runInContext(consts+['orderTrackingCodes','getDeletedOrders','saveDeletedOrders','addToDeletedOrders','buildOtherTablesIndex','findInOtherTables'].map(fn).join('\n'),c);return c}
test('đơn đã xóa được lưu (bỏ ảnh) và nhận diện là deleted khi nạp lại',()=>{
  const c=mk();c.addToDeletedOrders({orderId:'A1',track:'SPX1',photo:'BIGDATA',photoId:'p1',items:[{product:'Áo'}]},'main');
  const saved=JSON.parse(c.store.shopee_deleted_orders_v1);assert.equal(saved.length,1);assert.equal(saved[0].orderId,'A1');assert.equal(saved[0].photo,undefined);assert.equal(saved[0].deletedFrom,'main');
  const idx=c.buildOtherTablesIndex(true);
  assert.equal(c.findInOtherTables(idx,{orderId:'A1',track:'SPX1'}),'deleted');
  assert.equal(c.findInOtherTables(idx,{orderId:'ZZ',track:'SPXZ'}),null);
});
test('không dùng danh sách đã xóa cho các chỗ không yêu cầu (dọn trùng, quét điện thoại)',()=>{
  const c=mk();c.addToDeletedOrders({orderId:'A1',track:'SPX1'},'main');
  assert.equal(c.findInOtherTables(c.buildOtherTablesIndex(),{orderId:'A1',track:'SPX1'}),null);
});
test('kho lưu trữ có trước kho đã xóa; trần 5000; bỏ qua đơn không có mã',()=>{
  const c=mk();c.archivedOrders.push({orderId:'A1',track:'SPX1'});c.addToDeletedOrders({orderId:'A1',track:'SPX1'},'main');
  assert.equal(c.findInOtherTables(c.buildOtherTablesIndex(true),{orderId:'A1',track:'SPX1'}),'archive');
  c.addToDeletedOrders({items:[]},'main');assert.equal(c.getDeletedOrders().length,1);
  for(let i=0;i<5100;i++) c.getDeletedOrders().push({orderId:'X'+i});c.addToDeletedOrders({orderId:'N',track:'T'},'main');assert.equal(c.getDeletedOrders().length,5000);assert.equal(c.getDeletedOrders()[0].orderId,'N');
});
test('các đường xóa đều ghi nhớ; import bỏ qua đơn đã xóa',()=>{
  assert.equal((html.match(/addToDeletedOrders\(/g)||[]).length>=5,true);
  assert.equal((html.match(/buildOtherTablesIndex\(true\)/g)||[]).length,2);
  assert.equal((html.match(/=== 'deleted'\) \{ skippedDeleted\+\+; return; \}/g)||[]).length,2);
});
