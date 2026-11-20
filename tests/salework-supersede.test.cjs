const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),test=require('node:test'),path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'..','Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'),'utf8');
function ctx(orders){
  const c={console,orders,isLocked:false,localStorage:{getItem:()=>null,setItem(){}},saveOrders(){},addAuditLog(){},shiftCellLocksAfterOrderDelete(){},shiftCellEditHistoryAfterOrderDelete(){},hasLockedCellsInOrder:i=>!!c.locked&&c.locked.has(i)};
  vm.createContext(c);
  for(const n of ['removeVietnameseTones','extractCleanTrackingCode','isScanPlaceholder','isOrderReceived','mergeWarehouseIntoKeeper','supersedeStaleRowsFromSalework']){
    const m=source.match(new RegExp('^    (?:async )?function '+n+'\\([^]*?^    }','m'));assert(m,n);vm.runInContext(m[0],c);}
  return c;
}
const row=(o)=>Object.assign({orderId:'A1',platform:'TikTok Shop',status:'Chưa về',items:[{product:'x'}]},o);
test('dòng chờ cũ chưa có mã vận đơn được thay bằng dòng Salework, giữ dữ liệu kho',()=>{
  const c=ctx([row({platformStatus:'Chờ khách gửi hàng',myNote:'gọi khách',photo:'p.jpg'}),row({returnTrack:'854170000001',track:'862700000001'}),row({orderId:'B2'})]);
  const r=c.supersedeStaleRowsFromSalework(new Set(['A1']));
  assert.equal(r.merged,1);assert.equal(c.orders.length,2);
  const k=c.orders.find(o=>o.returnTrack);assert.equal(k.myNote,'gọi khách');assert.equal(k.photo,'p.jpg');
});
test('đã nhận ở dòng cũ -> dòng mới thừa hưởng trạng thái đã nhận',()=>{
  const c=ctx([row({status:'Hàng đã trả',recvDate:'20/11/2026'}),row({returnTrack:'854170000001'})]);
  c.supersedeStaleRowsFromSalework(new Set(['A1']));
  assert.equal(c.orders.length,1);assert.equal(c.orders[0].status,'Hàng đã trả');assert.equal(c.orders[0].recvDate,'20/11/2026');
});
test('nhiều kiện khác nhau: không đoán, giữ nguyên',()=>{
  const c=ctx([row({}),row({returnTrack:'854170000001'}),row({returnTrack:'854170000002'})]);
  const r=c.supersedeStaleRowsFromSalework(new Set(['A1']));
  assert.equal(r.merged,0);assert.equal(r.ambiguous,1);assert.equal(c.orders.length,3);
});
test('chỉ đụng mã đơn có trong file Salework; dòng khoá thì giữ',()=>{
  const c=ctx([row({}),row({returnTrack:'854170000001'}),row({orderId:'Z9'}),row({orderId:'Z9',returnTrack:'854170000009'})]);
  c.locked=new Set([0]);
  assert.equal(c.supersedeStaleRowsFromSalework(new Set(['A1'])).merged,0);
  c.locked=null;
  assert.equal(c.supersedeStaleRowsFromSalework(new Set(['A1'])).merged,1);
  assert.equal(c.orders.filter(o=>o.orderId==='Z9').length,2);
});
